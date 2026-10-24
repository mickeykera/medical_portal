using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Common;
using MedicalPortal.Core.Entities;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class EthiopianCalendarAndLocalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EthiopianCalendarAndLocalizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    #region 1. Core Conversion Engine & Roundtrip Verification

    [Fact]
    public void EthiopianCalendar_Roundtrip_200Years_MatchesGregorianExact()
    {
        // Test all 73,414 consecutive days from 1900-01-01 to 2100-12-31
        var start = new DateOnly(1900, 1, 1);
        var end = new DateOnly(2100, 12, 31);

        for (var current = start; current <= end; current = current.AddDays(1))
        {
            var eth = EthiopianCalendar.ToEthiopianDate(current);

            // Month must be 1..13
            Assert.InRange(eth.Month, 1, 13);

            // Day must be 1..30 for months 1..12, 1..6 for Pagume
            if (eth.Month <= 12)
            {
                Assert.InRange(eth.Day, 1, 30);
            }
            else
            {
                int maxPagume = (eth.Year % 4 == 3) ? 6 : 5;
                Assert.InRange(eth.Day, 1, maxPagume);
            }

            // Roundtrip conversion must recover original Gregorian date exactly
            var roundtrip = EthiopianCalendar.ToGregorianDate(eth);
            Assert.Equal(current, roundtrip);
        }
    }

    [Fact]
    public void EthiopianCalendar_HistoricalAndBenchmarkDates_AreExact()
    {
        // 1. Ethiopian Millennium: 2007-09-12 -> 2000-01-01 (1 Meskerem 2000)
        var millenniumG = new DateOnly(2007, 9, 12);
        var millenniumEth = EthiopianCalendar.ToEthiopianDate(millenniumG);
        Assert.Equal(2000, millenniumEth.Year);
        Assert.Equal(1, millenniumEth.Month);
        Assert.Equal(1, millenniumEth.Day);
        Assert.True(millenniumEth.IsEnkutatash);
        Assert.False(millenniumEth.IsLeapYear); // 2000 % 4 == 0 (not leap)

        // 2. Millennium Eve: 2007-09-11 -> 1999-13-06 (6 Pagume 1999 leap year)
        var millenniumEveG = new DateOnly(2007, 9, 11);
        var millenniumEveEth = EthiopianCalendar.ToEthiopianDate(millenniumEveG);
        Assert.Equal(1999, millenniumEveEth.Year);
        Assert.Equal(13, millenniumEveEth.Month);
        Assert.Equal(6, millenniumEveEth.Day);
        Assert.True(millenniumEveEth.IsPagume);
        Assert.True(millenniumEveEth.IsLeapYear); // 1999 % 4 == 3 (leap)

        // 3. Battle of Adwa: 1896-03-01 -> 1888-06-23 (23 Yekatit 1888)
        var adwaG = new DateOnly(1896, 3, 1);
        var adwaEth = EthiopianCalendar.ToEthiopianDate(adwaG);
        Assert.Equal(1888, adwaEth.Year);
        Assert.Equal(6, adwaEth.Month); // Yekatit
        Assert.Equal(23, adwaEth.Day);
        Assert.Equal(adwaG, EthiopianCalendar.ToGregorianDate(adwaEth));

        // 4. Leap Day Pagume 6, 2015: 2023-09-11
        var pagume6G = new DateOnly(2023, 9, 11);
        var pagume6Eth = EthiopianCalendar.ToEthiopianDate(pagume6G);
        Assert.Equal(2015, pagume6Eth.Year);
        Assert.Equal(13, pagume6Eth.Month);
        Assert.Equal(6, pagume6Eth.Day);
        Assert.True(pagume6Eth.IsLeapYear);
        Assert.True(pagume6Eth.IsPagume);

        // 5. Enkutatash 2016 (following leap year 2015): 2023-09-12
        var enkutatash2016G = new DateOnly(2023, 9, 12);
        var enkutatash2016Eth = EthiopianCalendar.ToEthiopianDate(enkutatash2016G);
        Assert.Equal(2016, enkutatash2016Eth.Year);
        Assert.Equal(1, enkutatash2016Eth.Month);
        Assert.Equal(1, enkutatash2016Eth.Day);
        Assert.True(enkutatash2016Eth.IsEnkutatash);

        // 6. Common year Pagume 5, 2016: 2024-09-10
        var pagume5G = new DateOnly(2024, 9, 10);
        var pagume5Eth = EthiopianCalendar.ToEthiopianDate(pagume5G);
        Assert.Equal(2016, pagume5Eth.Year);
        Assert.Equal(13, pagume5Eth.Month);
        Assert.Equal(5, pagume5Eth.Day);
        Assert.False(pagume5Eth.IsLeapYear);

        // 7. Enkutatash 2017: 2024-09-11
        var enkutatash2017G = new DateOnly(2024, 9, 11);
        var enkutatash2017Eth = EthiopianCalendar.ToEthiopianDate(enkutatash2017G);
        Assert.Equal(2017, enkutatash2017Eth.Year);
        Assert.Equal(1, enkutatash2017Eth.Month);
        Assert.Equal(1, enkutatash2017Eth.Day);
        Assert.True(enkutatash2017Eth.IsEnkutatash);
    }

    [Fact]
    public void EthiopianCalendar_EthiopianFiscalYear_CalculatesCorrectly()
    {
        // Hamle 1 is the first day of the Ethiopian Fiscal Year (corresponds to July 8 Gregorian).
        // 1 Hamle 2016 (2024-07-08) is EFY 2017, Q1.
        var startFiscalG = new DateOnly(2024, 7, 8);
        var startFiscalEth = EthiopianCalendar.ToEthiopianDate(startFiscalG);
        Assert.Equal(2016, startFiscalEth.Year);
        Assert.Equal(11, startFiscalEth.Month); // Hamle
        Assert.Equal(1, startFiscalEth.Day);
        Assert.Equal(2017, EthiopianCalendar.GetFiscalYear(startFiscalEth));
        Assert.Equal(1, EthiopianCalendar.GetFiscalQuarter(startFiscalEth));

        // 30 Sene 2016 (2024-07-07) is EFY 2016, Q4.
        var endFiscalG = new DateOnly(2024, 7, 7);
        var endFiscalEth = EthiopianCalendar.ToEthiopianDate(endFiscalG);
        Assert.Equal(2016, endFiscalEth.Year);
        Assert.Equal(10, endFiscalEth.Month); // Sene
        Assert.Equal(30, endFiscalEth.Day);
        Assert.Equal(2016, EthiopianCalendar.GetFiscalYear(endFiscalEth));
        Assert.Equal(4, EthiopianCalendar.GetFiscalQuarter(endFiscalEth));

        // Pagume 3, 2016 (2024-09-08) falls in EFY 2017, Q1 (Hamle-Meskerem).
        var pagumeG = new DateOnly(2024, 9, 8);
        var pagumeEth = EthiopianCalendar.ToEthiopianDate(pagumeG);
        Assert.Equal(2016, pagumeEth.Year);
        Assert.Equal(13, pagumeEth.Month);
        Assert.Equal(2017, EthiopianCalendar.GetFiscalYear(pagumeEth));
        Assert.Equal(1, EthiopianCalendar.GetFiscalQuarter(pagumeEth));

        // Tikimt 15, 2016 (2023-10-26) falls in EFY 2016, Q2 (Tikimt-Tahsas).
        var tikimtG = new DateOnly(2023, 10, 26);
        var tikimtEth = EthiopianCalendar.ToEthiopianDate(tikimtG);
        Assert.Equal(2016, tikimtEth.Year);
        Assert.Equal(2, tikimtEth.Month); // Tikimt
        Assert.Equal(2016, EthiopianCalendar.GetFiscalYear(tikimtEth));
        Assert.Equal(2, EthiopianCalendar.GetFiscalQuarter(tikimtEth));
    }

    [Fact]
    public void EthiopianCalendar_NegativeAndBoundaryValidation_ThrowsExpectedExceptions()
    {
        // Month out of range (0, 14)
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 14, 1));

        // Day out of range for standard month (0, 31)
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 1, 31));

        // Pagume non-leap year (e.g. 2016) has 5 days; day 6 must throw
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 13, 6));

        // Pagume leap year (e.g. 2015) has 6 days; day 7 must throw
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2015, 13, 7));

        // Year must be positive
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(-10, 1, 1));
    }

    [Fact]
    public void EthiopianCalendar_DHIS2PeriodFormats_AreValid()
    {
        var eth = new EthiopianDate(2016, 1, 15);
        Assert.Equal("201601", EthiopianCalendar.GetDhis2MonthlyPeriod(eth));
        Assert.Equal("2016", EthiopianCalendar.GetDhis2YearlyPeriod(eth));

        var ethPagume = new EthiopianDate(2016, 13, 3);
        Assert.Equal("201613", EthiopianCalendar.GetDhis2MonthlyPeriod(ethPagume));

        var ethHamle = new EthiopianDate(2016, 11, 10);
        // Hamle is Q1 of EFY 2017
        Assert.Equal("2017Q1", EthiopianCalendar.GetDhis2QuarterlyPeriod(ethHamle));

        var ethTikimt = new EthiopianDate(2016, 2, 5);
        // Tikimt is Q2 of EFY 2016
        Assert.Equal("2016Q2", EthiopianCalendar.GetDhis2QuarterlyPeriod(ethTikimt));
    }

    [Fact]
    public void EthiopianCalendar_MonthAndDayNames_SupportAllThreeLanguages()
    {
        // English
        Assert.Equal("Meskerem", EthiopianCalendar.GetMonthName(1, "en"));
        Assert.Equal("Pagume", EthiopianCalendar.GetMonthName(13, "en"));
        Assert.Equal("Sunday", EthiopianCalendar.GetDayOfWeekName(DayOfWeek.Sunday, "en"));

        // Amharic (አማርኛ)
        Assert.Equal("መስከረም", EthiopianCalendar.GetMonthName(1, "am"));
        Assert.Equal("ጳጉሜ", EthiopianCalendar.GetMonthName(13, "am"));
        Assert.Equal("እሑድ", EthiopianCalendar.GetDayOfWeekName(DayOfWeek.Sunday, "am"));

        // Afaan Oromoo
        Assert.Equal("Birraa", EthiopianCalendar.GetMonthName(1, "om"));
        Assert.Equal("Qaammee", EthiopianCalendar.GetMonthName(13, "om"));
        Assert.Equal("Dilbata", EthiopianCalendar.GetDayOfWeekName(DayOfWeek.Sunday, "om"));
    }

    #endregion

    #region 2. Localization Service & Catalog Completeness

    [Fact]
    public void LocalizationCatalog_AllLanguagesHaveIdenticalKeySets()
    {
        var enKeys = LocalizationCatalog.English.Keys.OrderBy(k => k).ToList();
        var amKeys = LocalizationCatalog.Amharic.Keys.OrderBy(k => k).ToList();
        var omKeys = LocalizationCatalog.Oromo.Keys.OrderBy(k => k).ToList();

        Assert.NotEmpty(enKeys);
        Assert.Equal(enKeys, amKeys);
        Assert.Equal(enKeys, omKeys);

        // Ensure key hospital domain strings are non-empty
        foreach (var key in enKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(LocalizationCatalog.English[key]));
            Assert.False(string.IsNullOrWhiteSpace(LocalizationCatalog.Amharic[key]));
            Assert.False(string.IsNullOrWhiteSpace(LocalizationCatalog.Oromo[key]));
        }
    }

    [Fact]
    public void LocalizationService_RetrievesTranslationsAndFallsBack()
    {
        var service = new LocalizationService();

        // Direct key lookups
        Assert.Equal("Medical Portal", service.GetString("app.title", "en"));
        Assert.Equal("የህክምና ፖርታል", service.GetString("app.title", "am"));
        Assert.Equal("Poortaalii Yaalaa", service.GetString("app.title", "om"));

        // Unknown language falls back to English
        Assert.Equal("Medical Portal", service.GetString("app.title", "fr"));

        // Non-existent key returns key name
        Assert.Equal("non.existent.key", service.GetString("non.existent.key", "am"));

        // Supported languages list
        var supported = service.GetSupportedLanguages();
        Assert.Equal(3, supported.Count);
        Assert.Contains(supported, l => l.Code == "en");
        Assert.Contains(supported, l => l.Code == "am");
        Assert.Contains(supported, l => l.Code == "om");
    }

    #endregion

    #region 3. Calendar Dimension Database Seeding & SQL Reporting

    [Fact]
    public async Task CalendarDimension_DatabaseTable_PopulatedAndAggregatesCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();

        // Ensure calendar dimension table is seeded
        if (!await db.CalendarDimensions.AnyAsync())
        {
            await DbSeeder.SeedCalendarDimensionAsync(db);
        }

        // 1. Verify leap year 2024 Gregorian has 366 days
        var count2024 = await db.CalendarDimensions.CountAsync(c => c.GregorianYear == 2024);
        Assert.Equal(366, count2024);

        // 2. Query aggregated Ethiopian months for year 2016
        // 12 months with 30 days + 1 Pagume with 5 days = 365 days
        var months2016 = await db.CalendarDimensions
            .Where(c => c.EthiopianYear == 2016)
            .GroupBy(c => new { c.EthiopianYear, c.EthiopianMonth, c.EthiopianMonthNameEnglish })
            .Select(g => new
            {
                g.Key.EthiopianMonth,
                g.Key.EthiopianMonthNameEnglish,
                DayCount = g.Count()
            })
            .OrderBy(m => m.EthiopianMonth)
            .ToListAsync();

        Assert.Equal(13, months2016.Count);
        for (int m = 1; m <= 12; m++)
        {
            Assert.Equal(30, months2016[m - 1].DayCount);
        }
        Assert.Equal(5, months2016[12].DayCount); // Pagume 2016 is common year

        // 3. Fast SQL grouping by Ethiopian Fiscal Year & Quarter
        var fiscalSummary = await db.CalendarDimensions
            .Where(c => c.EthiopianFiscalYear == 2017)
            .GroupBy(c => c.EthiopianFiscalQuarter)
            .Select(g => new { Quarter = g.Key, Days = g.Count() })
            .OrderBy(g => g.Quarter)
            .ToListAsync();

        Assert.Equal(4, fiscalSummary.Count);
    }

    #endregion

    #region 4. Calendar & Localization API Endpoints

    [Fact]
    public async Task CalendarApi_ConvertGregorian_ReturnsValidDualCalendarPayload()
    {
        var client = _factory.CreateClient();

        // Ethiopian Millennium: 2007-09-12
        var res = await client.GetAsync("/api/calendar/convert?date=2007-09-12");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        var eth = doc.GetProperty("ethiopian");
        Assert.Equal(2000, eth.GetProperty("year").GetInt32());
        Assert.Equal(1, eth.GetProperty("month").GetInt32());
        Assert.Equal(1, eth.GetProperty("day").GetInt32());
        Assert.True(eth.GetProperty("isEnkutatash").GetBoolean());

        var fiscal = doc.GetProperty("fiscal");
        Assert.Equal(2000, fiscal.GetProperty("fiscalYear").GetInt32());
        Assert.Equal(1, fiscal.GetProperty("fiscalQuarter").GetInt32());

        var dhis2 = doc.GetProperty("dhis2");
        Assert.Equal("200001", dhis2.GetProperty("monthlyPeriod").GetString());
    }

    [Fact]
    public async Task CalendarApi_ConvertEthiopian_ReturnsValidGregorianDate()
    {
        var client = _factory.CreateClient();

        // 1 Meskerem 2017 -> 2024-09-11
        var res = await client.GetAsync("/api/calendar/convert-ethiopian?year=2017&month=1&day=1");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        var greg = doc.GetProperty("gregorian");
        Assert.Equal("2024-09-11", greg.GetProperty("date").GetString());
        Assert.Equal(2024, greg.GetProperty("year").GetInt32());
        Assert.Equal(9, greg.GetProperty("month").GetInt32());
        Assert.Equal(11, greg.GetProperty("day").GetInt32());
    }

    [Fact]
    public async Task CalendarApi_MonthView_ReturnsAllDaysForEthiopianMonth()
    {
        var client = _factory.CreateClient();

        // Month 1 (Meskerem) 2016 -> exactly 30 days
        var res30 = await client.GetAsync("/api/calendar/month-view?year=2016&month=1");
        Assert.Equal(HttpStatusCode.OK, res30.StatusCode);
        var doc30 = await res30.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30, doc30.GetProperty("daysInMonth").GetInt32());
        Assert.Equal(30, doc30.GetProperty("days").GetArrayLength());

        // Month 13 (Pagume) in leap year 2015 -> exactly 6 days
        var resLeapPagume = await client.GetAsync("/api/calendar/month-view?year=2015&month=13");
        Assert.Equal(HttpStatusCode.OK, resLeapPagume.StatusCode);
        var docLeap = await resLeapPagume.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, docLeap.GetProperty("daysInMonth").GetInt32());
        Assert.Equal(6, docLeap.GetProperty("days").GetArrayLength());

        // Month 13 (Pagume) in common year 2016 -> exactly 5 days
        var resCommonPagume = await client.GetAsync("/api/calendar/month-view?year=2016&month=13");
        Assert.Equal(HttpStatusCode.OK, resCommonPagume.StatusCode);
        var docCommon = await resCommonPagume.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, docCommon.GetProperty("daysInMonth").GetInt32());
        Assert.Equal(5, docCommon.GetProperty("days").GetArrayLength());
    }

    [Fact]
    public async Task CalendarApi_NegativeInputs_ReturnBadRequest()
    {
        var client = _factory.CreateClient();

        // 1. Invalid Gregorian date format
        var badDate = await client.GetAsync("/api/calendar/convert?date=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);

        // 2. Invalid Ethiopian date components (Pagume 6 in common year 2016)
        var badEth = await client.GetAsync("/api/calendar/convert-ethiopian?year=2016&month=13&day=6");
        Assert.Equal(HttpStatusCode.BadRequest, badEth.StatusCode);

        // 3. Month 14 does not exist
        var badMonth = await client.GetAsync("/api/calendar/month-view?year=2016&month=14");
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
    }

    [Fact]
    public async Task LocalizationApi_LanguagesAndCatalogs_ReturnExpectedData()
    {
        var client = _factory.CreateClient();

        // 1. Supported languages endpoint
        var langRes = await client.GetAsync("/api/localization/languages");
        Assert.Equal(HttpStatusCode.OK, langRes.StatusCode);
        var langs = await langRes.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(langs);
        Assert.Equal(3, langs.Count);

        // 2. Amharic dictionary
        var amRes = await client.GetAsync("/api/localization/am");
        Assert.Equal(HttpStatusCode.OK, amRes.StatusCode);
        var amDoc = await amRes.Content.ReadFromJsonAsync<JsonElement>();
        var amStrings = amDoc.GetProperty("strings");
        Assert.Equal("የህክምና ፖርታል", amStrings.GetProperty("app.title").GetString());
        Assert.Equal("የቀጠሮዎች መርሃ-ግብር", amStrings.GetProperty("nav.appointments").GetString());

        // 3. Afaan Oromoo dictionary
        var omRes = await client.GetAsync("/api/localization/om");
        Assert.Equal(HttpStatusCode.OK, omRes.StatusCode);
        var omDoc = await omRes.Content.ReadFromJsonAsync<JsonElement>();
        var omStrings = omDoc.GetProperty("strings");
        Assert.Equal("Poortaalii Yaalaa", omStrings.GetProperty("app.title").GetString());
        Assert.Equal("Beellama", omStrings.GetProperty("nav.appointments").GetString());
    }

    #endregion

    #region 5. Rule 8 Offline Self-Sufficiency & Browser Dialog Ban

    private static string FindFrontendDir()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "frontend");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                return candidate;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find frontend directory from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Rule8_FrontendFiles_ContainZeroBrowserDialogs()
    {
        var frontendDir = FindFrontendDir();
        Assert.True(Directory.Exists(frontendDir), $"Frontend directory not found at {frontendDir}");

        var jsFiles = Directory.GetFiles(frontendDir, "*.js", SearchOption.AllDirectories);
        Assert.NotEmpty(jsFiles);

        foreach (var file in jsFiles)
        {
            // Skip vendor files if any vendor directory has non-dialog library code
            var relativePath = Path.GetRelativePath(frontendDir, file);
            if (relativePath.StartsWith("js/vendor")) continue;

            var content = File.ReadAllText(file);

            // Assert no window.alert, window.confirm, window.prompt or alert(, confirm(, prompt(
            Assert.DoesNotContain("window.alert", content);
            Assert.DoesNotContain("window.confirm", content);
            Assert.DoesNotContain("window.prompt", content);

            // Check function call signatures (allowing showInAppAlert, showInAppConfirm, etc.)
            Assert.DoesNotMatch(@"\balert\(", content);
            Assert.DoesNotMatch(@"\bconfirm\(", content);
            Assert.DoesNotMatch(@"\bprompt\(", content);
        }
    }

    [Fact]
    public void Rule8_FrontendIndex_ContainsZeroCdnDependencies()
    {
        var frontendDir = FindFrontendDir();
        var indexHtmlPath = Path.Combine(frontendDir, "index.html");
        Assert.True(File.Exists(indexHtmlPath), $"index.html not found at {indexHtmlPath}");

        var html = File.ReadAllText(indexHtmlPath);

        // Disallow remote runtime scripts and stylesheets
        Assert.DoesNotContain("cdnjs.cloudflare.com", html);
        Assert.DoesNotContain("unpkg.com", html);
        Assert.DoesNotContain("cdn.jsdelivr.net", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);

        // Verify local offline assets are linked
        Assert.Contains("/js/vendor/signalr.min.js", html);
        Assert.Contains("/js/ethiopian-calendar.js", html);
        Assert.Contains("/js/i18n.js", html);
    }

    [Fact]
    public void Rule8_LocalVendoredAssets_ExistOnDisk()
    {
        var frontendDir = FindFrontendDir();

        // 1. SignalR minified client
        var signalrJs = Path.Combine(frontendDir, "js/vendor/signalr.min.js");
        Assert.True(File.Exists(signalrJs), "Local signalr.min.js must exist for offline operation");
        var fi = new FileInfo(signalrJs);
        Assert.True(fi.Length > 10000, "signalr.min.js should be populated");

        // 2. Ethiopic font
        var fontWoff2 = Path.Combine(frontendDir, "fonts/NotoSansEthiopic-Regular.woff2");
        Assert.True(File.Exists(fontWoff2), "NotoSansEthiopic-Regular.woff2 must exist for offline operation");

        // 3. Offline JSON locale catalogs
        Assert.True(File.Exists(Path.Combine(frontendDir, "locales/en.json")), "en.json locale file missing");
        Assert.True(File.Exists(Path.Combine(frontendDir, "locales/am.json")), "am.json locale file missing");
        Assert.True(File.Exists(Path.Combine(frontendDir, "locales/om.json")), "om.json locale file missing");
    }

    #endregion
}
