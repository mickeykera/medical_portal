using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class UserInvite : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.PATIENT;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? CreatedUserAccountId { get; set; }
    public Guid InvitedByUserAccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual Facility? Facility { get; set; }
    public virtual Department? Department { get; set; }
}

public class PaymentRefundRequest : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PaymentId { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "ETB";
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED
    public Guid RequestedByUserAccountId { get; set; }
    public Guid? ReviewedByUserAccountId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual Payment Payment { get; set; } = null!;
}
