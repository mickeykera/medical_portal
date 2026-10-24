using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class Statement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public int TotalCents { get; set; }
    public int AmountPaidCents { get; set; } = 0;
    public int BalanceCents { get; set; }
    public string? Description { get; set; }
    public DateTime? DueAt { get; set; }
    public StatementStatus Status { get; set; } = StatementStatus.OPEN;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public virtual ICollection<PaymentPlan> PaymentPlans { get; set; } = new List<PaymentPlan>();
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StatementId { get; set; }
    public int AmountCents { get; set; }
    public string PaymentToken { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.SUCCEEDED;
    public string? Last4 { get; set; }
    public string? CardBrand { get; set; }
    public string? GatewayPaymentIntentId { get; set; }
    public string? GatewayChargeId { get; set; }
    public string? GatewayRefundId { get; set; }
    public string? FailureReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Statement Statement { get; set; } = null!;
}

public class ProcessedWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class ConsumedPaymentIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GatewayPaymentIntentId { get; set; } = string.Empty;
    public Guid PatientId { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "usd";
    public string? IdempotencyKey { get; set; }
    public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
}

public class PaymentPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid StatementId { get; set; }
    public int TotalCents { get; set; }
    public int InstallmentCents { get; set; }
    public int NumberOfPayments { get; set; }
    public DateTime StartDate { get; set; }
    public PaymentPlanStatus Status { get; set; } = PaymentPlanStatus.ACTIVE;
    public Guid? CreatedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
    public virtual Statement Statement { get; set; } = null!;
    public virtual ICollection<PaymentInstallment> Installments { get; set; } = new List<PaymentInstallment>();
}

public class PaymentInstallment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public int Sequence { get; set; }
    public int AmountCents { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? PaidAt { get; set; }

    public virtual PaymentPlan Plan { get; set; } = null!;
}
