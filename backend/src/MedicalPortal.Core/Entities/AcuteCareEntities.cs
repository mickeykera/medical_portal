namespace MedicalPortal.Core.Entities;

public enum AcuteCareUnit
{
    EMERGENCY,
    ICU,
    OPERATING_THEATRE,
    DELIVERY_SUITE,
    NICU
}

public enum AcuteCareStatus
{
    TRIAGED,
    ACTIVE,
    TRANSFER_REQUESTED,
    TRANSFERRED,
    COMPLETED,
    CANCELLED
}

public enum TriageAcuity
{
    RESUSCITATION,
    EMERGENCY,
    URGENT,
    LESS_URGENT,
    NON_URGENT
}

public enum AcuteBookingStatus
{
    REQUESTED,
    APPROVED,
    COMPLETED,
    CANCELLED
}

public enum EmergencyDisposition
{
    DISCHARGED,
    ADMITTED_TO_WARD,
    TRANSFERRED_TO_ICU,
    REFERRED,
    SENT_TO_OPERATING_THEATRE,
    DECEASED
}

public class AcuteCareEpisode : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid EncounterId { get; set; }
    public AcuteCareUnit Unit { get; set; }
    public AcuteCareStatus Status { get; set; } = AcuteCareStatus.TRIAGED;
    public TriageAcuity Acuity { get; set; }
    public DateTime ArrivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TriagedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ChiefComplaint { get; set; }
    public string? Notes { get; set; }
    public EmergencyDisposition? Disposition { get; set; }
    public string? DispositionReason { get; set; }
    public Guid CreatedByUserAccountId { get; set; }
    public virtual Patient Patient { get; set; } = null!;
    public virtual Encounter Encounter { get; set; } = null!;
}

public class AcuteCareTransferRequest : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid EpisodeId { get; set; }
    public AcuteCareUnit TargetUnit { get; set; }
    public AcuteBookingStatus Status { get; set; } = AcuteBookingStatus.REQUESTED;
    public string Reason { get; set; } = string.Empty;
    public Guid RequestedByUserAccountId { get; set; }
    public Guid? ApprovedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public virtual AcuteCareEpisode Episode { get; set; } = null!;
}

public class AcuteCareBooking : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid EpisodeId { get; set; }
    public AcuteCareUnit Unit { get; set; }
    public AcuteBookingStatus Status { get; set; } = AcuteBookingStatus.REQUESTED;
    public DateTime? ScheduledAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid RequestedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public virtual AcuteCareEpisode Episode { get; set; } = null!;
}
