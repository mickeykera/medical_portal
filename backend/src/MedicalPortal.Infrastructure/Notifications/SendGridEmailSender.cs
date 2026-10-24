using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Notifications;

public class SendGridEmailSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(HttpClient httpClient, IConfiguration config, ILogger<SendGridEmailSender> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://api.sendgrid.com/");
        }
    }

    public async Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = _config["SendGrid:ApiKey"] ?? _config["SENDGRID_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: "SendGrid API key is not configured.",
                IsTransientError: false);
        }

        var fromEmail = _config["SendGrid:FromEmail"] ?? _config["Notifications:FromEmail"] ?? "no-reply@addiscarehealth.org";
        var fromName = _config["SendGrid:FromName"] ?? "Addis Care Health Network (DEMO)";

        var payload = new
        {
            personalizations = new[]
            {
                new
                {
                    to = new[] { new { email = request.ToEmail } },
                    subject = request.Subject
                }
            },
            from = new { email = fromEmail, name = fromName },
            content = new object[]
            {
                new { type = "text/plain", value = request.BodyText },
                request.BodyHtml != null ? new { type = "text/html", value = request.BodyHtml } : null!
            }.Where(c => c != null).ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string? messageId = null;
                if (response.Headers.TryGetValues("X-Message-Id", out var values))
                {
                    messageId = values.FirstOrDefault();
                }
                return new NotificationSendResult(Success: true, ExternalMessageId: messageId ?? $"sg_{Guid.NewGuid():N}");
            }

            var statusCode = (int)response.StatusCode;
            var isTransient = statusCode == 429 || statusCode >= 500;
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning("SendGrid email send failed with status {StatusCode}: {Response}", statusCode, responseContent);

            return new NotificationSendResult(
                Success: false,
                ErrorMessage: $"SendGrid returned HTTP {statusCode}: {responseContent}",
                IsTransientError: isTransient);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network exception sending email via SendGrid.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception sending email via SendGrid.");
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
            ErrorMessage: "SendGridEmailSender does not support SMS delivery.",
            IsTransientError: false));
    }
}
