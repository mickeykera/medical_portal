using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Infrastructure.Interoperability.Mpi;

public record DuplicateCandidateDto(
    Guid Id,
    Guid PrimaryPatientId,
    string PrimaryPatientMrn,
    string PrimaryPatientName,
    Guid CandidateDuplicatePatientId,
    string CandidateDuplicateMrn,
    string CandidateDuplicateName,
    decimal ConfidenceScore,
    string MatchReasonsJson,
    string Status,
    DateTime DetectedAtUtc
);

public interface IPatientDeduplicationService
{
    Task<int> ScanForDuplicatesAsync(CancellationToken cancellationToken = default);

    Task<bool> ResolveDuplicateAsync(
        Guid candidateId,
        DuplicateResolutionStatus resolution,
        Guid resolvedByUserId,
        string? resolutionNotes = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<DuplicateCandidateDto>> GetPendingCandidatesAsync(CancellationToken cancellationToken = default);
}
