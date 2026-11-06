using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/acute-care")]
[RequireAuth]
public class AcuteCareController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public AcuteCareController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("worklist")]
    public async Task<IActionResult> Worklist([FromQuery] AcuteCareUnit? unit = null)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        var query = _db.AcuteCareEpisodes.AsNoTracking().Include(x => x.Patient).ThenInclude(x => x.Person)
            .Where(x => x.Status != AcuteCareStatus.COMPLETED && x.Status != AcuteCareStatus.CANCELLED);
        if (unit.HasValue) query = query.Where(x => x.Unit == unit.Value);
        var episodes = await query.OrderByDescending(x => x.Acuity).ThenBy(x => x.ArrivedAt).ToListAsync();
        return Ok(new
        {
            episodes = episodes.Select(x => new
            {
                id = x.Id, patientId = x.PatientId, name = $"{x.Patient.Person.FirstName} {x.Patient.Person.LastName}",
                mrn = x.Patient.Mrn, unit = x.Unit.ToString(), status = x.Status.ToString(),
                acuity = x.Acuity.ToString(), arrivedAt = x.ArrivedAt, triagedAt = x.TriagedAt
            })
        });
    }

    [HttpPost("episodes")]
    public async Task<IActionResult> CreateEpisode([FromBody] CreateEpisodeRequest request)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        if (!Enum.IsDefined(request.Unit) || !Enum.IsDefined(request.Acuity))
            return BadRequest(new { error = "A valid acute-care unit and triage acuity are required." });
        var encounter = await _db.Encounters.FirstOrDefaultAsync(x => x.Id == request.EncounterId && x.PatientId == request.PatientId && x.Status == EncounterStatus.ACTIVE);
        if (encounter == null) return NotFound(new { error = "Active patient encounter not found." });
        var episode = new AcuteCareEpisode
        {
            PatientId = request.PatientId, EncounterId = request.EncounterId, Unit = request.Unit,
            Acuity = request.Acuity, ChiefComplaint = request.ChiefComplaint?.Trim(),
            Notes = request.Notes?.Trim(), CreatedByUserAccountId = actor.UserAccountId,
            TriagedAt = request.Unit == AcuteCareUnit.EMERGENCY ? DateTime.UtcNow : null,
            Status = request.Unit == AcuteCareUnit.EMERGENCY ? AcuteCareStatus.ACTIVE : AcuteCareStatus.TRIAGED
        };
        _db.AcuteCareEpisodes.Add(episode);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_EPISODE_CREATED", "AcuteCareEpisode", episode.Id.ToString(), actor.UserAccountId, episode.PatientId,
            metadata: new { unit = episode.Unit.ToString(), acuity = episode.Acuity.ToString() });
        return Created($"/api/acute-care/episodes/{episode.Id}", new { id = episode.Id, status = episode.Status.ToString() });
    }

    [HttpPost("episodes/{id:guid}/triage")]
    public async Task<IActionResult> Triage(Guid id, [FromBody] TriageRequest request)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        if (!Enum.IsDefined(request.Acuity)) return BadRequest(new { error = "Invalid triage acuity." });
        var episode = await _db.AcuteCareEpisodes.FirstOrDefaultAsync(x => x.Id == id);
        if (episode == null) return NotFound(new { error = "Acute-care episode not found." });
        if (episode.Status is AcuteCareStatus.COMPLETED or AcuteCareStatus.CANCELLED)
            return Conflict(new { error = "Closed episodes cannot be triaged." });
        episode.Acuity = request.Acuity;
        episode.TriagedAt = DateTime.UtcNow;
        episode.Status = AcuteCareStatus.ACTIVE;
        if (!string.IsNullOrWhiteSpace(request.Notes)) episode.Notes = request.Notes.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_TRIAGED", "AcuteCareEpisode", id.ToString(), actor.UserAccountId, episode.PatientId);
        return Ok(new { id, status = episode.Status.ToString(), acuity = episode.Acuity.ToString(), triagedAt = episode.TriagedAt });
    }

    [HttpPost("episodes/{id:guid}/transfer-request")]
    public async Task<IActionResult> RequestTransfer(Guid id, [FromBody] TransferRequest request)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        if (request.TargetUnit == AcuteCareUnit.EMERGENCY || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A destination unit other than emergency and a reason are required." });
        var episode = await _db.AcuteCareEpisodes.FirstOrDefaultAsync(x => x.Id == id && x.Status == AcuteCareStatus.ACTIVE);
        if (episode == null) return NotFound(new { error = "Active acute-care episode not found." });
        var transfer = new AcuteCareTransferRequest
        {
            EpisodeId = id, TargetUnit = request.TargetUnit, Reason = request.Reason.Trim(),
            RequestedByUserAccountId = actor.UserAccountId
        };
        episode.Status = AcuteCareStatus.TRANSFER_REQUESTED;
        _db.AcuteCareTransferRequests.Add(transfer);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_TRANSFER_REQUESTED", "AcuteCareTransferRequest", transfer.Id.ToString(), actor.UserAccountId, episode.PatientId);
        return Ok(new { transferId = transfer.Id, status = transfer.Status.ToString() });
    }

    [HttpPost("transfer-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveTransfer(Guid id)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        var transfer = await _db.AcuteCareTransferRequests.Include(x => x.Episode).FirstOrDefaultAsync(x => x.Id == id);
        if (transfer == null) return NotFound(new { error = "Transfer request not found." });
        if (transfer.Status != AcuteBookingStatus.REQUESTED) return Conflict(new { error = "Transfer request is not pending." });
        transfer.Status = AcuteBookingStatus.APPROVED;
        transfer.ApprovedByUserAccountId = actor.UserAccountId;
        transfer.ApprovedAt = DateTime.UtcNow;
        transfer.Episode.Unit = transfer.TargetUnit;
        transfer.Episode.Status = AcuteCareStatus.TRANSFERRED;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_TRANSFER_APPROVED", "AcuteCareTransferRequest", id.ToString(), actor.UserAccountId, transfer.Episode.PatientId);
        return Ok(new { transferId = id, status = transfer.Status.ToString(), unit = transfer.TargetUnit.ToString() });
    }

    [HttpPost("episodes/{id:guid}/bookings")]
    public async Task<IActionResult> BookProcedure(Guid id, [FromBody] BookingRequest request)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        if (request.Unit != AcuteCareUnit.OPERATING_THEATRE && request.Unit != AcuteCareUnit.DELIVERY_SUITE)
            return BadRequest(new { error = "Bookings are currently limited to theatre and delivery suite." });
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { error = "Booking reason is required." });
        if (!await _db.AcuteCareEpisodes.AnyAsync(x => x.Id == id && x.Status != AcuteCareStatus.COMPLETED))
            return NotFound(new { error = "Acute-care episode not found." });
        var booking = new AcuteCareBooking
        {
            EpisodeId = id, Unit = request.Unit, ScheduledAt = request.ScheduledAt,
            Reason = request.Reason.Trim(), RequestedByUserAccountId = actor.UserAccountId
        };
        _db.AcuteCareBookings.Add(booking);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_BOOKING_REQUESTED", "AcuteCareBooking", booking.Id.ToString(), actor.UserAccountId);
        return Ok(new { bookingId = booking.Id, status = booking.Status.ToString() });
    }

    [HttpPost("bookings/{id:guid}/approve")]
    public async Task<IActionResult> ApproveBooking(Guid id)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        var booking = await _db.AcuteCareBookings.FirstOrDefaultAsync(x => x.Id == id);
        if (booking == null) return NotFound(new { error = "Booking not found." });
        if (booking.Status != AcuteBookingStatus.REQUESTED)
            return Conflict(new { error = "Only requested bookings can be approved." });
        if (booking.RequestedByUserAccountId == actor.UserAccountId)
            return Forbid();
        booking.Status = AcuteBookingStatus.APPROVED;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_BOOKING_APPROVED", "AcuteCareBooking", id.ToString(), actor.UserAccountId);
        return Ok(new { bookingId = id, status = booking.Status.ToString() });
    }

    [HttpPost("episodes/{id:guid}/disposition")]
    public async Task<IActionResult> Disposition(Guid id, [FromBody] DispositionRequest request)
    {
        var actor = Actor();
        if (!CanManageAcuteCare(actor)) return Forbid();
        if (!Enum.IsDefined(request.Disposition) || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A valid disposition and reason are required." });
        var episode = await _db.AcuteCareEpisodes.FirstOrDefaultAsync(x => x.Id == id);
        if (episode == null) return NotFound(new { error = "Acute-care episode not found." });
        if (episode.Status is AcuteCareStatus.COMPLETED or AcuteCareStatus.CANCELLED)
            return Conflict(new { error = "Episode is already closed." });
        episode.Disposition = request.Disposition;
        episode.DispositionReason = request.Reason.Trim();
        episode.CompletedAt = DateTime.UtcNow;
        episode.Status = AcuteCareStatus.COMPLETED;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ACUTE_CARE_DISPOSITION_RECORDED", "AcuteCareEpisode", id.ToString(), actor.UserAccountId, episode.PatientId,
            metadata: new { disposition = request.Disposition.ToString(), reason = request.Reason.Trim() });
        return Ok(new { id, status = episode.Status.ToString(), disposition = episode.Disposition.ToString(), completedAt = episode.CompletedAt });
    }

    private AccessTokenClaims Actor() => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private static bool CanManageAcuteCare(AccessTokenClaims actor) =>
        actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN or
        UserRole.PHYSICIAN or UserRole.CLINICIAN or UserRole.NURSE;

    public sealed record CreateEpisodeRequest(Guid PatientId, Guid EncounterId, AcuteCareUnit Unit, TriageAcuity Acuity, string? ChiefComplaint = null, string? Notes = null);
    public sealed record TriageRequest(TriageAcuity Acuity, string? Notes = null);
    public sealed record TransferRequest(AcuteCareUnit TargetUnit, string Reason);
    public sealed record BookingRequest(AcuteCareUnit Unit, DateTime? ScheduledAt, string Reason);
    public sealed record DispositionRequest(EmergencyDisposition Disposition, string Reason);
}
