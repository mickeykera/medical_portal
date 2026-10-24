using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public class BreakGlassTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BreakGlassTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, UserAccount Clinician, UserAccount Patient) SetupBaseAccounts()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var clinician = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "apatel@clinic.example");
        var patient = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");

        return (client, clinician, patient);
    }

    private async Task<Patient> CreateEmergencyPatientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var person = new Person
        {
            FirstName = "Emergency",
            LastName = $"Patient-{Guid.NewGuid():N}"[..18],
            DateOfBirth = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Email = $"patient.{Guid.NewGuid():N}@example.com"
        };
        db.Persons.Add(person);

        var patient = new Patient
        {
            Person = person,
            Mrn = $"MRN-{Guid.NewGuid():N}"[..12]
        };
        db.Patients.Add(patient);

        db.Observations.Add(new Observation
        {
            Patient = patient,
            Category = ObservationCategory.LAB,
            Code = "8480-6",
            DisplayName = "Troponin I",
            ValueNumber = 0.95m,
            Unit = "ng/mL",
            IsAbnormal = true,
            ObservedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return patient;
    }

    private string CreateToken(UserAccount account)
    {
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return tokenService.GenerateAccessToken(account, account.PersonId);
    }

    [Fact]
    public async Task BreakGlass_PatientCaller_ReturnsForbidden()
    {
        var (client, _, patient) = SetupBaseAccounts();
        var strangerPatient = await CreateEmergencyPatientAsync();
        var token = CreateToken(patient);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = strangerPatient.Id,
            reason = "Patient trying to break glass on another patient"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlass_WithoutWrittenReason_ReturnsBadRequest()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var strangerPatient = await CreateEmergencyPatientAsync();
        var token = CreateToken(clinician);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Missing reason
        var res1 = await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = strangerPatient.Id,
            reason = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        // Too short / whitespace reason
        var res2 = await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = strangerPatient.Id,
            reason = "  ED  "
        });
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }

    [Fact]
    public async Task BreakGlass_NonExistentPatient_ReturnsNotFound()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var token = CreateToken(clinician);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = Guid.NewGuid(),
            reason = "Emergency patient in cardiac arrest"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlass_ValidReason_Grants60MinuteAccess_AndWritesHighPriorityAudit()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var strangerPatient = await CreateEmergencyPatientAsync();
        var token = CreateToken(clinician);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // First confirm clinician is normally denied access (not on care team)
        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", strangerPatient.Id.ToString());
        var initialRes = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, initialRes.StatusCode);

        // Initiate emergency break-glass override
        var writtenReason = "Unconscious adult trauma patient admitted to ED with suspected internal hemorrhage. Immediate lab access needed.";
        var overrideRes = await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = strangerPatient.Id,
            reason = writtenReason
        });

        Assert.Equal(HttpStatusCode.OK, overrideRes.StatusCode);
        var body = await overrideRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal(60, body.GetProperty("expiresInMinutes").GetInt32());

        // Verify high-priority AuditLog was recorded
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var audit = await db.AuditLogs
            .Where(a => a.Action == "BREAK_GLASS_EMERGENCY_OVERRIDE" && a.PatientId == strangerPatient.Id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(audit);
        Assert.Equal(clinician.Id, audit.ActorUserAccountId);
        Assert.Equal(strangerPatient.Id, audit.PatientId);
        Assert.Contains("HIGH", audit.MetadataJson);
        Assert.Contains("EMERGENCY", audit.MetadataJson);
        Assert.Contains(writtenReason, audit.MetadataJson);

        // Now verify clinical record access is GRANTED
        var labsRes = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.OK, labsRes.StatusCode);
        Assert.True(labsRes.Headers.Contains("X-Break-Glass-Session"));
        Assert.Equal("true", labsRes.Headers.GetValues("X-Break-Glass-Session").First());

        var labsData = await labsRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(labsData.TryGetProperty("results", out var results));
        Assert.True(results.GetArrayLength() > 0);
    }

    [Fact]
    public async Task BreakGlass_PatientIsolation_DoesNotGrantAccessToOtherPatients()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var patientA = await CreateEmergencyPatientAsync();
        var patientB = await CreateEmergencyPatientAsync();

        var token = CreateToken(clinician);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Grant break-glass ONLY for patientA
        await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = patientA.Id,
            reason = "Emergency override specifically for Patient A in acute distress."
        });

        // Requesting Patient A succeeds
        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", patientA.Id.ToString());
        var resA = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.OK, resA.StatusCode);

        // Requesting Patient B MUST be denied
        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", patientB.Id.ToString());
        var resB = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, resB.StatusCode);
    }

    [Fact]
    public async Task BreakGlass_ExpiredSession_DeniesAccess()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var strangerPatient = await CreateEmergencyPatientAsync();
        var token = CreateToken(clinician);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Insert an expired override record directly
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var expiredOverride = new BreakGlassOverride
            {
                ClinicianUserAccountId = clinician.Id,
                PatientId = strangerPatient.Id,
                Reason = "Past trauma event now expired",
                GrantedAt = DateTime.UtcNow.AddMinutes(-70),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-10) // Expired 10 minutes ago
            };
            db.BreakGlassOverrides.Add(expiredOverride);
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", strangerPatient.Id.ToString());

        var response = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlass_Revocation_ImmediatelyDeniesAccess()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var strangerPatient = await CreateEmergencyPatientAsync();
        var token = CreateToken(clinician);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Initiate break-glass
        await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = strangerPatient.Id,
            reason = "Acute patient admission to ED."
        });

        client.DefaultRequestHeaders.Remove("x-patient-id");
        client.DefaultRequestHeaders.Add("x-patient-id", strangerPatient.Id.ToString());

        // Verify access is active
        var activeRes = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.OK, activeRes.StatusCode);

        // Clinician concludes session early
        var revokeRes = await client.PostAsJsonAsync("/api/clinical/break-glass/revoke", new
        {
            patientId = strangerPatient.Id
        });
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // Next request must be Forbidden
        var postRevokeRes = await client.GetAsync("/api/clinical/labs");
        Assert.Equal(HttpStatusCode.Forbidden, postRevokeRes.StatusCode);
    }

    [Fact]
    public async Task BreakGlass_StatusEndpoint_ReflectsLiveState()
    {
        var (client, clinician, _) = SetupBaseAccounts();
        var strangerPatient = await CreateEmergencyPatientAsync();
        var token = CreateToken(clinician);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Before break-glass: active = false
        var preStatus = await client.GetFromJsonAsync<JsonElement>($"/api/clinical/break-glass/status?patientId={strangerPatient.Id}");
        Assert.False(preStatus.GetProperty("active").GetBoolean());

        // Create break-glass
        await client.PostAsJsonAsync("/api/clinical/break-glass", new
        {
            patientId = strangerPatient.Id,
            reason = "Status check emergency test"
        });

        // After break-glass: active = true with remainingMinutes <= 60
        var postStatus = await client.GetFromJsonAsync<JsonElement>($"/api/clinical/break-glass/status?patientId={strangerPatient.Id}");
        Assert.True(postStatus.GetProperty("active").GetBoolean());
        Assert.True(postStatus.GetProperty("remainingMinutes").GetInt32() <= 60);
        Assert.Equal("Status check emergency test", postStatus.GetProperty("reason").GetString());
    }
}
