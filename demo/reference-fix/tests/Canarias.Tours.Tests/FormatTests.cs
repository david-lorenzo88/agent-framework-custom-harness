using Canarias.Tours.Domain;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Tests;

/// <summary>Formato DMS y resolución de puntos de interés.</summary>
public class FormatTests
{
    [Fact]
    public void PoiCard_Teide_ShowsDms()
    {
        Assert.Equal("teide: 28°16'20\"N 16°38'33\"W", PoiCard.Format("teide"));
    }

    [Fact]
    public void Itinerary_Describe_ShowsDestinationInDms()
    {
        Itinerary itinerary = new RoutePlanner().Plan("teide", ["garachico"]);
        string line = Assert.Single(itinerary.Describe());
        Assert.Contains("28°22'25\"N 16°45'49\"W", line);
    }

    [Fact]
    public void Itinerary_Endpoints_AreDms()
    {
        Itinerary itinerary = new RoutePlanner().Plan("teide", ["masca", "garachico"]);
        Assert.Equal(("28°16'20\"N 16°38'33\"W", "28°18'25\"N 16°50'28\"W"), itinerary.Endpoints());
    }

    [Fact]
    public void PointsOfInterest_HasEightPlaces()
    {
        Assert.Equal(8, PointsOfInterest.All.Count);
    }

    [Fact]
    public void PointsOfInterest_UnknownSlug_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => PointsOfInterest.Get("lanzarote"));
    }

    [Fact]
    public void Resolve_AcceptsRawCoordinates()
    {
        Assert.Equal(new GeoPoint(28.1, -16.5), PointsOfInterest.Resolve("28.1, -16.5"));
    }
}
