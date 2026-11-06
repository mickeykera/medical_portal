using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
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

public class ProductizationAndProvisioningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductizationAndProvisioningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(string Token, UserAccount Account, Person Person)> CreateUserWithRoleAsync(
        UserRole role, Guid? tenantId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var person = new Person
        {
            TenantId = tenantId ?? TenantConstants.DefaultTenantId,
            FirstName = "Test",
            LastName = role.ToString(),
            DateOfBirth = new DateTime(1990, 5, 10, 0, 0, 0, DateTimeKind.Utc),
            SexAtBirth = "other",
            Email = $"{role.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}@hospital.example"
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var account = new UserAccount
        {
            TenantId = tenantId ?? TenantConstants.DefaultTenantId,
            PersonId = person.Id,
            Email = person.Email,
            Role = role,
            PasswordHash = "dummy_hash"
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokens, account, person.Id);
        return (token, account, person);
    }

    [Fact]
    public void SchemaValidation_ValidAdotConfig_SucceedsWithWcagAnalysis()
    {
        var settings = new TenantSettings
        {
            Branding = new TenantBrandingSettings
            {
                PrimaryColor = "#e01b24",
                Logo = "/img/adot.svg",
                SupportEmail = "support@adot.et"
            },
            Modules = new TenantModuleSettings(),
            Hosting = new TenantHostingSettings
            {
                Provider = "Hospital on-premises",
                DataResidency = "same country as hospital",
                Isolation = "Dedicated database"
            },
            Security = new TenantSecuritySettings
            {
                IdleTimeoutMinutes = 15,
                RetentionYears = 20,
                BreakGlass = true
            }
        };

        var departments = new List<string>
        {
            "Cardiology", "Gastroenterology", "Endocrinology", "Pulmonology",
            "Rheumatology", "Nephrology", "Laboratory Services",
            "Imaging & Radiology", "Infusion Suite", "Preventive & Wellness Care"
        };

        var result = TenantConfigurationValidator.Validate(
            name: "Adot Internal Medicine",
            slug: "Adot-Hospital",
            region: "Other: Ethiopia, Addis Ababa",
            timeZone: "Africa/Addis_Ababa",
            languages: new List<string> { "en", "am", "om" },
            settings: settings,
            facilitiesCount: 1,
            departments: departments);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.WcagAnalysis);
        Assert.True(result.WcagAnalysis.PassesAaLargeText);
        Assert.True(result.WcagAnalysis.ContrastAgainstWhite >= 4.5);
    }

    [Fact]
    public void SchemaValidation_InaccessibleColor_FailsWcagRequirement()
    {
        var settings = new TenantSettings
        {
            Branding = new TenantBrandingSettings
            {
                PrimaryColor = "#ffff00" // Bright yellow: contrast against white is ~1.07:1
            }
        };

        var result = TenantConfigurationValidator.Validate(
            name: "Low Contrast Hospital",
            slug: "low-contrast",
            region: "Other: Ethiopia",
            timeZone: "UTC",
            languages: new List<string> { "en" },
            settings: settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("failing WCAG 2.1 AA"));
    }

    [Fact]
    public void SchemaValidation_UnsupportedLanguageAndModuleErrors_Detected()
    {
        var settings = new TenantSettings
        {
            Modules = new TenantModuleSettings
            {
                Enabled = new List<string> { "appointments", "nonexistent_module" },
                Disabled = new List<string> { "appointments" } // Overlapping!
            },
            Security = new TenantSecuritySettings
            {
                IdleTimeoutMinutes = 2 // Too short (< 5)
            }
        };

        var result = TenantConfigurationValidator.Validate(
            name: "Validation Test Hospital",
            slug: "invalid_slug!@#", // Invalid characters
            region: "Other: Ethiopia",
            timeZone: "UTC",
            languages: new List<string> { "en", "fr" }, // "fr" not supported
            settings: settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Language 'fr' is not supported"));
        Assert.Contains(result.Errors, e => e.Contains("alphanumeric characters"));
        Assert.Contains(result.Errors, e => e.Contains("not a valid system module"));
        Assert.Contains(result.Errors, e => e.Contains("cannot appear in both Enabled and Disabled"));
        Assert.Contains(result.Errors, e => e.Contains("idle timeout must be between 5 and 120"));
    }

    [Fact]
    public async Task DryRunValidationEndpoint_ReturnsWcagAndValidationResult()
    {
        var (token, _, _) = await CreateUserWithRoleAsync(UserRole.PLATFORM_ADMIN);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new
        {
            name = "Dry Run Test Hospital",
            slug = "dry-run-hospital",
            region = "Other: Ethiopia, Addis Ababa",
            timeZone = "Africa/Addis_Ababa",
            languages = new List<string> { "en", "am" },
            facilitiesCount = 1,
            departments = new List<string> { "General Medicine" },
            settings = new
            {
                branding = new
                {
                    primaryColor = "#0284c7"
                },
                modules = new
                {
                    enabled = new List<string> { "appointments", "messaging" },
                    disabled = new List<string>()
                },
                hosting = new
                {
                    provider = "Hospital on-premises",
                    dataResidency = "same country as hospital",
                    isolation = "Shared database"
                },
                security = new
                {
                    idleTimeoutMinutes = 15,
                    retentionYears = 10,
                    breakGlass = true
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/platform/tenants/validate", requestBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("isValid").GetBoolean());
        Assert.True(json.GetProperty("wcag").GetProperty("passesAaLargeText").GetBoolean());
    }

    [Fact]
    public async Task HierarchicalInteropModuleGating_DisablingParentInterop_BlocksSubmodules()
    {
        var (adminToken, _, _) = await CreateUserWithRoleAsync(UserRole.PLATFORM_ADMIN);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var tenantSlug = $"test-interop-{Guid.NewGuid():N}"[..18];
        var provisionRequest = new
        {
            name = "Interop Gating Hospital",
            slug = tenantSlug,
            region = "Other: Ethiopia",
            timeZone = "UTC",
            languages = new List<string> { "en" },
            facilitiesCount = 1,
            departments = new List<string> { "HMIS" },
            settings = new
            {
                branding = new { primaryColor = "#0284c7" },
                modules = new
                {
                    enabled = new List<string> { "appointments" },
                    disabled = new List<string> { "interop" } // Parent interop disabled!
                },
                hosting = new { provider = "Hospital on-premises", dataResidency = "local", isolation = "Shared database" },
                security = new { idleTimeoutMinutes = 15, retentionYears = 10, breakGlass = true }
            }
        };

        var provResponse = await client.PostAsJsonAsync("/api/platform/tenants/provision", provisionRequest);
        Assert.Equal(HttpStatusCode.Created, provResponse.StatusCode);

        // Create an admin inside this tenant
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == tenantSlug);

        var (hospAdminToken, _, _) = await CreateUserWithRoleAsync(UserRole.ADMIN, tenant.Id);

        var tenantClient = _factory.CreateClient();
        tenantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hospAdminToken);
        tenantClient.DefaultRequestHeaders.Add("X-Tenant-Slug", tenantSlug);

        // Submodule 'dhis2' must be blocked because parent 'interop' is disabled
        var dhis2Response = await tenantClient.PostAsJsonAsync("/api/interop/dhis2/draft", new
        {
            period = "201902"
        });
        Assert.Equal(HttpStatusCode.Forbidden, dhis2Response.StatusCode);
        var dhis2Json = await dhis2Response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("module_disabled", dhis2Json.GetProperty("code").GetString());

        // Submodule 'mfr' must also be blocked because parent 'interop' is disabled
        var mfrResponse = await tenantClient.PostAsync("/api/interop/mfr/sync", null);
        Assert.Equal(HttpStatusCode.Forbidden, mfrResponse.StatusCode);
        var mfrJson = await mfrResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("module_disabled", mfrJson.GetProperty("code").GetString());
        Assert.Equal("mfr", mfrJson.GetProperty("module").GetString());
    }

    [Fact]
    public async Task TenantSettingsUpdate_ValidatesAndAppliesUpdatedBranding()
    {
        var (adminToken, _, _) = await CreateUserWithRoleAsync(UserRole.PLATFORM_ADMIN);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var tenantSlug = $"test-upd-{Guid.NewGuid():N}"[..18];
        var provisionRequest = new
        {
            name = "Update Settings Hospital",
            slug = tenantSlug,
            region = "Other: Ethiopia",
            timeZone = "UTC",
            languages = new List<string> { "en" },
            facilitiesCount = 1,
            departments = new List<string> { "General" },
            settings = new
            {
                branding = new { primaryColor = "#0284c7", supportEmail = "old@hospital.et" },
                modules = new { enabled = new List<string> { "appointments" }, disabled = new List<string>() },
                hosting = new { provider = "Hospital on-premises", dataResidency = "local", isolation = "Shared database" },
                security = new { idleTimeoutMinutes = 15, retentionYears = 10, breakGlass = true }
            }
        };

        var provResponse = await client.PostAsJsonAsync("/api/platform/tenants/provision", provisionRequest);
        var provJson = await provResponse.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(provJson.GetProperty("tenant").GetProperty("id").GetString()!);

        // 1. Valid update with Adot red color
        var updatedSettings = new TenantSettings
        {
            Branding = new TenantBrandingSettings
            {
                PrimaryColor = "#e01b24",
                SupportEmail = "support@adot.et",
                Logo = "/img/new-logo.svg"
            },
            Modules = new TenantModuleSettings(),
            Hosting = new TenantHostingSettings { Provider = "Hospital on-premises", DataResidency = "Ethiopia", Isolation = "Dedicated database" },
            Security = new TenantSecuritySettings { IdleTimeoutMinutes = 20, RetentionYears = 25, BreakGlass = true }
        };

        var updateResponse = await client.PutAsJsonAsync($"/api/platform/tenants/{tenantId}/settings", updatedSettings);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // 2. Fetch updated tenant details
        var getResponse = await client.GetAsync($"/api/platform/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getJson = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("#e01b24", getJson.GetProperty("settings").GetProperty("branding").GetProperty("primaryColor").GetString());
        Assert.Equal("support@adot.et", getJson.GetProperty("settings").GetProperty("branding").GetProperty("supportEmail").GetString());
    }

    [Fact]
    public async Task ExportTenantConfig_ReturnsCleanPortableJson()
    {
        var (adminToken, _, _) = await CreateUserWithRoleAsync(UserRole.PLATFORM_ADMIN);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var tenantSlug = $"test-exp-{Guid.NewGuid():N}"[..18];
        var provisionRequest = new
        {
            name = "Export Test Hospital",
            slug = tenantSlug,
            hostDomain = "portal.exporthospital.et",
            region = "Other: Ethiopia, Addis Ababa",
            timeZone = "Africa/Addis_Ababa",
            languages = new List<string> { "en", "am" },
            facilitiesCount = 1,
            departments = new List<string> { "Emergency" },
            settings = new
            {
                branding = new { primaryColor = "#0284c7" },
                modules = new { enabled = new List<string> { "appointments" }, disabled = new List<string>() },
                hosting = new { provider = "Hospital on-premises", dataResidency = "local", isolation = "Shared database" },
                security = new { idleTimeoutMinutes = 15, retentionYears = 10, breakGlass = true }
            }
        };

        var provResponse = await client.PostAsJsonAsync("/api/platform/tenants/provision", provisionRequest);
        var provJson = await provResponse.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = Guid.Parse(provJson.GetProperty("tenant").GetProperty("id").GetString()!);

        var exportResponse = await client.GetAsync($"/api/platform/tenants/{tenantId}/export-config");
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        var exportJson = await exportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Export Test Hospital", exportJson.GetProperty("name").GetString());
        Assert.Equal("portal.exporthospital.et", exportJson.GetProperty("hostDomain").GetString());
        Assert.Equal("Africa/Addis_Ababa", exportJson.GetProperty("timeZone").GetString());
        Assert.Equal(JsonValueKind.Object, exportJson.GetProperty("settings").ValueKind);
    }

    [Fact]
    public async Task NonPlatformAdmin_CannotAccessPlatformTenantEndpoints()
    {
        // A physician attempts to call platform tenant management APIs
        var (doctorToken, _, _) = await CreateUserWithRoleAsync(UserRole.PHYSICIAN);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doctorToken);

        var validateRequest = new
        {
            name = "Unauthorized Hospital",
            slug = "unauth-hospital",
            region = "Other: Ethiopia",
            timeZone = "UTC",
            languages = new List<string> { "en" },
            settings = new
            {
                branding = new { primaryColor = "#0284c7" }
            }
        };
        var validateResponse = await client.PostAsJsonAsync("/api/platform/tenants/validate", validateRequest);
        Assert.Equal(HttpStatusCode.Forbidden, validateResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/platform/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var toggleResponse = await client.PatchAsJsonAsync($"/api/platform/tenants/{Guid.NewGuid()}/modules", new { module = "findcare", enabled = false });
        Assert.Equal(HttpStatusCode.Forbidden, toggleResponse.StatusCode);
    }

    [Fact]
    public void AdotHospitalTenantConfig_MatchesFormalSpecification()
    {
        var configPath = RepoPaths.AdotTenantConfig;
        Assert.True(File.Exists(configPath), $"File not found: {configPath}");

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Adot Internal Medicine", root.GetProperty("name").GetString());
        Assert.Equal("Adot-Hospital", root.GetProperty("slug").GetString());
        Assert.Equal("portal.adot.et", root.GetProperty("hostDomain").GetString());
        Assert.Equal("Dedicated database", root.GetProperty("settings").GetProperty("hosting").GetProperty("isolation").GetString());

        var departments = root.GetProperty("departments").EnumerateArray().Select(d => d.GetString()!).ToList();
        Assert.Equal(10, departments.Count);
        Assert.Contains("Cardiology", departments);
        Assert.Contains("Nephrology", departments);
        Assert.Contains("Preventive & Wellness Care", departments);

        var languages = root.GetProperty("languages").EnumerateArray().Select(l => l.GetString()!).ToList();
        Assert.Contains("en", languages);
        Assert.Contains("am", languages);
        Assert.Contains("om", languages);
    }
}
