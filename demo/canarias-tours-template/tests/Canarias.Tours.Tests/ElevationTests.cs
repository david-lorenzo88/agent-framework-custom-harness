using Canarias.Tours.Api;
using Teide.Geo;

namespace Canarias.Tours.Tests;

/// <summary>Elevaciones de los puntos de interés.</summary>
public class ElevationTests
{
    private readonly ElevationService _service = new(new ElevationClient());

    [Fact]
    public void GetElevationFor_Teide_Is3715m()
    {
        Assert.Equal(3715, this._service.GetElevationFor("teide"));
    }

    [Fact]
    public void Describe_Masca_HasElevationAndDms()
    {
        PoiElevation masca = this._service.Describe("masca");
        Assert.Equal(new PoiElevation("masca", 650, "28°18'25\"N 16°50'28\"W"), masca);
    }

    [Fact]
    public void DescribeAll_ReturnsEightPois()
    {
        Assert.Equal(8, this._service.DescribeAll().Count);
    }

    [Fact]
    public void HighestPoi_IsTeide()
    {
        Assert.Equal("teide", this._service.HighestPoi());
    }

    [Fact]
    public void ElevationGain_PuertoDeLaCruzToTeide_Is3706m()
    {
        Assert.Equal(3706, this._service.ElevationGain("puerto-de-la-cruz", "teide"));
    }

    [Fact]
    public void ElevationClient_FarFromAnyPoi_IsSeaLevel()
    {
        Assert.Equal(0, new ElevationClient().GetElevation(new GeoPoint(27.0, -15.0)));
    }
}
