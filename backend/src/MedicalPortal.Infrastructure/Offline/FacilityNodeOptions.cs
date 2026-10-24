using MedicalPortal.Core.Enums;

namespace MedicalPortal.Infrastructure.Offline;

public class FacilityNodeOptions
{
    public const string SectionName = "FacilityNode";

    /// <summary>
    /// Node role: Facility (authoritative local system of record on LAN) or RegionalCloud (aggregator replica).
    /// </summary>
    public DeploymentNodeRole NodeRole { get; set; } = DeploymentNodeRole.Facility;

    /// <summary>
    /// Identifier or code of this facility server (e.g. MFR code or local facility slug).
    /// </summary>
    public string FacilityCode { get; set; } = "ADOT-01";

    /// <summary>
    /// Display name of this facility server.
    /// </summary>
    public string FacilityName { get; set; } = "Adot Internal Medicine - Facility Server";

    /// <summary>
    /// Regional or national aggregator HTTPS endpoint.
    /// </summary>
    public string AggregatorEndpoint { get; set; } = "https://aggregator.health.gov.et/api/sync";

    /// <summary>
    /// Authentication token or API key presented to the aggregator.
    /// </summary>
    public string AggregatorApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Background sync polling interval in seconds.
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of messages dispatched in a single batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>
    /// Initial exponential backoff delay in seconds on transient network failure.
    /// </summary>
    public int InitialBackoffSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum exponential backoff cap in seconds (e.g. 1 hour).
    /// </summary>
    public int MaxBackoffSeconds { get; set; } = 3600;

    /// <summary>
    /// Maximum delivery attempts before moving a message to DeadLetter status.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// Shared HMAC-SHA256 secret or public key used to verify signed reference data packages.
    /// </summary>
    public string ReferencePackageSecret { get; set; } = "Ethiopia-Health-MOH-Reference-Signing-Secret-2026";

    /// <summary>
    /// If true, uses the fake in-memory aggregator client for test environments and offline verification.
    /// </summary>
    public bool UseFakeAggregator { get; set; } = false;
}
