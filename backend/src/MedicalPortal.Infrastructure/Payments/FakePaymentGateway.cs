using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MedicalPortal.Infrastructure.Payments;

public class FakePaymentGateway : IPaymentGateway
{
    private readonly IConfiguration? _config;
    public const string DefaultTestWebhookSecret = "whsec_test_secret_for_unit_tests_123456";

    public bool SimulateFailure { get; set; } = false;
    public string DefaultLast4 { get; set; } = "9876";
    public string DefaultCardBrand { get; set; } = "visa";

    public ConcurrentDictionary<string, PaymentIntentResponse> CreatedIntents { get; } = new();
    public ConcurrentDictionary<string, PaymentResult> ConfirmedPayments { get; } = new();
    public ConcurrentDictionary<string, RefundResult> Refunds { get; } = new();

    public FakePaymentGateway(IConfiguration? config = null)
    {
        _config = config;
    }

    public string GetWebhookSecret()
    {
        return _config?["Stripe:WebhookSecret"] ?? _config?["STRIPE_WEBHOOK_SECRET"] ?? DefaultTestWebhookSecret;
    }

    public Task<PaymentIntentResponse> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken cancellationToken = default)
    {
        var id = "pi_fake_" + Guid.NewGuid().ToString("N");
        var clientSecret = $"{id}_secret_{Guid.NewGuid():N}";
        var response = new PaymentIntentResponse(
            true,
            id,
            clientSecret,
            request.AmountCents,
            request.Currency,
            "requires_payment_method",
            PatientId: request.PatientId);

        CreatedIntents[id] = response;
        return Task.FromResult(response);
    }

    public Task<PaymentResult> ConfirmPaymentAsync(ConfirmPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var isFailure = SimulateFailure ||
                        (!string.IsNullOrEmpty(request.PaymentMethodToken) && request.PaymentMethodToken.Contains("fail", StringComparison.OrdinalIgnoreCase));

        if (isFailure)
        {
            var failResult = new PaymentResult(
                false,
                request.PaymentIntentId,
                null,
                0,
                "failed",
                null,
                null,
                "The card was declined.",
                "card_declined");

            ConfirmedPayments[request.PaymentIntentId] = failResult;
            return Task.FromResult(failResult);
        }

        string last4 = DefaultLast4;
        if (!string.IsNullOrEmpty(request.PaymentMethodToken))
        {
            // If token has digits at the end (e.g. pm_card_1234), extract them
            var digits = new string(request.PaymentMethodToken.Where(char.IsDigit).TakeLast(4).ToArray());
            if (digits.Length == 4) last4 = digits;
        }

        var chargeId = "ch_fake_" + Guid.NewGuid().ToString("N");
        var amountCents = CreatedIntents.TryGetValue(request.PaymentIntentId, out var intent) ? intent.AmountCents : 5000;
        var patientId = CreatedIntents.TryGetValue(request.PaymentIntentId, out var pIntent) ? pIntent.PatientId : null;
        var currency = CreatedIntents.TryGetValue(request.PaymentIntentId, out var cIntent) ? cIntent.Currency : "usd";

        var successResult = new PaymentResult(
            true,
            request.PaymentIntentId,
            chargeId,
            amountCents,
            "succeeded",
            last4,
            DefaultCardBrand,
            Currency: currency,
            PatientId: patientId);

        ConfirmedPayments[request.PaymentIntentId] = successResult;
        return Task.FromResult(successResult);
    }

    public Task<PaymentResult> GetPaymentDetailsAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        if (ConfirmedPayments.TryGetValue(paymentIntentId, out var existing))
        {
            return Task.FromResult(existing);
        }

        var amountCents = CreatedIntents.TryGetValue(paymentIntentId, out var intent) ? intent.AmountCents : 5000;
        var patientId = CreatedIntents.TryGetValue(paymentIntentId, out var pIntent) ? pIntent.PatientId : null;
        var currency = CreatedIntents.TryGetValue(paymentIntentId, out var cIntent) ? cIntent.Currency : "usd";

        var result = new PaymentResult(
            true,
            paymentIntentId,
            "ch_fake_" + Guid.NewGuid().ToString("N"),
            amountCents,
            "succeeded",
            DefaultLast4,
            DefaultCardBrand,
            Currency: currency,
            PatientId: patientId);

        return Task.FromResult(result);
    }

    public Task<RefundResult> RefundPaymentAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        var refundId = "re_fake_" + Guid.NewGuid().ToString("N");
        var amountCents = request.AmountCents ?? 5000;

        var result = new RefundResult(
            true,
            refundId,
            request.PaymentIntentId,
            request.ChargeId ?? "ch_fake_" + Guid.NewGuid().ToString("N"),
            amountCents,
            "succeeded");

        Refunds[refundId] = result;
        return Task.FromResult(result);
    }

    public WebhookVerificationResult VerifyWebhook(string rawPayload, string? signatureHeader, long toleranceSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Missing signature header.");
        }

        var secret = GetWebhookSecret();
        var items = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        long? timestamp = null;
        var signatures = new List<string>();

        foreach (var item in items)
        {
            var parts = item.Split('=', 2);
            if (parts.Length != 2) continue;
            if (parts[0] == "t" && long.TryParse(parts[1], out var parsedT))
            {
                timestamp = parsedT;
            }
            else if (parts[0] == "v1")
            {
                signatures.Add(parts[1]);
            }
        }

        if (!timestamp.HasValue || signatures.Count == 0)
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Malformed signature header.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp.Value) > toleranceSeconds)
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Timestamp outside tolerance window.");
        }

        var signedPayload = $"{timestamp.Value}.{rawPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expectedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

        bool matched = false;
        foreach (var sig in signatures)
        {
            var sigBytes = Encoding.UTF8.GetBytes(sig.ToLowerInvariant());
            if (expectedBytes.Length == sigBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, sigBytes))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Invalid webhook signature.");
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;
            var eventId = root.GetProperty("id").GetString()!;
            var eventType = root.GetProperty("type").GetString()!;

            string? paymentIntentId = null;
            string? chargeId = null;
            string? refundId = null;
            int amount = 0;
            string? status = null;
            string? last4 = null;
            string? cardBrand = null;
            string? failureReason = null;
            Guid? patientId = null;
            var metadata = new Dictionary<string, string>();

            if (root.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("object", out var obj))
            {
                paymentIntentId = obj.TryGetProperty("id", out var piId) ? piId.GetString() : null;
                chargeId = obj.TryGetProperty("charge", out var ch) ? ch.GetString() : null;
                refundId = obj.TryGetProperty("refund_id", out var refEl) ? refEl.GetString() : null;
                amount = obj.TryGetProperty("amount", out var am) ? am.GetInt32() : 0;
                status = obj.TryGetProperty("status", out var st) ? st.GetString() : null;
                last4 = obj.TryGetProperty("last4", out var l4) ? l4.GetString() : null;
                cardBrand = obj.TryGetProperty("brand", out var br) ? br.GetString() : null;
                failureReason = obj.TryGetProperty("failure_message", out var fm) ? fm.GetString() : null;

                if (obj.TryGetProperty("metadata", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in metaEl.EnumerateObject())
                    {
                        metadata[prop.Name] = prop.Value.GetString() ?? string.Empty;
                        if (prop.Name.Equals("patientId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(prop.Value.GetString(), out var pid))
                        {
                            patientId = pid;
                        }
                    }
                }
            }

            return new WebhookVerificationResult(
                true, eventId, eventType, paymentIntentId, chargeId, refundId, amount, status, last4, cardBrand, failureReason, patientId, metadata);
        }
        catch (Exception ex)
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, ex.Message);
        }
    }

    public static string GenerateSignatureHeader(string rawPayload, string secret, long? timestamp = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{t}.{rawPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();
        return $"t={t},v1={signature}";
    }

    public static string CreateWebhookPayload(
        string eventId,
        string eventType,
        string paymentIntentId,
        int amountCents,
        Guid? patientId = null,
        string status = "succeeded",
        string? last4 = "1234",
        string? chargeId = null,
        string? refundId = null,
        string? failureMessage = null)
    {
        var obj = new Dictionary<string, object?>
        {
            ["object"] = eventType.StartsWith("charge") ? "charge" : (eventType.StartsWith("refund") ? "refund" : "payment_intent"),
            ["id"] = paymentIntentId,
            ["amount"] = amountCents,
            ["status"] = status,
            ["last4"] = last4,
            ["brand"] = "visa",
            ["charge"] = chargeId ?? "ch_fake_" + Guid.NewGuid().ToString("N"),
            ["refund_id"] = refundId,
            ["failure_message"] = failureMessage,
            ["metadata"] = new Dictionary<string, string?>
            {
                ["patientId"] = patientId?.ToString()
            }
        };

        var root = new
        {
            id = eventId,
            type = eventType,
            data = new { @object = obj }
        };

        return JsonSerializer.Serialize(root);
    }
}
