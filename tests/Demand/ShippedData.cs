using System;
using System.IO;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Tests.Demand;

// The data files shipped in data/, copied next to the test assembly.
internal static class ShippedData
{
    private static readonly Lazy<RuleTables> rules = new(() => RuleTables.Parse(RulesJson));
    private static readonly Lazy<DemandTables> tables = new(() => DemandTables.Parse(Demand, Economy, Zones));

    public static string Demand => Read("demand.json");
    public static string Economy => Read("economy.json");
    public static string Zones => Read("zones.json");
    public static DemandTables Tables => tables.Value;
    public static string RulesJson => Read("rules.json");
    public static RuleTables Rules => rules.Value;

    // A second drink for selection tests; the shipped cooler serves soda only.
    public const string SecondColdDrink = "test:itemname_colddrink";
    public static DemandTables WithSecondColdDrink()
    {
        var economy = JObject.Parse(Economy);
        economy["products"]![SecondColdDrink] = economy["products"]!["ba:itemname_sodacan"]!.DeepClone();
        return DemandTables.Parse(Demand, economy.ToString(), Zones);
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));
}
