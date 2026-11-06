using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Observability;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/hms")]
[RequireAuth]
public class HmsFoundationController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public HmsFoundationController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("bed-board")]
    public async Task<IActionResult> GetBedBoard()
    {
        if (!CanManageAdmissions((AccessTokenClaims)HttpContext.Items["Actor"]!))
            return Forbid();

        var beds = await _db.Beds
            .AsNoTracking()
            .Include(b => b.Location)
            .Where(b => b.IsActive)
            .OrderBy(b => b.Location.Name)
            .ThenBy(b => b.Room)
            .ThenBy(b => b.Code)
            .ToListAsync();

        var encounterIds = beds.Where(b => b.CurrentEncounterId.HasValue)
            .Select(b => b.CurrentEncounterId!.Value)
            .ToHashSet();
        var encounters = await _db.Encounters
            .AsNoTracking()
            .Include(e => e.Patient).ThenInclude(p => p.Person)
            .Where(e => encounterIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        return Ok(new
        {
            beds = beds.Select(b =>
            {
                encounters.TryGetValue(b.CurrentEncounterId ?? Guid.Empty, out var encounter);
                return new
                {
                    id = b.Id,
                    code = b.Code,
                    room = b.Room,
                    location = b.Location.Name,
                    state = b.State.ToString(),
                    occupied = encounter != null,
                    patient = encounter == null ? null : new
                    {
                        id = encounter.PatientId,
                        name = $"{encounter.Patient.Person.FirstName} {encounter.Patient.Person.LastName}",
                        mrn = encounter.Patient.Mrn,
                        patientClass = encounter.PatientClass.ToString()
                    }
                };
            })
        });
    }

    [HttpGet("location-hierarchy")]
    public async Task<IActionResult> GetLocationHierarchy()
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!CanManageAdmissions(actor))
            return Forbid();

        var facilities = await _db.Facilities
            .AsNoTracking()
            .Include(f => f.Departments)
            .ThenInclude(d => d.Locations)
            .OrderBy(f => f.Name)
            .ToListAsync();

        return Ok(new
        {
            facilities = facilities.Select(f => new
            {
                id = f.Id,
                name = f.Name,
                code = f.Code,
                departments = f.Departments.OrderBy(d => d.Name).Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    code = d.Code,
                    locations = d.Locations.OrderBy(l => l.Name).Select(l => new
                    {
                        id = l.Id,
                        parentLocationId = l.ParentLocationId,
                        name = l.Name,
                        type = l.Type.ToString()
                    })
                })
            })
        });
    }

    [HttpPost("beds/{id:guid}/state")]
    public async Task<IActionResult> ChangeBedState(Guid id, [FromBody] ChangeBedStateRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!CanManageAdmissions(actor))
            return Forbid();
        if (!Enum.IsDefined(request.State))
            return BadRequest(new { error = "Invalid bed state." });
        if ((request.State is BedState.BLOCKED or BedState.OUT_OF_SERVICE) && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A reason is required for blocked or out-of-service beds." });

        var bed = await _db.Beds.FirstOrDefaultAsync(b => b.Id == id && b.IsActive);
        if (bed == null)
            return NotFound(new { error = "Bed not found." });
        if (bed.CurrentEncounterId.HasValue && request.State != BedState.OCCUPIED)
            return Conflict(new { error = "An occupied bed must remain occupied until the encounter is discharged or transferred." });
        if (bed.CurrentEncounterId.HasValue && request.State == BedState.OCCUPIED)
            return Ok(new { id, state = bed.State.ToString() });

        var previousState = bed.State;
        bed.State = request.State;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            "BED_STATE_CHANGED",
            "Bed",
            id.ToString(),
            actor.UserAccountId,
            metadata: new { previousState = previousState.ToString(), state = bed.State.ToString(), reason = request.Reason?.Trim() });

        return Ok(new { id, state = bed.State.ToString() });
    }

    [HttpPost("encounters")]
    public async Task<IActionResult> CreateEncounter([FromBody] CreateEncounterRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!CanManageAdmissions(actor))
            return Forbid();
        if (request.PatientId == Guid.Empty)
            return BadRequest(new { error = "PatientId is required." });

        var patientExists = await _db.Patients.AnyAsync(p => p.Id == request.PatientId);
        if (!patientExists)
            return NotFound(new { error = "Patient not found." });

        Bed? bed = null;
        if (request.BedId.HasValue)
        {
            bed = await _db.Beds.FirstOrDefaultAsync(b => b.Id == request.BedId.Value && b.IsActive);
            if (bed == null)
                return NotFound(new { error = "Bed not found." });
            if (bed.CurrentEncounterId.HasValue)
                return Conflict(new { error = "Bed is already occupied." });
            if (bed.State != BedState.AVAILABLE)
                return Conflict(new { error = $"Bed is {bed.State.ToString().ToLowerInvariant()}." });
        }

        var encounter = new Encounter
        {
            PatientId = request.PatientId,
            LocationId = request.LocationId,
            BedId = request.BedId,
            PatientClass = request.PatientClass,
            Status = EncounterStatus.ACTIVE,
            Reason = request.Reason?.Trim(),
            StartedAt = request.StartedAt ?? DateTime.UtcNow,
            ClassifiedByUserAccountId = actor.UserAccountId
        };
        _db.Encounters.Add(encounter);
        if (bed != null)
        {
            bed.CurrentEncounterId = encounter.Id;
            bed.State = BedState.OCCUPIED;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            "ENCOUNTER_CREATED",
            "Encounter",
            encounter.Id.ToString(),
            actor.UserAccountId,
            encounter.PatientId,
            metadata: new { patientClass = encounter.PatientClass.ToString(), bedId = encounter.BedId });

        return Created($"/api/hms/encounters/{encounter.Id}", new
        {
            id = encounter.Id,
            patientId = encounter.PatientId,
            patientClass = encounter.PatientClass.ToString(),
            status = encounter.Status.ToString()
        });
    }

    [HttpPost("encounters/{id:guid}/reclassify")]
    public async Task<IActionResult> ReclassifyEncounter(Guid id, [FromBody] ReclassifyEncounterRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!CanManageAdmissions(actor))
            return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A reclassification reason is required." });

        var encounter = await _db.Encounters.FirstOrDefaultAsync(e => e.Id == id);
        if (encounter == null)
            return NotFound(new { error = "Encounter not found." });
        if (encounter.Status != EncounterStatus.ACTIVE)
            return Conflict(new { error = "Only active encounters can be reclassified." });

        var previousClass = encounter.PatientClass;
        encounter.PatientClass = request.PatientClass;
        encounter.ClassificationReason = request.Reason.Trim();
        encounter.ClassifiedByUserAccountId = actor.UserAccountId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            "ENCOUNTER_RECLASSIFIED",
            "Encounter",
            id.ToString(),
            actor.UserAccountId,
            encounter.PatientId,
            metadata: new { previousClass = previousClass.ToString(), patientClass = encounter.PatientClass.ToString(), reason = encounter.ClassificationReason });

        return Ok(new { id, patientClass = encounter.PatientClass.ToString(), status = encounter.Status.ToString() });
    }

    [HttpPost("encounters/{id:guid}/transfer")]
    public async Task<IActionResult> TransferEncounter(Guid id, [FromBody] TransferEncounterRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!CanManageAdmissions(actor))
            return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A transfer reason is required." });

        var encounter = await _db.Encounters.FirstOrDefaultAsync(e => e.Id == id);
        if (encounter == null)
            return NotFound(new { error = "Encounter not found." });
        if (encounter.Status != EncounterStatus.ACTIVE)
            return Conflict(new { error = "Only active encounters can be transferred." });
        if (encounter.BedId == request.BedId)
            return BadRequest(new { error = "The encounter is already assigned to this bed." });

        var targetBed = await _db.Beds.FirstOrDefaultAsync(b => b.Id == request.BedId && b.IsActive);
        if (targetBed == null)
            return NotFound(new { error = "Target bed not found." });
        if (targetBed.CurrentEncounterId.HasValue)
            return Conflict(new { error = "Target bed is already occupied." });

        var previousBedId = encounter.BedId;
        if (previousBedId.HasValue)
        {
            var previousBed = await _db.Beds.FirstOrDefaultAsync(b => b.Id == previousBedId.Value);
            if (previousBed != null)
            {
                previousBed.CurrentEncounterId = null;
                previousBed.State = BedState.CLEANING;
            }
        }

        encounter.BedId = targetBed.Id;
        encounter.LocationId = targetBed.LocationId;
        targetBed.CurrentEncounterId = encounter.Id;
        targetBed.State = BedState.OCCUPIED;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            "ENCOUNTER_TRANSFERRED",
            "Encounter",
            id.ToString(),
            actor.UserAccountId,
            encounter.PatientId,
            metadata: new { previousBedId, bedId = targetBed.Id, reason = request.Reason.Trim() });

        return Ok(new { id, bedId = encounter.BedId, locationId = encounter.LocationId, status = encounter.Status.ToString() });
    }

    [HttpPost("encounters/{id:guid}/discharge")]
    public async Task<IActionResult> DischargeEncounter(Guid id, [FromBody] DischargeEncounterRequest request)
    {
        var actor = (AccessTokenClaims)HttpContext.Items["Actor"]!;
        if (!CanManageAdmissions(actor))
            return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "A discharge reason is required." });

        var encounter = await _db.Encounters.FirstOrDefaultAsync(e => e.Id == id);
        if (encounter == null)
            return NotFound(new { error = "Encounter not found." });
        if (encounter.Status != EncounterStatus.ACTIVE)
            return Conflict(new { error = "Only active encounters can be discharged." });

        if (encounter.BedId.HasValue)
        {
            var bed = await _db.Beds.FirstOrDefaultAsync(b => b.Id == encounter.BedId.Value);
            if (bed != null && bed.CurrentEncounterId == encounter.Id)
            {
                bed.CurrentEncounterId = null;
                bed.State = BedState.CLEANING;
                _db.SupportServiceTasks.Add(new SupportServiceTask
                {
                    Type = SupportTaskType.TERMINAL_CLEANING,
                    Status = SupportTaskStatus.OPEN,
                    BedId = bed.Id,
                    LocationId = bed.LocationId,
                    Notes = "Terminal cleaning required after discharge."
                });
            }
        }

        encounter.Status = EncounterStatus.COMPLETED;
        encounter.EndedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            "ENCOUNTER_DISCHARGED",
            "Encounter",
            id.ToString(),
            actor.UserAccountId,
            encounter.PatientId,
            metadata: new { disposition = request.Reason.Trim() });

        return Ok(new { id, status = encounter.Status.ToString(), endedAt = encounter.EndedAt });
    }

    public sealed record CreateEncounterRequest(
        Guid PatientId,
        PatientClass PatientClass,
        Guid? LocationId = null,
        Guid? BedId = null,
        string? Reason = null,
        DateTime? StartedAt = null);

    public sealed record ReclassifyEncounterRequest(PatientClass PatientClass, string Reason);
    public sealed record TransferEncounterRequest(Guid BedId, string Reason);
    public sealed record DischargeEncounterRequest(string Reason);
    public sealed record ChangeBedStateRequest(BedState State, string? Reason = null);

    private static bool CanManageAdmissions(AccessTokenClaims actor) =>
        actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN
            or UserRole.PHYSICIAN or UserRole.CLINICIAN or UserRole.NURSE;
}
