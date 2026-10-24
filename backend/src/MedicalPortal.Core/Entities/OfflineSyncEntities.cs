using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

/// <summary>
/// Transactional outbox message for store-and-forward synchronization from a facility LAN server
/// to regional/cloud aggregators. Persisted within the same database transaction as business operations.
/// </summary>
public class SyncOutboxMessage : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;

    /// <summary>
    /// Associated patient identifier. Messages with the same PatientId must be delivered in strict FIFO order.
    /// Null for tenant-level or facility-wide administrative events.
    /// </summary>
    public Guid? PatientId { get; set; }

    /// <summary>
    /// Event name, e.g. "Patient.Created", "Appointment.Booked", "Observation.Recorded", "Form.Submitted".
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// FHIR R4 resource or domain type, e.g. "Patient", "Appointment", "Observation", "Bundle".
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Serialized FHIR R4 JSON or structured domain event payload.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// Deterministic idempotency key ensuring aggregator deduplication.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Global monotonic sequence number within the tenant.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Per-patient sequence number ensuring strict FIFO order across dispatches for a single patient.
    /// </summary>
    public long PerPatientSequence { get; set; }

    /// <summary>
    /// Processing lifecycle status.
    /// </summary>
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    /// <summary>
    /// Number of delivery attempts made so far.
    /// </summary>
    public int AttemptCount { get; set; } = 0;

    /// <summary>
    /// Maximum retry attempts before transitioning to DeadLetter status.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// UTC timestamp when message was created in the local outbox.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Next scheduled delivery attempt time (computed via exponential backoff with jitter on failures).
    /// </summary>
    public DateTime ScheduledAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when delivery was confirmed by the aggregator.
    /// </summary>
    public DateTime? DeliveredAtUtc { get; set; }

    /// <summary>
    /// Error message from the most recent failed dispatch attempt.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Confirmation / transaction ID returned by the aggregator.
    /// </summary>
    public string? AggregatorAckId { get; set; }

    public virtual Patient? Patient { get; set; }
}

/// <summary>
/// Signed and versioned reference data package (terminology, facility directory, DHIS2 indicators, form templates)
/// distributed to offline facility servers.
/// </summary>
public class ReferencePackage : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;

    /// <summary>
    /// Unique package identifier, e.g. "ethiopia-moh-mfr", "dhis2-hmis-definitions", "terminology-icd10".
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable package display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Semantic version string, e.g. "2026.1.0".
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Monotonically increasing version ordinal used to prevent downgrade attacks.
    /// </summary>
    public int VersionOrdinal { get; set; }

    /// <summary>
    /// JSON content of the reference package.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of PayloadJson.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Cryptographic signature of the content hash.
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Identity of the signing authority, e.g. "Ethiopian Ministry of Health".
    /// </summary>
    public string SignedBy { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the package was signed by the authority.
    /// </summary>
    public DateTime SignedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the package was installed locally.
    /// </summary>
    public DateTime InstalledAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Package lifecycle status.
    /// </summary>
    public ReferencePackageStatus Status { get; set; } = ReferencePackageStatus.Installed;

    /// <summary>
    /// Indicates whether this package is currently active for lookups and lookahead validation.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
