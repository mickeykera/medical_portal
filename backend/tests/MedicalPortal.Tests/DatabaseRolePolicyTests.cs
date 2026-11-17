using MedicalPortal.Web;
using Npgsql;
using Xunit;

namespace MedicalPortal.Tests;

public class DatabaseRolePolicyTests
{
    [Theory]
    [InlineData("postgres")]
    [InlineData("password")]
    [InlineData("changeme")]
    [InlineData("admin")]
    [InlineData("root")]
    [InlineData("secret")]
    [InlineData("example")]
    public void RejectsKnownDefaultPasswords(string password)
    {
        var problem = DatabaseRolePolicy.EvaluatePassword("portal_app", password);
        Assert.NotNull(problem);
        Assert.Contains("known default", problem);
    }

    [Fact]
    public void RejectsEmptyPassword()
    {
        var problem = DatabaseRolePolicy.EvaluatePassword("portal_app", string.Empty);
        Assert.Equal("Production startup failed: the database password is empty.", problem);
    }

    [Fact]
    public void RejectsPasswordShorterThanTwentyChars()
    {
        var problem = DatabaseRolePolicy.EvaluatePassword("portal_app", "Short1!");
        Assert.Equal("Production startup failed: the database password must be at least 20 characters.", problem);
    }

    [Fact]
    public void RejectsPasswordEqualToUsername()
    {
        var longUser = "avery-long-username-123";
        var problem = DatabaseRolePolicy.EvaluatePassword(longUser, longUser);
        Assert.Equal("Production startup failed: the database password must not equal the username.", problem);
    }

    [Fact]
    public void RejectsLowVarietyPassword()
    {
        var problem = DatabaseRolePolicy.EvaluatePassword("portal_app", "aaaaaaaaaaaaaaaaaaaa");
        Assert.Equal("Production startup failed: the database password has too little variety.", problem);
    }

    [Fact]
    public void AcceptsStrongPassword()
    {
        var problem = DatabaseRolePolicy.EvaluatePassword("portal_app", "k9#mP2vL8qWz!rT4xN7$");
        Assert.Null(problem);
    }

    [Fact]
    public void RejectsSuperuser()
    {
        var problem = DatabaseRolePolicy.EvaluateRole(new DatabaseRoleFacts(true, false, 0));
        Assert.Equal("Production startup failed: the application database role must not be a superuser.", problem);
    }

    [Fact]
    public void RejectsBypassRls()
    {
        var problem = DatabaseRolePolicy.EvaluateRole(new DatabaseRoleFacts(false, true, 0));
        Assert.Equal("Production startup failed: the application database role must not have BYPASSRLS.", problem);
    }

    [Fact]
    public void RejectsTableOwner()
    {
        var problem = DatabaseRolePolicy.EvaluateRole(new DatabaseRoleFacts(false, false, 3));
        Assert.Equal("Production startup failed: the application database role must not own application tables.", problem);
    }

    [Fact]
    public void RejectsUnresolvedRole()
    {
        var problem = DatabaseRolePolicy.EvaluateRole(null);
        Assert.Equal("Production startup failed: the connected database role could not be resolved.", problem);
    }

    [Fact]
    public void AcceptsLimitedRole()
    {
        var problem = DatabaseRolePolicy.EvaluateRole(new DatabaseRoleFacts(false, false, 0));
        Assert.Null(problem);
    }

    [SkippableFact]
    public async Task PortalAppPassesRolePolicy()
    {
        var password = Environment.GetEnvironmentVariable("PORTAL_APP_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                Assert.Fail("PORTAL_APP_PASSWORD is not set");
            return;
        }

        var connectionString =
            $"Host=localhost;Port=5432;Database=medical_portal;Username=portal_app;Password={password}";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var facts = await DatabaseRolePolicy.ReadFactsAsync(connection);
        var problem = DatabaseRolePolicy.EvaluateRole(facts);

        Assert.Null(problem);
    }

    [SkippableFact]
    public async Task PortalAppConnectionUsesPortalAppRole()
    {
        var password = Environment.GetEnvironmentVariable("PORTAL_APP_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                Assert.Fail("PORTAL_APP_PASSWORD is not set");
            return;
        }

        var connectionString =
            $"Host=localhost;Port=5432;Database=medical_portal;Username=portal_app;Password={password}";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT current_user";
        var currentUser = (string)await cmd.ExecuteScalarAsync()!;

        Assert.Equal("portal_app", currentUser);
    }

    [SkippableFact]
    public async Task SuperuserFailsRolePolicy()
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_SUPERUSER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                Assert.Fail("POSTGRES_SUPERUSER_CONNECTION_STRING is not set");
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var facts = await DatabaseRolePolicy.ReadFactsAsync(connection);
        var problem = DatabaseRolePolicy.EvaluateRole(facts);

        Assert.NotNull(problem);
        Assert.Contains("must not be a superuser", problem);
    }
}
