using MedicalPortal.Core.Common;

namespace MedicalPortal.Core.Entities;

/// <summary>
/// Precalculated calendar dimension entity representing 'calendar_dim' for years 1900..2100.
/// Precalculated dual Gregorian and Ethiopian calendar attributes for high-performance SQL grouping,
/// fiscal year calculations, and national DHIS2 HMIS reporting.
/// Global reference table (not tenant-scoped).
/// </summary>
public class CalendarDim
{
    /// <summary>
    /// Gregorian date (Primary Key).
    /// </summary>
    public DateOnly Date { get; set; }

    public int GregorianYear { get; set; }
    public int GregorianMonth { get; set; }
    public int GregorianDay { get; set; }
    public int GregorianQuarter { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public string DayOfWeekNameEnglish { get; set; } = string.Empty;
    public string DayOfWeekNameAmharic { get; set; } = string.Empty;
    public string DayOfWeekNameOromo { get; set; } = string.Empty;

    public int EthiopianYear { get; set; }
    public int EthiopianMonth { get; set; }
    public int EthiopianDay { get; set; }
    public int EthiopianQuarter { get; set; }
    public string EthiopianMonthNameEnglish { get; set; } = string.Empty;
    public string EthiopianMonthNameAmharic { get; set; } = string.Empty;
    public string EthiopianMonthNameOromo { get; set; } = string.Empty;
    public string EthiopianDateFormatted { get; set; } = string.Empty;
    public string EthiopianDateFormattedAmharic { get; set; } = string.Empty;
    public string EthiopianDateFormattedOromo { get; set; } = string.Empty;

    public int EthiopianFiscalYear { get; set; }
    public int EthiopianFiscalQuarter { get; set; }
    public bool IsEthiopianLeapYear { get; set; }
    public bool IsPagume { get; set; }
    public bool IsEnkutatash { get; set; }

    /// <summary>
    /// DHIS2 Monthly Period format: "YYYYMM" (e.g. "201902" for Tikimt 2019).
    /// </summary>
    public string Dhis2MonthlyPeriod { get; set; } = string.Empty;

    /// <summary>
    /// DHIS2 Quarterly Period format: "YYYYQ#" (e.g. "2019Q1").
    /// </summary>
    public string Dhis2QuarterlyPeriod { get; set; } = string.Empty;

    /// <summary>
    /// DHIS2 Yearly Period format: "YYYY" (e.g. "2019").
    /// </summary>
    public string Dhis2YearlyPeriod { get; set; } = string.Empty;

    /// <summary>
    /// Factory constructor populating all dimension attributes from pure Gregorian DateOnly.
    /// </summary>
    public static CalendarDim FromGregorianDate(DateOnly date)
    {
        var eth = EthiopianCalendar.ToEthiopianDate(date);
        int gQuarter = (date.Month - 1) / 3 + 1;
        int ethQuarter = EthiopianCalendar.GetCalendarQuarter(eth);
        int fiscalYear = EthiopianCalendar.GetFiscalYear(eth);
        int fiscalQuarter = EthiopianCalendar.GetFiscalQuarter(eth);

        return new CalendarDim
        {
            Date = date,
            GregorianYear = date.Year,
            GregorianMonth = date.Month,
            GregorianDay = date.Day,
            GregorianQuarter = gQuarter,
            DayOfWeek = date.DayOfWeek,
            DayOfWeekNameEnglish = EthiopianCalendar.GetDayOfWeekName(date.DayOfWeek, "en"),
            DayOfWeekNameAmharic = EthiopianCalendar.GetDayOfWeekName(date.DayOfWeek, "am"),
            DayOfWeekNameOromo = EthiopianCalendar.GetDayOfWeekName(date.DayOfWeek, "om"),

            EthiopianYear = eth.Year,
            EthiopianMonth = eth.Month,
            EthiopianDay = eth.Day,
            EthiopianQuarter = ethQuarter,
            EthiopianMonthNameEnglish = EthiopianCalendar.GetMonthName(eth.Month, "en"),
            EthiopianMonthNameAmharic = EthiopianCalendar.GetMonthName(eth.Month, "am"),
            EthiopianMonthNameOromo = EthiopianCalendar.GetMonthName(eth.Month, "om"),
            EthiopianDateFormatted = eth.ToString(),
            EthiopianDateFormattedAmharic = EthiopianCalendar.Format(eth, "am"),
            EthiopianDateFormattedOromo = EthiopianCalendar.Format(eth, "om"),

            EthiopianFiscalYear = fiscalYear,
            EthiopianFiscalQuarter = fiscalQuarter,
            IsEthiopianLeapYear = eth.IsLeapYear,
            IsPagume = eth.IsPagume,
            IsEnkutatash = eth.IsEnkutatash,

            Dhis2MonthlyPeriod = EthiopianCalendar.GetDhis2MonthlyPeriod(eth),
            Dhis2QuarterlyPeriod = EthiopianCalendar.GetDhis2QuarterlyPeriod(eth),
            Dhis2YearlyPeriod = EthiopianCalendar.GetDhis2YearlyPeriod(eth)
        };
    }
}
