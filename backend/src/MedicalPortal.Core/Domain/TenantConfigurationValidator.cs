using System.Text.RegularExpressions;
using MedicalPortal.Core.Entities;

namespace MedicalPortal.Core.Domain;

public record TenantValidationResult(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings,
    WcagAnalysisResult? WcagAnalysis = null);

public record WcagAnalysisResult(
    string HexColor,
    double ContrastAgainstWhite,
    double ContrastAgainstBlack,
    bool PassesAaNormalText,
    bool PassesAaLargeText,
    string Summary);

public static class TenantConfigurationValidator
{
    private static readonly Regex SlugRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex HexColorRegex = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase) { "en", "am", "om" };
    private static readonly HashSet<string> ValidIsolationModes = new(StringComparer.OrdinalIgnoreCase) { "Shared database", "Dedicated database" };

    public static TenantValidationResult Validate(
        string name,
        string slug,
        string region,
        string timeZone,
        List<string>? languages,
        TenantSettings? settings,
        int facilitiesCount = 1,
        List<string>? departments = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        WcagAnalysisResult? wcagResult = null;

        // 1. Name validation
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Tenant name is required.");
        }
        else if (name.Trim().Length is < 2 or > 100)
        {
            errors.Add("Tenant name must be between 2 and 100 characters.");
        }

        // 2. Slug validation
        if (string.IsNullOrWhiteSpace(slug))
        {
            errors.Add("Tenant slug is required.");
        }
        else
        {
            var trimmedSlug = slug.Trim();
            if (trimmedSlug.Length is < 2 or > 60)
            {
                errors.Add("Tenant slug must be between 2 and 60 characters.");
            }
            if (!SlugRegex.IsMatch(trimmedSlug))
            {
                errors.Add("Tenant slug may contain only alphanumeric characters, underscores, and hyphens.");
            }
        }

        // 3. Languages validation
        if (languages == null || languages.Count == 0)
        {
            errors.Add("At least one language must be specified for the tenant.");
        }
        else
        {
            foreach (var lang in languages)
            {
                if (!SupportedLanguages.Contains(lang.Trim()))
                {
                    errors.Add($"Language '{lang}' is not supported. Supported languages are: en, am, om.");
                }
            }
        }

        // 4. TimeZone validation
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            errors.Add("TimeZone is required.");
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                // In cross-platform or Linux Docker, some Windows timezone IDs may not be found directly,
                // so warn rather than block if it's not standard UTC/Africa/Addis_Ababa
                if (!timeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase) &&
                    !timeZone.Equals("Africa/Addis_Ababa", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"TimeZone '{timeZone}' may not be recognized by all operating system timezone databases.");
                }
            }
            catch (Exception)
            {
                warnings.Add($"TimeZone '{timeZone}' could not be resolved by TimeZoneInfo.");
            }
        }

        // 5. Settings validation (if provided)
        if (settings != null)
        {
            // Branding & WCAG Color Contrast
            if (!string.IsNullOrWhiteSpace(settings.Branding.PrimaryColor))
            {
                var trimmedColor = settings.Branding.PrimaryColor.Trim();
                if (!HexColorRegex.IsMatch(trimmedColor))
                {
                    errors.Add($"Primary branding color '{trimmedColor}' is not a valid hex color code (e.g. #0284c7 or #e01b24).");
                }
                else
                {
                    var (ratioWhite, ratioBlack, passesAaNormal, passesAaLarge) =
                        WcagColorContrastCalculator.AnalyzeHexColor(trimmedColor);

                    wcagResult = new WcagAnalysisResult(
                        HexColor: trimmedColor,
                        ContrastAgainstWhite: ratioWhite,
                        ContrastAgainstBlack: ratioBlack,
                        PassesAaNormalText: passesAaNormal,
                        PassesAaLargeText: passesAaLarge,
                        Summary: passesAaNormal
                            ? $"Passes WCAG 2.1 AA for normal ({ratioWhite:F2}:1 >= 4.5:1) and large text."
                            : (passesAaLarge
                                ? $"Passes WCAG 2.1 AA for large text ({ratioWhite:F2}:1 >= 3.0:1) but fails normal text (< 4.5:1)."
                                : $"Fails WCAG 2.1 AA contrast requirements ({ratioWhite:F2}:1 < 3.0:1 against white).")
                    );

                    if (!passesAaLarge)
                    {
                        errors.Add($"Primary branding color '{trimmedColor}' has a contrast ratio of {ratioWhite:F2}:1 against white, failing WCAG 2.1 AA accessibility requirements (minimum 3.0:1 for large text).");
                    }
                    else if (!passesAaNormal)
                    {
                        warnings.Add($"Primary branding color '{trimmedColor}' meets WCAG 2.1 AA for large text ({ratioWhite:F2}:1) but fails normal text (< 4.5:1). Dark text or white text on large UI elements is recommended.");
                    }
                }
            }

            // Modules validation
            if (settings.Modules != null)
            {
                foreach (var mod in settings.Modules.Enabled)
                {
                    if (!TenantModules.All.Contains(mod.Trim()))
                    {
                        errors.Add($"Module '{mod}' in Enabled list is not a valid system module.");
                    }
                }

                foreach (var mod in settings.Modules.Disabled)
                {
                    if (!TenantModules.All.Contains(mod.Trim()))
                    {
                        errors.Add($"Module '{mod}' in Disabled list is not a valid system module.");
                    }
                }

                var overlapping = settings.Modules.Enabled
                    .Intersect(settings.Modules.Disabled, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (overlapping.Count > 0)
                {
                    errors.Add($"Modules cannot appear in both Enabled and Disabled lists: {string.Join(", ", overlapping)}.");
                }
            }

            // Hosting & Isolation
            if (settings.Hosting != null)
            {
                if (!ValidIsolationModes.Contains(settings.Hosting.Isolation.Trim()))
                {
                    errors.Add($"Hosting isolation '{settings.Hosting.Isolation}' is invalid. Supported values are: 'Shared database', 'Dedicated database'.");
                }
                if (string.IsNullOrWhiteSpace(settings.Hosting.Provider))
                {
                    errors.Add("Hosting provider is required.");
                }
            }

            // Security Settings
            if (settings.Security != null)
            {
                if (settings.Security.IdleTimeoutMinutes is < 5 or > 120)
                {
                    errors.Add("Security idle timeout must be between 5 and 120 minutes.");
                }

                if (settings.Security.RetentionYears < 1)
                {
                    errors.Add("Medical record retention period must be at least 1 year.");
                }
                else if (settings.Security.RetentionYears < 10)
                {
                    warnings.Add("Ethiopian MOH clinical guidelines recommend a minimum 10-year record retention period.");
                }
            }
        }

        // 6. Facilities & Departments
        if (facilitiesCount < 1)
        {
            errors.Add("Facilities count must be at least 1.");
        }

        if (departments != null)
        {
            foreach (var dept in departments)
            {
                if (string.IsNullOrWhiteSpace(dept))
                {
                    errors.Add("Department name cannot be blank.");
                }
                else if (dept.Trim().Length > 100)
                {
                    errors.Add($"Department name '{dept}' exceeds maximum allowed length of 100 characters.");
                }
            }
        }

        return new TenantValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings,
            WcagAnalysis: wcagResult);
    }
}
