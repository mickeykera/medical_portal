using System;
using System.IO;

namespace MedicalPortal.Tests;

public static class RepoPaths
{
    private static readonly Lazy<string> _rootLazy = new(FindRepoRoot);

    public static string Root => _rootLazy.Value;

    public static string Frontend => Path.Combine(Root, "frontend");
    public static string Dockerfile => Path.Combine(Root, "Dockerfile");
    public static string DockerCompose => Path.Combine(Root, "docker-compose.yml");
    public static string CiWorkflow => Path.Combine(Root, ".github", "workflows", "ci.yml");
    public static string AdotTenantConfig => Path.Combine(Root, "docs", "tenants", "Adot-Hospital", "tenant-config.json");

    public static string GetFrontendPath(params string[] paths)
    {
        var parts = new string[paths.Length + 1];
        parts[0] = Frontend;
        Array.Copy(paths, 0, parts, 1, paths.Length);
        return Path.Combine(parts);
    }

    private static string FindRepoRoot()
    {
        var searchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var startDir in searchRoots)
        {
            if (string.IsNullOrEmpty(startDir)) continue;

            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "repo-root.marker")) ||
                    Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException(
            $"Repository root could not be located from '{AppContext.BaseDirectory}' or '{Directory.GetCurrentDirectory()}'. " +
            "Ensure either a '.git' directory or 'repo-root.marker' file exists in the repository root.");
    }
}
