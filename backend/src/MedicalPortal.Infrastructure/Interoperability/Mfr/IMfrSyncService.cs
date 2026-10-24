namespace MedicalPortal.Infrastructure.Interoperability.Mfr;

public record MfrSyncResultDto(
    int TotalDiscovered,
    int ReconciledFacilities,
    int UnmatchedCount
);

public interface IMfrSyncService
{
    Task<MfrSyncResultDto> SyncFacilitiesWithMfrAsync(CancellationToken cancellationToken = default);
}
