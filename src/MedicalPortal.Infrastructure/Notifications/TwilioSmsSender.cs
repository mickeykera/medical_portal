using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Notifications;

public class TwilioSmsSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(HttpClient httpClient, IConfiguration config, ILogger<TwilioSmsSender> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://api.twilio.com/");
        }
    }

    public Task<NotificationSendResult> SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NotificationSendResult(
            Success: false,
            ErrorMessage: "TwilioSmsSender does not support email delivery.",
            IsTransientError: false));
    }

    public async Task<NotificationSendResult> SendSmsAsync(NotificationSmsRequest request, CancellationToken cancellationToken = default)
    {
        var accountSid = _config["Twilio:AccountSid"] ?? _config["TWILIO_ACCOUNT_SID"];
        var authToken = _config["Twilio:AuthToken"] ?? _config["TWILIO_AUTH_TOKEN"];

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken))
        {
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: "Twilio AccountSid or AuthToken is not configured.",
                IsTransientError: false);
        }

        var fromPhone = _config["Twilio:FromPhoneNumber"] ?? _config["Notifications:FromPhone"] ?? "+18005550199";

        var formParams = new Dictionary<string, string>
        {
            ["To"] = request.ToPhoneNumber,
            ["From"] = fromPhone,
            ["Body"] = request.Message
        };

        var path = $"2010-04-01/Accounts/{accountSid}/Messages.json";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpRequest.Content = new FormUrlEncodedContent(formParams);

        try
        {
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                string? sid = null;
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("sid", out var sidProp))
                    {
                        sid = sidProp.GetString();
                    }
                }
                catch
                {
                    sid = $"tw_{Guid.NewGuid():N}";
                }

                return new NotificationSendResult(Success: true, ExternalMessageId: sid ?? $"tw_{Guid.NewGuid():N}");
            }

            var statusCode = (int)response.StatusCode;
            var isTransient = statusCode == 429 || statusCode >= 500;

            _logger.LogWarning("Twilio SMS send failed with status {StatusCode}: {Response}", statusCode, content);

            return new NotificationSendResult(
                Success: false,
                ErrorMessage: $"Twilio returned HTTP {statusCode}: {content}",
                IsTransientError: isTransient);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network exception sending SMS via Twilio.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception sending SMS via Twilio.");
            return new NotificationSendResult(
                Success: false,
                ErrorMessage: ex.Message,
                IsTransientError: false);
        }
    }
}
