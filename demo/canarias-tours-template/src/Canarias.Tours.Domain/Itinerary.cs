using Teide.Geo;

namespace Canarias.Tours.Domain;

/// <summary>Resultado del planificador: tramos en orden de visita.</summary>
public sealed class Itinerary
{
    public Itinerary(IReadOnlyList<RouteLeg> legs) => this.Legs = legs;

    public IReadOnlyList<RouteLeg> Legs { get; }

    /// <summary>Kilómetros totales del itinerario.</summary>
    public double TotalKm => this.Legs.Sum(leg => leg.Km);

    /// <summary>Una línea por tramo, con el destino en formato DMS para el guía.</summary>
    public IEnumerable<string> Describe() =>
        this.Legs.Select(leg =>
            $"{leg.From} → {leg.To}: {leg.Km:0.0} km, rumbo {Compass.ToPoint(leg.BearingDegrees)} (destino {GeoFormat.ToDms(leg.ToPoint)})");

    /// <summary>Coordenadas de salida y llegada en DMS, para el PDF del bono.</summary>
    public (string Start, string End) Endpoints() =>
        this.Legs.Count == 0
            ? (string.Empty, string.Empty)
            : (GeoFormat.ToDms(this.Legs[0].FromPoint), GeoFormat.ToDms(this.Legs[^1].ToPoint));
}
