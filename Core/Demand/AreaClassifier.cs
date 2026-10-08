using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    // One building near the spot, as the game adapter reads it. The adapter decides how to measure the distance.
    public sealed class NearbyPlace
    {
        public NearbyPlace(string buildingType, string? businessType, double distanceMetres)
        {
            BuildingType = buildingType;
            BusinessType = businessType;
            DistanceMetres = distanceMetres;
        }

        public string BuildingType { get; }
        public string? BusinessType { get; }
        public double DistanceMetres { get; }
    }

    public sealed class AreaClassifier
    {
        private readonly DemandTables tables;

        public AreaClassifier(DemandTables tables) =>
            this.tables = tables ?? throw new ArgumentNullException(nameof(tables));

        public AreaMix Classify(IEnumerable<NearbyPlace> places, double x, double z) =>
            ApplyZones(FromPlaces(places), x, z);

        public AreaMix FromPlaces(IEnumerable<NearbyPlace> places)
        {
            if (places == null) throw new ArgumentNullException(nameof(places));
            var settings = tables.Classifier;
            var votes = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            foreach (var place in places)
            {
                if (place == null || !IsFinite(place.DistanceMetres) || place.DistanceMetres < 0 ||
                    place.DistanceMetres > settings.RadiusMetres) continue;

                var rule = RuleFor(place.BuildingType, place.BusinessType);
                if (rule == null) continue;

                var factor = 1.0 - (1.0 - settings.EdgeWeight) * (place.DistanceMetres / settings.RadiusMetres);
                foreach (var pair in rule)
                {
                    var weight = pair.Value * factor;
                    if (weight <= 0) continue;
                    if (!votes.TryGetValue(pair.Key, out var contributions))
                    {
                        contributions = new List<double>();
                        votes.Add(pair.Key, contributions);
                    }
                    contributions.Add(weight);
                }
            }

            // No classified buildings means street baseline only, without a fallback crowd.
            if (votes.Count == 0) return AreaMix.FromPresences(Array.Empty<KeyValuePair<string, double>>());
            // Fixed summation order prevents rounding from changing ties when places are reordered.
            var totals = votes.Select(pair =>
            {
                pair.Value.Sort();
                return new KeyValuePair<string, double>(pair.Key, 1.0 - Math.Exp(-pair.Value.Sum() / settings.Saturation));
            });
            // MaxAreas is accepted for old data, but never trims independent crowds.
            return AreaMix.FromPresences(totals.OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal));
        }

        // The areas a building votes for: its business type's rule, else its building type's; null for neither.
        public IReadOnlyDictionary<string, double>? RuleFor(string? buildingType, string? businessType)
        {
            var settings = tables.Classifier;
            IReadOnlyDictionary<string, double>? rule = null;
            if (businessType != null) settings.BusinessTypes.TryGetValue(businessType, out rule);
            if (rule == null && buildingType != null) settings.BuildingTypes.TryGetValue(buildingType, out rule);
            return rule;
        }

        // Whether a building brings the area's crowd to the spots around it.
        public bool Draws(string area, string? buildingType, string? businessType) =>
            RuleFor(buildingType, businessType) is { } rule && rule.TryGetValue(area, out var weight) && weight > 0;

        public AreaMix ApplyZones(AreaMix automatic, double x, double z)
        {
            if (automatic == null) throw new ArgumentNullException(nameof(automatic));
            var mix = automatic;
            foreach (var zone in tables.Zones)
            {
                if (!Contains(zone.Polygon, x, z)) continue;
                if (zone.Mode == ZoneMode.Override) return zone.Areas;
                mix = AreaMix.FromPresences(mix.Presences.Concat(zone.Areas.Presences));
            }
            return mix;
        }

        public static bool Contains(IReadOnlyList<ZonePoint> polygon, double x, double z)
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (polygon.Count < 3 || !IsFinite(x) || !IsFinite(z)) return false;
            var inside = false;
            var previous = polygon[polygon.Count - 1];
            foreach (var point in polygon)
            {
                var cross = (x - previous.X) * (point.Z - previous.Z) -
                    (z - previous.Z) * (point.X - previous.X);
                if (cross == 0 && x >= Math.Min(previous.X, point.X) && x <= Math.Max(previous.X, point.X) &&
                    z >= Math.Min(previous.Z, point.Z) && z <= Math.Max(previous.Z, point.Z)) return true;

                if ((point.Z > z) != (previous.Z > z) &&
                    x < previous.X + (z - previous.Z) * (point.X - previous.X) / (point.Z - previous.Z))
                    inside = !inside;
                previous = point;
            }
            return inside;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
