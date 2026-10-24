namespace MedicalPortal.Core.Enums;

public enum NotificationChannel
{
    EMAIL,
    SMS
}

public enum NotificationDeliveryStatus
{
    QUEUED,
    SENT,
    DELIVERED,
    FAILED,
    OPTED_OUT
}
