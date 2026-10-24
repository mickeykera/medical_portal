namespace MedicalPortal.Core.Common;

/// <summary>
/// Represents an immutable date in the Ethiopian Calendar (Incarnation Era / Amete Mihret).
/// Months are 1-based (1 = Meskerem, ..., 12 = Nehase, 13 = Pagume).
/// </summary>
public readonly record struct EthiopianDate : IComparable<EthiopianDate>
{
    public int Year { get; }
    public int Month { get; }
    public int Day { get; }

    public EthiopianDate(int year, int month, int day)
    {
        if (year < 1)
            throw new ArgumentOutOfRangeException(nameof(year), "Ethiopian year must be >= 1.");
        if (month < 1 || month > 13)
            throw new ArgumentOutOfRangeException(nameof(month), "Ethiopian month must be between 1 and 13.");

        bool isLeap = (year % 4 == 3);
        int maxDays = (month == 13) ? (isLeap ? 6 : 5) : 30;

        if (day < 1 || day > maxDays)
        {
            throw new ArgumentOutOfRangeException(nameof(day),
                $"Day {day} is invalid for Ethiopian month {month} in year {year} (max days: {maxDays}, isLeapYear: {isLeap}).");
        }

        Year = year;
        Month = month;
        Day = day;
    }

    /// <summary>
    /// An Ethiopian year is a leap year if (Year % 4 == 3). In leap years, Pagume has 6 days.
    /// </summary>
    public bool IsLeapYear => (Year % 4 == 3);

    /// <summary>
    /// Whether this date falls in the 13th intercalary month (Pagume).
    /// </summary>
    public bool IsPagume => Month == 13;

    /// <summary>
    /// Whether this date is Ethiopian New Year (Enkutatash - 1 Meskerem).
    /// </summary>
    public bool IsEnkutatash => Month == 1 && Day == 1;

    /// <summary>
    /// Number of days in the month (30 for months 1-12; 5 or 6 for Pagume).
    /// </summary>
    public int DaysInMonth => (Month == 13) ? (IsLeapYear ? 6 : 5) : 30;

    /// <summary>
    /// Formatted representation in DD/MM/YYYY.
    /// </summary>
    public override string ToString() => $"{Day:D2}/{Month:D2}/{Year:D4}";

    /// <summary>
    /// Standard ISO-like format YYYY-MM-DD.
    /// </summary>
    public string ToIsoString() => $"{Year:D4}-{Month:D2}-{Day:D2}";

    public int CompareTo(EthiopianDate other)
    {
        int y = Year.CompareTo(other.Year);
        if (y != 0) return y;
        int m = Month.CompareTo(other.Month);
        if (m != 0) return m;
        return Day.CompareTo(other.Day);
    }
}

/// <summary>
/// Pure, deterministic conversion engine between Gregorian Calendar (DateOnly/DateTime)
/// and the Ethiopian Calendar (Amete Mihret).
/// Reusable without runtime dependencies.
/// </summary>
public static class EthiopianCalendar
{
    /// <summary>
    /// The fixed day offset between .NET 0-indexed DateOnly.DayNumber (Gregorian 0001-01-01 = 0)
    /// and the Ethiopian Incarnation Era epoch (Meskerem 1, 1 A.M. = August 27, 8 A.D. Gregorian = DayNumber 2795).
    /// </summary>
    public const int DayNumberOffset = 2795;

    public static readonly string[] MonthNamesEnglish = new[]
    {
        "", "Meskerem", "Tikimt", "Hidar", "Tahsas", "Tir", "Yakatit",
        "Magabit", "Miyazya", "Ginbot", "Sene", "Hamle", "Nehase", "Pagume"
    };

    public static readonly string[] MonthNamesAmharic = new[]
    {
        "", "መስከረም", "ጥቅምት", "ኅዳር", "ታኅሣሥ", "ጥር", "የካቲት",
        "መጋቢት", "ሚያዝያ", "ግንቦት", "ሰኔ", "ሐምሌ", "ነሐሴ", "ጳጉሜ"
    };

    public static readonly string[] MonthNamesOromo = new[]
    {
        "", "Birraa", "Onkololeessa", "Sadaasa", "Muddee", "Amajjii", "Guraandhala",
        "Bitooteessa", "Ebla", "Caamsaa", "Waxabajjii", "Adooleessa", "Hagayya", "Qaammee"
    };

    public static readonly string[] DayNamesEnglish = new[]
    {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
    };

    public static readonly string[] DayNamesAmharic = new[]
    {
        "እሑድ", "ሰኞ", "ማክሰኞ", "ረቡዕ", "ሐሙስ", "ዓርብ", "ቅዳሜ"
    };

    public static readonly string[] DayNamesOromo = new[]
    {
        "Dilbata", "Wiixata", "Qibxata", "Roobii", "Kamiisa", "Jimmata", "Sanbata duraa"
    };

    /// <summary>
    /// Converts a Gregorian DateOnly to an EthiopianDate.
    /// </summary>
    public static EthiopianDate ToEthiopianDate(DateOnly gregorianDate)
    {
        int days = gregorianDate.DayNumber - DayNumberOffset;
        if (days < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gregorianDate),
                "Dates prior to Ethiopian Era Year 1 (August 27, 8 A.D. Gregorian) are not supported.");
        }

        // Each 4-year cycle has 4 * 365 + 1 = 1461 days
        int cycle = days / 1461;
        int rem = days % 1461;

        int yInCycle;
        int dInYear;

        if (rem < 365)
        {
            yInCycle = 1;
            dInYear = rem;
        }
        else if (rem < 730)
        {
            yInCycle = 2;
            dInYear = rem - 365;
        }
        else if (rem < 1096)
        {
            yInCycle = 3; // Leap year (366 days, Pagume has 6 days)
            dInYear = rem - 730;
        }
        else
        {
            yInCycle = 4;
            dInYear = rem - 1096;
        }

        int year = cycle * 4 + yInCycle;
        int month = (dInYear / 30) + 1;
        int day = (dInYear % 30) + 1;

        return new EthiopianDate(year, month, day);
    }

    /// <summary>
    /// Converts a Gregorian DateTime to an EthiopianDate.
    /// </summary>
    public static EthiopianDate ToEthiopianDate(DateTime gregorianDateTime)
    {
        return ToEthiopianDate(DateOnly.FromDateTime(gregorianDateTime));
    }

    /// <summary>
    /// Converts an EthiopianDate to a Gregorian DateOnly.
    /// </summary>
    public static DateOnly ToGregorianDate(EthiopianDate ethiopianDate)
    {
        return ToGregorianDate(ethiopianDate.Year, ethiopianDate.Month, ethiopianDate.Day);
    }

    /// <summary>
    /// Converts Ethiopian year, month, and day components to a Gregorian DateOnly.
    /// </summary>
    public static DateOnly ToGregorianDate(int year, int month, int day)
    {
        // Parameter validation handled by EthiopianDate
        var eth = new EthiopianDate(year, month, day);

        int days = (eth.Year - 1) * 365 + (eth.Year / 4) + 30 * (eth.Month - 1) + (eth.Day - 1);
        return DateOnly.FromDayNumber(days + DayNumberOffset);
    }

    /// <summary>
    /// Returns the month name in the specified language (en, am, om).
    /// </summary>
    public static string GetMonthName(int month, string language = "en")
    {
        if (month < 1 || month > 13)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be 1..13");

        return language.ToLowerInvariant() switch
        {
            "am" => MonthNamesAmharic[month],
            "om" => MonthNamesOromo[month],
            _ => MonthNamesEnglish[month]
        };
    }

    /// <summary>
    /// Returns the weekday name in the specified language (en, am, om).
    /// </summary>
    public static string GetDayOfWeekName(DayOfWeek dow, string language = "en")
    {
        int idx = (int)dow;
        return language.ToLowerInvariant() switch
        {
            "am" => DayNamesAmharic[idx],
            "om" => DayNamesOromo[idx],
            _ => DayNamesEnglish[idx]
        };
    }

    /// <summary>
    /// Formats an EthiopianDate into a localized display string (e.g. "ጥቅምት 14, 2019").
    /// </summary>
    public static string Format(EthiopianDate date, string language = "en")
    {
        var monthName = GetMonthName(date.Month, language);
        return language.ToLowerInvariant() switch
        {
            "am" => $"{monthName} {date.Day}, {date.Year}",
            "om" => $"{monthName} {date.Day}, {date.Year}",
            _ => $"{monthName} {date.Day}, {date.Year}"
        };
    }

    /// <summary>
    /// Returns the Ethiopian Fiscal Year (EFY).
    /// The Ethiopian fiscal year begins on 1 Hamle (July 8 Gregorian).
    /// Months 11 (Hamle), 12 (Nehase), and 13 (Pagume) belong to Fiscal Year (Year + 1).
    /// Months 1 (Meskerem) through 10 (Sene) belong to Fiscal Year (Year).
    /// </summary>
    public static int GetFiscalYear(EthiopianDate date)
    {
        return date.Month >= 11 ? date.Year + 1 : date.Year;
    }

    /// <summary>
    /// Returns the Ethiopian Fiscal Quarter (1 to 4):
    /// Q1: Hamle (11), Nehase (12), Pagume (13), Meskerem (1)
    /// Q2: Tikimt (2), Hidar (3), Tahsas (4)
    /// Q3: Tir (5), Yakatit (6), Magabit (7)
    /// Q4: Miyazya (8), Ginbot (9), Sene (10)
    /// </summary>
    public static int GetFiscalQuarter(EthiopianDate date)
    {
        return date.Month switch
        {
            11 or 12 or 13 or 1 => 1,
            2 or 3 or 4 => 2,
            5 or 6 or 7 => 3,
            8 or 9 or 10 => 4,
            _ => 1
        };
    }

    /// <summary>
    /// Returns standard calendar quarter (1 to 4) for the Ethiopian calendar year:
    /// Q1: Meskerem (1), Tikimt (2), Hidar (3)
    /// Q2: Tahsas (4), Tir (5), Yakatit (6)
    /// Q3: Magabit (7), Miyazya (8), Ginbot (9)
    /// Q4: Sene (10), Hamle (11), Nehase (12), Pagume (13)
    /// </summary>
    public static int GetCalendarQuarter(EthiopianDate date)
    {
        return (date.Month - 1) / 3 + 1 switch
        {
            > 4 => 4,
            var q => q
        };
    }

    /// <summary>
    /// Generates DHIS2 HMIS standard period identifier for monthly reporting (e.g. "201902").
    /// </summary>
    public static string GetDhis2MonthlyPeriod(EthiopianDate date)
    {
        return $"{date.Year:D4}{date.Month:D2}";
    }

    /// <summary>
    /// Generates DHIS2 HMIS standard period identifier for fiscal quarter reporting (e.g. "2019Q1").
    /// </summary>
    public static string GetDhis2QuarterlyPeriod(EthiopianDate date)
    {
        int fy = GetFiscalYear(date);
        int fq = GetFiscalQuarter(date);
        return $"{fy:D4}Q{fq}";
    }

    /// <summary>
    /// Generates DHIS2 HMIS standard period identifier for fiscal year reporting (e.g. "2019").
    /// </summary>
    public static string GetDhis2YearlyPeriod(EthiopianDate date)
    {
        return $"{GetFiscalYear(date):D4}";
    }
}
