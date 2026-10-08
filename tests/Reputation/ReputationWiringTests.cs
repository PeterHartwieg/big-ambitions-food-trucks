using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class ReputationWiringTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string Soda = "ba:itemname_sodacan";
    private static readonly DemandModel model = new(ShippedData.Tables);

    private static DemandQuery Lunch(IReadOnlyDictionary<string, double>? shares = null, double reputation = 1.0) => new()
    {
        Areas = AreaMix.Single("office"),
        Menu = new[] { HotDog, Soda },
        MinuteOfDay = 12 * 60,
        Day = DayOfWeek.Wednesday,
        CategoryShares = shares,
        Reputation = reputation
    };

    [Fact]
    public void TwoHotDogCartsHalveEachOther()
    {
        var alone = model.Estimate(Lunch());
        var cart = new CompetitionVehicle { VehicleId = "a", Menu = new[] { HotDog } };
        var other = new CompetitionVehicle { VehicleId = "b", X = 30, Menu = new[] { HotDog } };
        var effect = new Competition(ShippedData.Tables, ShippedData.Rules.Competition).Evaluate(new[] { cart, other })["a"];

        var shared = model.Estimate(Lunch(effect.CategoryShares, ReputationFactor.Combine(1, 1, effect.FootfallFactor)));

        // Half the hot dog customers, but 10% more people pass two carts than one. The cooler isn't
        // on the other cart, so the drink-only customers stay.
        Assert.Equal(0.5, effect.CategoryShares["fastfood"]);
        Assert.InRange(shared.CustomersPerHour / alone.CustomersPerHour, 0.5 * 1.1, 0.75);
    }

    [Fact]
    public void AContestedCategoryNeverPromotesAnother()
    {
        // Hot dogs and pizza at lunch: fast food leads and pizza adds half its share. Halving the
        // hot dog customers must not make pizza the full-weight category.
        var query = new DemandQuery
        {
            Areas = AreaMix.Single("office"),
            Menu = new[] { HotDog, "ba:itemname_pizza" },
            MinuteOfDay = 12 * 60,
            Day = DayOfWeek.Wednesday
        };
        var alone = model.Estimate(query);
        query.CategoryShares = new Dictionary<string, double> { ["fastfood"] = 0.5 };

        var shared = model.Estimate(query);

        Assert.Equal(alone.CustomerShares["pizza"] * alone.CustomersPerHour, shared.CustomerShares["pizza"] * shared.CustomersPerHour, 9);
        Assert.Equal(0.5 * alone.CustomerShares["fastfood"] * alone.CustomersPerHour,
            shared.CustomerShares["fastfood"] * shared.CustomersPerHour, 9);
    }

    [Fact]
    public void ASplitUnderTheAppetiteCapNeverLiftsAnotherCategory()
    {
        // Coffee and croissants at 06:00 on a rainy January weekday in Industry City: the menu's
        // appetite runs into the cap of 1. Halving coffee must leave the bakery customers alone.
        var query = new DemandQuery
        {
            Areas = AreaMix.Single("industrial"),
            Neighbourhood = "ba:neighborhood_industrycity",
            Menu = new[] { "ba:itemname_cupofcoffee", "ba:itemname_croissant" },
            MinuteOfDay = 6 * 60,
            Day = DayOfWeek.Wednesday,
            Month = 1,
            Raining = true
        };
        var alone = model.Estimate(query);
        query.CategoryShares = new Dictionary<string, double> { ["coffee"] = 0.5 };

        var shared = model.Estimate(query);

        Assert.Equal(alone.CustomerShares["bakery"] * alone.CustomersPerHour, shared.CustomerShares["bakery"] * shared.CustomersPerHour, 9);
    }

    [Fact]
    public void AMissingCategoryShareChangesNothing()
    {
        var alone = model.Estimate(Lunch());
        var other = model.Estimate(Lunch(new Dictionary<string, double> { ["pizza"] = 0.5 }));

        Assert.Equal(alone.CustomersPerHour, other.CustomersPerHour);
    }

    [Fact]
    public void AGradeCCartLosesAQuarter()
    {
        var factor = ReputationFactor.Combine(0.75, 1.0, 1.0);

        Assert.Equal(0.75 * model.Estimate(Lunch()).CustomersPerHour, model.Estimate(Lunch(reputation: factor)).CustomersPerHour, 9);
    }

    [Fact]
    public void RegularsAndGradeStack()
    {
        Assert.Equal(0.9 * 1.3 * 1.1, ReputationFactor.Combine(0.9, 1.3, 1.1), 12);
        Assert.Equal(1.3, ReputationFactor.Combine(double.NaN, 1.3, 1.0), 12);
    }

    [Fact]
    public void HealthAndRegularsSurviveASave()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        var health = HealthGrades.NewVehicle(ShippedData.Rules.Health, 1, 7);
        var grades = new HealthGrades(ShippedData.Rules.Health);
        grades.RecordDay(health, 1, cleaned: false, soldStaleStock: true);
        state.Vehicles.Add(new VehicleState { VehicleId = "v1", Health = health });
        new Regulars(ShippedData.Rules.Regulars).EndOfDay(state.Regulars, 3,
            new[] { new SpotVisit { SpotKey = "midtown-lunch", OpenMinutes = 120 } });

        var loaded = codec.Load(codec.Serialize(state)).State!;

        Assert.Equal(7, grades.Points(loaded.Vehicles[0].Health!));
        Assert.Equal(health.FirstSeenDay, loaded.Vehicles[0].Health!.FirstSeenDay);
        Assert.Equal(0.05, new Regulars(ShippedData.Rules.Regulars).Bonus(loaded.Regulars, "midtown-lunch", DayOfWeek.Wednesday), 12);
    }

    [Fact]
    public void ASaveWithoutRegularsLoadsEmpty()
    {
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[],\"dailySales\":[]}";

        var state = new SaveCodec().Load(json).State!;

        Assert.Empty(state.Regulars.Entries);
    }

    [Fact]
    public void BadRegularsMakeTheSaveCorrupt()
    {
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[],\"dailySales\":[],\"regulars\":{\"entries\":[{\"spotKey\":\"\",\"weekday\":\"Monday\",\"bonus\":0.1}]}}";

        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load(json).Status);
    }
}
