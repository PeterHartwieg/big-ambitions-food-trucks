using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    // Units of each item one vehicle sold (at the window or served at a gig) on one day, for BizMan's fleet page.
    public static class SoldCounts
    {
        // Today and yesterday: the page shows today, and yesterday's count still matters to a late shift.
        public const int KeepDays = 2;

        public static void Record(VehicleState vehicle, int day, string item, int units)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (string.IsNullOrEmpty(item)) throw new ArgumentException("Item must not be empty.", nameof(item));
            if (units <= 0) return;
            vehicle.Sold ??= new List<SoldDay>();
            vehicle.Sold.RemoveAll(d => d == null || d.Day <= day - KeepDays);
            var entry = vehicle.Sold.FirstOrDefault(d => d.Day == day);
            if (entry == null)
            {
                entry = new SoldDay { Day = day };
                vehicle.Sold.Add(entry);
            }
            entry.Units ??= new Dictionary<string, int>(StringComparer.Ordinal);
            entry.Units.TryGetValue(item, out var before);
            entry.Units[item] = before > int.MaxValue - units ? int.MaxValue : before + units;
        }

        // Most sold first, then by name.
        public static IReadOnlyList<(string Item, int Units)> On(VehicleState? vehicle, int day)
        {
            var entry = vehicle?.Sold?.FirstOrDefault(d => d != null && d.Day == day);
            if (entry?.Units == null) return Array.Empty<(string, int)>();
            return entry.Units.Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => (p.Key, p.Value)).ToList();
        }
    }

    // One vehicle's money for the fleet page: today, and the last seven days including today.
    public sealed class VehicleMoney
    {
        public long TodaySalesCents { get; set; }
        public long TodayProfitCents { get; set; }
        public long WeekSalesCents { get; set; }
        public long WeekProfitCents { get; set; }
    }

    // The numbers on BizMan's fleet page, from the per-vehicle ledger. Sales are street sales and catering fees;
    // profit takes off the stock sold, crew wages, parking, the permit, commissary rent and overnight fines.
    // The game pays wages at midnight into the day before, so today's profit has no wages in it until the day is over.
    public static class FleetReport
    {
        public const int WeekDays = 7;

        public static VehicleMoney Vehicle(SaveState? state, string vehicleId, int today)
        {
            var money = new VehicleMoney();
            if (state?.DailySales == null) return money;
            foreach (var entry in state.DailySales)
            {
                if (entry?.ByVehicle == null || entry.Day > today || entry.Day <= today - WeekDays) continue;
                foreach (var day in entry.ByVehicle)
                {
                    if (day == null || !string.Equals(day.VehicleId, vehicleId, StringComparison.Ordinal)) continue;
                    var sales = Sum(day.SalesCents, day.CateringCents);
                    var profit = sales - Sum(Sum(day.StockCents, day.WagesCents),
                        Sum(Sum(day.ParkingCents, day.PermitCents), Sum(day.CommissaryRentCents, day.OvernightFineCents)));
                    money.WeekSalesCents = Sum(money.WeekSalesCents, sales);
                    money.WeekProfitCents += profit;
                    if (entry.Day != today) continue;
                    money.TodaySalesCents = Sum(money.TodaySalesCents, sales);
                    money.TodayProfitCents += profit;
                }
            }
            return money;
        }

        // A vehicle's average daily income for the overview's "Avg Daily Income" column, the game's way
        // (BuildingRegistration.GetAvgDailyIncome(7)): the profit of each of the last seven finished days the vehicle
        // has a record for, averaged over those days only, so a new vehicle's first days aren't diluted by days it
        // didn't exist. Today is left out because its wages are only booked at midnight. 0 with no finished day.
        public static long AvgDailyProfitCents(SaveState? state, string vehicleId, int today)
        {
            if (state?.DailySales == null) return 0;
            var days = new HashSet<int>();
            long total = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry?.ByVehicle == null || entry.Day >= today || entry.Day < today - WeekDays) continue;
                foreach (var day in entry.ByVehicle)
                {
                    if (day == null || !string.Equals(day.VehicleId, vehicleId, StringComparison.Ordinal)) continue;
                    days.Add(entry.Day);
                    total += Sum(day.SalesCents, day.CateringCents) - Sum(Sum(day.StockCents, day.WagesCents),
                        Sum(Sum(day.ParkingCents, day.PermitCents), Sum(day.CommissaryRentCents, day.OvernightFineCents)));
                }
            }
            return days.Count == 0 ? 0 : (long)Math.Round(total / (double)days.Count, MidpointRounding.AwayFromZero);
        }

        // The whole business's profit over the last seven days, for its line in BizMan's list, on the same terms as
        // Econoview's Street food total: the day's sales and stock from its totals (which also hold whatever was
        // recorded before the per-vehicle breakdown began, even on a day that has both), less the itemised crew
        // wages, parking, permits, commissary rent and overnight fines, the licence, event places and delivery fees.
        public static long WeekProfitCents(SaveState? state, int today)
        {
            if (state?.DailySales == null) return 0;
            long total = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.Day > today || entry.Day <= today - WeekDays) continue;
                total += DayProfitCents(entry);
            }
            return total;
        }

        // The whole business's average daily income for its line in BizMan's list, by the rows' rule
        // (AvgDailyProfitCents): the last seven finished days with a record, each on WeekProfitCents's terms, averaged
        // over those days only. 0 with no finished day.
        public static long AvgDailyProfitCents(SaveState? state, int today)
        {
            if (state?.DailySales == null) return 0;
            var days = new HashSet<int>();
            long total = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.Day >= today || entry.Day < today - WeekDays) continue;
                days.Add(entry.Day);
                total += DayProfitCents(entry);
            }
            return days.Count == 0 ? 0 : (long)Math.Round(total / (double)days.Count, MidpointRounding.AwayFromZero);
        }

        private static long DayProfitCents(DailySales entry)
        {
            var total = entry.RevenueCents - entry.CostCents - entry.LicenceCents - entry.EventPlacesCents - entry.DeliveryFeesCents;
            foreach (var day in entry.ByVehicle ?? new List<VehicleDay>())
                if (day != null) total -= Sum(Sum(day.WagesCents, day.ParkingCents),
                    Sum(day.PermitCents, Sum(day.CommissaryRentCents, day.OvernightFineCents)));
            return total;
        }

        private static long Sum(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
    }
}
