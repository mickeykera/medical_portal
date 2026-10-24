using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Hms;

public class HmsDeadLetterService : IHmsDeadLetterService
{
    private readonly MedicalPortalDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<HmsDeadLetterService> _logger;

    public HmsDeadLetterService(
        MedicalPortalDbContext db,
        IConfiguration config,
        ILogger<HmsDeadLetterService>? logger = null)
    {
        _db = db;
        _config = config;
        _logger = logger ?? NullLogger<HmsDeadLetterService>.Instance;
    }

    public async Task<HmsDeadLetterRecord> RecordFailureAsync(
        string eventId,
        string source,
        string error,
        string payloadJson,
        string? hmsMrn = null,
        string? entityType = null,
        string? entityId = null,
        int attemptCount = 1,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.HmsDeadLetterRecords
            .FirstOrDefaultAsync(d => d.EventId == eventId, cancellationToken);

        if (existing != null)
        {
            existing.AttemptCount += attemptCount;
            existing.LastAttemptAt = DateTime.UtcNow;
            existing.Error = error;
            existing.Status = HmsDeadLetterStatus.Retrying;
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var record = new HmsDeadLetterRecord
        {
            EventId = eventId,
            Source = source,
            HmsMrn = hmsMrn,
            EntityType = entityType,
            EntityId = entityId,
            Error = error,
            AttemptCount = attemptCount,
            FirstFailedAt = DateTime.UtcNow,
            LastAttemptAt = DateTime.UtcNow,
            Status = HmsDeadLetterStatus.Pending,
            PayloadJson = payloadJson
        };

        _db.HmsDeadLetterRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Recorded HMS dead-letter record for event {EventId} (MRN: {HmsMrn}, Entity: {EntityType}): {Error}",
            eventId, hmsMrn, entityType, error);

        return record;
    }

    public async Task<List<HmsDeadLetterRecord>> GetDeadLettersAsync(
        HmsDeadLetterStatus? status = null,
        int page = 1,
        int pageSize = 50,
        Guid? actorUserAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.HmsDeadLetterRecords.AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        var results = await query
            .OrderByDescending(d => d.LastAttemptAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Audit view of dead letters (PHI security compliance)
        if (actorUserAccountId.HasValue)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "HMS_DEAD_LETTER_VIEWED",
                ResourceType = "HmsDeadLetterRecord",
                ActorUserAccountId = actorUserAccountId.Value,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    page,
                    pageSize,
                    status = status?.ToString(),
                    count = results.Count
                })
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return results;
    }

    public async Task<HmsDeadLetterRecord?> GetDeadLetterByIdAsync(
        Guid id,
        Guid? actorUserAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.HmsDeadLetterRecords
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (record != null && actorUserAccountId.HasValue)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "HMS_DEAD_LETTER_DETAIL_VIEWED",
                ResourceType = "HmsDeadLetterRecord",
                ResourceId = id.ToString(),
                ActorUserAccountId = actorUserAccountId.Value,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    deadLetterId = id,
                    eventId = record.EventId,
                    hmsMrn = record.HmsMrn,
                    entityType = record.EntityType
                })
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return record;
    }

    public async Task<bool> ResolveDeadLetterAsync(
        Guid id,
        HmsDeadLetterStatus newStatus,
        Guid actorUserAccountId,
        string? resolutionNotes = null,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.HmsDeadLetterRecords
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (record == null)
            return false;

        record.Status = newStatus;
        record.ResolutionNotes = resolutionNotes;
        record.ResolvedByUserAccountId = actorUserAccountId;
        record.ResolvedAt = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "HMS_DEAD_LETTER_RESOLVED",
            ResourceType = "HmsDeadLetterRecord",
            ResourceId = id.ToString(),
            ActorUserAccountId = actorUserAccountId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                deadLetterId = id,
                newStatus = newStatus.ToString(),
                resolutionNotes
            })
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> PurgeExpiredRecordsAsync(
        int? retentionDays = null,
        CancellationToken cancellationToken = default)
    {
        var days = retentionDays ?? _config.GetValue<int>("Hms:DeadLetterRetentionDays", 90);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var expired = await _db.HmsDeadLetterRecords
            .Where(d => d.LastAttemptAt < cutoff && (d.Status == HmsDeadLetterStatus.Resolved || d.Status == HmsDeadLetterStatus.Discarded))
            .ToListAsync(cancellationToken);

        if (expired.Count > 0)
        {
            _db.HmsDeadLetterRecords.RemoveRange(expired);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Purged {Count} expired HMS dead-letter records older than {Days} days.", expired.Count, days);
        }

        return expired.Count;
    }
}
