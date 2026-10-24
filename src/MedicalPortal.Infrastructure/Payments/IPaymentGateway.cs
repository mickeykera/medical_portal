namespace MedicalPortal.Infrastructure.Payments;

public record PaymentIntentRequest(
    int AmountCents,
    string Currency = "usd",
    Guid? PatientId = null,
    string? Description = null,
    string? IdempotencyKey = null,
    Dictionary<string, string>? Metadata = null);

public record PaymentIntentResponse(
    bool Success,
    string PaymentIntentId,
    string ClientSecret,
    int AmountCents,
    string Currency,
    string Status,
    string? ErrorMessage = null);

public record ConfirmPaymentRequest(
    string PaymentIntentId,
    string? PaymentMethodToken = null,
    string? IdempotencyKey = null);

public record PaymentResult(
    bool Success,
    string PaymentIntentId,
    string? ChargeId,
    int AmountCents,
    string Status,
    string? Last4,
    string? CardBrand,
    string? ErrorMessage = null,
    string? FailureReason = null);

public record RefundRequest(
    string PaymentIntentId,
    string? ChargeId = null,
    int? AmountCents = null,
    string? Reason = null,
    string? IdempotencyKey = null);

public record RefundResult(
    bool Success,
    string RefundId,
    string PaymentIntentId,
    string? ChargeId,
    int AmountCents,
    string Status,
    string? ErrorMessage = null);

public record WebhookVerificationResult(
    bool IsValid,
    string? EventId,
    string? EventType,
    string? PaymentIntentId,
    string? ChargeId,
    string? RefundId,
    int AmountCents,
    string? Status,
    string? Last4,
    string? CardBrand,
    string? FailureReason,
    Guid? PatientId,
    Dictionary<string, string>? Metadata,
    string? ErrorMessage = null);

public interface IPaymentGateway
{
    Task<PaymentIntentResponse> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentResult> ConfirmPaymentAsync(ConfirmPaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentResult> GetPaymentDetailsAsync(string paymentIntentId, CancellationToken cancellationToken = default);

    Task<RefundResult> RefundPaymentAsync(RefundRequest request, CancellationToken cancellationToken = default);

    WebhookVerificationResult VerifyWebhook(string rawPayload, string? signatureHeader, long toleranceSeconds = 300);
}
