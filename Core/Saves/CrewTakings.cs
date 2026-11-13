using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Saves
{
    public class CrewTaking
    {
        public string VehicleId { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }
        public int Sales { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public static class CrewTakings
    {
        public static bool CanAccrue(SaveState state, string vehicleId, int day, long cents)
        {
            Check(state, vehicleId, day, cents);
            var entry = Find(state, vehicleId, day);
            if (entry == null) return true;
            try
            {
                _ = checked(entry.Cents + cents);
                _ = checked(entry.Sales + 1);
                return true;
            }
            catch (OverflowException) { return false; }
        }

        public static void Accrue(SaveState state, string vehicleId, int day, long cents)
        {
            Check(state, vehicleId, day, cents);
            var entry = Find(state, vehicleId, day);
            if (entry == null)
            {
                state.CrewTakings.Add(new CrewTaking { VehicleId = vehicleId, Day = day, Cents = cents, Sales = 1 });
                return;
            }
            var total = checked(entry.Cents + cents);
            var sales = checked(entry.Sales + 1);
            entry.Cents = total;
            entry.Sales = sales;
        }

        public static IReadOnlyList<CrewTaking> Due(SaveState state, int today, Func<string, double>? soldThrough = null,
            Func<string, bool>? vehicleExists = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.CrewTakings
                .Where(t => vehicleExists?.Invoke(t.VehicleId) == false ||
                    t.Day < today && (soldThrough == null || soldThrough(t.VehicleId) >= t.Day * 1440.0))
                .OrderBy(t => t.Day).ThenBy(t => t.VehicleId, StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<CrewTaking> All(SaveState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.CrewTakings.OrderBy(t => t.Day).ThenBy(t => t.VehicleId, StringComparer.Ordinal).ToList();
        }

        public static bool Settled(SaveState state, CrewTaking taking)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.CrewTakings.Remove(taking);
        }

        internal static void Validate(SaveState state)
        {
            state.CrewTakings ??= new List<CrewTaking>();
            if (state.CrewTakings.Any(t => t == null || string.IsNullOrEmpty(t.VehicleId) || t.Day < 1 || t.Cents < 0 || t.Sales < 0) ||
                state.CrewTakings.Select(t => (t.VehicleId, t.Day)).Distinct().Count() != state.CrewTakings.Count)
                throw new JsonSerializationException("CrewTakings must be one per vehicle and day, named, with amounts not negative.");
        }

        private static CrewTaking? Find(SaveState state, string vehicleId, int day) =>
            state.CrewTakings.FirstOrDefault(t => t.Day == day && string.Equals(t.VehicleId, vehicleId, StringComparison.Ordinal));

        private static void Check(SaveState state, string vehicleId, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(vehicleId)) throw new ArgumentException("Vehicle id must not be empty.", nameof(vehicleId));
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            if (cents < 0) throw new ArgumentOutOfRangeException(nameof(cents));
        }
    }
}
