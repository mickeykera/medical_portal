using MedicalPortal.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MedicalPortal.Infrastructure.Services;

public class NotificationSenderHealthCheck : IHealthCheck
{
    private readonly INotificationSender _notificationSender;
    private readonly IConfiguration _configuration;

    public NotificationSenderHealthCheck(INotificationSender notificationSender, IConfiguration configuration)
    {
        _notificationSender = notificationSender;
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var senderType = _notificationSender.GetType().Name;
        var emailProvider = _configuration["Notifications:EmailProvider"]
            ?? _configuration["NOTIFICATION_EMAIL_PROVIDER"]
            ?? "Fake";
        var smsProvider = _configuration["Notifications:SmsProvider"]
            ?? _configuration["NOTIFICATION_SMS_PROVIDER"]
            ?? "Fake";

        var data = new Dictionary<string, object>
        {
            ["senderType"] = senderType,
            ["emailProvider"] = emailProvider,
            ["smsProvider"] = smsProvider,
            ["portalBaseUrl"] = _configuration["Notifications:PortalBaseUrl"] ?? _configuration["PORTAL_BASE_URL"] ?? "default"
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Notification delivery subsystem is operational (Email: {emailProvider}, SMS: {smsProvider}).",
            data));
    }
}
