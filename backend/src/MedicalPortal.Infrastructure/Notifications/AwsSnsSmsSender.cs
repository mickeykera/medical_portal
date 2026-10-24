using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Notifications;

public class AwsSnsSmsSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AwsSnsSmsSender> _logger;

    public AwsSnsSmsSender(HttpClient httpClient, IConfiguration config, ILogger<AwsSnsSmsSender> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NotificationSendResult(
            Success: false,
            ErrorMessage: "AwsSnsSmsSender does not support email delivery.",
            IsTransientError: false));
    }

    public async Task<NotificationSendResult> SendSmsAsync(NotificationSmsRequest request, CancellationToken cancellationToken = default)
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

        var form = new Dictionary<string, string>
        {
            ["Action"] = "Publish",
            ["PhoneNumber"] = request.ToPhoneNumber,
            ["Message"] = request.Message,
            ["Version"] = "2010-03-31"
        };

        var uri = new Uri($"https://sns.{region}.amazonaws.com/");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Content = new FormUrlEncodedContent(form);

        try
        {
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var id = $"sns_{Guid.NewGuid():N}";
                return new NotificationSendResult(Success: true, ExternalMessageId: id);
            }

            var statusCode = (int)response.StatusCode;
            var isTransient = statusCode == 429 || statusCode >= 500;
            var resp = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogWarning("AWS SNS SMS send failed with status {StatusCode}: {Response}", statusCode, resp);
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: $"AWS SNS returned HTTP {statusCode}: {resp}",
                IsTransientError: isTransient);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network exception sending SMS via AWS SNS.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception sending SMS via AWS SNS.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: false);
        }
    }
}
