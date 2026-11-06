using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/compliance")]
[RequireAuth]
[RequireAdmin]
[EnableRateLimiting("GeneralApiPolicy")]
public class ComplianceController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;

    public ComplianceController(MedicalPortalDbContext db)
    {
        _db = db;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var recent = _db.AuditLogs.AsNoTracking().Where(a => a.CreatedAt >= since);

        var total = await recent.CountAsync();
        var proxyAccesses = await recent.CountAsync(a => a.IsProxy);
        var denied = await recent.CountAsync(a =>
            a.Action.Contains("DENIED") || a.Action.Contains("FORBIDDEN"));
        var exports = await recent.CountAsync(a =>
            a.Action.Contains("EXPORT") || a.Action.Contains("PRINT"));
        var billingOverrides = await recent.CountAsync(a =>
            a.Action.Contains("WAIVED") || a.Action.Contains("BILL_LATER") ||
            a.Action.Contains("REFUND") || a.Action.Contains("OVERRIDE"));

        return Ok(new
        {
            periodStart = since,
            periodEnd = DateTime.UtcNow,
            totalAuditEvents = total,
            proxyAccesses,
            deniedAccesses = denied,
            exportsAndPrints = exports,
            billingIntegritySignals = billingOverrides
        });
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? patientId = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool? isProxy = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (patientId.HasValue)
        {
            query = query.Where(a => a.PatientId == patientId.Value);
        }

        if (actorId.HasValue)
        {
            query = query.Where(a => a.ActorUserAccountId == actorId.Value);
        }

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var rType = resourceType.Trim().ToLower();
            query = query.Where(a => a.ResourceType.ToLower() == rType);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var act = action.Trim().ToLower();
            query = query.Where(a => a.Action.ToLower() == act);
        }

        if (from.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= to.Value);
        }

        if (isProxy.HasValue)
        {
            query = query.Where(a => a.IsProxy == isProxy.Value);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var logs = await query
            .OrderByDescending(a => a.SequenceNumber)
            .ThenByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var actorIds = logs
            .Where(l => l.ActorUserAccountId.HasValue)
            .Select(l => l.ActorUserAccountId!.Value)
            .Distinct()
            .ToList();

        var actors = await _db.UserAccounts
            .AsNoTracking()
            .Include(u => u.Person)
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var items = logs.Select(l =>
        {
            var actor = l.ActorUserAccountId.HasValue ? actors.GetValueOrDefault(l.ActorUserAccountId.Value) : null;
            var actorName = actor?.Person != null ? $"{actor.Person.FirstName} {actor.Person.LastName}".Trim() : "System";

            return new
            {
                id = l.Id,
                action = l.Action,
                resourceType = l.ResourceType,
                resourceId = l.ResourceId,
                actor = new
                {
                    id = l.ActorUserAccountId,
                    name = actorName,
                    email = actor?.Email,
                    role = actor?.Role.ToString() ?? "SYSTEM"
                },
                patientId = l.PatientId,
                actingForPersonId = l.ActingForPersonId,
                isProxy = l.IsProxy,
                ipAddress = l.IpAddress,
                userAgent = l.UserAgent,
                createdAt = l.CreatedAt.ToString("o"),
                previousHash = l.PreviousHash,
                hash = l.Hash,
                metadataJson = l.MetadataJson
            };
        });

        return Ok(new
        {
            page,
            pageSize,
            totalCount,
            totalPages,
            items
        });
    }

    [HttpGet("audit-logs/verify")]
    public async Task<IActionResult> VerifyAuditChain()
    {
        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => !string.IsNullOrEmpty(a.Hash))
            .OrderBy(a => a.SequenceNumber)
            .ToListAsync();

        var isValid = AuditLogHasher.VerifyIntegrity(logs, out var corruptedId, out var reason);

        return Ok(new
        {
            valid = isValid,
            totalRecords = logs.Count,
            genesisHash = AuditLogHasher.GenesisHash,
            latestHash = logs.LastOrDefault()?.Hash,
            corruptedLogId = corruptedId,
            error = reason
        });
    }
}
