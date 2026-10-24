using MedicalPortal.Core.Entities;

namespace MedicalPortal.Infrastructure.Notifications;

public interface INotificationDeliveryService
{
    Task<List<NotificationDeliveryRecord>> SendAppointmentReminderAsync(
        Appointment appointment,
        CancellationToken cancellationToken = default);

    Task<List<NotificationDeliveryRecord>> SendBillingNoticeAsync(
        Person person,
        Guid patientId,
        int amountCents,
        DateTime? dueAt,
        string referenceId,
        CancellationToken cancellationToken = default);

    Task<List<NotificationDeliveryRecord>> SendPaymentReceiptAsync(
        Person person,
        Guid patientId,
        int amountCents,
        string referenceId,
        CancellationToken cancellationToken = default);

    Task<NotificationDeliveryRecord> DispatchEmailAsync(
        Person person,
        string category,
        string subject,
        string body,
        string? referenceType = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default);

    Task<NotificationDeliveryRecord> DispatchSmsAsync(
        Person person,
        string category,
        string message,
        string? referenceType = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default);
}
