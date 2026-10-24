using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[EnableRateLimiting("ShareRedemptionPolicy")]
public class ShareRedemptionController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IFhirService _fhirService;
    private readonly IAuditService _audit;

    public ShareRedemptionController(MedicalPortalDbContext db, IFhirService fhirService, IAuditService audit)
    {
        _db = db;
        _fhirService = fhirService;
        _audit = audit;
    }

    public record RedeemShareBody(string? Code, string[]? Categories);

    [HttpPost("api/share")]
    public async Task<IActionResult> RedeemShare([FromBody] RedeemShareBody? body)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        if (body == null || string.IsNullOrWhiteSpace(body.Code))
        {
            await _audit.LogAsync(
                action: "SHARE_REDEMPTION_FAILED",
                resourceType: "RecordShare",
                metadata: new { reason = "missing_code" },
                ipAddress: ip,
                userAgent: userAgent);

            return BadRequest(new { error = "Share code is required." });
        }

        var incomingCode = body.Code.Trim();
        var incomingHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(incomingCode));
        var incomingHashHex = Convert.ToHexString(incomingHashBytes);

        // Candidate lookup by hash
        var candidate = await _db.RecordShares
            .Include(s => s.Patient).ThenInclude(p => p.Person)
            .FirstOrDefaultAsync(s => s.CodeHash == incomingHashHex);

        // Constant-time comparison against CodeHash to defend against timing side-channel attacks
        bool isValidHash;
        if (candidate != null && !string.IsNullOrEmpty(candidate.CodeHash))
        {
            var storedHashBytes = Convert.FromHexString(candidate.CodeHash);
            isValidHash = CryptographicOperations.FixedTimeEquals(incomingHashBytes, storedHashBytes);
        }
        else
        {
            var dummyBytes = new byte[32];
            _ = CryptographicOperations.FixedTimeEquals(incomingHashBytes, dummyBytes);
            isValidHash = false;
        }

        var now = DateTime.UtcNow;

        // Enforce expiration, maximum uses, and revocation status
        var isValid = isValidHash &&
                      candidate != null &&
                      !candidate.RevokedAt.HasValue &&
                      candidate.ExpiresAt > now &&
                      candidate.UseCount < candidate.MaxUses;

        if (!isValid)
        {
            await _audit.LogAsync(
                action: "SHARE_REDEMPTION_FAILED",
                resourceType: "RecordShare",
                resourceId: candidate?.Id.ToString(),
                patientId: candidate?.PatientId,
                metadata: new { reason = "invalid_or_expired" },
                ipAddress: ip,
                userAgent: userAgent);

            // Generic error response to prevent user enumeration / status disclosure
            return NotFound(new { error = "Invalid or expired share code." });
        }

        // Atomic UseCount increment: prevents race condition / TOCTOU over-redemption
        var rowsAffected = await _db.RecordShares
            .Where(s => s.Id == candidate!.Id && s.UseCount < s.MaxUses && s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(setter => setter.SetProperty(s => s.UseCount, s => s.UseCount + 1));

        if (rowsAffected == 0)
        {
            await _audit.LogAsync(
                action: "SHARE_REDEMPTION_FAILED",
                resourceType: "RecordShare",
                resourceId: candidate!.Id.ToString(),
                patientId: candidate!.PatientId,
                metadata: new { reason = "concurrently_exhausted" },
                ipAddress: ip,
                userAgent: userAgent);

            return NotFound(new { error = "Invalid or expired share code." });
        }

        // Record access entry
        _db.RecordShareAccesses.Add(new RecordShareAccess
        {
            ShareId = candidate!.Id,
            AccessedAt = now,
            IpAddress = ip,
            UserAgent = userAgent
        });
        await _db.SaveChangesAsync();

        // AuditLog entry for successful redemption attempt
        await _audit.LogAsync(
            action: "SHARE_REDEEMED",
            resourceType: "RecordShare",
            resourceId: candidate!.Id.ToString(),
            patientId: candidate!.PatientId,
            metadata: new
            {
                recipientName = candidate!.RecipientName,
                recipientRole = candidate!.RecipientRole,
                useCount = candidate!.UseCount + 1,
                maxUses = candidate!.MaxUses
            },
            ipAddress: ip,
            userAgent: userAgent);

        // SEC-06: Parse allowed share scope from candidate.ScopeJson
        var allowedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(candidate.ScopeJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(candidate.ScopeJson);
                if (parsed != null)
                {
                    foreach (var cat in parsed) allowedCategories.Add(cat.Trim());
                }
            }
            catch
            {
                // Fallback to default full scope if malformed
            }
        }

        if (allowedCategories.Count == 0)
        {
            allowedCategories.UnionWith(new[] { "allergies", "conditions", "medications", "labs", "vitals" });
        }

        IEnumerable<string> effectiveCategories;
        if (body.Categories != null && body.Categories.Length > 0)
        {
            // Validate requested categories as subset of allowed scope
            foreach (var requested in body.Categories)
            {
                if (!allowedCategories.Contains(requested.Trim()))
                {
                    return BadRequest(new { error = $"Requested category '{requested}' is outside the authorized share scope." });
                }
            }
            effectiveCategories = body.Categories.Select(c => c.Trim());
        }
        else
        {
            effectiveCategories = allowedCategories;
        }

        var bundle = await _fhirService.GetScopedPatientBundleAsync(candidate.PatientId, effectiveCategories);
        return Ok(bundle);
    }
}
