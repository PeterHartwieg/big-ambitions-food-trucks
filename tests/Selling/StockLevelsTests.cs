using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

// The Stock box on a vehicle's BizMan page: each food of each fitted piece, low at or under a quarter, the game's rule.
public class StockLevelsTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string HotDogGrill = "ba:itemname_hotdoggrill";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Machine = "ba:itemname_industrialcoffeemachine";

    private static DemandTables Tables => ShippedData.Tables;
    private static CargoLine L(string item, int amount) => new(item, amount, false);

    [Theory]
    [InlineData(0, 60, StockLevel.Empty)]
    [InlineData(1, 60, StockLevel.Low)]
    [InlineData(15, 60, StockLevel.Low)]
    [InlineData(16, 60, StockLevel.Ok)]
    [InlineData(60, 60, StockLevel.Ok)]
    [InlineData(3, 0, StockLevel.Ok)]
    public void AQuarterOrLessIsLowAndNoneIsEmpty(int units, int capacity, StockLevel level) =>
        Assert.Equal(level, StockLevels.Level(units, capacity));

    [Fact]
    public void EachFoodOfAPieceIsListedWithItsOwnCapacity()
    {
        var view = Core.Selling.Storage.Of(Tables, "cart", new[] { L(HotDogGrill, 1), L(HotDog, 8), L(Machine, 1), L(Coffee, 100) })!;

        var stock = StockLevels.Of(Tables, view);

        var hotDog = stock.Single(s => s.Item == HotDog);
        Assert.Equal((8, 60, StockLevel.Low), (hotDog.Units, hotDog.Capacity, hotDog.Level));
        var coffee = stock.Single(s => s.Item == Coffee);
        Assert.Equal((100, 240, StockLevel.Ok), (coffee.Units, coffee.Capacity, coffee.Level));
        Assert.Equal(view.Pools.Sum(p => p.Piece.Products.Count), stock.Count);
    }

    [Fact]
    public void AFoodWithNoneOnBoardIsEmpty()
    {
        var view = Core.Selling.Storage.Of(Tables, "truck", new[] { L(HotDogGrill, 1) })!;

        var hotDog = StockLevels.Of(Tables, view).Single(s => s.Item == HotDog);
        Assert.Equal((0, 120, StockLevel.Empty), (hotDog.Units, hotDog.Capacity, hotDog.Level));
    }

    [Fact]
    public void StockedLeavesOutFoodsNeverCarriedAndFoodsMissingFromTheGame()
    {
        var view = Core.Selling.Storage.Of(Tables, "cart", new[] { L(Machine, 1), L(Coffee, 100) })!;
        var vehicle = new FoodTrucks.Core.Saves.VehicleState();

        Assert.Equal(new[] { Coffee }, StockLevels.Stocked(Tables, view, vehicle).Select(s => s.Item));

        vehicle.Sold.Add(new FoodTrucks.Core.Saves.SoldDay { Day = 2, Units = { ["ba:itemname_cupoftea"] = 3, ["bakeryxx:itemname_latte"] = 2 } });
        Assert.Equal(new[] { Coffee, "ba:itemname_cupoftea", "bakeryxx:itemname_latte" }, StockLevels.Stocked(Tables, view, vehicle).Select(s => s.Item));
        Assert.Equal(new[] { Coffee, "ba:itemname_cupoftea" },
            StockLevels.Stocked(Tables, view, vehicle, i => !i.StartsWith("bakeryxx:")).Select(s => s.Item));
    }
}
