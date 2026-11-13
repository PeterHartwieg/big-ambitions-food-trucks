using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    public enum LedgerLine
    {
        Sales,
        Catering,
        Stock,
        Wages,
        Parking,
        Permit,
        CommissaryRent,
        OvernightFine,
        Marketing
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

        public static void RecordCost(SaveState state, int day, long costCents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (costCents <= 0) return;
            var entry = GetOrAddDay(state, day);
            entry.CostCents = Add(entry.CostCents, costCents);
        }

        public static void RecordCost(SaveState state, int day, string vehicleId, long costCents)
        {
            RecordCost(state, day, costCents);
            RecordLine(state, day, vehicleId, LedgerLine.Stock, costCents);
        }

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
                case LedgerLine.Marketing: vehicle.MarketingCents = Add(vehicle.MarketingCents, cents); break;
                default: throw new ArgumentOutOfRangeException(nameof(line));
            }
        }

        public static bool RecordGameCost(SaveState state, int day, string vehicleId, LedgerLine line, long cents)
        {
            if (line != LedgerLine.Wages && line != LedgerLine.Parking)
                throw new ArgumentOutOfRangeException(nameof(line), "Only wages and parking are the game's own costs.");
            if (IsInSummary(state, day)) return false;
            RecordLine(state, day, vehicleId, line, cents);
            return true;
        }

        public static void RecordLicence(SaveState state, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cents <= 0) return;
            var entry = GetOrAddDay(state, day);
            entry.LicenceCents = Add(entry.LicenceCents, cents);
        }

        public static void RecordEventPlaces(SaveState state, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cents == 0) return;
            var entry = GetOrAddDay(state, day);
            entry.EventPlacesCents = cents > 0
                ? Add(entry.EventPlacesCents, cents)
                : (entry.EventPlacesCents < long.MinValue - cents ? long.MinValue : entry.EventPlacesCents + cents);
        }

        public static void RecordCommissaryRent(SaveState state, int day, string vehicleId, long cents) =>
            RecordLine(state, day, vehicleId, LedgerLine.CommissaryRent, cents);

        public static void RecordOvernightFine(SaveState state, int day, string vehicleId, long cents) =>
            RecordLine(state, day, vehicleId, LedgerLine.OvernightFine, cents);

        public static void RecordMarketing(SaveState state, int day, string vehicleId, long cents) =>
            RecordLine(state, day, vehicleId, LedgerLine.Marketing, cents);

        public static void RecordDeliveryFees(SaveState state, int day, long cents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (cents == 0) return;
            var entry = GetOrAddDay(state, day);
            entry.DeliveryFeesCents = cents > 0
                ? Add(entry.DeliveryFeesCents, cents)
                : (entry.DeliveryFeesCents < long.MinValue - cents ? long.MinValue : entry.DeliveryFeesCents + cents);
        }

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

        public static long DayProfitCents(SaveState? state, int day)
        {
            if (state == null) return 0;
            long profit = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry == null || entry.Day != day) continue;
                profit = checked(profit + entry.RevenueCents - entry.CostCents - entry.LicenceCents - entry.EventPlacesCents - entry.DeliveryFeesCents);
                foreach (var vehicle in entry.ByVehicle)
                    if (vehicle != null) profit = checked(profit - vehicle.PermitCents - vehicle.CommissaryRentCents - vehicle.OvernightFineCents - vehicle.MarketingCents);
            }
            return profit;
        }

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
