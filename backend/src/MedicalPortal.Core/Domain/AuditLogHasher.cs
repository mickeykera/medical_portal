using System.Security.Cryptography;
using System.Text;
using MedicalPortal.Core.Entities;

namespace MedicalPortal.Core.Domain;

public static class AuditLogHasher
{
    public const string GenesisHash = "GENESIS";

    public static string ComputeHash(AuditLog log, string? previousHash)
    {
        var prev = string.IsNullOrEmpty(previousHash) ? GenesisHash : previousHash;
        var createdAtUtc = DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc);
        var createdAtStr = createdAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

        var payload = string.Join("|",
            log.SequenceNumber.ToString(),
            prev,
            log.Id.ToString("D"),
            createdAtStr,
            log.ActorUserAccountId?.ToString("D") ?? "",
            log.PatientId?.ToString("D") ?? "",
            log.ActingForPersonId?.ToString("D") ?? "",
            log.IsProxy ? "1" : "0",
            log.Action ?? "",
            log.ResourceType ?? "",
            log.ResourceId ?? "",
            log.IpAddress ?? "",
            log.UserAgent ?? "",
            log.MetadataJson ?? ""
        );

        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies the integrity of audit log chains per tenant.
    /// Platform chain (TenantId == PlatformTenantId) is verified separately from tenant chains.
    /// Legacy audit rows (pre-migration) with TenantId == DefaultTenantId and no PlatformTenantId
    /// are verified under the original v1 rule: global sequence continuity across all tenants.
    /// </summary>
    public static bool VerifyIntegrity(IReadOnlyList<AuditLog> orderedLogs, out Guid? corruptedLogId, out string? failureReason)
    {
        // Separate platform chain from tenant chains
        var platformLogs = orderedLogs.Where(l => l.TenantId == TenantConstants.PlatformTenantId).ToList();
        var tenantLogs = orderedLogs.Where(l => l.TenantId != TenantConstants.PlatformTenantId).ToList();
        var legacyDefaultLogs = tenantLogs.Where(l => l.TenantId == TenantConstants.DefaultTenantId).ToList();
        var otherTenantLogs = tenantLogs.Where(l => l.TenantId != TenantConstants.DefaultTenantId).ToList();

        // 1. Verify platform chain (if any)
        if (platformLogs.Count > 0)
        {
            if (!VerifyChain(platformLogs.OrderBy(l => l.SequenceNumber).ToList(), out corruptedLogId, out failureReason, "platform"))
            {
                return false;
            }
        }

        // 2. Verify legacy default tenant chain (v1 rule: global sequence for DefaultTenantId)
        // This handles pre-migration audit rows that were written to DefaultTenantId before platform chain separation.
        if (legacyDefaultLogs.Count > 0)
        {
            if (!VerifyChain(legacyDefaultLogs.OrderBy(l => l.SequenceNumber).ToList(), out corruptedLogId, out failureReason, "legacy default tenant"))
            {
                return false;
            }
        }

        // 3. Verify other tenant chains (per-tenant sequence)
        var logsByTenant = otherTenantLogs.GroupBy(l => l.TenantId).OrderBy(g => g.Key);
        foreach (var tenantGroup in logsByTenant)
        {
            var tenantLogsList = tenantGroup.OrderBy(l => l.SequenceNumber).ToList();
            if (!VerifyChain(tenantLogsList, out corruptedLogId, out failureReason, $"tenant {tenantGroup.Key}"))
            {
                return false;
            }
        }

        corruptedLogId = null;
        failureReason = null;
        return true;
    }

    private static bool VerifyChain(List<AuditLog> chain, out Guid? corruptedLogId, out string? failureReason, string chainName)
    {
        string? expectedPrev = null;
        long? expectedSeq = null;

        for (int i = 0; i < chain.Count; i++)
        {
            var log = chain[i];

            if (i == 0)
            {
                expectedPrev = log.PreviousHash;
                expectedSeq = log.SequenceNumber;
            }
            else
            {
                if (expectedSeq.HasValue && log.SequenceNumber != expectedSeq.Value + 1)
                {
                    corruptedLogId = log.Id;
                    failureReason = $"{chainName}: Sequence gap or tampering detected on record {log.Id}. Expected SequenceNumber: {expectedSeq.Value + 1}, Got: {log.SequenceNumber}";
                    return false;
                }

                if (log.PreviousHash != expectedPrev)
                {
                    corruptedLogId = log.Id;
                    failureReason = $"{chainName}: PreviousHash mismatch on record {log.Id}. Expected: '{expectedPrev}', Got: '{log.PreviousHash}'";
                    return false;
                }
            }

            var computed = ComputeHash(log, log.PreviousHash);
            if (!string.Equals(computed, log.Hash, StringComparison.OrdinalIgnoreCase))
            {
                corruptedLogId = log.Id;
                failureReason = $"{chainName}: Hash mismatch on record {log.Id}. Computed: '{computed}', Stored: '{log.Hash}'";
                return false;
            }

            expectedPrev = log.Hash;
            expectedSeq = log.SequenceNumber;
        }

        corruptedLogId = null;
        failureReason = null;
        return true;
    }
}
