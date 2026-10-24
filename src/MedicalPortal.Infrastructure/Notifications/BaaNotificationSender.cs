using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Notifications;

public class BaaNotificationSender : INotificationSender
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<BaaNotificationSender> _logger;

    public BaaNotificationSender(IServiceProvider serviceProvider, IConfiguration config, ILogger<BaaNotificationSender>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger ?? NullLogger<BaaNotificationSender>.Instance;
    }

    private INotificationSender ResolveEmailSender()
    {
        var provider = (_config["Notifications:EmailProvider"] ?? _config["NOTIFICATION_EMAIL_PROVIDER"] ?? "Fake").ToLowerInvariant();
        return provider switch
        {
            "sendgrid" => _serviceProvider.GetRequiredService<SendGridEmailSender>(),
            "awsses" or "ses" or "aws_ses" => _serviceProvider.GetRequiredService<AwsSesEmailSender>(),
            "smtp" => _serviceProvider.GetRequiredService<SmtpEmailSender>(),
            _ => _serviceProvider.GetRequiredService<FakeNotificationSender>()
        };
    }

    private INotificationSender ResolveSmsSender()
    {
        var provider = (_config["Notifications:SmsProvider"] ?? _config["NOTIFICATION_SMS_PROVIDER"] ?? "Fake").ToLowerInvariant();
        return provider switch
        {
            "twilio" => _serviceProvider.GetRequiredService<TwilioSmsSender>(),
            "awssns" or "sns" or "aws_sns" => _serviceProvider.GetRequiredService<AwsSnsSmsSender>(),
            _ => _serviceProvider.GetRequiredService<FakeNotificationSender>()
        };
    }

    public Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        var sender = ResolveEmailSender();
        return sender.SendEmailAsync(request, cancellationToken);
    }

    public Task<NotificationSendResult> SendSmsAsync(NotificationSmsRequest request, CancellationToken cancellationToken = default)
    {
        var sender = ResolveSmsSender();
        return sender.SendSmsAsync(request, cancellationToken);
    }
}
