using Teide.Geo.Primitives;

namespace Canarias.Tours.Domain;

/// <summary>Ficha de un punto de interés para la web.</summary>
public static class PoiCard
{
    // Antes cada vista llamaba a GeoFormat.ToDms(punto) por su cuenta; ahora se centraliza en Format.
    public static string Format(string slug)
    {
        GeoPoint point = PointsOfInterest.Get(slug);
        return $"{slug}: {point.ToString("dms")}";
    }
}
