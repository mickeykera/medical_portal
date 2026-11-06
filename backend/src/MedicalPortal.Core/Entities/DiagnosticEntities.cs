namespace MedicalPortal.Core.Entities;

public enum DiagnosticService
{
    LABORATORY,
    IMAGING,
    PHYSIOLOGY,
    PATHOLOGY
}

public enum DiagnosticWorkStatus
{
    QUEUED,
    COLLECTED,
    PERFORMED,
    PRELIMINARY,
    FINAL,
    CANCELLED
}

public class DiagnosticWorkItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ClinicalOrderId { get; set; }
    public Guid PatientId { get; set; }
    public DiagnosticService Service { get; set; }
    public DiagnosticWorkStatus Status { get; set; } = DiagnosticWorkStatus.QUEUED;
    public string? AccessionNumber { get; set; }
    public string? SpecimenType { get; set; }
    public string? Modality { get; set; }
    public string? PacsStudyUid { get; set; }
    public bool PregnancySafetyAcknowledged { get; set; }
    public string? ResultText { get; set; }
    public bool IsCritical { get; set; }
    public string? CriticalCallbackNote { get; set; }
    public Guid CreatedByUserAccountId { get; set; }
    public Guid? PerformedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PerformedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public virtual ClinicalOrder ClinicalOrder { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
}
