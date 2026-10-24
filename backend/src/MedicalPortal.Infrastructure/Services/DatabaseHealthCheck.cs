using System.Diagnostics;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MedicalPortal.Infrastructure.Services;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            sw.Stop();

            var providerName = db.Database.ProviderName ?? "Unknown";
            var data = new Dictionary<string, object>
            {
                ["provider"] = providerName,
                ["latencyMs"] = sw.ElapsedMilliseconds
            };

            return canConnect
                ? HealthCheckResult.Healthy($"Database ({providerName}) is operational.", data)
                : HealthCheckResult.Unhealthy($"Cannot connect to database ({providerName}).", data: data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = sw.ElapsedMilliseconds,
                ["error"] = ex.Message
            };
            return HealthCheckResult.Unhealthy("Database health check exception.", ex, data);
        }
    }
}
