using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class PhiAuditAndComplianceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PhiAuditAndComplianceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, UserAccount Admin, UserAccount Clinician, UserAccount Patient, Patient TargetPatient, Patient MateoPatient) SetupTestAccounts()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var clinician = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "apatel@clinic.example");
        var patient = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");
        var targetPatient = db.Patients.Include(p => p.Person).First(p => p.PersonId == patient.PersonId);
        var mateoPatient = db.Patients.Include(p => p.Person).First(p => p.Person.FirstName == "Mateo");

        // Ensure Admin account exists
        var adminEmail = "compliance.admin@clinic.example";
        var admin = db.UserAccounts.Include(u => u.Person).FirstOrDefault(u => u.Email == adminEmail);
        if (admin == null)
        {
            var adminPerson = new Person
            {
                FirstName = "Compliance",
                LastName = "Officer",
                DateOfBirth = new DateTime(1985, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                Email = adminEmail
            };
            db.Persons.Add(adminPerson);

            admin = new UserAccount
            {
                Person = adminPerson,
                Email = adminEmail,
                PasswordHash = "admin-hash",
                Role = UserRole.ADMIN,
                IsActive = true
            };
            db.UserAccounts.Add(admin);
            db.SaveChanges();
        }

        return (client, admin, clinician, patient, targetPatient, mateoPatient);
    }

    private string CreateToken(UserAccount account)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return tokenService.GenerateAccessToken(account, account.PersonId);
    }

    [Fact]
    public async Task AuditLog_IsAppendOnly_ThrowsOnUpdate()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var log = new AuditLog
        {
            Action = "TEST_APPEND_ONLY",
            ResourceType = "Test",
            CreatedAt = DateTime.UtcNow
        };
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        // Mutating any field must trigger append-only enforcement
        log.Action = "MUTATED_ACTION";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuditLog_IsAppendOnly_ThrowsOnDelete()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var log = new AuditLog
        {
            Action = "TEST_DELETE_PROHIBITED",
            ResourceType = "Test",
            CreatedAt = DateTime.UtcNow
        };
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        // Deleting must trigger append-only enforcement
        db.AuditLogs.Remove(log);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditLog_HashChain_IsConsistentAndDetectsTampering()
    {
        var log1 = new AuditLog
        {
            Id = Guid.NewGuid(),
            SequenceNumber = 1,
            Action = "LOG_1",
            ResourceType = "Test",
            CreatedAt = DateTime.UtcNow.AddSeconds(-2)
        };
        log1.PreviousHash = AuditLogHasher.GenesisHash;
        log1.Hash = AuditLogHasher.ComputeHash(log1, log1.PreviousHash);

        var log2 = new AuditLog
        {
            Id = Guid.NewGuid(),
            SequenceNumber = 2,
            Action = "LOG_2",
            ResourceType = "Test",
            CreatedAt = DateTime.UtcNow.AddSeconds(-1)
        };
        log2.PreviousHash = log1.Hash;
        log2.Hash = AuditLogHasher.ComputeHash(log2, log2.PreviousHash);

        var log3 = new AuditLog
        {
            Id = Guid.NewGuid(),
            SequenceNumber = 3,
            Action = "LOG_3",
            ResourceType = "Test",
            CreatedAt = DateTime.UtcNow
        };
        log3.PreviousHash = log2.Hash;
        log3.Hash = AuditLogHasher.ComputeHash(log3, log3.PreviousHash);

        var chain = new List<AuditLog> { log1, log2, log3 };

        // Verify valid chain
        var valid = AuditLogHasher.VerifyIntegrity(chain, out var corruptedId, out var error);
        Assert.True(valid, $"Chain should be valid: {error}");
        Assert.Null(corruptedId);

        // Tamper with log2 Action
        log2.Action = "TAMPERED_CONTENT";
        var tampered = AuditLogHasher.VerifyIntegrity(chain, out corruptedId, out error);
        Assert.False(tampered, "Tampered chain must be detected");
        Assert.Equal(log2.Id, corruptedId);
        Assert.Contains("Hash mismatch", error);
    }

    [Fact]
    public async Task PhiRead_ClinicianReadingLabs_AuditsActorPatientIpAndResourceType()
    {
        var (client, _, clinician, _, targetPatient, _) = SetupTestAccounts();
        var token = CreateToken(clinician);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", targetPatient.Id.ToString());

        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Flush queued batch writes
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.FlushAsync();

        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var entry = await db.AuditLogs
            .Where(a => a.Action == "PHI_READ" && a.ResourceType == "Labs" && a.PatientId == targetPatient.Id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(entry);
        Assert.Equal(clinician.Id, entry.ActorUserAccountId);
        Assert.Equal(targetPatient.Id, entry.PatientId);
        Assert.False(entry.IsProxy);
        Assert.Null(entry.ActingForPersonId);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
        Assert.False(string.IsNullOrWhiteSpace(entry.PreviousHash));
    }

    [Fact]
    public async Task PhiRead_ClinicianReadingMultipleResources_AuditsEach()
    {
        var (client, _, clinician, _, targetPatient, _) = SetupTestAccounts();
        var token = CreateToken(clinician);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", targetPatient.Id.ToString());

        // Read Vitals
        var vitalsRes = await client.GetAsync("/api/clinical/vitals");
        Assert.Equal(HttpStatusCode.OK, vitalsRes.StatusCode);

        // Read Medications
        var medsRes = await client.GetAsync("/api/clinical/medications");
        Assert.Equal(HttpStatusCode.OK, medsRes.StatusCode);

        // Read Allergies
        var allergiesRes = await client.GetAsync("/api/clinical/allergies");
        Assert.Equal(HttpStatusCode.OK, allergiesRes.StatusCode);

        // Read Conditions
        var conditionsRes = await client.GetAsync("/api/clinical/conditions");
        Assert.Equal(HttpStatusCode.OK, conditionsRes.StatusCode);

        // Read Appointments
        var apptRes = await client.GetAsync("/api/appointments");
        Assert.Equal(HttpStatusCode.OK, apptRes.StatusCode);

        // Read Export Summary
        var exportRes = await client.GetAsync("/api/clinical/summary/export");
        Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);

        // Flush batch queue
        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.FlushAsync();

        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var patientLogs = await db.AuditLogs
            .Where(a => a.Action == "PHI_READ" && a.PatientId == targetPatient.Id)
            .ToListAsync();

        Assert.Contains(patientLogs, l => l.ResourceType == "Vitals");
        Assert.Contains(patientLogs, l => l.ResourceType == "Medications");
        Assert.Contains(patientLogs, l => l.ResourceType == "Allergies");
        Assert.Contains(patientLogs, l => l.ResourceType == "Conditions");
        Assert.Contains(patientLogs, l => l.ResourceType == "Appointments");
        Assert.Contains(patientLogs, l => l.ResourceType == "Exports");
    }

    [Fact]
    public async Task PhiRead_ProxyReadingPatientRecord_AuditsProxyFlagAndActingForPerson()
    {
        var (client, _, _, patient, _, mateoPatient) = SetupTestAccounts();
        var token = CreateToken(patient);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", mateoPatient.Id.ToString());

        // Dana Rivera accessing Mateo Rivera's labs as proxy
        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.FlushAsync();

        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var entry = await db.AuditLogs
            .Where(a => a.Action == "PHI_READ" && a.PatientId == mateoPatient.Id && a.ActorUserAccountId == patient.Id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(entry);
        Assert.True(entry.IsProxy, "Audit entry must record IsProxy == true");
        Assert.Equal(mateoPatient.PersonId, entry.ActingForPersonId);
        Assert.Equal(patient.Id, entry.ActorUserAccountId);
    }

    [Fact]
    public async Task ComplianceApi_RestrictedToAdmin_RejectsClinicianAndPatient()
    {
        var (client, admin, clinician, patient, _, _) = SetupTestAccounts();

        // 1. Patient caller -> 403 Forbidden
        var patientToken = CreateToken(patient);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        var patientResponse = await client.GetAsync("/api/compliance/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, patientResponse.StatusCode);

        // 2. Clinician caller -> 403 Forbidden
        var clinicianToken = CreateToken(clinician);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
        var clinicianResponse = await client.GetAsync("/api/compliance/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, clinicianResponse.StatusCode);

        // 3. Admin caller -> 200 OK
        var adminToken = CreateToken(admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminResponse = await client.GetAsync("/api/compliance/audit-logs");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task ComplianceApi_PagedAndFilterable_ReturnsAccurateResults()
    {
        var (client, admin, _, _, targetPatient, _) = SetupTestAccounts();
        var adminToken = CreateToken(admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Flush any pending logs first
        using (var scope = _factory.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.FlushAsync();
        }

        // Query filtered by resourceType=Labs and patientId
        var response = await client.GetAsync($"/api/compliance/audit-logs?resourceType=Labs&patientId={targetPatient.Id}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("items", out var items));
        Assert.True(body.TryGetProperty("totalCount", out var totalCount));
        Assert.True(body.TryGetProperty("page", out var page));
        Assert.Equal(1, page.GetInt32());

        foreach (var item in items.EnumerateArray())
        {
            Assert.Equal("Labs", item.GetProperty("resourceType").GetString(), ignoreCase: true);
            Assert.Equal(targetPatient.Id.ToString(), item.GetProperty("patientId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("hash").GetString()));
        }
    }

    [Fact]
    public async Task ComplianceApi_VerifyChain_ReportsValidStatus()
    {
        var (client, admin, _, _, _, _) = SetupTestAccounts();
        var adminToken = CreateToken(admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Flush queue
        using (var scope = _factory.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await audit.FlushAsync();
        }

        var response = await client.GetAsync("/api/compliance/audit-logs/verify");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reason = body.TryGetProperty("error", out var err) ? err.GetString() : "none";
        Assert.True(body.GetProperty("valid").GetBoolean(), $"Untouched audit chain should be cryptographically valid. Reason: {reason}");
        Assert.True(body.GetProperty("totalRecords").GetInt32() >= 0);
    }
}
