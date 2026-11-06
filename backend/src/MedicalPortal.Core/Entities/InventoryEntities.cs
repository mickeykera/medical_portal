namespace MedicalPortal.Core.Entities;

public enum StoreType
{
    CENTRAL,
    DEPARTMENT
}

public enum StockLedgerAction
{
    RECEIVE,
    ISSUE,
    TRANSFER,
    RETURN,
    ADJUSTMENT,
    WASTAGE,
    EXPIRY_WRITE_OFF,
    DISPENSE,
    CONSUME
}

public enum InventoryRequisitionStatus
{
    DRAFT,
    APPROVED,
    ISSUED,
    RECEIVED,
    CANCELLED
}

public class InventoryItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Code { get; set; } = string.Empty;
    public string GenericName { get; set; } = string.Empty;
    public string? Strength { get; set; }
    public string? Form { get; set; }
    public string Unit { get; set; } = "each";
    public string? PackSize { get; set; }
    public string? Category { get; set; }
    public string? AtcCode { get; set; }
    public string? EmlCode { get; set; }
    public string? VenClass { get; set; }
    public int MinimumLevel { get; set; }
    public int MaximumLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class InventoryStore : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public StoreType Type { get; set; } = StoreType.DEPARTMENT;
    public Guid? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Department? Department { get; set; }
}

public class StockBatch : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ItemId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsQuarantined { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual InventoryItem Item { get; set; } = null!;
}

public class StockLedgerEntry : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid StoreId { get; set; }
    public Guid ItemId { get; set; }
    public Guid BatchId { get; set; }
    public StockLedgerAction Action { get; set; }
    public int Quantity { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? EncounterId { get; set; }
    public Guid? ActorUserAccountId { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual InventoryStore Store { get; set; } = null!;
    public virtual InventoryItem Item { get; set; } = null!;
    public virtual StockBatch Batch { get; set; } = null!;
}

public class InventoryRequisition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid FromStoreId { get; set; }
    public Guid ToStoreId { get; set; }
    public InventoryRequisitionStatus Status { get; set; } = InventoryRequisitionStatus.DRAFT;
    public Guid RequestedByUserAccountId { get; set; }
    public Guid? ApprovedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Reason { get; set; }
    public virtual ICollection<InventoryRequisitionLine> Lines { get; set; } = new List<InventoryRequisitionLine>();
}

public class InventoryRequisitionLine : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid RequisitionId { get; set; }
    public Guid ItemId { get; set; }
    public int Quantity { get; set; }
    public int IssuedQuantity { get; set; }
    public virtual InventoryRequisition Requisition { get; set; } = null!;
}

public class ControlledDrugEntry : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid StoreId { get; set; }
    public Guid ItemId { get; set; }
    public Guid BatchId { get; set; }
    public int Quantity { get; set; }
    public Guid PatientId { get; set; }
    public Guid? EncounterId { get; set; }
    public Guid FirstSignatureUserAccountId { get; set; }
    public Guid? SecondSignatureUserAccountId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class InventoryCountSheet : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid StoreId { get; set; }
    public Guid CreatedByUserAccountId { get; set; }
    public Guid? ApprovedByUserAccountId { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public virtual ICollection<InventoryCountLine> Lines { get; set; } = new List<InventoryCountLine>();
}

public class InventoryCountLine : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid CountSheetId { get; set; }
    public Guid ItemId { get; set; }
    public Guid BatchId { get; set; }
    public int SystemQuantity { get; set; }
    public int CountedQuantity { get; set; }
    public string? VarianceReason { get; set; }
    public virtual InventoryCountSheet CountSheet { get; set; } = null!;
}
