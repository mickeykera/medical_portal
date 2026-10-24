using MedicalPortal.Infrastructure.Hms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MedicalPortal.Infrastructure.Services;

public class HmsHealthCheck : IHealthCheck
{
    private readonly IHmsClient _hmsClient;
    private readonly IConfiguration _configuration;

    public HmsHealthCheck(IHmsClient hmsClient, IConfiguration configuration)
    {
        _hmsClient = hmsClient;
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var clientType = _hmsClient.GetType().Name;

        if (_hmsClient is FakeHmsClient)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Hospital Management System (HMS) adapter is operational (FakeHmsClient).",
                new Dictionary<string, object>
                {
                    ["clientType"] = clientType,
                    ["mode"] = "fake",
                    ["ready"] = true
                }));
        }

        var baseUrl = _configuration["Hms:BaseUrl"] ?? _configuration["HMS_BASE_URL"];
        var username = _configuration["Hms:Username"] ?? _configuration["HMS_USERNAME"];
        var hasSecrets = !string.IsNullOrWhiteSpace(_configuration["Hms:WebhookSecrets"] ?? _configuration["HMS_WEBHOOK_SECRETS"]);

        var data = new Dictionary<string, object>
        {
            ["clientType"] = clientType,
            ["mode"] = "live",
            ["baseUrl"] = baseUrl ?? "not-configured",
            ["hasCredentials"] = !string.IsNullOrWhiteSpace(username),
            ["hasWebhookSecrets"] = hasSecrets
        };

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return Task.FromResult(HealthCheckResult.Healthy("HMS integration client is configured.", data));
        }

        return Task.FromResult(HealthCheckResult.Degraded("HMS integration client is active but base URL is missing.", data: data));
    }
}
