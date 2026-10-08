using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class AdditiveCrowdTests
{
    private static readonly NearbyPlace[] Offices =
    {
        new("ba:buildingtype_office", null, 20),
        new("ba:buildingtype_office", null, 40)
    };
    private static readonly NearbyPlace Club = new("ba:buildingtype_retail", "ba:businesstype_nightclub", 30);

    [Theory]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    public void OfficesAndNightclubKeepTheLunchCrowdAndAddAnEveningCrowd(DayOfWeek day)
    {
        var tables = ShippedData.Tables;
        var classifier = new AreaClassifier(tables);
        var offices = classifier.Classify(Offices, 0, 0);
        var both = classifier.Classify(Offices.Append(Club), 0, 0);
        var model = new DemandModel(tables);

        Assert.Equal(offices.PresenceOf("office"), both.PresenceOf("office"));
        var lunch = model.Estimate(Query(both, 12, day));
        var officeLunch = model.Estimate(Query(offices, 12, day));
        Assert.True(lunch.Footfall >= officeLunch.Footfall);
        Assert.True(officeLunch.Footfall > 2 * officeLunch.Baseline);
        var evening = model.Estimate(Query(both, 18, day));
        var officeEvening = model.Estimate(Query(offices, 18, day));
        Assert.True(evening.Footfall > officeEvening.Footfall);
        Assert.Equal(officeLunch.AreaFootfall["office"], lunch.AreaFootfall["office"]);
        Assert.Equal(lunch.Baseline + lunch.AreaFootfall.Values.Sum(), lunch.Footfall, 9);
    }

    [Fact]
    public void NightlifeLunchIsQuietButNeverBelowTheBaselineAtAnyHour()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var model = new DemandModel(ShippedData.Tables);
        var nightlife = classifier.FromPlaces(new[] { Club });
        var lunch = model.Estimate(Query(nightlife, 12));
        Assert.InRange(lunch.Footfall / lunch.Baseline, 1, 1.2);
        Assert.True(lunch.Footfall < model.Estimate(Query(classifier.FromPlaces(Offices), 12)).Footfall / 2);
        foreach (var day in Enum.GetValues<DayOfWeek>())
        foreach (var hour in Enumerable.Range(0, 24))
        {
            var estimate = model.Estimate(Query(nightlife, hour, day));
            Assert.True(estimate.Baseline > 0);
            Assert.True(estimate.Footfall >= estimate.Baseline);
        }
    }

    [Fact]
    public void PresenceSaturatesWithDiminishingReturnsAndRespondsToTheSetting()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        double Presence(int count) => classifier.FromPlaces(Enumerable.Repeat(Offices[0], count)).PresenceOf("office");
        var one = Presence(1);
        var two = Presence(2);
        var three = Presence(3);
        Assert.InRange(two, 0.6, 0.8);
        Assert.True(two > one);
        Assert.True(three - two < two - one);
        Assert.InRange(Presence(1000), 0.999999, 1);

        var demand = JObject.Parse(ShippedData.Demand);
        demand["classifier"]!["saturation"] = 2.5;
        demand["classifier"]!["maxAreas"] = 1;
        var custom = new AreaClassifier(DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones));
        var mix = custom.FromPlaces(Offices.Append(Club));
        Assert.Equal(2, mix.Presences.Count);
        Assert.Equal(1 - Math.Exp(-1.4 / 2.5), mix.PresenceOf("office"), 12);
    }

    [Fact]
    public void AppetiteFollowsTheOfficeCrowdAtLunchAndNightlifeCrowdAtNight()
    {
        // Distinct crowd preferences, with equal base category appetite, isolate the area weighting.
        var demand = JObject.Parse(ShippedData.Demand);
        demand["areas"]!["office"]!["appetite"] = new JObject { ["coffee"] = 2, ["pizza"] = 0.5 };
        demand["areas"]!["nightlife"]!["appetite"] = new JObject { ["coffee"] = 0.5, ["pizza"] = 2 };
        foreach (var category in new[] { "coffee", "pizza" })
        foreach (var part in new[] { "lunch", "late", "night" })
            demand["categories"]![category]!["appetite"]![part] = 0.2;
        var tables = DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones);
        var model = new DemandModel(tables);
        var spot = new AreaClassifier(tables).FromPlaces(Offices.Append(Club));

        foreach (var hour in new[] { 12, 22, 3 })
        {
            var query = Query(spot, hour);
            query.Menu = new[] { "ba:itemname_cupofcoffee" };
            var coffee = model.Estimate(query);
            query.Menu = new[] { "ba:itemname_pizza" };
            var pizza = model.Estimate(query);
            var expectedCoffee = 0.2 * (coffee.Baseline + coffee.AreaFootfall["office"] * 2
                + coffee.AreaFootfall["nightlife"] * 0.5) / coffee.Footfall;
            Assert.Equal(expectedCoffee, coffee.Appetite, 9);
            Assert.True(hour == 12 ? coffee.Appetite > pizza.Appetite : pizza.Appetite > coffee.Appetite);
            Assert.True(hour == 12 ? coffee.AreaFootfall["office"] > coffee.AreaFootfall["nightlife"]
                : coffee.AreaFootfall["nightlife"] > coffee.AreaFootfall["office"]);
        }
    }

    [Fact]
    public void BaselineAndAreaBreakdownKeepAllCommonFactorsAndExcludeReputation()
    {
        var tables = ShippedData.Tables;
        var spot = new AreaClassifier(tables).FromPlaces(Offices.Append(Club));
        var model = new DemandModel(tables);
        var query = Query(spot, 1, DayOfWeek.Saturday);
        var plain = model.Estimate(query);
        query.Neighbourhood = "ba:neighborhood_midtown";
        query.Month = 1;
        query.Raining = true;
        query.Promotion = 1.2;
        query.Reputation = 0.75;
        var scaled = model.Estimate(query);
        // Friday night; Midtown's late factor, winter month and rain affect every crowd.
        var factor = 1.3 * 0.6 * 0.6 * 0.5 * 1.2;
        Assert.Equal(plain.Footfall * factor, scaled.Footfall, 9);
        Assert.Equal(plain.Baseline * factor, scaled.Baseline, 9);
        foreach (var area in plain.AreaFootfall.Keys)
            Assert.Equal(plain.AreaFootfall[area] * factor, scaled.AreaFootfall[area], 9);
        Assert.Equal(scaled.Footfall * scaled.Appetite * 0.75, scaled.CustomersPerHour, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BaselineOnlySpotAndZeroPromotionGiveFiniteResults(double promotion)
    {
        var query = Query(AreaMix.FromPresences(new Dictionary<string, double>()), 12);
        query.Promotion = promotion;
        var tables = ShippedData.Tables;
        var estimate = new DemandModel(tables).Estimate(query);
        Assert.Equal(tables.PassersPerHour * tables.StreetBaseline(Daypart.Lunch) * promotion, estimate.Footfall, 9);
        Assert.Equal(estimate.Footfall, estimate.Baseline);
        Assert.Empty(estimate.AreaFootfall);
        Assert.Equal(tables.Category("fastfood").Appetite(Daypart.Lunch), estimate.Appetite, 9);
        Assert.Equal(estimate.Footfall * estimate.Appetite, estimate.CustomersPerHour, 9);
    }

    private static DemandQuery Query(AreaMix areas, int hour, DayOfWeek day = DayOfWeek.Wednesday) => new()
    {
        Areas = areas, MinuteOfDay = hour * 60, Day = day, Month = 4,
        Menu = new[] { "ba:itemname_hotdog" }
    };
}
