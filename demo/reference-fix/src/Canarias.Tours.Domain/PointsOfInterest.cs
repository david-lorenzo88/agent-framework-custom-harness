using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Domain;

/// <summary>Puntos de interés de Tenerife incluidos en nuestras excursiones.</summary>
public static class PointsOfInterest
{
    public static readonly GeoPoint Teide = new(28.2723, -16.6425);
    public static readonly GeoPoint Masca = new(28.3070, -16.8410);
    public static readonly GeoPoint LosGigantes = new(28.2435, -16.8414);
    public static readonly GeoPoint Garachico = new(28.3736, -16.7636);
    public static readonly GeoPoint LaLaguna = new(28.4874, -16.3159);
    public static readonly GeoPoint PuertoDeLaCruz = new(28.4142, -16.5449);
    public static readonly GeoPoint LosCristianos = new(28.0520, -16.7166);
    public static readonly GeoPoint Anaga = new(28.5333, -16.2167);

    /// <summary>Todos los puntos, indexados por su identificador corto (slug).</summary>
    public static IReadOnlyDictionary<string, GeoPoint> All { get; } = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase)
    {
        ["teide"] = Teide,
        ["masca"] = Masca,
        ["los-gigantes"] = LosGigantes,
        ["garachico"] = Garachico,
        ["la-laguna"] = LaLaguna,
        ["puerto-de-la-cruz"] = PuertoDeLaCruz,
        ["los-cristianos"] = LosCristianos,
        ["anaga"] = Anaga,
    };

    /// <summary>Devuelve el punto de un slug conocido.</summary>
    public static GeoPoint Get(string slug) =>
        All.TryGetValue(slug, out GeoPoint point)
            ? point
            : throw new KeyNotFoundException($"Punto de interés desconocido: '{slug}'.");

    /// <summary>Acepta un slug conocido o coordenadas crudas <c>"lat, lon"</c> enviadas por el cliente.</summary>
    public static GeoPoint Resolve(string slugOrCoordinates) =>
        All.TryGetValue(slugOrCoordinates, out GeoPoint point)
            ? point
            : GeoFormat.Parse(slugOrCoordinates);
}
