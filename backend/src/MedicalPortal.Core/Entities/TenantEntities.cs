using System.Text.Json;
using MedicalPortal.Core.Enums;

namespace MedicalPortal.Core.Entities;

public static class TenantConstants
{
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string DefaultTenantSlug = "springfield";
    public const string DefaultTenantName = "Springfield Health System";
    public const string TenantSlugHeader = "X-Tenant-Slug";
    public const string TenantIdHeader = "X-Tenant-Id";
}

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? HostDomain { get; set; }
    public string Region { get; set; } = "Other: Ethiopia, Addis Ababa";
    public string TimeZone { get; set; } = "UTC";
    public string LanguagesJson { get; set; } = "[\"en\"]";
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Serialized JSON storing branding, modules, hosting, integrations, and security settings.
    /// </summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>
    /// Unique cryptographic fingerprint identifying this tenant's encryption key.
    /// </summary>
    public string MasterKeyFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Initial genesis hash for this tenant's tamper-evident audit log hash chain.
    /// </summary>
    public string AuditChainHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TenantSettings GetParsedSettings()
    {
        if (string.IsNullOrWhiteSpace(SettingsJson)) return new TenantSettings();
        try
        {
            return JsonSerializer.Deserialize<TenantSettings>(SettingsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new TenantSettings();
        }
        catch
        {
            return new TenantSettings();
        }
    }

    public void SetSettings(TenantSettings settings)
    {
        SettingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }
}

public class TenantSettings
{
    public TenantBrandingSettings Branding { get; set; } = new();
    public TenantModuleSettings Modules { get; set; } = new();
    public TenantHostingSettings Hosting { get; set; } = new();
    public TenantIntegrationSettings Integrations { get; set; } = new();
    public TenantSecuritySettings Security { get; set; } = new();
}

public class TenantBrandingSettings
{
    public string PrimaryColor { get; set; } = "#0284c7";
    public string Logo { get; set; } = "to be supplied";
    public string SupportEmail { get; set; } = string.Empty;
}

public class TenantModuleSettings
{
    public List<string> Enabled { get; set; } = new()
    {
        "appointments", "messaging", "billing", "refills", "proxy", "sharing", "fhir", "findcare", "forms"
    };

    public List<string> Disabled { get; set; } = new();

    public bool IsEnabled(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) return false;
        var normalized = moduleName.Trim().ToLowerInvariant();
        if (Disabled.Any(d => d.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return false;
        return Enabled.Any(e => e.Trim().Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}

public class TenantHostingSettings
{
    public string Provider { get; set; } = "Hospital on-premises";
    public string DataResidency { get; set; } = "same country as hospital";
    public string Isolation { get; set; } = "Shared database"; // "Shared database" or "Dedicated database"
}

public class TenantIntegrationSettings
{
    public string Ehr { get; set; } = "Custom API"; // "OpenMRS", "Custom API", "HMS"
    public string Payments { get; set; } = "Other gateway"; // "Stripe", "Other gateway", "Telebirr"
    public string Notifications { get; set; } = "Email and SMS"; // "Email and SMS", "Email", "SMS"
    public string StaffSignIn { get; set; } = "Local accounts with MFA"; // "Local accounts with MFA", "OAuth2/OIDC"
}

public class TenantSecuritySettings
{
    public bool MfaStaff { get; set; } = true;
    public bool MfaPatients { get; set; } = true;
    public int IdleTimeoutMinutes { get; set; } = 15;
    public int RetentionYears { get; set; } = 20;
    public bool BreakGlass { get; set; } = true;
}

/// <summary>
/// WCAG 2.1 AA Color Contrast Calculator for verifying tenant branding tokens.
/// </summary>
public static class WcagColorContrastCalculator
{
    public static (double RatioAgainstWhite, double RatioAgainstBlack, bool PassesAaNormalWhite, bool PassesAaLargeWhite) AnalyzeHexColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return (1.0, 1.0, false, false);

        var hex = hexColor.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }

        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return (1.0, 1.0, false, false);
        }

        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;

        var lum = CalculateLuminance(r, g, b);
        var lumWhite = 1.0;
        var lumBlack = 0.0;

        var ratioWhite = (lumWhite + 0.05) / (lum + 0.05);
        var ratioBlack = (lum + 0.05) / (lumBlack + 0.05);

        // WCAG AA requires 4.5:1 for normal text, 3.0:1 for large text
        var passesAaNormalWhite = ratioWhite >= 4.5;
        var passesAaLargeWhite = ratioWhite >= 3.0;

        return (Math.Round(ratioWhite, 2), Math.Round(ratioBlack, 2), passesAaNormalWhite, passesAaLargeWhite);
    }

    private static double CalculateLuminance(int r, int g, int b)
    {
        var rs = Linearize(r / 255.0);
        var gs = Linearize(g / 255.0);
        var bs = Linearize(b / 255.0);
        return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
    }

    private static double Linearize(double c)
    {
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
