using System.Data.Common;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedicalPortal.Web;

public interface IDatabaseRoleInspector
{
    Task InspectAsync(MedicalPortalDbContext db, string connectionString);
}

public sealed record DatabaseRoleFacts(bool IsSuperuser, bool BypassRls, long OwnedTables);

public static class DatabaseRolePolicy
{
    public const int MinimumPasswordLength = 20;
    private static readonly string[] KnownDefaults =
        { "postgres", "password", "changeme", "admin", "root", "secret", "example" };

    public static string? EvaluatePassword(string? userName, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Production startup failed: the database password is empty.";
        if (KnownDefaults.Contains(password, StringComparer.OrdinalIgnoreCase))
            return "Production startup failed: the database password is a known default and must be rotated.";
        if (password.Length < MinimumPasswordLength)
            return "Production startup failed: the database password must be at least 20 characters.";
        if (string.Equals(password, userName, StringComparison.OrdinalIgnoreCase))
            return "Production startup failed: the database password must not equal the username.";
        if (password.Distinct().Count() < 8)
            return "Production startup failed: the database password has too little variety.";
        return null;
    }

    public static string? EvaluateRole(DatabaseRoleFacts? facts)
    {
        if (facts is null)
            return "Production startup failed: the connected database role could not be resolved.";
        if (facts.IsSuperuser)
            return "Production startup failed: the application database role must not be a superuser.";
        if (facts.BypassRls)
            return "Production startup failed: the application database role must not have BYPASSRLS.";
        if (facts.OwnedTables != 0)
            return "Production startup failed: the application database role must not own application tables.";
        return null;
    }

    public static async Task<DatabaseRoleFacts?> ReadFactsAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.rolsuper, r.rolbypassrls,
                   (SELECT COUNT(*) FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public' AND c.relkind IN ('r','S')
                      AND c.relowner = r.oid)
            FROM pg_roles r WHERE r.rolname = current_user
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new DatabaseRoleFacts(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetInt64(2));
    }
}

public sealed class DatabaseRoleInspector : IDatabaseRoleInspector
{
    public async Task InspectAsync(MedicalPortalDbContext db, string connectionString)
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            PostgresConnectionHelper.ToNpgsqlConnectionString(connectionString));
        var passwordProblem = DatabaseRolePolicy.EvaluatePassword(parsed.Username, parsed.Password);
        if (passwordProblem is not null) throw new InvalidOperationException(passwordProblem);

        await db.Database.OpenConnectionAsync();
        try
        {
            var facts = await DatabaseRolePolicy.ReadFactsAsync(db.Database.GetDbConnection());
            var roleProblem = DatabaseRolePolicy.EvaluateRole(facts);
            if (roleProblem is not null) throw new InvalidOperationException(roleProblem);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
