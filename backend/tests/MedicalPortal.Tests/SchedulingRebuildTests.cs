using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Auth;
using MedicalPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedicalPortal.Tests;

public class SchedulingRebuildTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SchedulingRebuildTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Scheduling_DST_SpringForward_PreservesLocalTimesAndAdjustsUtcOffset()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/Chicago");

        // Spring forward in US/Central occurred on Sunday, March 8, 2026.
        // Clocks jumped from 02:00 CST (UTC-6) to 03:00 CDT (UTC-5).
        // Standard time: Friday, March 6, 2026
        var preDstLocal = new DateTime(2026, 3, 6, 9, 0, 0, DateTimeKind.Unspecified);
        var preDstUtc = SchedulingLogic.ConvertLocalToUtc(preDstLocal, tz);
        // 09:00 CST = 15:00 UTC (UTC-6)
        Assert.Equal(new DateTime(2026, 3, 6, 15, 0, 0, DateTimeKind.Utc), preDstUtc);

        // Daylight Saving Time: Monday, March 9, 2026
        var postDstLocal = new DateTime(2026, 3, 9, 9, 0, 0, DateTimeKind.Unspecified);
        var postDstUtc = SchedulingLogic.ConvertLocalToUtc(postDstLocal, tz);
        // 09:00 CDT = 14:00 UTC (UTC-5)
        Assert.Equal(new DateTime(2026, 3, 9, 14, 0, 0, DateTimeKind.Utc), postDstUtc);

        // Verify slot generation across Spring Forward:
        // Local slots remain 09:00 to 09:30 on both days, but their UTC representations accurately reflect the 1-hour jump
        var templates = new[]
        {
            new SchedulingLogic.ScheduleTemplateRule(DayOfWeek.Friday, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0)),
            new SchedulingLogic.ScheduleTemplateRule(DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0))
        };

        var fridaySlots = SchedulingLogic.FindSlotsInLocationTimeZone(
            new DateTime(2026, 3, 6), tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(),
            durationMinutes: 30, nowUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var mondaySlots = SchedulingLogic.FindSlotsInLocationTimeZone(
            new DateTime(2026, 3, 9), tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(),
            durationMinutes: 30, nowUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(4, fridaySlots.Count);
        Assert.Equal(4, mondaySlots.Count);

        // First slot on Friday: local 09:00 -> 15:00 UTC
        Assert.Equal("09:00", fridaySlots[0].LocalStart);
        Assert.Equal(new DateTime(2026, 3, 6, 15, 0, 0, DateTimeKind.Utc), fridaySlots[0].StartAt);

        // First slot on Monday: local 09:00 -> 14:00 UTC (1 hour earlier in UTC, exactly 09:00 local)
        Assert.Equal("09:00", mondaySlots[0].LocalStart);
        Assert.Equal(new DateTime(2026, 3, 9, 14, 0, 0, DateTimeKind.Utc), mondaySlots[0].StartAt);
    }

    [Fact]
    public void Scheduling_DST_SpringForward_InvalidTimeGapHandledSafely()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/Chicago");

        // 02:30 AM on March 8, 2026 does not exist due to clocks jumping 2:00 -> 3:00.
        var invalidLocal = new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified);
        Assert.True(tz.IsInvalidTime(invalidLocal));

        // ConvertLocalToUtc should handle this gracefully without throwing an exception
        var utc = SchedulingLogic.ConvertLocalToUtc(invalidLocal, tz);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void Scheduling_DST_FallBack_PreservesLocalTimesAndAdjustsUtcOffset()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/Chicago");

        // Fall back in US/Central occurred on Sunday, November 1, 2026.
        // Clocks fall back from 02:00 CDT (UTC-5) to 01:00 CST (UTC-6).
        // Daylight saving time: Friday, October 30, 2026
        var preDstLocal = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Unspecified);
        var preDstUtc = SchedulingLogic.ConvertLocalToUtc(preDstLocal, tz);
        // 09:00 CDT = 14:00 UTC
        Assert.Equal(new DateTime(2026, 10, 30, 14, 0, 0, DateTimeKind.Utc), preDstUtc);

        // Standard time: Monday, November 2, 2026
        var postDstLocal = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Unspecified);
        var postDstUtc = SchedulingLogic.ConvertLocalToUtc(postDstLocal, tz);
        // 09:00 CST = 15:00 UTC
        Assert.Equal(new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc), postDstUtc);
    }

    [Fact]
    public void Scheduling_OverlappingBlocks_ExcludesBlockedSlots()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/Chicago");
        var testDay = new DateTime(2026, 4, 15); // Wednesday
        var templates = new[]
        {
            new SchedulingLogic.ScheduleTemplateRule(DayOfWeek.Wednesday, new TimeSpan(9, 0, 0), new TimeSpan(12, 0, 0))
        };

        // 9:00 - 12:00 in Chicago (CDT, UTC-5) = 14:00 - 17:00 UTC
        // Block 10:00 - 11:00 local (15:00 - 16:00 UTC) for CME / out of office
        var blocks = new[]
        {
            new SchedulingLogic.BlockRule(
                new DateTime(2026, 4, 15, 15, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc),
                Reason: "Provider CME",
                IsHoliday: false)
        };

        var slots = SchedulingLogic.FindSlotsInLocationTimeZone(
            testDay, tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(),
            blocks, durationMinutes: 30, nowUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // 6 potential half-hour slots: 09:00, 09:30, 10:00, 10:30, 11:00, 11:30
        // 10:00 and 10:30 fall inside the 15:00-16:00 UTC block, so exactly 4 slots must remain:
        Assert.Equal(4, slots.Count);
        Assert.DoesNotContain(slots, s => s.LocalStart == "10:00");
        Assert.DoesNotContain(slots, s => s.LocalStart == "10:30");
        Assert.Contains(slots, s => s.LocalStart == "09:00");
        Assert.Contains(slots, s => s.LocalStart == "09:30");
        Assert.Contains(slots, s => s.LocalStart == "11:00");
        Assert.Contains(slots, s => s.LocalStart == "11:30");
    }

    [Fact]
    public void Scheduling_LocationHoliday_ExcludesAllOverlappingSlots()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/Chicago");
        var testDay = new DateTime(2026, 7, 4); // Saturday or test holiday
        var templates = new[]
        {
            new SchedulingLogic.ScheduleTemplateRule(testDay.DayOfWeek, new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0))
        };

        // Location full closure
        var blocks = new[]
        {
            new SchedulingLogic.BlockRule(
                new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                Reason: "Independence Day Clinic Closure",
                IsHoliday: true)
        };

        var slots = SchedulingLogic.FindSlotsInLocationTimeZone(
            testDay, tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(),
            blocks, durationMinutes: 30, nowUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(slots);
    }

    [Fact]
    public void Scheduling_CheckSlot_ReturnsBlockedProblem()
    {
        var baseTime = DateTime.UtcNow.Date.AddDays(5).AddHours(14);
        var candidate = new SchedulingLogic.TimeSlot(baseTime, baseTime.AddMinutes(30));
        var blocked = new[]
        {
            new SchedulingLogic.TimeSlot(baseTime.AddMinutes(10), baseTime.AddMinutes(40))
        };

        var result = SchedulingLogic.CheckSlot(candidate, busySlots: Array.Empty<SchedulingLogic.TimeSlot>(), roomBusySlots: null, blockedSlots: blocked);
        Assert.False(result.Ok);
        Assert.Equal("blocked_time", result.Problem);
        var msg = SchedulingLogic.DescribeSlotProblem(result.Problem!);
        Assert.Contains("unavailable during this time", msg);
    }

    [Fact]
    public void Scheduling_WeeklyTemplates_PrioritizesProviderSpecificSchedule()
    {
        var providerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var templates = new[]
        {
            // Location default: 08:00 to 17:00
            new SchedulingLogic.ScheduleTemplateRule(DayOfWeek.Tuesday, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), LocationId: locationId),
            // Provider specific: 10:00 to 13:00
            new SchedulingLogic.ScheduleTemplateRule(DayOfWeek.Tuesday, new TimeSpan(10, 0, 0), new TimeSpan(13, 0, 0), ProviderId: providerId, LocationId: locationId)
        };

        var windows = SchedulingLogic.GetOperatingWindowsForDay(DayOfWeek.Tuesday, templates, providerId: providerId, locationId: locationId);
        Assert.Single(windows);
        Assert.Equal(new TimeSpan(10, 0, 0), windows[0].Start);
        Assert.Equal(new TimeSpan(13, 0, 0), windows[0].End);

        // For other provider, falls back to location schedule
        var otherProviderId = Guid.NewGuid();
        var fallbackWindows = SchedulingLogic.GetOperatingWindowsForDay(DayOfWeek.Tuesday, templates, providerId: otherProviderId, locationId: locationId);
        Assert.Single(fallbackWindows);
        Assert.Equal(new TimeSpan(8, 0, 0), fallbackWindows[0].Start);
        Assert.Equal(new TimeSpan(17, 0, 0), fallbackWindows[0].End);
    }

    [Fact]
    public void Scheduling_AppointmentTypes_GeneratesDifferentDurations()
    {
        var tz = SchedulingLogic.ResolveTimeZone("America/Chicago");
        var testDay = new DateTime(2026, 5, 20); // Wednesday
        var templates = new[]
        {
            new SchedulingLogic.ScheduleTemplateRule(DayOfWeek.Wednesday, new TimeSpan(9, 0, 0), new TimeSpan(11, 0, 0)) // 2 hour window
        };

        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 15 min -> 8 slots in 2 hours
        var slots15 = SchedulingLogic.FindSlotsInLocationTimeZone(testDay, tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(), durationMinutes: 15, nowUtc: now);
        Assert.Equal(8, slots15.Count);

        // 30 min -> 4 slots in 2 hours
        var slots30 = SchedulingLogic.FindSlotsInLocationTimeZone(testDay, tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(), durationMinutes: 30, nowUtc: now);
        Assert.Equal(4, slots30.Count);

        // 45 min -> 2 slots in 2 hours (09:00-09:45, 09:45-10:30; 10:30-11:15 exceeds 11:00)
        var slots45 = SchedulingLogic.FindSlotsInLocationTimeZone(testDay, tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(), durationMinutes: 45, nowUtc: now);
        Assert.Equal(2, slots45.Count);

        // 60 min -> 2 slots in 2 hours (09:00-10:00, 10:00-11:00)
        var slots60 = SchedulingLogic.FindSlotsInLocationTimeZone(testDay, tz, templates, Array.Empty<SchedulingLogic.TimeSlot>(), durationMinutes: 60, nowUtc: now);
        Assert.Equal(2, slots60.Count);
    }

    [Fact]
    public async Task Scheduling_Api_GetAppointmentTypes_ReturnsActiveTypes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await db.UserAccounts.FirstAsync(u => u.Email == "dana@example.com");
        var token = db.CreateAuthenticatedSession(tokenService, user, user.PersonId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/appointments/types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var types = doc.GetProperty("appointmentTypes").EnumerateArray().ToList();
        Assert.True(types.Count >= 4);

        var codes = types.Select(t => t.GetProperty("code").GetString()).ToList();
        Assert.Contains("ROUTINE", codes);
        Assert.Contains("WELLNESS", codes);
        Assert.Contains("NEW_PATIENT", codes);
        Assert.Contains("URGENT", codes);
    }

    [Fact]
    public async Task Scheduling_Api_GetRooms_ReturnsActiveRooms()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await db.UserAccounts.FirstAsync(u => u.Email == "dana@example.com");
        var token = db.CreateAuthenticatedSession(tokenService, user, user.PersonId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/appointments/rooms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rooms = doc.GetProperty("rooms").EnumerateArray().ToList();
        Assert.True(rooms.Count >= 2);

        var names = rooms.Select(r => r.GetProperty("name").GetString()).ToList();
        Assert.Contains("Exam Room 1", names);
    }

    [Fact]
    public async Task Scheduling_Api_GetAvailability_ReturnsSlotsInLocationTimeZone()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await db.UserAccounts.FirstAsync(u => u.Email == "dana@example.com");
        var token = db.CreateAuthenticatedSession(tokenService, user, user.PersonId);
        var drPatel = await db.Providers.FirstAsync(p => p.Email == "apatel@clinic.example");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/care/availability?providerId={drPatel.Id}&days=3&durationMinutes=30");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.TryGetProperty("timeZone", out var tzProp));
        Assert.Equal("Africa/Addis_Ababa", tzProp.GetString());
        Assert.True(doc.TryGetProperty("days", out var daysProp));
    }

    [Fact]
    public async Task Scheduling_Api_DoubleBooking_RejectedWithConflict409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var patientUser = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");
        var patient = await db.Patients.FirstAsync(p => p.PersonId == patientUser.PersonId);
        var drPatel = await db.Providers.FirstAsync(p => p.Email == "apatel@clinic.example");
        var clinic = await db.Locations.FirstAsync(l => l.Name == "Bole Family Health Clinic (DEMO)");

        var token = db.CreateAuthenticatedSession(tokenService, patientUser, patientUser.PersonId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        // Choose a future time slot on day 70 (within 180 day booking window)
        var start = DateTime.UtcNow.Date.AddDays(70).AddHours(14); // 14:00 UTC
        var end = start.AddMinutes(30);

        var existing = await db.Appointments
            .Where(a => a.ProviderId == drPatel.Id && a.StartAt >= start.AddHours(-1) && a.EndAt <= end.AddHours(1))
            .ToListAsync();
        if (existing.Count > 0)
        {
            db.Appointments.RemoveRange(existing);
            await db.SaveChangesAsync();
        }

        // 1. Initial booking request -> Success 201
        var request1 = new
        {
            providerId = drPatel.Id.ToString(),
            locationId = clinic.Id.ToString(),
            startAt = start.ToString("o"),
            endAt = end.ToString("o"),
            reasonForVisit = "First appointment"
        };

        var response1 = await client.PostAsJsonAsync("/api/appointments", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // 2. Overlapping booking request for same provider -> Rejected 409 Conflict
        var request2 = new
        {
            providerId = drPatel.Id.ToString(),
            locationId = clinic.Id.ToString(),
            startAt = start.AddMinutes(15).ToString("o"),
            endAt = end.AddMinutes(15).ToString("o"),
            reasonForVisit = "Double booking attempt"
        };

        var response2 = await client.PostAsJsonAsync("/api/appointments", request2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        var errDoc = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var error = errDoc.GetProperty("error").GetString();
        Assert.Contains("already has an appointment", error);
    }

    [Fact]
    public async Task Scheduling_Api_RoomDoubleBooking_RejectedWithConflict409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var patientUser = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");
        var patient = await db.Patients.FirstAsync(p => p.PersonId == patientUser.PersonId);
        var drPatel = await db.Providers.FirstAsync(p => p.Email == "apatel@clinic.example");
        var drKim = await db.Providers.FirstAsync(p => p.Email == "jkim@clinic.example");
        var clinic = await db.Locations.FirstAsync(l => l.Name == "Bole Family Health Clinic (DEMO)");
        var room = await db.RoomResources.FirstAsync(r => r.LocationId == clinic.Id);

        // Ensure patient is on care team for drKim as well for this test
        if (!await db.CareTeams.AnyAsync(ct => ct.PatientId == patient.Id && ct.ProviderId == drKim.Id))
        {
            db.CareTeams.Add(new PatientCareTeam { PatientId = patient.Id, ProviderId = drKim.Id, Role = "Specialist" });
            await db.SaveChangesAsync();
        }

        var token = db.CreateAuthenticatedSession(tokenService, patientUser, patientUser.PersonId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        var start = DateTime.UtcNow.Date.AddDays(72).AddHours(14);
        var end = start.AddMinutes(30);

        var existingRoomAppts = await db.Appointments
            .Where(a => a.RoomResourceId == room.Id && a.StartAt >= start.AddHours(-1) && a.EndAt <= end.AddHours(1))
            .ToListAsync();
        if (existingRoomAppts.Count > 0)
        {
            db.Appointments.RemoveRange(existingRoomAppts);
            await db.SaveChangesAsync();
        }

        // 1. First booking uses room with Dr. Patel
        var request1 = new
        {
            providerId = drPatel.Id.ToString(),
            locationId = clinic.Id.ToString(),
            roomResourceId = room.Id.ToString(),
            startAt = start.ToString("o"),
            endAt = end.ToString("o"),
            reasonForVisit = "Exam in room"
        };

        var response1 = await client.PostAsJsonAsync("/api/appointments", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // 2. Second booking with Dr. Kim attempts to book the SAME room at overlapping time -> 409 Conflict
        var request2 = new
        {
            providerId = drKim.Id.ToString(),
            locationId = clinic.Id.ToString(),
            roomResourceId = room.Id.ToString(),
            startAt = start.AddMinutes(15).ToString("o"),
            endAt = end.AddMinutes(15).ToString("o"),
            reasonForVisit = "Conflicting room booking attempt"
        };

        var response2 = await client.PostAsJsonAsync("/api/appointments", request2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        var errDoc = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var error = errDoc.GetProperty("error").GetString();
        Assert.Contains("room or resource is not available", error);
    }

    [Fact]
    public async Task Scheduling_Api_BookingDuringScheduleBlock_RejectedWithConflict409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var patientUser = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");
        var patient = await db.Patients.FirstAsync(p => p.PersonId == patientUser.PersonId);
        var drPatel = await db.Providers.FirstAsync(p => p.Email == "apatel@clinic.example");
        var clinic = await db.Locations.FirstAsync(l => l.Name == "Bole Family Health Clinic (DEMO)");

        // Add a block on day 78
        var blockStart = DateTime.UtcNow.Date.AddDays(78).AddHours(13);
        var blockEnd = blockStart.AddHours(4);
        db.ScheduleBlocks.Add(new ScheduleBlock
        {
            ProviderId = drPatel.Id,
            LocationId = clinic.Id,
            StartAt = blockStart,
            EndAt = blockEnd,
            Reason = "Physician Conference Out-of-Office",
            IsHoliday = false
        });
        await db.SaveChangesAsync();

        var token = db.CreateAuthenticatedSession(tokenService, patientUser, patientUser.PersonId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        var request = new
        {
            providerId = drPatel.Id.ToString(),
            locationId = clinic.Id.ToString(),
            startAt = blockStart.AddHours(1).ToString("o"),
            endAt = blockStart.AddHours(1.5).ToString("o"),
            reasonForVisit = "Should be blocked"
        };

        var response = await client.PostAsJsonAsync("/api/appointments", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var errDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = errDoc.GetProperty("error").GetString();
        Assert.Contains("unavailable during this time", error);
    }

    [Fact]
    public async Task Scheduling_Api_BookingWithAppointmentTypeId_InfersDuration()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedicalPortalDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var patientUser = await db.UserAccounts.Include(u => u.Person).FirstAsync(u => u.Email == "dana@example.com");
        var patient = await db.Patients.FirstAsync(p => p.PersonId == patientUser.PersonId);
        var drPatel = await db.Providers.FirstAsync(p => p.Email == "apatel@clinic.example");
        var clinic = await db.Locations.FirstAsync(l => l.Name == "Bole Family Health Clinic (DEMO)");
        var wellnessType = await db.AppointmentTypes.FirstAsync(at => at.Code == "WELLNESS"); // 45 minutes

        var token = db.CreateAuthenticatedSession(tokenService, patientUser, patientUser.PersonId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Patient-Id", patient.Id.ToString());

        var start = DateTime.UtcNow.Date.AddDays(82).AddHours(10);
        var existing = await db.Appointments
            .Where(a => a.ProviderId == drPatel.Id && a.StartAt >= start.AddHours(-1) && a.EndAt <= start.AddHours(2))
            .ToListAsync();
        if (existing.Count > 0)
        {
            db.Appointments.RemoveRange(existing);
            await db.SaveChangesAsync();
        }

        var request = new
        {
            providerId = drPatel.Id.ToString(),
            locationId = clinic.Id.ToString(),
            appointmentTypeId = wellnessType.Id.ToString(),
            startAt = start.ToString("o"),
            // endAt omitted to test automatic duration inference from AppointmentType
            reasonForVisit = "Annual Wellness"
        };

        var response = await client.PostAsJsonAsync("/api/appointments", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var apptJson = doc.GetProperty("appointment");
        var endAtActual = DateTimeOffset.Parse(apptJson.GetProperty("endAt").GetString()!).UtcDateTime;
        Assert.Equal(start.AddMinutes(45), endAtActual);
        Assert.Equal("WELLNESS", apptJson.GetProperty("appointmentType").GetProperty("code").GetString());
    }
}
