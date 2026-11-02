namespace Teide.Geo;

/// <summary>Punto geográfico en grados decimales (WGS84).</summary>
/// <param name="Latitude">Latitud en grados, positiva al norte.</param>
/// <param name="Longitude">Longitud en grados, positiva al este.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);
