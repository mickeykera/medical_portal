using System.Globalization;

namespace MedicalPortal.Core.Domain;

public static class CurrencyFormatter
{
    private static readonly CultureInfo EnEtCulture;
    private static readonly CultureInfo AmEtCulture;
    private static readonly CultureInfo OmEtCulture;

    static CurrencyFormatter()
    {
        EnEtCulture = BuildCulture("en-ET", "ETB", 2);
        AmEtCulture = BuildCulture("am-ET", "ብር", 2);
        OmEtCulture = BuildCulture("om-ET", "Br", 2);
    }

    private static CultureInfo BuildCulture(string localeName, string currencySymbol, int decimalDigits)
    {
        CultureInfo baseCi;
        try
        {
            baseCi = CultureInfo.GetCultureInfo(localeName);
        }
        catch
        {
            baseCi = CultureInfo.InvariantCulture;
        }

        var clone = (CultureInfo)baseCi.Clone();
        clone.NumberFormat.CurrencySymbol = currencySymbol;
        clone.NumberFormat.CurrencyDecimalDigits = decimalDigits;
        return CultureInfo.ReadOnly(clone);
    }

    public static CultureInfo GetCulture(string? locale = "en-ET")
    {
        if (string.IsNullOrWhiteSpace(locale)) return EnEtCulture;
        var norm = locale.Trim().ToLowerInvariant();
        if (norm.StartsWith("am")) return AmEtCulture;
        if (norm.StartsWith("om")) return OmEtCulture;
        return EnEtCulture;
    }

    public static string Format(int santim, string? locale = "en-ET", string currencyCode = "ETB", int minorUnitDigits = 2)
    {
        var divisor = (decimal)Math.Pow(10, minorUnitDigits);
        var amount = santim / divisor;
        var culture = GetCulture(locale);

        return amount.ToString($"C{minorUnitDigits}", culture);
    }
}
