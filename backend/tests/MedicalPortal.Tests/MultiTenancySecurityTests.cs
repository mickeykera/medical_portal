using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class MultiTenancySecurityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MultiTenancySecurityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MultiTenancy_TenantResolution_ResolvesDefaultAndDetectsNotFound()
    {
        var client = _factory.CreateClient();

        // 1. Without headers, resolves default tenant (Springfield Health System)
        var defaultResponse = await client.GetAsync("/api/tenants/current");
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        var defaultJson = await defaultResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TenantConstants.DefaultTenantSlug, defaultJson.GetProperty("slug").GetString());

        // 2. Non-existent slug returns 404
        var nonExistentRequest = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/current");
        nonExistentRequest.Headers.Add("X-Tenant-Slug", "non-existent-hospital");
        var nonExistentResponse = await client.SendAsync(nonExistentRequest);
        Assert.Equal(HttpStatusCode.NotFound, nonExistentResponse.StatusCode);
    }

    [Fact]
    public async Task MultiTenancy_ProvisionTenant_ProvisionsAdotHospital_WithSeededDepartmentsAndKeyFingerprint()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        // Create a platform admin user
        var adminPerson = new Person
        {
            FirstName = "Platform",
            LastName = "Admin",
            DateOfBirth = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "other",
            Email = $"platadmin.{Guid.NewGuid():N}@platform.example"
        };
        db.Persons.Add(adminPerson);
        await db.SaveChangesAsync();

        var adminAccount = new UserAccount
        {
            PersonId = adminPerson.Id,
            Email = adminPerson.Email,
            Role = UserRole.PLATFORM_ADMIN,
            PasswordHash = "dummyhash"
        };
        db.UserAccounts.Add(adminAccount);
        await db.SaveChangesAsync();

        var adminToken = db.CreateAuthenticatedSession(tokens, adminAccount, adminPerson.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var requestBody = new
        {
            name = "Adot Internal Medicine",
            slug = "Adot-Hospital",
            region = "Other: Ethiopia, Addis Ababa",
            timeZone = "UTC",
            languages = new List<string> { "en" },
            facilitiesCount = 1,
            departments = new List<string>
            {
                "Cardiology", "Gastroenterology", "Endocrinology", "Pulmonology",
                "Rheumatology", "Nephrology", "Laboratory Services",
                "Imaging & Radiology", "Infusion Suite", "and Preventive & Wellness Care"
            },
            settings = new
            {
                branding = new
                {
                    primaryColor = "#e01b24",
                    logo = "to be supplied",
                    supportEmail = ""
                },
                modules = new
                {
                    enabled = new List<string> { "appointments", "messaging", "billing", "refills", "proxy", "sharing", "fhir", "forms" },
                    disabled = new List<string>()
                },
                hosting = new
                {
                    provider = "Hospital on-premises",
                    dataResidency = "same country as hospital",
                    isolation = "Dedicated database"
                },
                integrations = new
                {
                    ehr = "Custom API",
                    payments = "Other gateway",
                    notifications = "Email and SMS",
                    staffSignIn = "Local accounts with MFA"
                },
                security = new
                {
                    mfaStaff = true,
                    mfaPatients = true,
                    idleTimeoutMinutes = 15,
                    retentionYears = 20,
                    breakGlass = true
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/platform/tenants/provision", requestBody);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.Conflict, $"Status: {response.StatusCode}, Content: {content}");

        // Verify tenant exists in database
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug.ToLower() == "adot-hospital");
        Assert.NotNull(tenant);
        Assert.Equal("Adot Internal Medicine", tenant.Name);
        Assert.StartsWith("fp_", tenant.MasterKeyFingerprint);
        Assert.Equal(64, tenant.AuditChainHash.Length);

        // Verify seeded facility and departments
        var facilities = await db.Facilities.IgnoreQueryFilters().Where(f => f.TenantId == tenant.Id).ToListAsync();
        Assert.Single(facilities);

        var departments = await db.Departments.IgnoreQueryFilters().Where(d => d.TenantId == tenant.Id).ToListAsync();
        Assert.Equal(10, departments.Count);
        var deptNames = departments.Select(d => d.Name).ToList();
        Assert.Contains("Cardiology", deptNames);
        Assert.Contains("Nephrology", deptNames);
        Assert.Contains("Preventive & Wellness Care", deptNames);
    }

    [Fact]
    public void MultiTenancy_WcagContrast_AdotRedColor_CalculatesCorrectly()
    {
        // Adot Internal Medicine primary color: #e01b24
        var analysis = WcagColorContrastCalculator.AnalyzeHexColor("#e01b24");

        // Ratio against white is ~4.81:1 -> passes WCAG AA (>= 4.5:1)
        Assert.True(analysis.RatioAgainstWhite >= 4.5, $"Contrast {analysis.RatioAgainstWhite} should be >= 4.5");
        Assert.True(analysis.PassesAaNormalWhite);
        Assert.True(analysis.PassesAaLargeWhite);

        // Ratio against black is ~4.36:1 -> passes large text (>= 3.0:1), fails normal text (< 4.5:1)
        Assert.True(analysis.RatioAgainstBlack >= 3.0);
        Assert.False(analysis.RatioAgainstBlack >= 4.5);
    }

    [Fact]
    public async Task MultiTenancy_WcagThemeEndpoint_ReturnsContrastAnalysis()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/current/theme");
        request.Headers.Add("X-Tenant-Slug", "Adot-Hospital");

        var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // If not yet provisioned in this specific test runner run, provision it
            await MultiTenancy_ProvisionTenant_ProvisionsAdotHospital_WithSeededDepartmentsAndKeyFingerprint();
            request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/current/theme");
            request.Headers.Add("X-Tenant-Slug", "Adot-Hospital");
            response = await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tokens = json.GetProperty("tokens");
        Assert.Equal("#e01b24", tokens.GetProperty("primaryColor").GetString());

        var wcag = json.GetProperty("wcagCompliance");
        Assert.True(wcag.GetProperty("passesAaNormalText").GetBoolean());
        Assert.True(wcag.GetProperty("ratioAgainstWhite").GetDouble() >= 4.5);
    }

    [Fact]
    public async Task MultiTenancy_CrossTenantRead_QueryFilterHidesRecordsAcrossTenants()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        // Insert Person and Patient for Tenant A
        var personA = new Person
        {
            TenantId = tenantAId,
            FirstName = "TenantA",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "female",
            Email = $"patientA.{Guid.NewGuid():N}@tenantA.example"
        };
        db.Persons.Add(personA);

        var patientA = new Patient
        {
            TenantId = tenantAId,
            Person = personA,
            Mrn = $"MRN-A-{Guid.NewGuid():N}".Substring(0, 16)
        };
        db.Patients.Add(patientA);
        await db.SaveChangesAsync();

        // 1. Query with Tenant B context
        var tenantBContext = new TenantContext();
        tenantBContext.SetTenant(new Tenant { Id = tenantBId, Name = "Tenant B", Slug = "tenant-b" });

        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseSqlite($"Data Source=file:memdb_{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        // Verify using Scoped DbContext with TenantContext set to Tenant B
        var tenantBScopedContext = new TenantContext();
        tenantBScopedContext.SetTenant(new Tenant { Id = tenantBId, Name = "Tenant B", Slug = "tenant-b" });
        using var dbB = new MedicalPortalDbContext(
            new DbContextOptionsBuilder<MedicalPortalDbContext>()
                .UseSqlite(db.Database.GetDbConnection())
                .Options,
            tenantBScopedContext);

        var patientsInTenantB = await dbB.Patients.Where(p => p.Id == patientA.Id).ToListAsync();
        Assert.Empty(patientsInTenantB); // Global query filter excludes Tenant A's patient!
    }

    [Fact]
    public async Task MultiTenancy_CrossTenantWrite_AttemptingCrossTenantWrite_ThrowsSecurityException()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantBContext = new TenantContext();
        tenantBContext.SetTenant(new Tenant { Id = tenantBId, Name = "Tenant B", Slug = "tenant-b" });

        using var scope = _factory.Services.CreateScope();
        var defaultDb = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        using var dbB = new MedicalPortalDbContext(
            new DbContextOptionsBuilder<MedicalPortalDbContext>()
                .UseSqlite(defaultDb.Database.GetDbConnection())
                .Options,
            tenantBContext);

        var personForTenantA = new Person
        {
            TenantId = tenantAId, // Deliberately mismatching tenant B's context
            FirstName = "Malicious",
            LastName = "CrossTenant",
            DateOfBirth = new DateTime(1992, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "male",
            Email = $"crosstenant.{Guid.NewGuid():N}@example.com"
        };
        dbB.Persons.Add(personForTenantA);

        var ex = await Assert.ThrowsAsync<SecurityException>(() => dbB.SaveChangesAsync());
        Assert.Contains("Cross-tenant entity creation is prohibited", ex.Message);
    }

    [Fact]
    public async Task MultiTenancy_CrossTenantMutation_AttemptingTenantReassignment_ThrowsSecurityException()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var defaultDb = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var person = new Person
        {
            TenantId = tenantAId,
            FirstName = "Target",
            LastName = "Person",
            DateOfBirth = new DateTime(1980, 5, 5, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "female",
            Email = $"reassign.{Guid.NewGuid():N}@example.com"
        };
        defaultDb.Persons.Add(person);
        await defaultDb.SaveChangesAsync();

        // Create Tenant A context
        var tenantAContext = new TenantContext();
        tenantAContext.SetTenant(new Tenant { Id = tenantAId, Name = "Tenant A", Slug = "tenant-a" });

        using var dbA = new MedicalPortalDbContext(
            new DbContextOptionsBuilder<MedicalPortalDbContext>()
                .UseSqlite(defaultDb.Database.GetDbConnection())
                .Options,
            tenantAContext);

        var loadedPerson = await dbA.Persons.FirstAsync(p => p.Id == person.Id);
        loadedPerson.TenantId = tenantBId; // Attempt reassigning tenant!

        var ex = await Assert.ThrowsAsync<SecurityException>(() => dbA.SaveChangesAsync());
        Assert.Contains("Modifying or reassigning an entity across tenants is prohibited", ex.Message);
    }

    [Fact]
    public async Task MultiTenancy_TokenTenantBinding_CrossTenantTokenMismatch_Returns403Forbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        // Ensure Adot-Hospital is provisioned
        var adotTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "Adot-Hospital");
        if (adotTenant == null)
        {
            await MultiTenancy_ProvisionTenant_ProvisionsAdotHospital_WithSeededDepartmentsAndKeyFingerprint();
        }

        // Create a user in DefaultTenant (Springfield)
        var user = await db.UserAccounts.FirstAsync(u => u.TenantId == TenantConstants.DefaultTenantId);
        var tokenForSpringfield = db.CreateAuthenticatedSession(tokens, user, user.PersonId);

        // Attempt to call Adot-Hospital with Springfield token
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/appointments/types");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenForSpringfield);
        request.Headers.Add("X-Tenant-Slug", "Adot-Hospital");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var errorDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenant_mismatch", errorDoc.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MultiTenancy_ModuleGating_DisabledModule_Returns403Forbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();

        // Provision a tenant with 'billing' disabled
        var restrictedSlug = $"restricted-{Guid.NewGuid():N}".Substring(0, 16);
        var restrictedTenant = await tenantService.ProvisionTenantAsync(new TenantProvisioningRequest(
            Name: "Restricted Clinic",
            Slug: restrictedSlug,
            Region: "US: Central",
            TimeZone: "America/Chicago",
            Languages: new List<string> { "en" },
            Settings: new TenantSettings
            {
                Modules = new TenantModuleSettings
                {
                    Enabled = new List<string> { "appointments" },
                    Disabled = new List<string> { "billing" } // Billing disabled!
                }
            }));

        // Create user in restricted tenant
        var person = new Person
        {
            TenantId = restrictedTenant.Id,
            FirstName = "Test",
            LastName = "Restricted",
            DateOfBirth = new DateTime(1995, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "male",
            Email = $"restricted.{Guid.NewGuid():N}@clinic.example"
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var account = new UserAccount
        {
            TenantId = restrictedTenant.Id,
            PersonId = person.Id,
            Email = person.Email,
            Role = UserRole.PATIENT,
            PasswordHash = "dummyhash"
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/billing/statements");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Slug", restrictedSlug);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var errorDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("module_disabled", errorDoc.GetProperty("code").GetString());
        Assert.Equal("billing", errorDoc.GetProperty("module").GetString());
    }

    [Fact]
    public async Task MultiTenancy_PlatformAdmin_HasZeroPhiAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var adminPerson = new Person
        {
            FirstName = "Strict",
            LastName = "PlatformAdmin",
            DateOfBirth = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "other",
            Email = $"strictplatadmin.{Guid.NewGuid():N}@platform.example"
        };
        db.Persons.Add(adminPerson);
        await db.SaveChangesAsync();

        var adminAccount = new UserAccount
        {
            PersonId = adminPerson.Id,
            Email = adminPerson.Email,
            Role = UserRole.PLATFORM_ADMIN,
            PasswordHash = "dummyhash"
        };
        db.UserAccounts.Add(adminAccount);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, adminAccount, adminPerson.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Attempting PHI: /api/clinical/allergies -> Forbidden 403
        var phiReq = new HttpRequestMessage(HttpMethod.Get, "/api/clinical/allergies");
        phiReq.Headers.Add("x-patient-id", Guid.NewGuid().ToString());
        var phiResponse = await client.SendAsync(phiReq);
        Assert.Equal(HttpStatusCode.Forbidden, phiResponse.StatusCode);

        // 2. Attempting FHIR Patient PHI: /R4/Patient/{id} -> Forbidden 403
        var fhirResponse = await client.GetAsync($"/R4/Patient/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, fhirResponse.StatusCode);

        // 3. Platform Admin CAN access platform tenant list: /api/platform/tenants -> OK 200
        var tenantsResponse = await client.GetAsync("/api/platform/tenants");
        Assert.Equal(HttpStatusCode.OK, tenantsResponse.StatusCode);
    }

    [Fact]
    public void MultiTenancy_PostgresRlsHelper_GeneratesRlsSqlForAllTenantScopedTables()
    {
        var tables = PostgresRlsHelper.TenantScopedTables;
        Assert.Equal(48, tables.Length); // 48 tenant scoped tables

        var sql = PostgresRlsHelper.GenerateRlsSql();
        Assert.Contains("ALTER TABLE \"Persons\" ENABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains("ALTER TABLE \"Patients\" ENABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains("ALTER TABLE \"Appointments\" ENABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains("ALTER TABLE \"AuditLogs\" ENABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains("CREATE POLICY \"Persons_tenant_isolation\" ON \"Persons\"", sql);
        Assert.Contains("current_setting('app.current_tenant_id', true)", sql);
    }
}
