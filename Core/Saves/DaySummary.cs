using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    // One vehicle's day in the mod's books, for the test bridge's day_summary: the same per-vehicle ledger lines
    // Econoview and BizMan's fleet page read (VehicleDay), the units sold (SoldCounts) and the spots worked (DayRecord).
    public sealed class VehicleDaySummary
    {
        public string VehicleId { get; set; } = string.Empty;
        public string VehicleType { get; set; } = string.Empty;
        public long SalesCents { get; set; }
        public long CateringCents { get; set; }
        public long StockCostCents { get; set; }
        public long WagesCents { get; set; }
        public long ParkingCents { get; set; }
        public long PermitCents { get; set; }
        public long CommissaryRentCents { get; set; }
        public long OvernightFineCents { get; set; }
        public long MarketingCents { get; set; }
        // Sales and catering less every cost line above (FleetReport's profit).
        public long ProfitCents { get; set; }
        // Null when the day is no longer kept: SoldCounts keeps today and yesterday only.
        public IReadOnlyList<(string Item, int Units)>? Sold { get; set; }
        // Spots the vehicle was open at and its minutes open there, null when the day's record is gone (counted for
        // reputation once the next working day is over, ReputationBook.CloseDaysBefore).
        public int? StopsServed { get; set; }
        public int? MinutesOpen { get; set; }
    }

    public sealed class DaySummaryReport
    {
        public int Day { get; set; }
        public bool InSummary { get; set; }
        public long RevenueCents { get; set; }
        public long StockCostCents { get; set; }
        public long LicenceCents { get; set; }
        public long EventPlacesCents { get; set; }
        public long DeliveryFeesCents { get; set; }
        // The whole street food business's profit that day, on FleetReport.WeekProfitCents's terms.
        public long ProfitCents { get; set; }
        // Commissaries whose morning delivery fee was paid that day (CommissaryState.FeesPaid, kept for today and yesterday).
        public IReadOnlyList<string> FeesPaidAt { get; set; } = Array.Empty<string>();
        public List<VehicleDaySummary> Vehicles { get; } = new List<VehicleDaySummary>();
    }

    public static class DaySummary
    {
        // `today` is the game's day now: it says which of the short-lived records still hold `day`.
        public static DaySummaryReport For(SaveState? state, int day, int today)
        {
            var report = new DaySummaryReport { Day = day };
            if (state == null) return report;
            var entries = (state.DailySales ?? new List<DailySales>()).Where(d => d != null && d.Day == day).ToList();
            report.InSummary = entries.Any(e => e.InSummary);
            foreach (var e in entries)
            {
                report.RevenueCents = Sum(report.RevenueCents, e.RevenueCents);
                report.StockCostCents = Sum(report.StockCostCents, e.CostCents);
                report.LicenceCents = Sum(report.LicenceCents, e.LicenceCents);
                report.EventPlacesCents += e.EventPlacesCents;
                report.DeliveryFeesCents += e.DeliveryFeesCents;
            }
            report.ProfitCents = entries.Aggregate(0L, (sum, e) => sum + FleetReport.DayProfitCents(e));
            report.FeesPaidAt = (state.Commissary?.FeesPaid ?? new List<Commissary.CommissaryFeePaid>())
                .Where(f => f != null && f.Day == day).Select(f => f.CommissaryId).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();

            var detailed = day <= today && day >= today - (SoldCounts.KeepDays - 1);
            var ids = new List<string>();
            foreach (var v in state.Vehicles ?? new List<VehicleState>())
                if (v != null && !ids.Contains(v.VehicleId)) ids.Add(v.VehicleId);
            foreach (var e in entries)
                foreach (var v in e.ByVehicle ?? new List<VehicleDay>())
                    if (v != null && !ids.Contains(v.VehicleId)) ids.Add(v.VehicleId);
            foreach (var id in ids.OrderBy(i => i, StringComparer.Ordinal))
            {
                var vehicle = (state.Vehicles ?? new List<VehicleState>()).FirstOrDefault(v => v != null && v.VehicleId == id);
                var summary = new VehicleDaySummary { VehicleId = id, VehicleType = vehicle?.VehicleType ?? string.Empty };
                foreach (var line in entries.SelectMany(e => e.ByVehicle ?? new List<VehicleDay>()).Where(v => v != null && v.VehicleId == id))
                {
                    summary.SalesCents = Sum(summary.SalesCents, line.SalesCents);
                    summary.CateringCents = Sum(summary.CateringCents, line.CateringCents);
                    summary.StockCostCents = Sum(summary.StockCostCents, line.StockCents);
                    summary.WagesCents = Sum(summary.WagesCents, line.WagesCents);
                    summary.ParkingCents = Sum(summary.ParkingCents, line.ParkingCents);
                    summary.PermitCents = Sum(summary.PermitCents, line.PermitCents);
                    summary.CommissaryRentCents = Sum(summary.CommissaryRentCents, line.CommissaryRentCents);
                    summary.OvernightFineCents = Sum(summary.OvernightFineCents, line.OvernightFineCents);
                    summary.MarketingCents = Sum(summary.MarketingCents, line.MarketingCents);
                }
                summary.ProfitCents = summary.SalesCents + summary.CateringCents - summary.StockCostCents - summary.WagesCents -
                                      summary.ParkingCents - summary.PermitCents - summary.CommissaryRentCents - summary.OvernightFineCents - summary.MarketingCents;
                if (detailed)
                {
                    summary.Sold = SoldCounts.On(vehicle, day);
                    var record = vehicle?.Days?.FirstOrDefault(d => d != null && d.Day == day);
                    var visits = record?.Visits?.Where(v => v != null && v.Minutes > 0).ToList() ?? new List<SpotMinutes>();
                    summary.StopsServed = visits.Count;
                    summary.MinutesOpen = visits.Aggregate(0, (sum, v) => sum > int.MaxValue - v.Minutes ? int.MaxValue : sum + v.Minutes);
                }
                // A vehicle with nothing that day and no record of it isn't listed.
                if (vehicle == null && summary.ProfitCents == 0 && summary.SalesCents == 0) continue;
                report.Vehicles.Add(summary);
            }
            return report;
        }

        private static long Sum(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
    }
}
