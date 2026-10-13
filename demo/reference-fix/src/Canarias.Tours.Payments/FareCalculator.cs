using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Payments;

/// <summary>Tarifas de traslado en euros. Propiedad del equipo de pagos (@maria-payments).</summary>
public sealed class FareCalculator
{
    /// <summary>Bajada de bandera, en euros.</summary>
    public const decimal BaseFare = 3.00m;

    /// <summary>Precio por kilómetro, en euros.</summary>
    public const decimal PerKmRate = 1.20m;

    /// <summary>A partir de esta distancia el traslado se considera de larga distancia.</summary>
    public const double LongDistanceKm = 40;

    // Nota: GeoCalculator.Distance devuelve kilómetros, por eso PerKmRate va en €/km.
    public decimal Quote(GeoPoint from, GeoPoint to)
    {
        double km = GeoCalculator.Distance(from, to);
        return Round(BaseFare + PerKmRate * (decimal)km);
    }

    /// <summary>Desglose para el ticket: kilómetros, bajada de bandera, importe por distancia y total.</summary>
    public FareBreakdown Breakdown(GeoPoint from, GeoPoint to)
    {
        double km = GeoCalculator.Distance(from, to);
        decimal distanceCharge = Round(PerKmRate * (decimal)km);
        return new FareBreakdown(Math.Round(km, 1), BaseFare, distanceCharge, BaseFare + distanceCharge);
    }

    /// <summary>Los traslados largos llevan un suplemento de conductor.</summary>
    public bool IsLongDistance(GeoPoint from, GeoPoint to) => GeoCalculator.Distance(from, to) > LongDistanceKm;

    internal static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Desglose de una tarifa.</summary>
public sealed record FareBreakdown(double Km, decimal BaseFare, decimal DistanceCharge, decimal Total);
