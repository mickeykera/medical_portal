using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class MigrationsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MigrationsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Migrations_InitialCreate_IsRegisteredInDbContext()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        var allMigrations = db.Database.GetMigrations().ToList();
        Assert.NotEmpty(allMigrations);
        Assert.Contains(allMigrations, m => m.EndsWith("_InitialCreate"));
    }

    [Fact]
    public void DesignTimeDbContextFactory_CreatesPostgresDbContextWithOptions()
    {
        var factory = new MedicalPortalDbContextFactory();
        using var db = factory.CreateDbContext(Array.Empty<string>());

        Assert.NotNull(db);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
    }

    [Fact]
    public async Task WebHost_InDevelopment_AppliesMigrationsAndSeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        // Verify seeded data exists in development
        var providerCount = await db.Providers.CountAsync();
        Assert.True(providerCount >= 2, "DbSeeder should seed initial providers in Development");

        var patientCount = await db.Patients.CountAsync();
        Assert.True(patientCount >= 3, "DbSeeder should seed initial patients in Development");
    }
}
