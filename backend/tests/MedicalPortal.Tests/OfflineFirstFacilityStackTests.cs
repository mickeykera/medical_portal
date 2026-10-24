using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Offline;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedicalPortal.Tests;

public class OfflineFirstFacilityStackTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OfflineFirstFacilityStackTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static (
        MedicalPortalDbContext Db,
        FakeCloudAggregatorClient FakeAggregator,
        OutboxSyncDispatcher Dispatcher,
        ReferenceDataService ReferenceService,
        IOptions<FacilityNodeOptions> NodeOptions
    ) CreateHarness(ITenantContext? tenantContext = null)
    {
        var dbName = $"OfflineStackTestDb_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var db = new MedicalPortalDbContext(options, tenantContext);
        var fakeAggregator = new FakeCloudAggregatorClient();
        var nodeOptions = Options.Create(new FacilityNodeOptions
        {
            NodeRole = DeploymentNodeRole.Facility,
            FacilityCode = "ADOT-01",
            FacilityName = "Adot Internal Medicine",
            AggregatorEndpoint = "https://aggregator.health.gov.et/api/sync",
            InitialBackoffSeconds = 2,
            MaxBackoffSeconds = 60,
            MaxAttempts = 3,
            ReferencePackageSecret = "Test-Ethiopia-MOH-Signing-Secret-2026",
            UseFakeAggregator = true
        });

        var dispatcher = new OutboxSyncDispatcher(
            db,
            fakeAggregator,
            nodeOptions,
            NullLogger<OutboxSyncDispatcher>.Instance
        );

        var refService = new ReferenceDataService(
            db,
            fakeAggregator,
            nodeOptions,
            NullLogger<ReferenceDataService>.Instance
        );

        return (db, fakeAggregator, dispatcher, refService, nodeOptions);
    }

    // =========================================================================
    // 1. 24-HOUR NETWORK CUT CHAOS SIMULATOR & TRANSACTIONAL QUEUE ACCUMULATION
    // =========================================================================

    [Fact]
    public async Task ChaosSimulation_24HourNetworkCut_AccumulatesOutboxWithoutDataLossOrLocalBlock()
    {
        var (db, fakeAggregator, dispatcher, _, _) = CreateHarness();

        // 1. Sever network link to regional aggregator
        fakeAggregator.IsOnline = false;

        // 2. Perform 5 local business operations on facility LAN during network cut
        var patientId = Guid.NewGuid();
        for (int i = 1; i <= 5; i++)
        {
            var msg = await dispatcher.EnqueueMessageAsync(
                eventType: $"Observation.Recorded.{i}",
                resourceType: "Observation",
                payloadJson: $"{{\"code\":\"1558-6\",\"value\":{100 + i}}}",
                patientId: patientId,
                customIdempotencyKey: $"chaos-event-{i}",
                saveImmediately: true
            );
            Assert.Equal(OutboxMessageStatus.Pending, msg.Status);
            Assert.Equal(i, msg.PerPatientSequence);
            Assert.Equal(i, msg.SequenceNumber);
        }

        // Assert 5 messages in local database
        var totalEnqueued = await db.OutboxMessages.CountAsync();
        Assert.Equal(5, totalEnqueued);

        // 3. Dispatcher attempts sync while network is severed
        var report = await dispatcher.DispatchPendingMessagesAsync();

        // Must report link unavailable and zero messages delivered
        Assert.False(report.LinkAvailable);
        Assert.Equal(0, report.Delivered);
        Assert.Equal(5, report.RemainingQueueDepth);

        // Assert local database records remain intact and pending
        var pendingCount = await db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Pending);
        Assert.Equal(5, pendingCount);
        Assert.Empty(fakeAggregator.ReceivedMessages);
    }

    // =========================================================================
    // 2. NETWORK RESTORATION & AUTOMATED QUEUE DRAINING
    // =========================================================================

    [Fact]
    public async Task ChaosSimulation_NetworkRestoration_DrainsAccumulatedQueueInOrder()
    {
        var (db, fakeAggregator, dispatcher, _, _) = CreateHarness();

        // Start offline
        fakeAggregator.IsOnline = false;
        var patientId = Guid.NewGuid();

        for (int i = 1; i <= 4; i++)
        {
            await dispatcher.EnqueueMessageAsync(
                eventType: $"Appointment.Event.{i}",
                resourceType: "Appointment",
                payloadJson: $"{{\"status\":\"confirmed\",\"seq\":{i}}}",
                patientId: patientId,
                customIdempotencyKey: $"restore-event-{i}",
                saveImmediately: true
            );
        }

        // Try dispatch while offline
        await dispatcher.DispatchPendingMessagesAsync();
        Assert.Equal(4, await db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Pending));

        // Network restored!
        fakeAggregator.IsOnline = true;

        // Drain queue
        var report = await dispatcher.DispatchPendingMessagesAsync();

        Assert.True(report.LinkAvailable);
        Assert.Equal(4, report.Processed);
        Assert.Equal(4, report.Delivered);
        Assert.Equal(0, report.RemainingQueueDepth);

        // All DB messages transitioned to Delivered
        var deliveredCount = await db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Delivered);
        Assert.Equal(4, deliveredCount);

        // All 4 messages arrived at aggregator
        Assert.Equal(4, fakeAggregator.ReceivedMessages.Count);
    }

    // =========================================================================
    // 3. PER-PATIENT STRICT FIFO ORDERING BARRIER
    // =========================================================================

    [Fact]
    public async Task PerPatientOrdering_StrictFifoEnforced_SubsequentEventsBlockedUntilPriorSucceeds()
    {
        var (db, fakeAggregator, dispatcher, _, _) = CreateHarness();

        var patientA = Guid.NewGuid();
        var patientB = Guid.NewGuid();

        // Enqueue 3 events for Patient A
        var msgA1 = await dispatcher.EnqueueMessageAsync("Patient.Registered", "Patient", "{}", patientA, "msg-a1");
        var msgA2 = await dispatcher.EnqueueMessageAsync("Appointment.Booked", "Appointment", "{}", patientA, "msg-a2");
        var msgA3 = await dispatcher.EnqueueMessageAsync("Appointment.Cancelled", "Appointment", "{}", patientA, "msg-a3");

        // Enqueue 1 event for Patient B
        var msgB1 = await dispatcher.EnqueueMessageAsync("Patient.Registered", "Patient", "{}", patientB, "msg-b1");

        // Block Patient A's first message by simulating a transient failure scheduled in the future
        msgA1.Status = OutboxMessageStatus.FailedTransient;
        msgA1.ScheduledAtUtc = DateTime.UtcNow.AddMinutes(10);
        msgA1.AttemptCount = 1;
        await db.SaveChangesAsync();

        // Execute dispatch
        var report = await dispatcher.DispatchPendingMessagesAsync();

        // Patient B's event should succeed!
        // But Patient A's msgA2 and msgA3 MUST NOT be dispatched because msgA1 is blocked
        Assert.Contains(fakeAggregator.ReceivedMessages, m => m.IdempotencyKey == "msg-b1");
        Assert.DoesNotContain(fakeAggregator.ReceivedMessages, m => m.IdempotencyKey == "msg-a2");
        Assert.DoesNotContain(fakeAggregator.ReceivedMessages, m => m.IdempotencyKey == "msg-a3");

        // Now unblock msgA1 (set scheduled time to past)
        msgA1.ScheduledAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        // Re-dispatch: now msgA1, msgA2, msgA3 must be dispatched in exact order
        var report2 = await dispatcher.DispatchPendingMessagesAsync();
        Assert.Equal(3, report2.Delivered);

        var seqs = fakeAggregator.PerPatientSequencesReceived[patientA];
        Assert.Equal(new List<long> { 1, 2, 3 }, seqs);
    }

    // =========================================================================
    // 4. IDEMPOTENCY DEDUPLICATION CONTRACT (REPLAY RESILIENCE)
    // =========================================================================

    [Fact]
    public async Task IdempotencyDeduplication_ReplayedMessages_MarkedDeliveredWithoutError()
    {
        var (db, fakeAggregator, dispatcher, _, _) = CreateHarness();

        var msg = await dispatcher.EnqueueMessageAsync("Encounter.Recorded", "Encounter", "{}", null, "dedup-test-key");

        // First dispatch -> 200 OK
        var report1 = await dispatcher.DispatchPendingMessagesAsync();
        Assert.Equal(1, report1.Delivered);
        Assert.Equal(OutboxMessageStatus.Delivered, msg.Status);
        Assert.StartsWith("ACK-", msg.AggregatorAckId);

        // Reset message to Pending to simulate replay
        msg.Status = OutboxMessageStatus.Pending;
        await db.SaveChangesAsync();

        // Re-dispatch: aggregator will return 409 Conflict (Duplicate idempotency key)
        var report2 = await dispatcher.DispatchPendingMessagesAsync();

        Assert.Equal(1, report2.Delivered);
        Assert.Equal(0, report2.Failed);
        Assert.Equal(OutboxMessageStatus.Delivered, msg.Status);
        Assert.Contains("409", msg.LastError);
    }

    // =========================================================================
    // 5. EXPONENTIAL BACKOFF & DEAD-LETTER THRESHOLD
    // =========================================================================

    [Fact]
    public async Task ExponentialBackoff_ScheduledDelayIncreases_TransitionsToDeadLetterAtMaxAttempts()
    {
        var (db, fakeAggregator, dispatcher, _, _) = CreateHarness();

        // Simulate 503 flapping
        fakeAggregator.SimulateFlapping503 = true;

        var msg = await dispatcher.EnqueueMessageAsync("Payment.Recorded", "Payment", "{}", null, "backoff-test-key");

        // Cycle 1: attempt 1 fails -> FailedTransient, scheduled in future
        await dispatcher.DispatchPendingMessagesAsync();
        Assert.Equal(1, msg.AttemptCount);
        Assert.Equal(OutboxMessageStatus.FailedTransient, msg.Status);
        Assert.True(msg.ScheduledAtUtc > DateTime.UtcNow);

        // Fast-forward scheduled time and attempt 2
        msg.ScheduledAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        await dispatcher.DispatchPendingMessagesAsync();
        Assert.Equal(2, msg.AttemptCount);
        Assert.Equal(OutboxMessageStatus.FailedTransient, msg.Status);

        // Fast-forward and attempt 3 (MaxAttempts is 3)
        msg.ScheduledAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        await dispatcher.DispatchPendingMessagesAsync();

        Assert.Equal(3, msg.AttemptCount);
        Assert.Equal(OutboxMessageStatus.DeadLetter, msg.Status);
        Assert.Contains("Exceeded maximum attempts", msg.LastError);
    }

    // =========================================================================
    // 6. REFERENCE PACKAGE CRYPTOGRAPHIC SIGNATURE VERIFICATION
    // =========================================================================

    [Fact]
    public async Task ReferencePackage_ValidSignature_InstallsSuccessfully()
    {
        var (db, _, _, refService, nodeOptions) = CreateHarness();

        var secret = nodeOptions.Value.ReferencePackageSecret;
        var validPkg = FakeCloudAggregatorClient.CreateSignedPackage(
            "ethiopia-icd10-subset",
            "Ethiopia ICD-10 Clinical Subset",
            "2026.1.0",
            100,
            "{\"terms\":[{\"code\":\"A00\",\"display\":\"Cholera\"}]}",
            secret
        );

        var result = await refService.ValidateAndInstallPackageAsync(validPkg);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);

        var installed = await db.ReferencePackages.FirstOrDefaultAsync(p => p.PackageId == "ethiopia-icd10-subset");
        Assert.NotNull(installed);
        Assert.True(installed.IsActive);
        Assert.Equal(ReferencePackageStatus.Installed, installed.Status);
    }

    [Fact]
    public async Task ReferencePackage_TamperedPayload_RejectedWithSignatureMismatch()
    {
        var (db, _, _, refService, nodeOptions) = CreateHarness();

        var secret = nodeOptions.Value.ReferencePackageSecret;
        var pkg = FakeCloudAggregatorClient.CreateSignedPackage(
            "ethiopia-icd10-subset",
            "Ethiopia ICD-10 Clinical Subset",
            "2026.1.0",
            100,
            "{\"terms\":[{\"code\":\"A00\",\"display\":\"Cholera\"}]}",
            secret
        );

        // Tamper with payload without updating signature
        var tamperedPkg = pkg with { PayloadJson = "{\"terms\":[{\"code\":\"A00\",\"display\":\"MALICIOUS_TAMPER\"}]}" };

        var result = await refService.ValidateAndInstallPackageAsync(tamperedPkg);

        Assert.False(result.Success);
        Assert.Contains("hash mismatch", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var notInstalled = await db.ReferencePackages.FirstOrDefaultAsync(p => p.PackageId == "ethiopia-icd10-subset");
        Assert.Null(notInstalled);
    }

    // =========================================================================
    // 7. REFERENCE PACKAGE ANTI-DOWNGRADE PROTECTION
    // =========================================================================

    [Fact]
    public async Task ReferencePackage_VersionDowngrade_RejectedWithAntiDowngradeError()
    {
        var (db, _, _, refService, nodeOptions) = CreateHarness();
        var secret = nodeOptions.Value.ReferencePackageSecret;

        // Install version 2026.1.0 with ordinal 100
        var v1 = FakeCloudAggregatorClient.CreateSignedPackage("mfr-facilities", "MFR", "2026.1.0", 100, "{}", secret);
        var r1 = await refService.ValidateAndInstallPackageAsync(v1);
        Assert.True(r1.Success);

        // Attempt to install version 2025.4.0 with lower ordinal 90
        var vOld = FakeCloudAggregatorClient.CreateSignedPackage("mfr-facilities", "MFR", "2025.4.0", 90, "{}", secret);
        var rOld = await refService.ValidateAndInstallPackageAsync(vOld);
        Assert.False(rOld.Success);
        Assert.Contains("downgrade rejected", rOld.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Attempt to re-install identical ordinal 100
        var rReinstall = await refService.ValidateAndInstallPackageAsync(v1);
        Assert.False(rReinstall.Success);
        Assert.Contains("downgrade rejected", rReinstall.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Install higher version 2026.2.0 with ordinal 110
        var vNew = FakeCloudAggregatorClient.CreateSignedPackage("mfr-facilities", "MFR", "2026.2.0", 110, "{}", secret);
        var rNew = await refService.ValidateAndInstallPackageAsync(vNew);
        Assert.True(rNew.Success);

        // Assert v1 is now Deprecated / Inactive and vNew is Active
        var activePkg = await db.ReferencePackages.SingleAsync(p => p.PackageId == "mfr-facilities" && p.IsActive);
        Assert.Equal("2026.2.0", activePkg.Version);
        Assert.Equal(110, activePkg.VersionOrdinal);

        var deprecatedPkg = await db.ReferencePackages.SingleAsync(p => p.PackageId == "mfr-facilities" && !p.IsActive);
        Assert.Equal("2026.1.0", deprecatedPkg.Version);
        Assert.Equal(ReferencePackageStatus.Deprecated, deprecatedPkg.Status);
    }

    // =========================================================================
    // 8. CROSS-TENANT OUTBOX ISOLATION (NEGATIVE SECURITY TEST)
    // =========================================================================

    [Fact]
    public async Task CrossTenantOutbox_QueryFilter_HidesMessagesFromOtherTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var dbName = $"TenantIsolationTestDb_{Guid.NewGuid():N}";
        var dbOptions = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Populate message in Tenant A
        var contextA = new TenantContext();
        contextA.SetTenant(new Tenant { Id = tenantA, Slug = "tenant-a", Name = "Tenant A" });
        using (var dbA = new MedicalPortalDbContext(dbOptions, contextA))
        {
            dbA.OutboxMessages.Add(new SyncOutboxMessage
            {
                TenantId = tenantA,
                EventType = "TenantA.Event",
                PayloadJson = "{}",
                IdempotencyKey = "tenant-a-msg"
            });
            await dbA.SaveChangesAsync();
        }

        // Query from Tenant B context
        var contextB = new TenantContext();
        contextB.SetTenant(new Tenant { Id = tenantB, Slug = "tenant-b", Name = "Tenant B" });
        using (var dbB = new MedicalPortalDbContext(dbOptions, contextB))
        {
            var visibleMessages = await dbB.OutboxMessages.ToListAsync();
            Assert.Empty(visibleMessages); // Global query filter prevents cross-tenant visibility!

            // Attempt cross-tenant insert
            var crossMsg = new SyncOutboxMessage
            {
                TenantId = tenantA, // trying to insert for tenant A while in tenant B context
                EventType = "CrossTenantAttack",
                PayloadJson = "{}",
                IdempotencyKey = "cross-attack-msg"
            };
            dbB.OutboxMessages.Add(crossMsg);

            await Assert.ThrowsAsync<System.Security.SecurityException>(() => dbB.SaveChangesAsync());
        }
    }

    // =========================================================================
    // 9. API ENDPOINTS: /api/system/node-status AND /api/system/outbox/drain-sync
    // =========================================================================

    [Fact]
    public async Task SystemNodeController_GetNodeStatus_ReturnsNodeRoleAndQueueDepth()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/system/node-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("nodeRole", out var roleProp));
        Assert.True(doc.RootElement.TryGetProperty("facilityCode", out var codeProp));
        Assert.True(doc.RootElement.TryGetProperty("outbox", out var outboxProp));
        Assert.True(outboxProp.TryGetProperty("totalDepth", out _));
    }

    [Fact]
    public async Task SystemNodeController_DrainOutboxSync_RequiresAdmin_RejectsUnauthorized()
    {
        var client = _factory.CreateClient();

        // 1. Unauthenticated request -> 401
        var unauthRes = await client.PostAsync("/api/system/outbox/drain-sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Patient role -> 403 Forbidden
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var (patientAccount, patientToken) = await CreateTestUserAsync(db, tokenService, UserRole.PATIENT);
        var patientReq = new HttpRequestMessage(HttpMethod.Post, "/api/system/outbox/drain-sync");
        patientReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        var patientRes = await client.SendAsync(patientReq);
        Assert.Equal(HttpStatusCode.Forbidden, patientRes.StatusCode);

        // 3. Admin role -> 200 OK
        var (adminAccount, adminToken) = await CreateTestUserAsync(db, tokenService, UserRole.ADMIN);
        var adminReq = new HttpRequestMessage(HttpMethod.Post, "/api/system/outbox/drain-sync");
        adminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminRes = await client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
    }

    private static async Task<(UserAccount Account, string Token)> CreateTestUserAsync(
        MedicalPortalDbContext db,
        ITokenService tokenService,
        UserRole role)
    {
        var person = new Person
        {
            FirstName = "Test",
            LastName = role.ToString(),
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "O",
            Email = $"offline_test_{role}_{Guid.NewGuid():N}@test.internal"
        };
        db.Persons.Add(person);

        var account = new UserAccount
        {
            Person = person,
            Role = role,
            Email = person.Email,
            PasswordHash = "HASH",
            AuthorizationVersion = 1
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokenService, account, person.Id);
        return (account, token);
    }
}
