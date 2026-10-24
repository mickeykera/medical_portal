using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Infrastructure.Hms;

public interface IHmsDeadLetterService
{
    Task<HmsDeadLetterRecord> RecordFailureAsync(
        string eventId,
        string source,
        string error,
        string payloadJson,
        string? hmsMrn = null,
        string? entityType = null,
        string? entityId = null,
        int attemptCount = 1,
        CancellationToken cancellationToken = default);

    Task<List<HmsDeadLetterRecord>> GetDeadLettersAsync(
        HmsDeadLetterStatus? status = null,
        int page = 1,
        int pageSize = 50,
        Guid? actorUserAccountId = null,
        CancellationToken cancellationToken = default);

    Task<HmsDeadLetterRecord?> GetDeadLetterByIdAsync(
        Guid id,
        Guid? actorUserAccountId = null,
        CancellationToken cancellationToken = default);

    Task<bool> ResolveDeadLetterAsync(
        Guid id,
        HmsDeadLetterStatus newStatus,
        Guid actorUserAccountId,
        string? resolutionNotes = null,
        CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredRecordsAsync(
        int? retentionDays = null,
        CancellationToken cancellationToken = default);
}
