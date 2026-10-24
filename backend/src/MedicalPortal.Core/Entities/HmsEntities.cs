using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

/// <summary>
/// Dead-letter store for failed HMS synchronizations, webhook dispatches, and push events.
/// Contains payload JSON treated as sensitive PHI (restricted access and audited).
/// </summary>
public class HmsDeadLetterRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string EventId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "HMS_WEBHOOK", "HMS_SYNC_WORKER", "APPOINTMENT_PUSH", "REFILL_SYNC"
    public string? HmsMrn { get; set; }
    public string? EntityType { get; set; } // "Patient", "Appointment", "RefillRequest", "LabTest", etc.
    public string? EntityId { get; set; }
    public string Error { get; set; } = string.Empty;
    public int AttemptCount { get; set; } = 1;
    public DateTime FirstFailedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;
    public HmsDeadLetterStatus Status { get; set; } = HmsDeadLetterStatus.Pending;
    public string PayloadJson { get; set; } = string.Empty; // Sensitive PHI payload
    public string? ResolutionNotes { get; set; }
    public Guid? ResolvedByUserAccountId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public virtual UserAccount? ResolvedByUserAccount { get; set; }
}

/// <summary>
/// Persisted record of demographic conflicts between Portal local data and HMS.
/// Created only when both sides changed since the last known sync.
/// </summary>
public class HmsDemographicConflict : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public string HmsMrn { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty; // "FirstName", "LastName", "DateOfBirth", "AddressLine1", "City", etc.
    public string? LocalValue { get; set; }
    public string? HmsValue { get; set; }
    public string? LastSyncedValue { get; set; }
    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
    public HmsConflictStatus Status { get; set; } = HmsConflictStatus.Pending;
    public Guid? ResolvedByUserAccountId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolutionNotes { get; set; }

    public virtual Patient Patient { get; set; } = null!;
    public virtual UserAccount? ResolvedByUserAccount { get; set; }
}

/// <summary>
/// Idempotency deduplication registry for received HMS webhook events.
/// </summary>
public class HmsProcessedWebhookEvent : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? HmsMrn { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
