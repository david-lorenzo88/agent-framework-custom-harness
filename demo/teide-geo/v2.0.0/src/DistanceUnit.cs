namespace Teide.Geo;

/// <summary>Unidad de distancia para <see cref="GeoCalculator.Distance(Primitives.GeoPoint, Primitives.GeoPoint, DistanceUnit)"/>.</summary>
public enum DistanceUnit
{
    /// <summary>Metros (la unidad por defecto desde 2.0.0).</summary>
    Meters,

    /// <summary>Kilómetros (la unidad por defecto en 1.x).</summary>
    Kilometers,
}
