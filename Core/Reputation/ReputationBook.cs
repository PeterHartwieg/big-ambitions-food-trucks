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

        // Food on board by the day it came on. More units than recorded: a new batch today. Fewer: the oldest
        // went first (sold, or taken out). An item gone from the vehicle is forgotten.
        public static bool TrackStock(VehicleState vehicle, IReadOnlyDictionary<string, int> unitsOnBoard, int today)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            vehicle.StockBatches ??= new Dictionary<string, List<StockBatch>>(StringComparer.Ordinal);
            var onBoard = unitsOnBoard ?? new Dictionary<string, int>();
            var changed = false;
            foreach (var item in vehicle.StockBatches.Keys.Where(k => !onBoard.TryGetValue(k, out var u) || u <= 0).ToList())
            {
                vehicle.StockBatches.Remove(item);
                changed = true;
            }
            foreach (var pair in onBoard)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value <= 0) continue;
                if (!vehicle.StockBatches.TryGetValue(pair.Key, out var batches)) vehicle.StockBatches[pair.Key] = batches = new List<StockBatch>();
                var recorded = batches.Sum(b => (long)b.Units);
                if (pair.Value > recorded)
                {
                    var last = batches.LastOrDefault();
                    var added = (int)Math.Min(int.MaxValue, pair.Value - recorded);
                    if (last != null && last.Day == today) last.Units = (int)Math.Min(int.MaxValue, (long)last.Units + added);
                    else batches.Add(new StockBatch { Day = today, Units = added });
                    changed = true;
                }
                else if (pair.Value < recorded)
                {
                    var take = recorded - pair.Value;
                    while (take > 0 && batches.Count > 0)
                    {
                        var used = Math.Min(take, batches[0].Units);
                        batches[0].Units -= (int)used;
                        take -= used;
                        if (batches[0].Units == 0) batches.RemoveAt(0);
                    }
                    changed = true;
                }
            }
            return changed;
        }

        // The unit sold next is the oldest on board.
        public bool IsStale(VehicleState vehicle, string item, int today) =>
            vehicle?.StockBatches != null && vehicle.StockBatches.TryGetValue(item, out var batches) && batches.Count > 0 &&
            today - batches[0].Day >= healthRules.StaleAfterDays;

        // A sale of stale food marks the day. True the first time that day.
        public bool Sold(VehicleState vehicle, string item, int today)
        {
            if (!IsStale(vehicle, item, today)) return false;
            var record = Day(vehicle, today);
            var first = !record.SoldStale;
            record.SoldStale = true;
            return first;
        }

        // The night belongs to the evening before: minutes before nightEnds count for the previous day, so a
        // stop running past midnight is one working day, as the demand model counts a night out.
        public static int ServiceDay(long absoluteMinute, int nightEnds)
        {
            var day = (int)(Math.Floor(absoluteMinute / 1440.0) + 1);
            var minute = absoluteMinute - (day - 1) * 1440L;
            return minute < nightEnds ? day - 1 : day;
        }

        // Open minutes from `from` to `to` (absolute game minutes) at a pitch, split by service day. Days
        // already counted (up to closedThrough) take nothing more.
        public static void AddOpenMinutes(VehicleState vehicle, long from, long to, string spotKey, int nightEnds, int closedThrough)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var t = from;
            while (t < to)
            {
                var day = ServiceDay(t, nightEnds);
                var dayEnds = (long)day * 1440 + nightEnds;
                var until = Math.Min(to, dayEnds);
                if (day > closedThrough && day >= 1) AddMinutes(vehicle, day, spotKey, (int)Math.Min(int.MaxValue, until - t));
                t = until;
            }
        }

        public double HealthFactor(VehicleState? vehicle) => vehicle?.Health == null ? 1.0 : health.DemandFactor(vehicle.Health);

        public double RegularsFactor(SaveState state, string spotKey, int day) =>
            state?.Regulars == null || string.IsNullOrEmpty(spotKey) ? 1.0 : regulars.DemandFactor(state.Regulars, spotKey, GameDays.Weekday(day));

        public int Points(VehicleState vehicle) => vehicle?.Health == null ? 0 : health.Points(vehicle.Health);

        // Every day before `today` that has a record goes into health and regulars, oldest first; then the
        // regulars move on through yesterday, so days nobody worked count as missed.
        // Returns the visits that came too late for an already counted day (they are dropped).
        public int CloseDaysBefore(SaveState state, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var dropped = 0;
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
                else if (day >= 1) dropped += visits.Count;
            }
            // Days nobody worked still count as missed for the regulars, through yesterday.
            if (today - 1 >= 1 && today - 1 > state.Regulars.LastUpdatedDay)
                regulars.EndOfDay(state.Regulars, today - 1, Array.Empty<SpotVisit>());
            return dropped;
        }

        // The last day already counted: nothing may be recorded for it or earlier.
        public static int ClosedThrough(SaveState state) =>
            state?.Regulars == null || state.Regulars.LastUpdatedDay == int.MinValue ? 0 : state.Regulars.LastUpdatedDay;
    }
}
