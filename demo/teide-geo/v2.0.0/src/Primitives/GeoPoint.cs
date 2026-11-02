using System.Globalization;

namespace Teide.Geo.Primitives;

/// <summary>Punto geográfico en grados decimales (WGS84).</summary>
/// <param name="Latitude">Latitud en grados, positiva al norte.</param>
/// <param name="Longitude">Longitud en grados, positiva al este.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude) : IFormattable
{
    /// <summary>
    /// Formatea el punto. Formatos: <c>"dms"</c> (grados/minutos/segundos, sustituye a GeoFormat.ToDms)
    /// y <c>"G"</c> (decimal, <c>"28.2723, -16.6425"</c>).
    /// </summary>
    public string ToString(string? format) => this.ToString(format, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => format switch
    {
        null or "" => this.ToString(),
        "dms" or "DMS" => DmsFormatter.Format(this),
        "G" or "g" => string.Create(CultureInfo.InvariantCulture, $"{this.Latitude}, {this.Longitude}"),
        _ => throw new FormatException($"Formato '{format}' no soportado. Usa \"dms\" o \"G\"."),
    };
}
