using System;
using System.Collections.Generic;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class CargoRulesTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string Grill = "ba:itemname_hotdoggrill";
    private const string Fridge = "ba:itemname_drinksfridge";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Tea = "ba:itemname_cupoftea";
    private const string CoffeeMachine = "ba:itemname_industrialcoffeemachine";
    private const string Storage = "foodtrucks:itemname_storagecabinet";

    [Fact]
    public void ACartsCoffeeMachineHoldsOneHundredSixtyCoffees()
    {
        var tables = ShippedData.Tables;
        var machine = new CargoLine(CoffeeMachine, 1, false);
        Assert.Equal(160, CargoRules.AllowedUnits(tables, "cart", new[] { machine }, Coffee, 500, false));
        var full = new[] { machine, new CargoLine(Coffee, 160, false) };
        Assert.Equal(40, CargoRules.FoodUnits(tables, full));
        Assert.Equal(0, CargoRules.AllowedUnits(tables, "cart", full, Coffee, 1, false));
        Assert.Equal(1, CargoRules.AllowedUnits(tables, "cart", new[] { machine, new CargoLine(Coffee, 159, false) }, Coffee, 10, false));
        Assert.Equal(320, CargoRules.AllowedUnits(tables, "cart", new[] { machine, new CargoLine(Storage, 1, false) }, Coffee, 500, false));
    }

    [Fact]
    public void MixedFoodSharesWeightedCapacityAndKeepsFractionalRoom()
    {
        // Each piece keeps its own room: hot dogs fill only the grill, coffee and tea share the machine.
        var cargo = new[]
        {
            new CargoLine(Grill, 1, false), new CargoLine(CoffeeMachine, 1, false), new CargoLine(Storage, 1, false),
            new CargoLine(HotDog, 60, false), new CargoLine(Coffee, 3, false), new CargoLine(Tea, 2, false),
            new CargoLine("ba:itemname_croissant", 3, false)
        };
        Assert.Equal(62.75, CargoRules.FoodLoad(ShippedData.Tables, cargo));
        Assert.Equal(63, CargoRules.FoodUnits(ShippedData.Tables, cargo));
        Assert.Equal(635, CargoRules.AllowedUnits(ShippedData.Tables, "truck", cargo, Coffee, 1000, false));
        Assert.Equal(100, CargoRules.AllowedUnits(ShippedData.Tables, "truck", cargo, HotDog, 500, false));
        Assert.Equal(20, CargoRules.AllowedUnits(ShippedData.Tables, "cart", cargo, HotDog, 500, false));
        Assert.Equal(0, CargoRules.AllowedUnits(ShippedData.Tables, "truck", cargo, "ba:itemname_donut", 500, false));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.3333333333333333)]
    [InlineData(0.9999999999999999)]
    [InlineData(1e-300)]
    public void FractionalWeightsNeverOverfillTheLimit(double weight)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        economy["products"]![Coffee]!["stockWeight"] = weight;
        var tables = DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones);
        for (var existing = 0; existing <= 80; existing++)
        {
            var cargo = new List<CargoLine> { new(CoffeeMachine, 1, false), new(Tea, existing * 2, false), new(Coffee, 1, false) };
            var before = CargoRules.FoodLoad(tables, cargo);
            var allowed = CargoRules.AllowedUnits(tables, "cart", cargo, Coffee, int.MaxValue, false);
            if (before > 40)
            {
                Assert.Equal(0, allowed);
                continue;
            }
            cargo.Add(new CargoLine(Coffee, allowed, false));
            Assert.True(CargoRules.FoodLoad(tables, cargo) <= 40);
            if (allowed < int.MaxValue)
                Assert.True(before + (allowed + 1.0) * weight > 40 || before == 40);
        }
    }

    [Fact]
    public void FoodUnitsCountsAllLooseProductsOnly()
    {
        var cargo = new[]
        {
            new CargoLine(HotDog, 30, false), new CargoLine("ba:itemname_pizza", 20, false),
            new CargoLine(HotDog, 500, true), new CargoLine(Grill, 2, false),
            new CargoLine("ba:itemname_box", 80, false), new CargoLine(null, 10, false),
            new CargoLine(HotDog, -5, false), new CargoLine(HotDog, 0, false)
        };

        Assert.Equal(50, CargoRules.FoodUnits(ShippedData.Tables, cargo));
        Assert.Equal(0, CargoRules.FoodUnits(ShippedData.Tables, Array.Empty<CargoLine>()));
    }

    [Fact]
    public void FoodUnitsSaturatesRatherThanOverflowing()
    {
        var cargo = new[] { new CargoLine(Grill, 1, false), new CargoLine(HotDog, int.MaxValue, false), new CargoLine(HotDog, 1, false) };

        Assert.Equal(int.MaxValue, CargoRules.FoodUnits(ShippedData.Tables, cargo));
        Assert.Equal(0, CargoRules.AllowedUnits(ShippedData.Tables, "cart", cargo, HotDog, 500, false));
    }

    [Fact]
    public void FittedEquipmentKeepsEntryOrderAndAmounts()
    {
        var cargo = new[]
        {
            new CargoLine(Grill, 2, false), new CargoLine(HotDog, 80, false),
            new CargoLine("ba:itemname_industrialcoffeemachine", 1, false),
            new CargoLine(Fridge, 1, true), new CargoLine("ba:itemname_box", 1, true),
            new CargoLine(Grill, -1, false), new CargoLine(Grill, 0, false), new CargoLine(null, 1, false)
        };

        Assert.Equal(new[] { "hotdoggrill", "hotdoggrill", "coffeemachine", "cooler" },
            CargoRules.FittedEquipment(ShippedData.Tables, cargo));
    }

    [Theory]
    [InlineData("cart", 0, 10, 10)]
    [InlineData("cart", 30, 10, 10)]
    [InlineData("cart", 30, 500, 50)]
    [InlineData("cart", 40, 500, 40)]
    [InlineData("cart", 100, 500, 0)]
    [InlineData("truck", 60, 500, 100)]
    [InlineData("cart", -5, 500, 80)]
    [InlineData("unknown", 80, 500, 500)]
    [InlineData(null, 80, 500, 500)]
    public void FoodLoadsOnlyWhatFitsItsPiece(string? vehicle, int stock, int incoming, int allowed)
    {
        // Soda belongs to the cooler (80 a cart at stock weight 0.5); hot dogs in the grill and nested stock don't take its room.
        var cargo = new[]
        {
            new CargoLine("ba:itemname_sodacan", stock, false), new CargoLine(Fridge, 1, false), new CargoLine(HotDog, 30, false),
            new CargoLine(Grill, 1, false), new CargoLine("ba:itemname_sodacan", 500, true), new CargoLine("ba:itemname_box", 80, false)
        };

        Assert.Equal(allowed, CargoRules.AllowedUnits(ShippedData.Tables, vehicle, cargo,
            "ba:itemname_sodacan", incoming, false));
    }

    [Theory]
    [InlineData("cart", 0, 1, 1)]
    [InlineData("cart", 0, 5, 3)]
    [InlineData("cart", 1, 3, 2)]
    [InlineData("cart", 2, 1, 1)]
    [InlineData("cart", 3, 1, 0)]
    [InlineData("truck", 2, 3, 3)]
    [InlineData("truck", 4, 1, 1)]
    [InlineData("unknown", 4, 3, 3)]
    [InlineData("cart", int.MaxValue, 1, 0)]
    public void EquipmentLoadsOnlyIntoFreeEquipmentSlots(string vehicle, int fitted, int incoming, int allowed)
    {
        var cargo = new[] { new CargoLine(Grill, fitted, false), new CargoLine(HotDog, 80, false) };

        Assert.Equal(allowed, CargoRules.AllowedUnits(ShippedData.Tables, vehicle, cargo, Fridge, incoming, false));
    }

    [Fact]
    public void StockedFridgeUsesItsDedicatedSlot()
    {
        var cargo = new[] { new CargoLine(Grill, 2, false) };

        Assert.Equal(1, CargoRules.AllowedUnits(ShippedData.Tables, "cart", cargo, Fridge, 1, true));
        Assert.Equal(1, CargoRules.AllowedUnits(ShippedData.Tables, "cart", new[] { new CargoLine(Grill, 1, false) }, Fridge, 1, true));
    }

    [Theory]
    [InlineData(HotDog, true)]
    [InlineData("ba:itemname_box", true)]
    [InlineData("ba:itemname_box", false)]
    [InlineData(null, false)]
    public void OtherItemsAndIncomingNestedCargoPassThrough(string? incomingItem, bool nested)
    {
        var cargo = new[] { new CargoLine(HotDog, 80, false), new CargoLine(Grill, 2, false) };

        Assert.Equal(500, CargoRules.AllowedUnits(ShippedData.Tables, "cart", cargo, incomingItem, 500, nested));
    }

    [Theory]
    [InlineData(HotDog)]
    [InlineData(Grill)]
    [InlineData("ba:itemname_box")]
    public void IncomingAmountsNeverBecomeNegative(string incomingItem)
    {
        Assert.Equal(0, CargoRules.AllowedUnits(ShippedData.Tables, "cart", Array.Empty<CargoLine>(), incomingItem, -1, false));
        Assert.Equal(0, CargoRules.AllowedUnits(ShippedData.Tables, null, Array.Empty<CargoLine>(), incomingItem, -1, true));
    }

    [Fact]
    public void MenuFromCargoUsesOnlyFittedEquipment()
    {
        var cargo = new[]
        {
            new CargoLine(Grill, 1, false), new CargoLine(Fridge, 1, false),
            new CargoLine("ba:itemname_pizza", 40, false), new CargoLine("ba:itemname_box", 1, true)
        };

        var build = Menu.FromCargo(ShippedData.Tables, "cart", cargo);

        Assert.Equal(MenuProblem.None, build.Problem);
        Assert.Null(build.Detail);
        Assert.Equal(new[] { HotDog, "ba:itemname_sodacan" }, build.Menu.Items);
    }

    [Fact]
    public void MenuFromCargoCountsDuplicatePiecesAgainstSlots()
    {
        var cargo = new[] { new CargoLine(Grill, 3, false), new CargoLine(Fridge, 1, false) };

        var cart = Menu.FromCargo(ShippedData.Tables, "cart", cargo);
        var truck = Menu.FromCargo(ShippedData.Tables, "truck", cargo);

        Assert.Equal(MenuProblem.TooMuchEquipment, cart.Problem);
        Assert.Equal("hotdoggrill", cart.Detail);
        Assert.Empty(cart.Menu.Items);
        Assert.Equal(MenuProblem.None, truck.Problem);
        Assert.Equal(new[] { HotDog, "ba:itemname_sodacan" }, truck.Menu.Items);
    }

    [Fact]
    public void MenuFromCargoReportsUnknownVehicleAndEmptyMenu()
    {
        Assert.Equal(MenuProblem.UnknownVehicle,
            Menu.FromCargo(ShippedData.Tables, "unknown", Array.Empty<CargoLine>()).Problem);
        var build = Menu.FromCargo(ShippedData.Tables, "cart", new[] { new CargoLine(HotDog, 80, false) });

        Assert.Equal(MenuProblem.None, build.Problem);
        Assert.Empty(build.Menu.Items);
    }

    [Fact]
    public void AllowedUnitsReadsCargoOnce()
    {
        var enumerations = 0;
        IEnumerable<CargoLine> Cargo()
        {
            Assert.Equal(1, ++enumerations);
            yield return new CargoLine(Grill, 1, false);
            yield return new CargoLine(HotDog, 20, false);
        }

        Assert.Equal(20, CargoRules.AllowedUnits(ShippedData.Tables, "cart", Cargo(), HotDog, 500, false));
    }
}
