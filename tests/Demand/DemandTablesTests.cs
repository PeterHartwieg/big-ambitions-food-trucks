using System;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class DemandTablesTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AdditiveCrowdKeysDefaultWhenAbsent(bool omitBaseline, bool omitSaturation)
    {
        var demand = JObject.Parse(ShippedData.Demand);
        if (omitBaseline) demand.Property("streetBaseline")!.Remove();
        if (omitSaturation) ((JObject)demand["classifier"]!).Property("saturation")!.Remove();

        var tables = DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones);

        foreach (Daypart daypart in Enum.GetValues(typeof(Daypart)))
            Assert.Equal(omitBaseline ? 0.0 : ShippedData.Tables.StreetBaseline(daypart), tables.StreetBaseline(daypart));
        Assert.Equal(omitSaturation ? 1.25 : ShippedData.Tables.Classifier.Saturation, tables.Classifier.Saturation);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.25")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("null")]
    [InlineData("\"0.25\"")]
    public void InvalidStockWeightNamesTheProductAndField(string value)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        economy["products"]!["ba:itemname_cupofcoffee"]!["stockWeight"] = JToken.Parse(value);

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones));

        Assert.Contains("economy.json", error.Message);
        Assert.Contains("ba:itemname_cupofcoffee", error.Message);
        Assert.Contains("stockWeight", error.Message);
    }

    [Fact]
    public void StockWeightDefaultsToOneAndAcceptsAnyPositiveFiniteWeight()
    {
        var economy = JObject.Parse(ShippedData.Economy);
        ((JObject)economy["products"]!["ba:itemname_cupofcoffee"]!).Property("stockWeight")!.Remove();
        economy["products"]!["ba:itemname_hotdog"]!["stockWeight"] = double.Epsilon;
        economy["products"]!["ba:itemname_burger"]!["stockWeight"] = double.MaxValue;
        var tables = DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones);

        Assert.Equal(1.0, tables.Product("ba:itemname_cupofcoffee")!.StockWeight);
        Assert.Equal(double.Epsilon, tables.Product("ba:itemname_hotdog")!.StockWeight);
        Assert.Equal(double.MaxValue, tables.Product("ba:itemname_burger")!.StockWeight);
    }

    [Theory]
    [InlineData("{\"cofee\":2}")]
    [InlineData("{\"coffee\":-1}")]
    [InlineData("{\"coffee\":NaN}")]
    [InlineData("{\"coffee\":Infinity}")]
    [InlineData("{\"coffee\":101}")]
    [InlineData("{\"coffee\":\"2\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    public void InvalidAreaAppetiteNamesTheAreaAndField(string value)
    {
        var demand = JObject.Parse(ShippedData.Demand);
        demand["areas"]!["waterfront"]!["appetite"] = JToken.Parse(value);

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones));

        Assert.Contains("demand.json", error.Message);
        Assert.Contains("areas.waterfront.appetite", error.Message);
    }

    [Fact]
    public void AreaAppetiteDefaultsToOneAndCanDisableACategory()
    {
        var demand = JObject.Parse(ShippedData.Demand);
        demand["areas"]!["waterfront"]!["appetite"] = new JObject { ["coffee"] = 0 };
        var tables = DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones);

        Assert.Equal(0, tables.Area("waterfront").AppetiteFactor("coffee"));
        Assert.Equal(1, tables.Area("waterfront").AppetiteFactor("fastfood"));
        Assert.Equal(1, tables.Area("office").AppetiteFactor("coffee"));
    }

    [Theory]
    [InlineData("demand.json", "schemaVersion", "2")]
    [InlineData("economy.json", "schemaVersion", "3")]
    [InlineData("zones.json", "schemaVersion", "2")]
    [InlineData("demand.json", "passersPerHour", null)]
    [InlineData("demand.json", "streetBaseline", "null")]
    [InlineData("demand.json", "streetBaseline", "[]")]
    [InlineData("demand.json", "streetBaseline.lunch", "0")]
    [InlineData("demand.json", "streetBaseline.night", "-1")]
    [InlineData("demand.json", "streetBaseline.morning", "NaN")]
    [InlineData("demand.json", "streetBaseline.late", "Infinity")]
    [InlineData("demand.json", "streetBaseline.early", "\"0.3\"")]
    [InlineData("demand.json", "streetBaseline.afternoon", null)]
    [InlineData("demand.json", "classifier.saturation", "0")]
    [InlineData("demand.json", "classifier.saturation", "-1")]
    [InlineData("demand.json", "classifier.saturation", "NaN")]
    [InlineData("demand.json", "classifier.saturation", "Infinity")]
    [InlineData("demand.json", "classifier.saturation", "null")]
    [InlineData("demand.json", "classifier.saturation", "\"1.25\"")]
    [InlineData("zones.json", "zones[0].areas.waterfront", "1.1")]
    [InlineData("demand.json", "areas.office.footfall.lunch", "-1")]
    [InlineData("demand.json", "areas.office.footfall", "rename:footfal")]
    [InlineData("demand.json", "categories.coffee.appetite.morning", "1.01")]
    [InlineData("demand.json", "dayparts.afternoon", "\"10:00\"")]
    [InlineData("demand.json", "dayparts.morning", "\"25:00\"")]
    [InlineData("demand.json", "neighbourhoods['ba:neighborhood_midtown'].appetite[0].categories[0]", "\"cofee\"")]
    [InlineData("demand.json", "months.jan.season", "\"wintr\"")]
    [InlineData("demand.json", "classifier.buildingTypes['ba:buildingtype_office']", "{\"offce\":1}")]
    [InlineData("demand.json", "classifier.fallbackArea", "\"residental\"")]
    [InlineData("demand.json", "market.ceiling", "0.2")]
    [InlineData("economy.json", "products['ba:itemname_hotdog'].category", "\"fastfod\"")]
    [InlineData("economy.json", "equipment.hotdoggrill.products[0]", "\"ba:itemname_missing\"")]
    [InlineData("economy.json", "priceCurve.points[1][1]", "1.2")]
    [InlineData("economy.json", "priceCurve.assumedPriceIndex", "1.1")]
    [InlineData("economy.json", "priceCurve.assumedPriceIndex", "1.9")]
    [InlineData("economy.json", "vehicles.cart.unitsPerPiece", "0")]
    [InlineData("economy.json", "vehicles.cart.unitsPerPiece", null)]
    [InlineData("economy.json", "priceCurve.points", "[[1,1]]")]
    [InlineData("zones.json", "zones[0].polygon", "[[0,0],[5,0],[10,0]]")]
    [InlineData("zones.json", "zones[0].polygon", "[[0,0],[10,10],[10,0],[0,10]]")]
    [InlineData("zones.json", "zones[0].polygon", "[[0,0],[0,0],[10,0],[10,10]]")]
    [InlineData("demand.json", "passersPerHour", "9223372036854775808")]
    [InlineData("demand.json", "calendar.daysPerMonth", "99999999999999999999999")]
    [InlineData("zones.json", "schemaVersion", "9223372036854775808")]
    [InlineData("zones.json", "zones[0].mode", "\"overide\"")]
    [InlineData("zones.json", "zones[0].polygon", "[[0,0],[10,0]]")]
    [InlineData("zones.json", "zones[0]", "add-share:0.5")]
    public void BadFieldsNameTheirFileAndPath(string file, string path, string? change)
    {
        var demand = JObject.Parse(ShippedData.Demand);
        var economy = JObject.Parse(ShippedData.Economy);
        var zones = ZonesWithSample();
        var root = file == "demand.json" ? demand : file == "economy.json" ? economy : zones;
        var token = root.SelectToken(path)!;
        var expectedPath = token.Path;

        if (change == null)
        {
            ((JProperty)token.Parent!).Remove();
        }
        else if (change.StartsWith("rename:", StringComparison.Ordinal))
        {
            var property = (JProperty)token.Parent!;
            var newName = change.Substring("rename:".Length);
            property.Replace(new JProperty(newName, token.DeepClone()));
            expectedPath = "areas.office." + newName;
        }
        else if (change.StartsWith("add-share:", StringComparison.Ordinal))
        {
            ((JObject)token)["share"] = JToken.Parse(change.Substring("add-share:".Length));
            expectedPath += ".share";
        }
        else
        {
            token.Replace(JToken.Parse(change));
            if (path == "classifier.buildingTypes['ba:buildingtype_office']") expectedPath += ".offce";
        }

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(demand.ToString(), economy.ToString(), zones.ToString()));

        Assert.Contains(file, error.Message);
        Assert.Contains(expectedPath, error.Message);
        if (change == "rename:footfal") Assert.Contains("not a known field", error.Message);
    }

    [Theory]
    [InlineData("demand.json", "", "is empty")]
    [InlineData("demand.json", "not JSON", "not valid JSON")]
    [InlineData("economy.json", "", "is empty")]
    [InlineData("economy.json", "not JSON", "not valid JSON")]
    [InlineData("zones.json", "", "is empty")]
    [InlineData("zones.json", "not JSON", "not valid JSON")]
    public void BadFileContentsNameTheFile(string file, string contents, string problem)
    {
        var error = Assert.Throws<DemandDataException>(() => DemandTables.Parse(
            file == "demand.json" ? contents : ShippedData.Demand,
            file == "economy.json" ? contents : ShippedData.Economy,
            file == "zones.json" ? contents : ShippedData.Zones));

        Assert.Contains(file, error.Message);
        Assert.Contains(problem, error.Message);
    }

    [Fact]
    public void DuplicateZoneIdNamesTheSecondId()
    {
        var zones = ZonesWithSample();
        var list = (JArray)zones["zones"]!;
        list.Add(list[0].DeepClone());

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(ShippedData.Demand, ShippedData.Economy, zones.ToString()));

        Assert.Contains("zones.json", error.Message);
        Assert.Contains("zones[1].id", error.Message);
        Assert.Contains("repeats zone id", error.Message);
    }

    [Theory]
    [InlineData("demand.json")]
    [InlineData("economy.json")]
    [InlineData("zones.json")]
    public void DuplicateJsonKeysNameTheFileAndField(string file)
    {
        var demand = JObject.Parse(ShippedData.Demand).ToString();
        var economy = JObject.Parse(ShippedData.Economy).ToString();
        var zones = JObject.Parse(ShippedData.Zones).ToString();
        const string original = "\"schemaVersion\": 1";
        const string duplicate = "\"schemaVersion\": 1, \"schemaVersion\": 1";
        if (file == "demand.json") demand = demand.Replace(original, duplicate);
        else if (file == "economy.json") economy = economy.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 2, \"schemaVersion\": 2");
        else zones = zones.Replace(original, duplicate);

        var error = Assert.Throws<DemandDataException>(() => DemandTables.Parse(demand, economy, zones));

        Assert.Contains(file, error.Message);
        Assert.Contains("schemaVersion", error.Message);
    }

    [Fact]
    public void UnderscoreKeysAreIgnoredInObjectsAndTables()
    {
        var demand = JObject.Parse(ShippedData.Demand);
        var economy = JObject.Parse(ShippedData.Economy);
        var zones = ZonesWithSample();
        AddNotes(demand);
        AddNotes(economy);
        AddNotes(zones);

        var tables = DemandTables.Parse(demand.ToString(), economy.ToString(), zones.ToString());

        Assert.Equal(ShippedData.Tables.PassersPerHour, tables.PassersPerHour);
        Assert.Equal(ShippedData.Tables.AreaNames, tables.AreaNames);
        Assert.Equal(ShippedData.Tables.CategoryNames, tables.CategoryNames);
        Assert.Equal(ShippedData.Tables.ProductNames, tables.ProductNames);
        Assert.Equal(ShippedData.Tables.EquipmentIds, tables.EquipmentIds);
        Assert.Single(tables.Zones);
        Assert.Equal(1.0, tables.Zones[0].Areas.ShareOf("waterfront"));
    }

    [Fact]
    public void UnknownNeighbourhoodIsNeutral()
    {
        Assert.Equal(1.0, ShippedData.Tables.Neighbourhood("ba:neighborhood_unknown").Footfall);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(7, 4)]
    [InlineData(8, 5)]
    [InlineData(63, 12)]
    [InlineData(64, 1)]
    [InlineData(0, 3)]
    public void MonthOfUsesTheConfiguredCalendar(int day, int month)
    {
        Assert.Equal(7, ShippedData.Tables.DaysPerMonth);
        Assert.Equal(4, ShippedData.Tables.StartMonth);
        Assert.Equal(month, ShippedData.Tables.MonthOf(day));
    }

    [Theory]
    [InlineData(4, 59, Daypart.Night)]
    [InlineData(5, 0, Daypart.Early)]
    [InlineData(14, 29, Daypart.Lunch)]
    [InlineData(14, 30, Daypart.Afternoon)]
    [InlineData(21, 0, Daypart.Late)]
    [InlineData(1, 59, Daypart.Late)]
    [InlineData(2, 0, Daypart.Night)]
    [InlineData(23, 59, Daypart.Late)]
    public void DaypartAtUsesStartBoundariesAndWrapsMidnight(int hour, int minute, Daypart expected)
    {
        Assert.Equal(expected, ShippedData.Tables.DaypartAt(hour * 60 + minute));
    }

    [Fact]
    public void ContentAfterACommentIsRejected()
    {
        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(ShippedData.Demand + " /* note */ {\"unexpected\":true}", ShippedData.Economy, ShippedData.Zones));

        Assert.StartsWith("demand.json: ", error.Message);
        Assert.NotNull(DemandTables.Parse(ShippedData.Demand + " /* note */", ShippedData.Economy, ShippedData.Zones));
    }

    [Theory]
    [InlineData("areas")]
    [InlineData("categories")]
    [InlineData("classifier.buildingTypes['ba:buildingtype_office']")]
    public void EmptyNamesAreRejected(string path)
    {
        var demand = JObject.Parse(ShippedData.Demand);
        ((JObject)demand.SelectToken(path)!)[""] = JObject.Parse("{}");

        var error = Assert.Throws<DemandDataException>(() =>
            DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones));

        Assert.Contains("demand.json", error.Message);
        Assert.Contains("empty name", error.Message);
    }

    [Fact]
    public void ShippedNeighbourhoodsAreTheGamesSeven()
    {
        Assert.Equal(new[]
        {
            "ba:neighborhood_garmentdistrict", "ba:neighborhood_hellskitchen", "ba:neighborhood_industrycity",
            "ba:neighborhood_lowermanhattan", "ba:neighborhood_midtown", "ba:neighborhood_murrayhill",
            "ba:neighborhood_thehamptons"
        }, ShippedData.Tables.NeighbourhoodNames.OrderBy(n => n, StringComparer.Ordinal));
    }

    private static JObject ZonesWithSample()
    {
        var zones = JObject.Parse(ShippedData.Zones);
        zones["zones"] = JArray.Parse("[{\"id\":\"sample\",\"mode\":\"override\",\"areas\":{\"waterfront\":1},\"polygon\":[[0,0],[10,0],[10,10],[0,10]]}]");
        return zones;
    }

    private static void AddNotes(JToken token)
    {
        foreach (var child in token.Children().ToArray()) AddNotes(child);
        if (token is JObject obj) obj["_testNote"] = new JObject { ["invalidField"] = -999 };
    }
}
