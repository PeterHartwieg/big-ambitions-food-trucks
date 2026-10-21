using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Marketing
{
    // One marketing level a vehicle can run: its passers factor and its fee per game day by vehicle kind.
    public sealed class MarketingLevel
    {
        public MarketingLevel(string id, double footfall, IReadOnlyDictionary<string, long> costPerDayCents)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Level ID must not be empty.", nameof(id));
            if (double.IsNaN(footfall) || footfall < MarketingRules.MinFootfall || footfall > MarketingRules.MaxFootfall)
                throw new ArgumentOutOfRangeException(nameof(footfall), "The passers factor must be 1 to 3.");
            if (costPerDayCents == null || costPerDayCents.Count == 0)
                throw new ArgumentException("Costs must contain at least one vehicle kind.", nameof(costPerDayCents));
            var costs = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in costPerDayCents)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)
                    throw new ArgumentException("Costs must name vehicle kinds with amounts not negative.", nameof(costPerDayCents));
                costs.Add(pair.Key, pair.Value);
            }
            Id = id;
            Footfall = footfall;
            CostPerDayCents = new ReadOnlyDictionary<string, long>(costs);
        }

        public string Id { get; }
        // Multiplies the vehicle's passers, as the game's promotion multiplies a shop's customers.
        public double Footfall { get; }
        public IReadOnlyDictionary<string, long> CostPerDayCents { get; }
        public bool IsNone => Id == MarketingRules.None;
    }

    // The marketing levels of data/rules.json, in order. The first is always "none": no factor, no fee.
    public sealed class MarketingRules
    {
        public const string None = "none";
        public const double MinFootfall = 1.0;
        public const double MaxFootfall = 3.0;

        public MarketingRules(IEnumerable<MarketingLevel>? levels = null)
        {
            var list = (levels ?? DefaultLevels()).ToList();
            if (list.Count == 0 || list.Any(l => l == null)) throw new ArgumentException("Levels must not be empty.", nameof(levels));
            var first = list[0];
            if (!first.IsNone || first.Footfall != 1.0 || first.CostPerDayCents.Values.Any(c => c != 0))
                throw new ArgumentException("The first level must be none, with factor 1 and no cost.", nameof(levels));
            if (list.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Level IDs must be unique.", nameof(levels));
            Levels = list.AsReadOnly();
        }

        public IReadOnlyList<MarketingLevel> Levels { get; }

        // Null for an unknown ID (a level a newer mod version added), or for null.
        public MarketingLevel? Level(string? id) => id == null ? null : Levels.FirstOrDefault(l => l.Id == id);

        private static IEnumerable<MarketingLevel> DefaultLevels() => new[]
        {
            new MarketingLevel(None, 1.0, Costs(0, 0)),
            new MarketingLevel("flyers", 1.10, Costs(2000, 6000)),
            new MarketingLevel("local", 1.20, Costs(4500, 14000)),
            new MarketingLevel("citywide", 1.30, Costs(8000, 24000)),
        };

        private static Dictionary<string, long> Costs(long cart, long truck) =>
            new Dictionary<string, long>(StringComparer.Ordinal) { ["cart"] = cart, ["truck"] = truck };

        internal static MarketingRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "levels");
            var list = r.List(obj, "levels");
            if (list.Count == 0) throw r.Fail(list.Path, "must not be empty");
            var levels = new List<MarketingLevel>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in list)
            {
                var level = token as JObject ?? throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(level, "id", "footfall", "costPerDayCents");
                var id = r.Text(level, "id");
                if (string.IsNullOrWhiteSpace(id)) throw r.Fail(DataReader.PathOf(level, "id"), "must not be blank");
                if (!ids.Add(id)) throw r.Fail(DataReader.PathOf(level, "id"), "must be unique");
                var footfall = r.Number(level, "footfall", MinFootfall, MaxFootfall);
                var costs = r.Object(level, "costPerDayCents");
                var amounts = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var entry in r.Entries(costs))
                {
                    if (string.IsNullOrWhiteSpace(entry.Name)) throw r.Fail(entry.Path, "must not be blank");
                    amounts.Add(entry.Name, Cents(r, costs, entry.Name));
                }
                if (amounts.Count == 0) throw r.Fail(costs.Path, "must not be empty");
                if (levels.Count == 0)
                {
                    if (id != None) throw r.Fail(DataReader.PathOf(level, "id"), $"must be \"{None}\" for the first level");
                    if (footfall != 1.0) throw r.Fail(DataReader.PathOf(level, "footfall"), "must be 1 for the first level");
                    var paid = amounts.FirstOrDefault(a => a.Value != 0);
                    if (paid.Key != null) throw r.Fail(DataReader.PathOf(costs, paid.Key), "must be 0 for the first level");
                }
                levels.Add(new MarketingLevel(id, footfall, amounts));
            }
            return new MarketingRules(levels);
        }

        private static long Cents(DataReader r, JObject obj, string name)
        {
            var token = r.Required(obj, name);
            if (token.Type != JTokenType.Integer || !(((JValue)token).Value is long value) || value < 0)
                throw r.Fail(DataReader.PathOf(obj, name), "must be whole cents between 0 and Int64.MaxValue");
            return value;
        }
    }
}
