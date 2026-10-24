using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedicalPortal.Infrastructure.Hms;

public class HmsWebhookHandler : IHmsWebhookHandler
{
    private readonly MedicalPortalDbContext _db;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HmsWebhookHandler> _logger;

    public HmsWebhookHandler(
        MedicalPortalDbContext db,
        IConfiguration config,
        IServiceProvider serviceProvider,
        ILogger<HmsWebhookHandler>? logger = null)
    {
        _db = db;
        _config = config;
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<HmsWebhookHandler>.Instance;
    }

    private List<string> GetActiveWebhookSecrets()
    {
        var secretsList = new List<string>();
        var multiSecrets = _config["Hms:WebhookSecrets"] ?? _config["HMS_WEBHOOK_SECRETS"];
        if (!string.IsNullOrWhiteSpace(multiSecrets))
        {
            var parts = multiSecrets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            secretsList.AddRange(parts);
        }

        var singleSecret = _config["Hms:WebhookSecret"] ?? _config["HMS_WEBHOOK_SECRET"];
        if (!string.IsNullOrWhiteSpace(singleSecret) && !secretsList.Contains(singleSecret))
        {
            secretsList.Add(singleSecret);
        }

        if (secretsList.Count == 0)
        {
            secretsList.Add("demo-hms-webhook-secret");
        }

        return secretsList;
    }

    public async Task<HmsWebhookValidationResult> ProcessWebhookAsync(
        string signatureHeader,
        string rawBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            _logger.LogWarning("HMS webhook missing signature header.");
            return new HmsWebhookValidationResult(false, "Missing signature header");
        }

        // Parse signature header: "t=1729720000,v1=abcdef..."
        long timestamp = 0;
        string? signatureHex = null;

        var parts = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kvp = part.Split('=', 2);
            if (kvp.Length == 2)
            {
                var k = kvp[0].Trim();
                var v = kvp[1].Trim();
                if (string.Equals(k, "t", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(v, out timestamp);
                }
                else if (string.Equals(k, "v1", StringComparison.OrdinalIgnoreCase))
                {
                    signatureHex = v;
                }
            }
        }

        if (timestamp == 0 || string.IsNullOrWhiteSpace(signatureHex))
        {
            _logger.LogWarning("HMS webhook invalid signature format: {Header}", signatureHeader);
            return new HmsWebhookValidationResult(false, "Invalid signature header format");
        }

        // Replay protection: 5 minutes (300 seconds)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > 300)
        {
            _logger.LogWarning("HMS webhook timestamp expired or drifted. Timestamp: {Ts}, Now: {Now}", timestamp, now);
            return new HmsWebhookValidationResult(false, "Signature timestamp expired or invalid");
        }

        // Compute HMAC-SHA256 over "{t}.{rawBody}"
        var messageToSign = $"{timestamp}.{rawBody}";
        var messageBytes = Encoding.UTF8.GetBytes(messageToSign);

        byte[]? expectedSigBytes = null;
        try
        {
            expectedSigBytes = Convert.FromHexString(signatureHex);
        }
        catch
        {
            return new HmsWebhookValidationResult(false, "Malformed signature hex");
        }

        var secrets = GetActiveWebhookSecrets();
        bool signatureValid = false;

        foreach (var secret in secrets)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var computedHash = hmac.ComputeHash(messageBytes);
            if (CryptographicOperations.FixedTimeEquals(computedHash, expectedSigBytes))
            {
                signatureValid = true;
                break;
            }
        }

        if (!signatureValid)
        {
            _logger.LogWarning("HMS webhook signature verification failed.");
            return new HmsWebhookValidationResult(false, "Invalid signature");
        }

        // Parse payload JSON
        HmsWebhookPayloadDto? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<HmsWebhookPayloadDto>(rawBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse HMS webhook payload JSON.");
            return new HmsWebhookValidationResult(false, "Invalid JSON payload");
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.EventId))
        {
            return new HmsWebhookValidationResult(false, "Missing eventId in payload");
        }

        // Deduplication on eventId
        var alreadyProcessed = await _db.HmsProcessedWebhookEvents
            .AnyAsync(e => e.EventId == payload.EventId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation("HMS webhook event {EventId} already processed (idempotent duplicate).", payload.EventId);
            return new HmsWebhookValidationResult(true, IsDuplicate: true, Payload: payload);
        }

        // Persist processed event ID
        _db.HmsProcessedWebhookEvents.Add(new HmsProcessedWebhookEvent
        {
            EventId = payload.EventId,
            EventType = payload.Event,
            HmsMrn = payload.HmsMrn,
            ProcessedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("HMS webhook received event {EventId} (Type: {Type}, MRN: {HmsMrn}).",
            payload.EventId, payload.Event, payload.HmsMrn);

        // Accelerate sync for this patient (trigger priority sync)
        if (!string.IsNullOrWhiteSpace(payload.HmsMrn))
        {
            var syncService = _serviceProvider.GetService<IHmsSyncService>();
            if (syncService != null)
            {
                var patient = await _db.Patients.FirstOrDefaultAsync(p => p.HmsMrn == payload.HmsMrn, cancellationToken);
                if (patient != null)
                {
                    try
                    {
                        await syncService.SyncPatientAsync(patient.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing patient {PatientId} following webhook {EventId}", patient.Id, payload.EventId);
                    }
                }
            }
        }

        return new HmsWebhookValidationResult(true, IsDuplicate: false, Payload: payload);
    }
}
