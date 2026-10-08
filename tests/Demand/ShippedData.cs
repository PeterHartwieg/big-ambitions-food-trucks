using System;
using System.IO;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Tests.Demand;

// The data files shipped in data/, copied next to the test assembly.
internal static class ShippedData
{
    private static readonly Lazy<DemandTables> tables = new(() => DemandTables.Parse(Demand, Economy, Zones));

    public static string Demand => Read("demand.json");
    public static string Economy => Read("economy.json");
    public static string Zones => Read("zones.json");
    public static DemandTables Tables => tables.Value;

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));
}
