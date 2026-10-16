using System;
using System.Collections.Generic;
using System.Linq;
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

        // Minutes from one place to another, as PlanDay, the runner and the route editor all count them.
        public static int Between(FleetRules rules, double fromX, double fromZ, double toX, double toZ, TravelMode mode, double gameSpeed = 1.0) =>
            Minutes(rules, Distance(fromX, fromZ, toX, toZ), mode, gameSpeed);

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
        // Opening at or after closing, before midnight, closing after the latest close, or open longer than a day.
        BadTimes,
        // A stop opens before the one before it closes, or a day's last stop runs past midnight into the next day's first.
        Overlap,
        // A stop opens before the vehicle can get there from the one before (its close plus the drive). Not one of
        // Validate's: such a route still runs, the stop opening late on arrival (PlanDay); the route's to-do and the
        // BizMan page say so (HireRunner.RouteProblem).
        Unreachable,
        // Part of a route day (from the morning restock to the drive back) has nobody on shift (WeekPlan.Gaps). Not one
        // of Validate's: the vehicle waits closed where it is until a shift starts.
        Uncovered
    }

    public enum LegKind
    {
        Travel,
        Open,
        // The morning restock in the commissary bay before the vehicle leaves (FleetRules.RestockMinutes). Its stop is
        // the first stop it leaves for.
        Restock
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
        // Stops the vehicle can't reach before they close (or with nobody on shift while they are open).
        public IReadOnlyList<int> Skipped { get; }
        // Distance covered, for fuel.
        public double Metres { get; }
        public long ShiftStart => Legs.Count == 0 ? 0 : Legs[0].Start;
        public long ShiftEnd => Legs.Count == 0 ? 0 : Legs.Max(l => l.End);
        // From the first leg to the last.
        public double ShiftHours => (ShiftEnd - ShiftStart) / 60.0;

        // The leg running at that minute, or null between legs and outside the shift.
        public PlannedLeg? LegAt(long minute)
        {
            foreach (var leg in Legs)
                if (minute >= leg.Start && minute < leg.End) return leg;
            return null;
        }
    }

    // When at least one crew member is on shift, as merged intervals of absolute game minutes (WeekPlan.Coverage).
    // The vehicle moves and serves only inside them (Peter, 16 October 2026).
    public sealed class Coverage
    {
        private readonly List<(long Start, long End)> spans;

        public Coverage(IEnumerable<(long Start, long End)> intervals)
        {
            spans = new List<(long, long)>();
            foreach (var (start, end) in (intervals ?? Enumerable.Empty<(long, long)>()).Where(i => i.Item2 > i.Item1).OrderBy(i => i.Item1))
            {
                if (spans.Count > 0 && start <= spans[spans.Count - 1].Item2)
                {
                    var last = spans[spans.Count - 1];
                    spans[spans.Count - 1] = (last.Item1, Math.Max(last.Item2, end));
                }
                else spans.Add((start, end));
            }
        }

        public IReadOnlyList<(long Start, long End)> Spans => spans;

        public bool Covers(long minute) => spans.Any(s => minute >= s.Start && minute < s.End);

        // The earliest t >= from with [t, t + length) all covered and t + length <= before; null when there is none.
        // A zero length needs no one on shift.
        public long? Earliest(long from, long length, long before)
        {
            if (length <= 0) return from <= before ? from : (long?)null;
            foreach (var (start, end) in spans)
            {
                var t = Math.Max(from, start);
                if (t + length > before) return null;
                if (t + length <= end) return t;
            }
            return null;
        }

        // The covered pieces of [from, to), in order.
        public IEnumerable<(long Start, long End)> Clip(long from, long to)
        {
            foreach (var (start, end) in spans)
            {
                var a = Math.Max(from, start);
                var b = Math.Min(to, end);
                if (b > a) yield return (a, b);
            }
        }

        // The uncovered pieces of [from, to), in order.
        public IEnumerable<(long Start, long End)> Gaps(long from, long to)
        {
            var at = from;
            foreach (var (a, b) in Clip(from, to))
            {
                if (a > at) yield return (at, a);
                at = b;
            }
            if (at < to) yield return (at, to);
        }
    }

    // A hire's day: drive (or push) to each of the weekday's stops so as to open on time, sell, close, move on.
    public sealed class HireSchedules
    {
        private readonly FleetRules rules;

        public HireSchedules(FleetRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public FleetRules Rules => rules;

        public static long Absolute(int day, int minute) => (day - 1) * 1440L + minute;

        // A leg's stop index for the vehicle's own commissary bay (where it rents its space): the drive back after the
        // last stop (Peter, 16 October 2026). Not a stop of the route.
        public const int HomeIndex = -1;
        // A trip back to the bay names this spot.
        public const string HomeSpotKey = "foodtrucks:commissary-bay";

        // The first stop (from a day's second on) that opens before the vehicle can get there from the stop before, or -1.
        // A day's first stop isn't limited by where the day starts: the crew leaves early enough.
        public int UnreachableStop(HireSchedule schedule, TravelMode mode, double gameSpeed = 1.0)
        {
            if (schedule?.Stops == null) return -1;
            foreach (var day in WeekPlan.Days)
            {
                var stops = WeekPlan.StopsOf(schedule, day);
                for (var k = 1; k < stops.Count; k++)
                {
                    var before = schedule.Stops[stops[k - 1]];
                    var stop = schedule.Stops[stops[k]];
                    if (stop.OpenAt < before.CloseAt + Travel.Between(rules, before.X, before.Z, stop.X, stop.Z, mode, gameSpeed)) return stops[k];
                }
            }
            return -1;
        }

        // Each weekday's stops on their own: valid times in order without overlap, at most MaxStops; a day's last stop
        // running past midnight closes by the time the next day's first opens, when that day runs.
        public ScheduleProblem Validate(HireSchedule schedule)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (schedule.Stops == null || schedule.Stops.Count == 0) return ScheduleProblem.NoStops;
            foreach (var stop in schedule.Stops)
            {
                if (stop == null || string.IsNullOrEmpty(stop.SpotKey)) return ScheduleProblem.EmptySpotKey;
                if (!Enum.IsDefined(typeof(DayOfWeek), stop.Day)) return ScheduleProblem.BadTimes;
                if (double.IsNaN(stop.X) || double.IsInfinity(stop.X) || double.IsNaN(stop.Z) || double.IsInfinity(stop.Z))
                    return ScheduleProblem.BadPosition;
                if (stop.OpenAt < 0 || stop.OpenAt >= 1440 || stop.CloseAt <= stop.OpenAt || stop.CloseAt > rules.LatestCloseMinute)
                    return ScheduleProblem.BadTimes;
                // A day at most: a longer stop would run into its own opening the next day.
                if (stop.CloseAt - stop.OpenAt > 1440) return ScheduleProblem.BadTimes;
            }
            foreach (var day in WeekPlan.Days)
            {
                var stops = WeekPlan.StopsOf(schedule, day);
                if (stops.Count > rules.MaxStops) return ScheduleProblem.TooManyStops;
                for (var k = 1; k < stops.Count; k++)
                    if (schedule.Stops[stops[k]].OpenAt < schedule.Stops[stops[k - 1]].CloseAt) return ScheduleProblem.Overlap;
                if (stops.Count == 0 || !WeekPlan.RunsOn(schedule, day)) continue;
                var next = WeekPlan.Next(day);
                if (!WeekPlan.RunsOn(schedule, next)) continue;
                var last = schedule.Stops[stops[stops.Count - 1]];
                var first = schedule.Stops[WeekPlan.StopsOf(schedule, next)[0]];
                if (last.CloseAt > first.OpenAt + 1440) return ScheduleProblem.Overlap;
            }
            return ScheduleProblem.None;
        }

        public bool RunsOn(HireSchedule schedule, DayOfWeek weekday) => WeekPlan.RunsOn(schedule, weekday);

        // The legs for one day of the route (the weekday's own stops), starting where the vehicle stands and no earlier
        // than availableFrom. The vehicle leaves as late as it can and still open on time; when it can't, it opens on
        // arrival and closes at the usual time, and a stop it can't reach before closing is skipped. An invalid schedule
        // or a day off gives an empty plan.
        // With `home` (the vehicle's own commissary bay): a day that starts in the bay begins with the restock
        // (RestockMinutes, a Restock leg) just before it leaves, and the day ends with a drive back there after the last
        // stop it reached, unless that stop is in the bay already (`bayStops`). With `coverage` (who is on shift), the
        // restock and each drive need someone on shift throughout, so the vehicle waits closed where it is until a shift
        // starts, and it is open only while someone is on shift (an Open leg per covered piece); a stop with no covered
        // piece left is skipped. Null coverage: always someone on shift.
        public DayPlan PlanDay(HireSchedule schedule, int day, DayOfWeek weekday, double fromX, double fromZ, long availableFrom,
            TravelMode mode, double gameSpeed = 1.0, (double X, double Z)? home = null, IReadOnlyCollection<int>? bayStops = null,
            Coverage? coverage = null, Coverage? serving = null)
        {
            // Opening needs someone who serves; a driver alone covers the restock and the drives (review round 3).
            serving ??= coverage;
            if (Validate(schedule) != ScheduleProblem.None || !RunsOn(schedule, weekday)) return DayPlan.Empty;
            var legs = new List<PlannedLeg>();
            var skipped = new List<int>();
            var metres = 0.0;
            var x = fromX;
            var z = fromZ;
            var free = availableFrom;
            var restock = home != null && rules.RestockMinutes > 0 &&
                          Travel.Distance(fromX, fromZ, home.Value.X, home.Value.Z) <= rules.StopRadiusMetres ? rules.RestockMinutes : 0;
            var lastReached = -1;
            foreach (var i in WeekPlan.StopsOf(schedule, weekday))
            {
                var stop = schedule.Stops[i];
                var distance = Travel.Distance(x, z, stop.X, stop.Z);
                var travel = Travel.Minutes(rules, distance, mode, gameSpeed);
                var open = Absolute(day, stop.OpenAt);
                var close = Absolute(day, stop.CloseAt);
                var depart = Math.Max(free + restock, open - travel);
                if (coverage != null)
                {
                    var block = coverage.Earliest(depart - restock, restock + travel, close - 1);
                    if (block == null)
                    {
                        skipped.Add(i);
                        continue;
                    }
                    depart = block.Value + restock;
                }
                var arrive = depart + travel;
                if (arrive >= close)
                {
                    skipped.Add(i);
                    continue;
                }
                var pieces = serving == null
                    ? new List<(long Start, long End)> { (Math.Max(arrive, open), close) }
                    : serving.Clip(Math.Max(arrive, open), close).ToList();
                if (pieces.Count == 0)
                {
                    skipped.Add(i);
                    continue;
                }
                if (restock > 0) legs.Add(new PlannedLeg(LegKind.Restock, i, depart - restock, depart));
                restock = 0;
                if (travel > 0) legs.Add(new PlannedLeg(LegKind.Travel, i, depart, arrive));
                foreach (var (start, end) in pieces) legs.Add(new PlannedLeg(LegKind.Open, i, start, end));
                metres += distance;
                x = stop.X;
                z = stop.Z;
                free = close;
                lastReached = i;
            }
            var endsAtHome = lastReached >= 0 && bayStops != null && bayStops.Contains(lastReached);
            if (home != null && !endsAtHome && lastReached >= 0)
            {
                var back = Travel.Distance(x, z, home.Value.X, home.Value.Z);
                var minutes = Travel.Minutes(rules, back, mode, gameSpeed);
                var leave = minutes <= 0 ? null : coverage == null ? free : coverage.Earliest(free, minutes, free + 1440);
                if (leave != null)
                {
                    legs.Add(new PlannedLeg(LegKind.Travel, HomeIndex, leave.Value, leave.Value + minutes));
                    metres += back;
                }
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
