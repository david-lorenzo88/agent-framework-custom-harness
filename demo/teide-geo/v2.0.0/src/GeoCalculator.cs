using Teide.Geo.Primitives;

namespace Teide.Geo;

/// <summary>Cálculos sobre la esfera terrestre.</summary>
public static class GeoCalculator
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Distancia haversine entre dos puntos, en <b>metros</b> (cambio incompatible en 2.0.0).</summary>
    public static double Distance(GeoPoint a, GeoPoint b) => Distance(a, b, DistanceUnit.Meters);

    /// <summary>Distancia haversine entre dos puntos en la unidad indicada.</summary>
    public static double Distance(GeoPoint a, GeoPoint b, DistanceUnit unit)
    {
        double dLat = ToRadians(b.Latitude - a.Latitude);
        double dLon = ToRadians(b.Longitude - a.Longitude);
        double h = Math.Pow(Math.Sin(dLat / 2), 2)
                 + Math.Cos(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        double meters = 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(h));
        return unit == DistanceUnit.Kilometers ? meters / 1000 : meters;
    }

    /// <summary>Rumbo inicial de <paramref name="a"/> hacia <paramref name="b"/>, en grados (0–360).</summary>
    public static double Bearing(GeoPoint a, GeoPoint b)
    {
        double lat1 = ToRadians(a.Latitude);
        double lat2 = ToRadians(b.Latitude);
        double dLon = ToRadians(b.Longitude - a.Longitude);
        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return (ToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;
}
