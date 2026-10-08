using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    public sealed class FleetRules
    {
        public FleetRules(int licenceDays = 60, int renewWindowDays = 10, long licenceFeeCents = 250_00, long permitFeeCents = 500_00,
            double driveMinutesPerMetre = 0.04, double pushMetresPerSecond = 1.2, double roadFactor = 1.0,
            int maxStops = 6, int latestCloseMinute = 27 * 60, double stopRadiusMetres = 25,
            IReadOnlyDictionary<string, int>? crewLimits = null)
        {
            LicenceDays = licenceDays;
            RenewWindowDays = renewWindowDays;
            LicenceFeeCents = licenceFeeCents;
            PermitFeeCents = permitFeeCents;
            DriveMinutesPerMetre = driveMinutesPerMetre;
            PushMetresPerSecond = pushMetresPerSecond;
            RoadFactor = roadFactor;
            MaxStops = maxStops;
            LatestCloseMinute = latestCloseMinute;
            StopRadiusMetres = stopRadiusMetres;
            CrewLimits = crewLimits ?? new Dictionary<string, int>(StringComparer.Ordinal) { ["cart"] = 1, ["truck"] = 3 };
        }

        // How long a vendor licence lasts. The game's Food Safety diploma itself never expires [code, unverified in game].
        public int LicenceDays { get; }
        // A licence can be renewed once this few days are left, so nobody banks years of licence.
        public int RenewWindowDays { get; }
        // What the city charges for a licence period and for one vehicle's permit. Placeholders until the
        // balance pass near the end of development.
        public long LicenceFeeCents { get; }
        public long PermitFeeCents { get; }
        // TaxiSystem.MinutePerMeter: game minutes per straight-line metre at game speed 1 [code, unverified in game].
        public double DriveMinutesPerMetre { get; }
        // GlobalReferences.walkingSpeedWalk: a cart moves at walking pace [code, unverified in game].
        public double PushMetresPerSecond { get; }
        // Straight-line distance times this; 1.0 matches the game's taxi, which ignores roads.
        public double RoadFactor { get; }
        public int MaxStops { get; }
        // Minutes after the schedule day's midnight; 27 × 60 lets a late stop close at 03:00.
        public int LatestCloseMinute { get; }
        // A vehicle this close to a stop is at it; further away, the hire moves it there first.
        public double StopRadiusMetres { get; }
        // Hires per vehicle kind (design: cart 1, truck up to 3 at the window).
        public IReadOnlyDictionary<string, int> CrewLimits { get; }

        public int CrewLimit(string? vehicleKind) =>
            CrewLimits.TryGetValue(string.IsNullOrEmpty(vehicleKind) ? "cart" : vehicleKind!, out var limit) ? limit : 1;

        internal static FleetRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "licenceDays", "renewWindowDays", "licenceFee", "permitFee", "travel", "schedules", "crew");
            var licenceDays = r.Integer(obj, "licenceDays", 1, 3650);
            var renewWindow = r.Integer(obj, "renewWindowDays", 0, 3650);
            if (renewWindow > licenceDays) throw r.Fail(DataReader.PathOf(obj, "renewWindowDays"), "must not be above licenceDays");
            var licenceFee = Cents(r.Number(obj, "licenceFee", 0, 1_000_000));
            var permitFee = Cents(r.Number(obj, "permitFee", 0, 1_000_000));

            var travel = r.Object(obj, "travel");
            r.OnlyKeys(travel, "driveMinutesPerMetre", "pushMetresPerSecond", "roadFactor");
            var schedules = r.Object(obj, "schedules");
            r.OnlyKeys(schedules, "maxStops", "latestCloseMinute", "stopRadiusMetres");
            var crew = r.Object(obj, "crew");
            var crewLimits = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(crew)) crewLimits.Add(entry.Name, r.Integer(crew, entry.Name, 1, 10));
            return new FleetRules(licenceDays, renewWindow, licenceFee, permitFee,
                r.Number(travel, "driveMinutesPerMetre", 0.0001, 10),
                r.Number(travel, "pushMetresPerSecond", 0.1, 20),
                r.Number(travel, "roadFactor", 1, 5),
                r.Integer(schedules, "maxStops", 1, 24),
                r.Integer(schedules, "latestCloseMinute", 60, 48 * 60),
                r.Number(schedules, "stopRadiusMetres", 1, 500),
                crewLimits);
        }

        private static long Cents(double dollars) => (long)Math.Round(dollars * 100);
    }
}
