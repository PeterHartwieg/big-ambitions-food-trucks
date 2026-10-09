using System;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class EquipmentMappingTests
{
    [Theory]
    [InlineData("hotdoggrill", "ba:itemname_hotdoggrill")]
    [InlineData("burgerstation", "ba:itemname_industrialgrill")]
    [InlineData("burgerstation", "ba:itemname_industrialfryermachine")]
    [InlineData("coffeemachine", "ba:itemname_industrialcoffeemachine")]
    [InlineData("icecreamcounter", "ba:itemname_icecreamcounter")]
    [InlineData("pizzaoven", "ba:itemname_pizzaoven")]
    [InlineData("cooler", "ba:itemname_drinksfridge")]
    [InlineData("cooler", "ba:itemname_largedrinksfridge")]
    [InlineData("displaycase", "ba:itemname_bakeryshowcase")]
    public void VanillaItemsMapToTheirEquipment(string id, string item)
    {
        var piece = ShippedData.Tables.EquipmentForGameItem(item);

        Assert.Same(ShippedData.Tables.Equipment(id), piece);
        Assert.Contains(item, piece!.GameItems);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hotdoggrill")]
    [InlineData("ba:itemname_hotdog")]
    [InlineData("ba:itemname_missing")]
    [InlineData("ba:itemname_cheapcoffeemachine")]
    [InlineData("BA:ITEMNAME_HOTDOGGRILL")]
    public void OtherNamesDoNotMap(string? item)
    {
        Assert.Null(ShippedData.Tables.EquipmentForGameItem(item));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrEmptyGameItemsMeansNoMapping(bool missing)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        var piece = (JObject)economy["equipment"]!["hotdoggrill"]!;
        if (missing) piece.Property("gameItems")!.Remove();
        else piece["gameItems"] = new JArray();

        var tables = DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones);

        Assert.Empty(tables.Equipment("hotdoggrill")!.GameItems);
        Assert.Null(tables.EquipmentForGameItem("ba:itemname_hotdoggrill"));
        Assert.NotNull(tables.EquipmentForGameItem("ba:itemname_drinksfridge"));
    }

    [Theory]
    [InlineData("null", "gameItems", "must be an array")]
    [InlineData("{}", "gameItems", "must be an array")]
    [InlineData("\"ba:itemname_hotdoggrill\"", "gameItems", "must be an array")]
    [InlineData("[null]", "gameItems[0]", "must be a string")]
    [InlineData("[42]", "gameItems[0]", "must be a string")]
    [InlineData("[\"\"]", "gameItems[0]", "must not be empty")]
    [InlineData("[\"  \\t\"]", "gameItems[0]", "must not be blank")]
    public void InvalidGameItemsNameTheFileFieldAndProblem(string json, string field, string problem)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        economy["equipment"]!["hotdoggrill"]!["gameItems"] = JToken.Parse(json);

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones));

        Assert.Contains("economy.json", error.Message);
        Assert.Contains("equipment.hotdoggrill." + field, error.Message);
        Assert.Contains(problem, error.Message);
    }

    [Theory]
    [InlineData("hotdoggrill", 1)]
    [InlineData("cooler", 2)]
    public void RepeatedGameItemsNameTheOriginalEquipment(string secondId, int index)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        ((JArray)economy["equipment"]![secondId]!["gameItems"]!).Add("ba:itemname_hotdoggrill");

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones));

        Assert.Contains("economy.json", error.Message);
        Assert.Contains($"equipment.{secondId}.gameItems[{index}]", error.Message);
        Assert.Contains("ba:itemname_hotdoggrill", error.Message);
        Assert.Contains("already maps to equipment 'hotdoggrill'", error.Message);
    }

    [Theory]
    [InlineData("coffeemachine", "CoffeeStation")]
    [InlineData("cooler", "DrinksCooler")]
    [InlineData("hotdoggrill", "HotDogRoller")]
    [InlineData("burgerstation", "BurgerStation")]
    [InlineData("icecreamcounter", "IceCreamCabinet")]
    [InlineData("pizzaoven", "PizzaOven")]
    [InlineData("displaycase", "PastryCase")]
    public void ShippedDisplayModels(string id, string? display)
    {
        Assert.Equal(display, ShippedData.Tables.Equipment(id)!.Display);
    }

    [Theory]
    [InlineData("\"\"", "must not be empty")]
    [InlineData("3", "must be a string")]
    [InlineData("\"Coffee/Station\"", "must be a model name")]
    public void InvalidDisplayNamesTheField(string json, string problem)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        economy["equipment"]!["hotdoggrill"]!["display"] = JToken.Parse(json);

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones));

        Assert.Contains("equipment.hotdoggrill.display", error.Message);
        Assert.Contains(problem, error.Message);
    }
}
