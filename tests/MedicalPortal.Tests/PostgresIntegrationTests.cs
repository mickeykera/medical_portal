using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedicalPortal.Tests;

public class PostgresIntegrationTests
{
    [Fact]
    public async Task Postgres_CanConnect_CreateSchema_AndSeed()
    {
        var rawConn = Environment.GetEnvironmentVariable("TEST_POSTGRES_URL") 
                      ?? "postgres://postgres:postgres@localhost:5432/medical_portal";

        var npgsqlConn = PostgresConnectionHelper.ToNpgsqlConnectionString(rawConn);

        var options = new DbContextOptionsBuilder<MedicalPortalDbContext>()
            .UseNpgsql(npgsqlConn)
            .Options;

        using var db = new MedicalPortalDbContext(options);

        // Verify connection is possible
        if (!await db.Database.CanConnectAsync())
        {
            // If postgres is not reachable in CI/environment, skip gracefully
            return;
        }

        // Apply EF Core migrations in PostgreSQL
        await db.Database.MigrateAsync();

        var hasher = new PasswordHasher();
        await DbSeeder.SeedAsync(db, hasher);

        // Verify entities created and seeded in PostgreSQL
        var patientsCount = await db.Patients.CountAsync();
        Assert.True(patientsCount >= 3, "Patients should be seeded");

        var dana = await db.Patients
            .Include(p => p.Person)
            .Include(p => p.Allergies)
            .Include(p => p.Prescriptions)
            .FirstOrDefaultAsync(p => p.Mrn == "MRN-1000001");

        Assert.NotNull(dana);
        Assert.Equal("Dana", dana.Person.FirstName);
        Assert.NotEmpty(dana.Allergies);
        Assert.NotEmpty(dana.Prescriptions);

        var providersCount = await db.Providers.CountAsync();
        Assert.True(providersCount >= 2, "Providers should be seeded");

        var locationsCount = await db.Locations.CountAsync();
        Assert.True(locationsCount >= 5, "Locations should be seeded");
    }
}
