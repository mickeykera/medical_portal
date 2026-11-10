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

    public static bool VerifyIntegrity(IReadOnlyList<AuditLog> orderedLogs, out Guid? corruptedLogId, out string? failureReason)
    {
        var logsByTenant = orderedLogs.GroupBy(l => l.TenantId).OrderBy(g => g.Key);

        foreach (var tenantGroup in logsByTenant)
        {
            var tenantLogs = tenantGroup.OrderBy(l => l.SequenceNumber).ToList();
            string? expectedPrev = null;
            long? expectedSeq = null;

            for (int i = 0; i < tenantLogs.Count; i++)
            {
                var log = tenantLogs[i];

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
                        failureReason = $"Sequence gap or tampering detected on record {log.Id} (tenant {log.TenantId}). Expected SequenceNumber: {expectedSeq.Value + 1}, Got: {log.SequenceNumber}";
                        return false;
                    }

                    if (log.PreviousHash != expectedPrev)
                    {
                        corruptedLogId = log.Id;
                        failureReason = $"PreviousHash mismatch on record {log.Id} (tenant {log.TenantId}). Expected: '{expectedPrev}', Got: '{log.PreviousHash}'";
                        return false;
                    }
                }

                var computed = ComputeHash(log, log.PreviousHash);
                if (!string.Equals(computed, log.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    corruptedLogId = log.Id;
                    failureReason = $"Hash mismatch on record {log.Id} (tenant {log.TenantId}). Computed: '{computed}', Stored: '{log.Hash}'";
                    return false;
                }

                expectedPrev = log.Hash;
                expectedSeq = log.SequenceNumber;
            }
        }

        corruptedLogId = null;
        failureReason = null;
        return true;
    }
}
