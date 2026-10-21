using System;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class ShippedDataTests
{
    [Fact]
    public void ShippedFilesLoad()
    {
        var tables = ShippedData.Tables;

        Assert.Equal(8, tables.AreaNames.Count);
        Assert.Equal(9, tables.CategoryNames.Count);
        Assert.Equal(0.3, tables.MarketFloor);
        Assert.Empty(tables.Zones);
        // Raised from 16 on 21 October 2026, after an AI playtest showed carts and trucks earning too little.
        Assert.Equal(20, tables.PassersPerHour);
        // Marketing levels, cheapest first, each priced for both vehicle kinds.
        var marketing = ShippedData.Rules.Marketing.Levels;
        Assert.Equal(new[] { "none", "flyers", "local", "citywide" }, marketing.Select(l => l.Id));
        Assert.All(marketing, l => Assert.Equal(new[] { "cart", "truck" }, l.CostPerDayCents.Keys.OrderBy(k => k, StringComparer.Ordinal)));
    }

    [Fact]
    public void CoffeeMachineMapsOnlyToTheIndustrialMachine()
    {
        var tables = ShippedData.Tables;
        // And to the Bakery mod's bistro machine, when that mod is installed.
        Assert.Equal(new[] { "ba:itemname_industrialcoffeemachine", "bakeryxx:itemname_bistromachine" }, tables.Equipment("coffeemachine")!.GameItems);
        Assert.Same(tables.Equipment("coffeemachine"), tables.EquipmentForGameItem("ba:itemname_industrialcoffeemachine"));
        Assert.Null(tables.EquipmentForGameItem("ba:itemname_cheapcoffeemachine"));
    }

    [Fact]
    public void BakeryModGoodsSellFromTheDisplayCaseAndTheCoffeeMachine()
    {
        var tables = ShippedData.Tables;
        Assert.Same(tables.Equipment("displaycase"), tables.EquipmentForGameItem("bakeryxx:itemname_breadcounter"));
        Assert.Same(tables.Equipment("coffeemachine"), tables.EquipmentForGameItem("bakeryxx:itemname_bistromachine"));
        foreach (var item in new[] { "bakeryxx:itemname_bread", "bakeryxx:itemname_bun", "bakeryxx:itemname_chocolatecookie" })
            Assert.Equal("bakery", tables.Product(item)!.Category);
        foreach (var item in new[] { "bakeryxx:itemname_latte", "bakeryxx:itemname_americano" })
            Assert.Equal("coffee", tables.Product(item)!.Category);
    }

    [Fact]
    public void OfficeLunchRemainsAboutThreeTimesTheAverageOpenHour()
    {
        var model = new DemandModel(ShippedData.Tables);
        var query = new DemandQuery { Areas = AreaMix.Single("office"), Day = DayOfWeek.Wednesday, Month = 5 };
        var average = Enumerable.Range(5 * 60, 21 * 60).Average(minute =>
        {
            query.MinuteOfDay = minute % 1440;
            return model.Estimate(query).Footfall;
        });
        query.MinuteOfDay = 12 * 60;

        Assert.InRange(model.Estimate(query).Footfall / average, 2.5, 3.5);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    public void NearbyNightclubAddsAnEveningCrowdToAnOfficeSpot(DayOfWeek day)
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        // Section 6's in-game case: two offices at 20/40 m and a club at 30 m.
        var buildings = new[]
        {
            new NearbyPlace("ba:buildingtype_office", null, 20),
            new NearbyPlace("ba:buildingtype_office", null, 40)
        };
        var offices = classifier.FromPlaces(buildings);
        var spot = classifier.FromPlaces(buildings.Append(new NearbyPlace("ba:buildingtype_special", "ba:businesstype_nightclub", 30)));
        Assert.Equal(2, spot.Presences.Count);
        Assert.Equal(offices.PresenceOf("office"), spot.PresenceOf("office"));
        Assert.InRange(spot.PresenceOf("nightlife"), double.Epsilon, 1);

        var model = new DemandModel(ShippedData.Tables);
        var query = new DemandQuery
        {
            Areas = spot, Neighbourhood = "ba:neighborhood_midtown",
            Day = day, Month = 5, MinuteOfDay = 12 * 60
        };
        var lunch = model.Estimate(query).Footfall;
        Assert.True(lunch >= 60, $"{day}: Midtown lunch should be busy, got {lunch} passers/h.");
        query.MinuteOfDay = 19 * 60;
        var evening = model.Estimate(query).Footfall;
        // Compare with the study's dense pure office block, rather than pinning a crowd ratio.
        query.Areas = classifier.FromPlaces(Enumerable.Repeat(new NearbyPlace("ba:buildingtype_office", null, 0), 8));
        Assert.True(evening > model.Estimate(query).Footfall);
        query.Areas = offices;
        query.MinuteOfDay = 12 * 60;
        Assert.True(lunch > model.Estimate(query).Footfall);
    }

    [Fact]
    public void SubwayEntranceGivesTransitAMorningPeak()
    {
        var spot = new AreaClassifier(ShippedData.Tables).FromPlaces(new[] { new NearbyPlace("subwayentrance", null, 20) });
        Assert.Single(spot.Presences);
        Assert.InRange(spot.PresenceOf("transit"), double.Epsilon, 1);

        var model = new DemandModel(ShippedData.Tables);
        var query = new DemandQuery { Areas = spot, Day = DayOfWeek.Wednesday, Month = 5, MinuteOfDay = 9 * 60 };
        var peak = model.Estimate(query).Footfall;
        Assert.True(peak > model.Estimate(query).Baseline);
        foreach (var hour in new[] { 6, 12, 15, 18, 22, 3 })
        {
            query.MinuteOfDay = hour * 60;
            Assert.True(model.Estimate(query).Footfall < peak);
        }
        query.MinuteOfDay = 9 * 60;
        query.Day = DayOfWeek.Saturday;
        var weekend = model.Estimate(query);
        Assert.True(weekend.Footfall > weekend.Baseline);
        Assert.True(weekend.Footfall < peak);
    }

    [Theory]
    [InlineData("cupofcoffee", 0.25, 1.0)]
    [InlineData("cupoftea", 0.25, 1.0)]
    [InlineData("croissant", 0.5, 2.0)]
    [InlineData("donut", 0.5, 2.0)]
    [InlineData("cupcake", 0.5, 2.0)]
    [InlineData("hotdog", 1.0, 3.0)]
    public void ProductsShipTheStudysStockWeightsAndServiceTimes(string item, double weight, double minutes)
    {
        var product = ShippedData.Tables.Product("ba:itemname_" + item)!;
        Assert.Equal(weight, product.StockWeight);
        Assert.Equal(minutes, product.ServiceMinutes);
    }

    [Theory]
    [InlineData("dumplings", "streetmeal", 0.5, 3.5, "dumplingsteamer")]
    [InlineData("tacos", "fastfood", 1.0, 3.5, "griddle")]
    [InlineData("choppedcheese", "fastfood", 1.0, 3.5, "griddle")]
    [InlineData("grilledcorn", "snacks", 0.5, 2.0, "corngrill")]
    public void ModFoodsShipTheProposalAndSellFromTheirMachine(string item, string category, double weight, double minutes, string equipment)
    {
        var tables = ShippedData.Tables;
        var product = tables.Product("foodtrucks:itemname_" + item)!;
        Assert.Equal(category, product.Category);
        Assert.Equal(weight, product.StockWeight);
        Assert.Equal(minutes, product.ServiceMinutes);
        Assert.Contains("foodtrucks:itemname_" + item, tables.Equipment(equipment)!.Products);
        Assert.Equal(new[] { "foodtrucks:itemname_" + equipment }, tables.Equipment(equipment)!.GameItems);
    }

    [Theory]
    [InlineData("coffeeshop", "retail")]
    [InlineData("fastfoodrestaurant", "retail")]
    [InlineData("giftshop", "retail")]
    [InlineData("supermarket", "retail")]
    [InlineData("jewelrystore", "retail")]
    [InlineData("clothingstore", "retail")]
    [InlineData("cardealership", "retail")]
    [InlineData("appliancestore", "retail")]
    [InlineData("furniturestore", "retail")]
    [InlineData("officesupplystore", "retail")]
    [InlineData("florist", "retail")]
    [InlineData("fruitandvegetablestore", "retail")]
    [InlineData("hairdresser", "retail")]
    [InlineData("electronicsstore", "retail")]
    [InlineData("bookstore", "retail")]
    [InlineData("school", "residential")]
    [InlineData("hospital", "office")]
    [InlineData("clinic", "office")]
    [InlineData("irs", "office")]
    [InlineData("gasstation", "industrial")]
    public void AddedBusinessesClassifyByTheirUse(string business, string area)
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var mix = classifier.FromPlaces(new[] { new NearbyPlace("ba:buildingtype_special", "ba:businesstype_" + business, 0) });

        Assert.Single(mix.Shares);
        Assert.Equal(1 - Math.Exp(-1 / 1.25), mix.PresenceOf(area), 12);
    }

    [Fact]
    public void GymAddsRetailAndResidentialPresences()
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_special", "ba:businesstype_gym", 0)
        });

        Assert.Equal(1 - Math.Exp(-0.5 / 1.25), mix.PresenceOf("retail"), 12);
        Assert.Equal(1 - Math.Exp(-0.5 / 1.25), mix.PresenceOf("residential"), 12);
    }

    [Fact]
    public void EmptyBusinessesUseTheBuildingAndUnclassifiedSpecialBuildingsLeaveNoPresence()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);

        Assert.False(ShippedData.Tables.Classifier.BusinessTypes.ContainsKey("ba:businesstype_empty"));
        Assert.False(ShippedData.Tables.Classifier.BuildingTypes.ContainsKey("ba:buildingtype_special"));
        Assert.Equal(1 - Math.Exp(-1 / 1.25), classifier.FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_office", "ba:businesstype_empty", 0)
        }).PresenceOf("office"), 12);
        Assert.Empty(classifier.FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_special", "ba:businesstype_empty", 0)
        }).Presences);
    }

    [Fact]
    public void DataNotesMarkTheAcceptedTuningAndUnverifiedEquipment()
    {
        var demand = JObject.Parse(ShippedData.Demand);
        var economy = JObject.Parse(ShippedData.Economy);

        Assert.Contains("accepted study tuning", demand["classifier"]!["_coverage"]!.Value<string>());
        Assert.Contains("buildingtype_special stays unclassified", demand["classifier"]!["_coverage"]!.Value<string>());
        Assert.Contains("1.0 assets is unverified", economy["_equipment"]!.Value<string>());
    }
}
