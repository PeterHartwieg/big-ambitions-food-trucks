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
            int courseMinutes = 6 * 60, int coursePricePerHour = 25, int restockMinutes = 30, int maxShiftHours = 12)
        {
            RestockMinutes = restockMinutes;
            MaxShiftHours = maxShiftHours;
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
            CourseMinutes = courseMinutes;
            CoursePricePerHour = coursePricePerHour;
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
        // A route day starts this long before the vehicle leaves its commissary bay: the morning restock, which needs
        // someone on shift (Peter, 16 October 2026).
        public int RestockMinutes { get; }
        // The longest shift a crew member may have in a day, the game's ScheduleHelper.ShiftLengthCap.
        public int MaxShiftHours { get; }
        // The Food Safety course the business school's Market Demands door offers: its length and its
        // price per hour in whole dollars, as the game's DiplomaData stores them.
        public int CourseMinutes { get; }
        public int CoursePricePerHour { get; }

        internal static FleetRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "licenceDays", "renewWindowDays", "licenceFee", "permitFee", "travel", "schedules", "course");
            var licenceDays = r.Integer(obj, "licenceDays", 1, 3650);
            var renewWindow = r.Integer(obj, "renewWindowDays", 0, 3650);
            if (renewWindow > licenceDays) throw r.Fail(DataReader.PathOf(obj, "renewWindowDays"), "must not be above licenceDays");
            var licenceFee = Cents(r.Number(obj, "licenceFee", 0, 1_000_000));
            var permitFee = Cents(r.Number(obj, "permitFee", 0, 1_000_000));

            var travel = r.Object(obj, "travel");
            r.OnlyKeys(travel, "driveMinutesPerMetre", "pushMetresPerSecond", "roadFactor");
            var schedules = r.Object(obj, "schedules");
            r.OnlyKeys(schedules, "maxStops", "latestCloseMinute", "stopRadiusMetres", "restockMinutes", "maxShiftHours");
            var course = r.Object(obj, "course");
            r.OnlyKeys(course, "hours", "pricePerHour");
            return new FleetRules(licenceDays, renewWindow, licenceFee, permitFee,
                r.Number(travel, "driveMinutesPerMetre", 0.0001, 10),
                r.Number(travel, "pushMetresPerSecond", 0.1, 20),
                r.Number(travel, "roadFactor", 1, 5),
                r.Integer(schedules, "maxStops", 1, 24),
                r.Integer(schedules, "latestCloseMinute", 60, 48 * 60),
                r.Number(schedules, "stopRadiusMetres", 1, 500),
                r.Integer(course, "hours", 1, 100) * 60,
                r.Integer(course, "pricePerHour", 0, 10_000),
                r.Integer(schedules, "restockMinutes", 0, 240),
                r.Integer(schedules, "maxShiftHours", 1, 24));
        }

        private static long Cents(double dollars) => (long)Math.Round(dollars * 100);
    }
}
