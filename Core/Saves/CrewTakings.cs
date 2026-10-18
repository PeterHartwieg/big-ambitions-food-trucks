using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Saves
{
    // What a vehicle took on one calendar day and the game hasn't paid in yet, whoever served. The sales are in the
    // ledger (StreetSales.Record) the moment they are made, so Econoview, the day summary and the tax statement see
    // them on their day; only the money waits.
    public class CrewTaking
    {
        public string VehicleId { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }
        public int Sales { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // All window sales are paid in as the game pays its own shops: the day's orders
    // pile up and the money comes in one transaction per business after midnight, into the day sold
    // (BusinessHelper.ProcessDailyOrders, `ba:transaction_revenue`) [code, 1.0]. Here, one per vehicle and day.
    // The historical CrewTakings name is kept in the save; it now holds player and crew sales together.
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

        // One unit sold for `cents` on `day`, to be paid in later.
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

        // Takings of days before `today`, oldest first: what midnight pays in. With soldThrough (the absolute game minute
        // each vehicle has sold up to), a day is due only once that vehicle's sales have passed its end, so a catch-up
        // still selling into it never splits its takings into two payments.
        // A removed vehicle's takings, today's too, are paid back immediately rather than waiting for midnight.
        public static IReadOnlyList<CrewTaking> Due(SaveState state, int today, Func<string, double>? soldThrough = null,
            Func<string, bool>? vehicleExists = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.CrewTakings
                .Where(t => vehicleExists?.Invoke(t.VehicleId) == false ||
                    t.Day < today && (soldThrough == null || soldThrough(t.VehicleId) >= t.Day * 1440.0))
                .OrderBy(t => t.Day).ThenBy(t => t.VehicleId, StringComparer.Ordinal).ToList();
        }

        // Every taking, today's too (the uninstall step pays them all in at once).
        public static IReadOnlyList<CrewTaking> All(SaveState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.CrewTakings.OrderBy(t => t.Day).ThenBy(t => t.VehicleId, StringComparer.Ordinal).ToList();
        }

        // The game took the payment: the taking is settled.
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
