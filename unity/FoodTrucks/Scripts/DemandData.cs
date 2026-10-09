#nullable enable
using System;
using System.IO;
using System.Linq;
using FoodTrucks.Core.Commissary;
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
        // commissaries.json: the commissary garages.
        public static CommissaryTables? Commissaries { get; private set; }
        public static string? Error { get; private set; }

        public static void Load(string modRootPath)
        {
            Tables = null;
            Rules = null;
            Pitches = null;
            Commissaries = null;
            Error = null;
            try
            {
                var folder = Path.Combine(modRootPath, "data");
                Tables = DemandTables.Parse(
                    Read(folder, "demand.json"),
                    Read(folder, "economy.json"),
                    Read(folder, "zones.json"));
                Rules = RuleTables.Parse(Read(folder, "rules.json"));
                Log.Info($"Demand data loaded from {folder}: {Tables.Zones.Count} zones; queue patience {Rules.Queue.PatienceMinutes} min, cart queue limit {Rules.Service.QueueLimit("cart", Rules.Queue.MaxQueue)}, truck {Rules.Service.QueueLimit("truck", Rules.Queue.MaxQueue)}; licence ${Rules.Fleet.LicenceFeeCents / 100} for {Rules.Fleet.LicenceDays} days, permit ${Rules.Fleet.PermitFeeCents / 100}.");
            }
            catch (Exception ex)
            {
                Tables = null;
                Rules = null;
                Pitches = null;
                Error = ex.Message;
                Log.Error("Demand data not loaded; carts can't open. " + ex.Message);
                return;
            }
            // Apart: a mistake in pitches.json takes away premium spots, events and partnerships, not selling.
            try
            {
                Pitches = PitchTables.Parse(Read(Path.Combine(modRootPath, "data"), "pitches.json"), Tables);
                Log.Info($"Pitches loaded: {Pitches.Spots.Count} premium spots, {Pitches.Places.Count} event places, {Pitches.Kinds.Count} event kinds.");
            }
            catch (Exception ex)
            {
                Pitches = null;
                Log.Error("Pitches not loaded; no premium spots, events or partnerships. " + ex.Message);
            }
            // Apart too: without commissaries there is no renting, ordering or overnight check.
            try
            {
                Commissaries = CommissaryTables.Parse(Read(Path.Combine(modRootPath, "data"), "commissaries.json"));
                var rules = Rules!.Commissary;
                Log.Info($"Commissaries loaded: {Commissaries.Sites.Count} garages; rent {string.Join(", ", rules.RentPerMonthCents.Select(p => $"{p.Key} ${p.Value / 100}"))} a month, overnight fine ${rules.OvernightFineCents / 100}, bay radius {rules.BayRadiusMetres} m.");
            }
            catch (Exception ex)
            {
                Commissaries = null;
                Log.Error("Commissaries not loaded; no renting, ordering or overnight check. " + ex.Message);
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
