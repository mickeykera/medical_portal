using System.Net;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MedicalPortal.Tests;

public class PostgresAndIpTests
{
    [Fact]
    public void PostgresConnectionHelper_IdentifiesPostgresCorrectly()
    {
        Assert.True(PostgresConnectionHelper.IsPostgres("Host=localhost;Port=5432;Database=medical_portal;Username=postgres;Password=postgres;"));
        Assert.True(PostgresConnectionHelper.IsPostgres("postgres://postgres:postgres@localhost:5432/medical_portal"));
        Assert.True(PostgresConnectionHelper.IsPostgres("postgresql://postgres:secret@db.render.com:5432/medical_portal?sslmode=require"));
        Assert.True(PostgresConnectionHelper.IsPostgres("Server=localhost;Port=5432;Database=medical_portal;User Id=postgres;Password=postgres;"));

        Assert.False(PostgresConnectionHelper.IsPostgres("Data Source=medical_portal.db"));
        Assert.False(PostgresConnectionHelper.IsPostgres(null));
        Assert.False(PostgresConnectionHelper.IsPostgres(""));
    }

    [Fact]
    public void PostgresConnectionHelper_ConvertsUriToNpgsqlConnectionString()
    {
        var uri = "postgresql://myuser:mypassword@db.host.cloud:5432/portal_db?sslmode=require&pooling=false";
        var connStr = PostgresConnectionHelper.ToNpgsqlConnectionString(uri);

        Assert.Contains("Host=db.host.cloud", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Port=5432", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Database=portal_db", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Username=myuser", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=mypassword", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SSL Mode=Require", connStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pooling=false", connStr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgresConnectionHelper_HandlesUrlEncodedCredentials()
    {
        var uri = "postgres://user%40work:p%40ss%3Aword@pg.example.com/mydb";
        var connStr = PostgresConnectionHelper.ToNpgsqlConnectionString(uri);

        Assert.Contains("Host=pg.example.com", connStr);
        Assert.Contains("Port=5432", connStr);
        Assert.Contains("Database=mydb", connStr);
        Assert.Contains("Username=user@work", connStr);
        Assert.Contains("Password=p@ss:word", connStr);
    }

    [Fact]
    public void PublicIpService_ClassifiesPrivateAndPublicIpsCorrectly()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PublicIpService(mockFactory.Object, cache, NullLogger<PublicIpService>.Instance);

        // Loopback & Private
        Assert.True(service.IsPrivateOrLoopback("127.0.0.1"));
        Assert.True(service.IsPrivateOrLoopback("::1"));
        Assert.True(service.IsPrivateOrLoopback("10.0.0.1"));
        Assert.True(service.IsPrivateOrLoopback("10.254.254.1"));
        Assert.True(service.IsPrivateOrLoopback("172.16.0.1"));
        Assert.True(service.IsPrivateOrLoopback("172.31.255.255"));
        Assert.True(service.IsPrivateOrLoopback("192.168.1.100"));
        Assert.True(service.IsPrivateOrLoopback("169.254.1.1"));
        Assert.True(service.IsPrivateOrLoopback(""));
        Assert.True(service.IsPrivateOrLoopback("invalid-ip"));

        // Public IPs
        Assert.False(service.IsPrivateOrLoopback("8.8.8.8"));
        Assert.False(service.IsPrivateOrLoopback("1.1.1.1"));
        Assert.False(service.IsPrivateOrLoopback("196.188.252.99"));
        Assert.False(service.IsPrivateOrLoopback("142.250.190.46"));
    }

    [Fact]
    public void PublicIpService_ExtractsClientIpFromHeadersWithPriority()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PublicIpService(mockFactory.Object, cache, NullLogger<PublicIpService>.Instance);

        // 1. CF-Connecting-IP takes highest precedence
        var ctx1 = new DefaultHttpContext();
        ctx1.Request.Headers["CF-Connecting-IP"] = "198.51.100.1";
        ctx1.Request.Headers["X-Forwarded-For"] = "203.0.113.2";
        ctx1.Request.Headers["X-Real-IP"] = "192.0.2.3";
        ctx1.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        Assert.Equal("198.51.100.1", service.GetClientIpAddress(ctx1));

        // 2. X-Real-IP when CF-Connecting-IP is absent
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Headers["X-Real-IP"] = "192.0.2.3";
        ctx2.Request.Headers["X-Forwarded-For"] = "203.0.113.2, 10.0.0.1";
        Assert.Equal("192.0.2.3", service.GetClientIpAddress(ctx2));

        // 3. X-Forwarded-For (first entry)
        var ctx3 = new DefaultHttpContext();
        ctx3.Request.Headers["X-Forwarded-For"] = "203.0.113.5:8080, 10.0.0.1";
        Assert.Equal("203.0.113.5", service.GetClientIpAddress(ctx3));

        // 4. Fallback to RemoteIpAddress
        var ctx4 = new DefaultHttpContext();
        ctx4.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        Assert.Equal("192.168.1.50", service.GetClientIpAddress(ctx4));
    }

    [Fact]
    public async Task PublicIpService_ResolvesFallbackForLocalIp()
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PublicIpService(mockFactory.Object, cache, NullLogger<PublicIpService>.Instance);

        var loc = await service.ResolveIpLocationAsync("127.0.0.1");
        Assert.NotNull(loc);
        Assert.Equal(9.0054, loc.Latitude!.Value);
        Assert.Equal(38.7636, loc.Longitude!.Value);
        Assert.Equal("Addis Ababa", loc.City);
    }
}
