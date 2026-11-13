using System;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Marketing
{
    public sealed class MarketingCharge
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }
        public bool Paid { get; set; }
    }

    public static class MarketingFees
    {
        public static MarketingLevel? Active(VehicleState? vehicle, MarketingRules rules, int day)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (vehicle == null) return null;
            if (vehicle.MarketingPaidThroughDay == day && Running(vehicle, rules, vehicle.MarketingPaidLevel) is MarketingLevel current) return current;
            return vehicle.MarketingPreviousDay == day ? Running(vehicle, rules, vehicle.MarketingPreviousLevel) : null;
        }

        public static double Factor(VehicleState? vehicle, MarketingRules rules, int day) => Active(vehicle, rules, day)?.Footfall ?? 1.0;

        public static MarketingLevel? Booked(VehicleState? vehicle, MarketingRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return vehicle == null ? null : Running(vehicle, rules, vehicle.MarketingLevel);
        }

        public static MarketingCharge? Renew(VehicleState vehicle, MarketingRules rules, int today, Func<MarketingCharge, bool> pay)
        {
            Check(vehicle, rules, today);
            if (pay == null) throw new ArgumentNullException(nameof(pay));
            if (vehicle.MarketingPaidThroughDay >= today || vehicle.MarketingStartsDay > today) return null;
            var level = Running(vehicle, rules, vehicle.MarketingLevel);
            if (level == null) return null;
            var cents = level.CostPerDayCents[vehicle.VehicleType];
            if (cents <= 0)
            {
                Settle(vehicle, level.Id, today, 0);
                return null;
            }
            var charge = new MarketingCharge { VehicleId = vehicle.VehicleId, Level = level.Id, Day = today, Cents = cents };
            charge.Paid = pay(charge);
            if (charge.Paid) Settle(vehicle, level.Id, today, cents);
            else Book(vehicle, null, null);
            return charge;
        }

        public static void Choose(VehicleState vehicle, MarketingRules rules, string levelId, int today)
        {
            Check(vehicle, rules, today);
            var level = rules.Level(levelId) ?? throw new ArgumentException($"Unknown marketing level '{levelId}'.", nameof(levelId));
            if (level.IsNone)
            {
                Book(vehicle, null, null);
                return;
            }
            if (!level.CostPerDayCents.ContainsKey(vehicle.VehicleType))
                throw new InvalidOperationException($"Marketing level '{level.Id}' has no price for a {vehicle.VehicleType}.");
            Book(vehicle, level.Id, today + 1);
        }

        private static MarketingLevel? Running(VehicleState vehicle, MarketingRules rules, string? levelId)
        {
            var level = rules.Level(levelId);
            return level == null || level.IsNone || vehicle.VehicleType == null || !level.CostPerDayCents.ContainsKey(vehicle.VehicleType) ? null : level;
        }

        private static void Book(VehicleState vehicle, string? levelId, int? startsDay)
        {
            vehicle.MarketingLevel = levelId;
            vehicle.MarketingStartsDay = startsDay;
        }

        private static void Settle(VehicleState vehicle, string levelId, int day, long paidCents)
        {
            if (vehicle.MarketingPaidThroughDay is int paid && paid < day)
            {
                vehicle.MarketingPreviousDay = paid;
                vehicle.MarketingPreviousLevel = vehicle.MarketingPaidLevel;
            }
            vehicle.MarketingPaidThroughDay = day;
            vehicle.MarketingPaidLevel = levelId;
            vehicle.MarketingPaidCents = paidCents;
            vehicle.MarketingStartsDay = null;
        }

        private static void Check(VehicleState vehicle, MarketingRules rules, int today)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
        }
    }
}
