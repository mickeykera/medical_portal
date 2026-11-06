using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class Tariff : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ClinicalOrderType? OrderType { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "ETB";
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ChargeItemStatus
{
    OPEN,
    PAID,
    WAIVED,
    VOID
}

public class ChargeItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid? EncounterId { get; set; }
    public Guid? ClinicalOrderId { get; set; }
    public Guid? TariffId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "ETB";
    public ChargeItemStatus Status { get; set; } = ChargeItemStatus.OPEN;
    public string? WaiverReason { get; set; }
    public Guid? WaivedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
    public virtual ClinicalOrder? ClinicalOrder { get; set; }
    public virtual Tariff? Tariff { get; set; }
}

public class Receipt : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid? PaymentId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "ETB";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
    public virtual Payment? Payment { get; set; }
}
