using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class DemandModelTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string Burger = "ba:itemname_burger";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string IceCream = "ba:itemname_icecream";
    private const string Soda = "ba:itemname_sodacan";
    private const string Midtown = "ba:neighborhood_midtown";

    private static readonly DemandModel model = new(ShippedData.Tables);

    private static DemandQuery Query(string area, int hour, int minute = 0, params string[] menu) => new()
    {
        Areas = AreaMix.Single(area),
        Menu = menu.Length == 0 ? new[] { HotDog } : menu,
        MinuteOfDay = hour * 60 + minute,
        Day = DayOfWeek.Wednesday,
        Month = 4
    };

    [Fact]
    public void OfficeLunchIsThreeTimesAnAverageHour()
    {
        var estimate = model.Estimate(Query("office", 12));

        Assert.Equal(Daypart.Lunch, estimate.Daypart);
        Assert.Equal(3.0 * ShippedData.Tables.PassersPerHour, estimate.Footfall, 9);
    }

    [Fact]
    public void OfficeLunchIsAboutThreeTimesTheSpotsAverageHour()
    {
        // The average over the open day, 05:00 to 02:00, on a weekday.
        var minutes = Enumerable.Range(5 * 60, 21 * 60).Select(m => m % 1440).ToList();
        var average = minutes.Average(m => model.Estimate(new DemandQuery
        {
            Areas = AreaMix.Single("office"), Menu = new[] { HotDog }, MinuteOfDay = m, Day = DayOfWeek.Wednesday, Month = 4
        }).Footfall);
        var lunch = model.Estimate(Query("office", 12)).Footfall;

        Assert.InRange(lunch / average, 2.5, 3.5);
    }

    [Fact]
    public void OfficeLunchIsTheHotDogPeak()
    {
        var lunch = model.Estimate(Query("office", 12)).CustomersPerHour;

        foreach (var hour in Enumerable.Range(0, 24).Where(h => h < 11 || h > 14))
            Assert.True(model.Estimate(Query("office", hour)).CustomersPerHour < lunch / 2, $"hour {hour}");
    }

    [Fact]
    public void WinterIceCreamIsAFifthOfSpring()
    {
        var spring = model.Estimate(Query("retail", 15, 0, IceCream));
        var winterQuery = Query("retail", 15, 0, IceCream);
        winterQuery.Month = 1;
        var winter = model.Estimate(winterQuery);

        Assert.Equal(Season.Winter, winter.Season);
        Assert.Equal(0.2, winter.Appetite / spring.Appetite, 9);
        Assert.Equal(0.6, winter.Footfall / spring.Footfall, 9);
        Assert.Equal(0.12, winter.CustomersPerHour / spring.CustomersPerHour, 9);
    }

    [Fact]
    public void SummerRaisesIceCream()
    {
        var query = Query("retail", 15, 0, IceCream);
        query.Month = 7;

        Assert.Equal(Season.Summer, model.Estimate(query).Season);
        Assert.Equal(0.20 * 1.3, model.Estimate(query).Appetite, 9);
    }

    [Theory]
    [InlineData("office", 6, 0.8)]
    [InlineData("office", 9, 1.2)]
    [InlineData("office", 15, 0.5)]
    [InlineData("retail", 15, 1.3)]
    [InlineData("residential", 18, 1.0)]
    [InlineData("nightlife", 22, 2.0)]
    [InlineData("industrial", 6, 1.5)]
    [InlineData("waterfront", 15, 1.2)]
    public void WeekdayFootfallFollowsTheAreaTable(string area, int hour, double factor)
    {
        var wednesday = ShippedData.Tables.Area(area).DayFactor(DayOfWeek.Wednesday);

        Assert.Equal(factor * wednesday * ShippedData.Tables.PassersPerHour, model.Estimate(Query(area, hour)).Footfall, 9);
    }

    [Fact]
    public void AMixedSpotBlendsItsAreas()
    {
        var query = Query("office", 12);
        query.Areas = AreaMix.FromWeights(new Dictionary<string, double> { ["office"] = 0.7, ["nightlife"] = 0.3 });

        Assert.Equal((0.7 * 3.0 + 0.3 * 0.4 * 0.7) * ShippedData.Tables.PassersPerHour, model.Estimate(query).Footfall, 9);
    }

    [Theory]
    [InlineData(DayOfWeek.Saturday, 0.2)]
    [InlineData(DayOfWeek.Friday, 0.9)]
    public void OfficesEmptyAtTheWeekend(DayOfWeek day, double factor)
    {
        var query = Query("office", 12);
        query.Day = day;

        Assert.Equal(3.0 * factor * ShippedData.Tables.PassersPerHour, model.Estimate(query).Footfall, 9);
    }

    [Fact]
    public void AfterMidnightCountsAsThePreviousNight()
    {
        var query = Query("nightlife", 1);
        query.Day = DayOfWeek.Saturday;

        var estimate = model.Estimate(query);

        Assert.Equal(Daypart.Late, estimate.Daypart);
        // Friday's 1.4, not Saturday's 1.5.
        Assert.Equal(2.0 * 1.4 * ShippedData.Tables.PassersPerHour, estimate.Footfall, 9);
    }

    [Fact]
    public void NeighbourhoodDayRulesAfterMidnightUseThePreviousDay()
    {
        var saturdayNight = Query("nightlife", 1, 0, "ba:itemname_pizza");
        saturdayNight.Neighbourhood = "ba:neighborhood_murrayhill";
        saturdayNight.Day = DayOfWeek.Sunday;
        var sundayNight = Query("nightlife", 1, 0, "ba:itemname_pizza");
        sundayNight.Neighbourhood = "ba:neighborhood_murrayhill";
        sundayNight.Day = DayOfWeek.Monday;

        // Murray Hill's late pizza rule runs Thursday to Saturday nights.
        Assert.Equal(0.30 * 1.4, model.Estimate(saturdayNight).Appetite, 9);
        Assert.Equal(0.30, model.Estimate(sundayNight).Appetite, 9);
    }

    [Fact]
    public void NoAddOnsWhenTheMainItemBringsNobody()
    {
        // Pizza appetite is zero at 06:00, so every customer came for a drink.
        var estimate = model.Estimate(Query("office", 6, 0, "ba:itemname_pizza", Soda));

        Assert.True(estimate.CustomersPerHour > 0);
        Assert.Equal(0, estimate.AddOnsPerCustomer);
    }

    [Fact]
    public void MenuOrderNeverChangesTheResult()
    {
        var market = new Dictionary<string, double> { [HotDog] = 30, [Burger] = 90, ["ba:itemname_frenchfries"] = 100 };
        var first = Query("office", 12, 0, HotDog, Burger, "ba:itemname_frenchfries");
        first.Promotion = 0.5;
        first.MarketDemand = market;
        var second = Query("office", 12, 0, Burger, "ba:itemname_frenchfries", HotDog);
        second.Promotion = 0.5;
        second.MarketDemand = market;

        Assert.Equal(model.Estimate(first).CustomersPerHour, model.Estimate(second).CustomersPerHour);
    }

    [Fact]
    public void MidtownMorningCoffeeIsStronger()
    {
        var plain = Query("office", 9, 0, Coffee);
        var midtown = Query("office", 9, 0, Coffee);
        midtown.Neighbourhood = Midtown;

        Assert.Equal(0.30, model.Estimate(plain).Appetite, 9);
        Assert.Equal(0.30 * 1.4, model.Estimate(midtown).Appetite, 9);
        Assert.Equal(1.3 * model.Estimate(plain).Footfall, model.Estimate(midtown).Footfall, 9);
    }

    [Fact]
    public void HamptonsEmptyInWinter()
    {
        var summer = Query("waterfront", 15);
        summer.Neighbourhood = "ba:neighborhood_thehamptons";
        summer.Month = 7;
        var winter = Query("waterfront", 15);
        winter.Neighbourhood = "ba:neighborhood_thehamptons";
        winter.Month = 1;

        // Waterfront afternoon 1.2 on a Wednesday (0.7), Hamptons 0.7, then the season and month.
        Assert.Equal(0.7 * 1.6 * 1.2 * 0.7 * ShippedData.Tables.PassersPerHour, model.Estimate(summer).Footfall, 9);
        // The Hamptons' winter 0.5 times the winter month's 0.6: footfall 0.3 of normal.
        Assert.Equal(0.7 * 0.5 * 0.6 * 1.2 * 0.7 * ShippedData.Tables.PassersPerHour, model.Estimate(winter).Footfall, 9);
    }

    [Fact]
    public void UnknownNeighbourhoodIsNeutral()
    {
        var plain = Query("office", 12);
        var unknown = Query("office", 12);
        unknown.Neighbourhood = "modded:neighborhood_elsewhere";

        Assert.Equal(model.Estimate(plain).CustomersPerHour, model.Estimate(unknown).CustomersPerHour, 9);
    }

    [Fact]
    public void RainHalvesFootfallAndShiftsAppetite()
    {
        var dry = Query("office", 9, 0, Coffee);
        var wet = Query("office", 9, 0, Coffee);
        wet.Raining = true;
        var wetIceCream = Query("retail", 15, 0, IceCream);
        wetIceCream.Raining = true;

        Assert.Equal(0.5 * model.Estimate(dry).Footfall, model.Estimate(wet).Footfall, 9);
        Assert.Equal(0.30 * 1.3, model.Estimate(wet).Appetite, 9);
        Assert.Equal(0.20 * 0.3, model.Estimate(wetIceCream).Appetite, 9);
    }

    [Fact]
    public void FurtherCategoriesAddHalf()
    {
        var estimate = model.Estimate(Query("office", 12, 0, HotDog, Coffee));

        Assert.Equal(0.30 + 0.5 * 0.08, estimate.Appetite, 9);
        Assert.Equal(0.30 / 0.34, estimate.CustomerShares["fastfood"], 9);
        Assert.Equal(0.04 / 0.34, estimate.CustomerShares["coffee"], 9);
    }

    [Fact]
    public void TwoItemsOfOneCategoryCountOnce()
    {
        Assert.Equal(0.30, model.Estimate(Query("office", 12, 0, HotDog, Burger)).Appetite, 9);
    }

    [Fact]
    public void ColdDrinksMostlyAddToTheTicket()
    {
        var food = model.Estimate(Query("office", 12, 0, HotDog));
        var withDrinks = model.Estimate(Query("office", 12, 0, HotDog, Soda));
        var drinksOnly = model.Estimate(Query("office", 12, 0, Soda));

        var drinkPart = 0.5 * 0.15 * 0.25;
        Assert.Equal(0.30 + drinkPart, withDrinks.Appetite, 9);
        // Drink-only customers get no add-on drink, so the add-on rate counts only the hot dog customers.
        Assert.Equal(0.30 / (0.30 + drinkPart) * 0.15 * 0.75 * 2.0, withDrinks.AddOnsPerCustomer, 9);
        Assert.Equal(drinkPart / (0.30 + drinkPart), withDrinks.CustomerShares["colddrinks"], 9);
        Assert.Equal(0, food.AddOnsPerCustomer);
        Assert.Equal(0.15 * 0.25, drinksOnly.Appetite, 9);
        Assert.Equal(0, drinksOnly.AddOnsPerCustomer);
    }

    [Theory]
    [InlineData(100, 1.0)]
    [InlineData(50, 0.5)]
    [InlineData(10, 0.3)]
    [InlineData(0, 0.3)]
    [InlineData(150, 1.0)]
    public void MarketDemandScalesTheCategoryWithAFloor(double demand, double factor)
    {
        var query = Query("office", 12);
        query.MarketDemand = new Dictionary<string, double> { [HotDog] = demand };

        var estimate = model.Estimate(query);

        Assert.Equal(0.30 * factor, estimate.Appetite, 9);
        Assert.Equal(factor, estimate.Market, 9);
    }

    [Fact]
    public void MarketHitsOnlyItsOwnCategory()
    {
        var query = Query("office", 12, 0, HotDog, Coffee);
        query.MarketDemand = new Dictionary<string, double> { [HotDog] = 30, [Coffee] = 100 };

        // Fast food falls to 0.09 and still leads; coffee adds half its 0.08.
        Assert.Equal(0.30 * 0.3 + 0.5 * 0.08, model.Estimate(query).Appetite, 9);
    }

    [Fact]
    public void MarketAveragesTheCategorysItems()
    {
        var query = Query("office", 12, 0, HotDog, Burger);
        query.MarketDemand = new Dictionary<string, double> { [HotDog] = 100, [Burger] = 40 };

        Assert.Equal(0.30 * 0.7, model.Estimate(query).Appetite, 9);
    }

    [Fact]
    public void ReputationAndPromotionScaleCustomers()
    {
        var plain = model.Estimate(Query("office", 12)).CustomersPerHour;
        var query = Query("office", 12);
        query.Reputation = 0.75;
        query.Promotion = 1.2;

        Assert.Equal(plain * 0.75 * 1.2, model.Estimate(query).CustomersPerHour, 9);
    }

    [Fact]
    public void CustomersAreFootfallTimesAppetite()
    {
        var estimate = model.Estimate(Query("office", 12));

        Assert.Equal(90 * 0.30, estimate.CustomersPerHour, 9);
    }

    [Fact]
    public void EmptyOrUnknownMenuBringsNobody()
    {
        var empty = model.Estimate(Query("office", 12, 0, "ba:itemname_cheapflower"));

        Assert.Equal(0, empty.CustomersPerHour);
        Assert.Empty(empty.CustomerShares);
        Assert.Equal(1.0, empty.Market);
    }

    [Fact]
    public void SameQueryGivesTheSameAnswer()
    {
        var first = model.Estimate(Query("office", 12, 0, HotDog, Coffee, Soda));
        var second = model.Estimate(Query("office", 12, 0, Soda, Coffee, HotDog));

        Assert.Equal(first.CustomersPerHour, second.CustomersPerHour);
        Assert.Equal(first.AddOnsPerCustomer, second.AddOnsPerCustomer);
    }

    [Fact]
    public void BadQueriesThrow()
    {
        Assert.Throws<ArgumentNullException>(() => model.Estimate(null!));
        Assert.Throws<ArgumentException>(() => model.Estimate(new DemandQuery { Menu = new[] { HotDog } }));
        Assert.Throws<ArgumentException>(() => model.Estimate(new DemandQuery { Areas = AreaMix.Single("beach"), Menu = new[] { HotDog } }));

        var lateMinute = Query("office", 12);
        lateMinute.MinuteOfDay = 1440;
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Estimate(lateMinute));

        var badMonth = Query("office", 12);
        badMonth.Month = 13;
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Estimate(badMonth));

        var badReputation = Query("office", 12);
        badReputation.Reputation = double.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Estimate(badReputation));

        var badDay = Query("office", 12);
        badDay.Day = (DayOfWeek)7;
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Estimate(badDay));

        var badPromotion = Query("office", 12);
        badPromotion.Promotion = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Estimate(badPromotion));
    }
}
