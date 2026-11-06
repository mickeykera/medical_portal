using Npgsql;
using Xunit;

namespace MedicalPortal.Tests;

public sealed class PostgresRlsSecurityTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("POSTGRES_RLS_TEST_CONNECTION")
        ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

    private static NpgsqlConnection OpenConnection(string? connectionString = null)
    {
        var value = connectionString ?? ConnectionString;
        Assert.False(string.IsNullOrWhiteSpace(value), "PostgreSQL RLS tests require a PostgreSQL connection.");

        var connection = new NpgsqlConnection(value);
        connection.Open();
        return connection;
    }

    private static NpgsqlConnection OpenApplicationConnection()
    {
        var value = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = "portal_app",
            Password = Environment.GetEnvironmentVariable("PORTAL_APP_PASSWORD")
        }.ConnectionString;
        return OpenConnection(value);
    }

    [Fact]
    public void PostgresTestInfrastructure_UsesDedicatedNonBypassApplicationRole()
    {
        if (ConnectionString is null) return;
        using var connection = OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT rolname, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole
            FROM pg_roles
            WHERE rolname IN ('portal_owner', 'portal_app')
            ORDER BY rolname
            """, connection);

        using var reader = command.ExecuteReader();
        var roles = new Dictionary<string, (bool Superuser, bool BypassRls, bool CreateDb, bool CreateRole)>();
        while (reader.Read())
        {
            roles[reader.GetString(0)] =
                (reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4));
        }

        Assert.True(roles.ContainsKey("portal_owner"), "portal_owner must be created before migrations.");
        Assert.True(roles.ContainsKey("portal_app"), "portal_app must be created before application startup.");
        Assert.False(roles["portal_app"].Superuser);
        Assert.False(roles["portal_app"].BypassRls);
        Assert.False(roles["portal_app"].CreateDb);
        Assert.False(roles["portal_app"].CreateRole);
    }

    [Fact]
    public void EveryTenantOwnedTable_IsForcedRlsProtected()
    {
        if (ConnectionString is null) return;
        using var connection = OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT c.relname,
                   c.relrowsecurity,
                   c.relforcerowsecurity,
                   EXISTS (
                       SELECT 1
                       FROM pg_policies p
                       WHERE p.schemaname = n.nspname AND p.tablename = c.relname
                   )
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relkind = 'r'
              AND EXISTS (
                  SELECT 1
                  FROM information_schema.columns col
                  WHERE col.table_schema = n.nspname
                    AND col.table_name = c.relname
                    AND col.column_name = 'TenantId'
              )
            ORDER BY c.relname
            """, connection);

        var missing = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.GetBoolean(1) || !reader.GetBoolean(2) || !reader.GetBoolean(3))
                missing.Add(reader.GetString(0));
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void AppendOnlyLedgers_CannotBeUpdatedOrDeletedByApplicationRole()
    {
        if (ConnectionString is null) return;
        using var connection = OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT table_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND column_name = 'TenantId'
              AND table_name IN ('AuditLogs', 'StockLedgerEntries', 'ControlledDrugEntries', 'LinenLedgerEntries', 'Payments', 'Receipts', 'MortuaryCustodyEvents')
            ORDER BY table_name
            """, connection);

        var tables = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        Assert.NotEmpty(tables);
        foreach (var table in tables)
        {
            using var privilegeCommand = new NpgsqlCommand(
                "SELECT has_table_privilege('portal_app', $1, 'UPDATE'), has_table_privilege('portal_app', $1, 'DELETE')",
                connection);
            privilegeCommand.Parameters.AddWithValue($"public.\"{table}\"");
            using var reader = privilegeCommand.ExecuteReader();
            Assert.True(reader.Read());
            Assert.False(reader.GetBoolean(0), $"{table} must not grant UPDATE to portal_app.");
            Assert.False(reader.GetBoolean(1), $"{table} must not grant DELETE to portal_app.");
        }
    }

    [Fact]
    public void ApplicationRole_FailsClosedWhenTenantSettingIsAbsent()
    {
        if (ConnectionString is null) return;
        using var connection = OpenApplicationConnection();
        using var clear = new NpgsqlCommand("RESET app.current_tenant_id", connection);
        clear.ExecuteNonQuery();
        using var command = new NpgsqlCommand("SELECT count(*) FROM \"Patients\"", connection);

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void ApplicationRole_TenantQueriesFailClosedAndTenantSettingDoesNotLeak()
    {
        if (ConnectionString is null) return;

        using var owner = OpenConnection();
        using var tableCommand = new NpgsqlCommand(
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r'
              AND EXISTS (
                  SELECT 1 FROM information_schema.columns col
                  WHERE col.table_schema = n.nspname
                    AND col.table_name = c.relname
                    AND col.column_name = 'TenantId'
              )
            ORDER BY c.relname
            """, owner);
        var tables = new List<string>();
        using (var reader = tableCommand.ExecuteReader())
        {
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        using var appDataSource = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(ConnectionString)
            {
                Username = "portal_app",
                Password = Environment.GetEnvironmentVariable("PORTAL_APP_PASSWORD"),
                MaxPoolSize = 1
            }.ConnectionString);
        var app = appDataSource.OpenConnection();
        foreach (var table in tables)
        {
            using var command = new NpgsqlCommand(
                $"SELECT count(*) FROM public.\"{table}\"",
                app);
            Assert.Equal(0L, (long)command.ExecuteScalar()!);
        }

        using (var transaction = app.BeginTransaction())
        {
            using var set = new NpgsqlCommand(
                "SELECT set_config('app.current_tenant_id', @tenant_id, true)",
                app,
                transaction);
            set.Parameters.AddWithValue("tenant_id", "00000000-0000-0000-0000-000000000001");
            set.ExecuteNonQuery();
            transaction.Commit();
        }

        app.Dispose();
        using var pooledSecondRequest = appDataSource.OpenConnection();
        using var leakedSetting = new NpgsqlCommand(
            "SELECT current_setting('app.current_tenant_id', true)",
            pooledSecondRequest);
        Assert.Equal(string.Empty, leakedSetting.ExecuteScalar()?.ToString());
    }

    [Fact]
    public void ApplicationRole_InsertWithAnotherTenant_IsRejectedByWithCheck()
    {
        if (ConnectionString is null) return;

        using var connection = OpenApplicationConnection();
        using var transaction = connection.BeginTransaction();
        using var set = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tenant_id, true)",
            connection,
            transaction);
        set.Parameters.AddWithValue("tenant_id", "00000000-0000-0000-0000-000000000001");
        set.ExecuteNonQuery();

        using var insert = new NpgsqlCommand(
            """
            INSERT INTO "Patients" ("Id", "PersonId", "Mrn", "CreatedAt", "TenantId")
            VALUES (@id, (SELECT "Id" FROM "Persons" LIMIT 1), @mrn, @created_at, @tenant_id)
            """,
            connection,
            transaction);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("mrn", $"RLS-{Guid.NewGuid():N}");
        insert.Parameters.AddWithValue("created_at", DateTime.UtcNow);
        insert.Parameters.AddWithValue("tenant_id", Guid.Parse("00000000-0000-0000-0000-000000000002"));

        Assert.Throws<PostgresException>(() => insert.ExecuteNonQuery());
        transaction.Rollback();
    }
}
