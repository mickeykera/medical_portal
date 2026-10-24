using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedicalPortal.Infrastructure.Data;

public class MedicalPortalDbContextFactory : IDesignTimeDbContextFactory<MedicalPortalDbContext>
{
    public MedicalPortalDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MedicalPortalDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=medical_portal;Username=postgres;Password=postgres;";

        var npgsqlConnection = PostgresConnectionHelper.ToNpgsqlConnectionString(connectionString);

        optionsBuilder.UseNpgsql(npgsqlConnection, b =>
        {
            b.MigrationsAssembly(typeof(MedicalPortalDbContext).Assembly.FullName);
        });

        return new MedicalPortalDbContext(optionsBuilder.Options);
    }
}
