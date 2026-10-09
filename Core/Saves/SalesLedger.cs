using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    // One vehicle's lines in Econoview's street food breakdown.
    public enum LedgerLine
    {
        Sales,
        Catering,
        Stock,
        Wages,
        Parking,
        Permit,
        CommissaryRent,
        OvernightFine
    }

    public static class SalesLedger
    {
        public static bool CanRecord(SaveState state, int day, long revenueCents)
        {
            if (state == null || state.DailySales == null || revenueCents < 0) return false;
            var remaining = long.MaxValue - revenueCents;
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.RevenueCents < 0) return false;
                if (entry.Day == day && entry.RevenueCents > long.MaxValue - revenueCents) return false;
                if (entry.RevenueCents > remaining) return false;
                remaining -= entry.RevenueCents;
            }
            return true;
        }

        public static void Record(SaveState state, int day, long revenueCents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (revenueCents < 0) throw new ArgumentOutOfRangeException(nameof(revenueCents));
            if (revenueCents == 0) return;
            var entry = state.DailySales.FirstOrDefault(d => d.Day == day);
            if (entry == null)
            {
                entry = new DailySales { Day = day };
                state.DailySales.Add(entry);
            }
            entry.RevenueCents = checked(entry.RevenueCents + revenueCents);
        }

        // Adds the wholesale cost of stock sold on a day. Never fails: the cost only feeds the profit shown
        // in the game's summaries, so an overflow is capped rather than refusing a sale already paid.
        public static void RecordCost(SaveState state, int day, long costCents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (costCents <= 0) return;
            var entry = GetOrAddDay(state, day);
            entry.CostCents = Add(entry.CostCents, costCents);
        }

        // The same, for the vehicle that sold or served the stock.
        public static void RecordCost(SaveState state, int day, string vehicleId, long costCents)
        {
            RecordCost(state, day, costCents);
            RecordLine(state, day, vehicleId, LedgerLine.Stock, costCents);
        }

        // Adds to one vehicle's line for a day, for Econoview's breakdown. Like RecordCost it never fails:
        // amounts are capped, and nothing at or below 0 is recorded. Sales and catering go through StreetSales,
        // which also keeps the day's revenue for tax.
        public static void RecordLine(SaveState state, int day, string vehicleId, LedgerLine line, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(vehicleId)) throw new ArgumentException("Vehicle id must not be empty.", nameof(vehicleId));
            if (cents <= 0) return;
            var vehicle = GetOrAddVehicle(GetOrAddDay(state, day), vehicleId);
            switch (line)
            {
                case LedgerLine.Sales: vehicle.SalesCents = Add(vehicle.SalesCents, cents); break;
                case LedgerLine.Catering: vehicle.CateringCents = Add(vehicle.CateringCents, cents); break;
                case LedgerLine.Stock: vehicle.StockCents = Add(vehicle.StockCents, cents); break;
                case LedgerLine.Wages: vehicle.WagesCents = Add(vehicle.WagesCents, cents); break;
                case LedgerLine.Parking: vehicle.ParkingCents = Add(vehicle.ParkingCents, cents); break;
                case LedgerLine.Permit: vehicle.PermitCents = Add(vehicle.PermitCents, cents); break;
                case LedgerLine.CommissaryRent: vehicle.CommissaryRentCents = Add(vehicle.CommissaryRentCents, cents); break;
                case LedgerLine.OvernightFine: vehicle.OvernightFineCents = Add(vehicle.OvernightFineCents, cents); break;
                default: throw new ArgumentOutOfRangeException(nameof(line));
            }
        }

        // A cost the game charges and keeps in its own summary rows (crew wages, parking). It is only recorded
        // while the day's summary is still to come: once the game has stored that day's figures, a late charge
        // isn't in them, so moving it out of the game's row would break the total. False when not recorded.
        public static bool RecordGameCost(SaveState state, int day, string vehicleId, LedgerLine line, long cents)
        {
            if (line != LedgerLine.Wages && line != LedgerLine.Parking)
                throw new ArgumentOutOfRangeException(nameof(line), "Only wages and parking are the game's own costs.");
            if (IsInSummary(state, day)) return false;
            RecordLine(state, day, vehicleId, line, cents);
            return true;
        }

        // The vendor licence, the whole fleet's.
        public static void RecordLicence(SaveState state, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cents <= 0) return;
            var entry = GetOrAddDay(state, day);
            entry.LicenceCents = Add(entry.LicenceCents, cents);
        }

        // Event places: a booking adds its fee, a refund passes a negative amount.
        public static void RecordEventPlaces(SaveState state, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cents == 0) return;
            var entry = GetOrAddDay(state, day);
            entry.EventPlacesCents = cents > 0
                ? Add(entry.EventPlacesCents, cents)
                : (entry.EventPlacesCents < long.MinValue - cents ? long.MinValue : entry.EventPlacesCents + cents);
        }

        // Call only after the game payment succeeds (including a retry), on the day it was paid.
        public static void RecordCommissaryRent(SaveState state, int day, string vehicleId, long cents) =>
            RecordLine(state, day, vehicleId, LedgerLine.CommissaryRent, cents);

        public static void RecordOvernightFine(SaveState state, int day, string vehicleId, long cents) =>
            RecordLine(state, day, vehicleId, LedgerLine.OvernightFine, cents);

        // Fleet-wide, like event places: a payment adds its fee, a refund passes a negative amount.
        // The goods price is carried into cargo and recorded by RecordCost only when those goods sell.
        public static void RecordDeliveryFees(SaveState state, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cents == 0) return;
            var entry = GetOrAddDay(state, day);
            entry.DeliveryFeesCents = cents > 0
                ? Add(entry.DeliveryFeesCents, cents)
                : (entry.DeliveryFeesCents < long.MinValue - cents ? long.MinValue : entry.DeliveryFeesCents + cents);
        }

        // Marks a day as counted in the game's financial summary, keeping the amount that was added.
        public static void MarkInSummary(SaveState state, int day)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var added = DayProfitCents(state, day);
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.Day != day) continue;
                entry.InSummary = true;
                entry.SummaryCents = added;
            }
        }

        public static bool IsInSummary(SaveState? state, int day) =>
            state != null && state.DailySales.Any(d => d != null && d.Day == day && d.InSummary);

        // What the day summary adds for a day the mod knows of: sales and catering, less the stock sold, the
        // permits, commissary rent, overnight fines, the licence, event places and delivery fees. Crew wages
        // and parking aren't in it: the game's summary has them.
        public static long DayProfitCents(SaveState? state, int day)
        {
            if (state == null) return 0;
            long profit = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.Day != day) continue;
                profit = checked(profit + entry.RevenueCents - entry.CostCents - entry.LicenceCents - entry.EventPlacesCents - entry.DeliveryFeesCents);
                foreach (var vehicle in entry.ByVehicle)
                    if (vehicle != null) profit = checked(profit - vehicle.PermitCents - vehicle.CommissaryRentCents - vehicle.OvernightFineCents);
            }
            return profit;
        }

        // What the game's stored summary holds for a day: the amount kept when it was marked, or for a day marked
        // by an older version, sales less stock cost (what those versions added). 0 for a day not in the summary.
        public static long SummaryCents(SaveState? state, int day)
        {
            if (state == null) return 0;
            long total = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.Day != day || !entry.InSummary) continue;
                total = checked(total + (entry.SummaryCents ?? entry.RevenueCents - entry.CostCents));
            }
            return total;
        }

        // Inclusive on both ends, like the game's tax period.
        public static long SumCents(SaveState? state, int firstDay, int lastDay)
        {
            if (state == null) return 0;
            long total = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry.Day >= firstDay && entry.Day <= lastDay)
                    total = checked(total + entry.RevenueCents);
            }
            return total;
        }

        private static DailySales GetOrAddDay(SaveState state, int day)
        {
            var entry = state.DailySales.FirstOrDefault(d => d != null && d.Day == day);
            if (entry == null)
            {
                entry = new DailySales { Day = day };
                state.DailySales.Add(entry);
            }
            return entry;
        }

        private static VehicleDay GetOrAddVehicle(DailySales entry, string vehicleId)
        {
            entry.ByVehicle ??= new List<VehicleDay>();
            var vehicle = entry.ByVehicle.FirstOrDefault(v => v != null && string.Equals(v.VehicleId, vehicleId, StringComparison.Ordinal));
            if (vehicle == null)
            {
                vehicle = new VehicleDay { VehicleId = vehicleId };
                entry.ByVehicle.Add(vehicle);
            }
            return vehicle;
        }

        private static long Add(long total, long cents) => total > long.MaxValue - cents ? long.MaxValue : total + cents;
    }
}
