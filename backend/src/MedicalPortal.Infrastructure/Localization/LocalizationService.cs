using MedicalPortal.Core.Common;

namespace MedicalPortal.Infrastructure.Localization;

public interface ILocalizationService
{
    string GetString(string key, string? language = "en");
    IReadOnlyDictionary<string, string> GetCatalog(string language);
    IReadOnlyList<LanguageInfo> GetSupportedLanguages();
}

public class LocalizationService : ILocalizationService
{
    public string GetString(string key, string? language = "en")
    {
        var dict = LocalizationCatalog.GetDictionary(language ?? "en");
        if (dict.TryGetValue(key, out var val))
        {
            return val;
        }

        // Fallback to English if translation is missing
        if (LocalizationCatalog.English.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public IReadOnlyDictionary<string, string> GetCatalog(string language)
    {
        return LocalizationCatalog.GetDictionary(language);
    }

    public IReadOnlyList<LanguageInfo> GetSupportedLanguages()
    {
        return LocalizationCatalog.SupportedLanguages;
    }
}
