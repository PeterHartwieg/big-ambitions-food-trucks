using System;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Marketing
{
    // One marketing fee for one vehicle and one game day. Paid says whether the game took the money.
    public sealed class MarketingCharge
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }
        public bool Paid { get; set; }
    }

    // Daily marketing fees (design: Marketing). A level runs only on a day its fee is paid. Picking a level books it from
    // the next day: nothing is charged and today keeps the level it paid for, if any. The first time the day changes with
    // a level booked, that day's whole fee is due; days the game skipped in between are neither charged nor marketed.
    // Picking again the same day only replaces the booking. `pay` takes the money through the game and says whether it
    // did. A level priced at 0 runs without a payment.
    public static class MarketingFees
    {
        // The level that raised the vehicle's passers on `day`: the one paid for that day, the current paid day's or the
        // one before it, so sales still catching up on yesterday keep yesterday's marketing. Null for none, an unknown
        // level or a vehicle kind the level has no price for.
        public static MarketingLevel? Active(VehicleState? vehicle, MarketingRules rules, int day)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (vehicle == null) return null;
            if (vehicle.MarketingPaidThroughDay == day && Running(vehicle, rules, vehicle.MarketingPaidLevel) is MarketingLevel current) return current;
            return vehicle.MarketingPreviousDay == day ? Running(vehicle, rules, vehicle.MarketingPreviousLevel) : null;
        }

        // DemandQuery.Marketing for the vehicle on `day`.
        public static double Factor(VehicleState? vehicle, MarketingRules rules, int day) => Active(vehicle, rules, day)?.Footfall ?? 1.0;

        // The booked level that renews at the next day change, for the page and the bridge. Null for none, an unknown
        // level or a vehicle kind the level has no price for.
        public static MarketingLevel? Booked(VehicleState? vehicle, MarketingRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return vehicle == null ? null : Running(vehicle, rules, vehicle.MarketingLevel);
        }

        // A new day: today's fee for the booked level, once. Null when nothing is to pay: no level, none, an unknown level,
        // a vehicle kind the level has no price for, today already paid, a level booked today (it starts tomorrow), or a
        // level priced at 0 (which then runs today). A refused payment turns the level off.
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

        // The player picks a level: it is booked from the next day, and today runs on with whatever today paid for. Throws
        // for an unknown level or a vehicle kind it has no price for.
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
            return level == null || level.IsNone || !level.CostPerDayCents.ContainsKey(vehicle.VehicleType) ? null : level;
        }

        // Changes the booking; the paid day keeps the level it paid for.
        private static void Book(VehicleState vehicle, string? levelId, int? startsDay)
        {
            vehicle.MarketingLevel = levelId;
            vehicle.MarketingStartsDay = startsDay;
        }

        // The level `day` is paid with. The paid day before it and its level become the previous ones.
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
