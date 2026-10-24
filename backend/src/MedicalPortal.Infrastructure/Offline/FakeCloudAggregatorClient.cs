using System.Security.Cryptography;
using System.Text;

namespace MedicalPortal.Infrastructure.Offline;

/// <summary>
/// In-memory fake cloud aggregator client supporting network cut simulation,
/// transient 503 flapping, idempotency deduplication, and per-patient sequence verification.
/// </summary>
public class FakeCloudAggregatorClient : ICloudAggregatorClient
{
    private readonly object _lock = new();

    public bool IsOnline { get; set; } = true;
    public bool SimulateFlapping503 { get; set; } = false;
    public int InjectedNetworkDelayMs { get; set; } = 0;

    public List<AggregatorDispatchItemDto> ReceivedMessages { get; } = new();
    public HashSet<string> ProcessedIdempotencyKeys { get; } = new();
    public Dictionary<Guid, List<long>> PerPatientSequencesReceived { get; } = new();
    public List<ReferencePackageDto> AvailableReferencePackages { get; } = new();

    public FakeCloudAggregatorClient()
    {
        SeedDefaultReferencePackages();
    }

    public async Task<bool> PingAggregatorAsync(CancellationToken cancellationToken = default)
    {
        if (InjectedNetworkDelayMs > 0)
        {
            await Task.Delay(InjectedNetworkDelayMs, cancellationToken);
        }

        return IsOnline;
    }

    public async Task<AggregatorDispatchResultDto> DispatchMessageAsync(AggregatorDispatchItemDto item, CancellationToken cancellationToken = default)
    {
        if (InjectedNetworkDelayMs > 0)
        {
            await Task.Delay(InjectedNetworkDelayMs, cancellationToken);
        }

        if (!IsOnline)
        {
            return new AggregatorDispatchResultDto(
                item.IdempotencyKey,
                Success: false,
                AckId: null,
                StatusCode: 0,
                ErrorMessage: "Network unreachable: facility LAN operating in offline mode."
            );
        }

        if (SimulateFlapping503)
        {
            return new AggregatorDispatchResultDto(
                item.IdempotencyKey,
                Success: false,
                AckId: null,
                StatusCode: 503,
                ErrorMessage: "Aggregator service temporarily unavailable (503)."
            );
        }

        lock (_lock)
        {
            // Idempotency check
            if (ProcessedIdempotencyKeys.Contains(item.IdempotencyKey))
            {
                // Already processed: return 409 Conflict with known Ack
                return new AggregatorDispatchResultDto(
                    item.IdempotencyKey,
                    Success: false,
                    AckId: $"ACK-DEDUP-{item.IdempotencyKey}",
                    StatusCode: 409,
                    ErrorMessage: $"Idempotency conflict: event {item.IdempotencyKey} has already been recorded."
                );
            }

            ProcessedIdempotencyKeys.Add(item.IdempotencyKey);
            ReceivedMessages.Add(item);

            if (item.PatientId.HasValue)
            {
                if (!PerPatientSequencesReceived.TryGetValue(item.PatientId.Value, out var seqList))
                {
                    seqList = new List<long>();
                    PerPatientSequencesReceived[item.PatientId.Value] = seqList;
                }
                seqList.Add(item.PerPatientSequence);
            }

            return new AggregatorDispatchResultDto(
                item.IdempotencyKey,
                Success: true,
                AckId: $"ACK-{Guid.NewGuid():N}",
                StatusCode: 200,
                ErrorMessage: null
            );
        }
    }

    public async Task<AggregatorBatchResultDto> DispatchBatchAsync(IReadOnlyList<AggregatorDispatchItemDto> items, CancellationToken cancellationToken = default)
    {
        var results = new List<AggregatorDispatchResultDto>();
        bool overall = true;

        foreach (var item in items)
        {
            var res = await DispatchMessageAsync(item, cancellationToken);
            results.Add(res);
            if (!res.Success && res.StatusCode != 409)
            {
                overall = false;
            }
        }

        return new AggregatorBatchResultDto(overall, results);
    }

    public Task<IReadOnlyList<ReferencePackageDto>> FetchReferencePackagesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOnline || SimulateFlapping503)
        {
            throw new HttpRequestException("Aggregator unreachable for reference package sync.");
        }

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ReferencePackageDto>>(AvailableReferencePackages.ToList());
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            IsOnline = true;
            SimulateFlapping503 = false;
            InjectedNetworkDelayMs = 0;
            ReceivedMessages.Clear();
            ProcessedIdempotencyKeys.Clear();
            PerPatientSequencesReceived.Clear();
            AvailableReferencePackages.Clear();
            SeedDefaultReferencePackages();
        }
    }

    private void SeedDefaultReferencePackages()
    {
        var secret = "Ethiopia-Health-MOH-Reference-Signing-Secret-2026";
        var pkg1 = CreateSignedPackage(
            "ethiopia-moh-mfr",
            "Ethiopia Master Facility Registry (MFR)",
            "2026.1.0",
            100,
            "{\"facilities\":[{\"mfrId\":\"ETH-AA-001\",\"name\":\"Tikur Anbessa Specialized Hospital\"},{\"mfrId\":\"ETH-AA-002\",\"name\":\"St. Paul's Hospital Millennium Medical College\"}]}",
            secret
        );

        var pkg2 = CreateSignedPackage(
            "dhis2-hmis-definitions",
            "Ethiopia DHIS2 HMIS Indicator Data Sets",
            "2026.1.0",
            100,
            "{\"dataSets\":[{\"id\":\"HMIS-MOH-MONTHLY\",\"name\":\"HMIS Monthly Service Delivery Summary\"}]}",
            secret
        );

        AvailableReferencePackages.Add(pkg1);
        AvailableReferencePackages.Add(pkg2);
    }

    public static ReferencePackageDto CreateSignedPackage(string packageId, string name, string version, int versionOrdinal, string payloadJson, string secret)
    {
        var contentHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        var contentHash = Convert.ToHexString(contentHashBytes).ToLowerInvariant();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{packageId}:{versionOrdinal}:{contentHash}"));
        var signature = Convert.ToHexString(sigBytes).ToLowerInvariant();

        return new ReferencePackageDto(
            PackageId: packageId,
            Name: name,
            Version: version,
            VersionOrdinal: versionOrdinal,
            PayloadJson: payloadJson,
            ContentHash: contentHash,
            Signature: signature,
            SignedBy: "Ethiopian Ministry of Health - HIT Directorate",
            SignedAtUtc: DateTime.UtcNow
        );
    }
}
