namespace MedicalPortal.Core.Entities;

public class ProxyGrant : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid GrantorId { get; set; }
    public Guid GranteeId { get; set; }
    public bool CanViewRecords { get; set; } = true;
    public bool CanMessage { get; set; } = true;
    public bool CanManageAppointments { get; set; } = true;
    public bool CanRequestRefills { get; set; } = true;
    public bool CanPayBills { get; set; } = false;
    public bool CanManageSharing { get; set; } = false;
    public bool CanSubmitForms { get; set; } = false;
    public DateTime ConsentGivenAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConsentExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public virtual Person Grantor { get; set; } = null!;
    public virtual Person Grantee { get; set; } = null!;
}

public class ProxyInvite : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid InviterPersonId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string? Relationship { get; set; }
    public string Status { get; set; } = "PENDING";
    public bool CanViewRecords { get; set; } = true;
    public bool CanMessage { get; set; } = true;
    public bool CanManageAppointments { get; set; } = true;
    public bool CanRequestRefills { get; set; } = true;
    public bool CanPayBills { get; set; } = false;
    public bool CanSubmitForms { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }

    public virtual Person Inviter { get; set; } = null!;
}

public class RecordShare : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string? RecipientRole { get; set; }
    public string ScopeJson { get; set; } = "[\"allergies\",\"conditions\",\"medications\",\"labs\",\"vitals\"]";
    public int MaxUses { get; set; } = 5;
    public int UseCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public virtual Patient Patient { get; set; } = null!;
    public virtual ICollection<RecordShareAccess> Accesses { get; set; } = new List<RecordShareAccess>();
}

public class RecordShareAccess : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ShareId { get; set; }
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public virtual RecordShare Share { get; set; } = null!;
}
