using Microsoft.EntityFrameworkCore;
using MedicalPortal.Infrastructure.Data;
using Npgsql;

namespace MedicalPortal.Web;

public interface IDatabaseRoleInspector
{
    Task InspectAsync(MedicalPortalDbContext db, string connectionString);
}

public sealed class DatabaseRoleInspector : IDatabaseRoleInspector
{
    public async Task InspectAsync(MedicalPortalDbContext db, string connectionString)
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            PostgresConnectionHelper.ToNpgsqlConnectionString(connectionString));
        if (string.Equals(parsed.Password, "postgres", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.Password, "password", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.Password, "changeme", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production startup failed: the database password is a known default and must be rotated.");
        }

        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                """
                SELECT r.rolsuper,
                       r.rolbypassrls,
                       (
                           SELECT COUNT(*)
                           FROM pg_class c
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE n.nspname = 'public'
                             AND c.relkind IN ('r', 'S')
                             AND c.relowner = r.oid
                       )
                FROM pg_roles r
                WHERE r.rolname = current_user
                """;

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Production startup failed: the connected database role could not be resolved.");
            if (reader.GetBoolean(0))
                throw new InvalidOperationException("Production startup failed: the application database role must not be a superuser.");
            if (reader.GetBoolean(1))
                throw new InvalidOperationException("Production startup failed: the application database role must not have BYPASSRLS.");
            if (reader.GetInt64(2) != 0)
                throw new InvalidOperationException("Production startup failed: the application database role must not own application tables.");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
