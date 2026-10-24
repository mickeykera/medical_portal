using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/clinical")]
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class BreakGlassController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;
    private readonly IPortalNotificationService _notifications;

    public BreakGlassController(
        MedicalPortalDbContext db,
        IAuditService audit,
        IPortalNotificationService notifications)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
    }

    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    public record BreakGlassRequest(Guid PatientId, string? Reason);

    [HttpPost("break-glass")]
    [HttpPost("emergency-override")]
    public async Task<IActionResult> RequestBreakGlass([FromBody] BreakGlassRequest? request)
    {
        if (Actor.Role != UserRole.CLINICIAN && Actor.Role != UserRole.PHYSICIAN && Actor.Role != UserRole.ADMIN && Actor.Role != UserRole.SYSTEM_ADMIN)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Only clinical staff may initiate an emergency break-glass override."
            });
        }

        if (request == null || request.PatientId == Guid.Empty)
        {
            return BadRequest(new { error = "Valid patient ID is required." });
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 5)
        {
            return BadRequest(new
            {
                error = "A written clinical reason (at least 5 characters) is required for emergency break-glass access."
            });
        }

        var patient = await _db.Patients
            .Include(p => p.Person)
            .FirstOrDefaultAsync(p => p.Id == request.PatientId);

        if (patient == null)
        {
            return NotFound(new { error = "Patient not found." });
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(60);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var breakGlass = new BreakGlassOverride
        {
            ClinicianUserAccountId = Actor.UserAccountId,
            PatientId = request.PatientId,
            Reason = reason,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            IpAddress = ip,
            UserAgent = userAgent
        };

        _db.BreakGlassOverrides.Add(breakGlass);
        await _db.SaveChangesAsync();

        // High-Priority AuditLog Entry
        await _audit.LogAsync(
            action: "BREAK_GLASS_EMERGENCY_OVERRIDE",
            resourceType: "Patient",
            resourceId: request.PatientId.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            patientId: request.PatientId,
            metadata: new
            {
                priority = "HIGH",
                severity = "EMERGENCY",
                overrideId = breakGlass.Id,
                reason = reason,
                grantedAt = now.ToString("o"),
                expiresAt = expiresAt.ToString("o")
            },
            ipAddress: ip,
            userAgent: userAgent
        );

        // Notify Patient and Compliance Queue
        var clinicianAccount = await _db.UserAccounts
            .Include(u => u.Person)
            .FirstOrDefaultAsync(u => u.Id == Actor.UserAccountId);

        var clinicianName = clinicianAccount?.Person != null
            ? $"{clinicianAccount.Person.FirstName} {clinicianAccount.Person.LastName}".Trim()
            : "Emergency Clinician";

        var payload = new
        {
            overrideId = breakGlass.Id,
            patientId = request.PatientId,
            patientName = $"{patient.Person.FirstName} {patient.Person.LastName}".Trim(),
            clinicianId = Actor.UserAccountId,
            clinicianName = clinicianName,
            reason = reason,
            grantedAt = now.ToString("o"),
            expiresAt = expiresAt.ToString("o")
        };

        await _notifications.NotifyBreakGlassAsync(request.PatientId, Actor.UserAccountId, reason, expiresAt, payload);

        return Ok(new
        {
            ok = true,
            overrideId = breakGlass.Id,
            patientId = request.PatientId,
            grantedAt = now.ToString("o"),
            expiresAt = expiresAt.ToString("o"),
            expiresInMinutes = 60,
            reason = reason,
            message = "Emergency break-glass access granted for 60 minutes. All actions are logged and compliance-monitored."
        });
    }

    [HttpGet("break-glass/status")]
    public async Task<IActionResult> GetBreakGlassStatus([FromQuery] Guid? patientId)
    {
        if (!patientId.HasValue || patientId.Value == Guid.Empty)
        {
            return BadRequest(new { error = "patientId query parameter is required." });
        }

        var now = DateTime.UtcNow;
        var activeOverride = await _db.BreakGlassOverrides
            .AsNoTracking()
            .Where(b => b.ClinicianUserAccountId == Actor.UserAccountId &&
                        b.PatientId == patientId.Value &&
                        b.ExpiresAt > now &&
                        b.RevokedAt == null)
            .OrderByDescending(b => b.GrantedAt)
            .FirstOrDefaultAsync();

        if (activeOverride == null)
        {
            return Ok(new { active = false });
        }

        var remainingSeconds = Math.Max(0, (activeOverride.ExpiresAt - now).TotalSeconds);

        return Ok(new
        {
            active = true,
            overrideId = activeOverride.Id,
            patientId = activeOverride.PatientId,
            grantedAt = activeOverride.GrantedAt.ToString("o"),
            expiresAt = activeOverride.ExpiresAt.ToString("o"),
            remainingMinutes = (int)Math.Ceiling(remainingSeconds / 60),
            reason = activeOverride.Reason
        });
    }

    public record RevokeBreakGlassRequest(Guid PatientId);

    [HttpPost("break-glass/revoke")]
    public async Task<IActionResult> RevokeBreakGlass([FromBody] RevokeBreakGlassRequest? request)
    {
        if (request == null || request.PatientId == Guid.Empty)
        {
            return BadRequest(new { error = "Valid patient ID is required." });
        }

        var now = DateTime.UtcNow;
        var activeOverrides = await _db.BreakGlassOverrides
            .Where(b => b.ClinicianUserAccountId == Actor.UserAccountId &&
                        b.PatientId == request.PatientId &&
                        b.ExpiresAt > now &&
                        b.RevokedAt == null)
            .ToListAsync();

        if (activeOverrides.Count == 0)
        {
            return NotFound(new { error = "No active break-glass session found to revoke." });
        }

        foreach (var activeOverride in activeOverrides)
        {
            activeOverride.RevokedAt = now;
            activeOverride.RevokedReason = "Clinician concluded emergency session";
        }
        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            action: "BREAK_GLASS_REVOKED",
            resourceType: "Patient",
            resourceId: request.PatientId.ToString(),
            actorUserAccountId: Actor.UserAccountId,
            patientId: request.PatientId,
            metadata: new
            {
                revokedCount = activeOverrides.Count,
                revokedAt = now.ToString("o"),
                reason = "Clinician concluded emergency session"
            },
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers.UserAgent.ToString()
        );

        return Ok(new
        {
            ok = true,
            message = "Break-glass emergency session revoked."
        });
    }
}
