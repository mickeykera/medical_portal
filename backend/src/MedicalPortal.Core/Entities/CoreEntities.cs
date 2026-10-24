using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class Person : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string? SexAtBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? PreferredPharmacy { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<UserAccount> Accounts { get; set; } = new List<UserAccount>();
    public virtual Patient? Patient { get; set; }
}

public class UserAccount : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PersonId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.PATIENT;
    public int FailedLoginCount { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int AuthorizationVersion { get; set; } = 1;
    public bool MfaEnabled { get; set; } = false;
    public string? MfaSecret { get; set; }
    public string? MfaRecoveryCodesJson { get; set; }
    public DateTime? MfaEnrolledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Person Person { get; set; } = null!;
    public virtual Provider? Provider { get; set; }
    public virtual ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public virtual ICollection<UserMembership> Memberships { get; set; } = new List<UserMembership>();
}

public class UserSession : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Sid { get; set; } = string.Empty;
    public Guid UserAccountId { get; set; }
    public string RefreshTokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public virtual UserAccount UserAccount { get; set; } = null!;
}

public class Patient : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PersonId { get; set; }
    public string Mrn { get; set; } = string.Empty;
    public Guid? PrimaryProviderId { get; set; }
    public string? HmsMrn { get; set; }
    public DateTime? HmsLastSync { get; set; }
    public string? LastSyncedDemographicsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Person Person { get; set; } = null!;
    public virtual Provider? PrimaryProvider { get; set; }
    public virtual HealthSnapshot? Snapshot { get; set; }

    public virtual ICollection<PatientCareTeam> CareTeams { get; set; } = new List<PatientCareTeam>();
    public virtual ICollection<Allergy> Allergies { get; set; } = new List<Allergy>();
    public virtual ICollection<Observation> Observations { get; set; } = new List<Observation>();
    public virtual ICollection<Condition> Conditions { get; set; } = new List<Condition>();
    public virtual ICollection<Procedure> Procedures { get; set; } = new List<Procedure>();
    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public virtual ICollection<Statement> Statements { get; set; } = new List<Statement>();
    public virtual ICollection<PaymentPlan> PaymentPlans { get; set; } = new List<PaymentPlan>();
    public virtual ICollection<FormResponse> FormResponses { get; set; } = new List<FormResponse>();
}

public class Provider : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid? AccountId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Credentials { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Npi { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual UserAccount? Account { get; set; }
    public virtual ICollection<ProviderLocation> Locations { get; set; } = new List<ProviderLocation>();
    public virtual ICollection<PatientCareTeam> CareTeams { get; set; } = new List<PatientCareTeam>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public virtual ICollection<ProviderDepartment> ProviderDepartments { get; set; } = new List<ProviderDepartment>();
    public virtual ICollection<WeeklyScheduleTemplate> WeeklySchedules { get; set; } = new List<WeeklyScheduleTemplate>();
    public virtual ICollection<ScheduleBlock> ScheduleBlocks { get; set; } = new List<ScheduleBlock>();
}

public class Location : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid? DepartmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public LocationType Type { get; set; } = LocationType.CLINIC;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? Hours { get; set; }
    public string TimeZoneId { get; set; } = "America/Chicago";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Department? Department { get; set; }
    public virtual ICollection<ProviderLocation> ProviderLocations { get; set; } = new List<ProviderLocation>();
    public virtual ICollection<RoomResource> RoomResources { get; set; } = new List<RoomResource>();
    public virtual ICollection<WeeklyScheduleTemplate> WeeklySchedules { get; set; } = new List<WeeklyScheduleTemplate>();
    public virtual ICollection<ScheduleBlock> ScheduleBlocks { get; set; } = new List<ScheduleBlock>();
}

public class ProviderLocation : ITenantScoped
{
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ProviderId { get; set; }
    public Guid LocationId { get; set; }

    public virtual Provider Provider { get; set; } = null!;
    public virtual Location Location { get; set; } = null!;
}

public class PatientCareTeam : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid ProviderId { get; set; }
    public string Role { get; set; } = "Primary care";
    public bool IsPrimary { get; set; } = false;

    public virtual Patient Patient { get; set; } = null!;
    public virtual Provider Provider { get; set; } = null!;
}
