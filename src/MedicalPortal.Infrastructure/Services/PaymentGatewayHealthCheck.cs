using MedicalPortal.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MedicalPortal.Infrastructure.Services;

public class PaymentGatewayHealthCheck : IHealthCheck
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IConfiguration _configuration;

    public PaymentGatewayHealthCheck(IPaymentGateway paymentGateway, IConfiguration configuration)
    {
        _paymentGateway = paymentGateway;
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var gatewayType = _paymentGateway.GetType().Name;

        if (_paymentGateway is FakePaymentGateway)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Payment gateway is operational (FakePaymentGateway for test/dev).",
                new Dictionary<string, object>
                {
                    ["gatewayType"] = gatewayType,
                    ["mode"] = "fake",
                    ["ready"] = true
                }));
        }

        // Stripe gateway health verification
        var secretKey = _configuration["Stripe:SecretKey"] ?? _configuration["STRIPE_SECRET_KEY"];
        var pubKey = _configuration["Stripe:PublishableKey"] ?? _configuration["STRIPE_PUBLISHABLE_KEY"];
        var webhookSecret = _configuration["Stripe:WebhookSecret"] ?? _configuration["STRIPE_WEBHOOK_SECRET"];

        var hasSecret = !string.IsNullOrWhiteSpace(secretKey);
        var hasPub = !string.IsNullOrWhiteSpace(pubKey);
        var hasWebhook = !string.IsNullOrWhiteSpace(webhookSecret);

        var data = new Dictionary<string, object>
        {
            ["gatewayType"] = gatewayType,
            ["mode"] = "stripe",
            ["hasSecretKey"] = hasSecret,
            ["hasPublishableKey"] = hasPub,
            ["hasWebhookSecret"] = hasWebhook
        };

        if (hasSecret && hasPub)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Stripe payment gateway configuration is healthy and valid.", data));
        }

        return Task.FromResult(HealthCheckResult.Degraded("Stripe gateway is active but API credentials are incomplete.", data: data));
    }
}
