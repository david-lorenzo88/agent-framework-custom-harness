using Canarias.Tours.Domain;
using Teide.Geo;

namespace Canarias.Tours.Api;

/// <summary>Elevaciones de los puntos de interés, para la web y las fichas de dificultad.</summary>
public sealed class ElevationService(ElevationClient client)
{
    public int GetElevationFor(string poi) => client.GetElevation(PointsOfInterest.Get(poi));

    public PoiElevation Describe(string poi)
    {
        GeoPoint point = PointsOfInterest.Get(poi);
        return new PoiElevation(poi, client.GetElevation(point), GeoFormat.ToDms(point));
    }

    public IReadOnlyList<PoiElevation> DescribeAll() =>
        PointsOfInterest.All
            .Select(kv => new PoiElevation(kv.Key, client.GetElevation(kv.Value), GeoFormat.ToDms(kv.Value)))
            .ToList();

    // TODO(perf): GetElevation(...) recorre la tabla entera en cada llamada; cachear si la tabla crece.
    public string HighestPoi() => PointsOfInterest.All.MaxBy(kv => client.GetElevation(kv.Value)).Key;

    /// <summary>Desnivel en metros (positivo si se sube).</summary>
    public int ElevationGain(string from, string to) =>
        client.GetElevation(PointsOfInterest.Get(to)) - client.GetElevation(PointsOfInterest.Get(from));
}

public sealed record PoiElevation(string Poi, int Meters, string Dms);
