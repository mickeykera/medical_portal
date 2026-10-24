using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public class NotificationDeliveryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RecipientPersonId { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Category { get; set; } = "General";
    public string RecipientAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.QUEUED;
    public int AttemptCount { get; set; } = 0;
    public string? ErrorMessage { get; set; }
    public string? ExternalMessageId { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public virtual Person? RecipientPerson { get; set; }
}
