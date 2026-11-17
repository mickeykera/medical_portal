using System;
using System.Collections.Generic;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using Xunit;

namespace MedicalPortal.Tests;

/// <summary>
/// Proof tests for audit chain verification logic.
/// These tests verify the AuditLogHasher.VerifyIntegrity method directly
/// without requiring database infrastructure.
/// </summary>
public class AuditChainProofTests
{
    private static (bool IsValid, Guid? CorruptedId, string? Reason) VerifyChain(IReadOnlyList<AuditLog> logs)
    {
        var isValid = AuditLogHasher.VerifyIntegrity(logs, out var corruptedId, out var reason);
        return (isValid, corruptedId, reason);
    }

    [Fact]
    public async Task VerifyIntegrity_DetectsSequenceGap()
    {
        // Arrange: Create logs with a gap in sequence numbers (1, 2, 4)
        var logs = new List<AuditLog>
        {
            CreateTestLog(1, "ACTION_1", "GENESIS"),
            CreateTestLog(2, "ACTION_2", null),
            CreateTestLog(4, "ACTION_4", null), // Gap: missing sequence 3
        };

        // Compute hashes properly
        logs[0].PreviousHash = AuditLogHasher.GenesisHash;
        logs[0].Hash = AuditLogHasher.ComputeHash(logs[0], AuditLogHasher.GenesisHash);

        logs[1].PreviousHash = logs[0].Hash;
        logs[1].Hash = AuditLogHasher.ComputeHash(logs[1], logs[1].PreviousHash);

        logs[2].PreviousHash = logs[1].Hash;
        logs[2].Hash = AuditLogHasher.ComputeHash(logs[2], logs[2].PreviousHash);

        // Act
        var (isValid, corruptedId, reason) = VerifyChain(logs);

        // Assert
        Assert.False(isValid);
        Assert.Contains("Sequence gap", reason);
        Assert.Equal(logs[2].Id, corruptedId);
    }

    [Fact]
    public async Task VerifyIntegrity_DetectsHashMismatch()
    {
        // Arrange: Create valid chain then tamper with hash
        var logs = new List<AuditLog>
        {
            CreateTestLog(1, "ACTION_1", "GENESIS"),
            CreateTestLog(2, "ACTION_2", null),
        };

        logs[0].PreviousHash = AuditLogHasher.GenesisHash;
        logs[0].Hash = AuditLogHasher.ComputeHash(logs[0], AuditLogHasher.GenesisHash);

        logs[1].PreviousHash = logs[0].Hash;
        logs[1].Hash = AuditLogHasher.ComputeHash(logs[1], logs[1].PreviousHash);

        // Tamper: modify hash of first log
        logs[0].Hash = "tampered_hash";

        // Act
        var (isValid, corruptedId, reason) = VerifyChain(logs);

        // Assert
        Assert.False(isValid);
        Assert.Contains("Hash mismatch", reason);
        Assert.Equal(logs[0].Id, corruptedId);
    }

    [Fact]
    public async Task VerifyIntegrity_DetectsPreviousHashMismatch()
    {
        // Arrange: Create valid chain then tamper with PreviousHash
        var logs = new List<AuditLog>
        {
            CreateTestLog(1, "ACTION_1", "GENESIS"),
            CreateTestLog(2, "ACTION_2", null),
        };

        logs[0].PreviousHash = AuditLogHasher.GenesisHash;
        logs[0].Hash = AuditLogHasher.ComputeHash(logs[0], AuditLogHasher.GenesisHash);

        logs[1].PreviousHash = logs[0].Hash;
        logs[1].Hash = AuditLogHasher.ComputeHash(logs[1], logs[1].PreviousHash);

        // Tamper: modify PreviousHash of second log
        logs[1].PreviousHash = "wrong_previous_hash";

        // Act
        var (isValid, corruptedId, reason) = VerifyChain(logs);

        // Assert
        Assert.False(isValid);
        Assert.Contains("PreviousHash mismatch", reason);
        Assert.Equal(logs[1].Id, corruptedId);
    }

    [Fact]
    public async Task VerifyIntegrity_DetectsReorderedRows()
    {
        // Arrange: Create logs with sequence numbers 1, 3, 2 (reordered)
        var logs = new List<AuditLog>
        {
            CreateTestLog(1, "ACTION_1", "GENESIS"),
            CreateTestLog(3, "ACTION_3", null), // Out of order
            CreateTestLog(2, "ACTION_2", null),
        };

        logs[0].PreviousHash = AuditLogHasher.GenesisHash;
        logs[0].Hash = AuditLogHasher.ComputeHash(logs[0], AuditLogHasher.GenesisHash);

        logs[1].PreviousHash = logs[0].Hash;
        logs[1].Hash = AuditLogHasher.ComputeHash(logs[1], logs[1].PreviousHash);

        logs[2].PreviousHash = logs[1].Hash;
        logs[2].Hash = AuditLogHasher.ComputeHash(logs[2], logs[2].PreviousHash);

        // Act
        var (isValid, corruptedId, reason) = VerifyChain(logs);

        // Assert: Should detect either sequence gap or PreviousHash mismatch
        Assert.False(isValid);
        Assert.True(reason.Contains("Sequence gap") || reason.Contains("PreviousHash mismatch"));
    }

    [Fact]
    public async Task VerifyIntegrity_ValidChain_Passes()
    {
        // Arrange: Create valid chain with sequences 1, 2, 3
        var logs = new List<AuditLog>
        {
            CreateTestLog(1, "ACTION_1", "GENESIS"),
            CreateTestLog(2, "ACTION_2", null),
            CreateTestLog(3, "ACTION_3", null),
        };

        logs[0].PreviousHash = AuditLogHasher.GenesisHash;
        logs[0].Hash = AuditLogHasher.ComputeHash(logs[0], AuditLogHasher.GenesisHash);

        logs[1].PreviousHash = logs[0].Hash;
        logs[1].Hash = AuditLogHasher.ComputeHash(logs[1], logs[1].PreviousHash);

        logs[2].PreviousHash = logs[1].Hash;
        logs[2].Hash = AuditLogHasher.ComputeHash(logs[2], logs[2].PreviousHash);

        // Act
        var (isValid, corruptedId, reason) = VerifyChain(logs);

        // Assert
        Assert.True(isValid);
        Assert.Null(corruptedId);
        Assert.Null(reason);
    }

    [Fact]
    public async Task VerifyIntegrity_PlatformChain_SeparateFromTenantChain()
    {
        // Arrange: Create platform chain and tenant chain
        var platformLogs = new List<AuditLog>
        {
            CreateTestLog(1, "LOGIN", "GENESIS", TenantConstants.PlatformTenantId),
            CreateTestLog(2, "LOGIN_FAILED", null, TenantConstants.PlatformTenantId),
        };

        platformLogs[0].PreviousHash = AuditLogHasher.GenesisHash;
        platformLogs[0].Hash = AuditLogHasher.ComputeHash(platformLogs[0], AuditLogHasher.GenesisHash);

        platformLogs[1].PreviousHash = platformLogs[0].Hash;
        platformLogs[1].Hash = AuditLogHasher.ComputeHash(platformLogs[1], platformLogs[1].PreviousHash);

        var tenantLogs = new List<AuditLog>
        {
            CreateTestLog(1, "PHI_READ", "GENESIS", TenantConstants.DefaultTenantId),
            CreateTestLog(2, "PHI_READ", null, TenantConstants.DefaultTenantId),
        };

        tenantLogs[0].PreviousHash = AuditLogHasher.GenesisHash;
        tenantLogs[0].Hash = AuditLogHasher.ComputeHash(tenantLogs[0], AuditLogHasher.GenesisHash);

        tenantLogs[1].PreviousHash = tenantLogs[0].Hash;
        tenantLogs[1].Hash = AuditLogHasher.ComputeHash(tenantLogs[1], tenantLogs[1].PreviousHash);

        // Act: Verify combined logs - should verify both chains independently
        var allLogs = new List<AuditLog>();
        allLogs.AddRange(platformLogs);
        allLogs.AddRange(tenantLogs);

        var (isValid, corruptedId, reason) = VerifyChain(allLogs);

        // Assert
        Assert.True(isValid, $"Platform and tenant chains should verify independently: {reason}");
    }

    [Fact]
    public async Task VerifyIntegrity_LegacyDefaultTenantChain_VerifiedUnderV1Rule()
    {
        // Arrange: Legacy logs with DefaultTenantId (pre-migration) verified under v1 rule
        var legacyLogs = new List<AuditLog>
        {
            CreateTestLog(1, "LEGACY_1", "GENESIS", TenantConstants.DefaultTenantId),
            CreateTestLog(2, "LEGACY_2", null, TenantConstants.DefaultTenantId),
        };

        legacyLogs[0].PreviousHash = AuditLogHasher.GenesisHash;
        legacyLogs[0].Hash = AuditLogHasher.ComputeHash(legacyLogs[0], AuditLogHasher.GenesisHash);

        legacyLogs[1].PreviousHash = legacyLogs[0].Hash;
        legacyLogs[1].Hash = AuditLogHasher.ComputeHash(legacyLogs[1], legacyLogs[1].PreviousHash);

        // Act
        var (isValid, corruptedId, reason) = VerifyChain(legacyLogs);

        // Assert: Should pass as legacy chain
        Assert.True(isValid, $"Legacy default tenant chain should verify under v1 rule: {reason}");
    }

    private static AuditLog CreateTestLog(long sequence, string action, string? previousHash, Guid? tenantId = null)
    {
        return new AuditLog
        {
            SequenceNumber = sequence,
            Action = action,
            ResourceType = "Test",
            ResourceId = Guid.NewGuid().ToString(),
            PatientId = Guid.NewGuid(),
            ActorUserAccountId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            PreviousHash = previousHash ?? string.Empty,
            TenantId = tenantId ?? TenantConstants.DefaultTenantId,
            IsProxy = false,
            IpAddress = "127.0.0.1",
            UserAgent = "test-agent",
            MetadataJson = "{}"
        };
    }
}
