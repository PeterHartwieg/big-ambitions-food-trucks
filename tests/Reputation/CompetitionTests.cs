using System;
using System.Linq;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class CompetitionTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string Burger = "ba:itemname_burger";
    private const string Fries = "ba:itemname_frenchfries";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private static readonly Competition competition = new(ShippedData.Tables, new CompetitionRules());

    private static CompetitionVehicle Vehicle(string id, double x = 0, double z = 0, params string[] menu) => new()
    {
        VehicleId = id, X = x, Z = z, Menu = menu.Length == 0 ? new[] { HotDog } : menu
    };

    [Fact]
    public void TwoHotDogCartsHalveEachOthersProductDemand()
    {
        var effects = competition.Evaluate(new[] { Vehicle("one"), Vehicle("two", 10) });

        foreach (var effect in effects.Values)
        {
            Assert.Equal(0.5, effect.CategoryShares["fastfood"]);
            Assert.Equal(1.1, effect.FootfallFactor);
        }
    }

    [Fact]
    public void HotDogNextToBurgerKeepsBothProductShares()
    {
        var effects = competition.Evaluate(new[] { Vehicle("cart"), Vehicle("truck", menu: new[] { Burger }) });

        Assert.Equal(1, effects["cart"].CategoryShares["fastfood"]);
        Assert.Equal(1, effects["truck"].CategoryShares["fastfood"]);
    }

    [Fact]
    public void PartlyOverlappingMenusAverageSharesByItem()
    {
        var effects = competition.Evaluate(new[]
        {
            Vehicle("truck", menu: new[] { Burger, Fries }), Vehicle("cart", menu: new[] { HotDog, Burger })
        });

        Assert.Equal(0.75, effects["truck"].CategoryShares["fastfood"]);
        Assert.Equal(0.75, effects["cart"].CategoryShares["fastfood"]);
    }

    [Fact]
    public void FootfallIsCappedButProductSharingCountsEveryNeighbour()
    {
        var effects = competition.Evaluate(Enumerable.Range(0, 8).Select(i => Vehicle(i.ToString())));

        foreach (var effect in effects.Values)
        {
            Assert.Equal(1.3, effect.FootfallFactor);
            Assert.Equal(0.125, effect.CategoryShares["fastfood"]);
        }
    }

    [Fact]
    public void DistanceUsesBothCoordinatesAndIncludesTheBoundary()
    {
        var effects = competition.Evaluate(new[] { Vehicle("origin"), Vehicle("edge", 48, 64), Vehicle("outside", -80.001) });

        Assert.Equal(0.5, effects["origin"].CategoryShares["fastfood"]);
        Assert.Equal(1.1, effects["origin"].FootfallFactor);
        Assert.Equal(1, effects["outside"].CategoryShares["fastfood"]);
        Assert.Equal(1, effects["outside"].FootfallFactor);
    }

    [Fact]
    public void InputAndMenuOrderDoNotChangeTheResult()
    {
        var vehicles = new[]
        {
            Vehicle("z", menu: new[] { Coffee, Fries, Burger }),
            Vehicle("b", 20, menu: new[] { Burger, HotDog }), Vehicle("a", 1000)
        };
        var first = competition.Evaluate(vehicles);
        foreach (var vehicle in vehicles) vehicle.Menu = vehicle.Menu.Reverse().ToArray();
        var second = competition.Evaluate(vehicles.Reverse());

        Assert.Equal(first.Keys, second.Keys);
        foreach (var id in first.Keys)
        {
            Assert.Equal(first[id].FootfallFactor, second[id].FootfallFactor);
            Assert.Equal(first[id].CategoryShares.ToArray(), second[id].CategoryShares.ToArray());
        }
    }

    [Fact]
    public void UnknownAndRepeatedItemsDoNotDistortCategoryShares()
    {
        var effects = competition.Evaluate(new[]
        {
            Vehicle("truck", menu: new[] { Burger, Burger, Fries, "unknown", Coffee }),
            Vehicle("cart", menu: new[] { Burger }), Vehicle("empty", menu: new[] { "unknown" })
        });

        Assert.Equal(0.75, effects["truck"].CategoryShares["fastfood"]);
        Assert.Equal(1, effects["truck"].CategoryShares["coffee"]);
        Assert.Empty(effects["empty"].CategoryShares);
        Assert.Equal(1.1, effects["truck"].FootfallFactor);
    }

    [Fact]
    public void EmptyMenusDoNotAddFootfall()
    {
        var empty = Vehicle("empty");
        empty.Menu = Array.Empty<string>();
        var effect = competition.Evaluate(new[] { Vehicle("truck"), empty })["truck"];
        Assert.Equal(1, effect.FootfallFactor);
        Assert.Equal(1, effect.CategoryShares["fastfood"]);
    }

    [Fact]
    public void DuplicateVehicleIdsThrow()
    {
        Assert.Throws<ArgumentException>(() => competition.Evaluate(new[] { Vehicle("same"), Vehicle("same", 1000) }));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    [InlineData(0, double.NaN)]
    public void NonfinitePositionsThrow(double x, double z)
    {
        Assert.Throws<ArgumentException>(() => competition.Evaluate(new[] { Vehicle("bad", x, z) }));
    }

    [Fact]
    public void ExtremeFinitePositionsDoNotOverflowDistance()
    {
        var effects = competition.Evaluate(new[] { Vehicle("one", double.MaxValue), Vehicle("two", -double.MaxValue) });

        Assert.All(effects.Values, effect => Assert.Equal(1, effect.FootfallFactor));
        Assert.Empty(competition.Evaluate(Array.Empty<CompetitionVehicle>()));
    }

    [Fact]
    public void CustomRulesControlNeighbourRadiusAndFootfall()
    {
        var custom = new Competition(ShippedData.Tables, new CompetitionRules(nearbyMetres: 10, footfallPerNeighbour: 0.2, maxFootfallBonus: 0.1));
        var effects = custom.Evaluate(new[] { Vehicle("one"), Vehicle("two", 10), Vehicle("far", 30) });

        Assert.Equal(1.1, effects["one"].FootfallFactor);
        Assert.Equal(0.5, effects["one"].CategoryShares["fastfood"]);
        Assert.Equal(1, effects["far"].FootfallFactor);
    }
}
