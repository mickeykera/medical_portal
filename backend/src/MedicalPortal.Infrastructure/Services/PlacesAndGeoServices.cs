using MedicalPortal.Core.Domain;
using MedicalPortal.Core.Entities;
using MedicalPortal.Core.Enums;
using MedicalPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalPortal.Infrastructure.Services;

public record PlaceResultDto(
    Guid Id,
    string Name,
    string Type,
    string AddressLine1,
    string City,
    string State,
    string PostalCode,
    string? Phone,
    string? Website,
    string? Hours,
    double Latitude,
    double Longitude,
    double DistanceKm,
    string DistanceLabel);

public record PlacesResponseDto(
    List<PlaceResultDto> Places,
    Dictionary<string, int> Counts,
    double RadiusKm,
    LatLng Origin,
    string Source,
    bool? Fallback = null);

public record ApproximateLocationDto(
    double Latitude,
    double Longitude,
    string Label,
    string Precision,
    string Source,
    string? IpAddress = null,
    bool? IsPublicIp = null,
    string? City = null,
    string? Region = null,
    string? Country = null,
    string? Isp = null);

public record GeocodeResultDto(
    double Latitude,
    double Longitude,
    string Label,
    string Detail);

public interface IPlacesService
{
    Task<PlacesResponseDto> SearchNearbyPlacesAsync(double latitude, double longitude, double radiusKm, string[]? types, string? source = null);
}

public interface IGeocodingService
{
    Task<ApproximateLocationDto> GetApproximateLocationAsync(string? ipAddress, CancellationToken cancellationToken = default);
    ApproximateLocationDto GetApproximateLocation(string? ipAddress);
    List<GeocodeResultDto> SearchPlaces(string query);
}

public class PlacesService : IPlacesService
{
    private readonly MedicalPortalDbContext _db;

    public PlacesService(MedicalPortalDbContext db)
    {
        _db = db;
    }

    public async Task<PlacesResponseDto> SearchNearbyPlacesAsync(double latitude, double longitude, double radiusKm, string[]? types, string? source = null)
    {
        var origin = new LatLng(latitude, longitude);
        var box = GeoLogic.GetBoundingBox(origin, radiusKm);

        var query = _db.Locations
            .AsNoTracking()
            .Where(l => l.Latitude != null && l.Longitude != null &&
                        l.Latitude >= box.MinLat && l.Latitude <= box.MaxLat &&
                        l.Longitude >= box.MinLng && l.Longitude <= box.MaxLng);

        if (types != null && types.Length > 0)
        {
            var parsedTypes = types
                .Select(t => Enum.TryParse<LocationType>(t, true, out var lt) ? (LocationType?)lt : null)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .ToList();

            if (parsedTypes.Count > 0)
            {
                query = query.Where(l => parsedTypes.Contains(l.Type));
            }
        }

        var candidates = await query.ToListAsync();

        var places = new List<PlaceResultDto>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in candidates)
        {
            var distKm = GeoLogic.DistanceKm(origin, new LatLng(c.Latitude!.Value, c.Longitude!.Value));
            if (distKm <= radiusKm)
            {
                var typeStr = c.Type.ToString();
                counts[typeStr] = counts.GetValueOrDefault(typeStr, 0) + 1;

                places.Add(new PlaceResultDto(
                    c.Id,
                    c.Name,
                    typeStr,
                    c.AddressLine1,
                    c.City,
                    c.State,
                    c.PostalCode,
                    c.Phone,
                    c.Website,
                    c.Hours,
                    c.Latitude.Value,
                    c.Longitude.Value,
                    Math.Round(distKm, 1),
                    GeoLogic.DistanceLabel(distKm)
                ));
            }
        }

        places = places.OrderBy(p => p.DistanceKm).ToList();

        return new PlacesResponseDto(places, counts, radiusKm, origin, "directory");
    }
}

public class GeocodingService : IGeocodingService
{
    private readonly IPublicIpService _publicIpService;

    public GeocodingService(IPublicIpService publicIpService)
    {
        _publicIpService = publicIpService;
    }

    public async Task<ApproximateLocationDto> GetApproximateLocationAsync(string? ipAddress, CancellationToken cancellationToken = default)
    {
        var info = await _publicIpService.ResolveIpLocationAsync(ipAddress, cancellationToken);
        var label = !string.IsNullOrWhiteSpace(info.City) && !string.IsNullOrWhiteSpace(info.Region)
            ? $"{info.City}, {info.Region}, {info.Country ?? "ET"}"
            : (info.City ?? "Addis Ababa, Ethiopia");

        return new ApproximateLocationDto(
            Latitude: info.Latitude ?? 9.0054,
            Longitude: info.Longitude ?? 38.7636,
            Label: label,
            Precision: info.IsPublic ? "public-ip" : "default-fallback",
            Source: info.Source,
            IpAddress: info.IpAddress,
            IsPublicIp: info.IsPublic,
            City: info.City,
            Region: info.Region,
            Country: info.Country,
            Isp: info.Isp
        );
    }

    public ApproximateLocationDto GetApproximateLocation(string? ipAddress)
    {
        // Synchronous fallback for non-async callers
        try
        {
            return GetApproximateLocationAsync(ipAddress).GetAwaiter().GetResult();
        }
        catch
        {
            return new ApproximateLocationDto(
                9.0054,
                38.7636,
                "Addis Ababa, Ethiopia",
                "default",
                "fallback",
                ipAddress ?? "127.0.0.1",
                false,
                "Addis Ababa",
                "Addis Ababa",
                "ET",
                "Local Network"
            );
        }
    }

    public List<GeocodeResultDto> SearchPlaces(string query)
    {
        var list = new List<GeocodeResultDto>
        {
            new(9.0054, 38.7636, "Addis Ababa Central", "Addis Ababa, Ethiopia"),
            new(8.9950, 38.7850, "Bole, Addis Ababa", "Bole Subcity, Addis Ababa, Ethiopia"),
            new(9.0110, 38.7620, "Kirkos, Addis Ababa", "Kirkos Subcity, Addis Ababa, Ethiopia"),
            new(9.0300, 38.8000, "Yeka, Addis Ababa", "Yeka Subcity, Addis Ababa, Ethiopia"),
            new(9.0380, 38.7520, "Arada, Addis Ababa", "Arada Subcity, Addis Ababa, Ethiopia"),
            new(9.0120, 38.7380, "Lideta, Addis Ababa", "Lideta Subcity, Addis Ababa, Ethiopia"),
            new(8.9700, 38.7400, "Nifas Silk-Lafto, Addis Ababa", "Nifas Silk-Lafto Subcity, Addis Ababa, Ethiopia"),
            new(9.0200, 38.7100, "Kolfe Keranio, Addis Ababa", "Kolfe Keranio Subcity, Addis Ababa, Ethiopia"),
            new(9.0600, 38.7300, "Gullele, Addis Ababa", "Gullele Subcity, Addis Ababa, Ethiopia"),
            new(9.0350, 38.7350, "Addis Ketema, Addis Ababa", "Addis Ketema Subcity, Addis Ababa, Ethiopia"),
            new(8.9000, 38.7650, "Akaki Kaliti, Addis Ababa", "Akaki Kaliti Subcity, Addis Ababa, Ethiopia"),
            new(9.0150, 38.8350, "Lemi Kura, Addis Ababa", "Lemi Kura Subcity, Addis Ababa, Ethiopia")
        };

        if (string.IsNullOrWhiteSpace(query))
            return list;

        return list
            .Where(x => x.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Detail.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
