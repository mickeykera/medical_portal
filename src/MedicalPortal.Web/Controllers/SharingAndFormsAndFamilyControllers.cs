using System.Security.Cryptography;
using System.Text.Json;
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
[RequireAuth]
[EnableRateLimiting("GeneralApiPolicy")]
public class SharingController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IFhirService _fhirService;
    private readonly IAuditService _audit;

    public SharingController(MedicalPortalDbContext db, IFhirService fhirService, IAuditService audit)
    {
        _db = db;
        _fhirService = fhirService;
        _audit = audit;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    [HttpGet("api/shares")]
    [RequirePatientAccess(Permission.manageSharing)]
    public async Task<IActionResult> GetShares()
    {
        var shares = await _db.RecordShares
            .AsNoTracking()
            .Include(s => s.Accesses)
            .Where(s => s.PatientId == TargetPatientId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(new
        {
            availableCategories = new[] { "allergies", "conditions", "medications", "labs", "vitals", "immunizations" },
            shares = shares.Select(s =>
            {
                var isExpired = s.ExpiresAt < DateTime.UtcNow;
                var isExhausted = s.UseCount >= s.MaxUses;
                var status = s.RevokedAt.HasValue ? "REVOKED" : (isExpired ? "EXPIRED" : (isExhausted ? "EXHAUSTED" : "ACTIVE"));

                return new
                {
                    id = s.Id,
                    label = s.Label,
                    recipientName = s.RecipientName,
                    recipientRole = s.RecipientRole,
                    scope = JsonSerializer.Deserialize<string[]>(s.ScopeJson) ?? Array.Empty<string>(),
                    createdAt = s.CreatedAt.ToString("o"),
                    expiresAt = s.ExpiresAt.ToString("o"),
                    revokedAt = s.RevokedAt?.ToString("o"),
                    maxUses = s.MaxUses,
                    useCount = s.UseCount,
                    remainingUses = Math.Max(0, s.MaxUses - s.UseCount),
                    lastUsedAt = s.Accesses.OrderByDescending(a => a.AccessedAt).FirstOrDefault()?.AccessedAt.ToString("o"),
                    accessCount = s.Accesses.Count,
                    status
                };
            })
        });
    }

    public record CreateShareBody(
        string Label,
        string? RecipientName,
        string? RecipientRole,
        string[] Scope,
        int ExpiresInDays,
        int MaxUses);

    [HttpPost("api/shares")]
    [RequirePatientAccess(Permission.manageSharing)]
    public async Task<IActionResult> CreateShare([FromBody] CreateShareBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Label is required." });

        var days = Math.Clamp(body.ExpiresInDays > 0 ? body.ExpiresInDays : 30, 1, 90);
        var maxUses = Math.Clamp(body.MaxUses > 0 ? body.MaxUses : 5, 1, 50);

        // Generate high entropy share code
        var code = $"SHR-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
        var codeHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));

        var share = new RecordShare
        {
            PatientId = TargetPatientId,
            Label = body.Label.Trim(),
            CodeHash = codeHash,
            RecipientName = body.RecipientName,
            RecipientRole = body.RecipientRole,
            ScopeJson = JsonSerializer.Serialize(body.Scope != null && body.Scope.Length > 0 ? body.Scope : new[] { "allergies", "conditions", "medications", "labs", "vitals" }),
            MaxUses = maxUses,
            UseCount = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(days)
        };

        _db.RecordShares.Add(share);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("SHARE_CREATED", "RecordShare", share.Id.ToString(), Actor.UserAccountId, TargetPatientId);

        return StatusCode(201, new
        {
            share = new
            {
                id = share.Id,
                label = share.Label,
                status = "ACTIVE"
            },
            code,
            message = "Share created. Give the recipient this code to view the record."
        });
    }

    [HttpPost("api/shares/{id}/revoke")]
    [RequirePatientAccess(Permission.manageSharing)]
    public async Task<IActionResult> RevokeShare(Guid id)
    {
        var share = await _db.RecordShares.FirstOrDefaultAsync(s => s.Id == id && s.PatientId == TargetPatientId);
        if (share == null)
            return NotFound(new { error = "Share not found." });

        share.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("SHARE_REVOKED", "RecordShare", share.Id.ToString(), Actor.UserAccountId, TargetPatientId);

        return Ok(new { ok = true, message = "Share has been revoked." });
    }
}

[ApiController]
[Route("api/forms")]
[RequireAuth]
public class FormsController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public FormsController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;
    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;

    [HttpGet]
    [RequirePatientAccess(Permission.viewRecords)]
    public async Task<IActionResult> GetForms()
    {
        var definitions = await _db.FormDefinitions.AsNoTracking().ToListAsync();
        var responses = await _db.FormResponses
            .AsNoTracking()
            .Where(r => r.PatientId == TargetPatientId)
            .ToListAsync();

        var forms = definitions.Select(def =>
        {
            var resp = responses.FirstOrDefault(r => r.FormDefinitionId == def.Id);
            var fields = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(def.FieldsJson) ?? new();

            int completedFields = 0;
            var outstanding = new List<string>();

            if (resp != null)
            {
                var answers = JsonSerializer.Deserialize<Dictionary<string, object>>(resp.AnswersJson) ?? new();
                foreach (var f in fields)
                {
                    var id = f["id"]?.ToString() ?? "";
                    if (answers.ContainsKey(id) && answers[id] != null && !string.IsNullOrEmpty(answers[id].ToString()))
                    {
                        completedFields++;
                    }
                    else
                    {
                        outstanding.Add(f.GetValueOrDefault("label")?.ToString() ?? id);
                    }
                }
            }
            else
            {
                outstanding = fields.Select(f => f.GetValueOrDefault("label")?.ToString() ?? "").ToList();
            }

            var completion = fields.Count > 0 ? (int)Math.Round((double)completedFields / fields.Count * 100) : 0;

            return new
            {
                id = def.Id,
                slug = def.Slug,
                title = def.Title,
                description = def.Description,
                category = def.Category,
                fields,
                response = resp != null ? new
                {
                    answers = JsonSerializer.Deserialize<object>(resp.AnswersJson),
                    submittedAt = resp.SubmittedAt.ToString("o"),
                    status = resp.Status
                } : null,
                completion,
                outstandingFields = outstanding
            };
        });

        return Ok(new { forms });
    }

    public record SubmitFormBody(Dictionary<string, object> Answers);

    [HttpPost("{id}/submit")]
    [RequirePatientAccess(Permission.viewRecords)]
    public async Task<IActionResult> SubmitForm(Guid id, [FromBody] SubmitFormBody body)
    {
        var def = await _db.FormDefinitions.FindAsync(id);
        if (def == null) return NotFound(new { error = "Form not found." });

        var existing = await _db.FormResponses.FirstOrDefaultAsync(r => r.FormDefinitionId == id && r.PatientId == TargetPatientId);
        if (existing == null)
        {
            existing = new FormResponse
            {
                PatientId = TargetPatientId,
                FormDefinitionId = id,
                SubmittedByUserAccountId = Actor.UserAccountId
            };
            _db.FormResponses.Add(existing);
        }

        existing.AnswersJson = JsonSerializer.Serialize(body.Answers ?? new());
        existing.SubmittedAt = DateTime.UtcNow;
        existing.Status = "SUBMITTED";

        await _db.SaveChangesAsync();

        await _audit.LogAsync("FORM_SUBMITTED", "FormResponse", existing.Id.ToString(), Actor.UserAccountId, TargetPatientId);

        return Ok(new
        {
            response = new { id = existing.Id },
            message = "Form submitted successfully."
        });
    }
}

[ApiController]
[RequireAuth]
public class FamilyAndSettingsController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IAuditService _audit;

    public FamilyAndSettingsController(MedicalPortalDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private AccessTokenClaims Actor => (AccessTokenClaims)HttpContext.Items["Actor"]!;
    private Guid TargetPatientId => (Guid)HttpContext.Items["TargetPatientId"]!;

    [HttpGet("api/invites")]
    public async Task<IActionResult> GetInvites()
    {
        var invites = await _db.ProxyInvites
            .AsNoTracking()
            .Where(i => i.InviterPersonId == Actor.PersonId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(new
        {
            invites = invites.Select(i => new
            {
                id = i.Id,
                email = i.Email,
                relationship = i.Relationship,
                status = i.Status,
                effectiveStatus = (i.ExpiresAt < DateTime.UtcNow && i.Status == "PENDING") ? "EXPIRED" : i.Status,
                createdAt = i.CreatedAt.ToString("o"),
                expiresAt = i.ExpiresAt.ToString("o"),
                permissions = new
                {
                    canViewRecords = i.CanViewRecords,
                    canMessage = i.CanMessage,
                    canManageAppointments = i.CanManageAppointments,
                    canRequestRefills = i.CanRequestRefills,
                    canPayBills = i.CanPayBills
                }
            })
        });
    }

    public record CreateInviteBody(
        string Email,
        string? Relationship,
        bool CanMessage,
        bool CanManageAppointments,
        bool CanRequestRefills,
        bool CanPayBills);

    [HttpPost("api/invites")]
    public async Task<IActionResult> CreateInvite([FromBody] CreateInviteBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
            return BadRequest(new { error = "Email is required." });

        var code = $"INV-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
        var codeHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));

        var invite = new ProxyInvite
        {
            InviterPersonId = Actor.PersonId,
            Email = body.Email.Trim().ToLowerInvariant(),
            CodeHash = codeHash,
            Relationship = body.Relationship,
            CanViewRecords = true,
            CanMessage = body.CanMessage,
            CanManageAppointments = body.CanManageAppointments,
            CanRequestRefills = body.CanRequestRefills,
            CanPayBills = body.CanPayBills,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _db.ProxyInvites.Add(invite);
        await _db.SaveChangesAsync();

        return StatusCode(201, new
        {
            invite = new
            {
                id = invite.Id,
                email = invite.Email,
                status = invite.Status
            },
            code,
            message = "Invitation sent. Share the code with your family member."
        });
    }

    [HttpPost("api/invites/{id}/revoke")]
    public async Task<IActionResult> RevokeInvite(Guid id)
    {
        var invite = await _db.ProxyInvites.FirstOrDefaultAsync(i => i.Id == id && i.InviterPersonId == Actor.PersonId);
        if (invite == null) return NotFound(new { error = "Invite not found." });

        invite.Status = "REVOKED";
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, message = "Invitation revoked." });
    }

    public record AcceptInviteBody(string Code);

    [HttpPost("api/invites/accept")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
            return BadRequest(new { error = "Invitation code is required." });

        var codeHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body.Code.Trim())));
        var invite = await _db.ProxyInvites.FirstOrDefaultAsync(i => i.CodeHash == codeHash && i.Status == "PENDING");

        if (invite == null || invite.ExpiresAt < DateTime.UtcNow)
            return BadRequest(new { error = "Invalid or expired invitation code." });

        invite.Status = "ACCEPTED";
        invite.AcceptedAt = DateTime.UtcNow;

        // Create ProxyGrant
        var grant = new ProxyGrant
        {
            GrantorId = invite.InviterPersonId,
            GranteeId = Actor.PersonId,
            CanViewRecords = invite.CanViewRecords,
            CanMessage = invite.CanMessage,
            CanManageAppointments = invite.CanManageAppointments,
            CanRequestRefills = invite.CanRequestRefills,
            CanPayBills = invite.CanPayBills,
            CanManageSharing = false,
            ConsentGivenAt = DateTime.UtcNow
        };
        _db.ProxyGrants.Add(grant);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, message = "Invitation accepted. You now have proxy access." });
    }

    [HttpGet("api/access-log")]
    [RequirePatientAccess(Permission.viewRecords)]
    public async Task<IActionResult> GetAccessLog()
    {
        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(l => l.PatientId == TargetPatientId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .ToListAsync();

        var userAccountIds = logs.Where(l => l.ActorUserAccountId.HasValue).Select(l => l.ActorUserAccountId!.Value).Distinct().ToList();
        var users = await _db.UserAccounts
            .Include(u => u.Person)
            .Where(u => userAccountIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        return Ok(new
        {
            entries = logs.Select(l =>
            {
                var actorAccount = l.ActorUserAccountId.HasValue ? users.GetValueOrDefault(l.ActorUserAccountId.Value) : null;
                var actorName = actorAccount?.Person != null ? $"{actorAccount.Person.FirstName} {actorAccount.Person.LastName}" : "System";

                return new
                {
                    id = l.Id,
                    action = l.Action,
                    resourceType = l.ResourceType,
                    at = l.CreatedAt.ToString("o"),
                    viaProxy = l.ActingForPersonId.HasValue,
                    actor = new
                    {
                        name = actorName,
                        email = actorAccount?.Email,
                        role = actorAccount?.Role.ToString() ?? "SYSTEM"
                    },
                    ipAddress = l.IpAddress
                };
            })
        });
    }

    [HttpGet("api/notification-preferences")]
    public async Task<IActionResult> GetNotificationPreferences()
    {
        var categories = new[] { "Appointments", "Billing", "Messages", "Lab Results", "Prescriptions" };
        var prefs = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.PersonId == Actor.PersonId)
            .ToListAsync();

        return Ok(new
        {
            categories,
            preferences = categories.Select(cat =>
            {
                var existing = prefs.FirstOrDefault(p => p.Category == cat);
                return new
                {
                    category = cat,
                    emailEnabled = existing?.EmailEnabled ?? true,
                    smsEnabled = existing?.SmsEnabled ?? false,
                    pushEnabled = existing?.PushEnabled ?? false,
                    muted = existing?.Muted ?? false
                };
            })
        });
    }

    public record NotificationPrefDto(string Category, bool EmailEnabled, bool SmsEnabled, bool PushEnabled, bool Muted);
    public record SaveNotificationPrefsBody(List<NotificationPrefDto> Preferences);

    [HttpPut("api/notification-preferences")]
    public async Task<IActionResult> SaveNotificationPreferences([FromBody] SaveNotificationPrefsBody body)
    {
        if (body.Preferences != null)
        {
            foreach (var prefDto in body.Preferences)
            {
                var existing = await _db.NotificationPreferences
                    .FirstOrDefaultAsync(p => p.PersonId == Actor.PersonId && p.Category == prefDto.Category);

                if (existing == null)
                {
                    existing = new NotificationPreference
                    {
                        PersonId = Actor.PersonId,
                        Category = prefDto.Category
                    };
                    _db.NotificationPreferences.Add(existing);
                }

                existing.EmailEnabled = prefDto.EmailEnabled;
                existing.SmsEnabled = prefDto.SmsEnabled;
                existing.PushEnabled = prefDto.PushEnabled;
                existing.Muted = prefDto.Muted;
            }

            await _db.SaveChangesAsync();
        }

        return Ok(new { ok = true, message = "Notification preferences updated." });
    }
}
