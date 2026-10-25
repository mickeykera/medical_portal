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

public class MfaDevHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string LegacySeedSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    public MfaDevHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TaskA2_FrontendFiles_ContainNoDevTotpOrSeedSecret()
    {
        var frontendDir = RepoPaths.Frontend;
        Assert.True(Directory.Exists(frontendDir), $"Frontend directory not found at {frontendDir}");

        var files = Directory.GetFiles(frontendDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".woff2") && !f.EndsWith(".png") && !f.EndsWith(".ico"))
            .ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("dev-totp", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(LegacySeedSecret, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TaskA2_LoginAndMfaScreens_RenderGenericTextOnlyWithoutScriptsOrDevMentions()
    {
        var appJsPath = Path.Combine(RepoPaths.Frontend, "js", "app.js");
        Assert.True(File.Exists(appJsPath));

        var appJs = File.ReadAllText(appJsPath);

        // Extract renderSignIn markup
        var signInMatch = Regex.Match(appJs, @"function renderSignIn\(\)\s*\{([\s\S]*?)\n\s*\}", RegexOptions.Multiline);
        Assert.True(signInMatch.Success, "renderSignIn function should exist in app.js");

        var signInHtml = signInMatch.Groups[1].Value;

        // Must not contain script execution references or dev tooling
        Assert.DoesNotContain("dev-totp", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scripts/", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("./scripts", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dev", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacySeedSecret, signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo-buttons", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo-patient", signInHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo-clinician", signInHtml, StringComparison.OrdinalIgnoreCase);

        // Must contain generic MFA instructions
        Assert.Contains("Two-factor authentication code required. Enter the 6-digit code from your authenticator app.", signInHtml);
    }

    [Fact]
    public async Task TaskA2_LoginAndMfaApiResponses_ContainNoDevTotpDevScriptOrSeedSecret()
    {
        var client = _factory.CreateClient();

        // 1. Password step for clinician
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "apatel@clinic.example",
            password = DbSeeder.DemoPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginRaw = await loginRes.Content.ReadAsStringAsync();

        Assert.DoesNotContain("dev-totp", loginRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dev", loginRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", loginRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacySeedSecret, loginRaw, StringComparison.OrdinalIgnoreCase);

        var loginJson = JsonSerializer.Deserialize<JsonElement>(loginRaw);
        var mfaToken = loginJson.GetProperty("mfaToken").GetString()!;

        // 2. Generate valid TOTP
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var clinician = await db.UserAccounts.FirstAsync(u => u.Email == "apatel@clinic.example");
        var totp = _factory.Services.GetRequiredService<ITotpService>();
        var validCode = totp.GenerateTotpCode(clinician.MfaSecret!);

        // 3. MFA step
        var verifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken,
            code = validCode
        });
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        var verifyRaw = await verifyRes.Content.ReadAsStringAsync();

        Assert.DoesNotContain("dev-totp", verifyRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LegacySeedSecret, verifyRaw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskA4_Seeder_UsesDevTotpSecretOnlyWhenDevelopmentAndVariableSet()
    {
        var testSecret = "TESTDEVSECRETFORDEVELOPMENT12345";
        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();

        // Case 1: Development with secret set -> uses secret
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        using (var db = new MedicalPortalDbContext(options))
        {
            await DbSeeder.SeedAsync(db, hasher, isProduction: false, devTotpSecret: testSecret);
            var patel = await db.UserAccounts.FirstAsync(u => u.Email == "apatel@clinic.example");
            var admin = await db.UserAccounts.FirstAsync(u => u.Email == "admin@clinic.example");

            Assert.True(patel.MfaEnabled);
            Assert.Equal(testSecret, patel.MfaSecret);
            Assert.True(admin.MfaEnabled);
            Assert.Equal(testSecret, admin.MfaSecret);
        }

        // Case 2: Development without secret set -> MFA not enabled with dev secret
        var originalSecret = Environment.GetEnvironmentVariable("DEV_TOTP_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("DEV_TOTP_SECRET", null);
            var options2 = new DbContextOptionsBuilder<MedicalPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var db2 = new MedicalPortalDbContext(options2);
            await DbSeeder.SeedAsync(db2, hasher, isProduction: false, devTotpSecret: null);
            var patel = await db2.UserAccounts.FirstAsync(u => u.Email == "apatel@clinic.example");
            var admin = await db2.UserAccounts.FirstAsync(u => u.Email == "admin@clinic.example");

            Assert.False(patel.MfaEnabled);
            Assert.Null(patel.MfaSecret);
            Assert.False(admin.MfaEnabled);
            Assert.Null(admin.MfaSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEV_TOTP_SECRET", originalSecret);
        }
    }

    [Fact]
    public void TaskA4_StartupGuard_RefusesToStartInProductionIfDevTotpSecretPresent()
    {
        using var customFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("DEV_TOTP_SECRET", "SOME_DEV_SECRET_VALUE_12345");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = customFactory.Server;
        });

        Assert.Contains("Production startup failed: DEV_TOTP_SECRET is strictly prohibited in production", ex.Message);
    }

    [Fact]
    public void TaskA4_StartupGuard_RefusesToStartInProductionIfDevAccountExists()
    {
        // Setup SQLite DB with dev accounts
        var dbPath = Path.Combine(Path.GetTempPath(), $"guard_test_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseSqlite(connStr)
            .Options;

        using (var db = new MedicalPortalDbContext(options))
        {
            db.Database.EnsureCreated();
            var person = new Person { FirstName = "Dr", LastName = "Patel", Email = "apatel@clinic.example" };
            db.Persons.Add(person);
            db.UserAccounts.Add(new UserAccount
            {
                Email = "apatel@clinic.example",
                PasswordHash = "fakehash",
                Person = person,
                Role = UserRole.CLINICIAN
            });
            db.SaveChanges();
        }

        var origDevSecret = Environment.GetEnvironmentVariable("DEV_TOTP_SECRET");
        var origStripeFake = Environment.GetEnvironmentVariable("STRIPE_USE_FAKE");
        var origHmsFake = Environment.GetEnvironmentVariable("HMS_USE_FAKE");
        var origHmsSecret = Environment.GetEnvironmentVariable("HMS_WEBHOOK_SECRET");
        var origStripeKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");

        Environment.SetEnvironmentVariable("DEV_TOTP_SECRET", null);
        Environment.SetEnvironmentVariable("STRIPE_USE_FAKE", "false");
        Environment.SetEnvironmentVariable("HMS_USE_FAKE", "false");
        Environment.SetEnvironmentVariable("HMS_WEBHOOK_SECRET", "prod-webhook-secret-value-12345");
        Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", "sk_live_valid_production_stripe_key_12345");

        try
        {
            using var customFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting("DEV_TOTP_SECRET", "");
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
                            opts.UseSqlite(connStr);
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
            Environment.SetEnvironmentVariable("STRIPE_USE_FAKE", origStripeFake);
            Environment.SetEnvironmentVariable("HMS_USE_FAKE", origHmsFake);
            Environment.SetEnvironmentVariable("HMS_WEBHOOK_SECRET", origHmsSecret);
            Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", origStripeKey);
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task TaskA5_RealMfaFlow_EnrollmentAndRecoveryCodeLogin()
    {
        var client = _factory.CreateClient();

        // 1. Create a fresh test user
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var totpService = scope.ServiceProvider.GetRequiredService<ITotpService>();

        var testEmail = $"mfa_normal_user_{Guid.NewGuid():N}@patient.et";
        var testPassword = "SafePassword!2026Secure";

        var person = new Person
        {
            FirstName = "Mfa",
            LastName = "User",
            Email = testEmail
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var account = new UserAccount
        {
            Person = person,
            PersonId = person.Id,
            Email = testEmail,
            PasswordHash = hasher.HashPassword(testPassword),
            Role = UserRole.PATIENT,
            TenantId = TenantConstants.DefaultTenantId,
            MfaEnabled = false
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        // 2. Normal user logs in (MFA not yet enabled)
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = testPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginData = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginData.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // 3. User initiates MFA setup
        using var setupReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/setup");
        setupReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        setupReq.Content = JsonContent.Create(new { });

        var setupRes = await client.SendAsync(setupReq);
        Assert.Equal(HttpStatusCode.OK, setupRes.StatusCode);

        var setupData = await setupRes.Content.ReadFromJsonAsync<JsonElement>();
        var mfaSecret = setupData.GetProperty("secret").GetString();
        var qrCodeUri = setupData.GetProperty("qrCodeUri").GetString();

        Assert.False(string.IsNullOrWhiteSpace(mfaSecret));
        Assert.StartsWith("otpauth://totp/", qrCodeUri);
        Assert.Contains(mfaSecret, qrCodeUri);

        // 4. User enrolls with valid TOTP code generated from manual secret
        var enrollCode = totpService.GenerateTotpCode(mfaSecret!);
        using var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        enrollReq.Content = JsonContent.Create(new
        {
            secret = mfaSecret,
            code = enrollCode
        });

        var enrollRes = await client.SendAsync(enrollReq);
        Assert.Equal(HttpStatusCode.OK, enrollRes.StatusCode);

        var enrollData = await enrollRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(enrollData.GetProperty("success").GetBoolean());
        var recoveryCodes = enrollData.GetProperty("recoveryCodes")
            .EnumerateArray()
            .Select(c => c.GetString()!)
            .ToList();

        Assert.Equal(8, recoveryCodes.Count);

        // 5. User logs out / logs in again -> now MFA is required
        var secondLoginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = testPassword
        });
        Assert.Equal(HttpStatusCode.OK, secondLoginRes.StatusCode);
        var secondLoginData = await secondLoginRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondLoginData.GetProperty("mfaRequired").GetBoolean());
        var challengeToken = secondLoginData.GetProperty("mfaToken").GetString()!;

        // 6. User logs in using a recovery code instead of TOTP
        var chosenRecoveryCode = recoveryCodes[0];
        var recoveryVerifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken = challengeToken,
            code = chosenRecoveryCode
        });
        Assert.Equal(HttpStatusCode.OK, recoveryVerifyRes.StatusCode);

        var recoveryVerifyData = await recoveryVerifyRes.Content.ReadFromJsonAsync<JsonElement>();
        var sessionToken = recoveryVerifyData.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionToken));

        // 7. Verify the used recovery code cannot be reused
        var thirdLoginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = testPassword
        });
        var thirdChallengeToken = (await thirdLoginRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaToken").GetString()!;

        var reuseVerifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken = thirdChallengeToken,
            code = chosenRecoveryCode
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseVerifyRes.StatusCode);
    }

    [Fact]
    public async Task TaskA_SuccessfulClinicianAndAdminLoginUsingDevTotp()
    {
        var client = _factory.CreateClient();
        var config = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var envSecret = config["DEV_TOTP_SECRET"] ?? Environment.GetEnvironmentVariable("DEV_TOTP_SECRET");
        if (string.IsNullOrWhiteSpace(envSecret))
        {
            var envPath = Path.Combine(RepoPaths.Root, ".env");
            if (File.Exists(envPath))
            {
                var match = Regex.Match(File.ReadAllText(envPath), @"^DEV_TOTP_SECRET=(.+)$", RegexOptions.Multiline);
                if (match.Success) envSecret = match.Groups[1].Value.Trim();
            }
        }
        Assert.False(string.IsNullOrWhiteSpace(envSecret), "DEV_TOTP_SECRET must be set for dev logins");

        var totpService = _factory.Services.GetRequiredService<ITotpService>();

        // --- 1. Seeded Clinician Login ---
        var clinicianLoginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "apatel@clinic.example",
            password = DbSeeder.DemoPassword
        });
        Assert.Equal(HttpStatusCode.OK, clinicianLoginRes.StatusCode);
        var clinicianLoginData = await clinicianLoginRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(clinicianLoginData.GetProperty("mfaRequired").GetBoolean());
        var clinicianMfaToken = clinicianLoginData.GetProperty("mfaToken").GetString()!;

        var clinicianCode = totpService.GenerateTotpCode(envSecret);
        var clinicianVerifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken = clinicianMfaToken,
            code = clinicianCode
        });
        Assert.Equal(HttpStatusCode.OK, clinicianVerifyRes.StatusCode);
        var clinicianVerifyData = await clinicianVerifyRes.Content.ReadFromJsonAsync<JsonElement>();
        var clinicianToken = clinicianVerifyData.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(clinicianToken));
        Assert.Equal("CLINICIAN", clinicianVerifyData.GetProperty("user").GetProperty("role").GetString());

        // --- 2. Seeded Admin Login ---
        var adminLoginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@clinic.example",
            password = DbSeeder.DemoPassword
        });
        Assert.Equal(HttpStatusCode.OK, adminLoginRes.StatusCode);
        var adminLoginData = await adminLoginRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(adminLoginData.GetProperty("mfaRequired").GetBoolean());
        var adminMfaToken = adminLoginData.GetProperty("mfaToken").GetString()!;

        var adminCode = totpService.GenerateTotpCode(envSecret);
        var adminVerifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken = adminMfaToken,
            code = adminCode
        });
        Assert.Equal(HttpStatusCode.OK, adminVerifyRes.StatusCode);
        var adminVerifyData = await adminVerifyRes.Content.ReadFromJsonAsync<JsonElement>();
        var adminToken = adminVerifyData.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(adminToken));
        Assert.Equal("ADMIN", adminVerifyData.GetProperty("user").GetProperty("role").GetString());
    }
}
