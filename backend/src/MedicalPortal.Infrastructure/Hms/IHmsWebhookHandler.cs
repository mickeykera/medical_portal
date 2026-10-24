namespace MedicalPortal.Infrastructure.Hms;

public record HmsWebhookValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    bool IsDuplicate = false,
    HmsWebhookPayloadDto? Payload = null);

public interface IHmsWebhookHandler
{
    Task<HmsWebhookValidationResult> ProcessWebhookAsync(
        string signatureHeader,
        string rawBody,
        CancellationToken cancellationToken = default);
}
