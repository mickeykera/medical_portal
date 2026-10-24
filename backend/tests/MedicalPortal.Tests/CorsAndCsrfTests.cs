using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class CorsAndCsrfTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorsAndCsrfTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private string GetDanaToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var danaAccount = db.UserAccounts.Include(u => u.Person).First(u => u.Email == "dana@example.com");
        return db.CreateAuthenticatedSession(tokenService, danaAccount, danaAccount.PersonId);
    }

    [Fact]
    public async Task Cors_DisallowedOrigin_DoesNotReceiveAllowOriginHeader()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/profile");
        request.Headers.Add("Origin", "https://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "PATCH");

        var response = await client.SendAsync(request);

        // Disallowed origin MUST NOT receive Access-Control-Allow-Origin header
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"),
            "Disallowed origin should not receive Access-Control-Allow-Origin header.");
    }

    [Fact]
    public async Task Csrf_CookieAuth_StateChangingRequest_WithoutCustomHeader_IsRejectedWith403()
    {
        var token = GetDanaToken();
        var client = _factory.CreateClient();

        // Simulate cookie authentication without custom header
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/profile");
        request.Headers.Add("Cookie", $"mp_session={token}");
        request.Content = new StringContent("{\"phone\":\"555-9999\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CSRF", body);
    }

    [Fact]
    public async Task Csrf_CookieAuth_StateChangingRequest_WithCustomHeader_Succeeds()
    {
        var token = GetDanaToken();
        var client = _factory.CreateClient();

        // Cookie auth WITH X-CSRF-Token custom header
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/profile");
        request.Headers.Add("Cookie", $"mp_session={token}");
        request.Headers.Add("X-CSRF-Token", "1");
        request.Content = new StringContent("{\"phone\":\"555-8888\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Csrf_CookieAuth_StateChangingRequest_WithXRequestedWith_Succeeds()
    {
        var token = GetDanaToken();
        var client = _factory.CreateClient();

        // Cookie auth WITH X-Requested-With custom header (sent by app.js api client)
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/profile");
        request.Headers.Add("Cookie", $"mp_session={token}");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Content = new StringContent("{\"phone\":\"555-6666\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cors_AllowedOrigin_ReceivesAllowOriginAndCredentialsHeaders()
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Cors:AllowedOrigins:0", "https://trusted.example.com");
        });

        var client = customFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/profile");
        request.Headers.Add("Origin", "https://trusted.example.com");
        request.Headers.Add("Access-Control-Request-Method", "PATCH");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"),
            "Allowed origin should receive Access-Control-Allow-Origin header.");
        Assert.Equal("https://trusted.example.com",
            response.Headers.GetValues("Access-Control-Allow-Origin").First());
        Assert.True(response.Headers.Contains("Access-Control-Allow-Credentials"),
            "Allowed origin should receive Access-Control-Allow-Credentials header.");
    }

    [Fact]
    public async Task Csrf_BearerAuth_StateChangingRequest_DoesNotRequireCustomHeader()
    {
        var token = GetDanaToken();
        var client = _factory.CreateClient();

        // Bearer token auth without cookie does not require anti-CSRF custom header
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent("{\"phone\":\"555-7777\"}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Csrf_CookieAuth_SafeGetRequest_DoesNotRequireCustomHeader()
    {
        var token = GetDanaToken();
        var client = _factory.CreateClient();

        // GET request is safe/idempotent and should succeed with cookie alone
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Add("Cookie", $"mp_session={token}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
