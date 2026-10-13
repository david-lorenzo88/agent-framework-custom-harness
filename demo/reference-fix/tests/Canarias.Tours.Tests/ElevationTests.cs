using Canarias.Tours.Api;
using Teide.Geo;
using Teide.Geo.Primitives;

namespace Canarias.Tours.Tests;

/// <summary>Elevaciones de los puntos de interés.</summary>
public class ElevationTests
{
    private readonly ElevationService _service = new(new ElevationClient());

    [Fact]
    public async Task GetElevationFor_Teide_Is3715m()
    {
        Assert.Equal(3715, await this._service.GetElevationForAsync("teide"));
    }

    [Fact]
    public async Task Describe_Masca_HasElevationAndDms()
    {
        PoiElevation masca = await this._service.DescribeAsync("masca");
        Assert.Equal(new PoiElevation("masca", 650, "28°18'25\"N 16°50'28\"W"), masca);
    }

    [Fact]
    public async Task DescribeAll_ReturnsEightPois()
    {
        Assert.Equal(8, (await this._service.DescribeAllAsync()).Count);
    }

    [Fact]
    public async Task HighestPoi_IsTeide()
    {
        Assert.Equal("teide", await this._service.HighestPoiAsync());
    }

    [Fact]
    public async Task ElevationGain_PuertoDeLaCruzToTeide_Is3706m()
    {
        Assert.Equal(3706, await this._service.ElevationGainAsync("puerto-de-la-cruz", "teide"));
    }

    [Fact]
    public async Task ElevationClient_FarFromAnyPoi_IsSeaLevel()
    {
        Assert.Equal(0, await new ElevationClient().GetElevationAsync(new GeoPoint(27.0, -15.0)));
    }
}
