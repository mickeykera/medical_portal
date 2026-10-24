using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Notifications;

public class SmtpEmailSender : INotificationSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        var host = _config["Smtp:Host"] ?? "localhost";
        var port = _config.GetValue<int>("Smtp:Port", 587);
        var username = _config["Smtp:Username"];
        var password = _config["Smtp:Password"];
        var enableSsl = _config.GetValue<bool>("Smtp:EnableSsl", true);
        var fromEmail = _config["Smtp:FromEmail"] ?? _config["Notifications:FromEmail"] ?? "no-reply@springfieldhealth.org";

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl
            };

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new System.Net.NetworkCredential(username, password);
            }

            using var message = new MailMessage(fromEmail, request.ToEmail, request.Subject, request.BodyText);
            if (!string.IsNullOrWhiteSpace(request.BodyHtml))
            {
                message.IsBodyHtml = true;
                message.Body = request.BodyHtml;
            }

            await client.SendMailAsync(message, cancellationToken);
            var id = $"smtp_{Guid.NewGuid():N}";
            return new NotificationSendResult(Success: true, ExternalMessageId: id);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP exception sending email to {Recipient}", request.ToEmail);
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception sending SMTP email.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: false);
        }
    }

    public Task<NotificationSendResult> SendSmsAsync(NotificationSmsRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NotificationSendResult(
            Success: false,
            ErrorMessage: "SmtpEmailSender does not support SMS delivery.",
            IsTransientError: false));
    }
}
