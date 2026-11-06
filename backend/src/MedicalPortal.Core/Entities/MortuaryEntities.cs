namespace MedicalPortal.Core.Entities;

public enum MortuaryCaseStatus
{
    DECLARED,
    INTAKE,
    STORED,
    RELEASED,
    CLOSED
}

public enum MortuaryCustodyAction
{
    RECEIVED,
    MOVED,
    RELEASED,
    SEALED,
    UNSEALED
}

public class MortuaryCase : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid? EncounterId { get; set; }
    public MortuaryCaseStatus Status { get; set; } = MortuaryCaseStatus.DECLARED;
    public DateTime DeathDeclaredAt { get; set; } = DateTime.UtcNow;
    public Guid DeclaredByUserAccountId { get; set; }
    public string? DeathCertificateReference { get; set; }
    public string? TagNumber { get; set; }
    public string? StorageLocation { get; set; }
    public bool ForensicFlag { get; set; }
    public bool InfectiousFlag { get; set; }
    public string? ReleaseAuthorizationReference { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public virtual Patient Patient { get; set; } = null!;
}

public class MortuaryCustodyEvent : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid MortuaryCaseId { get; set; }
    public MortuaryCustodyAction Action { get; set; }
    public string? FromLocation { get; set; }
    public string? ToLocation { get; set; }
    public string? IdentityVerificationReference { get; set; }
    public Guid RecordedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public virtual MortuaryCase MortuaryCase { get; set; } = null!;
}
