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

        Assert.Equal(6, tables.AreaNames.Count);
        Assert.Equal(6, tables.CategoryNames.Count);
        Assert.Equal(0.3, tables.MarketFloor);
        Assert.Empty(tables.Zones);
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
    public void DataNotesMarkTheDraftWeightsAndUnverifiedEquipment()
    {
        var demand = JObject.Parse(ShippedData.Demand);
        var economy = JObject.Parse(ShippedData.Economy);

        Assert.Contains("tuning guesses", demand["classifier"]!["_coverage"]!.Value<string>());
        Assert.Contains("buildingtype_special stays unclassified", demand["classifier"]!["_coverage"]!.Value<string>());
        Assert.Contains("1.0 assets is unverified", economy["_equipment"]!.Value<string>());
    }
}
