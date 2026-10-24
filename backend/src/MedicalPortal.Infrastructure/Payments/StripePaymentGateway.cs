using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MedicalPortal.Infrastructure.Payments;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<StripePaymentGateway> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://api.stripe.com/");
        }
    }

    private string GetSecretKey()
    {
        var key = _config["Stripe:SecretKey"] ?? _config["STRIPE_SECRET_KEY"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Stripe secret key is not configured. Set 'Stripe:SecretKey' or 'STRIPE_SECRET_KEY'.");
        return key;
    }

    private string GetWebhookSecret()
    {
        var secret = _config["Stripe:WebhookSecret"] ?? _config["STRIPE_WEBHOOK_SECRET"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Stripe webhook secret is not configured. Set 'Stripe:WebhookSecret' or 'STRIPE_WEBHOOK_SECRET'.");
        return secret;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetSecretKey());
        request.Headers.Add("Stripe-Version", "2024-06-20");

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    public async Task<PaymentIntentResponse> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["amount"] = request.AmountCents.ToString(),
            ["currency"] = request.Currency.ToLowerInvariant(),
            ["automatic_payment_methods[enabled]"] = "true"
        };

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            form["description"] = request.Description;
        }

        if (request.PatientId.HasValue)
        {
            form["metadata[patientId]"] = request.PatientId.Value.ToString();
        }

        if (request.Metadata != null)
        {
            foreach (var kvp in request.Metadata)
            {
                form[$"metadata[{kvp.Key}]"] = kvp.Value;
            }
        }

        using var httpRequest = CreateRequest(HttpMethod.Post, "v1/payment_intents", request.IdempotencyKey);
        httpRequest.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            var err = root.TryGetProperty("error", out var errEl) && errEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString()
                : "Failed to create payment intent with Stripe.";
            _logger.LogError("Stripe CreatePaymentIntent failed: {Error}", err);
            return new PaymentIntentResponse(false, string.Empty, string.Empty, request.AmountCents, request.Currency, "failed", err);
        }

        var id = root.GetProperty("id").GetString()!;
        var clientSecret = root.TryGetProperty("client_secret", out var csEl) ? csEl.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "unknown" : "unknown";
        var amount = root.GetProperty("amount").GetInt32();
        var currency = root.GetProperty("currency").GetString()!;

        return new PaymentIntentResponse(true, id, clientSecret, amount, currency, status);
    }

    public async Task<PaymentResult> ConfirmPaymentAsync(ConfirmPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentMethodToken))
        {
            return await GetPaymentDetailsAsync(request.PaymentIntentId, cancellationToken);
        }

        var form = new Dictionary<string, string>
        {
            ["payment_method"] = request.PaymentMethodToken
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, $"v1/payment_intents/{request.PaymentIntentId}/confirm", request.IdempotencyKey);
        httpRequest.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return ParsePaymentResult(json, request.PaymentIntentId, response.IsSuccessStatusCode);
    }

    public async Task<PaymentResult> GetPaymentDetailsAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, $"v1/payment_intents/{paymentIntentId}");
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return ParsePaymentResult(json, paymentIntentId, response.IsSuccessStatusCode);
    }

    private PaymentResult ParsePaymentResult(string json, string paymentIntentId, bool isSuccessStatus)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!isSuccessStatus)
        {
            var err = root.TryGetProperty("error", out var errEl) && errEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString()
                : "Failed to process payment with Stripe.";
            return new PaymentResult(false, paymentIntentId, null, 0, "failed", null, null, err, err);
        }

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? paymentIntentId : paymentIntentId;
        var status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "unknown" : "unknown";
        var amount = root.TryGetProperty("amount", out var amEl) ? amEl.GetInt32() : 0;

        string? chargeId = null;
        string? last4 = null;
        string? cardBrand = null;
        string? failureReason = null;

        if (root.TryGetProperty("latest_charge", out var lcEl) && lcEl.ValueKind == JsonValueKind.String)
        {
            chargeId = lcEl.GetString();
        }

        if (root.TryGetProperty("charges", out var chargesEl) &&
            chargesEl.TryGetProperty("data", out var dataArr) &&
            dataArr.GetArrayLength() > 0)
        {
            var firstCharge = dataArr[0];
            chargeId ??= firstCharge.TryGetProperty("id", out var chId) ? chId.GetString() : null;

            if (firstCharge.TryGetProperty("payment_method_details", out var pmd) &&
                pmd.TryGetProperty("card", out var cardEl))
            {
                last4 = cardEl.TryGetProperty("last4", out var l4) ? l4.GetString() : null;
                cardBrand = cardEl.TryGetProperty("brand", out var br) ? br.GetString() : null;
            }
        }

        if (root.TryGetProperty("last_payment_error", out var lpe))
        {
            failureReason = lpe.TryGetProperty("message", out var lpeMsg) ? lpeMsg.GetString() : null;
        }

        string? currency = root.TryGetProperty("currency", out var currEl) ? currEl.GetString() : "usd";
        Guid? patientId = null;
        if (root.TryGetProperty("metadata", out var metaEl) &&
            metaEl.TryGetProperty("patient_id", out var pidEl) &&
            Guid.TryParse(pidEl.GetString(), out var parsedPid))
        {
            patientId = parsedPid;
        }

        var isSucceeded = status == "succeeded";
        return new PaymentResult(isSucceeded, id, chargeId, amount, status, last4, cardBrand, isSucceeded ? null : failureReason, failureReason, Currency: currency, PatientId: patientId);
    }

    public async Task<RefundResult> RefundPaymentAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["payment_intent"] = request.PaymentIntentId
        };

        if (request.AmountCents.HasValue)
        {
            form["amount"] = request.AmountCents.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            form["reason"] = request.Reason;
        }

        using var httpRequest = CreateRequest(HttpMethod.Post, "v1/refunds", request.IdempotencyKey);
        httpRequest.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            var err = root.TryGetProperty("error", out var errEl) && errEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString()
                : "Failed to process refund with Stripe.";
            return new RefundResult(false, string.Empty, request.PaymentIntentId, request.ChargeId, request.AmountCents ?? 0, "failed", err);
        }

        var refundId = root.GetProperty("id").GetString()!;
        var piId = root.TryGetProperty("payment_intent", out var piEl) ? piEl.GetString() ?? request.PaymentIntentId : request.PaymentIntentId;
        var chId = root.TryGetProperty("charge", out var chEl) ? chEl.GetString() : request.ChargeId;
        var amount = root.GetProperty("amount").GetInt32();
        var status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() ?? "succeeded" : "succeeded";

        return new RefundResult(true, refundId, piId, chId, amount, status);
    }

    public WebhookVerificationResult VerifyWebhook(string rawPayload, string? signatureHeader, long toleranceSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Missing Stripe-Signature header.");
        }

        string webhookSecret;
        try
        {
            webhookSecret = GetWebhookSecret();
        }
        catch (Exception ex)
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, ex.Message);
        }

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
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Malformed Stripe-Signature header.");
        }

        // Verify timestamp tolerance (protect against replay attacks)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp.Value) > toleranceSeconds)
        {
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Timestamp outside tolerance window.");
        }

        // Calculate expected signature: HMAC-SHA256 of timestamp + "." + rawPayload
        var signedPayload = $"{timestamp.Value}.{rawPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
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
                var objectType = obj.TryGetProperty("object", out var oType) ? oType.GetString() : null;

                if (objectType == "payment_intent")
                {
                    paymentIntentId = obj.TryGetProperty("id", out var piId) ? piId.GetString() : null;
                    amount = obj.TryGetProperty("amount", out var am) ? am.GetInt32() : 0;
                    status = obj.TryGetProperty("status", out var st) ? st.GetString() : null;

                    if (obj.TryGetProperty("latest_charge", out var lc) && lc.ValueKind == JsonValueKind.String)
                    {
                        chargeId = lc.GetString();
                    }

                    if (obj.TryGetProperty("last_payment_error", out var lpe))
                    {
                        failureReason = lpe.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                    }
                }
                else if (objectType == "charge")
                {
                    chargeId = obj.TryGetProperty("id", out var chId) ? chId.GetString() : null;
                    paymentIntentId = obj.TryGetProperty("payment_intent", out var piId) ? piId.GetString() : null;
                    amount = obj.TryGetProperty("amount", out var am) ? am.GetInt32() : 0;
                    status = obj.TryGetProperty("status", out var st) ? st.GetString() : null;

                    if (obj.TryGetProperty("payment_method_details", out var pmd) &&
                        pmd.TryGetProperty("card", out var cardEl))
                    {
                        last4 = cardEl.TryGetProperty("last4", out var l4) ? l4.GetString() : null;
                        cardBrand = cardEl.TryGetProperty("brand", out var br) ? br.GetString() : null;
                    }

                    if (obj.TryGetProperty("failure_message", out var fm))
                    {
                        failureReason = fm.GetString();
                    }
                }
                else if (objectType == "refund")
                {
                    refundId = obj.TryGetProperty("id", out var refId) ? refId.GetString() : null;
                    paymentIntentId = obj.TryGetProperty("payment_intent", out var piId) ? piId.GetString() : null;
                    chargeId = obj.TryGetProperty("charge", out var chId) ? chId.GetString() : null;
                    amount = obj.TryGetProperty("amount", out var am) ? am.GetInt32() : 0;
                    status = obj.TryGetProperty("status", out var st) ? st.GetString() : null;
                }

                if (obj.TryGetProperty("metadata", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in metaEl.EnumerateObject())
                    {
                        metadata[prop.Name] = prop.Value.GetString() ?? string.Empty;
                        if (prop.Name.Equals("patientId", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(prop.Value.GetString(), out var parsedPid))
                        {
                            patientId = parsedPid;
                        }
                    }
                }
            }

            return new WebhookVerificationResult(
                true,
                eventId,
                eventType,
                paymentIntentId,
                chargeId,
                refundId,
                amount,
                status,
                last4,
                cardBrand,
                failureReason,
                patientId,
                metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse webhook JSON payload.");
            return new WebhookVerificationResult(false, null, null, null, null, null, 0, null, null, null, null, null, null, "Failed to parse webhook payload.");
        }
    }
}
