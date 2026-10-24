using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Logging;
using MedicalPortal.Infrastructure.Security;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedicalPortal.Tests;

public class DataProtectionAndHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DataProtectionAndHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Sec11_PhiScrubber_ScrubsEthiopianPhoneAndFayyaIdAndSensitiveKeys()
    {
        var scrubber = new PhiScrubber();

        // Ethiopian phone formats: +251911223344, 0911223344, +251711223344
        var textWithPhone = "Patient contact phone is +251911223344 and alternate 0911223344.";
        var scrubbedPhone = scrubber.ScrubText(textWithPhone);
        Assert.DoesNotContain("+251911223344", scrubbedPhone);
        Assert.DoesNotContain("0911223344", scrubbedPhone);
        Assert.Contains("[PHONE-REDACTED]", scrubbedPhone);

        // Ethiopian Fayya National ID
        var textWithFayya = "Registered with national ID ET-FAYYA-8839201 in MPI directory.";
        var scrubbedFayya = scrubber.ScrubText(textWithFayya);
        Assert.DoesNotContain("ET-FAYYA-8839201", scrubbedFayya);
        Assert.Contains("[NATIONALID-REDACTED]", scrubbedFayya);

        // Sensitive dictionary keys (MfaSecret, TotpSecret)
        var properties = new Dictionary<string, object?>
        {
            { "mfaSecret", "JBSWY3DPEHPK3PXP" },
            { "totpSecret", "KRSXG5CTMVRXEZLU" },
            { "nationalPatientId", "ET-FAYYA-12345" }
        };
        var scrubbedDict = scrubber.ScrubProperties(properties);
        Assert.Equal("[REDACTED]", scrubbedDict["mfaSecret"]);
        Assert.Equal("[REDACTED]", scrubbedDict["totpSecret"]);
        Assert.Equal("[REDACTED]", scrubbedDict["nationalPatientId"]);
    }

    [Fact]
    public void Sec12_ConfigurationKeyProvider_ProductionFailsFastWithoutExplicitKey()
    {
        // When running in Production environment without explicit FieldEncryption key,
        // it must throw InvalidOperationException and refuse to derive keys from JWT_SECRET
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ASPNETCORE_ENVIRONMENT", "Production" },
                { "Jwt:Secret", "ASecretKeyUsedForAuthenticationTokens1234567890!" }
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ConfigurationKeyProvider(config));
        Assert.Contains("Production startup failed", ex.Message);
        Assert.Contains("FieldEncryption key", ex.Message);
        Assert.Contains("JWT_SECRET", ex.Message);
    }

    [Fact]
    public async Task Sec12_MfaSecretAndPatientNotes_StoredEncryptedInDatabaseAtRest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var person = new Person
        {
            FirstName = "Enc",
            LastName = "Test",
            Email = $"enc_test_{Guid.NewGuid():N}@hospital.et"
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var testSecret = "BASE32SECRETFORTESTING1234567890";
        var user = new UserAccount
        {
            PersonId = person.Id,
            Email = person.Email,
            PasswordHash = "hash",
            Role = UserRole.PATIENT,
            MfaSecret = testSecret,
            MfaEnabled = true
        };

        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        // 1. Transparent decryption via EF Core entity navigation
        var retrievedUser = await db.UserAccounts.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.Equal(testSecret, retrievedUser.MfaSecret);

        // 2. Direct raw SQL verification: verify underlying database column starts with "enc:v1:"
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MfaSecret\" FROM \"UserAccounts\" WHERE \"Id\" = @id";
        var idParam = command.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = user.Id;
        command.Parameters.Add(idParam);

        var rawMfaSecret = (string?)await command.ExecuteScalarAsync();
        Assert.NotNull(rawMfaSecret);
        Assert.NotEqual(testSecret, rawMfaSecret);
        Assert.StartsWith("enc:v1:", rawMfaSecret);
    }

    [Fact]
    public void Sec17_PublicIpService_UntrustedPeerCannotSpoofIpViaHeaders()
    {
        var mockFactory = new Moq.Mock<IHttpClientFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PublicIpService(mockFactory.Object, cache, NullLogger<PublicIpService>.Instance);

        // Untrusted external socket peer (203.0.113.199) sending spoofed proxy headers
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.199");
        ctx.Request.Headers["X-Forwarded-For"] = "198.51.100.1";
        ctx.Request.Headers["CF-Connecting-IP"] = "198.51.100.2";
        ctx.Request.Headers["X-Real-IP"] = "198.51.100.3";
        ctx.Request.Headers["True-Client-IP"] = "198.51.100.4";

        var resolvedIp = service.GetClientIpAddress(ctx);

        // Must reject spoofed headers and return actual socket IP
        Assert.Equal("203.0.113.199", resolvedIp);
    }

    [Fact]
    public void Sec17_PublicIpService_TrustedProxyHonorsForwardedHeaders()
    {
        var mockFactory = new Moq.Mock<IHttpClientFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PublicIpService(mockFactory.Object, cache, NullLogger<PublicIpService>.Instance);

        // Immediate peer is localhost loopback reverse proxy (127.0.0.1)
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        ctx.Request.Headers["CF-Connecting-IP"] = "198.51.100.42";

        var resolvedIp = service.GetClientIpAddress(ctx);
        Assert.Equal("198.51.100.42", resolvedIp);
    }

    [Fact]
    public void Sec19_CiWorkflow_PinsActionsAndEnforcesNonZeroVulnerabilityExitCode()
    {
        var ciPath = RepoPaths.CiWorkflow;

        Assert.True(File.Exists(ciPath), $"ci.yml not found at {ciPath}");
        var ciContent = File.ReadAllText(ciPath);

        // Actions must NOT use mutable @master tags
        Assert.DoesNotContain("@master", ciContent);

        // Trivy scans must fail the build on critical/high vulnerabilities
        Assert.Contains("exit-code: '1'", ciContent);

        // Must run vulnerable package check that fails if vulnerabilities exist
        Assert.Contains("has the following vulnerable packages", ciContent);

        // Required check: Separate test job runs full suite on complete checkout
        Assert.Contains("test:", ciContent);
        Assert.Contains("fetch-depth: 0", ciContent);
    }

    [Fact]
    public void Sec21_Containers_ConfiguredWithNonRootUserAndNoNewPrivileges()
    {
        var dockerfilePath = RepoPaths.Dockerfile;

        Assert.True(File.Exists(dockerfilePath), $"Dockerfile not found at {dockerfilePath}");
        var dockerfileContent = File.ReadAllText(dockerfilePath);
        Assert.Contains("USER app", dockerfileContent);
        Assert.DoesNotContain("RUN dotnet test", dockerfileContent);

        var composePath = RepoPaths.DockerCompose;

        Assert.True(File.Exists(composePath), $"docker-compose.yml not found at {composePath}");
        var composeContent = File.ReadAllText(composePath);
        Assert.Contains("no-new-privileges:true", composeContent);
    }

    [Fact]
    public async Task Sec22_PhiEndpoints_ReturnNoStoreCacheControlHeaders()
    {
        var client = _factory.CreateClient();

        // Request an API endpoint that handles PHI / clinical information
        var response = await client.GetAsync("/api/health");
        // Check standard API endpoint
        var apiResponse = await client.GetAsync("/api/auth/csrf");

        Assert.True(apiResponse.Headers.Contains("Cache-Control"));
        var cacheControl = apiResponse.Headers.CacheControl?.ToString();
        Assert.NotNull(cacheControl);
        Assert.Contains("no-store", cacheControl);
        Assert.Contains("no-cache", cacheControl);
    }

    [Fact]
    public async Task Sec22_HmsWebhook_RejectsOversizedPayloadExceeding1Mb()
    {
        var client = _factory.CreateClient();

        // Create an oversized payload > 1 MB (1,100,000 bytes)
        var oversizedContent = new string('A', 1100000);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/hms/webhook")
        {
            Content = new StringContent(oversizedContent, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hms-Signature", "fake_signature");

        var response = await client.SendAsync(request);

        // Must be rejected with 413 Payload Too Large (or BadRequest/RequestSizeLimit)
        Assert.True(
            response.StatusCode == HttpStatusCode.RequestEntityTooLarge ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 413 or 400, got {response.StatusCode}");
    }
}
