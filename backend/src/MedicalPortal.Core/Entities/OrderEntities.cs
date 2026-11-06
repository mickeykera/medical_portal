using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class ClinicalOrder : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid? EncounterId { get; set; }
    public Guid OrderingUserAccountId { get; set; }
    public Guid? PerformingDepartmentId { get; set; }
    public ClinicalOrderType Type { get; set; }
    public OrderPriority Priority { get; set; } = OrderPriority.ROUTINE;
    public ClinicalOrderStatus Status { get; set; } = ClinicalOrderStatus.DRAFT;
    public PaymentGateState PaymentGate { get; set; } = PaymentGateState.NOT_REQUIRED;
    public string? ClinicalIndication { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? ChargeAmountCents { get; set; }
    public Guid? TariffId { get; set; }
    public string? GateExceptionReason { get; set; }
    public Guid? GateApprovedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public virtual Patient Patient { get; set; } = null!;
    public virtual Encounter? Encounter { get; set; }
    public virtual Department? PerformingDepartment { get; set; }
    public virtual Tariff? Tariff { get; set; }
}
