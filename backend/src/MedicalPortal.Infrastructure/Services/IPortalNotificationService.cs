namespace MedicalPortal.Infrastructure.Services;

public interface IPortalNotificationService
{
    Task NotifyAppointmentChangedAsync(Guid patientId, Guid? providerId, string action, object payload);
    Task NotifyNewMessageAsync(Guid threadId, Guid recipientPersonId, object payload, Guid? patientId = null);
    Task NotifyRefillChangedAsync(Guid patientId, Guid refillId, string status, object payload);
    Task NotifyBreakGlassAsync(Guid patientId, Guid clinicianId, string reason, DateTime expiresAt, object payload);
    Task NotifyComplianceQueueAsync(string eventType, object payload);
}
