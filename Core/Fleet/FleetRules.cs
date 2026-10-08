using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    // A step up in the fleet cap: reached after Days in business or LifetimeSales items sold, whichever comes first.
    public sealed class PermitUnlock
    {
        public PermitUnlock(int days, long lifetimeSales)
        {
            if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));
            if (lifetimeSales < 0) throw new ArgumentOutOfRangeException(nameof(lifetimeSales));
            Days = days;
            LifetimeSales = lifetimeSales;
        }

        public int Days { get; }
        public long LifetimeSales { get; }
    }

    public sealed class FleetRules
    {
        public FleetRules(int licenceDays = 60, int renewWindowDays = 10, int maxPermits = 4, IReadOnlyList<PermitUnlock>? unlocks = null,
            double driveMinutesPerMetre = 0.04, double pushMetresPerSecond = 1.2, double roadFactor = 1.0,
            int maxStops = 6, int latestCloseMinute = 27 * 60)
        {
            LicenceDays = licenceDays;
            RenewWindowDays = renewWindowDays;
            MaxPermits = maxPermits;
            Unlocks = unlocks ?? new[] { new PermitUnlock(14, 400), new PermitUnlock(35, 1500), new PermitUnlock(70, 4000) };
            DriveMinutesPerMetre = driveMinutesPerMetre;
            PushMetresPerSecond = pushMetresPerSecond;
            RoadFactor = roadFactor;
            MaxStops = maxStops;
            LatestCloseMinute = latestCloseMinute;
        }

        // How long a vendor licence lasts. The game's Food Safety diploma itself never expires [code, unverified in game].
        public int LicenceDays { get; }
        // A licence can be renewed once this few days are left, so nobody banks years of licence.
        public int RenewWindowDays { get; }
        // The permit cap's ceiling, the mod setting's default.
        public int MaxPermits { get; }
        // One permit at the start, one more per unlock reached, never above MaxPermits.
        public IReadOnlyList<PermitUnlock> Unlocks { get; }
        // TaxiSystem.MinutePerMeter: game minutes per straight-line metre at game speed 1 [code, unverified in game].
        public double DriveMinutesPerMetre { get; }
        // GlobalReferences.walkingSpeedWalk: a cart moves at walking pace [code, unverified in game].
        public double PushMetresPerSecond { get; }
        // Straight-line distance times this; 1.0 matches the game's taxi, which ignores roads.
        public double RoadFactor { get; }
        public int MaxStops { get; }
        // Minutes after the schedule day's midnight; 27 × 60 lets a late stop close at 03:00.
        public int LatestCloseMinute { get; }

        internal static FleetRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "licenceDays", "renewWindowDays", "maxPermits", "permitUnlocks", "travel", "schedules");
            var licenceDays = r.Integer(obj, "licenceDays", 1, 3650);
            var renewWindow = r.Integer(obj, "renewWindowDays", 0, 3650);
            if (renewWindow > licenceDays) throw r.Fail(DataReader.PathOf(obj, "renewWindowDays"), "must not be above licenceDays");
            var maxPermits = r.Integer(obj, "maxPermits", 1, 100);

            var unlocks = new List<PermitUnlock>();
            foreach (var token in r.List(obj, "permitUnlocks"))
            {
                if (!(token is JObject unlock)) throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(unlock, "days", "lifetimeSales");
                var days = r.Integer(unlock, "days", 0, 100000);
                var sales = r.Integer(unlock, "lifetimeSales", 0, int.MaxValue);
                if (unlocks.Count > 0 && days < unlocks[unlocks.Count - 1].Days)
                    throw r.Fail(DataReader.PathOf(unlock, "days"), "must not be below the unlock before it");
                if (unlocks.Count > 0 && sales < unlocks[unlocks.Count - 1].LifetimeSales)
                    throw r.Fail(DataReader.PathOf(unlock, "lifetimeSales"), "must not be below the unlock before it");
                unlocks.Add(new PermitUnlock(days, sales));
            }

            var travel = r.Object(obj, "travel");
            r.OnlyKeys(travel, "driveMinutesPerMetre", "pushMetresPerSecond", "roadFactor");
            var schedules = r.Object(obj, "schedules");
            r.OnlyKeys(schedules, "maxStops", "latestCloseMinute");
            return new FleetRules(licenceDays, renewWindow, maxPermits, unlocks,
                r.Number(travel, "driveMinutesPerMetre", 0.0001, 10),
                r.Number(travel, "pushMetresPerSecond", 0.1, 20),
                r.Number(travel, "roadFactor", 1, 5),
                r.Integer(schedules, "maxStops", 1, 24),
                r.Integer(schedules, "latestCloseMinute", 60, 48 * 60));
        }
    }
}
