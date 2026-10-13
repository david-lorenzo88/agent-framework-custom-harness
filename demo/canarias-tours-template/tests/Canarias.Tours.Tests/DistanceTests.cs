using Canarias.Tours.Domain;

namespace Canarias.Tours.Tests;

/// <summary>El planificador trabaja en kilómetros.</summary>
public class DistanceTests
{
    private readonly RoutePlanner _planner = new();

    [Fact]
    public void DistanceKm_LaLagunaToPuertoDeLaCruz_IsAbout24Km()
    {
        double km = this._planner.DistanceKm("la-laguna", "puerto-de-la-cruz");
        Assert.InRange(km, 23.5, 24.1);
    }

    [Fact]
    public void Plan_TeideToMascaAndGarachico_TotalIsAbout27Km()
    {
        Itinerary itinerary = this._planner.Plan("teide", ["masca", "garachico"]);
        Assert.InRange(itinerary.TotalKm, 26.5, 27.5);
    }

    [Fact]
    public void Plan_VisitsNearestStopFirst()
    {
        Itinerary itinerary = this._planner.Plan("teide", ["masca", "garachico"]);
        Assert.Equal("garachico", itinerary.Legs[0].To);
        Assert.Equal("masca", itinerary.Legs[1].To);
    }

    [Fact]
    public void Plan_CreatesOneLegPerStop()
    {
        Itinerary itinerary = this._planner.Plan("la-laguna", ["anaga", "puerto-de-la-cruz", "teide"]);
        Assert.Equal(3, itinerary.Legs.Count);
    }

    [Fact]
    public void Compass_LaLagunaToAnaga_IsNorthEast()
    {
        Assert.Equal("NE", Compass.Heading(PointsOfInterest.LaLaguna, PointsOfInterest.Anaga));
    }

    [Fact]
    public void Compass_TeideToMasca_IsWest()
    {
        Assert.Equal("O", Compass.Heading(PointsOfInterest.Teide, PointsOfInterest.Masca));
    }
}
