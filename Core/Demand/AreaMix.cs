using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    // Shares of area types at a spot, summing to 1. Office 0.7 and nightlife 0.3 blends their curves.
    public sealed class AreaMix
    {
        private readonly Dictionary<string, double> shares;

        private AreaMix(Dictionary<string, double> shares) => this.shares = shares;

        public IReadOnlyDictionary<string, double> Shares => shares;

        public double ShareOf(string area) => shares.TryGetValue(area, out var share) ? share : 0;

        public static AreaMix Single(string area)
        {
            if (string.IsNullOrEmpty(area)) throw new ArgumentException("Area must not be empty.", nameof(area));
            return new AreaMix(new Dictionary<string, double>(StringComparer.Ordinal) { [area] = 1.0 });
        }

        // Scales positive weights to shares; zero weights are dropped.
        public static AreaMix FromWeights(IEnumerable<KeyValuePair<string, double>> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            var positive = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in weights)
            {
                if (string.IsNullOrEmpty(pair.Key)) throw new ArgumentException("Area names must not be empty.", nameof(weights));
                if (double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value < 0)
                    throw new ArgumentException($"Weight for '{pair.Key}' must be finite and not negative.", nameof(weights));
                if (pair.Value == 0) continue;
                positive.TryGetValue(pair.Key, out var sum);
                positive[pair.Key] = sum + pair.Value;
            }
            var total = positive.OrderBy(p => p.Key, StringComparer.Ordinal).Sum(p => p.Value);
            if (total <= 0) throw new ArgumentException("At least one area needs a weight above 0.", nameof(weights));
            return new AreaMix(positive.ToDictionary(p => p.Key, p => p.Value / total, StringComparer.Ordinal));
        }

        public override string ToString() =>
            string.Join(", ", shares.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key} {p.Value:0.##}"));
    }
}
