using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class MenuTests
{
    [Fact]
    public void MenuFollowsTheFittedEquipment()
    {
        var build = Menu.FromEquipment(ShippedData.Tables, "cart", new[] { "hotdoggrill", "cooler" });

        Assert.Equal(MenuProblem.None, build.Problem);
        Assert.Equal(new[] { "ba:itemname_hotdog", "ba:itemname_sodacan" }, build.Menu.Items);
        Assert.False(build.Menu.IsOnMenu("ba:itemname_water"));
        // Commissary ordering uses ProductNames, independently of the menu.
        Assert.DoesNotContain("ba:itemname_water", ShippedData.Tables.ProductNames);
        Assert.False(build.Menu.IsOnMenu("ba:itemname_burger"));
    }

    [Fact]
    public void NoEquipmentMeansNothingToSell()
    {
        var build = Menu.FromEquipment(ShippedData.Tables, "cart", Array.Empty<string>());

        Assert.Equal(MenuProblem.None, build.Problem);
        Assert.Empty(build.Menu.Items);
    }

    [Fact]
    public void ACartHasTwoSlots()
    {
        var build = Menu.FromEquipment(ShippedData.Tables, "cart", new[] { "hotdoggrill", "cooler", "coffeemachine" });

        Assert.Equal(MenuProblem.TooMuchEquipment, build.Problem);
        Assert.Equal("coffeemachine", build.Detail);
        Assert.Empty(build.Menu.Items);
    }

    [Fact]
    public void ATruckHasFour()
    {
        var build = Menu.FromEquipment(ShippedData.Tables, "truck", new[] { "hotdoggrill", "cooler", "coffeemachine", "displaycase" });

        Assert.Equal(MenuProblem.None, build.Problem);
        // Seven base-game items (the cooler sells soda only) and five from the Bakery mod, which only reach a vehicle when that mod is installed.
        Assert.Equal(12, build.Menu.Items.Count);
        Assert.Equal(7, build.Menu.Items.Count(i => i.StartsWith("ba:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ASecondPieceOfTheSameKindTakesASlotButAddsNothing()
    {
        var build = Menu.FromEquipment(ShippedData.Tables, "cart", new[] { "hotdoggrill", "hotdoggrill" });

        Assert.Equal(MenuProblem.None, build.Problem);
        Assert.Equal(new[] { "ba:itemname_hotdog" }, build.Menu.Items);
        Assert.Equal(MenuProblem.TooMuchEquipment,
            Menu.FromEquipment(ShippedData.Tables, "cart", new[] { "hotdoggrill", "hotdoggrill", "cooler" }).Problem);
    }

    [Theory]
    [InlineData("rocket", "hotdoggrill", MenuProblem.UnknownVehicle, "rocket")]
    [InlineData(null, "hotdoggrill", MenuProblem.UnknownVehicle, null)]
    [InlineData("cart", "fondue", MenuProblem.UnknownEquipment, "fondue")]
    [InlineData("cart", null, MenuProblem.UnknownEquipment, null)]
    public void UnknownNamesAreReported(string? vehicle, string? equipment, MenuProblem problem, string? detail)
    {
        var build = Menu.FromEquipment(ShippedData.Tables, vehicle, new[] { equipment });

        Assert.Equal(problem, build.Problem);
        Assert.Equal(detail, build.Detail);
        Assert.Empty(build.Menu.Items);
    }

    [Fact]
    public void PicksTheItemWithMostUnitsInMenuOrder()
    {
        var menu = new Menu(new[] { "ba:itemname_hotdog", "ba:itemname_sodacan", "ba:itemname_hotdog" });
        var stock = new Dictionary<string, int> { ["ba:itemname_sodacan"] = 4, ["ba:itemname_hotdog"] = 4, ["ba:itemname_burger"] = 9 };

        Assert.Equal(2, menu.Items.Count);
        Assert.Equal("ba:itemname_hotdog", menu.PickNext(stock));
        Assert.Null(menu.PickNext(new Dictionary<string, int>()));
    }

    [Fact]
    public void EveryShippedEquipmentProductHasACategory()
    {
        foreach (var id in ShippedData.Tables.EquipmentIds)
            foreach (var item in ShippedData.Tables.Equipment(id)!.Products)
                Assert.NotNull(ShippedData.Tables.Product(item));
    }

    [Fact]
    public void EveryMilestoneOneCartItemIsAProduct()
    {
        foreach (var item in CartMenu.Items)
            Assert.NotNull(ShippedData.Tables.Product(item));
    }
}
