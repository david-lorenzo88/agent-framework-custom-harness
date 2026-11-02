using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Domain;

/// <summary>Convierte rumbos en puntos cardinales para los guías.</summary>
public static class Compass
{
    private static readonly string[] Points = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];

    public static string ToPoint(double bearingDegrees) => Points[(int)Math.Round(bearingDegrees / 45.0) % 8];

    /// <summary>Punto cardinal hacia el que hay que salir de <paramref name="from"/> para llegar a <paramref name="to"/>.</summary>
    public static string Heading(GeoPoint from, GeoPoint to) => ToPoint(GeoCalculator.Bearing(from, to));
}
