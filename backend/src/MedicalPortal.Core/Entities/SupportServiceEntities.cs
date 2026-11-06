namespace MedicalPortal.Core.Entities;

public enum SupportTaskType
{
    TERMINAL_CLEANING,
    SPILL_BIOHAZARD,
    OR_TURNOVER
}

public enum SupportTaskStatus
{
    OPEN,
    ASSIGNED,
    COMPLETED,
    CANCELLED
}

public class SupportServiceTask : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public SupportTaskType Type { get; set; }
    public SupportTaskStatus Status { get; set; } = SupportTaskStatus.OPEN;
    public Guid? BedId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? AssignedUserAccountId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public enum DietOrderStatus
{
    ACTIVE,
    COMPLETED,
    CANCELLED
}

public class DietOrder : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid EncounterId { get; set; }
    public string DietType { get; set; } = string.Empty;
    public bool Npo { get; set; }
    public string? AllergiesAndRestrictions { get; set; }
    public DietOrderStatus Status { get; set; } = DietOrderStatus.ACTIVE;
    public Guid OrderedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum LinenLedgerAction
{
    RECEIVED,
    DISTRIBUTED,
    RETURNED,
    LOST,
    DAMAGED
}

public class LinenLedgerEntry : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid DepartmentId { get; set; }
    public string LinenItem { get; set; } = string.Empty;
    public LinenLedgerAction Action { get; set; }
    public int Quantity { get; set; }
    public Guid RecordedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
