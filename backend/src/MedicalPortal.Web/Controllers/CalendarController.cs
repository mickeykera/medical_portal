using MedicalPortal.Core.Common;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/calendar")]
public class CalendarController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;

    public CalendarController(MedicalPortalDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Converts a Gregorian date (YYYY-MM-DD) to its Ethiopian calendar representation,
    /// including Amharic and Afaan Oromoo localized strings, fiscal year, and DHIS2 period IDs.
    /// </summary>
    [HttpGet("convert")]
    public async Task<IActionResult> ConvertGregorian([FromQuery] string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out var gDate))
        {
            return BadRequest(new { error = "Valid Gregorian date query parameter 'date' (YYYY-MM-DD) is required." });
        }

        try
        {
            // Fast lookup in calendar_dim table if available
            var dim = await _db.CalendarDimensions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Date == gDate);

            if (dim != null)
            {
                return Ok(new
                {
                    gregorian = new
                    {
                        date = dim.Date.ToString("yyyy-MM-dd"),
                        year = dim.GregorianYear,
                        month = dim.GregorianMonth,
                        day = dim.GregorianDay,
                        quarter = dim.GregorianQuarter,
                        dayOfWeek = dim.DayOfWeek.ToString()
                    },
                    ethiopian = new
                    {
                        year = dim.EthiopianYear,
                        month = dim.EthiopianMonth,
                        day = dim.EthiopianDay,
                        quarter = dim.EthiopianQuarter,
                        monthNames = new
                        {
                            en = dim.EthiopianMonthNameEnglish,
                            am = dim.EthiopianMonthNameAmharic,
                            om = dim.EthiopianMonthNameOromo
                        },
                        dayNames = new
                        {
                            en = dim.DayOfWeekNameEnglish,
                            am = dim.DayOfWeekNameAmharic,
                            om = dim.DayOfWeekNameOromo
                        },
                        formatted = new
                        {
                            iso = $"{dim.EthiopianYear:D4}-{dim.EthiopianMonth:D2}-{dim.EthiopianDay:D2}",
                            standard = dim.EthiopianDateFormatted,
                            am = dim.EthiopianDateFormattedAmharic,
                            om = dim.EthiopianDateFormattedOromo
                        },
                        isLeapYear = dim.IsEthiopianLeapYear,
                        isPagume = dim.IsPagume,
                        isEnkutatash = dim.IsEnkutatash
                    },
                    fiscal = new
                    {
                        fiscalYear = dim.EthiopianFiscalYear,
                        fiscalQuarter = dim.EthiopianFiscalQuarter
                    },
                    dhis2 = new
                    {
                        monthlyPeriod = dim.Dhis2MonthlyPeriod,
                        quarterlyPeriod = dim.Dhis2QuarterlyPeriod,
                        yearlyPeriod = dim.Dhis2YearlyPeriod
                    }
                });
            }

            // Fallback to pure calculation
            var eth = EthiopianCalendar.ToEthiopianDate(gDate);
            return Ok(FormatCalculationResult(gDate, eth));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Converts Ethiopian date components (year, month, day) to Gregorian DateOnly (YYYY-MM-DD).
    /// </summary>
    [HttpGet("convert-ethiopian")]
    public IActionResult ConvertEthiopian([FromQuery] int year, [FromQuery] int month, [FromQuery] int day)
    {
        try
        {
            var eth = new EthiopianDate(year, month, day);
            var gDate = EthiopianCalendar.ToGregorianDate(eth);

            return Ok(new
            {
                ethiopian = new
                {
                    year = eth.Year,
                    month = eth.Month,
                    day = eth.Day,
                    monthNameEn = EthiopianCalendar.GetMonthName(eth.Month, "en"),
                    monthNameAm = EthiopianCalendar.GetMonthName(eth.Month, "am"),
                    monthNameOm = EthiopianCalendar.GetMonthName(eth.Month, "om"),
                    isLeapYear = eth.IsLeapYear,
                    isPagume = eth.IsPagume,
                    isEnkutatash = eth.IsEnkutatash
                },
                gregorian = new
                {
                    date = gDate.ToString("yyyy-MM-dd"),
                    year = gDate.Year,
                    month = gDate.Month,
                    day = gDate.Day,
                    dayOfWeek = gDate.DayOfWeek.ToString()
                }
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns current date representation in both Gregorian and Ethiopian formats
    /// localized to Africa/Addis_Ababa timezone (UTC+3).
    /// </summary>
    [HttpGet("today")]
    public IActionResult GetToday()
    {
        // Standard Ethiopia timezone is Africa/Addis_Ababa (UTC+3, no DST)
        var eatZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Addis_Ababa");
        var nowEat = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eatZone);
        var gToday = DateOnly.FromDateTime(nowEat);
        var ethToday = EthiopianCalendar.ToEthiopianDate(gToday);

        return Ok(FormatCalculationResult(gToday, ethToday));
    }

    /// <summary>
    /// Returns the complete calendar grid for an Ethiopian month,
    /// mapping each day to its corresponding Gregorian date and weekday.
    /// </summary>
    [HttpGet("month-view")]
    public IActionResult GetMonthView([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 1) return BadRequest(new { error = "Year must be >= 1" });
        if (month < 1 || month > 13) return BadRequest(new { error = "Month must be 1..13" });

        bool isLeap = (year % 4 == 3);
        int daysInMonth = (month == 13) ? (isLeap ? 6 : 5) : 30;

        var days = new List<object>(daysInMonth);
        for (int d = 1; d <= daysInMonth; d++)
        {
            var eth = new EthiopianDate(year, month, d);
            var g = EthiopianCalendar.ToGregorianDate(eth);

            days.Add(new
            {
                ethiopianDay = d,
                gregorianDate = g.ToString("yyyy-MM-dd"),
                dayOfWeek = g.DayOfWeek.ToString(),
                dayOfWeekAm = EthiopianCalendar.GetDayOfWeekName(g.DayOfWeek, "am"),
                dayOfWeekOm = EthiopianCalendar.GetDayOfWeekName(g.DayOfWeek, "om"),
                isEnkutatash = eth.IsEnkutatash,
                isPagume = eth.IsPagume
            });
        }

        return Ok(new
        {
            year,
            month,
            monthNameEn = EthiopianCalendar.GetMonthName(month, "en"),
            monthNameAm = EthiopianCalendar.GetMonthName(month, "am"),
            monthNameOm = EthiopianCalendar.GetMonthName(month, "om"),
            daysInMonth,
            isLeapYear = isLeap,
            days
        });
    }

    private static object FormatCalculationResult(DateOnly gDate, EthiopianDate eth)
    {
        return new
        {
            gregorian = new
            {
                date = gDate.ToString("yyyy-MM-dd"),
                year = gDate.Year,
                month = gDate.Month,
                day = gDate.Day,
                quarter = (gDate.Month - 1) / 3 + 1,
                dayOfWeek = gDate.DayOfWeek.ToString()
            },
            ethiopian = new
            {
                year = eth.Year,
                month = eth.Month,
                day = eth.Day,
                quarter = EthiopianCalendar.GetCalendarQuarter(eth),
                monthNames = new
                {
                    en = EthiopianCalendar.GetMonthName(eth.Month, "en"),
                    am = EthiopianCalendar.GetMonthName(eth.Month, "am"),
                    om = EthiopianCalendar.GetMonthName(eth.Month, "om")
                },
                dayNames = new
                {
                    en = EthiopianCalendar.GetDayOfWeekName(gDate.DayOfWeek, "en"),
                    am = EthiopianCalendar.GetDayOfWeekName(gDate.DayOfWeek, "am"),
                    om = EthiopianCalendar.GetDayOfWeekName(gDate.DayOfWeek, "om")
                },
                formatted = new
                {
                    iso = eth.ToIsoString(),
                    standard = eth.ToString(),
                    am = EthiopianCalendar.Format(eth, "am"),
                    om = EthiopianCalendar.Format(eth, "om")
                },
                isLeapYear = eth.IsLeapYear,
                isPagume = eth.IsPagume,
                isEnkutatash = eth.IsEnkutatash
            },
            fiscal = new
            {
                fiscalYear = EthiopianCalendar.GetFiscalYear(eth),
                fiscalQuarter = EthiopianCalendar.GetFiscalQuarter(eth)
            },
            dhis2 = new
            {
                monthlyPeriod = EthiopianCalendar.GetDhis2MonthlyPeriod(eth),
                quarterlyPeriod = EthiopianCalendar.GetDhis2QuarterlyPeriod(eth),
                yearlyPeriod = EthiopianCalendar.GetDhis2YearlyPeriod(eth)
            }
        };
    }
}
