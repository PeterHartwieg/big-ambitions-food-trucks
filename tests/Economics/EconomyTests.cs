using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Economics;
using FoodTrucks.Core.Tests.Demand;
using Xunit;
using Xunit.Abstractions;

namespace FoodTrucks.Core.Tests.Economics;

public class EconomyTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string Burger = "ba:itemname_burger";
    private const string Fries = "ba:itemname_frenchfries";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Soda = "ba:itemname_sodacan";
    private const string Midtown = "ba:neighborhood_midtown";
    private const string HellsKitchen = "ba:neighborhood_hellskitchen";

    private static readonly Economy economy = new(ShippedData.Rules.Economy);
    private readonly ITestOutputHelper output;

    public EconomyTests(ITestOutputHelper output) => this.output = output;

    private static long Shop => economy.ReferenceShopIncome(GameVariables.Default);

    // The cart's best weekday: an office lunch in Midtown, restock, then Hell's Kitchen's evening.
    // This is the player's own day, so the 3.5 hours between the pitches are for the restock and
    // pushing the cart across at a run: 6 m/s is 360 m a game hour at normal speed, so about 1.2 km.
    // Pushing at a run is a guess, untested in game; at walking pace (1.2 m/s) the move takes about
    // 16 game hours and the cart gets one pitch a day, about 0.37 of a shop.
    internal static RoutePlan CartDay(long seed = 1) => new()
    {
        VehicleType = "cart",
        Load = new Dictionary<string, int> { [HotDog] = 64, [Soda] = 16 },
        Stops = new[]
        {
            new RouteStop { Areas = AreaMix.Single("office"), Neighbourhood = Midtown, FromMinute = 11 * 60, ToMinute = 14 * 60 + 30 },
            new RouteStop { Areas = AreaMix.Single("nightlife"), Neighbourhood = HellsKitchen, FromMinute = 18 * 60, ToMinute = 23 * 60 }
        },
        Seed = seed
    };

    // A truck on a good route with the player and one hire at the window.
    internal static RoutePlan TruckDay(int servers, int hires, long seed = 1) => new()
    {
        VehicleType = "truck",
        Load = new Dictionary<string, int> { [Coffee] = 30, [HotDog] = 60, [Burger] = 30, [Fries] = 20, [Soda] = 20 },
        Servers = servers,
        Hires = hires,
        DrivenMetres = 6000,
        Stops = new[]
        {
            new RouteStop { Areas = AreaMix.Single("office"), Neighbourhood = Midtown, FromMinute = 7 * 60, ToMinute = 14 * 60 + 30 },
            new RouteStop { Areas = AreaMix.Single("nightlife"), Neighbourhood = HellsKitchen, FromMinute = 17 * 60, ToMinute = 24 * 60 }
        },
        Seed = seed
    };

    // Several seeds, so one lucky day doesn't decide a target.
    private static double AverageProfit(Func<long, RoutePlan> plan) => Average(plan, o => o.ProfitCents);

    private static double Average(Func<long, RoutePlan> plan, Func<RouteOutcome, double> value)
    {
        var total = 0.0;
        for (var seed = 1; seed <= 5; seed++) total += value(RouteDay.Run(plan(seed)));
        return total / 5;
    }

    [Fact]
    public void TheReferenceShopFollowsTheGamesFormula()
    {
        var shop = ShippedData.Rules.Economy.Shop;
        // 20 × (0.5 + 0.75 × 0.5) × 0.7 = 12.25 customers an hour, under the register's 30.
        var customers = 12.25 * shop.HoursOpen;
        var margin = shop.ItemPriceCents - shop.ItemWholesaleCents * 1.05;
        var wages = shop.HoursOpen * shop.BaseHourlyWageCents * (Math.Pow(20, 1.05) / 100 + 1);

        Assert.Equal(Math.Round(customers * shop.ItemsPerCustomer * margin - shop.DailyRentCents - wages), Shop);
    }

    [Fact]
    public void HarderDifficultyShrinksTheShop()
    {
        var hard = new GameVariables { MarketPriceMultiplier = 1.3, WageMultiplier = 1.3, BaseCustomerPromotion = 0.3 };
        var easy = new GameVariables { MarketPriceMultiplier = 0.7, WageMultiplier = 0.5, BaseCustomerPromotion = 1.0 };

        Assert.True(economy.ReferenceShopIncome(hard) < Shop);
        Assert.True(economy.ReferenceShopIncome(easy) > Shop);
    }

    [Fact]
    public void TheRegisterCapsTheShopsCustomers()
    {
        var shop = ShippedData.Rules.Economy.Shop.Copy();
        shop.BuildingCustomersPerHour = 1000;
        var capped = economy.ReferenceShopIncome(GameVariables.Default, shop);
        shop.BuildingCustomersPerHour = 2000;

        Assert.Equal(capped, economy.ReferenceShopIncome(GameVariables.Default, shop));
    }

    [Fact]
    public void AMeasuredShopWins()
    {
        var shop = ShippedData.Rules.Economy.Shop.Copy();
        shop.MeasuredDailyProfitCents = 123456;

        Assert.Equal(123456, economy.ReferenceShopIncome(GameVariables.Default, shop));
    }

    [Fact]
    public void SetupCostIsDepositEquipmentAndStock()
    {
        var shop = ShippedData.Rules.Economy.Shop;
        var expected = shop.DailyRentCents * shop.DepositDays + shop.EquipmentCents +
                       (long)Math.Round(shop.OpeningStockUnits * shop.ItemWholesaleCents * 1.05);

        Assert.Equal(expected, economy.ReferenceSetupCost(GameVariables.Default));
    }

    [Fact]
    public void TheCartCostsWhatTheMilestoneOneDealerCharges()
    {
        Assert.Equal(120000, economy.Price("cart", GameVariables.Default));
    }

    [Fact]
    public void PricesFollowTheSetupCostAndRoundToTenDollars()
    {
        var expensive = new GameVariables { MarketPriceMultiplier = 1.3 };

        foreach (var key in new[] { "cart", "truck", "licence", "permit", "hotdoggrill" })
        {
            var price = economy.Price(key, GameVariables.Default);
            Assert.Equal(0, price % 1000);
            Assert.True(price > 0, key);
            Assert.True(economy.Price(key, expensive) >= price, key);
        }
        Assert.True(economy.Price("truck", GameVariables.Default) > 5 * economy.Price("cart", GameVariables.Default));
    }

    [Fact]
    public void EveryEquipmentPieceHasAPrice()
    {
        foreach (var id in ShippedData.Tables.EquipmentIds)
            Assert.True(economy.HasPrice(id), id);
        foreach (var type in new[] { "cart", "truck" })
            Assert.True(economy.HasPrice(type), type);
    }

    [Fact]
    public void AnUnknownPriceThrows()
    {
        Assert.Throws<ArgumentException>(() => economy.Price("hotdoggril", GameVariables.Default));
    }

    [Theory]
    [InlineData(5, 1.0541)]
    [InlineData(20, 1.2328)]
    [InlineData(40, 1.4810)]
    public void WagesFollowTheGamesSkillBoost(double skill, double boost)
    {
        Assert.Equal(1600 * boost, Economy.HourlyWageCents(1600, skill, GameVariables.Default), 0);
        Assert.Equal(800 * boost, Economy.HourlyWageCents(1600, skill, new GameVariables { WageMultiplier = 0.5 }), 0);
    }

    [Fact]
    public void FuelCostsALittle()
    {
        // 10 km at 0.15 l/km and $1.30 a litre.
        Assert.Equal(195, economy.FuelCents(10000));
        Assert.Equal(0, economy.FuelCents(double.NaN));
    }

    [Fact]
    public void ACartAtAGoodLunchPitchMakesAboutHalfASmallShop()
    {
        var cart = AverageProfit(CartDay);
        var sold = Average(CartDay, o => o.SoldShare);
        output.WriteLine($"cart {cart / 100:F0}, shop {Shop / 100.0:F0}, ratio {cart / Shop:F2}, sold {sold:P0} of the stock");

        // The design sizes the cart's stock at about half a day at a good pitch, so the stock limit,
        // not demand, sets a good day: two loads of 80 units.
        Assert.True(sold > 0.9);

        // Target 0.5. The band is ±50% while the reference shop is a guess; tighten it to ±30% once
        // the game side measures the shop.
        Assert.InRange(cart / Shop, 0.25, 0.75);
    }

    [Fact]
    public void ATruckWithOneHireMakesAboutWhatASmallShopMakes()
    {
        var truck = AverageProfit(seed => TruckDay(servers: 2, hires: 1, seed));
        var sold = Average(seed => TruckDay(servers: 2, hires: 1, seed), o => o.SoldShare);
        output.WriteLine($"truck {truck / 100:F0}, shop {Shop / 100.0:F0}, ratio {truck / Shop:F2}, sold {sold:P0} of the stock");

        // Target 1.0, with the same guess band as the cart.
        Assert.InRange(truck / Shop, 0.5, 1.5);
    }

    [Fact]
    public void ATruckWithOneHireEarnsMoreThanACart()
    {
        // The design's targets put the truck at about two carts. This doesn't depend on the guessed
        // shop, only on the demand tables, the vehicles and the wage.
        var cart = AverageProfit(CartDay);
        var truck = AverageProfit(seed => TruckDay(servers: 2, hires: 1, seed));
        output.WriteLine($"truck / cart {truck / cart:F2}");

        Assert.True(truck > 1.2 * cart);
    }

    [Fact]
    public void ABadPitchLosesMoneyOnceStaffAndFuelAreCounted()
    {
        // A hire alone on a weekday afternoon in a residential street.
        var plan = new RoutePlan
        {
            VehicleType = "truck",
            Load = new Dictionary<string, int> { [HotDog] = 80, [Soda] = 40 },
            Servers = 1,
            Hires = 1,
            DrivenMetres = 6000,
            Stops = new[] { new RouteStop { Areas = AreaMix.Single("residential"), FromMinute = 14 * 60 + 30, ToMinute = 17 * 60 } }
        };

        var outcome = RouteDay.Run(plan);
        output.WriteLine($"bad pitch {outcome.ProfitCents / 100.0:F0}, sold {outcome.Sold}");

        Assert.True(outcome.ProfitCents < 0);
    }

    [Fact]
    public void FourTrucksStayBelowAMidGameBusiness()
    {
        // Four trucks run by hires alone on good routes, with whichever crew size earns most.
        var truck = Enumerable.Range(1, 3).Max(hires => AverageProfit(seed => TruckDay(servers: hires, hires: hires, seed)));
        var midGame = ShippedData.Rules.Economy.MidGameBusinessShops * Shop;
        output.WriteLine($"four trucks {4 * truck / 100:F0}, mid-game business {midGame / 100:F0}");

        Assert.True(4 * truck < midGame);
    }
}
