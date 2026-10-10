using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class StockLimitTests
{
    [Theory]
    [InlineData(0, 500, 80, 80)]
    [InlineData(60, 500, 80, 20)]
    [InlineData(60, 10, 80, 10)]
    [InlineData(80, 500, 80, 0)]
    [InlineData(120, 500, 80, 0)]
    [InlineData(-5, 500, 80, 80)]
    [InlineData(0, 0, 80, 0)]
    [InlineData(0, 500, 0, 0)]
    public void ABoxLoadsOnlyWhatFits(int stock, int box, int capacity, int loaded)
    {
        Assert.Equal(loaded, StockLimit.CanLoad(stock, box, capacity));
    }

    [Fact]
    public void ACartLoadedFromABigBoxSellsOutAtItsLimit()
    {
        var capacity = ShippedData.Tables.Vehicle("cart")!.UnitsPerPiece;
        var stock = StockLimit.CanLoad(0, 500, capacity);
        var sales = 0;
        var progress = 0.0;
        for (var minute = 0; minute < 24 * 60 && stock > 0; minute++)
        {
            var tick = SalesTick.Advance(progress, 1, 20, stock);
            stock -= tick.Sales;
            sales += tick.Sales;
            progress = tick.Progress;
        }

        Assert.Equal(capacity, sales);
        Assert.Equal(0, StockLimit.CanLoad(capacity, 500, capacity));
    }

    [Fact]
    public void ACartSellsOutWithinADayAtAGoodPitch()
    {
        var cart = ShippedData.Tables.Vehicle("cart")!;
        var truck = ShippedData.Tables.Vehicle("truck")!;

        // Storage per piece (Peter, 10 October 2026). One person serves 12 to 20 an hour: a cart with one food
        // piece and the storage piece holds 80 units, 4 to 7 hours.
        Assert.Equal(40, cart.UnitsPerPiece);
        Assert.Equal(80, truck.UnitsPerPiece);
        Assert.InRange(cart.UnitsPerPiece * 2 / 20.0, 3, 8);
    }
}
