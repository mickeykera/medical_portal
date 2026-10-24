using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedicalPortal.Infrastructure.Offline;

public class OutboxSyncDispatcher : IOutboxSyncDispatcher
{
    private readonly MedicalPortalDbContext _db;
    private readonly ICloudAggregatorClient _aggregatorClient;
    private readonly IOptions<FacilityNodeOptions> _options;
    private readonly ILogger<OutboxSyncDispatcher> _logger;
    private static readonly SemaphoreSlim _dispatchLock = new(1, 1);

    public OutboxSyncDispatcher(
        MedicalPortalDbContext db,
        ICloudAggregatorClient aggregatorClient,
        IOptions<FacilityNodeOptions> options,
        ILogger<OutboxSyncDispatcher>? logger = null)
    {
        _db = db;
        _aggregatorClient = aggregatorClient;
        _options = options;
        _logger = logger ?? NullLogger<OutboxSyncDispatcher>.Instance;
    }

    public async Task<SyncOutboxMessage> EnqueueMessageAsync(
        string eventType,
        string resourceType,
        string payloadJson,
        Guid? patientId = null,
        string? customIdempotencyKey = null,
        bool saveImmediately = true,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _db.CurrentTenantId ?? TenantConstants.DefaultTenantId;

        // Sequence numbers
        long perPatientSeq = 1;
        if (patientId.HasValue)
        {
            var lastPatientSeq = await _db.OutboxMessages
                .Where(m => m.PatientId == patientId.Value)
                .OrderByDescending(m => m.PerPatientSequence)
                .Select(m => m.PerPatientSequence)
                .FirstOrDefaultAsync(cancellationToken);
            perPatientSeq = lastPatientSeq + 1;
        }

        var lastGlobalSeq = await _db.OutboxMessages
            .OrderByDescending(m => m.SequenceNumber)
            .Select(m => m.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        long globalSeq = lastGlobalSeq + 1;

        var message = new SyncOutboxMessage
        {
            TenantId = tenantId,
            PatientId = patientId,
            EventType = eventType,
            ResourceType = resourceType,
            PayloadJson = payloadJson,
            IdempotencyKey = !string.IsNullOrWhiteSpace(customIdempotencyKey)
                ? customIdempotencyKey
                : $"evt-{tenantId:N}-{Guid.NewGuid():N}",
            SequenceNumber = globalSeq,
            PerPatientSequence = perPatientSeq,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = _options.Value.MaxAttempts,
            CreatedAtUtc = DateTime.UtcNow,
            ScheduledAtUtc = DateTime.UtcNow
        };

        _db.OutboxMessages.Add(message);

        if (saveImmediately)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return message;
    }

    public async Task<OutboxDispatchReportDto> DispatchPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        await _dispatchLock.WaitAsync(cancellationToken);
        try
        {
            var isOnline = await _aggregatorClient.PingAggregatorAsync(cancellationToken);
            if (!isOnline)
            {
                var currentDepth = await _db.OutboxMessages
                    .CountAsync(m => m.Status == OutboxMessageStatus.Pending || m.Status == OutboxMessageStatus.FailedTransient, cancellationToken);
                return new OutboxDispatchReportDto(0, 0, 0, 0, currentDepth, false);
            }

            var now = DateTime.UtcNow;
            var maxBatch = _options.Value.MaxBatchSize;

            // Blocked patient IDs: any patient with a message currently in DeadLetter,
            // or in FailedTransient whose ScheduledAtUtc has not arrived yet.
            var blockedPatientIds = await _db.OutboxMessages
                .Where(m => m.PatientId != null &&
                           (m.Status == OutboxMessageStatus.DeadLetter ||
                            (m.Status == OutboxMessageStatus.FailedTransient && m.ScheduledAtUtc > now)))
                .Select(m => m.PatientId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            var blockedSet = new HashSet<Guid>(blockedPatientIds);

            // Fetch candidates ordered by SequenceNumber (preserving chronological order)
            var candidates = await _db.OutboxMessages
                .Where(m => m.Status == OutboxMessageStatus.Pending ||
                           (m.Status == OutboxMessageStatus.FailedTransient && m.ScheduledAtUtc <= now))
                .OrderBy(m => m.SequenceNumber)
                .Take(maxBatch * 2) // take surplus to allow skipping blocked patients
                .ToListAsync(cancellationToken);

            int processed = 0;
            int delivered = 0;
            int failed = 0;
            int deadLettered = 0;

            var patientsFailedInThisCycle = new HashSet<Guid>();

            foreach (var msg in candidates)
            {
                if (processed >= maxBatch) break;

                // Per-patient FIFO barrier
                if (msg.PatientId.HasValue)
                {
                    var pid = msg.PatientId.Value;
                    if (blockedSet.Contains(pid) || patientsFailedInThisCycle.Contains(pid))
                    {
                        // Patient is blocked; do not send subsequent events out of order
                        continue;
                    }
                }

                processed++;
                msg.AttemptCount++;

                var dispatchItem = new AggregatorDispatchItemDto(
                    IdempotencyKey: msg.IdempotencyKey,
                    EventType: msg.EventType,
                    ResourceType: msg.ResourceType,
                    PayloadJson: msg.PayloadJson,
                    PatientId: msg.PatientId,
                    PerPatientSequence: msg.PerPatientSequence
                );

                var result = await _aggregatorClient.DispatchMessageAsync(dispatchItem, cancellationToken);

                if (result.Success)
                {
                    msg.Status = OutboxMessageStatus.Delivered;
                    msg.DeliveredAtUtc = DateTime.UtcNow;
                    msg.AggregatorAckId = result.AckId;
                    msg.LastError = null;
                    delivered++;
                }
                else if (result.StatusCode == 409)
                {
                    // Aggregator already has this message recorded (idempotency deduplication)
                    msg.Status = OutboxMessageStatus.Delivered;
                    msg.DeliveredAtUtc = DateTime.UtcNow;
                    msg.AggregatorAckId = result.AckId ?? $"ACK-DEDUP-{msg.IdempotencyKey}";
                    msg.LastError = "Deduplicated by aggregator (409).";
                    delivered++;
                }
                else
                {
                    // Failure: transient network / 5xx or unrecoverable 4xx
                    failed++;
                    if (msg.PatientId.HasValue)
                    {
                        patientsFailedInThisCycle.Add(msg.PatientId.Value);
                    }

                    bool isFatal = result.StatusCode is >= 400 and < 500 and not 408;

                    if (isFatal || msg.AttemptCount >= msg.MaxAttempts)
                    {
                        msg.Status = OutboxMessageStatus.DeadLetter;
                        msg.LastError = isFatal
                            ? $"Fatal aggregator rejection ({result.StatusCode}): {result.ErrorMessage}"
                            : $"Exceeded maximum attempts ({msg.MaxAttempts}): {result.ErrorMessage}";
                        deadLettered++;
                    }
                    else
                    {
                        msg.Status = OutboxMessageStatus.FailedTransient;
                        msg.ScheduledAtUtc = ComputeNextRetry(msg.AttemptCount);
                        msg.LastError = $"Attempt {msg.AttemptCount} failed ({result.StatusCode}): {result.ErrorMessage}";
                    }
                }
            }

            if (processed > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            var remainingDepth = await _db.OutboxMessages
                .CountAsync(m => m.Status == OutboxMessageStatus.Pending || m.Status == OutboxMessageStatus.FailedTransient, cancellationToken);

            return new OutboxDispatchReportDto(processed, delivered, failed, deadLettered, remainingDepth, true);
        }
        finally
        {
            _dispatchLock.Release();
        }
    }

    public async Task<OutboxStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Pending, cancellationToken);
        var delivered = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Delivered, cancellationToken);
        var failedTransient = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.FailedTransient, cancellationToken);
        var deadLetter = await _db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.DeadLetter, cancellationToken);
        var lastDelivered = await _db.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Delivered && m.DeliveredAtUtc != null)
            .OrderByDescending(m => m.DeliveredAtUtc)
            .Select(m => m.DeliveredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var reachable = await _aggregatorClient.PingAggregatorAsync(cancellationToken);

        return new OutboxStatusDto(
            PendingCount: pending,
            DeliveredCount: delivered,
            FailedTransientCount: failedTransient,
            DeadLetterCount: deadLetter,
            TotalDepth: pending + failedTransient,
            LastDeliveredAtUtc: lastDelivered,
            AggregatorReachable: reachable
        );
    }

    private DateTime ComputeNextRetry(int attemptCount)
    {
        var initial = _options.Value.InitialBackoffSeconds;
        var max = _options.Value.MaxBackoffSeconds;
        var exponent = Math.Max(0, attemptCount - 1);
        var delay = Math.Min(max, initial * Math.Pow(2, exponent));

        // Jitter +/- 10%
        var jitter = 0.9 + (Random.Shared.NextDouble() * 0.2);
        var finalDelay = delay * jitter;
        return DateTime.UtcNow.AddSeconds(finalDelay);
    }
}
