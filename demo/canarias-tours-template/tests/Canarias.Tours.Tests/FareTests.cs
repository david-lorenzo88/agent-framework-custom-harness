using Canarias.Tours.Domain;
using Canarias.Tours.Payments;

namespace Canarias.Tours.Tests;

/// <summary>Tarifas en euros: bajada de bandera 3,00 € + 1,20 €/km.</summary>
public class FareTests
{
    private readonly FareCalculator _fares = new();

    [Fact]
    public void Quote_LaLagunaToPuertoDeLaCruz_Is31_59Eur()
    {
        Assert.Equal(31.59m, this._fares.Quote(PointsOfInterest.LaLaguna, PointsOfInterest.PuertoDeLaCruz));
    }

    [Fact]
    public void Quote_LosCristianosToTeide_Is33_66Eur()
    {
        Assert.Equal(33.66m, this._fares.Quote(PointsOfInterest.LosCristianos, PointsOfInterest.Teide));
    }

    [Fact]
    public void Breakdown_LaLagunaToPuertoDeLaCruz_SplitsBaseAndDistance()
    {
        FareBreakdown breakdown = this._fares.Breakdown(PointsOfInterest.LaLaguna, PointsOfInterest.PuertoDeLaCruz);
        Assert.Equal(new FareBreakdown(23.8, 3.00m, 28.59m, 31.59m), breakdown);
    }

    [Fact]
    public void IsLongDistance_LaLagunaToPuertoDeLaCruz_IsFalse()
    {
        Assert.False(this._fares.IsLongDistance(PointsOfInterest.LaLaguna, PointsOfInterest.PuertoDeLaCruz));
    }

    [Fact]
    public void QuoteMultiStop_TeideMascaGarachico_Is39_49Eur()
    {
        var pricing = new TourPackagePricing();
        Assert.Equal(39.49m, pricing.QuoteMultiStop([PointsOfInterest.Teide, PointsOfInterest.Masca, PointsOfInterest.Garachico]));
    }

    [Fact]
    public void QuoteRoundTrip_TeideMasca_Is53_56Eur()
    {
        var pricing = new TourPackagePricing();
        Assert.Equal(53.56m, pricing.QuoteRoundTrip(PointsOfInterest.Teide, PointsOfInterest.Masca));
    }
}
