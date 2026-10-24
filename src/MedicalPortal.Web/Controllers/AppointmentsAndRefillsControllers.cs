using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/appointments")]
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class AppointmentsController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly ISnapshotService _snapshotService;
    private readonly IAuditService _audit;
    private readonly IPortalNotificationService _notifications;

    public AppointmentsController(
        MedicalPortalDbContext db,
        ISnapshotService snapshotService,
        IAuditService audit,
        IPortalNotificationService notifications)
    {
        _db = db;
        _snapshotService = snapshotService;
        _audit = audit;
        _notifications = notifications;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    [HttpGet]
    [RequirePatientAccess(Permission.manageAppointments, AppResource.Appointments, AppAction.Read)]
    public async Task<IActionResult> GetAppointments()
    {
        var appointments = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .Include(a => a.AppointmentType)
            .Include(a => a.RoomResource)
            .Where(a => a.PatientId == TargetPatientId)
            .OrderBy(a => a.StartAt)
            .ToListAsync();

        return Ok(new
        {
            appointments = appointments.Select(a => new
            {
                id = a.Id,
                status = a.Status.ToString(),
                startAt = a.StartAt.ToString("o"),
                endAt = a.EndAt.ToString("o"),
                reasonForVisit = a.ReasonForVisit,
                appointmentType = a.AppointmentType != null ? new
                {
                    id = a.AppointmentType.Id,
                    name = a.AppointmentType.Name,
                    code = a.AppointmentType.Code,
                    durationMinutes = a.AppointmentType.DurationMinutes
                } : null,
                room = a.RoomResource != null ? new
                {
                    id = a.RoomResource.Id,
                    name = a.RoomResource.Name,
                    code = a.RoomResource.Code
                } : null,
                provider = new
                {
                    name = $"Dr. {a.Provider.FirstName} {a.Provider.LastName}",
                    credentials = a.Provider.Credentials
                },
                location = a.Location != null ? new
                {
                    name = a.Location.Name,
                    city = a.Location.City,
                    state = a.Location.State,
                    timeZone = a.Location.TimeZoneId
                } : null
            })
        });
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetAppointmentTypes()
    {
        var types = await _db.AppointmentTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DurationMinutes)
            .Select(t => new
            {
                id = t.Id,
                name = t.Name,
                code = t.Code,
                durationMinutes = t.DurationMinutes,
                description = t.Description
            })
            .ToListAsync();

        return Ok(new { appointmentTypes = types });
    }

    [HttpGet("rooms")]
    public async Task<IActionResult> GetRooms([FromQuery] Guid? locationId = null)
    {
        var query = _db.RoomResources.AsNoTracking().Where(r => r.IsActive);
        if (locationId.HasValue)
        {
            query = query.Where(r => r.LocationId == locationId.Value);
        }

        var rooms = await query
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                id = r.Id,
                locationId = r.LocationId,
                name = r.Name,
                code = r.Code
            })
            .ToListAsync();

        return Ok(new { rooms });
    }

    public record RequestAppointmentBody(
        string? ProviderId,
        string? LocationId,
        string StartAt,
        string? EndAt = null,
        string? ReasonForVisit = null,
        string? PatientNotes = null,
        string? AppointmentTypeId = null,
        string? RoomResourceId = null);

    [HttpPost]
    [RequirePatientAccess(Permission.manageAppointments, AppResource.Appointments, AppAction.Write)]
    public async Task<IActionResult> RequestAppointment([FromBody] RequestAppointmentBody body)
    {
        if (!DateTime.TryParse(body.StartAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var startAt))
            return BadRequest(new { error = "Invalid start or end time format." });

        startAt = DateTime.SpecifyKind(startAt, DateTimeKind.Utc);

        Guid? appointmentTypeId = Guid.TryParse(body.AppointmentTypeId, out var parsedAtId) ? parsedAtId : null;
        AppointmentType? apptType = null;
        if (appointmentTypeId.HasValue)
        {
            apptType = await _db.AppointmentTypes.FindAsync(appointmentTypeId.Value);
            if (apptType == null || !apptType.IsActive)
                return BadRequest(new { error = "Invalid or inactive appointment type." });
        }

        DateTime endAt;
        if (!string.IsNullOrEmpty(body.EndAt) && DateTime.TryParse(body.EndAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedEndAt))
        {
            endAt = DateTime.SpecifyKind(parsedEndAt, DateTimeKind.Utc);
        }
        else if (apptType != null)
        {
            endAt = startAt.AddMinutes(apptType.DurationMinutes);
        }
        else
        {
            return BadRequest(new { error = "Invalid start or end time format." });
        }

        if (!SchedulingLogic.WithinBookingWindow(startAt))
            return BadRequest(new { error = "Appointments can only be requested up to 180 days ahead." });

        Guid providerId;
        if (!string.IsNullOrEmpty(body.ProviderId) && Guid.TryParse(body.ProviderId, out var parsedProviderId))
        {
            providerId = parsedProviderId;
        }
        else
        {
            // Default to actor's own provider record if clinician
            var ownProvider = await _db.Providers.FirstOrDefaultAsync(p => p.AccountId == Actor.UserAccountId);
            if (ownProvider == null)
                return BadRequest(new { error = "Choose a provider for this appointment." });
            providerId = ownProvider.Id;
        }

        // Verify patient is on care team
        var onTeam = await _db.CareTeams.AnyAsync(ct => ct.PatientId == TargetPatientId && ct.ProviderId == providerId);
        if (!onTeam)
            return StatusCode(403, new { error = "You cannot request an appointment with this provider." });

        Guid? locationId = Guid.TryParse(body.LocationId, out var locId) ? locId : (Guid?)null;
        Guid? departmentId = null;
        if (locationId.HasValue)
        {
            var loc = await _db.Locations.FindAsync(locationId.Value);
            departmentId = loc?.DepartmentId;
        }

        Guid? roomResourceId = Guid.TryParse(body.RoomResourceId, out var parsedRoomId) ? parsedRoomId : null;
        RoomResource? room = null;
        if (roomResourceId.HasValue)
        {
            room = await _db.RoomResources.FindAsync(roomResourceId.Value);
            if (room == null || !room.IsActive)
                return BadRequest(new { error = "Requested room is invalid or inactive." });
            if (locationId.HasValue && room.LocationId != locationId.Value)
                return BadRequest(new { error = "Room does not belong to the selected location." });
        }

        // Begin transaction with Serializable isolation to strictly prevent double bookings under concurrency
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        // 1. Conflict check for provider
        var busyAppointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.ProviderId == providerId &&
                        a.Status != AppointmentStatus.CANCELLED_BY_PATIENT &&
                        a.Status != AppointmentStatus.CANCELLED_BY_CLINIC &&
                        startAt < a.EndAt && endAt > a.StartAt)
            .Select(a => new SchedulingLogic.TimeSlot(a.StartAt, a.EndAt))
            .ToListAsync();

        // 2. Conflict check for room
        List<SchedulingLogic.TimeSlot>? roomBusySlots = null;
        if (roomResourceId.HasValue)
        {
            roomBusySlots = await _db.Appointments
                .AsNoTracking()
                .Where(a => a.RoomResourceId == roomResourceId.Value &&
                            a.Status != AppointmentStatus.CANCELLED_BY_PATIENT &&
                            a.Status != AppointmentStatus.CANCELLED_BY_CLINIC &&
                            startAt < a.EndAt && endAt > a.StartAt)
                .Select(a => new SchedulingLogic.TimeSlot(a.StartAt, a.EndAt))
                .ToListAsync();
        }

        // 3. Conflict check for blocked time / holidays
        var blockedSlots = await _db.ScheduleBlocks
            .AsNoTracking()
            .Where(b => ((b.ProviderId == providerId) || (locationId.HasValue && b.LocationId == locationId.Value)) &&
                        startAt < b.EndAt && endAt > b.StartAt)
            .Select(b => new SchedulingLogic.TimeSlot(b.StartAt, b.EndAt))
            .ToListAsync();

        var check = SchedulingLogic.CheckSlot(
            new SchedulingLogic.TimeSlot(startAt, endAt),
            busyAppointments,
            roomBusySlots,
            blockedSlots);

        if (!check.Ok)
        {
            await tx.RollbackAsync();
            return Conflict(new { error = SchedulingLogic.DescribeSlotProblem(check.Problem ?? "conflicts_with_existing") });
        }

        var appointment = new Appointment
        {
            PatientId = TargetPatientId,
            ProviderId = providerId,
            LocationId = locationId,
            DepartmentId = departmentId,
            AppointmentTypeId = appointmentTypeId,
            RoomResourceId = roomResourceId,
            StartAt = startAt,
            EndAt = endAt,
            Status = AppointmentStatus.REQUESTED,
            ReasonForVisit = body.ReasonForVisit,
            PatientNotes = body.PatientNotes,
            RequestedByUserAccountId = Actor.UserAccountId
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        using (var activity = MedicalPortalDiagnostics.ActivitySource.StartActivity("portal.appointment.book"))
        {
            activity?.SetTag("appointment.id", appointment.Id.ToString());
            activity?.SetTag("provider.id", providerId.ToString());
            MedicalPortalDiagnostics.AppointmentsBookedTotal.Add(1);
        }

        await _audit.LogAsync("APPOINTMENT_REQUESTED", "Appointment", appointment.Id.ToString(), Actor.UserAccountId, TargetPatientId);
        await _snapshotService.RefreshSnapshotAsync(TargetPatientId);
        await _notifications.NotifyAppointmentChangedAsync(TargetPatientId, providerId, "REQUESTED", new { appointmentId = appointment.Id, startAt = appointment.StartAt, status = appointment.Status.ToString() });

        var provider = await _db.Providers.FindAsync(providerId);

        return StatusCode(201, new
        {
            appointment = new
            {
                id = appointment.Id,
                status = appointment.Status.ToString(),
                startAt = appointment.StartAt.ToString("o"),
                endAt = appointment.EndAt.ToString("o"),
                reasonForVisit = appointment.ReasonForVisit,
                appointmentType = apptType != null ? new
                {
                    id = apptType.Id,
                    name = apptType.Name,
                    code = apptType.Code,
                    durationMinutes = apptType.DurationMinutes
                } : null,
                room = room != null ? new
                {
                    id = room.Id,
                    name = room.Name,
                    code = room.Code
                } : null,
                provider = new
                {
                    firstName = provider?.FirstName,
                    lastName = provider?.LastName,
                    credentials = provider?.Credentials
                }
            },
            message = "Request submitted. The clinic will confirm your appointment."
        });
    }

    [HttpPost("{id}/cancel")]
    [RequirePatientAccess(Permission.manageAppointments, AppResource.Appointments, AppAction.Write)]
    public async Task<IActionResult> CancelAppointment(Guid id)
    {
        var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.PatientId == TargetPatientId);
        if (appt == null)
            return NotFound(new { error = "Appointment not found." });

        if (appt.Status is AppointmentStatus.CANCELLED_BY_PATIENT or AppointmentStatus.CANCELLED_BY_CLINIC)
            return Conflict(new { error = "This appointment is already cancelled." });

        if (appt.Status == AppointmentStatus.COMPLETED)
            return Conflict(new { error = "Completed appointments cannot be cancelled." });

        appt.Status = AppointmentStatus.CANCELLED_BY_PATIENT;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("APPOINTMENT_CANCELLED", "Appointment", appt.Id.ToString(), Actor.UserAccountId, TargetPatientId);
        await _snapshotService.RefreshSnapshotAsync(TargetPatientId);
        await _notifications.NotifyAppointmentChangedAsync(TargetPatientId, appt.ProviderId, "CANCELLED", new { appointmentId = appt.Id, status = appt.Status.ToString() });

        return Ok(new { ok = true, message = "Your appointment has been cancelled." });
    }
}

[ApiController]
[Route("api/refills")]
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class RefillsController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly ISnapshotService _snapshotService;
    private readonly IAuditService _audit;
    private readonly IPortalNotificationService _notifications;

    public RefillsController(
        MedicalPortalDbContext db,
        ISnapshotService snapshotService,
        IAuditService audit,
        IPortalNotificationService notifications)
    {
        _db = db;
        _snapshotService = snapshotService;
        _audit = audit;
        _notifications = notifications;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    [HttpGet]
    [RequirePatientAccess(Permission.viewRecords, AppResource.Medications, AppAction.Read)]
    public async Task<IActionResult> GetRefills()
    {
        var rows = await _db.RefillRequests
            .AsNoTracking()
            .Include(r => r.Medication)
            .Include(r => r.Prescription)
            .Where(r => r.Prescription.PatientId == TargetPatientId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync();

        return Ok(new
        {
            refills = rows.Select(r => new
            {
                id = r.Id,
                medication = r.Medication.Name,
                strength = r.Medication.Strength,
                dosage = r.Prescription.Dosage,
                status = r.Status.ToString(),
                pharmacyName = r.PharmacyName,
                requestedAt = r.RequestedAt.ToString("o"),
                resolvedAt = r.ResolvedAt?.ToString("o"),
                clinicianResponse = r.ClinicianResponse
            })
        });
    }

    public record RequestRefillBody(string PrescriptionId, string? PharmacyName, string? Message);

    [HttpPost]
    [RequirePatientAccess(Permission.requestRefills, AppResource.Medications, AppAction.Write)]
    public async Task<IActionResult> RequestRefill([FromBody] RequestRefillBody body)
    {
        if (!Guid.TryParse(body.PrescriptionId, out var rxId))
            return BadRequest(new { error = "Please choose a prescription to refill." });

        var prescription = await _db.Prescriptions
            .Include(p => p.Medication)
            .FirstOrDefaultAsync(p => p.Id == rxId && p.PatientId == TargetPatientId);

        if (prescription == null)
            return NotFound(new { error = "Prescription not found." });

        if (!prescription.IsActive)
            return Conflict(new { error = "This prescription is no longer active. Please contact your clinic." });

        var existingPending = await _db.RefillRequests
            .AnyAsync(r => r.PrescriptionId == rxId && r.Status == RefillStatus.PENDING);

        if (existingPending)
            return Conflict(new { error = "A refill request for this prescription is already pending." });

        var refill = new RefillRequest
        {
            PrescriptionId = rxId,
            MedicationId = prescription.MedicationId,
            Status = RefillStatus.PENDING,
            PharmacyName = body.PharmacyName,
            PatientMessage = body.Message,
            RequestedByUserAccountId = Actor.UserAccountId,
            RequestedAt = DateTime.UtcNow
        };

        _db.RefillRequests.Add(refill);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("REFILL_REQUESTED", "RefillRequest", refill.Id.ToString(), Actor.UserAccountId, TargetPatientId, metadata: new { medication = prescription.Medication.Name });
        await _snapshotService.RefreshSnapshotAsync(TargetPatientId);
        await _notifications.NotifyRefillChangedAsync(TargetPatientId, refill.Id, "PENDING", new { refillId = refill.Id, medication = prescription.Medication.Name });

        return StatusCode(201, new
        {
            refill = new
            {
                id = refill.Id,
                medication = prescription.Medication.Name,
                status = refill.Status.ToString(),
                requestedAt = refill.RequestedAt.ToString("o")
            },
            message = "Refill request sent to your clinic."
        });
    }

    [HttpPost("{id}/approve")]
    [RequireClinician]
    public async Task<IActionResult> ApproveRefill(Guid id)
    {
        var refill = await _db.RefillRequests
            .Include(r => r.Prescription)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (refill == null) return NotFound(new { error = "Refill request not found." });

        refill.Status = RefillStatus.APPROVED;
        refill.ResolvedAt = DateTime.UtcNow;
        if (refill.Prescription != null)
        {
            refill.Prescription.ExpiresAt = DateTime.UtcNow.AddMonths(6);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("REFILL_APPROVED", "RefillRequest", refill.Id.ToString(), Actor.UserAccountId, refill.Prescription?.PatientId);
        if (refill.Prescription != null)
        {
            await _snapshotService.RefreshSnapshotAsync(refill.Prescription.PatientId);
            await _notifications.NotifyRefillChangedAsync(refill.Prescription.PatientId, refill.Id, "APPROVED", new { refillId = refill.Id, status = "APPROVED" });
        }

        return Ok(new { ok = true, status = "APPROVED" });
    }

    [HttpPost("{id}/deny")]
    [RequireClinician]
    public async Task<IActionResult> DenyRefill(Guid id, [FromBody] dynamic? body = null)
    {
        var refill = await _db.RefillRequests
            .Include(r => r.Prescription)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (refill == null) return NotFound(new { error = "Refill request not found." });

        refill.Status = RefillStatus.DENIED;
        refill.ResolvedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("REFILL_DENIED", "RefillRequest", refill.Id.ToString(), Actor.UserAccountId, refill.Prescription?.PatientId);
        if (refill.Prescription != null)
        {
            await _snapshotService.RefreshSnapshotAsync(refill.Prescription.PatientId);
            await _notifications.NotifyRefillChangedAsync(refill.Prescription.PatientId, refill.Id, "DENIED", new { refillId = refill.Id, status = "DENIED" });
        }

        return Ok(new { ok = true, status = "DENIED" });
    }
}
