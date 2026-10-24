namespace MedicalPortal.Core.Entities;

public class Organization : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? TaxId { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Facility> Facilities { get; set; } = new List<Facility>();
}

public class Facility : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Organization Organization { get; set; } = null!;
    public virtual ICollection<Department> Departments { get; set; } = new List<Department>();
    public virtual ICollection<UserMembership> Memberships { get; set; } = new List<UserMembership>();
}

public class Department : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid FacilityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Facility Facility { get; set; } = null!;
    public virtual ICollection<ProviderDepartment> ProviderDepartments { get; set; } = new List<ProviderDepartment>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();
    public virtual ICollection<UserMembership> Memberships { get; set; } = new List<UserMembership>();
}

public class ProviderDepartment : ITenantScoped
{
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ProviderId { get; set; }
    public Guid DepartmentId { get; set; }
    public bool IsPrimary { get; set; } = false;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public virtual Provider Provider { get; set; } = null!;
    public virtual Department Department { get; set; } = null!;
}

public class UserMembership : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid UserAccountId { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string Role { get; set; } = "MEMBER";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual UserAccount UserAccount { get; set; } = null!;
    public virtual Facility? Facility { get; set; }
    public virtual Department? Department { get; set; }
}
