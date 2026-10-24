using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Notifications;

public class AwsSesEmailSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AwsSesEmailSender> _logger;

    public AwsSesEmailSender(HttpClient httpClient, IConfiguration config, ILogger<AwsSesEmailSender> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        var region = _config["Aws:Region"] ?? _config["AWS_REGION"] ?? "us-east-1";
        var accessKey = _config["Aws:AccessKeyId"] ?? _config["AWS_ACCESS_KEY_ID"];
        var secretKey = _config["Aws:SecretAccessKey"] ?? _config["AWS_SECRET_ACCESS_KEY"];

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: "AWS credentials are not configured.",
                IsTransientError: false);
        }

        var fromEmail = _config["Aws:Ses:FromEmail"] ?? _config["Notifications:FromEmail"] ?? "no-reply@springfieldhealth.org";

        var payload = new
        {
            FromEmailAddress = fromEmail,
            Destination = new { ToAddresses = new[] { request.ToEmail } },
            Content = new
            {
                Simple = new
                {
                    Subject = new { Data = request.Subject, Charset = "UTF-8" },
                    Body = new
                    {
                        Text = new { Data = request.BodyText, Charset = "UTF-8" }
                    }
                }
            }
        };

        var uri = new Uri($"https://email.{region}.amazonaws.com/v2/email/outbound-emails");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                string? messageId = null;
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("MessageId", out var msgProp))
                        messageId = msgProp.GetString();
                }
                catch
                {
                    messageId = $"ses_{Guid.NewGuid():N}";
                }

                return new NotificationSendResult(Success: true, ExternalMessageId: messageId ?? $"ses_{Guid.NewGuid():N}");
            }

            var statusCode = (int)response.StatusCode;
            var isTransient = statusCode == 429 || statusCode >= 500;
            var resp = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning("AWS SES email send failed with status {StatusCode}: {Response}", statusCode, resp);
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: $"AWS SES returned HTTP {statusCode}: {resp}",
                IsTransientError: isTransient);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network exception sending email via AWS SES.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception sending email via AWS SES.");
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
            ErrorMessage: "AwsSesEmailSender does not support SMS delivery.",
            IsTransientError: false));
    }
}
