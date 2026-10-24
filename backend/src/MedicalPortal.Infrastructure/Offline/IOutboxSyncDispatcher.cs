using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Offline;

public record OutboxDispatchReportDto(
    int Processed,
    int Delivered,
    int Failed,
    int DeadLettered,
    int RemainingQueueDepth,
    bool LinkAvailable
);

public record OutboxStatusDto(
    int PendingCount,
    int DeliveredCount,
    int FailedTransientCount,
    int DeadLetterCount,
    int TotalDepth,
    DateTime? LastDeliveredAtUtc,
    bool AggregatorReachable
);

public interface IOutboxSyncDispatcher
{
    Task<SyncOutboxMessage> EnqueueMessageAsync(
        string eventType,
        string resourceType,
        string payloadJson,
        Guid? patientId = null,
        string? customIdempotencyKey = null,
        bool saveImmediately = true,
        CancellationToken cancellationToken = default
    );

    Task<OutboxDispatchReportDto> DispatchPendingMessagesAsync(CancellationToken cancellationToken = default);

    Task<OutboxStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
