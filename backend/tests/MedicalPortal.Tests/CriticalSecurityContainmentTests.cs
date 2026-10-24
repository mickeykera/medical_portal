using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace MedicalPortal.Tests;

/// <summary>
/// Verifies negative abuse and containment tests for SEC-01, SEC-03, SEC-04, SEC-13, SEC-14, SEC-18, and SAFE-01.
/// Ensures unauthorized access, claim tampering, lazy sessions, test stubs in production,
/// cleartext external network calls, and demo clinical engines are rejected fail-fast.
/// </summary>
public class CriticalSecurityContainmentTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CriticalSecurityContainmentTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // 1. SEC-01 & SEC-14: LAZY SESSION CREATION DELETION & UNKNOWN SID REJECTION
    // =========================================================================

    [Fact]
    public async Task SessionService_UnknownSid_RejectsWithoutLazyCreation()
    {
        using var scope = _factory.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var unknownSid = "forged-or-untracked-session-id-" + Guid.NewGuid().ToString("N");
        var user = await db.UserAccounts.FirstAsync(u => u.Email == "dana@example.com");

        var result = await sessionService.ValidateSessionAsync(unknownSid, user.Id);

        Assert.False(result.IsValid);
        Assert.Contains("not found", result.FailureReason, StringComparison.OrdinalIgnoreCase);

        // Ensure no session was lazily created in the database
        var created = await db.UserSessions.AnyAsync(s => s.Sid == unknownSid);
        Assert.False(created, "Lazy session creation must be strictly deleted.");
    }

    [Fact]
    public async Task AuthFilter_UnknownSid_ReturnsUnauthorized_DoesNotCreateLazySession()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var user = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");
        var unknownSid = "unregistered-sid-" + Guid.NewGuid().ToString("N");

        // Attacker creates a validly signed token with an unmanaged sid
        var forgedToken = tokens.GenerateAccessToken(user, user.PersonId, unknownSid);

        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        // Verify session was not lazily added to database
        var sessionExists = await db.UserSessions.AnyAsync(s => s.Sid == unknownSid);
        Assert.False(sessionExists);
    }

    // =========================================================================
    // 2. SEC-01 & SEC-14: FORGED / ALTERED CLAIMS & PRIVILEGE ESCALATION
    // =========================================================================

    [Fact]
    public async Task AuthFilter_ForgedToken_AlteredRoleClaim_RejectedAndRevokesSession()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Create genuine patient user with genuine session
        var email = $"patient.attacker.{Guid.NewGuid():N}@patient.example";
        var person = new Person { FirstName = "Eve", LastName = "Attacker", Email = email, DateOfBirth = DateTime.UtcNow.AddYears(-25) };
        db.Persons.Add(person);
        var patientAccount = new UserAccount { Person = person, Email = email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.PATIENT, IsActive = true };
        db.UserAccounts.Add(patientAccount);
        db.SaveChanges();

        var sid = Guid.NewGuid().ToString("N");
        var session = new UserSession
        {
            UserAccountId = patientAccount.Id,
            Sid = sid,
            RefreshTokenHash = "test-hash",
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        db.UserSessions.Add(session);
        db.SaveChanges();

        // Attacker creates a token pointing to their genuine session & userAccountId,
        // but tampers the role claim to 'ADMIN'
        var secret = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Jwt:Secret"]
                     ?? "ThisIsACISecretKeyWithAtLeast32BytesLength1234567!";
        var claims = new[]
        {
            new Claim("userAccountId", patientAccount.Id.ToString()),
            new Claim("personId", person.Id.ToString()),
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("role", "ADMIN"), // Tampered role claim!
            new Claim("sid", sid),
            new Claim("authVersion", "1"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(15),
            Issuer = "medical-portal",
            Audience = "medical-portal-api",
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        var tamperedToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

        var res = await client.SendAsync(req);

        // Request must be rejected
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        // Session must be marked revoked due to role tampering
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var updatedSession = await verifyDb.UserSessions.FirstAsync(s => s.Sid == sid);
        Assert.NotNull(updatedSession.RevokedAt);
        Assert.Equal("role_mismatch", updatedSession.RevokedReason);
    }

    [Fact]
    public async Task AuthFilter_ForgedToken_InvalidSignature_ReturnsUnauthorized()
    {
        var attackerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("an-attacker-crafted-secret-key-32-bytes-long!"));
        var claims = new[]
        {
            new Claim("userAccountId", Guid.NewGuid().ToString()),
            new Claim("personId", Guid.NewGuid().ToString()),
            new Claim("role", "ADMIN"),
            new Claim("sid", "any-sid")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(15),
            Issuer = "medical-portal",
            Audience = "medical-portal-api",
            SigningCredentials = new SigningCredentials(attackerKey, SecurityAlgorithms.HmacSha256Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        var forgedToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task AuthFilter_RoleChange_IncrementsAuthVersion_RevokesSessionImmediately()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var email = $"staff.rolechange.{Guid.NewGuid():N}@clinic.example";
        var person = new Person { FirstName = "Robin", LastName = "Staff", Email = email, DateOfBirth = DateTime.UtcNow.AddYears(-30) };
        db.Persons.Add(person);
        var staffAccount = new UserAccount { Person = person, Email = email, PasswordHash = hasher.HashPassword("Pass123!"), Role = UserRole.CLINICIAN, IsActive = true, AuthorizationVersion = 1 };
        db.UserAccounts.Add(staffAccount);
        db.SaveChanges();

        // Generate token with AuthVersion = 1
        var originalToken = db.CreateAuthenticatedSession(tokens, staffAccount, person.Id);

        var client = _factory.CreateClient();

        // 1. Request succeeds while AuthVersion is current
        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", originalToken);
        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Admin alters role / increments AuthorizationVersion
        using (var editScope = _factory.Services.CreateScope())
        {
            var editDb = editScope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var sessionService = editScope.ServiceProvider.GetRequiredService<ISessionService>();
            var acct = await editDb.UserAccounts.FirstAsync(u => u.Id == staffAccount.Id);
            acct.Role = UserRole.PATIENT;
            acct.AuthorizationVersion++;
            await editDb.SaveChangesAsync();
            await sessionService.RevokeAllSessionsForUserAsync(acct.Id, "role_changed_to_PATIENT");
        }

        // 3. Request with old token (AuthVersion = 1) is immediately rejected
        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", originalToken);
        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Unauthorized, res2.StatusCode);
    }

    // =========================================================================
    // 3. SEC-14: UNIFIED SIGNALR SESSION VALIDATION (REVOKED / UNKNOWN REJECTION)
    // =========================================================================

    [Fact]
    public async Task SignalR_PortalHub_RejectsConnection_WhenSessionIsUnknown()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var user = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");

        // Token with unknown sid
        var unknownSidToken = tokens.GenerateAccessToken(user, user.PersonId, "unknown-signalr-sid-" + Guid.NewGuid().ToString("N"));

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/portal", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(unknownSidToken);
            })
            .Build();

        bool rejected = false;
        try
        {
            await connection.StartAsync();
            await connection.InvokeAsync("JoinPatientRoom", Guid.NewGuid().ToString());
        }
        catch
        {
            rejected = true;
        }
        finally
        {
            await connection.DisposeAsync();
        }

        Assert.True(rejected, "SignalR connection must be aborted when session ID is unknown.");
    }

    [Fact]
    public async Task SignalR_PortalHub_RejectsConnection_WhenSessionIsRevoked()
    {
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var user = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");

        var token = db.CreateAuthenticatedSession(tokens, user, user.PersonId);

        // Parse sid from token and revoke it in DB
        var claims = tokens.ValidateAccessToken(token);
        Assert.NotNull(claims);

        var session = await db.UserSessions.FirstAsync(s => s.Sid == claims.Sid);
        session.RevokedAt = DateTime.UtcNow;
        session.RevokedReason = "manual_admin_revocation";
        await db.SaveChangesAsync();

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/portal", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        bool rejected = false;
        try
        {
            await connection.StartAsync();
            await connection.InvokeAsync("JoinPatientRoom", Guid.NewGuid().ToString());
        }
        catch
        {
            rejected = true;
        }
        finally
        {
            await connection.DisposeAsync();
        }

        Assert.True(rejected, "SignalR connection must be rejected when session has been revoked.");
    }

    // =========================================================================
    // 4. SEC-03, SEC-04, SEC-13, SAFE-01: PRODUCTION STARTUP FAIL-FAST GUARDS
    // =========================================================================

    [Fact]
    public void ProductionStartup_FailsFast_WhenFakePaymentGatewayEnabled()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("Stripe:UseFake", "true"); // FORBIDDEN IN PROD
                builder.UseSetting("Hms:UseFake", "false");
                builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Fake payment gateway is strictly prohibited in production", ex.Message);
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenStripeSecretKeyIsMissing()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("Stripe:UseFake", "false");
                builder.UseSetting("Stripe:SecretKey", ""); // MISSING IN PROD
                builder.UseSetting("Hms:UseFake", "false");
                builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Valid production Stripe secret key is required", ex.Message);
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenFakeHmsClientEnabled()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("Stripe:UseFake", "false");
                builder.UseSetting("Stripe:SecretKey", "sk_live_real_production_secret_key_123");
                builder.UseSetting("Hms:UseFake", "true"); // FORBIDDEN IN PROD
                builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Fake HMS client is strictly prohibited in production", ex.Message);
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenHmsWebhookSecretIsPlaceholder()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("Stripe:UseFake", "false");
                builder.UseSetting("Stripe:SecretKey", "sk_live_real_production_secret_key_123");
                builder.UseSetting("Hms:UseFake", "false");
                builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                builder.UseSetting("Hms:WebhookSecret", "demo-hms-webhook-secret"); // PLACEHOLDER IN PROD
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Valid production HMS webhook secret is required", ex.Message);
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenDemoClinicalKnowledgeBaseConfigured()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("Stripe:UseFake", "false");
                builder.UseSetting("Stripe:SecretKey", "sk_live_real_production_secret_key_123");
                builder.UseSetting("Hms:UseFake", "false");
                builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
                builder.UseSetting("Clinical:UseDemoKnowledgeBase", "true"); // FORBIDDEN IN PROD
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Demo clinical knowledge bases are strictly prohibited in production", ex.Message);
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenExternalEndpointIsPlainHttp()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "a-strong-production-ready-jwt-secret-key-at-least-32-bytes!");
                builder.UseSetting("Stripe:UseFake", "false");
                builder.UseSetting("Stripe:SecretKey", "sk_live_real_production_secret_key_123");
                builder.UseSetting("Hms:UseFake", "false");
                builder.UseSetting("Hms:BaseUrl", "http://external-hospital-hms.com"); // PLAINTEXT HTTP
                builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("must use HTTPS", ex.Message);
    }

    [Fact]
    public void ProductionStartup_FailsFast_WhenJwtSecretIsClusterDefault()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=postgres;Password=postgres;");
                builder.UseSetting("Jwt:Secret", "ThisIsAClusterSecretKeyForMultiInstanceDeployments12345!"); // STATIC CLUSTER KEY IN PROD
                builder.UseSetting("Stripe:UseFake", "false");
                builder.UseSetting("Stripe:SecretKey", "sk_live_real_production_secret_key_123");
                builder.UseSetting("Hms:UseFake", "false");
                builder.UseSetting("Hms:BaseUrl", "https://hms.internal");
                builder.UseSetting("Hms:WebhookSecret", "prod-webhook-secret-value-12345");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Default or placeholder JWT secret is strictly prohibited in production", ex.Message);
    }

    // =========================================================================
    // 5. SEC-18: DROPPED PLAINTEXT IP-API CALL & SEC-03: HTTPS / HSTS ENFORCEMENT
    // =========================================================================

    [Fact]
    public async Task PublicIpService_ResolvesSafelyWithoutPlainHttpIpApi()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PublicIpService(mockFactory.Object, cache, NullLogger<PublicIpService>.Instance);

        // Resolving an external public IP should never make plain HTTP requests
        var location = await service.ResolveIpLocationAsync("198.51.100.25");

        Assert.NotNull(location);
        Assert.Equal("default-fallback", location.Source);
        Assert.Equal("ET", location.CountryCode);
    }

    [Fact]
    public async Task SecurityHeaders_IncludesHsts_WhenForwardedProtoIsHttps()
    {
        var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/health");
        req.Headers.Add("X-Forwarded-Proto", "https");

        var res = await client.SendAsync(req);

        Assert.True(res.Headers.Contains("Strict-Transport-Security"), "HSTS header must be emitted for HTTPS / forwarded HTTPS requests.");
        var hsts = res.Headers.GetValues("Strict-Transport-Security").FirstOrDefault();
        Assert.Contains("max-age=31536000", hsts);
        Assert.Contains("includeSubDomains", hsts);
    }
}
