namespace MedicalPortal.Core.Entities;

public class KpiDefinition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string NumeratorMetric { get; set; } = string.Empty;
    public string? DenominatorMetric { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class KpiSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid KpiDefinitionId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = "count";
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    public virtual KpiDefinition KpiDefinition { get; set; } = null!;
}
