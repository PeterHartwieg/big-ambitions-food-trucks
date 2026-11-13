using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    public sealed class FleetRules
    {
        public FleetRules(int licenceDays = 60, int renewWindowDays = 10, long licenceFeeCents = 250_00, long permitFeeCents = 500_00,
            double driveMinutesPerMetre = 0.04, double pushMetresPerSecond = 4.0, double roadFactor = 1.0,
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

        public int LicenceDays { get; }
        public int RenewWindowDays { get; }
        // What the city charges for a licence period and for one vehicle's permit. Placeholders until the
        // balance pass near the end of development.
        public long LicenceFeeCents { get; }
        public long PermitFeeCents { get; }
        public double DriveMinutesPerMetre { get; }
        public double PushMetresPerSecond { get; }
        public double RoadFactor { get; }
        public int MaxStops { get; }
        public int LatestCloseMinute { get; }
        public double StopRadiusMetres { get; }
        public int RestockMinutes { get; }
        public int MaxShiftHours { get; }
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
