using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class Medication : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Form { get; set; }
    public string? Strength { get; set; }
    public string? RxNormCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
}

public class Prescription : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid MedicationId { get; set; }
    public Guid ProviderId { get; set; }
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime PrescribedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public virtual Patient Patient { get; set; } = null!;
    public virtual Medication Medication { get; set; } = null!;
    public virtual Provider Provider { get; set; } = null!;
    public virtual ICollection<RefillRequest> RefillRequests { get; set; } = new List<RefillRequest>();
}

public class RefillRequest : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PrescriptionId { get; set; }
    public Guid MedicationId { get; set; }
    public RefillStatus Status { get; set; } = RefillStatus.PENDING;
    public string? PharmacyName { get; set; }
    public string? PatientMessage { get; set; }
    public string? ClinicianResponse { get; set; }
    public Guid? RequestedByUserAccountId { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public bool HmsSynced { get; set; } = false;

    public virtual Prescription Prescription { get; set; } = null!;
    public virtual Medication Medication { get; set; } = null!;
}

public class Appointment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? AppointmentTypeId { get; set; }
    public Guid? RoomResourceId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.REQUESTED;
    public string? ReasonForVisit { get; set; }
    public string? PatientNotes { get; set; }
    public Guid? RequestedByUserAccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool HmsSynced { get; set; } = false;

    public virtual Patient Patient { get; set; } = null!;
    public virtual Provider Provider { get; set; } = null!;
    public virtual Location? Location { get; set; }
    public virtual Department? Department { get; set; }
    public virtual AppointmentType? AppointmentType { get; set; }
    public virtual RoomResource? RoomResource { get; set; }
}

public class AppointmentType : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 30;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}

public class RoomResource : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Location Location { get; set; } = null!;
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
}

public class WeeklyScheduleTemplate : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid? ProviderId { get; set; }
    public Guid? LocationId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Provider? Provider { get; set; }
    public virtual Location? Location { get; set; }
}

public class ScheduleBlock : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid? ProviderId { get; set; }
    public Guid? LocationId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsHoliday { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Provider? Provider { get; set; }
    public virtual Location? Location { get; set; }
}
