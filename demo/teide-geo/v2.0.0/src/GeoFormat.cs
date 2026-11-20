using System.Globalization;
using Teide.Geo.Primitives;

namespace Teide.Geo;

/// <summary>Parseo de coordenadas. (<c>ToDms</c> se eliminó en 2.0.0: usa <c>GeoPoint.ToString("dms")</c>.)</summary>
public static class GeoFormat
{
    /// <summary>Convierte <c>"lat, lon"</c> en un <see cref="GeoPoint"/>. Sin regex (corrige TEIDE-2026-0042).</summary>
    public static GeoPoint Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 64)
        {
            throw new FormatException("Coordenadas demasiado largas.");
        }

        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
        {
            throw new FormatException($"Coordenadas no válidas: '{text}'.");
        }

        return new GeoPoint(lat, lon);
    }
}
