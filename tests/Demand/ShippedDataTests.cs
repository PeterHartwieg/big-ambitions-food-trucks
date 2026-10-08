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

        Assert.Equal(7, tables.AreaNames.Count);
        Assert.Equal(6, tables.CategoryNames.Count);
        Assert.Equal(0.3, tables.MarketFloor);
        Assert.Empty(tables.Zones);
    }

    [Fact]
    public void CoffeeMachineMapsOnlyToTheIndustrialMachine()
    {
        var tables = ShippedData.Tables;
        Assert.Equal(new[] { "ba:itemname_industrialcoffeemachine" }, tables.Equipment("coffeemachine")!.GameItems);
        Assert.Same(tables.Equipment("coffeemachine"), tables.EquipmentForGameItem("ba:itemname_industrialcoffeemachine"));
        Assert.Null(tables.EquipmentForGameItem("ba:itemname_cheapcoffeemachine"));
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

    [Fact]
    public void NearbyNightclubLeadsAMixedSpotAndBringsAnEveningPeak()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var buildings = Enumerable.Range(0, 4).Select(_ => new NearbyPlace("ba:buildingtype_office", null, 0));
        var spot = classifier.FromPlaces(buildings.Append(new NearbyPlace("ba:buildingtype_special", "ba:businesstype_nightclub", 40)));
        Assert.Equal(2, spot.Shares.Count);
        Assert.True(spot.ShareOf("nightlife") > spot.ShareOf("office"));

        var model = new DemandModel(ShippedData.Tables);
        var query = new DemandQuery { Areas = spot, Day = DayOfWeek.Friday, Month = 5, MinuteOfDay = 12 * 60 };
        var lunch = model.Estimate(query).Footfall;
        query.MinuteOfDay = 18 * 60;
        Assert.True(model.Estimate(query).Footfall > lunch);
    }

    [Fact]
    public void SubwayEntranceGivesTransitAMorningPeak()
    {
        var spot = new AreaClassifier(ShippedData.Tables).FromPlaces(new[] { new NearbyPlace("subwayentrance", null, 20) });
        Assert.Equal(1.0, spot.ShareOf("transit"));

        var model = new DemandModel(ShippedData.Tables);
        var query = new DemandQuery { Areas = spot, Day = DayOfWeek.Wednesday, Month = 5, MinuteOfDay = 9 * 60 };
        var peak = model.Estimate(query).Footfall;
        Assert.Equal(22 * 2.5, peak);
        foreach (var hour in new[] { 6, 12, 15, 18, 22, 3 })
        {
            query.MinuteOfDay = hour * 60;
            Assert.True(model.Estimate(query).Footfall < peak);
        }
        query.MinuteOfDay = 9 * 60;
        query.Day = DayOfWeek.Saturday;
        Assert.Equal(peak * 0.5, model.Estimate(query).Footfall);
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
        Assert.Equal(1.0, mix.ShareOf(area));
    }

    [Fact]
    public void GymBlendsRetailAndResidential()
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_special", "ba:businesstype_gym", 0)
        });

        Assert.Equal(0.5, mix.ShareOf("retail"));
        Assert.Equal(0.5, mix.ShareOf("residential"));
    }

    [Fact]
    public void EmptyBusinessesUseTheBuildingAndSpecialWithoutBusinessFallsBack()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);

        Assert.False(ShippedData.Tables.Classifier.BusinessTypes.ContainsKey("ba:businesstype_empty"));
        Assert.False(ShippedData.Tables.Classifier.BuildingTypes.ContainsKey("ba:buildingtype_special"));
        Assert.Equal(1.0, classifier.FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_office", "ba:businesstype_empty", 0)
        }).ShareOf("office"));
        Assert.Equal(1.0, classifier.FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_special", "ba:businesstype_empty", 0)
        }).ShareOf(ShippedData.Tables.Classifier.FallbackArea));
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
