using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class CustomerPickTests
{
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Tea = "ba:itemname_cupoftea";
    private const string HotDog = "ba:itemname_hotdog";
    private const string Burger = "ba:itemname_burger";
    private const string Pizza = "ba:itemname_pizza";
    private const string Soda = "ba:itemname_sodacan";
    private const string OtherDrink = ShippedData.SecondColdDrink;

    [Theory]
    [InlineData(0, Coffee)]
    [InlineData(0.249, Coffee)]
    [InlineData(0.25, HotDog)]
    [InlineData(0.749, HotDog)]
    [InlineData(0.75, Pizza)]
    [InlineData(0.999999, Pizza)]
    public void MainItemWeightsStockedCategories(double roll, string expected)
    {
        var shares = new Dictionary<string, double> { ["pizza"] = 0.25, ["fastfood"] = 0.5, ["coffee"] = 0.25 };
        var stock = new Dictionary<string, int> { [HotDog] = 20, [Burger] = 10, [Coffee] = 10, [Pizza] = 50 };

        Assert.Equal(expected, CustomerPick.MainItem(ShippedData.Tables, shares, stock, roll));
    }

    [Theory]
    [InlineData(0, Coffee)]
    [InlineData(0.499, Coffee)]
    [InlineData(0.5, Pizza)]
    [InlineData(0.999999, Pizza)]
    public void MissingStockRenormalisesTheRemainingShares(double roll, string expected)
    {
        var shares = new Dictionary<string, double> { ["coffee"] = 0.2, ["fastfood"] = 0.6, ["pizza"] = 0.2 };
        var stock = new Dictionary<string, int> { [HotDog] = 0, [Burger] = -3, [Coffee] = 2, [Pizza] = 5 };

        Assert.Equal(expected, CustomerPick.MainItem(ShippedData.Tables, shares, stock, roll));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.9)]
    public void ZeroSharesChooseTheCategoryWithMostTotalUnits(double roll)
    {
        var shares = new Dictionary<string, double> { ["coffee"] = 0, ["fastfood"] = 0, ["pizza"] = 0 };
        // Fast food wins on category total (13), then hot dog wins within it (7).
        var stock = new Dictionary<string, int> { [Coffee] = 10, [Burger] = 6, [HotDog] = 7, [Pizza] = 12 };

        Assert.Equal(HotDog, CustomerPick.MainItem(ShippedData.Tables, shares, stock, roll));
        Assert.Equal(HotDog, CustomerPick.MainItem(ShippedData.Tables, new Dictionary<string, double>(), stock, roll));
    }

    [Fact]
    public void ZeroShareCategoryTiesUseOrdinalCategoryOrder()
    {
        var stock = new Dictionary<string, int> { [HotDog] = 10, [Coffee] = 10 };

        Assert.Equal(Coffee, CustomerPick.MainItem(ShippedData.Tables, new Dictionary<string, double>(), stock, 0.8));
    }

    [Fact]
    public void ItemTiesUseOrdinalNames()
    {
        var shares = new Dictionary<string, double> { ["fastfood"] = 1 };
        var stock = new Dictionary<string, int> { [HotDog] = 10, [Burger] = 10 };

        Assert.Equal(Burger, CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0.8));
    }

    [Fact]
    public void CategoriesWithoutSharesNeverWinWhenAnotherHasDemand()
    {
        var shares = new Dictionary<string, double> { ["coffee"] = 0.1, ["unknown"] = 100 };
        var stock = new Dictionary<string, int> { [Coffee] = 1, [HotDog] = 80 };

        Assert.Equal(Coffee, CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0));
        Assert.Equal(Coffee, CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0.999999));
    }

    [Fact]
    public void NoStockMeansNoMainItem()
    {
        var shares = new Dictionary<string, double> { ["fastfood"] = 1 };
        var stock = new Dictionary<string, int>
        {
            [HotDog] = 0, [Coffee] = -1, ["ba:itemname_hotdoggrill"] = 1, ["unknown"] = 500
        };

        Assert.Null(CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0));
        Assert.Null(CustomerPick.MainItem(ShippedData.Tables, shares, new Dictionary<string, int>(), 0));
    }

    [Fact]
    public void EveryRollIsDeterministicAcrossDictionaryOrders()
    {
        var shares = new Dictionary<string, double> { ["fastfood"] = 0.7, ["coffee"] = 0.1, ["pizza"] = 0.2 };
        var stock = new Dictionary<string, int> { [HotDog] = 10, [Burger] = 10, [Coffee] = 5, [Pizza] = 20 };
        var reversedShares = shares.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        var reversedStock = stock.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);

        for (var i = 0; i < 100; i++)
        {
            var roll = i / 100.0;
            var expected = CustomerPick.MainItem(ShippedData.Tables, shares, stock, roll);
            Assert.Equal(expected, CustomerPick.MainItem(ShippedData.Tables, shares, stock, roll));
            Assert.Equal(expected, CustomerPick.MainItem(ShippedData.Tables, reversedShares, reversedStock, roll));
        }
    }

    [Fact]
    public void CategoryUnitTotalsDoNotOverflow()
    {
        var stock = new Dictionary<string, int> { [HotDog] = int.MaxValue, [Burger] = 1, [Pizza] = int.MaxValue };

        Assert.Equal(HotDog, CustomerPick.MainItem(ShippedData.Tables, new Dictionary<string, double>(), stock, 0.9));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSharesCountAsZero(double share)
    {
        var shares = new Dictionary<string, double> { ["coffee"] = share, ["fastfood"] = 1 };
        var stock = new Dictionary<string, int> { [Coffee] = 80, [HotDog] = 1 };

        Assert.Equal(HotDog, CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0));
    }

    [Fact]
    public void LargeFiniteSharesDoNotOverflow()
    {
        var shares = new Dictionary<string, double> { ["coffee"] = double.MaxValue, ["fastfood"] = double.MaxValue };
        var stock = new Dictionary<string, int> { [Coffee] = 1, [HotDog] = 1 };

        Assert.Equal(Coffee, CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0.49));
        Assert.Equal(HotDog, CustomerPick.MainItem(ShippedData.Tables, shares, stock, 0.5));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidRollsAreRejected(double roll)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => CustomerPick.MainItem(ShippedData.Tables,
            new Dictionary<string, double>(), new Dictionary<string, int>(), roll));

        Assert.Equal("roll", error.ParamName);
    }

    [Theory]
    [InlineData(3, 5, OtherDrink)]
    [InlineData(5, 3, Soda)]
    [InlineData(5, 5, Soda)]
    [InlineData(0, 1, OtherDrink)]
    [InlineData(-1, 0, null)]
    public void AddOnItemChoosesTheMostPlentifulColdDrink(int soda, int otherDrink, string? expected)
    {
        var stock = new Dictionary<string, int> { [OtherDrink] = otherDrink, [Soda] = soda, [Coffee] = 500, ["unknown"] = 1000 };

        Assert.Equal(expected, CustomerPick.AddOnItem(ShippedData.WithSecondColdDrink(), stock));
    }

    [Fact]
    public void AddOnItemReadsTheCategoryFromTheTables()
    {
        var demand = JObject.Parse(ShippedData.Demand.Replace("\"colddrinks\"", "\"drinks\""));
        var economy = JObject.Parse(ShippedData.Economy);
        economy["products"]![Soda]!["category"] = "drinks";
        var tables = DemandTables.Parse(demand.ToString(), economy.ToString(), ShippedData.Zones);

        Assert.Equal(Soda, CustomerPick.AddOnItem(tables, new Dictionary<string, int> { [Soda] = 3, [Tea] = 50 }));
        Assert.Null(CustomerPick.AddOnItem(tables, new Dictionary<string, int>()));
    }

    [Theory]
    [InlineData(0, 0.2, 0, 0.2)]
    [InlineData(0.7, 0.2, 0, 0.9)]
    [InlineData(0.8, 0.2, 1, 0)]
    [InlineData(0.9, 0.2, 1, 0.1)]
    [InlineData(0.4, 2.8, 3, 0.2)]
    public void AddOnsCarryTheFraction(double progress, double rate, int expectedAddOns, double expectedProgress)
    {
        var result = CustomerPick.AdvanceAddOns(progress, rate);

        Assert.Equal(expectedAddOns, result.AddOns);
        Assert.Equal(expectedProgress, result.Progress, 10);
    }

    [Fact]
    public void FractionalAddOnsAccumulateAcrossCustomers()
    {
        var progress = 0.0;
        var addOns = 0;
        for (var i = 0; i < 10; i++)
        {
            var result = CustomerPick.AdvanceAddOns(progress, 0.2);
            addOns += result.AddOns;
            progress = result.Progress;
        }

        Assert.Equal(2, addOns);
        Assert.Equal(0, progress, 10);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidAddOnProgressResets(double progress)
    {
        var result = CustomerPick.AdvanceAddOns(progress, 0.2);

        Assert.Equal(0, result.AddOns);
        Assert.Equal(0.2, result.Progress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidAddOnRatesKeepGoodProgress(double rate)
    {
        var result = CustomerPick.AdvanceAddOns(0.3, rate);

        Assert.Equal(0, result.AddOns);
        Assert.Equal(0.3, result.Progress);
        Assert.Equal((0, 0.0), CustomerPick.AdvanceAddOns(double.NaN, rate));
    }

    [Fact]
    public void LargeFiniteAddOnRateSaturatesRatherThanOverflowing()
    {
        Assert.Equal((int.MaxValue, 0.0), CustomerPick.AdvanceAddOns(0.9, double.MaxValue));
    }
}
