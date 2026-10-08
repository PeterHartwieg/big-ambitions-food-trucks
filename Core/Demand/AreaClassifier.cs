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

                IReadOnlyDictionary<string, double>? rule = null;
                if (place.BusinessType != null) settings.BusinessTypes.TryGetValue(place.BusinessType, out rule);
                if (rule == null && place.BuildingType != null)
                    settings.BuildingTypes.TryGetValue(place.BuildingType, out rule);
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

            if (votes.Count == 0) return AreaMix.Single(settings.FallbackArea);
            // Fixed summation order prevents rounding from changing ties when places are reordered.
            var totals = votes.Select(pair =>
            {
                pair.Value.Sort();
                return new KeyValuePair<string, double>(pair.Key, pair.Value.Sum());
            });
            return AreaMix.FromWeights(totals.OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(settings.MaxAreas));
        }

        public AreaMix ApplyZones(AreaMix automatic, double x, double z)
        {
            if (automatic == null) throw new ArgumentNullException(nameof(automatic));
            var mix = automatic;
            foreach (var zone in tables.Zones)
            {
                if (!Contains(zone.Polygon, x, z)) continue;
                if (zone.Mode == ZoneMode.Override) return zone.Areas;
                mix = AreaMix.FromWeights(mix.Shares.Select(pair =>
                        new KeyValuePair<string, double>(pair.Key, (1.0 - zone.Share) * pair.Value))
                    .Concat(zone.Areas.Shares.Select(pair =>
                        new KeyValuePair<string, double>(pair.Key, zone.Share * pair.Value))));
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
