using Xunit;

namespace MedicalPortal.Tests;

public class PostgresTestInfrastructureTests
{
    [Fact]
    public void DevelopmentComposeOverridePublishesPostgresOnlyOnLoopback()
    {
        var path = Path.Combine(RepoPaths.Root, "docker-compose.override.yml");
        Assert.True(File.Exists(path), "A development/test-only compose override is required.");
        var content = File.ReadAllText(path);
        Assert.Contains("127.0.0.1:5432:5432", content);
        Assert.Contains("postgres:17", File.ReadAllText(RepoPaths.DockerCompose));
    }

    [Fact]
    public void ContinuousIntegrationDefinesPostgres17TestService()
    {
        var content = File.ReadAllText(RepoPaths.CiWorkflow);
        Assert.Contains("postgres:17", content);
        Assert.Contains("POSTGRES_CONNECTION_STRING", content);
        Assert.Contains("dotnet test", content);
        Assert.Contains("-- -parallel none", content);
    }
}
