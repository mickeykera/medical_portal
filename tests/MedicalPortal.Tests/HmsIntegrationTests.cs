using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Hms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedicalPortal.Tests;

public class HmsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HmsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static string ComputeSignatureHeader(string secret, string rawBody, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var message = $"{ts}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"t={ts},v1={hashHex}";
    }

    private static (MedicalPortalDbContext Db, FakeHmsClient FakeHms, HmsConflictService ConflictService, HmsDeadLetterService DeadLetterService, HmsWebhookHandler WebhookHandler, HmsSyncService SyncService) CreateTestHarness(
        string? activeSecrets = "secret-v1,secret-v2")
    {
        var dbName = $"hms_test_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var db = new MedicalPortalDbContext(options);

        var configDict = new Dictionary<string, string?>
        {
            ["Hms:BaseUrl"] = "http://localhost:3210",
            ["Hms:WebhookSecrets"] = activeSecrets,
            ["Hms:DeadLetterRetentionDays"] = "90",
            ["Hms:RefreshTtlMinutes"] = "15"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

        var fakeHms = new FakeHmsClient();
        var conflictService = new HmsConflictService(db, NullLogger<HmsConflictService>.Instance);
        var deadLetterService = new HmsDeadLetterService(db, config, NullLogger<HmsDeadLetterService>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton<MedicalPortalDbContext>(db);
        services.AddSingleton<IHmsClient>(fakeHms);
        services.AddSingleton<IHmsConflictService>(conflictService);
        services.AddSingleton<IHmsDeadLetterService>(deadLetterService);
        var sp = services.BuildServiceProvider();

        var syncService = new HmsSyncService(db, fakeHms, conflictService, deadLetterService, config, null, NullLogger<HmsSyncService>.Instance);
        var webhookHandler = new HmsWebhookHandler(db, config, sp, NullLogger<HmsWebhookHandler>.Instance);

        return (db, fakeHms, conflictService, deadLetterService, webhookHandler, syncService);
    }

    [Fact]
    public async Task Webhook_ValidSignature_ProcessesAndDeduplicates()
    {
        var (db, _, _, _, webhookHandler, _) = CreateTestHarness();

        var eventId = $"evt_{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            eventId = eventId,
            @event = "patient.updated",
            hmsMrn = "HMS-TEST-001",
            timestamp = DateTime.UtcNow.ToString("o")
        });

        var signature = ComputeSignatureHeader("secret-v1", payload);

        // First delivery -> Valid
        var result1 = await webhookHandler.ProcessWebhookAsync(signature, payload);
        Assert.True(result1.IsValid);
        Assert.False(result1.IsDuplicate);
        Assert.Equal(eventId, result1.Payload?.EventId);

        // Verify recorded in DB
        var processed = await db.HmsProcessedWebhookEvents.FirstOrDefaultAsync(e => e.EventId == eventId);
        Assert.NotNull(processed);
        Assert.Equal("HMS-TEST-001", processed.HmsMrn);

        // Second delivery of same eventId -> Deduplicated (no-op)
        var result2 = await webhookHandler.ProcessWebhookAsync(signature, payload);
        Assert.True(result2.IsValid);
        Assert.True(result2.IsDuplicate);
    }

    [Fact]
    public async Task Webhook_KeyRotation_ValidatesAgainstAnyActiveSecret()
    {
        var (db, _, _, _, webhookHandler, _) = CreateTestHarness(activeSecrets: "primary-key,secondary-key");

        var payload1 = JsonSerializer.Serialize(new { eventId = "evt_k1", @event = "lab.released", hmsMrn = "MRN-1" });
        var sig1 = ComputeSignatureHeader("primary-key", payload1);
        var res1 = await webhookHandler.ProcessWebhookAsync(sig1, payload1);
        Assert.True(res1.IsValid);

        var payload2 = JsonSerializer.Serialize(new { eventId = "evt_k2", @event = "lab.released", hmsMrn = "MRN-2" });
        var sig2 = ComputeSignatureHeader("secondary-key", payload2);
        var res2 = await webhookHandler.ProcessWebhookAsync(sig2, payload2);
        Assert.True(res2.IsValid);

        // Invalid key
        var payload3 = JsonSerializer.Serialize(new { eventId = "evt_k3", @event = "lab.released", hmsMrn = "MRN-3" });
        var sig3 = ComputeSignatureHeader("wrong-key", payload3);
        var res3 = await webhookHandler.ProcessWebhookAsync(sig3, payload3);
        Assert.False(res3.IsValid);
        Assert.Equal("Invalid signature", res3.ErrorMessage);
    }

    [Fact]
    public async Task Webhook_ExpiredOrTamperedTimestamp_Rejects()
    {
        var (_, _, _, _, webhookHandler, _) = CreateTestHarness();

        var payload = JsonSerializer.Serialize(new { eventId = "evt_old", @event = "patient.updated" });

        // Timestamp 10 minutes in the past (> 300s window)
        var oldTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;
        var signature = ComputeSignatureHeader("secret-v1", payload, timestamp: oldTimestamp);

        var result = await webhookHandler.ProcessWebhookAsync(signature, payload);
        Assert.False(result.IsValid);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Tampered payload
        var validSig = ComputeSignatureHeader("secret-v1", payload);
        var tamperedPayload = payload + " ";
        var tamperedResult = await webhookHandler.ProcessWebhookAsync(validSig, tamperedPayload);
        Assert.False(tamperedResult.IsValid);
    }

    [Fact]
    public async Task DemographicConflict_ContactFields_UpdateImmediatelyFromHms()
    {
        var (db, _, conflictService, _, _, _) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Alice",
            LastName = "Cooper",
            Phone = "+1555000111",
            EmergencyContactName = "Bob Cooper",
            EmergencyContactPhone = "+1555000222"
        };
        var patient = new Patient { Person = person, Mrn = "MRN-ALICE", HmsMrn = "HMS-ALICE" };
        db.Persons.Add(person);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var hmsPatient = new HmsPatientDto(
            Id: 10,
            GlobalId: "HMS-ALICE",
            FirstName: "Alice",
            LastName: "Cooper",
            DateOfBirth: "1980-05-15",
            Gender: "Female",
            BloodType: "A+",
            Email: "alice@example.com",
            Phone: "+1555999888", // New phone in HMS
            Address: "123 Main St",
            EmergencyContactName: "Charlie Cooper", // New emergency contact
            EmergencyContactPhone: "+1555999777",
            CreatedAt: null,
            UpdatedAt: null);

        var conflicts = await conflictService.DetectAndReconcileDemographicsAsync(patient, hmsPatient);

        // Contact fields must update immediately with zero conflicts
        Assert.Equal("+1555999888", person.Phone);
        Assert.Equal("Charlie Cooper", person.EmergencyContactName);
        Assert.Equal("+1555999777", person.EmergencyContactPhone);
    }

    [Fact]
    public async Task DemographicConflict_OnlyHmsChanged_AutoAppliesWithoutConflict()
    {
        var (db, _, conflictService, _, _, _) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "David",
            LastName = "Bowie",
            DateOfBirth = new DateTime(1975, 1, 8, 0, 0, 0, DateTimeKind.Utc),
            AddressLine1 = "100 Old St"
        };
        var patient = new Patient
        {
            Person = person,
            Mrn = "MRN-DAVID",
            HmsMrn = "HMS-DAVID",
            // Baseline from last sync
            LastSyncedDemographicsJson = JsonSerializer.Serialize(new
            {
                FirstName = "David",
                LastName = "Bowie",
                DateOfBirth = "1975-01-08",
                Address = "100 Old St"
            })
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        // HMS updated address to "200 New Blvd", while local still equals baseline ("100 Old St")
        var hmsPatient = new HmsPatientDto(
            Id: 20,
            GlobalId: "HMS-DAVID",
            FirstName: "David",
            LastName: "Bowie",
            DateOfBirth: "1975-01-08",
            Gender: "Male",
            BloodType: "O+",
            Email: null,
            Phone: null,
            Address: "200 New Blvd",
            EmergencyContactName: null,
            EmergencyContactPhone: null,
            CreatedAt: null,
            UpdatedAt: null);

        var conflicts = await conflictService.DetectAndReconcileDemographicsAsync(patient, hmsPatient);

        // No conflict should be created because only HMS changed
        Assert.Empty(conflicts);
        Assert.Equal("200 New Blvd", person.AddressLine1);
    }

    [Fact]
    public async Task DemographicConflict_BothSidesChanged_CreatesPendingConflict_PreservesLocal()
    {
        var (db, _, conflictService, _, _, _) = CreateTestHarness();

        var person = new Person
        {
            FirstName = "Eva",
            LastName = "LocalEdit", // Changed locally by patient
            DateOfBirth = new DateTime(1990, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            AddressLine1 = "500 Local Ave"
        };
        var patient = new Patient
        {
            Person = person,
            Mrn = "MRN-EVA",
            HmsMrn = "HMS-EVA",
            // Last synced baseline had original name "Eva Green" and address "100 Base St"
            LastSyncedDemographicsJson = JsonSerializer.Serialize(new
            {
                FirstName = "Eva",
                LastName = "Green",
                DateOfBirth = "1990-06-20",
                Address = "100 Base St"
            })
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        // HMS has different name "Eva HmsEdit" and address "200 Hms St"
        var hmsPatient = new HmsPatientDto(
            Id: 30,
            GlobalId: "HMS-EVA",
            FirstName: "Eva",
            LastName: "HmsEdit",
            DateOfBirth: "1990-06-20",
            Gender: "Female",
            BloodType: "B+",
            Email: null,
            Phone: null,
            Address: "200 Hms St",
            EmergencyContactName: null,
            EmergencyContactPhone: null,
            CreatedAt: null,
            UpdatedAt: null);

        var conflicts = await conflictService.DetectAndReconcileDemographicsAsync(patient, hmsPatient);

        // Both sides changed -> Conflicts must be recorded
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c => c.FieldName == "LastName" && c.LocalValue == "LocalEdit" && c.HmsValue == "HmsEdit");
        Assert.Contains(conflicts, c => c.FieldName == "AddressLine1" && c.LocalValue == "500 Local Ave" && c.HmsValue == "200 Hms St");

        // Local values MUST be preserved
        Assert.Equal("LocalEdit", person.LastName);
        Assert.Equal("500 Local Ave", person.AddressLine1);
    }

    [Fact]
    public async Task DemographicConflict_StaffResolution_AppliesDecisionAndAudits()
    {
        var (db, _, conflictService, _, _, _) = CreateTestHarness();

        var person = new Person { FirstName = "Frank", LastName = "LocalName" };
        var patient = new Patient { Person = person, Mrn = "MRN-FRANK", HmsMrn = "HMS-FRANK" };
        db.Persons.Add(person);
        db.Patients.Add(patient);

        var conflict = new HmsDemographicConflict
        {
            Patient = patient,
            HmsMrn = "HMS-FRANK",
            FieldName = "LastName",
            LocalValue = "LocalName",
            HmsValue = "HmsApprovedName",
            Status = HmsConflictStatus.Pending
        };
        db.HmsDemographicConflicts.Add(conflict);
        await db.SaveChangesAsync();

        var staffUserId = Guid.NewGuid();

        // Staff accepts HMS value
        var resolved = await conflictService.ResolveConflictAsync(conflict.Id, acceptHms: true, staffUserId, notes: "Verified against passport");
        Assert.NotNull(resolved);
        Assert.Equal(HmsConflictStatus.ResolvedAcceptHms, resolved.Status);
        Assert.Equal("HmsApprovedName", person.LastName);

        // Verify audit log
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "HMS_DEMOGRAPHIC_CONFLICT_RESOLVED" && a.ResourceId == conflict.Id.ToString());
        Assert.NotNull(audit);
        Assert.Contains("ACCEPT_HMS", audit.MetadataJson);
        Assert.Contains("Verified against passport", audit.MetadataJson);
    }

    [Fact]
    public async Task DeadLetterService_RecordsFailure_ReplaysAndPurgesRetention()
    {
        var (db, _, _, deadLetterService, _, _) = CreateTestHarness();

        var eventId = "evt_deadletter_1";
        var payload = "{\"patientId\":\"p1\",\"hmsMrn\":\"HMS-1\"}";

        // Record dead letter
        var record = await deadLetterService.RecordFailureAsync(
            eventId: eventId,
            source: "APPOINTMENT_PUSH",
            error: "HTTP 500 Internal Server Error",
            payloadJson: payload,
            hmsMrn: "HMS-1",
            entityType: "Appointment",
            entityId: "appt_123",
            attemptCount: 3);

        Assert.Equal(HmsDeadLetterStatus.Pending, record.Status);
        Assert.Equal(3, record.AttemptCount);

        // Query dead letters (audited view)
        var staffId = Guid.NewGuid();
        var letters = await deadLetterService.GetDeadLettersAsync(HmsDeadLetterStatus.Pending, actorUserAccountId: staffId);
        Assert.Single(letters);

        var viewAudit = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "HMS_DEAD_LETTER_VIEWED");
        Assert.NotNull(viewAudit);

        // Resolve dead letter
        var resolved = await deadLetterService.ResolveDeadLetterAsync(record.Id, HmsDeadLetterStatus.Resolved, staffId, "Manually cleared");
        Assert.True(resolved);

        // Retention Purge: set LastAttemptAt to 100 days ago
        record.LastAttemptAt = DateTime.UtcNow.AddDays(-100);
        await db.SaveChangesAsync();

        var purgedCount = await deadLetterService.PurgeExpiredRecordsAsync(retentionDays: 90);
        Assert.Equal(1, purgedCount);

        var remaining = await db.HmsDeadLetterRecords.FindAsync(record.Id);
        Assert.Null(remaining);
    }

    [Fact]
    public async Task SyncService_InboundPull_ReconcilesEmrAndTombstonesMissing()
    {
        var (db, fakeHms, _, _, _, syncService) = CreateTestHarness();

        var person = new Person { FirstName = "Grace", LastName = "Hopper" };
        var patient = new Patient { Person = person, Mrn = "MRN-GRACE", HmsMrn = "HMS-GRACE" };

        // Pre-existing condition in portal (HMS sourced)
        var existingHmsCond = new Condition
        {
            Patient = patient,
            Code = "HMS-101",
            Name = "Old Resolved Problem",
            ClinicalStatus = "ACTIVE"
        };
        // Pre-existing PORTAL-native condition (must NOT be touched!)
        var portalNativeCond = new Condition
        {
            Patient = patient,
            Code = "PORTAL-NATIVE-1",
            Name = "Local Portal Problem",
            ClinicalStatus = "ACTIVE"
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Conditions.Add(existingHmsCond);
        db.Conditions.Add(portalNativeCond);
        await db.SaveChangesAsync();

        // Seed HMS with patient EMR containing only HMS-102 (HMS-101 is gone from HMS!)
        var hmsPatient = new HmsPatientDto(40, "HMS-GRACE", "Grace", "Hopper", "1906-12-09", "Female", "A+", null, null, "Arlington", null, null, null, null);
        fakeHms.SeedPatient(hmsPatient);

        var emr = new HmsEmrBundleDto(
            Patient: hmsPatient,
            MedicalHistory: new List<HmsMedicalHistoryDto>
            {
                new(102, 40, "New Active Condition", "2026-01-01", "Observation", "Active", null, null)
            },
            Consultations: null,
            Prescriptions: null);
        fakeHms.SeedEmr(40, emr);

        var synced = await syncService.SyncPatientAsync(patient.Id);
        Assert.True(synced);

        // Verify patient HmsLastSync was updated
        Assert.NotNull(patient.HmsLastSync);

        // 1. New condition HMS-102 was added
        var newCond = await db.Conditions.FirstOrDefaultAsync(c => c.Code == "HMS-102");
        Assert.NotNull(newCond);
        Assert.Equal("ACTIVE", newCond.ClinicalStatus);

        // 2. Missing HMS condition HMS-101 was tombstoned (status -> RESOLVED), not deleted!
        var tombstoned = await db.Conditions.FirstOrDefaultAsync(c => c.Code == "HMS-101");
        Assert.NotNull(tombstoned);
        Assert.Equal("RESOLVED", tombstoned.ClinicalStatus);

        // 3. Portal-native condition was untouched!
        var portalCond = await db.Conditions.FirstOrDefaultAsync(c => c.Code == "PORTAL-NATIVE-1");
        Assert.NotNull(portalCond);
        Assert.Equal("ACTIVE", portalCond.ClinicalStatus);
    }

    [Fact]
    public async Task SyncService_OutboundAppointmentsAndRefills_PushesAndMarksSynced()
    {
        var (db, fakeHms, _, _, _, syncService) = CreateTestHarness();

        var person = new Person { FirstName = "Henry", LastName = "Ford" };
        var patient = new Patient { Person = person, Mrn = "MRN-HENRY", HmsMrn = "HMS-HENRY" };
        var provider = new Provider { FirstName = "Thomas", LastName = "Edison" };

        var unsyncedAppt = new Appointment
        {
            Patient = patient,
            Provider = provider,
            StartAt = DateTime.UtcNow.AddDays(2),
            EndAt = DateTime.UtcNow.AddDays(2).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED,
            ReasonForVisit = "Annual Wellness Exam",
            HmsSynced = false
        };

        var med = new Medication { Name = "Metformin", Form = "Tablet", Strength = "500mg" };
        var rx = new Prescription { Patient = patient, Medication = med, Provider = provider, Instructions = "Daily", IsActive = true };
        var unsyncedRefill = new RefillRequest
        {
            Prescription = rx,
            Medication = med,
            Status = RefillStatus.PENDING,
            HmsSynced = false
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Providers.Add(provider);
        db.Appointments.Add(unsyncedAppt);
        db.Medications.Add(med);
        db.Prescriptions.Add(rx);
        db.RefillRequests.Add(unsyncedRefill);
        await db.SaveChangesAsync();

        // Seed HMS with patient
        fakeHms.SeedPatient(new HmsPatientDto(50, "HMS-HENRY", "Henry", "Ford", "1863-07-30", "Male", "O+", null, null, "Dearborn", null, null, null, null));

        // Push outbound
        var apptsPushed = await syncService.SyncOutboundAppointmentsAsync();
        Assert.Equal(1, apptsPushed);
        Assert.True(unsyncedAppt.HmsSynced);
        Assert.Single(fakeHms.CreatedAppointments);

        var refillsPushed = await syncService.SyncOutboundRefillsAsync();
        Assert.Equal(1, refillsPushed);
        Assert.True(unsyncedRefill.HmsSynced);
    }

    [Fact]
    public async Task HmsSyncWorker_ExecutesFullSyncCycle()
    {
        var (db, fakeHms, _, _, _, syncService) = CreateTestHarness();

        var person = new Person { FirstName = "Ada", LastName = "Lovelace" };
        var patient = new Patient { Person = person, Mrn = "MRN-ADA", HmsMrn = "HMS-ADA", HmsLastSync = null };
        var provider = new Provider { FirstName = "Charles", LastName = "Babbage" };

        var unsyncedAppt = new Appointment
        {
            Patient = patient,
            Provider = provider,
            StartAt = DateTime.UtcNow.AddDays(1),
            EndAt = DateTime.UtcNow.AddDays(1).AddMinutes(30),
            Status = AppointmentStatus.CONFIRMED,
            ReasonForVisit = "Algorithm review",
            HmsSynced = false
        };

        db.Persons.Add(person);
        db.Patients.Add(patient);
        db.Providers.Add(provider);
        db.Appointments.Add(unsyncedAppt);
        await db.SaveChangesAsync();

        fakeHms.SeedPatient(new HmsPatientDto(60, "HMS-ADA", "Ada", "Lovelace", "1815-12-10", "Female", "AB+", null, null, "London", null, null, null, null));
        fakeHms.SeedEmr(60, new HmsEmrBundleDto(
            Patient: fakeHms.PatientsById[60],
            MedicalHistory: new List<HmsMedicalHistoryDto> { new(201, 60, "Mathematical Excellence", "2026-01-01", "Coding", "Active", null, null) },
            Consultations: null,
            Prescriptions: null));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<IHmsClient>(fakeHms);
        services.AddSingleton<IHmsSyncService>(syncService);
        var deadLetterService = new HmsDeadLetterService(db, new ConfigurationBuilder().Build(), NullLogger<HmsDeadLetterService>.Instance);
        services.AddSingleton<IHmsDeadLetterService>(deadLetterService);
        var sp = services.BuildServiceProvider();

        var worker = new HmsSyncWorker(sp, NullLogger<HmsSyncWorker>.Instance);
        var totalActions = await worker.ProcessSyncCycleAsync();

        Assert.True(totalActions >= 2); // 1 outbound appt pushed + 1 inbound stale patient synced
        Assert.True(unsyncedAppt.HmsSynced);
        Assert.NotNull(patient.HmsLastSync);
        Assert.Single(fakeHms.CreatedAppointments);
    }
}
