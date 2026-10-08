using System;
using System.Collections.Generic;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;

namespace FoodTrucks.Core.Fleet
{
    public enum TravelMode
    {
        // Trucks: the game's taxi formula.
        Drive,
        // Carts: pushed at walking pace.
        Push
    }

    // How long a hire's vehicle is off the map between two stops. The game has no NPC that drives a
    // player vehicle, so the mod marks it in transit for this long and then places it at the next stop
    // (design: Technical answers, row 11).
    public static class Travel
    {
        // Vehicles saved before Milestone 4 have no type, and all of them were carts.
        public static TravelMode ModeFor(string? vehicleType) =>
            string.IsNullOrEmpty(vehicleType) || vehicleType == "cart" ? TravelMode.Push : TravelMode.Drive;

        public static double Distance(double x1, double z1, double x2, double z2)
        {
            var dx = x2 - x1;
            var dz = z2 - z1;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        // Whole game minutes, rounded up. Driving is TaxiSystem.GetTravelDurationMinutes: straight-line
        // metres × 0.04 × game speed; the game's bridge skip uses the same rate. Pushing is metres ÷
        // walking speed in real seconds, and a real second is GameSpeed game minutes (GameManager's
        // MinutesMultiplier) [code, unverified in game]. A cart crossing a bridge walks its length.
        public static int Minutes(FleetRules rules, double metres, TravelMode mode, double gameSpeed = 1.0)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (double.IsNaN(metres) || double.IsInfinity(metres) || metres <= 0) return 0;
            if (double.IsNaN(gameSpeed) || double.IsInfinity(gameSpeed) || gameSpeed <= 0) gameSpeed = 1.0;
            var path = metres * rules.RoadFactor;
            var minutes = mode == TravelMode.Drive
                ? path * rules.DriveMinutesPerMetre * gameSpeed
                : path / rules.PushMetresPerSecond * gameSpeed;
            return (int)Math.Min(int.MaxValue, Math.Ceiling(minutes - 1e-9));
        }
    }

    public enum ScheduleProblem
    {
        None,
        NoStops,
        TooManyStops,
        EmptySpotKey,
        BadPosition,
        // Opening at or after closing, before midnight, or closing after the latest close.
        BadTimes,
        // A stop opens before the one before it closes.
        Overlap
    }

    public enum LegKind
    {
        Travel,
        Open
    }

    public readonly struct PlannedLeg
    {
        internal PlannedLeg(LegKind kind, int stopIndex, long start, long end)
        {
            Kind = kind;
            StopIndex = stopIndex;
            Start = start;
            End = end;
        }

        public LegKind Kind { get; }
        // The stop this leg drives to or opens at.
        public int StopIndex { get; }
        // Absolute game minutes; End is exclusive.
        public long Start { get; }
        public long End { get; }
    }

    public sealed class DayPlan
    {
        internal DayPlan(IReadOnlyList<PlannedLeg> legs, IReadOnlyList<int> skipped, double metres)
        {
            Legs = legs;
            Skipped = skipped;
            Metres = metres;
        }

        public static readonly DayPlan Empty = new DayPlan(Array.Empty<PlannedLeg>(), Array.Empty<int>(), 0);

        public IReadOnlyList<PlannedLeg> Legs { get; }
        // Stops the vehicle can't reach before they close.
        public IReadOnlyList<int> Skipped { get; }
        // Distance covered, for fuel.
        public double Metres { get; }
        public long ShiftStart => Legs.Count == 0 ? 0 : Legs[0].Start;
        public long ShiftEnd => Legs.Count == 0 ? 0 : Legs[Legs.Count - 1].End;
        // The hire is paid for the whole shift, driving included.
        public double ShiftHours => (ShiftEnd - ShiftStart) / 60.0;

        // The leg running at that minute, or null between legs and outside the shift.
        public PlannedLeg? LegAt(long minute)
        {
            foreach (var leg in Legs)
                if (minute >= leg.Start && minute < leg.End) return leg;
            return null;
        }
    }

    // A hire's day: drive (or push) to each stop so as to open on time, sell, close, move on.
    public sealed class HireSchedules
    {
        private readonly FleetRules rules;

        public HireSchedules(FleetRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public static long Absolute(int day, int minute) => (day - 1) * 1440L + minute;

        public ScheduleProblem Validate(HireSchedule schedule)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (schedule.Stops == null || schedule.Stops.Count == 0) return ScheduleProblem.NoStops;
            if (schedule.Stops.Count > rules.MaxStops) return ScheduleProblem.TooManyStops;
            for (var i = 0; i < schedule.Stops.Count; i++)
            {
                var stop = schedule.Stops[i];
                if (stop == null || string.IsNullOrEmpty(stop.SpotKey)) return ScheduleProblem.EmptySpotKey;
                if (double.IsNaN(stop.X) || double.IsInfinity(stop.X) || double.IsNaN(stop.Z) || double.IsInfinity(stop.Z))
                    return ScheduleProblem.BadPosition;
                if (stop.OpenAt < 0 || stop.OpenAt >= 1440 || stop.CloseAt <= stop.OpenAt || stop.CloseAt > rules.LatestCloseMinute)
                    return ScheduleProblem.BadTimes;
                if (i > 0 && stop.OpenAt < schedule.Stops[i - 1].CloseAt) return ScheduleProblem.Overlap;
            }
            return ScheduleProblem.None;
        }

        public bool RunsOn(HireSchedule schedule, DayOfWeek weekday) =>
            schedule.Days == null || schedule.Days.Count == 0 || schedule.Days.Contains(weekday);

        // The legs for one day of the route, starting where the vehicle stands and no earlier than
        // availableFrom. The vehicle leaves as late as it can and still open on time; when it can't,
        // it opens on arrival and closes at the usual time, and a stop it can't reach before closing
        // is skipped. An invalid schedule or a day off gives an empty plan.
        public DayPlan PlanDay(HireSchedule schedule, int day, DayOfWeek weekday, double fromX, double fromZ, long availableFrom,
            TravelMode mode, double gameSpeed = 1.0)
        {
            if (Validate(schedule) != ScheduleProblem.None || !RunsOn(schedule, weekday)) return DayPlan.Empty;
            var legs = new List<PlannedLeg>();
            var skipped = new List<int>();
            var metres = 0.0;
            var x = fromX;
            var z = fromZ;
            var free = availableFrom;
            for (var i = 0; i < schedule.Stops.Count; i++)
            {
                var stop = schedule.Stops[i];
                var distance = Travel.Distance(x, z, stop.X, stop.Z);
                var travel = Travel.Minutes(rules, distance, mode, gameSpeed);
                var open = Absolute(day, stop.OpenAt);
                var close = Absolute(day, stop.CloseAt);
                var depart = Math.Max(free, open - travel);
                var arrive = depart + travel;
                if (arrive >= close)
                {
                    skipped.Add(i);
                    continue;
                }
                if (travel > 0) legs.Add(new PlannedLeg(LegKind.Travel, i, depart, arrive));
                legs.Add(new PlannedLeg(LegKind.Open, i, Math.Max(arrive, open), close));
                metres += distance;
                x = stop.X;
                z = stop.Z;
                free = close;
            }
            return new DayPlan(legs, skipped, metres);
        }

        // The vehicle closes and leaves for a stop: off the map until ArrivesAt. The result lists the
        // customers sent away, for the visible queue; null when the vehicle wasn't open.
        public static SalesResult? Depart(VehicleState vehicle, ScheduleStop stop, PlannedLeg leg)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            if (leg.Kind != LegKind.Travel) throw new ArgumentException("Only a travel leg departs.", nameof(leg));
            var closed = vehicle.Service == null ? null : SalesSimulator.Close(vehicle.Service);
            vehicle.Service = null;
            vehicle.IsOpen = false;
            vehicle.Transit = new TransitState
            {
                ToSpotKey = stop.SpotKey,
                ToX = stop.X,
                ToZ = stop.Z,
                DepartsAt = leg.Start,
                ArrivesAt = leg.End
            };
            return closed;
        }

        // Ends the transit once its time is up and returns where the game side should place the
        // vehicle (the nearest free parking spot to it); null while still on the way or not travelling.
        public static TransitState? ArriveIfDue(VehicleState vehicle, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var transit = vehicle.Transit;
            if (transit == null || now < transit.ArrivesAt) return null;
            vehicle.Transit = null;
            return transit;
        }
    }
}
