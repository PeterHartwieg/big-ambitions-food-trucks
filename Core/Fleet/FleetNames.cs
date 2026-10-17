using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    // Each food vehicle keeps its number once it has one, so buying another never renames it (the first blind playtest,
    // 17 October 2026: a new cart took "Food cart 2" and the old one became "Food cart 3"). A vehicle without a number
    // takes the lowest one of its kind that no vehicle of that kind has had, in ID order. Numbers are never given back:
    // a vehicle missing from the game's lists for a moment (a load, the private driver) must come back under its own
    // name, so a sold cart's number stays unused. The number shows only while the player has more than one of a kind.
    public static class FleetNames
    {
        // Numbers the vehicles the player has now (ID and kind) that have none; true when it added one.
        public static bool Assign(List<VehicleNumber> numbers, IReadOnlyCollection<(string Id, string Kind)> owned)
        {
            if (numbers == null) throw new ArgumentNullException(nameof(numbers));
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            var changed = false;
            foreach (var kind in owned.GroupBy(v => v.Kind, StringComparer.Ordinal))
            {
                var used = new HashSet<int>(numbers.Where(n => n.Kind == kind.Key).Select(n => n.Number));
                var next = 1;
                foreach (var id in kind.Select(v => v.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
                {
                    if (numbers.Any(n => n.Id == id && n.Kind == kind.Key)) continue;
                    while (used.Contains(next)) next++;
                    numbers.Add(new VehicleNumber { Id = id, Kind = kind.Key, Number = next });
                    used.Add(next);
                    changed = true;
                }
            }
            return changed;
        }

        // In the raw save, before it is read: `vehicleNumbers` that isn't a list goes, and so does each entry that isn't an
        // object with a string id and kind and a whole number that fits an int.
        public static void DropUnreadable(JObject document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var token = document["vehicleNumbers"];
            if (token == null) return;
            if (token.Type != JTokenType.Array)
            {
                document.Remove("vehicleNumbers");
                return;
            }
            foreach (var entry in ((JArray)token).ToList())
            {
                var ok = entry is JObject o &&
                         (o["id"] == null || o["id"]!.Type == JTokenType.String || o["id"]!.Type == JTokenType.Null) &&
                         (o["kind"] == null || o["kind"]!.Type == JTokenType.String || o["kind"]!.Type == JTokenType.Null) &&
                         (o["number"] == null || (o["number"]!.Type == JTokenType.Integer &&
                                                  o["number"]!.Value<long>() >= int.MinValue && o["number"]!.Value<long>() <= int.MaxValue));
                if (!ok) entry.Remove();
            }
        }

        // The saved list without what can't name a vehicle: null entries, empty IDs or kinds, numbers below 1, and a second
        // entry for a vehicle or a second holder of a number (the first is kept).
        public static List<VehicleNumber> Repaired(List<VehicleNumber>? numbers)
        {
            var kept = new List<VehicleNumber>();
            foreach (var n in numbers ?? new List<VehicleNumber>())
            {
                if (n == null || string.IsNullOrEmpty(n.Id) || string.IsNullOrEmpty(n.Kind) || n.Number < 1) continue;
                if (kept.Any(k => k.Kind == n.Kind && (k.Id == n.Id || k.Number == n.Number))) continue;
                kept.Add(n);
            }
            return kept;
        }

        // The vehicle's number, or 0 when it has none.
        public static int NumberOf(IEnumerable<VehicleNumber> numbers, string id, string kind) =>
            numbers.FirstOrDefault(n => n.Id == id && n.Kind == kind)?.Number ?? 0;

        // The vehicle's name: the kind's label alone while it is the only one of its kind, else with its number.
        public static string Name(string label, int number, int ofKind) => ofKind > 1 && number > 0 ? $"{label} {number}" : label;
    }
}
