using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    public static class FleetNames
    {
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

        public static List<VehicleNumber> TakeOut(JObject document, JsonSerializer serializer)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));
            var taken = new List<VehicleNumber>();
            foreach (var property in document.Properties().Where(p => string.Equals(p.Name, "vehicleNumbers", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                property.Remove();
                if (!(property.Value is JArray entries)) continue;
                foreach (var entry in entries)
                {
                    try
                    {
                        if (entry is JObject && entry.ToObject<VehicleNumber>(serializer) is VehicleNumber number) taken.Add(number);
                    }
                    catch (Exception ex) when (ex is JsonException || ex is OverflowException || ex is FormatException || ex is InvalidCastException || ex is ArgumentException)
                    {
                    }
                }
            }
            return taken;
        }

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

        public static int NumberOf(IEnumerable<VehicleNumber> numbers, string id, string kind) =>
            numbers.FirstOrDefault(n => n.Id == id && n.Kind == kind)?.Number ?? 0;

        public static string Name(string label, int number, int ofKind) => ofKind > 1 && number > 0 ? $"{label} {number}" : label;
    }
}
