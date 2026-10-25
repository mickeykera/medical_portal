using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class DevLoginsAndIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DevLoginsAndIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ProductionStartupGuards_RefuseStartupIfDevArtifactsOrAccountsPresent()
    {
        // 1. Refuses if DEV_SEED_PASSWORD is present in Production
        Assert.Throws<InvalidOperationException>(() =>
        {
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting("DEV_SEED_PASSWORD", "random_dev_password_123");
                })
                .CreateClient();
        });

        // 2. Refuses if DEV_TOTP_SECRET is present in Production
        Assert.Throws<InvalidOperationException>(() =>
        {
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting("DEV_TOTP_SECRET", "JBSWY3DPEHPK3PXP");
                })
                .CreateClient();
        });

        // 3. Refuses if DEV_QUICK_LOGIN is true in Production
        Assert.Throws<InvalidOperationException>(() =>
        {
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting("DEV_QUICK_LOGIN", "true");
                })
                .CreateClient();
        });

        // 4. Refuses if any IsDevAccount row exists in DB in Production
        var origDevSecret = Environment.GetEnvironmentVariable("DEV_TOTP_SECRET");
        var origStripeFake = Environment.GetEnvironmentVariable("STRIPE_USE_FAKE");
        var origDevSeedPw = Environment.GetEnvironmentVariable("DEV_SEED_PASSWORD");
        var origDevQuick = Environment.GetEnvironmentVariable("DEV_QUICK_LOGIN");

        Environment.SetEnvironmentVariable("DEV_TOTP_SECRET", null);
        Environment.SetEnvironmentVariable("DEV_SEED_PASSWORD", null);
        Environment.SetEnvironmentVariable("DEV_QUICK_LOGIN", null);
        Environment.SetEnvironmentVariable("STRIPE_USE_FAKE", "false");
        Environment.SetEnvironmentVariable("HMS_USE_FAKE", "false");
        Environment.SetEnvironmentVariable("HMS_WEBHOOK_SECRET", "prod-webhook-secret-value-12345");
        Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "sk_live_valid_production_stripe_key_12345");

        var dbName = $"prod_guard_test_{Guid.NewGuid():N}.db";
        var connectionString = $"Data Source={dbName}";

        try
        {
            var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using (var db = new MedicalPortalDbContext(options))
            {
                db.Database.EnsureCreated();
                var person = new Person
                {
                    FirstName = "Dev",
                    LastName = "User",
                    Email = "devuser@test.example",
                    DateOfBirth = DateTime.UtcNow.AddYears(-30)
                };
                db.Persons.Add(person);
                db.UserAccounts.Add(new UserAccount
                {
                    Person = person,
                    Email = "devuser@test.example",
                    PasswordHash = "hash",
                    Role = UserRole.PATIENT,
                    IsDevAccount = true
                });
                db.SaveChanges();
            }

            using var customFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting("DEV_TOTP_SECRET", "");
                    builder.UseSetting("DEV_SEED_PASSWORD", "");
                    builder.UseSetting("DEV_QUICK_LOGIN", "false");
                    builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                    builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                    builder.UseSetting("Stripe:UseFake", "false");
                    builder.UseSetting("Stripe:SecretKey", "sk_live_valid_production_stripe_key_12345");
                    builder.UseSetting("Hms:UseFake", "false");
                    builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                    builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
                    builder.UseSetting("ClinicalCore:UseFake", "false");
                    builder.UseSetting("ClinicalCore:BaseUrl", "https://fhir.internal");

                    builder.ConfigureServices(services =>
                    {
                        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<MedicalPortalDbContext>));
                        if (descriptor != null) services.Remove(descriptor);

                        services.AddDbContext<MedicalPortalDbContext>(opts =>
                        {
                            opts.UseSqlite(connectionString);
                        });
                    });
                });

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = customFactory.Server;
            });
            Assert.Contains("Production startup failed: Dev accounts are strictly prohibited in production", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEV_TOTP_SECRET", origDevSecret);
            Environment.SetEnvironmentVariable("DEV_SEED_PASSWORD", origDevSeedPw);
            Environment.SetEnvironmentVariable("DEV_QUICK_LOGIN", origDevQuick);
            Environment.SetEnvironmentVariable("STRIPE_USE_FAKE", origStripeFake);
            if (File.Exists(dbName)) File.Delete(dbName);
        }
    }

    [Fact]
    public void FrontendAndDockerHygiene_NoDevCredentialsSecretsScriptsOrPickerFiles()
    {
        var frontendDir = RepoPaths.Frontend;
        Assert.True(Directory.Exists(frontendDir), $"Frontend directory not found at {frontendDir}");

        var files = Directory.GetFiles(frontendDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".woff2") && !f.EndsWith(".png") && !f.EndsWith(".ico"))
            .ToList();

        Assert.NotEmpty(files);

        // Verify no dev-picker or quick-login-picker files exist in frontend
        var pickerFiles = files.Where(f =>
            f.Contains("dev-picker", StringComparison.OrdinalIgnoreCase) ||
            f.Contains("quick-login", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(pickerFiles);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("DEV_SEED_PASSWORD", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DEV_TOTP_SECRET", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dev-logins.sh", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dev-totp.sh", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dev-setup.sh", content, StringComparison.OrdinalIgnoreCase);
        }

        // Verify .dockerignore excludes scripts and env files
        var dockerignorePath = Path.Combine(RepoPaths.Root, ".dockerignore");
        Assert.True(File.Exists(dockerignorePath));
        var dockerignore = File.ReadAllText(dockerignorePath);
        Assert.Contains("scripts/", dockerignore);
        Assert.Contains(".env", dockerignore);
    }

    [Fact]
    public async Task QuickLoginPicker_Endpoint404InProduction()
    {
        var origQuick = Environment.GetEnvironmentVariable("DEV_QUICK_LOGIN");
        try
        {
            Environment.SetEnvironmentVariable("DEV_QUICK_LOGIN", "false");
            // 1. In Development when DEV_QUICK_LOGIN is false, endpoint returns 404
            using var devDisabledFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DEV_QUICK_LOGIN", "false");
            });
            var devClient = devDisabledFactory.CreateClient();
            var devRes = await devClient.GetAsync("/api/dev/quick-logins");
            Assert.Equal(HttpStatusCode.NotFound, devRes.StatusCode);

            // 2. In Production, startup guard refuses DEV_QUICK_LOGIN
            Assert.Throws<InvalidOperationException>(() =>
            {
                new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Production");
                        builder.UseSetting("DEV_QUICK_LOGIN", "true");
                    })
                    .CreateClient();
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEV_QUICK_LOGIN", origQuick);
        }
    }

    [Fact]
    public async Task SeededLogins_AllRolesCanAuthenticate_AndHaveExpectedAccess()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var totpService = scope.ServiceProvider.GetRequiredService<ITotpService>();

        var envPath = Path.Combine(RepoPaths.Root, ".env");
        var devPassword = "ChangeMe!Demo2024";
        var devTotpSecret = "JBSWY3DPEHPK3PXP";

        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("DEV_SEED_PASSWORD=")) devPassword = line.Split('=', 2)[1].Trim(' ', '"', '\'');
                if (line.StartsWith("DEV_TOTP_SECRET=")) devTotpSecret = line.Split('=', 2)[1].Trim(' ', '"', '\'');
            }
        }

        var seededRoles = new[]
        {
            new { Email = "almaz.bekele@clinic.example", Role = UserRole.PATIENT, Mfa = false, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "tigist.hailu@clinic.example", Role = UserRole.PATIENT, Mfa = false, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "apatel@clinic.example", Role = UserRole.CLINICIAN, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "sister.bethlehem@clinic.example", Role = UserRole.NURSE, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "sara.tefera@clinic.example", Role = UserRole.SCHEDULER, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "daniel.worku@clinic.example", Role = UserRole.BILLING, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "meron.assefa@clinic.example", Role = UserRole.LAB, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "eyob.getachew@clinic.example", Role = UserRole.PHARMACIST, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "deptadmin.fam@clinic.example", Role = UserRole.DEPT_ADMIN, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "deptadmin.card@clinic.example", Role = UserRole.DEPT_ADMIN, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "admin@clinic.example", Role = UserRole.ADMIN, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "auditor.compliance@clinic.example", Role = UserRole.COMPLIANCE_AUDITOR, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "platform.admin@clinic.example", Role = UserRole.PLATFORM_ADMIN, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "system.admin@clinic.example", Role = UserRole.SYSTEM_ADMIN, Mfa = true, Tenant = TenantConstants.DefaultTenantSlug },
            new { Email = "admin.adot@clinic.example", Role = UserRole.ADMIN, Mfa = true, Tenant = "adot-hospital" },
            new { Email = "patient.adot@clinic.example", Role = UserRole.PATIENT, Mfa = false, Tenant = "adot-hospital" }
        };

        foreach (var seeded in seededRoles)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", seeded.Tenant);

            var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = seeded.Email,
                password = devPassword
            });

            Assert.True(loginRes.IsSuccessStatusCode, $"Login failed for {seeded.Email}: {await loginRes.Content.ReadAsStringAsync()}");

            var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();

            string? sessionToken = null;

            if (seeded.Mfa)
            {
                Assert.True(loginBody.GetProperty("mfaRequired").GetBoolean(), $"MFA required was expected true for {seeded.Email}");
                var mfaToken = loginBody.GetProperty("mfaToken").GetString();
                Assert.NotNull(mfaToken);

                var account = await db.UserAccounts.IgnoreQueryFilters().FirstAsync(u => u.Email == seeded.Email);
                var secret = account.MfaSecret ?? devTotpSecret;
                var mfaCode = totpService.GenerateTotpCode(secret);

                var verifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
                {
                    mfaToken,
                    code = mfaCode
                });

                Assert.True(verifyRes.IsSuccessStatusCode, $"MFA verify failed for {seeded.Email}: {await verifyRes.Content.ReadAsStringAsync()}");
                var verifyBody = await verifyRes.Content.ReadFromJsonAsync<JsonElement>();
                sessionToken = verifyBody.GetProperty("token").GetString();
            }
            else
            {
                if (loginBody.TryGetProperty("token", out var tokenProp))
                {
                    sessionToken = tokenProp.GetString();
                }
            }

            // Call /api/auth/me to verify user identity & role
            using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            if (!string.IsNullOrEmpty(sessionToken))
            {
                meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
            }
            meReq.Headers.Add("X-Tenant-Slug", seeded.Tenant);

            var meRes = await client.SendAsync(meReq);
            Assert.True(meRes.IsSuccessStatusCode, $"/api/auth/me failed for {seeded.Email}: {await meRes.Content.ReadAsStringAsync()}");

            var meBody = await meRes.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(seeded.Role.ToString(), meBody.GetProperty("user").GetProperty("role").GetString());
            Assert.Equal(seeded.Email, meBody.GetProperty("user").GetProperty("email").GetString());
        }
    }

    [Fact]
    public async Task PlatformAdmin_ZeroPhiGuarantee_ProhibitedFromAllPatientAndClinicalData()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantConstants.DefaultTenantSlug);

        var envPath = Path.Combine(RepoPaths.Root, ".env");
        var devPassword = "ChangeMe!Demo2024";
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("DEV_SEED_PASSWORD=")) devPassword = line.Split('=', 2)[1].Trim(' ', '"', '\'');
            }
        }

        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "platform.admin@clinic.example",
            password = devPassword
        });

        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var mfaToken = loginBody.GetProperty("mfaToken").GetString()!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var totpService = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var account = await db.UserAccounts.IgnoreQueryFilters().FirstAsync(u => u.Email == "platform.admin@clinic.example");
        var code = totpService.GenerateTotpCode(account.MfaSecret!);

        var verifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken, code });
        var token = (await verifyRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        // 1. /api/auth/me returns zero managing patients
        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meRes = await client.SendAsync(meReq);
        var meBody = await meRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, meBody.GetProperty("managing").GetArrayLength());

        // 2. Prohibited from Clinical Snapshot
        using var snapReq = new HttpRequestMessage(HttpMethod.Get, "/api/clinical/snapshot");
        snapReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        snapReq.Headers.Add("x-patient-id", Guid.NewGuid().ToString());
        var snapRes = await client.SendAsync(snapReq);
        Assert.Equal(HttpStatusCode.Forbidden, snapRes.StatusCode);

        // 3. Prohibited from Vitals
        using var vitalsReq = new HttpRequestMessage(HttpMethod.Get, "/api/clinical/vitals");
        vitalsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        vitalsReq.Headers.Add("x-patient-id", Guid.NewGuid().ToString());
        var vitalsRes = await client.SendAsync(vitalsReq);
        Assert.Equal(HttpStatusCode.Forbidden, vitalsRes.StatusCode);

        // 4. Prohibited from Medications
        using var medsReq = new HttpRequestMessage(HttpMethod.Get, "/api/clinical/medications");
        medsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        medsReq.Headers.Add("x-patient-id", Guid.NewGuid().ToString());
        var medsRes = await client.SendAsync(medsReq);
        Assert.Equal(HttpStatusCode.Forbidden, medsRes.StatusCode);

        // 5. Allowed to access platform tenant settings
        using var tenantReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/tenant-settings");
        tenantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tenantRes = await client.SendAsync(tenantReq);
        Assert.Equal(HttpStatusCode.OK, tenantRes.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_Isolation_Tenant2AdminCannotAccessTenant1()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", "adot-hospital");

        var envPath = Path.Combine(RepoPaths.Root, ".env");
        var devPassword = "ChangeMe!Demo2024";
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("DEV_SEED_PASSWORD=")) devPassword = line.Split('=', 2)[1].Trim(' ', '"', '\'');
            }
        }

        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin.adot@clinic.example",
            password = devPassword
        });

        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var mfaToken = loginBody.GetProperty("mfaToken").GetString()!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var totpService = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var account = await db.UserAccounts.IgnoreQueryFilters().FirstAsync(u => u.Email == "admin.adot@clinic.example");
        var code = totpService.GenerateTotpCode(account.MfaSecret!);

        var verifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new { mfaToken, code });
        var token = (await verifyRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        // 1. Can access Tenant 2 users
        using var t2UsersReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        t2UsersReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        t2UsersReq.Headers.Add("X-Tenant-Slug", "adot-hospital");
        var t2UsersRes = await client.SendAsync(t2UsersReq);
        Assert.Equal(HttpStatusCode.OK, t2UsersRes.StatusCode);

        // 2. Attempt to query Tenant 1 resources using Tenant 2 token returns Forbidden
        using var crossTenantReq = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        crossTenantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        crossTenantReq.Headers.Add("X-Tenant-Slug", TenantConstants.DefaultTenantSlug);
        var crossTenantRes = await client.SendAsync(crossTenantReq);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRes.StatusCode);
    }

    [Fact]
    public async Task ProxyGrant_TigistHailu_CanManageYonas_WithPartialPermissions()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", TenantConstants.DefaultTenantSlug);

        var envPath = Path.Combine(RepoPaths.Root, ".env");
        var devPassword = "ChangeMe!Demo2024";
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("DEV_SEED_PASSWORD=")) devPassword = line.Split('=', 2)[1].Trim(' ', '"', '\'');
            }
        }

        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "tigist.hailu@clinic.example",
            password = devPassword
        });

        Assert.True(loginRes.IsSuccessStatusCode);
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("token").GetString()!;

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meRes = await client.SendAsync(meReq);
        Assert.True(meRes.IsSuccessStatusCode);

        var meBody = await meRes.Content.ReadFromJsonAsync<JsonElement>();
        var managing = meBody.GetProperty("managing").EnumerateArray().ToList();
        Assert.NotEmpty(managing);

        var yonas = managing.FirstOrDefault(m => m.GetProperty("firstName").GetString() == "Yonas");
        Assert.True(yonas.ValueKind != JsonValueKind.Undefined, "Yonas was expected in managing list");
        Assert.False(yonas.GetProperty("isSelf").GetBoolean());
    }

    [Fact]
    public void DevLoginsScript_RefusesInProduction_AndGeneratesFormattedTableInDev()
    {
        var scriptPath = Path.Combine(RepoPaths.Root, "scripts", "dev-logins.sh");
        Assert.True(File.Exists(scriptPath), $"Script not found at {scriptPath}");

        // 1. Refuses in Production
        var prodProc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = scriptPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };
        prodProc.StartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Production";
        prodProc.Start();
        prodProc.WaitForExit();
        Assert.NotEqual(0, prodProc.ExitCode);
        var errOutput = prodProc.StandardError.ReadToEnd();
        Assert.Contains("strictly prohibited in Production", errOutput);

        // 2. Runs successfully in Development
        var devProc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = scriptPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };
        devProc.StartInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        devProc.Start();
        devProc.WaitForExit();
        Assert.Equal(0, devProc.ExitCode);
        var output = devProc.StandardOutput.ReadToEnd();
        Assert.Contains("MEDICAL PORTAL - DEVELOPMENT LOGINS", output);
        Assert.Contains("almaz.bekele@clinic.example", output);
        Assert.Contains("apatel@clinic.example", output);
        Assert.Contains("admin@clinic.example", output);
    }
}
