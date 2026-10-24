namespace MedicalPortal.Core.Entities;

public class MessageThread : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid PatientId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Status { get; set; } = "OPEN";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Patient Patient { get; set; } = null!;
    public virtual ICollection<MessageThreadParticipant> Participants { get; set; } = new List<MessageThreadParticipant>();
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}

public class MessageThreadParticipant : ITenantScoped
{
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ThreadId { get; set; }
    public Guid PersonId { get; set; }
    public Guid? LastReadMessageId { get; set; }

    public virtual MessageThread Thread { get; set; } = null!;
    public virtual Person Person { get; set; } = null!;
}

public class Message : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = TenantConstants.DefaultTenantId;
    public Guid ThreadId { get; set; }
    public Guid SenderPersonId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsUrgent { get; set; } = false;
    public bool Deleted { get; set; } = false;

    public virtual MessageThread Thread { get; set; } = null!;
    public virtual Person Sender { get; set; } = null!;
}
