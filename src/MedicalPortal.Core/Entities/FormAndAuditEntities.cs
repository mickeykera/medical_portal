namespace MedicalPortal.Core.Entities;

public class FormDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "General";
    public string FieldsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FormResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid FormDefinitionId { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public Guid? SubmittedByUserAccountId { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "SUBMITTED";

    public virtual Patient Patient { get; set; } = null!;
    public virtual FormDefinition FormDefinition { get; set; } = null!;
}

public class NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonId { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = false;
    public bool PushEnabled { get; set; } = false;
    public bool Muted { get; set; } = false;

    public virtual Person Person { get; set; } = null!;
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long SequenceNumber { get; set; }
    public Guid? ActorUserAccountId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? ActingForPersonId { get; set; }
    public bool IsProxy { get; set; } = false;
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? MetadataJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? PreviousHash { get; set; }
    public string Hash { get; set; } = string.Empty;
}

public class SnomedConcept
{
    public string Code { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
}

public class RxNormConcept
{
    public string Code { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string Tty { get; set; } = "SCD";
}
