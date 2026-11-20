using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Payments;

/// <summary>Precio de excursiones con varias paradas.</summary>
public sealed class TourPackagePricing
{
    /// <summary>Una única bajada de bandera más el precio por kilómetro de todos los tramos.</summary>
    public decimal QuoteMultiStop(IReadOnlyList<GeoPoint> stops)
    {
        double totalKm = 0;
        for (int i = 1; i < stops.Count; i++)
        {
            totalKm += GeoCalculator.Distance(stops[i - 1], stops[i]);
        }

        return FareCalculator.Round(FareCalculator.BaseFare + FareCalculator.PerKmRate * (decimal)totalKm);
    }

    /// <summary>Ida y vuelta: dos tarifas sencillas.</summary>
    public decimal QuoteRoundTrip(GeoPoint from, GeoPoint to)
    {
        var fares = new FareCalculator();
        return fares.Quote(from, to) + fares.Quote(to, from);
    }
}
