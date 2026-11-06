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
[Route("api/mortuary")]
[RequireAuth]
public class MortuaryController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public MortuaryController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("cases")]
    public async Task<IActionResult> Cases()
    {
        if (!CanAccess(Actor())) return Forbid();
        var rows = await _db.MortuaryCases.AsNoTracking().OrderByDescending(x => x.DeathDeclaredAt)
            .Select(x => new
            {
                id = x.Id, patientId = x.PatientId, status = x.Status.ToString(), tagNumber = x.TagNumber,
                storageLocation = x.StorageLocation, deathDeclaredAt = x.DeathDeclaredAt,
                forensic = x.ForensicFlag, infectious = x.InfectiousFlag
            }).ToListAsync();
        return Ok(new { cases = rows });
    }

    [HttpPost("cases")]
    public async Task<IActionResult> Create([FromBody] CreateCaseRequest request)
    {
        var actor = Actor();
        if (!CanAccess(actor)) return Forbid();
        var patientExists = await _db.Patients.AnyAsync(x => x.Id == request.PatientId);
        if (!patientExists) return NotFound(new { error = "Patient not found." });
        if (string.IsNullOrWhiteSpace(request.TagNumber))
            return BadRequest(new { error = "Tag number is required." });
        var mortuaryCase = new MortuaryCase
        {
            PatientId = request.PatientId, EncounterId = request.EncounterId,
            DeclaredByUserAccountId = actor.UserAccountId, DeathCertificateReference = request.DeathCertificateReference?.Trim(),
            TagNumber = request.TagNumber.Trim(), ForensicFlag = request.ForensicFlag,
            InfectiousFlag = request.InfectiousFlag, Status = MortuaryCaseStatus.INTAKE
        };
        _db.MortuaryCases.Add(mortuaryCase);
        _db.MortuaryCustodyEvents.Add(new MortuaryCustodyEvent
        {
            MortuaryCase = mortuaryCase, Action = MortuaryCustodyAction.RECEIVED,
            ToLocation = request.StorageLocation?.Trim(), RecordedByUserAccountId = actor.UserAccountId
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MORTUARY_CASE_CREATED", "MortuaryCase", mortuaryCase.Id.ToString(), actor.UserAccountId, request.PatientId);
        return Created($"/api/mortuary/cases/{mortuaryCase.Id}", new { id = mortuaryCase.Id, status = mortuaryCase.Status.ToString() });
    }

    [HttpPost("cases/{id:guid}/move")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveRequest request)
    {
        var actor = Actor();
        if (!CanAccess(actor)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.ToLocation)) return BadRequest(new { error = "Destination location is required." });
        var mortuaryCase = await _db.MortuaryCases.FirstOrDefaultAsync(x => x.Id == id);
        if (mortuaryCase == null) return NotFound(new { error = "Mortuary case not found." });
        if (mortuaryCase.Status is MortuaryCaseStatus.RELEASED or MortuaryCaseStatus.CLOSED)
            return Conflict(new { error = "Released or closed cases cannot be moved." });
        var from = mortuaryCase.StorageLocation;
        mortuaryCase.StorageLocation = request.ToLocation.Trim();
        mortuaryCase.Status = MortuaryCaseStatus.STORED;
        _db.MortuaryCustodyEvents.Add(new MortuaryCustodyEvent
        {
            MortuaryCaseId = id, Action = MortuaryCustodyAction.MOVED, FromLocation = from,
            ToLocation = mortuaryCase.StorageLocation, RecordedByUserAccountId = actor.UserAccountId
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MORTUARY_CASE_MOVED", "MortuaryCase", id.ToString(), actor.UserAccountId);
        return Ok(new { id, status = mortuaryCase.Status.ToString(), storageLocation = mortuaryCase.StorageLocation });
    }

    [HttpPost("cases/{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id, [FromBody] ReleaseRequest request)
    {
        var actor = Actor();
        if (!CanAccess(actor)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.AuthorizationReference) || string.IsNullOrWhiteSpace(request.IdentityVerificationReference))
            return BadRequest(new { error = "Authorization and identity verification references are required." });
        var mortuaryCase = await _db.MortuaryCases.FirstOrDefaultAsync(x => x.Id == id);
        if (mortuaryCase == null) return NotFound(new { error = "Mortuary case not found." });
        if (mortuaryCase.Status is MortuaryCaseStatus.RELEASED or MortuaryCaseStatus.CLOSED)
            return Conflict(new { error = "Mortuary case is already closed." });
        mortuaryCase.Status = MortuaryCaseStatus.RELEASED;
        mortuaryCase.ReleaseAuthorizationReference = request.AuthorizationReference.Trim();
        mortuaryCase.ReleasedAt = DateTime.UtcNow;
        _db.MortuaryCustodyEvents.Add(new MortuaryCustodyEvent
        {
            MortuaryCaseId = id, Action = MortuaryCustodyAction.RELEASED,
            FromLocation = mortuaryCase.StorageLocation, IdentityVerificationReference = request.IdentityVerificationReference.Trim(),
            RecordedByUserAccountId = actor.UserAccountId
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MORTUARY_CASE_RELEASED", "MortuaryCase", id.ToString(), actor.UserAccountId);
        return Ok(new { id, status = mortuaryCase.Status.ToString(), releasedAt = mortuaryCase.ReleasedAt });
    }

    private AccessTokenClaims Actor() => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private static bool CanAccess(AccessTokenClaims actor) =>
        actor.Role is UserRole.ADMIN or UserRole.SYSTEM_ADMIN or UserRole.DEPT_ADMIN;

    public sealed record CreateCaseRequest(Guid PatientId, Guid? EncounterId, string TagNumber, string? StorageLocation = null, string? DeathCertificateReference = null, bool ForensicFlag = false, bool InfectiousFlag = false);
    public sealed record MoveRequest(string ToLocation);
    public sealed record ReleaseRequest(string AuthorizationReference, string IdentityVerificationReference);
}
