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

    // Daily marketing fees (design: Marketing). A level runs only on a day its fee is paid: the first time the day changes
    // with a level on, that day's whole fee is due; days the game skipped in between are neither charged nor marketed. A
    // level chosen during the day is paid at once, a higher one for the difference to what that day already paid; a lower
    // one or none refunds nothing. `pay` takes the money through the game and says whether it did. A level priced at 0
    // runs without a payment.
    public static class MarketingFees
    {
        // The level that raised the vehicle's passers on `day`: the one paid for that day, the current paid day's or the
        // one before it, so sales still catching up on yesterday keep yesterday's marketing. Null for none, an unknown
        // level or a vehicle kind the level has no price for.
        public static MarketingLevel? Active(VehicleState? vehicle, MarketingRules rules, int day)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (vehicle == null) return null;
            if (vehicle.MarketingPaidThroughDay == day && Running(vehicle, rules, vehicle.MarketingLevel) is MarketingLevel current) return current;
            return vehicle.MarketingPreviousDay == day ? Running(vehicle, rules, vehicle.MarketingPreviousLevel) : null;
        }

        // DemandQuery.Marketing for the vehicle on `day`.
        public static double Factor(VehicleState? vehicle, MarketingRules rules, int day) => Active(vehicle, rules, day)?.Footfall ?? 1.0;

        // A new day: today's fee for the vehicle's level, once. Null when nothing is to pay: no level, none, an unknown
        // level, a vehicle kind the level has no price for, today already paid, or a level priced at 0 (which then runs
        // today). A refused payment turns the level off.
        public static MarketingCharge? Renew(VehicleState vehicle, MarketingRules rules, int today, Func<MarketingCharge, bool> pay)
        {
            Check(vehicle, rules, today, pay);
            if (vehicle.MarketingPaidThroughDay >= today) return null;
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
            else Change(vehicle, null, today);
            return charge;
        }

        // The player picks a level. Null when nothing is to pay (none, a lower level, one today's payment covers, or one
        // priced at 0), and the level is set. Otherwise the charge: the level is set only if it was paid. Throws for an
        // unknown level or a vehicle kind it has no price for.
        public static MarketingCharge? Choose(VehicleState vehicle, MarketingRules rules, string levelId, int today, Func<MarketingCharge, bool> pay)
        {
            Check(vehicle, rules, today, pay);
            var level = rules.Level(levelId) ?? throw new ArgumentException($"Unknown marketing level '{levelId}'.", nameof(levelId));
            if (level.IsNone)
            {
                Change(vehicle, null, today);
                return null;
            }
            if (!level.CostPerDayCents.TryGetValue(vehicle.VehicleType, out var price))
                throw new InvalidOperationException($"Marketing level '{level.Id}' has no price for a {vehicle.VehicleType}.");
            var paidToday = vehicle.MarketingPaidThroughDay == today ? vehicle.MarketingPaidCents : 0;
            if (price <= paidToday)
            {
                Settle(vehicle, level.Id, today, paidToday);
                return null;
            }
            var charge = new MarketingCharge { VehicleId = vehicle.VehicleId, Level = level.Id, Day = today, Cents = price - paidToday };
            charge.Paid = pay(charge);
            if (charge.Paid) Settle(vehicle, level.Id, today, price);
            return charge;
        }

        private static MarketingLevel? Running(VehicleState vehicle, MarketingRules rules, string? levelId)
        {
            var level = rules.Level(levelId);
            return level == null || level.IsNone || !level.CostPerDayCents.ContainsKey(vehicle.VehicleType) ? null : level;
        }

        // The level `day` is paid with. Moving to a new day keeps the old paid day and its level as the previous one.
        private static void Settle(VehicleState vehicle, string levelId, int day, long paidCents)
        {
            Change(vehicle, levelId, day);
            vehicle.MarketingPaidThroughDay = day;
            vehicle.MarketingPaidCents = paidCents;
        }

        // Sets the level. Before an earlier paid day loses the level it ran with, that day and level become the previous
        // ones (once: a second change today must not overwrite them); a change on the paid day itself is that day's level
        // from now on.
        private static void Change(VehicleState vehicle, string? levelId, int today)
        {
            if (vehicle.MarketingPaidThroughDay is int paid && paid < today && vehicle.MarketingPreviousDay != paid)
            {
                vehicle.MarketingPreviousDay = paid;
                vehicle.MarketingPreviousLevel = vehicle.MarketingLevel;
            }
            vehicle.MarketingLevel = levelId;
        }

        private static void Check(VehicleState vehicle, MarketingRules rules, int today, Func<MarketingCharge, bool> pay)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (pay == null) throw new ArgumentNullException(nameof(pay));
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
        }
    }
}
