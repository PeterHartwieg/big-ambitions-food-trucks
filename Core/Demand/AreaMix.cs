using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    public sealed class AreaMix
    {
        private readonly IReadOnlyDictionary<string, double> presences;

        private AreaMix(Dictionary<string, double> presences) =>
            this.presences = new ReadOnlyDictionary<string, double>(presences);

        public IReadOnlyDictionary<string, double> Presences => presences;
        public double PresenceOf(string area) => presences.TryGetValue(area, out var presence) ? presence : 0;

        public IReadOnlyDictionary<string, double> Shares => Presences;
        public double ShareOf(string area) => PresenceOf(area);

        public static AreaMix Single(string area)
        {
            if (string.IsNullOrEmpty(area)) throw new ArgumentException("Area must not be empty.", nameof(area));
            return new AreaMix(new Dictionary<string, double>(StringComparer.Ordinal) { [area] = 1.0 });
        }

        public static AreaMix FromWeights(IEnumerable<KeyValuePair<string, double>> weights) => FromPresences(weights);

        public static AreaMix FromPresences(IEnumerable<KeyValuePair<string, double>> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            var positive = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in weights)
            {
                if (string.IsNullOrEmpty(pair.Key)) throw new ArgumentException("Area names must not be empty.", nameof(weights));
                if (double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value < 0 || pair.Value > 1)
                    throw new ArgumentException($"Presence for '{pair.Key}' must be finite and between 0 and 1.", nameof(weights));
                if (pair.Value == 0) continue;
                positive.TryGetValue(pair.Key, out var sum);
                positive[pair.Key] = Math.Max(sum, pair.Value);
            }
            return new AreaMix(positive);
        }

        public override string ToString() =>
            string.Join(", ", presences.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key} presence {p.Value:0.##}"));
    }
}
