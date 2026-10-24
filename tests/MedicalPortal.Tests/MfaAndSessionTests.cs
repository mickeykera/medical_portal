using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class MfaAndSessionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MfaAndSessionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TotpService_GeneratesAndVerifiesValidCode()
    {
        var totp = new TotpService();
        var secret = totp.GenerateSecret();
        Assert.False(string.IsNullOrWhiteSpace(secret));

        var code = totp.GenerateTotpCode(secret);
        Assert.Equal(6, code.Length);

        var isValid = totp.VerifyTotpCode(secret, code);
        Assert.True(isValid, "TOTP code should verify successfully");

        var isInvalid = totp.VerifyTotpCode(secret, "000000".Equals(code) ? "111111" : "000000");
        Assert.False(isInvalid, "Wrong code must be rejected");
    }

    [Fact]
    public void TotpService_GeneratesRecoveryCodes_AndVerifiesAndConsumes()
    {
        var totp = new TotpService();
        var rawCodes = totp.GenerateRecoveryCodes(8);
        Assert.Equal(8, rawCodes.Count);

        var items = rawCodes.Select(c => new RecoveryCodeItem(totp.HashRecoveryCode(c), false)).ToList();
        var account = new UserAccount
        {
            MfaRecoveryCodesJson = JsonSerializer.Serialize(items)
        };

        var firstCode = rawCodes[0];

        // Consume first code
        var consumed = totp.VerifyAndConsumeRecoveryCode(account, firstCode);
        Assert.True(consumed, "First code should be verified and consumed");

        // Attempt to consume first code again
        var consumedAgain = totp.VerifyAndConsumeRecoveryCode(account, firstCode);
        Assert.False(consumedAgain, "First code cannot be reused");

        // Second code should still be valid
        var secondConsumed = totp.VerifyAndConsumeRecoveryCode(account, rawCodes[1]);
        Assert.True(secondConsumed, "Second code should still be valid");
    }

    [Fact]
    public async Task Login_ClinicianWithoutMfa_RequiresMfaSetup()
    {
        var client = _factory.CreateClient();

        // Seeded clinician Dr. Patel
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "apatel@clinic.example",
            password = DbSeeder.DemoPassword
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("mfaSetupRequired").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("mfaToken").GetString()));
    }

    [Fact]
    public async Task Login_PatientWithoutMfa_SucceedsDirectly()
    {
        var client = _factory.CreateClient();

        // Seeded patient Dana Rivera
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "dana@example.com",
            password = DbSeeder.DemoPassword
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("accessToken", out var tokenProp));
        Assert.False(string.IsNullOrWhiteSpace(tokenProp.GetString()));
        Assert.True(body.TryGetProperty("refreshToken", out var refreshProp));
        Assert.False(string.IsNullOrWhiteSpace(refreshProp.GetString()));
    }

    [Fact]
    public async Task MfaEnroll_And_LoginWithTotp_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var testEmail = $"clinician.{Guid.NewGuid():N}@clinic.example";
        var account = new UserAccount
        {
            Email = testEmail,
            PasswordHash = hasher.HashPassword("ClinicianPass123!"),
            Role = UserRole.CLINICIAN,
            Person = new Person
            {
                FirstName = "Test",
                LastName = "Clinician",
                Email = testEmail,
                DateOfBirth = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Step 1: Login triggers MFA Setup Required
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "ClinicianPass123!"
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(loginBody.GetProperty("mfaSetupRequired").GetBoolean());
        var mfaToken = loginBody.GetProperty("mfaToken").GetString()!;

        // Step 2: Get TOTP Setup details
        var setupRes = await client.PostAsJsonAsync("/api/auth/mfa/setup", new { mfaToken });
        Assert.Equal(HttpStatusCode.OK, setupRes.StatusCode);
        var setupBody = await setupRes.Content.ReadFromJsonAsync<JsonElement>();
        var secret = setupBody.GetProperty("secret").GetString()!;
        Assert.Contains("otpauth://", setupBody.GetProperty("qrCodeUri").GetString());

        // Step 3: Enroll with valid TOTP code
        var totp = new TotpService();
        var code = totp.GenerateTotpCode(secret);

        var enrollRes = await client.PostAsJsonAsync("/api/auth/mfa/enroll", new
        {
            mfaToken,
            secret,
            code
        });
        Assert.Equal(HttpStatusCode.OK, enrollRes.StatusCode);
        var enrollBody = await enrollRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(enrollBody.GetProperty("success").GetBoolean());
        var recoveryCodes = enrollBody.GetProperty("recoveryCodes").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(8, recoveryCodes.Count);

        // Step 4: Login again now prompts for MFA code
        var loginRes2 = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "ClinicianPass123!"
        });
        var loginBody2 = await loginRes2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(loginBody2.GetProperty("mfaRequired").GetBoolean());
        var challengeToken = loginBody2.GetProperty("mfaToken").GetString()!;

        // Step 5: Verify MFA challenge with TOTP
        var currentCode = totp.GenerateTotpCode(secret);
        var verifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken = challengeToken,
            code = currentCode
        });
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        var verifyBody = await verifyRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verifyBody.TryGetProperty("accessToken", out var accessProp));
        Assert.False(string.IsNullOrWhiteSpace(accessProp.GetString()));

        // Verify Audit Log
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "MFA_VERIFIED" && a.ResourceId == account.Id.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task MfaVerify_WithRecoveryCode_SucceedsAndConsumesCode()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var totp = new TotpService();

        var secret = totp.GenerateSecret();
        var rawCodes = totp.GenerateRecoveryCodes(8);
        var recoveryItems = rawCodes.Select(c => new RecoveryCodeItem(totp.HashRecoveryCode(c), false)).ToList();

        var testEmail = $"recovery.{Guid.NewGuid():N}@clinic.example";
        var account = new UserAccount
        {
            Email = testEmail,
            PasswordHash = hasher.HashPassword("RecoveryPass123!"),
            Role = UserRole.CLINICIAN,
            MfaEnabled = true,
            MfaSecret = secret,
            MfaRecoveryCodesJson = JsonSerializer.Serialize(recoveryItems),
            Person = new Person
            {
                FirstName = "Recovery",
                LastName = "User",
                Email = testEmail,
                DateOfBirth = new DateTime(1980, 5, 5, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Login prompts for MFA challenge
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "RecoveryPass123!"
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var mfaToken = loginBody.GetProperty("mfaToken").GetString()!;

        // Use first recovery code
        var usedCode = rawCodes[0];
        var verifyRes = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken,
            code = usedCode
        });
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);

        // Attempting to use the same recovery code again on next login must fail
        var loginRes2 = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "RecoveryPass123!"
        });
        var mfaToken2 = (await loginRes2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaToken").GetString()!;

        var verifyRes2 = await client.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            mfaToken = mfaToken2,
            code = usedCode
        });
        Assert.Equal(HttpStatusCode.Unauthorized, verifyRes2.StatusCode);
    }

    [Fact]
    public async Task Session_RevokedOnLogout()
    {
        var client = _factory.CreateClient();

        // Login as Dana
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "dana@example.com",
            password = DbSeeder.DemoPassword
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;

        // Authenticated request works
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Logout
        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var logoutRes = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);

        // Subsequent request using old session is rejected
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Unauthorized, res2.StatusCode);
    }

    [Fact]
    public async Task Session_RevokedOnPasswordChange()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var testEmail = $"pwdchange.{Guid.NewGuid():N}@patient.example";
        var account = new UserAccount
        {
            Email = testEmail,
            PasswordHash = hasher.HashPassword("InitialPassword123!"),
            Role = UserRole.PATIENT,
            Person = new Person
            {
                FirstName = "Password",
                LastName = "Change",
                Email = testEmail,
                DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Login
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "InitialPassword123!"
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;

        // Change password
        using var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        changeReq.Content = JsonContent.Create(new
        {
            currentPassword = "InitialPassword123!",
            newPassword = "UpdatedPassword456!"
        });

        var changeRes = await client.SendAsync(changeReq);
        Assert.Equal(HttpStatusCode.OK, changeRes.StatusCode);

        // Old session token is now rejected
        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meRes = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.Unauthorized, meRes.StatusCode);

        // New login with new password works
        var newLoginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "UpdatedPassword456!"
        });
        Assert.Equal(HttpStatusCode.OK, newLoginRes.StatusCode);
    }

    [Fact]
    public async Task Session_IdleTimeout_RejectsExpiredSession()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();

        var testEmail = $"idletimeout.{Guid.NewGuid():N}@patient.example";
        var account = new UserAccount
        {
            Email = testEmail,
            PasswordHash = hasher.HashPassword("TestPass123!"),
            Role = UserRole.PATIENT,
            Person = new Person
            {
                FirstName = "Idle",
                LastName = "User",
                Email = testEmail,
                DateOfBirth = new DateTime(1995, 3, 3, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Login
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "TestPass123!"
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;

        // Artificially age the session's LastActiveAt beyond IdleTimeoutMinutes
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var claims = tokenService.ValidateAccessToken(accessToken)!;

        var session = await db.UserSessions.FirstAsync(s => s.Sid == claims.Sid);
        session.LastActiveAt = DateTime.UtcNow.AddMinutes(-(sessionService.IdleTimeoutMinutes + 5));
        await db.SaveChangesAsync();

        // Request should now be rejected due to idle timeout
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("inactivity", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_RefreshToken_SuccessfullyRefreshesAccessToken()
    {
        var client = _factory.CreateClient();

        // Login as Dana
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "dana@example.com",
            password = DbSeeder.DemoPassword
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var initialRefreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        // Refresh token
        var refreshRes = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = initialRefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var refreshBody = await refreshRes.Content.ReadFromJsonAsync<JsonElement>();

        var newAccessToken = refreshBody.GetProperty("accessToken").GetString();
        var newRefreshToken = refreshBody.GetProperty("refreshToken").GetString();

        Assert.False(string.IsNullOrWhiteSpace(newAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(newRefreshToken));
        Assert.NotEqual(initialRefreshToken, newRefreshToken); // Rotated refresh token

        // New access token works
        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newAccessToken);
        var meRes = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
    }

    [Fact]
    public async Task MfaDisable_ClinicianForbidden_PatientAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        // 1. Clinician attempting to disable MFA
        var clinicianEmail = $"clinician.mfa.{Guid.NewGuid():N}@clinic.example";
        var clinician = new UserAccount
        {
            Email = clinicianEmail,
            PasswordHash = hasher.HashPassword("ClinicianPass123!"),
            Role = UserRole.CLINICIAN,
            MfaEnabled = true,
            Person = new Person
            {
                FirstName = "Doctor",
                LastName = "Mfa",
                Email = clinicianEmail,
                DateOfBirth = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(clinician);

        // 2. Patient attempting to disable MFA
        var patientEmail = $"patient.mfa.{Guid.NewGuid():N}@patient.example";
        var patient = new UserAccount
        {
            Email = patientEmail,
            PasswordHash = hasher.HashPassword("PatientPass123!"),
            Role = UserRole.PATIENT,
            MfaEnabled = true,
            Person = new Person
            {
                FirstName = "Patient",
                LastName = "Mfa",
                Email = patientEmail,
                DateOfBirth = new DateTime(1992, 2, 2, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(patient);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Clinician disable request -> 403 Forbidden
        var clinicianToken = tokenService.GenerateAccessToken(clinician, clinician.PersonId);
        using var clinicianReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/disable");
        clinicianReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clinicianToken);
        clinicianReq.Content = JsonContent.Create(new { password = "ClinicianPass123!" });
        var clinicianRes = await client.SendAsync(clinicianReq);
        Assert.Equal(HttpStatusCode.Forbidden, clinicianRes.StatusCode);

        // Patient disable request -> 200 OK
        var patientToken = tokenService.GenerateAccessToken(patient, patient.PersonId);
        using var patientReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/disable");
        patientReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patientToken);
        patientReq.Content = JsonContent.Create(new { password = "PatientPass123!" });
        var patientRes = await client.SendAsync(patientReq);
        Assert.Equal(HttpStatusCode.OK, patientRes.StatusCode);

        // Verify patient MFA is now false in DB
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updatedPatient = await verifyDb.UserAccounts.FindAsync(patient.Id);
        Assert.NotNull(updatedPatient);
        Assert.False(updatedPatient.MfaEnabled);
    }

    [Fact]
    public async Task Session_RevokedOnAccountDeactivation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var testEmail = $"deactivate.{Guid.NewGuid():N}@patient.example";
        var account = new UserAccount
        {
            Email = testEmail,
            PasswordHash = hasher.HashPassword("TestPass123!"),
            Role = UserRole.PATIENT,
            IsActive = true,
            Person = new Person
            {
                FirstName = "Deactivate",
                LastName = "Test",
                Email = testEmail,
                DateOfBirth = new DateTime(1991, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Login
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = testEmail,
            password = "TestPass123!"
        });
        var loginBody = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;

        // Authenticated request works initially
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Deactivate account in database
        account.IsActive = false;
        await db.SaveChangesAsync();

        // Subsequent request is rejected with 401 Unauthorized
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Unauthorized, res2.StatusCode);
    }
}
