namespace MedicalPortal.Core.Domain;

public record LatLng(double Latitude, double Longitude);

public record BoundingBox(double MinLat, double MaxLat, double MinLng, double MaxLng);

public static class GeoLogic
{
    private const double EarthRadiusKm = 6371.0;
    private const double KmToMiles = 0.621371;

    public static double DistanceKm(LatLng p1, LatLng p2)
    {
        var dLat = ToRadians(p2.Latitude - p1.Latitude);
        var dLon = ToRadians(p2.Longitude - p1.Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(p1.Latitude)) * Math.Cos(ToRadians(p2.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    public static double DistanceMiles(LatLng p1, LatLng p2) =>
        DistanceKm(p1, p2) * KmToMiles;

    public static string DistanceLabel(double km)
    {
        if (km < 1.0)
            return "<1 km";
        return $"{Math.Round(km, 1):0.#} km";
    }

    public static BoundingBox GetBoundingBox(LatLng origin, double radiusKm)
    {
        var latDelta = radiusKm / 111.0;
        var cosLat = Math.Cos(ToRadians(origin.Latitude));
        var lngDelta = cosLat > 0.0001 ? radiusKm / (111.0 * cosLat) : radiusKm / 111.0;

        return new BoundingBox(
            Math.Max(-90, origin.Latitude - latDelta),
            Math.Min(90, origin.Latitude + latDelta),
            Math.Max(-180, origin.Longitude - lngDelta),
            Math.Min(180, origin.Longitude + lngDelta)
        );
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
