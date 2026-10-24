using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Infrastructure.Observability;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MedicalPortal.Tests;

public class HealthCheckAndObservabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckAndObservabilityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsJsonWithAllDependencyChecks()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("status", out var statusProp));
        Assert.Equal("Healthy", statusProp.GetString());

        Assert.True(json.TryGetProperty("entries", out var entriesProp));
        Assert.True(entriesProp.TryGetProperty("database", out var dbProp));
        Assert.Equal("Healthy", dbProp.GetProperty("status").GetString());

        Assert.True(entriesProp.TryGetProperty("redis", out var redisProp));
        Assert.Equal("Healthy", redisProp.GetProperty("status").GetString());

        Assert.True(entriesProp.TryGetProperty("payment_gateway", out var paymentProp));
        Assert.Equal("Healthy", paymentProp.GetProperty("status").GetString());

        Assert.True(entriesProp.TryGetProperty("hms", out var hmsProp));
        Assert.Equal("Healthy", hmsProp.GetProperty("status").GetString());

        Assert.True(entriesProp.TryGetProperty("notifications", out var notifProp));
        Assert.Equal("Healthy", notifProp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthLiveEndpoint_Returns200Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReadyEndpoint_Returns200Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void OpenTelemetry_Diagnostics_ActivitySourceAndMeterAreConfigured()
    {
        Assert.NotNull(MedicalPortalDiagnostics.ActivitySource);
        Assert.Equal("MedicalPortal.Tracing", MedicalPortalDiagnostics.ActivitySourceName);

        Assert.NotNull(MedicalPortalDiagnostics.Meter);
        Assert.Equal("MedicalPortal.Metrics", MedicalPortalDiagnostics.MeterName);

        // Verify custom instruments can record without exception
        MedicalPortalDiagnostics.LoginsTotal.Add(1);
        MedicalPortalDiagnostics.AppointmentsBookedTotal.Add(1);
        MedicalPortalDiagnostics.ClinicalSafetyChecksTotal.Add(1);
        MedicalPortalDiagnostics.PaymentsProcessedCents.Add(2500);
        MedicalPortalDiagnostics.MessagesSentTotal.Add(1);
        MedicalPortalDiagnostics.HmsSyncDurationMs.Record(142.5);
    }
}
