using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Reputation
{
    // The day-to-day record behind health grades and regulars. The game side notes what happens while a
    // vehicle is open (minutes at each spot, cleaning, stale stock); when a new day starts, every finished
    // day goes into the vehicle's health marks and the fleet's regulars, in day order, and is dropped.
    public sealed class ReputationBook
    {
        private readonly HealthGrades health;
        private readonly Regulars regulars;
        private readonly HealthRules healthRules;

        public ReputationBook(RuleTables rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            healthRules = rules.Health;
            health = new HealthGrades(rules.Health);
            regulars = new Regulars(rules.Regulars);
        }

        public static DayRecord Day(VehicleState vehicle, int day)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            vehicle.Days ??= new List<DayRecord>();
            var record = vehicle.Days.Find(d => d.Day == day);
            if (record != null) return record;
            record = new DayRecord { Day = day };
            vehicle.Days.Add(record);
            return record;
        }

        public static void AddMinutes(VehicleState vehicle, int day, string spotKey, int minutes)
        {
            if (string.IsNullOrEmpty(spotKey) || minutes <= 0) return;
            var record = Day(vehicle, day);
            record.Opened = true;
            var visit = record.Visits.Find(v => v.SpotKey == spotKey);
            if (visit == null) record.Visits.Add(visit = new SpotMinutes { SpotKey = spotKey });
            visit.Minutes = (int)Math.Min(int.MaxValue, (long)visit.Minutes + minutes);
        }

        // The vehicle opens: a new vehicle gets its health record, and the inspector may come by. Returns the
        // inspection's result, if there was one.
        public InspectionResult? Opened(VehicleState vehicle, int today, long seed)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            Day(vehicle, today).Opened = true;
            vehicle.Health ??= HealthGrades.NewVehicle(healthRules, today, seed);
            return health.OnOpen(vehicle.Health, today);
        }

        // Notes when each item came on board: kept while any of it is left, dropped when it runs out.
        public static bool TrackStock(VehicleState vehicle, IEnumerable<string> itemsOnBoard, int today)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            vehicle.StockSince ??= new Dictionary<string, int>(StringComparer.Ordinal);
            var onBoard = new HashSet<string>(itemsOnBoard ?? Array.Empty<string>(), StringComparer.Ordinal);
            var changed = false;
            foreach (var item in vehicle.StockSince.Keys.Where(k => !onBoard.Contains(k)).ToList())
            {
                vehicle.StockSince.Remove(item);
                changed = true;
            }
            foreach (var item in onBoard)
            {
                if (string.IsNullOrEmpty(item) || vehicle.StockSince.ContainsKey(item)) continue;
                vehicle.StockSince[item] = today;
                changed = true;
            }
            return changed;
        }

        public bool IsStale(VehicleState vehicle, string item, int today) =>
            vehicle?.StockSince != null && vehicle.StockSince.TryGetValue(item, out var since) && today - since >= healthRules.StaleAfterDays;

        // A sale of stale food marks the day.
        public bool Sold(VehicleState vehicle, string item, int today)
        {
            if (!IsStale(vehicle, item, today)) return false;
            var record = Day(vehicle, today);
            var first = !record.SoldStale;
            record.SoldStale = true;
            return first;
        }

        public double HealthFactor(VehicleState? vehicle) => vehicle?.Health == null ? 1.0 : health.DemandFactor(vehicle.Health);

        public double RegularsFactor(SaveState state, string spotKey, int day) =>
            state?.Regulars == null || string.IsNullOrEmpty(spotKey) ? 1.0 : regulars.DemandFactor(state.Regulars, spotKey, GameDays.Weekday(day));

        public int Points(VehicleState vehicle) => vehicle?.Health == null ? 0 : health.Points(vehicle.Health);

        // Every day before `today` that has a record goes into health and regulars, oldest first. Days with no
        // record need nothing: regulars count the misses in between on their next update.
        public void CloseDaysBefore(SaveState state, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var days = state.Vehicles.SelectMany(v => v.Days ?? new List<DayRecord>()).Select(d => d.Day).Where(d => d < today).Distinct().OrderBy(d => d).ToList();
            foreach (var day in days)
            {
                var visits = new List<SpotVisit>();
                foreach (var vehicle in state.Vehicles)
                {
                    var record = vehicle.Days?.Find(d => d.Day == day);
                    if (record == null) continue;
                    if (record.Opened && vehicle.Health != null) health.RecordDay(vehicle.Health, day, record.Cleaned, record.SoldStale);
                    foreach (var visit in record.Visits) visits.Add(new SpotVisit { SpotKey = visit.SpotKey, OpenMinutes = visit.Minutes });
                    vehicle.Days!.Remove(record);
                }
                // Two vehicles at one spot on one day count as one visit there.
                var merged = visits.GroupBy(v => v.SpotKey, StringComparer.Ordinal)
                    .Select(g => new SpotVisit { SpotKey = g.Key, OpenMinutes = g.Max(v => v.OpenMinutes) });
                if (day >= 1 && day > state.Regulars.LastUpdatedDay) regulars.EndOfDay(state.Regulars, day, merged);
            }
        }
    }
}
