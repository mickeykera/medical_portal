using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MedicalPortal.Infrastructure.Services;

public class RedisHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public RedisHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connStr = _configuration["Redis:ConnectionString"]
            ?? _configuration["REDIS_CONNECTION_STRING"]
            ?? _configuration["SignalR:Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connStr))
        {
            return HealthCheckResult.Healthy("Redis backplane is disabled (running in in-memory mode).",
                new Dictionary<string, object>
                {
                    ["enabled"] = false,
                    ["mode"] = "in-memory"
                });
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var options = ConfigurationOptions.Parse(connStr);
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 2000;
            options.AbortOnConnectFail = false;

            using var muxer = await ConnectionMultiplexer.ConnectAsync(options);
            if (!muxer.IsConnected)
            {
                sw.Stop();
                return HealthCheckResult.Degraded($"Redis connection attempted to {connStr} but not yet connected.",
                    data: new Dictionary<string, object>
                    {
                        ["endpoint"] = connStr,
                        ["latencyMs"] = sw.ElapsedMilliseconds
                    });
            }

            var db = muxer.GetDatabase();
            var pingResult = await db.PingAsync();
            sw.Stop();

            return HealthCheckResult.Healthy($"Redis backplane operational ({pingResult.TotalMilliseconds:F1}ms).",
                new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["endpoint"] = connStr,
                    ["pingMs"] = pingResult.TotalMilliseconds,
                    ["latencyMs"] = sw.ElapsedMilliseconds
                });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return HealthCheckResult.Degraded($"Redis check failed: {ex.Message}",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["endpoint"] = connStr,
                    ["latencyMs"] = sw.ElapsedMilliseconds,
                    ["error"] = ex.Message
                });
        }
    }
}
