#nullable enable
using System;
using System.IO;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Rules;

namespace FoodTrucks
{
    // The demand, economy and rule tables from the mod folder's data/ files, loaded once at startup.
    // Players can edit them; a bad file is reported in the log and the cart can't open until it's fixed.
    internal static class DemandData
    {
        public static DemandTables? Tables { get; private set; }
        // rules.json: queues and service for now; licences, fleet, reputation and catering later.
        public static RuleTables? Rules { get; private set; }
        // pitches.json: premium spots, city events and partnerships.
        public static PitchTables? Pitches { get; private set; }
        public static string? Error { get; private set; }

        public static void Load(string modRootPath)
        {
            Tables = null;
            Rules = null;
            Pitches = null;
            Error = null;
            try
            {
                var folder = Path.Combine(modRootPath, "data");
                Tables = DemandTables.Parse(
                    Read(folder, "demand.json"),
                    Read(folder, "economy.json"),
                    Read(folder, "zones.json"));
                Rules = RuleTables.Parse(Read(folder, "rules.json"));
                Pitches = PitchTables.Parse(Read(folder, "pitches.json"), Tables);
                Log.Info($"Demand data loaded from {folder}: {Tables.Zones.Count} zones; queue patience {Rules.Queue.PatienceMinutes} min, cart queue limit {Rules.Service.QueueLimit("cart", Rules.Queue.MaxQueue)}, truck {Rules.Service.QueueLimit("truck", Rules.Queue.MaxQueue)}; licence ${Rules.Fleet.LicenceFeeCents / 100} for {Rules.Fleet.LicenceDays} days, permit ${Rules.Fleet.PermitFeeCents / 100}; {Pitches.Spots.Count} premium spots, {Pitches.Places.Count} event places, {Pitches.Kinds.Count} event kinds.");
            }
            catch (Exception ex)
            {
                Tables = null;
                Rules = null;
                Pitches = null;
                Error = ex.Message;
                Log.Error("Demand data not loaded; carts can't open. " + ex.Message);
            }
        }

        // zones.json is optional; the others are required.
        private static string? Read(string folder, string name)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) return File.ReadAllText(path);
            if (name == "zones.json") return null;
            throw new FileNotFoundException($"Missing {path}.");
        }
    }
}
