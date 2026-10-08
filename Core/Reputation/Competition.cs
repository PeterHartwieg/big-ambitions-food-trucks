using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Reputation
{
    public sealed class CompetitionVehicle
    {
        public string VehicleId { get; set; } = string.Empty;
        public double X { get; set; }
        public double Z { get; set; }
        // The adapter passes only open vehicles with their stocked items.
        public IReadOnlyCollection<string> Menu { get; set; } = Array.Empty<string>();
    }

    public sealed class CompetitionEffect
    {
        internal CompetitionEffect(double footfallFactor, Dictionary<string, double> categoryShares)
        {
            FootfallFactor = footfallFactor;
            CategoryShares = new ReadOnlyDictionary<string, double>(categoryShares);
        }

        public double FootfallFactor { get; }
        public IReadOnlyDictionary<string, double> CategoryShares { get; }
    }

    public sealed class Competition
    {
        private readonly DemandTables tables;
        private readonly CompetitionRules rules;

        public Competition(DemandTables tables, CompetitionRules rules)
        {
            this.tables = tables ?? throw new ArgumentNullException(nameof(tables));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public IReadOnlyDictionary<string, CompetitionEffect> Evaluate(IEnumerable<CompetitionVehicle> vehicles)
        {
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            var fleet = new SortedDictionary<string, CompetitionVehicle>(StringComparer.Ordinal);
            var menus = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var vehicle in vehicles)
            {
                if (vehicle == null || string.IsNullOrEmpty(vehicle.VehicleId))
                    throw new ArgumentException("Vehicles need a nonempty ID.", nameof(vehicles));
                if (!Finite(vehicle.X) || !Finite(vehicle.Z)) throw new ArgumentException("Vehicle positions must be finite.", nameof(vehicles));
                if (fleet.ContainsKey(vehicle.VehicleId)) throw new ArgumentException($"Duplicate vehicle ID '{vehicle.VehicleId}'.", nameof(vehicles));
                if (vehicle.Menu == null) throw new ArgumentException("Vehicle menus must not be null.", nameof(vehicles));
                fleet.Add(vehicle.VehicleId, vehicle);
                menus.Add(vehicle.VehicleId, new HashSet<string>(vehicle.Menu.Where(item => item != null && tables.Product(item) != null), StringComparer.Ordinal));
            }
            var effects = new Dictionary<string, CompetitionEffect>(StringComparer.Ordinal);
            foreach (var vehicle in fleet.Values)
            {
                var neighbours = fleet.Values.Where(other => other.VehicleId != vehicle.VehicleId && menus[other.VehicleId].Count > 0 && Nearby(vehicle, other)).ToList();
                var shares = new Dictionary<string, double>(StringComparer.Ordinal);
                var categories = menus[vehicle.VehicleId].OrderBy(item => item, StringComparer.Ordinal)
                    .GroupBy(item => tables.Product(item)!.Category).OrderBy(group => group.Key, StringComparer.Ordinal);
                foreach (var category in categories)
                    shares.Add(category.Key, category.Average(item => 1.0 / (1 + neighbours.Count(n => menus[n.VehicleId].Contains(item)))));
                var footfall = 1 + Math.Min(rules.MaxFootfallBonus, rules.FootfallPerNeighbour * neighbours.Count);
                effects.Add(vehicle.VehicleId, new CompetitionEffect(footfall, shares));
            }
            return effects;
        }

        private bool Nearby(CompetitionVehicle a, CompetitionVehicle b)
        {
            var x = Math.Abs(a.X - b.X);
            var z = Math.Abs(a.Z - b.Z);
            // Reject distant coordinates before squaring, so extreme finite positions stay safe.
            return x <= rules.NearbyMetres && z <= rules.NearbyMetres && x * x + z * z <= rules.NearbyMetres * rules.NearbyMetres;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
