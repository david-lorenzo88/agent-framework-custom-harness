using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Domain;

/// <summary>Planifica rutas entre puntos de interés con la heurística del vecino más cercano.</summary>
public sealed class RoutePlanner
{
    /// <summary>Ordena las paradas visitando siempre la más cercana y devuelve el itinerario.</summary>
    public Itinerary Plan(string start, IEnumerable<string> stops)
    {
        var current = (Name: start, Point: PointsOfInterest.Get(start));
        var remaining = stops.Select(s => (Name: s, Point: PointsOfInterest.Get(s))).ToList();
        var legs = new List<RouteLeg>();

        while (remaining.Count > 0)
        {
            var next = remaining.MinBy(r => GeoCalculator.Distance(current.Point, r.Point));
            legs.Add(new RouteLeg(
                current.Name,
                next.Name,
                current.Point,
                next.Point,
                Km: GeoCalculator.Distance(current.Point, next.Point),
                BearingDegrees: GeoCalculator.Bearing(current.Point, next.Point)));
            remaining.Remove(next);
            current = next;
        }

        return new Itinerary(legs);
    }

    /// <summary>Distancia directa en kilómetros entre dos puntos de interés.</summary>
    public double DistanceKm(string from, string to) =>
        GeoCalculator.Distance(PointsOfInterest.Get(from), PointsOfInterest.Get(to));
}
