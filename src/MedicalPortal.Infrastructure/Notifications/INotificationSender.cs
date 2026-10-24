namespace MedicalPortal.Infrastructure.Notifications;

public record NotificationEmailRequest(
    string ToEmail,
    string Subject,
    string BodyText,
    string? BodyHtml = null,
    string Category = "General");

public record NotificationSmsRequest(
    string ToPhoneNumber,
    string Message,
    string Category = "General");

public record NotificationSendResult(
    bool Success,
    string? ExternalMessageId = null,
    string? ErrorMessage = null,
    bool IsTransientError = false);

public interface INotificationSender
{
    Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default);
    Task<NotificationSendResult> SendSmsAsync(NotificationSmsRequest request, CancellationToken cancellationToken = default);
}
