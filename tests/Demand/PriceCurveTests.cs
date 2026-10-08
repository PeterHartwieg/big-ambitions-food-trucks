using System;
using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class PriceCurveTests
{
    private static PriceCurve Curve => ShippedData.Tables.PriceCurve;

    [Fact]
    public void HigherPriceFewerBuyers()
    {
        var previous = double.MaxValue;
        for (var ratio = 0.0; ratio <= 2.5; ratio += 0.01)
        {
            var share = Curve.Conversion(ratio);
            Assert.True(share <= previous, $"ratio {ratio}");
            previous = share;
        }
        Assert.True(Curve.Conversion(1.5) < Curve.Conversion(1.0));
    }

    [Theory]
    [InlineData(0.5, 1.1)]
    [InlineData(0.9, 1.05)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.2, 1.0)]
    [InlineData(1.35, 1.0)]
    [InlineData(1.4, 0.7)]
    [InlineData(1.6, 0.18)]
    [InlineData(1.9, 0.0)]
    [InlineData(3.0, 0.0)]
    public void FollowsTheShippedPoints(double ratio, double share)
    {
        Assert.Equal(share, Curve.Conversion(ratio), 9);
    }

    [Fact]
    public void EveryGameCustomerBuysUpToTheWorkingClassIndex()
    {
        // In the game nobody refuses a price up to 1.2 times the reference, whatever the neighbourhood.
        Assert.Equal(1.0, Curve.Conversion(PriceCurve.LowestPriceIndex));
        Assert.Equal(1.0, Curve.Conversion(Curve.NeighbourhoodRatio(1.2, 1.0, 1.2)));
    }

    [Theory]
    [InlineData(1.2, 1.2, 1.35)]
    [InlineData(1.6, 1.6, 1.35)]
    [InlineData(1.2, 1.0, 1.35)]
    [InlineData(1.1, 1.2, 1.175)]
    [InlineData(1.2, double.NaN, 1.2)]
    [InlineData(1.2, 0, 1.2)]
    [InlineData(1.3, 1.35, 1.3)]
    [InlineData(1.6, 1.35, 1.6)]
    [InlineData(0.8, 1.7, 0.8)]
    [InlineData(1.0, 1.7, 1.0)]
    [InlineData(1.8, 1.7, 1.625)]
    [InlineData(2.5, 1.7, 2.5)]
    public void NeighbourhoodRatioMovesTheIndexOntoTheCurve(double price, double index, double ratio)
    {
        Assert.Equal(ratio, Curve.NeighbourhoodRatio(price, 1.0, index), 9);
    }

    [Theory]
    [InlineData(1.2)]
    [InlineData(1.35)]
    [InlineData(1.5)]
    [InlineData(1.7)]
    public void EveryNeighbourhoodKeepsTheUndercutBonusAndTheEndOfTheCurve(double index)
    {
        Assert.Equal(1.1, Curve.Conversion(Curve.NeighbourhoodRatio(0.8, 1.0, index)), 9);
        Assert.Equal(1.0, Curve.Conversion(Curve.NeighbourhoodRatio(1.0, 1.0, index)), 9);
        Assert.Equal(1.0, Curve.Conversion(Curve.NeighbourhoodRatio(index, 1.0, index)), 9);
        Assert.Equal(0.0, Curve.Conversion(Curve.NeighbourhoodRatio(1.9, 1.0, index)), 9);
        Assert.True(Curve.Conversion(Curve.NeighbourhoodRatio(index + 0.1, 1.0, index)) < 1.0);
    }

    [Fact]
    public void ARicherNeighbourhoodNeverBuysLessAtTheSamePrice()
    {
        for (var price = 0.5; price <= 2.2; price += 0.01)
            Assert.True(Curve.Conversion(Curve.NeighbourhoodRatio(price, 1.0, 1.7)) >=
                        Curve.Conversion(Curve.NeighbourhoodRatio(price, 1.0, 1.2)) - 1e-12, $"price {price}");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void BadRatioCountsAsTheReferencePrice(double ratio)
    {
        Assert.Equal(1.0, Curve.Conversion(ratio));
    }

    [Theory]
    [InlineData(3.0, 2.0, 1.5)]
    [InlineData(2.0, 0, 1.0)]
    [InlineData(0, 2.0, 1.0)]
    [InlineData(double.NaN, 2.0, 1.0)]
    public void RatioOfPriceToReference(double price, double reference, double ratio)
    {
        Assert.Equal(ratio, PriceCurve.Ratio(price, reference), 9);
    }

    [Fact]
    public void RejectsCurvesWhereAHigherPriceSellsMore()
    {
        Assert.Throws<ArgumentException>(() => new PriceCurve(new[] { 1.0, 1.5 }, new[] { 0.5, 0.8 }));
        Assert.Throws<ArgumentException>(() => new PriceCurve(new[] { 1.5, 1.0 }, new[] { 1.0, 0.5 }));
        Assert.Throws<ArgumentException>(() => new PriceCurve(new[] { 1.0 }, new[] { 1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PriceCurve(new[] { 1.0, 1.5 }, new[] { 1.0, 0.5 }, 1.1));
        Assert.Throws<ArgumentException>(() => new PriceCurve(new[] { 1.0, 1.3 }, new[] { 1.0, 0.5 }, 1.35));
    }
}
