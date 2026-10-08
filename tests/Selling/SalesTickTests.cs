using FoodTrucks.Core.Selling;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class SalesTickTests
{
    [Fact]
    public void FiveMinutesAtTwelvePerHourMakesOneSale()
    {
        var result = SalesTick.Advance(0, 5, CartTuning.SalesPerHour, 20);

        Assert.Equal(1, result.Sales);
        Assert.Equal(0, result.Progress);
        Assert.False(result.SoldOut);
        Assert.False(result.Skipped);
    }

    [Fact]
    public void MinuteTicksForAnHourMakeExactlyTwelveSales()
    {
        var progress = 0.0;
        var sales = 0;
        for (var minute = 0; minute < 60; minute++)
        {
            var result = SalesTick.Advance(progress, 1, 12, 100 - sales);
            sales += result.Sales;
            progress = result.Progress;
        }

        Assert.Equal(12, sales);
        Assert.Equal(0, progress, 12);
    }

    [Fact]
    public void FractionalProgressCarriesToTheNextTick()
    {
        var first = SalesTick.Advance(0.25, 2, 12, 10);
        var second = SalesTick.Advance(first.Progress, 2, 12, 10);

        Assert.Equal(0, first.Sales);
        Assert.Equal(0.65, first.Progress, 12);
        Assert.Equal(1, second.Sales);
        Assert.Equal(0.05, second.Progress, 12);
    }

    [Theory]
    [InlineData(120, 24, false)]
    [InlineData(121, 0, true)]
    [InlineData(int.MaxValue, 0, true)]
    public void CatchUpLimitAllowsTwoHoursAndSkipsLongerGaps(int minutes, int sales, bool skipped)
    {
        var result = SalesTick.Advance(0.5, minutes, 12, 100);

        Assert.Equal(sales, result.Sales);
        Assert.Equal(0.5, result.Progress);
        Assert.Equal(skipped, result.Skipped);
        Assert.False(result.SoldOut);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(10, 2)]
    public void ReachingStockCapMarksSoldOutAndClearsProgress(int minutes, int stock)
    {
        var result = SalesTick.Advance(0.5, minutes, 12, stock);

        Assert.Equal(stock, result.Sales);
        Assert.Equal(0, result.Progress);
        Assert.True(result.SoldOut);
        Assert.False(result.Skipped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoStockIsSoldOutAndClearsProgress(int stock)
    {
        var result = SalesTick.Advance(0.5, 1, 12, stock);

        Assert.Equal(0, result.Sales);
        Assert.Equal(0, result.Progress);
        Assert.True(result.SoldOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonPositiveElapsedMinutesPreserveProgress(int minutes)
    {
        var result = SalesTick.Advance(0.5, minutes, 12, 10);

        Assert.Equal(0, result.Sales);
        Assert.Equal(0.5, result.Progress);
        Assert.False(result.SoldOut);
        Assert.False(result.Skipped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidRateMakesNoSalesAndPreservesProgress(double rate)
    {
        var result = SalesTick.Advance(0.5, 5, rate, 10);

        Assert.Equal(0, result.Sales);
        Assert.Equal(0.5, result.Progress);
        Assert.False(result.SoldOut);
        Assert.False(result.Skipped);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidProgressIsResetEvenWhenNoTimePasses(double progress)
    {
        var paused = SalesTick.Advance(progress, 0, 12, 10);
        var advanced = SalesTick.Advance(progress, 1, 12, 10);

        Assert.Equal(0, paused.Progress);
        Assert.Equal(0, advanced.Sales);
        Assert.Equal(0.2, advanced.Progress, 12);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public void HugeFiniteRateIsCappedBeforeIntegerConversion(int minutes)
    {
        var result = SalesTick.Advance(0.5, minutes, double.MaxValue, int.MaxValue);

        Assert.Equal(int.MaxValue, result.Sales);
        Assert.True(result.SoldOut);
        Assert.Equal(0, result.Progress);
    }

    [Fact]
    public void TimeSkipWithNoStockIsStillSoldOut()
    {
        var result = SalesTick.Advance(0.5, 121, 12, 0);

        Assert.Equal(0, result.Sales);
        Assert.Equal(0, result.Progress);
        Assert.True(result.SoldOut);
        Assert.True(result.Skipped);
    }
}
