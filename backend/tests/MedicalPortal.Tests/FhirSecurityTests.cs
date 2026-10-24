using System.Net;
using System.Net.Http.Headers;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class FhirSecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FhirSecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, Guid DanaPatientId, Guid MateoPatientId, Guid StrangerPatientId, string DanaToken, string DrPatelToken, string StrangerToken) SetupTestData()
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        // Seeded accounts
        var danaAccount = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");
        var danaPatient = db.Patients.First(p => p.PersonId == danaAccount.PersonId);

        var mateoPatient = db.Patients.Include(p => p.Person).First(p => p.Person.FirstName == "Mateo");

        var patelAccount = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "apatel@clinic.example");

        // Create a stranger patient + account that Dana and Dr. Patel do NOT have access to
        var strangerEmail = "stranger.test@example.com";
        var strangerAccount = db.UserAccounts.Include(u => u.Person).FirstOrDefault(u => u.Email == strangerEmail);
        Patient strangerPatient;

        if (strangerAccount == null)
        {
            var strangerPerson = new Person
            {
                FirstName = "Stranger",
                LastName = "Patient",
                DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Email = strangerEmail
            };
            db.Persons.Add(strangerPerson);

            strangerAccount = new UserAccount
            {
                Person = strangerPerson,
                Email = strangerEmail,
                PasswordHash = "hash",
                Role = UserRole.PATIENT
            };
            db.UserAccounts.Add(strangerAccount);

            strangerPatient = new Patient
            {
                Person = strangerPerson,
                Mrn = "MRN-STRANGER-999"
            };
            db.Patients.Add(strangerPatient);
            db.SaveChanges();
        }
        else
        {
            strangerPatient = db.Patients.First(p => p.PersonId == strangerAccount.PersonId);
        }

        var danaToken = db.CreateAuthenticatedSession(tokenService, danaAccount, danaAccount.PersonId);
        var drPatelToken = db.CreateAuthenticatedSession(tokenService, patelAccount, patelAccount.PersonId);
        var strangerToken = db.CreateAuthenticatedSession(tokenService, strangerAccount, strangerAccount.PersonId);

        return (client, danaPatient.Id, mateoPatient.Id, strangerPatient.Id, danaToken, drPatelToken, strangerToken);
    }

    [Fact]
    public async Task Metadata_Endpoint_IsPubliclyAccessibleWithoutAuth()
    {
        var (client, _, _, _, _, _, _) = SetupTestData();

        var response = await client.GetAsync("/R4/metadata");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("CapabilityStatement", content);
    }

    [Fact]
    public async Task Patient_Endpoints_RequireAuth_Return401WhenUnauthenticated()
    {
        var (client, danaPatientId, _, _, _, _, _) = SetupTestData();

        // No Authorization header or cookie
        client.DefaultRequestHeaders.Authorization = null;

        var patientResp = await client.GetAsync($"/R4/Patient/{danaPatientId}");
        Assert.Equal(HttpStatusCode.Unauthorized, patientResp.StatusCode);

        var everythingResp = await client.GetAsync($"/R4/Patient/{danaPatientId}/$everything");
        Assert.Equal(HttpStatusCode.Unauthorized, everythingResp.StatusCode);

        var obsResp = await client.GetAsync($"/R4/Observation?patient={danaPatientId}");
        Assert.Equal(HttpStatusCode.Unauthorized, obsResp.StatusCode);
    }

    [Fact]
    public async Task Patient_Endpoints_Return403WhenAccessingForbiddenPatient()
    {
        var (client, _, _, strangerPatientId, danaToken, _, _) = SetupTestData();

        // Dana tries to access Stranger's record
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", danaToken);

        var patientResp = await client.GetAsync($"/R4/Patient/{strangerPatientId}");
        Assert.Equal(HttpStatusCode.Forbidden, patientResp.StatusCode);

        var everythingResp = await client.GetAsync($"/R4/Patient/{strangerPatientId}/$everything");
        Assert.Equal(HttpStatusCode.Forbidden, everythingResp.StatusCode);

        var obsResp = await client.GetAsync($"/R4/Observation?patient={strangerPatientId}");
        Assert.Equal(HttpStatusCode.Forbidden, obsResp.StatusCode);
    }

    [Fact]
    public async Task Patient_Endpoints_AllowAccessForOwnChart_CareTeamClinician_AndProxy()
    {
        var (client, danaPatientId, mateoPatientId, _, danaToken, drPatelToken, _) = SetupTestData();

        // 1. Dana accesses her own chart
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", danaToken);
        var selfResp = await client.GetAsync($"/R4/Patient/{danaPatientId}");
        Assert.Equal(HttpStatusCode.OK, selfResp.StatusCode);

        // 2. Dana accesses Mateo (proxy grant with CanViewRecords = true)
        var proxyResp = await client.GetAsync($"/R4/Patient/{mateoPatientId}");
        Assert.Equal(HttpStatusCode.OK, proxyResp.StatusCode);

        var proxyObsResp = await client.GetAsync($"/R4/Observation?patient={mateoPatientId}");
        Assert.Equal(HttpStatusCode.OK, proxyObsResp.StatusCode);

        // 3. Dr. Patel (clinician on care team) accesses Dana's chart
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", drPatelToken);
        var clinicianResp = await client.GetAsync($"/R4/Patient/{danaPatientId}");
        Assert.Equal(HttpStatusCode.OK, clinicianResp.StatusCode);
    }
}
