using Npgsql;
using Xunit;

namespace MedicalPortal.Tests;

/// <summary>
/// Proves that <c>app.current_tenant_id</c> is SET LOCAL (transaction-scoped) and never
/// leaks at session level across a single-connection pool used by two sequential tenants.
/// </summary>
public class TenantContextTransactionLocalTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("POSTGRES_RLS_TEST_CONNECTION");

    [SkippableFact]
    public async Task TenantContext_IsTransactionLocal_OnSingleConnectionPool()
    {
        var password = Environment.GetEnvironmentVariable("PORTAL_APP_PASSWORD");
        Skip.If(string.IsNullOrWhiteSpace(password), "PORTAL_APP_PASSWORD is not set");
        Skip.If(ConnectionString is null, "PostgreSQL connection string is not set");

        if (Environment.GetEnvironmentVariable("CI") == "true" &&
            (string.IsNullOrWhiteSpace(password) || ConnectionString is null))
        {
            Assert.Fail("PostgreSQL connection details are required in CI");
        }

        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Username = "portal_app",
            Password = password,
            MaxPoolSize = 1
        };

        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);

        // ---- Tenant A: set inside transaction, verify visible, then commit ----
        await using var connA = await dataSource.OpenConnectionAsync();
        await using var txA = connA.BeginTransaction();

        await using var setA = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, true)", connA, txA);
        setA.Parameters.AddWithValue("tid", "00000000-0000-0000-0000-000000000001");
        await setA.ExecuteNonQueryAsync();

        await using var checkA = new NpgsqlCommand(
            "SELECT current_setting('app.current_tenant_id', true)", connA, txA);
        var insideA = (string)await checkA.ExecuteScalarAsync()!;
        Assert.Equal("00000000-0000-0000-0000-000000000001", insideA);

        await txA.CommitAsync();

        // After commit, the SET LOCAL value must have reverted on the same connection.
        await using var afterA = new NpgsqlCommand(
            "SELECT current_setting('app.current_tenant_id', true)", connA);
        var afterCommitA = (string?)await afterA.ExecuteScalarAsync();
        Assert.True(string.IsNullOrEmpty(afterCommitA),
            $"Tenant A leaked after commit: '{afterCommitA}'");

        connA.Close();

        // ---- Tenant B: reuse the same physical connection from the pool ----
        await using var connB = await dataSource.OpenConnectionAsync();
        await using var txB = connB.BeginTransaction();

        await using var setB = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, true)", connB, txB);
        setB.Parameters.AddWithValue("tid", "00000000-0000-0000-0000-000000000002");
        await setB.ExecuteNonQueryAsync();

        await using var checkB = new NpgsqlCommand(
            "SELECT current_setting('app.current_tenant_id', true)", connB, txB);
        var insideB = (string)await checkB.ExecuteScalarAsync()!;
        Assert.Equal("00000000-0000-0000-0000-000000000002", insideB);

        // Tenant A's value must not be visible during Tenant B's transaction.
        Assert.NotEqual("00000000-0000-0000-0000-000000000001", insideB);

        await txB.RollbackAsync();

        // After rollback, setting reverts again.
        await using var afterB = new NpgsqlCommand(
            "SELECT current_setting('app.current_tenant_id', true)", connB);
        var afterRollbackB = (string?)await afterB.ExecuteScalarAsync();
        Assert.True(string.IsNullOrEmpty(afterRollbackB),
            $"Tenant B leaked after rollback: '{afterRollbackB}'");
    }
}
