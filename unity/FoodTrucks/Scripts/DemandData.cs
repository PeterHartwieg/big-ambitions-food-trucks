#nullable enable
using System;
using System.IO;
using FoodTrucks.Core.Demand;

namespace FoodTrucks
{
    // The demand and economy tables from the mod folder's data/ files, loaded once at startup.
    // Players can edit them; a bad file is reported in the log and the cart can't open until it's fixed.
    internal static class DemandData
    {
        public static DemandTables? Tables { get; private set; }
        public static string? Error { get; private set; }

        public static void Load(string modRootPath)
        {
            Tables = null;
            Error = null;
            try
            {
                var folder = Path.Combine(modRootPath, "data");
                Tables = DemandTables.Parse(
                    Read(folder, "demand.json"),
                    Read(folder, "economy.json"),
                    Read(folder, "zones.json"));
                Log.Info($"Demand data loaded from {folder}: {Tables.Zones.Count} zones.");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Log.Error("Demand data not loaded; carts can't open. " + ex.Message);
            }
        }

        // zones.json is optional; the other two are required.
        private static string? Read(string folder, string name)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) return File.ReadAllText(path);
            if (name == "zones.json") return null;
            throw new FileNotFoundException($"Missing {path}.");
        }
    }
}
