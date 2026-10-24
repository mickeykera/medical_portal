using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class Allergy : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public string Substance { get; set; } = string.Empty;
    public string DrugClass { get; set; } = string.Empty;
    public string? DrugClassCode { get; set; }
    public string? Reaction { get; set; }
    public AllergySeverity Severity { get; set; } = AllergySeverity.MODERATE;
    public DateTime? OnsetDate { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public bool Tombstone { get; set; } = false;

    public virtual Patient Patient { get; set; } = null!;
}

public class Observation : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public ObservationCategory Category { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal? ValueNumber { get; set; }
    public string? ValueText { get; set; }
    public string? Unit { get; set; }
    public decimal? RefRangeLow { get; set; }
    public decimal? RefRangeHigh { get; set; }
    public string? RefRangeText { get; set; }
    public bool IsAbnormal { get; set; } = false;
    public string? LotNumber { get; set; }
    public string? ImmunizationStatus { get; set; }
    public string? ImmunizationStatusCode { get; set; }
    public DateTime ObservedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
}

public class Condition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClinicalStatus { get; set; } = "ACTIVE";
    public DateTime? OnsetDate { get; set; }

    public virtual Patient Patient { get; set; } = null!;
}

public class Procedure : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
    public string? PerformedBy { get; set; }
    public string? Facility { get; set; }

    public virtual Patient Patient { get; set; } = null!;
}

public class HealthSnapshot : ITenantScoped
{
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int ActiveConditionCount { get; set; } = 0;
    public int ActiveMedicationCount { get; set; } = 0;
    public int AbnormalLabCount { get; set; } = 0;
    public string ImmunizationStatus { get; set; } = "UP_TO_DATE";
    public DateTime? LatestLabObservedAt { get; set; }
    public DateTime? UpcomingAppointmentAt { get; set; }
    public int OutstandingBalanceCents { get; set; } = 0;

    public virtual Patient Patient { get; set; } = null!;
}

public class BreakGlassOverride : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ClinicianUserAccountId { get; set; }
    public Guid PatientId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(60);
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public virtual UserAccount ClinicianUserAccount { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
}
