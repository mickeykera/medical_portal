using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace MedicalPortal.Tests;

/// <summary>
/// Source-scan tests that enforce coding standards.
/// </summary>
public class SourceScanTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "..", ".."));

    private static readonly string BackendRoot = Path.Combine(RepoRoot, "backend", "src");

    [Fact]
    public void NoSessionLevelSetConfig_InBackendCode()
    {
        // Scan all .cs files in backend/src for set_config(..., false) or plain SET app.current_tenant_id
        if (!Directory.Exists(BackendRoot))
        {
            Assert.Fail($"Backend source directory not found: {BackendRoot}");
            return;
        }

        var csFiles = Directory.GetFiles(BackendRoot, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        // Pattern 1: set_config('app.current_tenant_id', ..., false) - session-level
        var sessionLevelPattern = new Regex(
            @"set_config\s*\(\s*['""]app\.current_tenant_id['""]\s*,\s*.+?\s*,\s*false\s*\)",
            RegexOptions.IgnoreCase);

        // Pattern 2: SET app.current_tenant_id = ... (not SET LOCAL)
        var plainSetPattern = new Regex(
            @"\bSET\s+app\.current_tenant_id\s*=",
            RegexOptions.IgnoreCase);

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            var relativePath = Path.GetRelativePath(BackendRoot, file);

            // Skip test files
            if (relativePath.Contains("Tests") || relativePath.Contains("Test"))
                continue;

            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                // Skip comments
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                // Check for session-level set_config
                if (sessionLevelPattern.IsMatch(line))
                {
                    violations.Add($"{relativePath}:{i + 1}: Session-level set_config(..., false) found: {line.Trim()}");
                }

                // Check for plain SET (not SET LOCAL)
                if (plainSetPattern.IsMatch(line) && !line.Contains("SET LOCAL", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{relativePath}:{i + 1}: Plain SET app.current_tenant_id found (should be SET LOCAL): {line.Trim()}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void TenantContext_IsTransactionLocal_Enforced()
    {
        // Verify that TenantRlsConnectionInterceptor uses SET LOCAL (true) not session-level (false)
        var interceptorFile = Path.Combine(BackendRoot, "MedicalPortal.Infrastructure", "Data", "TenantRlsConnectionInterceptor.cs");
        Assert.True(File.Exists(interceptorFile), $"TenantRlsConnectionInterceptor.cs not found at {interceptorFile}");

        var content = File.ReadAllText(interceptorFile);

        // Should use SET LOCAL (third parameter = true)
        Assert.Contains("set_config('app.current_tenant_id',", content);
        Assert.Contains(", true)", content); // SET LOCAL
        Assert.DoesNotContain(", false)", content); // Not session-level
    }
}
