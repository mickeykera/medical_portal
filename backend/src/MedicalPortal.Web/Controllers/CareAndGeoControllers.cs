using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using MedicalPortal.Infrastructure.Services;
using MedicalPortal.Web.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Web.Controllers;

[ApiController]
[Route("api/care")]
[RequireAuth]
[RequireModule("findcare")]
[EnableRateLimiting("GeneralApiPolicy")]
public class CareController : ControllerBase
{
    private readonly MedicalPortalDbContext _db;
    private readonly IPlacesService _placesService;

    public CareController(MedicalPortalDbContext db, IPlacesService placesService)
    {
        _db = db;
        _placesService = placesService;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchCare(
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] string? specialty,
        [FromQuery] string? name,
        [FromQuery] bool includeUnknownDistance = false)
    {
        if (latitude.HasValue != longitude.HasValue)
            return BadRequest(new { error = "Provide both latitude and longitude, or neither." });

        LatLng? origin = (latitude.HasValue && longitude.HasValue) ? new LatLng(latitude.Value, longitude.Value) : null;

        var query = _db.Providers
            .AsNoTracking()
            .Include(p => p.Locations).ThenInclude(pl => pl.Location)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(specialty))
        {
            query = query.Where(p => EF.Functions.Like(p.Specialty, $"%{specialty}%"));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(p => EF.Functions.Like(p.FirstName, $"%{name}%") || EF.Functions.Like(p.LastName, $"%{name}%"));
        }

        var providers = await query.OrderBy(p => p.LastName).ToListAsync();

        var results = providers.Select(p =>
        {
            var locs = p.Locations.Select(pl =>
            {
                var loc = pl.Location;
                var hasCoords = loc.Latitude.HasValue && loc.Longitude.HasValue;
                double? km = (origin != null && hasCoords)
                    ? GeoLogic.DistanceKm(origin, new LatLng(loc.Latitude!.Value, loc.Longitude!.Value))
                    : null;

                return new
                {
                    locationId = loc.Id,
                    name = loc.Name,
                    addressLine1 = loc.AddressLine1,
                    city = loc.City,
                    state = loc.State,
                    postalCode = loc.PostalCode,
                    phone = loc.Phone,
                    distanceKm = km.HasValue ? Math.Round(km.Value, 1) : (double?)null,
                    distanceMiles = km.HasValue ? Math.Round(GeoLogic.DistanceMiles(origin!, new LatLng(loc.Latitude!.Value, loc.Longitude!.Value)), 1) : (double?)null,
                    distanceLabel = km.HasValue ? GeoLogic.DistanceLabel(km.Value) : null
                };
            }).ToList();

            if (origin != null)
            {
                locs = locs.OrderBy(l => l.distanceKm.HasValue ? l.distanceKm.Value : 999999).ToList();
            }

            return new
            {
                providerId = p.Id,
                name = $"Dr. {p.FirstName} {p.LastName}",
                credentials = p.Credentials,
                specialty = p.Specialty,
                locations = includeUnknownDistance ? locs : locs.Where(l => origin == null || l.distanceKm != null).ToList()
            };
        }).Where(p => origin == null || p.locations.Count > 0).ToList();

        return Ok(new { providers = results, originProvided = origin != null });
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(
        [FromQuery] Guid providerId,
        [FromQuery] Guid? locationId = null,
        [FromQuery] Guid? appointmentTypeId = null,
        [FromQuery] Guid? roomResourceId = null,
        [FromQuery] int days = 7,
        [FromQuery] int durationMinutes = 30)
    {
        var provider = await _db.Providers.FindAsync(providerId);
        if (provider == null)
            return NotFound(new { error = "Provider not found." });

        if (appointmentTypeId.HasValue)
        {
            var apptType = await _db.AppointmentTypes.FindAsync(appointmentTypeId.Value);
            if (apptType != null && apptType.IsActive)
            {
                durationMinutes = apptType.DurationMinutes;
            }
        }

        days = Math.Clamp(days, 1, 14);
        durationMinutes = Math.Clamp(durationMinutes, 10, 120);

        Location? location = null;
        if (locationId.HasValue)
        {
            location = await _db.Locations.FindAsync(locationId.Value);
        }
        else
        {
            var provLoc = await _db.ProviderLocations
                .Include(pl => pl.Location)
                .FirstOrDefaultAsync(pl => pl.ProviderId == providerId);
            location = provLoc?.Location;
        }

        var tz = SchedulingLogic.ResolveTimeZone(location?.TimeZoneId);

        var now = DateTime.UtcNow;
        var until = now.AddDays(days);

        var busyAppts = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.ProviderId == providerId &&
                        a.StartAt <= until && a.EndAt >= now &&
                        a.Status != AppointmentStatus.CANCELLED_BY_PATIENT &&
                        a.Status != AppointmentStatus.CANCELLED_BY_CLINIC)
            .Select(a => new SchedulingLogic.TimeSlot(a.StartAt, a.EndAt))
            .ToListAsync();

        List<SchedulingLogic.TimeSlot>? roomBusyAppts = null;
        if (roomResourceId.HasValue)
        {
            roomBusyAppts = await _db.Appointments
                .AsNoTracking()
                .Where(a => a.RoomResourceId == roomResourceId.Value &&
                            a.StartAt <= until && a.EndAt >= now &&
                            a.Status != AppointmentStatus.CANCELLED_BY_PATIENT &&
                            a.Status != AppointmentStatus.CANCELLED_BY_CLINIC)
                .Select(a => new SchedulingLogic.TimeSlot(a.StartAt, a.EndAt))
                .ToListAsync();
        }

        var templates = await _db.WeeklyScheduleTemplates
            .AsNoTracking()
            .Where(t => t.IsActive &&
                        (t.ProviderId == providerId || t.ProviderId == null) &&
                        (location == null || t.LocationId == location.Id || t.LocationId == null))
            .Select(t => new SchedulingLogic.ScheduleTemplateRule(
                t.DayOfWeek,
                t.StartTime,
                t.EndTime,
                t.IsActive,
                t.ProviderId,
                t.LocationId))
            .ToListAsync();

        var blocks = await _db.ScheduleBlocks
            .AsNoTracking()
            .Where(b => ((b.ProviderId == providerId) || (location != null && b.LocationId == location.Id)) &&
                        b.EndAt >= now && b.StartAt <= until)
            .Select(b => new SchedulingLogic.BlockRule(
                b.StartAt,
                b.EndAt,
                b.Reason,
                b.IsHoliday,
                b.ProviderId,
                b.LocationId))
            .ToListAsync();

        var daysOutput = new List<object>();
        var localNow = SchedulingLogic.ConvertUtcToLocal(now, tz);

        for (int offset = 0; offset < days; offset++)
        {
            var localDay = localNow.Date.AddDays(offset);
            var daySlots = SchedulingLogic.FindSlotsInLocationTimeZone(
                localDay,
                tz,
                templates,
                busyAppts,
                blocks,
                roomBusyAppts,
                durationMinutes,
                providerId,
                location?.Id,
                now);

            if (daySlots.Count > 0)
            {
                daysOutput.Add(new
                {
                    date = localDay.ToString("yyyy-MM-dd"),
                    slots = daySlots.Select(s => new
                    {
                        startAt = s.StartAt.ToString("o"),
                        endAt = s.EndAt.ToString("o"),
                        localStart = s.LocalStart,
                        localEnd = s.LocalEnd
                    })
                });
            }
        }

        return Ok(new
        {
            providerId,
            locationId = location?.Id,
            timeZone = tz.Id,
            durationMinutes,
            days = daysOutput
        });
    }

    [HttpGet("places")]
    public async Task<IActionResult> GetPlaces(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        [FromQuery] double radiusKm = 10,
        [FromQuery] string? type = null,
        [FromQuery] string? source = null)
    {
        var types = !string.IsNullOrEmpty(type) ? type.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
        var res = await _placesService.SearchNearbyPlacesAsync(latitude, longitude, radiusKm, types, source);
        return Ok(res);
    }
}

[ApiController]
[RequireAuth]
public class FacilitiesController : ControllerBase
{
    private readonly IPlacesService _placesService;

    public FacilitiesController(IPlacesService placesService)
    {
        _placesService = placesService;
    }

    [HttpGet("api/facilities/nearby")]
    public async Task<IActionResult> GetNearbyFacilities(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radius = 10,
        [FromQuery] string? type = null,
        [FromQuery] string? source = null)
    {
        var types = !string.IsNullOrEmpty(type) ? type.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
        var res = await _placesService.SearchNearbyPlacesAsync(lat, lng, radius, types, source);
        return Ok(res);
    }
}

[ApiController]
[Route("api/geo")]
[RequireAuth]
public class GeoController : ControllerBase
{
    private readonly IGeocodingService _geocoding;
    private readonly IPublicIpService _publicIpService;

    public GeoController(IGeocodingService geocoding, IPublicIpService publicIpService)
    {
        _geocoding = geocoding;
        _publicIpService = publicIpService;
    }

    [HttpGet("approximate")]
    public async Task<IActionResult> ApproximateLocation()
    {
        var clientIp = _publicIpService.GetClientIpAddress(HttpContext);
        var loc = await _geocoding.GetApproximateLocationAsync(clientIp);
        return Ok(loc);
    }

    [HttpGet("search")]
    public IActionResult SearchPlaces([FromQuery] string? q)
    {
        var results = _geocoding.SearchPlaces(q ?? "");
        return Ok(new { results });
    }
}

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly IPublicIpService _publicIpService;
    private readonly MedicalPortalDbContext _db;

    public SystemController(IPublicIpService publicIpService, MedicalPortalDbContext db)
    {
        _publicIpService = publicIpService;
        _db = db;
    }

    [HttpGet("ip")]
    public async Task<IActionResult> GetIpDetails()
    {
        var details = await _publicIpService.GetClientIpDetailsAsync(HttpContext);
        return Ok(details);
    }

    [HttpGet("db")]
    public async Task<IActionResult> GetDatabaseInfo()
    {
        var provider = _db.Database.ProviderName ?? "Unknown";
        var canConnect = await _db.Database.CanConnectAsync();

        return Ok(new
        {
            provider = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "PostgreSQL" :
                       provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "SQLite" : provider,
            canConnect,
            serverUtc = DateTime.UtcNow
        });
    }
}
