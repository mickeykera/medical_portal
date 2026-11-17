using MedicalPortal.Web;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalPortal.Tests;

/// <summary>
/// Temporarily clears development-only environment variables so that Production test hosts
/// evaluate the guard under test instead of an earlier, unrelated production guard.
/// Environment variables win over <see cref="WebApplicationFactory{Program}"/> configuration,
/// so they must be cleared at the process level for the duration of the test.
/// </summary>
public sealed class ClearDevelopmentEnvironmentVariables : IDisposable
{
    private readonly (string Name, string? OriginalValue)[] _originals =
    {
        ("DEV_TOTP_SECRET", Environment.GetEnvironmentVariable("DEV_TOTP_SECRET")),
        ("DEV_SEED_PASSWORD", Environment.GetEnvironmentVariable("DEV_SEED_PASSWORD")),
        ("DEV_QUICK_LOGIN", Environment.GetEnvironmentVariable("DEV_QUICK_LOGIN")),
        ("STRIPE_USE_FAKE", Environment.GetEnvironmentVariable("STRIPE_USE_FAKE")),
        ("HMS_USE_FAKE", Environment.GetEnvironmentVariable("HMS_USE_FAKE")),
        ("JWT_SECRET", Environment.GetEnvironmentVariable("JWT_SECRET")),
    };

    public ClearDevelopmentEnvironmentVariables()
    {
        foreach (var (name, _) in _originals)
            Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var (name, originalValue) in _originals)
            Environment.SetEnvironmentVariable(name, originalValue);
    }
}

public static class FakeDatabaseRoleInspectorRegistration
{
    public static void Register(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseRoleInspector));
        if (descriptor is not null)
            services.Remove(descriptor);
        services.AddSingleton<IDatabaseRoleInspector, FakeDatabaseRoleInspector>();
    }
}
