using System.Net;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class ConfigurationHardeningTests
{
    [Fact]
    public void TokenService_ThrowsException_WhenSecretIsMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new TokenService(config));
        Assert.Contains("JWT secret", ex.Message);
    }

    [Fact]
    public void TokenService_ThrowsException_WhenSecretIsEmptyOrWhitespace()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "   "
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new TokenService(config));
        Assert.Contains("JWT secret", ex.Message);
    }

    [Theory]
    [InlineData("short-key")]
    [InlineData("1234567890123456789012345678901")] // 31 characters
    public void TokenService_ThrowsException_WhenSecretIsShorterThan32Bytes(string shortSecret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SECRET"] = shortSecret
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new TokenService(config));
        Assert.Contains("32 bytes", ex.Message);
        // Verify secret value is never printed in the exception message
        Assert.DoesNotContain(shortSecret, ex.Message);
    }

    [Fact]
    public void TokenService_Succeeds_WhenSecretIsAtLeast32Bytes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "this-is-a-valid-test-secret-that-is-at-least-32-bytes-long!"
            })
            .Build();

        var service = new TokenService(config);
        Assert.NotNull(service);

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Role = UserRole.PATIENT
        };

        var token = service.GenerateAccessToken(account, Guid.NewGuid());
        Assert.False(string.IsNullOrWhiteSpace(token));

        var claims = service.ValidateAccessToken(token);
        Assert.NotNull(claims);
        Assert.Equal(account.Id, claims.UserAccountId);
    }

    [Fact]
    public void WebHost_StartupFails_WhenJwtSecretMissing()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Secret", "");
                builder.UseSetting("JWT_SECRET", "");
            });

        // Attempting to resolve services or create client should throw InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var scope = factory.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ITokenService>();
        });

        Assert.Contains("JWT secret", ex.Message);
    }

    [Fact]
    public void WebHost_StartupFails_WhenJwtSecretShorterThan32Bytes()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Secret", "too-short-key");
                builder.UseSetting("JWT_SECRET", "");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var scope = factory.Services.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ITokenService>();
        });

        Assert.Contains("32 bytes", ex.Message);
        Assert.DoesNotContain("too-short-key", ex.Message);
    }

    [Fact]
    public void WebHost_InProduction_FailsStartup_WhenPostgreSqlIsNotConfigured()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("DATABASE_URL", "");
                builder.UseSetting("POSTGRES_CONNECTION_STRING", "");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Data Source=medical_portal.db");
                builder.UseSetting("Jwt:Secret", "a-sufficiently-long-production-ready-jwt-key-32-bytes!");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Production environment requires PostgreSQL", ex.Message);
        Assert.DoesNotContain("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebHost_InProduction_FailsStartup_WhenNoConnectionStringExistsAtAll()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("DATABASE_URL", "");
                builder.UseSetting("POSTGRES_CONNECTION_STRING", "");
                builder.UseSetting("ConnectionStrings:DefaultConnection", "");
                builder.UseSetting("Jwt:Secret", "a-sufficiently-long-production-ready-jwt-key-32-bytes!");
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Server;
        });

        Assert.Contains("Production environment requires PostgreSQL", ex.Message);
    }
}
