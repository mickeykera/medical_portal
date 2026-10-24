namespace MedicalPortal.Infrastructure.Offline;

public record AggregatorDispatchItemDto(
    string IdempotencyKey,
    string EventType,
    string ResourceType,
    string PayloadJson,
    Guid? PatientId,
    long PerPatientSequence
);

public record AggregatorDispatchResultDto(
    string IdempotencyKey,
    bool Success,
    string? AckId,
    int StatusCode,
    string? ErrorMessage
);

public record AggregatorBatchResultDto(
    bool OverallSuccess,
    IReadOnlyList<AggregatorDispatchResultDto> Results
);

public record ReferencePackageDto(
    string PackageId,
    string Name,
    string Version,
    int VersionOrdinal,
    string PayloadJson,
    string ContentHash,
    string Signature,
    string SignedBy,
    DateTime SignedAtUtc
);

/// <summary>
/// Client abstraction for store-and-forward HTTPS communication with regional or national aggregators.
/// </summary>
public interface ICloudAggregatorClient
{
    Task<AggregatorDispatchResultDto> DispatchMessageAsync(AggregatorDispatchItemDto item, CancellationToken cancellationToken = default);
    Task<AggregatorBatchResultDto> DispatchBatchAsync(IReadOnlyList<AggregatorDispatchItemDto> items, CancellationToken cancellationToken = default);
    Task<bool> PingAggregatorAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReferencePackageDto>> FetchReferencePackagesAsync(CancellationToken cancellationToken = default);
}
