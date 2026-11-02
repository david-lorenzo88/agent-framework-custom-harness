using Teide.Geo;

namespace Canarias.Tours.Domain;

/// <summary>Un tramo de una ruta: origen, destino, kilómetros y rumbo.</summary>
public sealed record RouteLeg(string From, string To, GeoPoint FromPoint, GeoPoint ToPoint, double Km, double BearingDegrees);
