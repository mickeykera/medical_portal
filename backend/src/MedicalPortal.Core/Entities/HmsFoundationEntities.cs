using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class Encounter : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? BedId { get; set; }
    public PatientClass PatientClass { get; set; } = PatientClass.OUTPATIENT;
    public EncounterStatus Status { get; set; } = EncounterStatus.PLANNED;
    public string? Reason { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public string? ClassificationReason { get; set; }
    public Guid? ClassifiedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
    public virtual Location? Location { get; set; }
    public virtual Bed? Bed { get; set; }
}

public class Bed : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid LocationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Room { get; set; }
    public BedState State { get; set; } = BedState.AVAILABLE;
    public bool IsActive { get; set; } = true;
    public Guid? CurrentEncounterId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Location Location { get; set; } = null!;
}
