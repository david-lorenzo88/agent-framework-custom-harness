using Teide.Geo.Primitives;

namespace Teide.Geo;

/// <summary>Elevaciones aproximadas a partir de una tabla offline de lugares conocidos.</summary>
public sealed class ElevationClient
{
    // Tabla offline: lugar conocido → elevación en metros.
    private static readonly (GeoPoint Point, int Meters)[] Table =
    [
        (new GeoPoint(28.2723, -16.6425), 3715), // Pico del Teide
        (new GeoPoint(28.3070, -16.8410), 650),  // Masca
        (new GeoPoint(28.2435, -16.8414), 25),   // Los Gigantes
        (new GeoPoint(28.3736, -16.7636), 10),   // Garachico
        (new GeoPoint(28.4874, -16.3159), 550),  // San Cristóbal de La Laguna
        (new GeoPoint(28.4142, -16.5449), 9),    // Puerto de la Cruz
        (new GeoPoint(28.0520, -16.7166), 5),    // Los Cristianos
        (new GeoPoint(28.5333, -16.2167), 1024), // Macizo de Anaga (Cruz del Carmen)
    ];

    /// <summary>
    /// Elevación en metros del lugar conocido más cercano (0 si no hay ninguno a menos de 5 km).
    /// Sustituye a <c>GetElevation</c> (eliminado en 2.0.0).
    /// </summary>
    public Task<int> GetElevationAsync(GeoPoint point, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nearest = Table.MinBy(entry => GeoCalculator.Distance(entry.Point, point, DistanceUnit.Kilometers));
        int meters = GeoCalculator.Distance(nearest.Point, point, DistanceUnit.Kilometers) <= 5 ? nearest.Meters : 0;
        return Task.FromResult(meters);
    }
}
