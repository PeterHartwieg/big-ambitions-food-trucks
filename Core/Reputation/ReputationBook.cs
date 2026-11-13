using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Reputation
{
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

        public InspectionResult? Opened(VehicleState vehicle, int today, long seed, bool opening = true)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            Day(vehicle, today).Opened = true;
            if (opening) vehicle.LastOpenedDay = today;
            vehicle.Health ??= HealthGrades.NewVehicle(healthRules, today, seed);
            return health.OnOpen(vehicle.Health, today);
        }

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

        public bool IsStale(VehicleState vehicle, string item, int today) =>
            vehicle?.StockBatches != null && vehicle.StockBatches.TryGetValue(item, out var batches) && batches.Count > 0 &&
            today - batches[0].Day >= healthRules.StaleAfterDays;

        public bool Sold(VehicleState vehicle, string item, int today, int workDay)
        {
            var stale = IsStale(vehicle, item, today);
            Consume(vehicle, item, 1);
            if (!stale) return false;
            var record = Day(vehicle, workDay);
            var first = !record.SoldStale;
            record.SoldStale = true;
            return first;
        }

        public static void Consume(VehicleState vehicle, string item, int units)
        {
            if (vehicle?.StockBatches == null || !vehicle.StockBatches.TryGetValue(item, out var batches)) return;
            while (units > 0 && batches.Count > 0)
            {
                var used = Math.Min(units, batches[0].Units);
                batches[0].Units -= used;
                units -= used;
                if (batches[0].Units == 0) batches.RemoveAt(0);
            }
            if (batches.Count == 0) vehicle.StockBatches.Remove(item);
        }

        public static int ServiceDay(long absoluteMinute, int nightEnds)
        {
            var day = (int)(Math.Floor(absoluteMinute / 1440.0) + 1);
            var minute = absoluteMinute - (day - 1) * 1440L;
            return minute < nightEnds ? day - 1 : day;
        }

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
            state?.Regulars == null || string.IsNullOrEmpty(spotKey) || day < 1 ? 1.0 : regulars.DemandFactor(state.Regulars, spotKey, GameDays.Weekday(day));

        public static void Cleaned(VehicleState vehicle, int workDay, int closedThrough)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            foreach (var day in new[] { workDay, vehicle.LastOpenedDay ?? workDay }.Distinct())
                if (day >= 1 && day > closedThrough) Day(vehicle, day).Cleaned = true;
        }

        public static int? TakeOutOldest(VehicleState vehicle, string item, int units)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            int? oldest = vehicle.StockBatches != null && vehicle.StockBatches.TryGetValue(item, out var batches) && batches.Count > 0 && units > 0
                ? batches[0].Day : (int?)null;
            Consume(vehicle, item, units);
            return oldest;
        }

        public static void Restored(VehicleState vehicle, string item, int units, int day, int unitsOnBoard, int today)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (string.IsNullOrEmpty(item) || units <= 0 || day < 1 || day >= today) return;
            TrackStock(vehicle, OnlyItem(vehicle, item, unitsOnBoard), today);
            if (vehicle.StockBatches == null || !vehicle.StockBatches.TryGetValue(item, out var batches)) return;
            var move = 0;
            for (var i = batches.Count - 1; i >= 0 && move < units; i--)
            {
                if (batches[i].Day <= day) break;
                var take = Math.Min(units - move, batches[i].Units);
                batches[i].Units -= take;
                move += take;
                if (batches[i].Units == 0) batches.RemoveAt(i);
            }
            if (move == 0) return;
            var at = batches.FindIndex(b => b.Day >= day);
            if (at >= 0 && batches[at].Day == day) batches[at].Units = checked(batches[at].Units + move);
            else batches.Insert(at < 0 ? batches.Count : at, new StockBatch { Day = day, Units = move });
        }

        private static Dictionary<string, int> OnlyItem(VehicleState vehicle, string item, int unitsOnBoard)
        {
            var counts = (vehicle.StockBatches ?? new Dictionary<string, List<StockBatch>>())
                .ToDictionary(p => p.Key, p => (int)Math.Min(int.MaxValue, p.Value.Sum(b => (long)b.Units)), StringComparer.Ordinal);
            counts[item] = Math.Max(0, unitsOnBoard);
            return counts;
        }

        public static bool CleanedOn(VehicleState vehicle, int serviceDay, int closedThrough)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (serviceDay < 1 || serviceDay <= closedThrough) return false;
            Day(vehicle, serviceDay).Cleaned = true;
            return true;
        }

        public int Points(VehicleState vehicle) => vehicle?.Health == null ? 0 : health.Points(vehicle.Health);

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
                var merged = visits.GroupBy(v => v.SpotKey, StringComparer.Ordinal)
                    .Select(g => new SpotVisit { SpotKey = g.Key, OpenMinutes = g.Max(v => v.OpenMinutes) });
                if (day >= 1 && day > state.Regulars.LastUpdatedDay) regulars.EndOfDay(state.Regulars, day, merged);
                else if (day >= 1) dropped += visits.Count;
            }
            if (today - 1 >= 1 && today - 1 > state.Regulars.LastUpdatedDay)
                regulars.EndOfDay(state.Regulars, today - 1, Array.Empty<SpotVisit>());
            return dropped;
        }

        public static int ClosedThrough(SaveState state) =>
            state?.Regulars == null || state.Regulars.LastUpdatedDay == int.MinValue ? 0 : state.Regulars.LastUpdatedDay;
    }
}
