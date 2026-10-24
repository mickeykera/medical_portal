namespace MedicalPortal.Core.Domain;

public static class SchedulingLogic
{
    public const int MaxBookingDaysAhead = 180;

    public record TimeSlot(DateTime StartAt, DateTime EndAt);

    public record SlotCheckResult(bool Ok, string? Problem = null);

    public record ScheduleTemplateRule(
        DayOfWeek DayOfWeek,
        TimeSpan StartTime,
        TimeSpan EndTime,
        bool IsActive = true,
        Guid? ProviderId = null,
        Guid? LocationId = null);

    public record BlockRule(
        DateTime StartAtUtc,
        DateTime EndAtUtc,
        string? Reason = null,
        bool IsHoliday = false,
        Guid? ProviderId = null,
        Guid? LocationId = null);

    public record ComputedSlot(
        DateTime StartAt,
        DateTime EndAt,
        string LocalStart,
        string LocalEnd);

    public const string DefaultTimeZoneId = "Africa/Addis_Ababa";

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var tzId = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tzId, out var winId))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(winId); } catch { }
            }
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    public static DateTime ConvertLocalToUtc(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(unspecified))
        {
            // During spring forward transition, clocks jump forward (e.g. 2:00 -> 3:00).
            // Shift past the transition gap by 1 hour.
            unspecified = unspecified.AddHours(1);
        }

        if (timeZone.IsAmbiguousTime(unspecified))
        {
            // During fall back transition, clocks repeat an hour.
            // Pick the standard time offset (smaller UTC offset).
            var offsets = timeZone.GetAmbiguousTimeOffsets(unspecified);
            var standardOffset = offsets.Min();
            return DateTime.SpecifyKind(unspecified - standardOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }

    public static DateTime ConvertUtcToLocal(DateTime utcDateTime, TimeZoneInfo timeZone)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
    }

    public static bool WithinBookingWindow(DateTime startAt, DateTime? now = null)
    {
        var current = now ?? DateTime.UtcNow;
        if (startAt <= current) return false;
        var limit = current.AddDays(MaxBookingDaysAhead);
        return startAt <= limit;
    }

    public static bool HasOverlap(TimeSlot candidate, IEnumerable<TimeSlot> slots)
    {
        return slots.Any(s => candidate.StartAt < s.EndAt && candidate.EndAt > s.StartAt);
    }

    public static SlotCheckResult CheckSlot(TimeSlot candidate, IEnumerable<TimeSlot> busySlots)
    {
        return CheckSlot(candidate, busySlots, roomBusySlots: null, blockedSlots: null);
    }

    public static SlotCheckResult CheckSlot(
        TimeSlot candidate,
        IEnumerable<TimeSlot> busySlots,
        IEnumerable<TimeSlot>? roomBusySlots = null,
        IEnumerable<TimeSlot>? blockedSlots = null)
    {
        if (candidate.EndAt <= candidate.StartAt)
            return new(false, "end_before_start");

        var duration = (candidate.EndAt - candidate.StartAt).TotalMinutes;
        if (duration < 10)
            return new(false, "duration_too_short");
        if (duration > 120)
            return new(false, "duration_too_long");

        if (blockedSlots != null && HasOverlap(candidate, blockedSlots))
            return new(false, "blocked_time");

        if (HasOverlap(candidate, busySlots))
            return new(false, "conflicts_with_existing");

        if (roomBusySlots != null && HasOverlap(candidate, roomBusySlots))
            return new(false, "room_unavailable");

        return new(true);
    }

    public static string DescribeSlotProblem(string problem) => problem switch
    {
        "end_before_start" => "End time must be after the start time.",
        "duration_too_short" => "Appointments must be at least 10 minutes long.",
        "duration_too_long" => "Appointments cannot exceed 2 hours.",
        "conflicts_with_existing" => "That provider already has an appointment during this time.",
        "room_unavailable" => "The requested room or resource is not available for this time.",
        "blocked_time" => "The clinic or provider is unavailable during this time (holiday or blocked time).",
        _ => "The requested time slot is not available."
    };

    public static (int StartHour, int EndHour)? HoursForDay(DateTime date)
    {
        // Monday (1) to Friday (5): 8:00 to 17:00
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return null;
        return (8, 17);
    }

    public static List<(TimeSpan Start, TimeSpan End)> GetOperatingWindowsForDay(
        DayOfWeek dayOfWeek,
        IEnumerable<ScheduleTemplateRule>? templates,
        Guid? providerId = null,
        Guid? locationId = null)
    {
        var windows = new List<(TimeSpan Start, TimeSpan End)>();
        if (templates != null)
        {
            var activeTemplates = templates.Where(t => t.IsActive && t.DayOfWeek == dayOfWeek).ToList();

            // Priority 1: Provider + Location specific match
            if (providerId.HasValue && locationId.HasValue)
            {
                var exact = activeTemplates.Where(t => t.ProviderId == providerId && t.LocationId == locationId).ToList();
                if (exact.Count > 0)
                {
                    return exact.Select(t => (t.StartTime, t.EndTime)).ToList();
                }
            }

            // Priority 2: Provider-specific match
            if (providerId.HasValue)
            {
                var prov = activeTemplates.Where(t => t.ProviderId == providerId).ToList();
                if (prov.Count > 0)
                {
                    return prov.Select(t => (t.StartTime, t.EndTime)).ToList();
                }
            }

            // Priority 3: Location-specific match
            if (locationId.HasValue)
            {
                var loc = activeTemplates.Where(t => t.LocationId == locationId && t.ProviderId == null).ToList();
                if (loc.Count > 0)
                {
                    return loc.Select(t => (t.StartTime, t.EndTime)).ToList();
                }
            }

            // Priority 4: Generic templates
            var generic = activeTemplates.Where(t => t.ProviderId == null && t.LocationId == null).ToList();
            if (generic.Count > 0)
            {
                return generic.Select(t => (t.StartTime, t.EndTime)).ToList();
            }
        }

        // Fallback default: 08:00 to 17:00 on weekdays
        if (dayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
        {
            windows.Add((new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
        }

        return windows;
    }

    public static List<TimeSlot> FindSlots(
        DateTime day,
        (int StartHour, int EndHour) window,
        IEnumerable<TimeSlot> busySlots,
        int durationMinutes = 30,
        DateTime? now = null)
    {
        var current = now ?? DateTime.UtcNow;
        var slots = new List<TimeSlot>();
        var dayDate = day.Date;
        var start = dayDate.AddHours(window.StartHour);
        var end = dayDate.AddHours(window.EndHour);

        var busyList = busySlots.ToList();

        var cursor = start;
        while (cursor.AddMinutes(durationMinutes) <= end)
        {
            var candidate = new TimeSlot(cursor, cursor.AddMinutes(durationMinutes));
            if (candidate.StartAt > current)
            {
                var check = CheckSlot(candidate, busyList);
                if (check.Ok)
                {
                    slots.Add(candidate);
                }
            }
            cursor = cursor.AddMinutes(durationMinutes);
        }

        return slots;
    }

    public static List<ComputedSlot> FindSlotsInLocationTimeZone(
        DateTime localDayDate,
        TimeZoneInfo locationTimeZone,
        IEnumerable<ScheduleTemplateRule>? templates,
        IEnumerable<TimeSlot> busySlotsUtc,
        IEnumerable<BlockRule>? blockedSlotsUtc = null,
        IEnumerable<TimeSlot>? roomBusySlotsUtc = null,
        int durationMinutes = 30,
        Guid? providerId = null,
        Guid? locationId = null,
        DateTime? nowUtc = null)
    {
        var currentUtc = nowUtc ?? DateTime.UtcNow;
        var results = new List<ComputedSlot>();

        var windows = GetOperatingWindowsForDay(localDayDate.DayOfWeek, templates, providerId, locationId);
        if (windows.Count == 0) return results;

        var busyList = busySlotsUtc.ToList();
        var blockedList = blockedSlotsUtc?.Select(b => new TimeSlot(b.StartAtUtc, b.EndAtUtc)).ToList() ?? new List<TimeSlot>();
        var roomBusyList = roomBusySlotsUtc?.ToList();

        var localDateOnly = localDayDate.Date;

        foreach (var window in windows)
        {
            var cursor = window.Start;
            while (cursor.Add(TimeSpan.FromMinutes(durationMinutes)) <= window.End)
            {
                var localStart = localDateOnly + cursor;
                var localEnd = localStart.AddMinutes(durationMinutes);

                var utcStart = ConvertLocalToUtc(localStart, locationTimeZone);
                var utcEnd = ConvertLocalToUtc(localEnd, locationTimeZone);

                var candidate = new TimeSlot(utcStart, utcEnd);

                if (candidate.StartAt > currentUtc && WithinBookingWindow(candidate.StartAt, currentUtc))
                {
                    var check = CheckSlot(candidate, busyList, roomBusyList, blockedList);
                    if (check.Ok)
                    {
                        results.Add(new ComputedSlot(
                            candidate.StartAt,
                            candidate.EndAt,
                            localStart.ToString("HH:mm"),
                            localEnd.ToString("HH:mm")));
                    }
                }

                cursor = cursor.Add(TimeSpan.FromMinutes(durationMinutes));
            }
        }

        return results;
    }
}
