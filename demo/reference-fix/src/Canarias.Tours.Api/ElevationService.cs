using Canarias.Tours.Domain;
using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Api;

/// <summary>Elevaciones de los puntos de interés, para la web y las fichas de dificultad.</summary>
public sealed class ElevationService(ElevationClient client)
{
    public Task<int> GetElevationForAsync(string poi, CancellationToken cancellationToken = default) =>
        client.GetElevationAsync(PointsOfInterest.Get(poi), cancellationToken);

    public async Task<PoiElevation> DescribeAsync(string poi, CancellationToken cancellationToken = default)
    {
        GeoPoint point = PointsOfInterest.Get(poi);
        return new PoiElevation(poi, await client.GetElevationAsync(point, cancellationToken), point.ToString("dms"));
    }

    public async Task<IReadOnlyList<PoiElevation>> DescribeAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<PoiElevation>();
        foreach (var (slug, point) in PointsOfInterest.All)
        {
            result.Add(new PoiElevation(slug, await client.GetElevationAsync(point, cancellationToken), point.ToString("dms")));
        }

        return result;
    }

    // TODO(perf): GetElevation(...) recorre la tabla entera en cada llamada; cachear si la tabla crece.
    public async Task<string> HighestPoiAsync(CancellationToken cancellationToken = default)
    {
        var elevations = await this.DescribeAllAsync(cancellationToken);
        return elevations.MaxBy(e => e.Meters)!.Poi;
    }

    /// <summary>Desnivel en metros (positivo si se sube).</summary>
    public async Task<int> ElevationGainAsync(string from, string to, CancellationToken cancellationToken = default) =>
        await client.GetElevationAsync(PointsOfInterest.Get(to), cancellationToken)
        - await client.GetElevationAsync(PointsOfInterest.Get(from), cancellationToken);
}

public sealed record PoiElevation(string Poi, int Meters, string Dms);
