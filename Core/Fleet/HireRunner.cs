using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    public enum HireActionKind
    {
        None,
        // Close the window the hire opened: the stop's hours are over.
        Close,
        // Take the vehicle off the map and move it to Stop; it arrives at ArrivesAt.
        Depart,
        // The transit is over: place the vehicle at Transit's spot.
        Arrive,
        // Open the window at Stop, where the vehicle stands.
        Open
    }

    public readonly struct HireAction
    {
        internal HireAction(HireActionKind kind, int stop = -1, long arrivesAt = 0, TransitState? transit = null)
        {
            Kind = kind;
            Stop = stop;
            ArrivesAt = arrivesAt;
            Transit = transit;
        }

        public static readonly HireAction None = new HireAction(HireActionKind.None);

        public HireActionKind Kind { get; }
        public int Stop { get; }
        public long ArrivesAt { get; }
        public TransitState? Transit { get; }
    }

    // Where the vehicle is and what the game side knows about the place, this minute.
    public readonly struct VehicleWhere
    {
        public VehicleWhere(double x, double z, bool leaveAlone, bool canSellHere = true)
        {
            X = x;
            Z = z;
            LeaveAlone = leaveAlone;
            CanSellHere = canSellHere;
        }

        public double X { get; }
        public double Z { get; }
        // The player pushes or drives it, or it stands in a building or garage: the crew leaves it alone.
        public bool LeaveAlone { get; }
        // False where the vehicle may not sell even near a stop (a truck parked illegally): the crew moves it.
        public bool CanSellHere { get; }
    }

    // Runs a hire's route minute by minute. The saved day plan says what the hire should be doing now;
    // Step compares that with the vehicle and gives the one thing to do about it. Calling Step again after
    // the game side has done it gives the next thing, so a load or a time skip catches up on its own.
    public sealed class HireRunner
    {
        private readonly FleetRules rules;
        private readonly HireSchedules schedules;

        public HireRunner(FleetRules rules)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            schedules = new HireSchedules(rules);
        }

        public static int DayOf(long minute) => (int)(Math.Floor(minute / 1440.0) + 1);

        // A crewed vehicle with a valid route gets a plan for the day that contains `now`, starting from where
        // it will be: the last stop of a plan still running past midnight, or where it stands. A plan made in
        // the middle of the day starts now, so stops already over are skipped. Old plans are dropped.
        public void EnsurePlan(VehicleState vehicle, long now, double x, double z, double gameSpeed = 1.0)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            vehicle.Shifts ??= new List<ShiftPlan>();
            var day = DayOf(now);
            vehicle.Shifts.RemoveAll(p => p.Day < day && End(p) <= now);
            var schedule = vehicle.Schedule;
            if (schedule == null || vehicle.Crew == null || vehicle.Crew.Count == 0 || schedules.Validate(schedule) != ScheduleProblem.None)
            {
                vehicle.Shifts.Clear();
                return;
            }
            if (vehicle.Shifts.Any(p => p.Day == day)) return;
            var from = now;
            var running = vehicle.Shifts.Where(p => p.Legs.Count > 0 && End(p) > now).OrderBy(End).LastOrDefault();
            if (running != null)
            {
                var last = schedule.Stops.ElementAtOrDefault(running.Legs[running.Legs.Count - 1].StopIndex);
                if (last != null)
                {
                    x = last.X;
                    z = last.Z;
                }
                from = End(running);
            }
            var plan = schedules.PlanDay(schedule, day, GameDays.Weekday(day), x, z, from, Travel.ModeFor(vehicle.VehicleType), gameSpeed);
            vehicle.Shifts.Add(new ShiftPlan
            {
                Day = day,
                Legs = plan.Legs.Select(l => new ShiftLeg { Kind = l.Kind, StopIndex = l.StopIndex, Start = l.Start, End = l.End }).ToList()
            });
        }

        // The route changed: plan today again from now. Yesterday's plan for a stop still running past
        // midnight stays, so the crew finishes it.
        public static void Replan(VehicleState vehicle, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var day = DayOf(now);
            vehicle.Shifts?.RemoveAll(p => p.Day >= day);
        }

        // The leg the plan has running at that minute, or null when the hire is off.
        public static ShiftLeg? Want(VehicleState vehicle, long now)
        {
            if (vehicle?.Shifts == null) return null;
            foreach (var plan in vehicle.Shifts)
                foreach (var leg in plan.Legs)
                    if (now >= leg.Start && now < leg.End) return leg;
            return null;
        }

        // The next thing to do for a hired vehicle. Rules, in order:
        // a vehicle in transit arrives when its time is up and does nothing else meanwhile; a vehicle the
        // player has, or opened themselves, is left alone; a window the hire opened closes when its stop's
        // hours end; otherwise, during a leg to stop i, a vehicle away from i leaves for it (travel timed
        // from where it really is), and one at i opens once the stop's hours have begun.
        public HireAction Step(VehicleState vehicle, long now, VehicleWhere where, double gameSpeed = 1.0)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.Transit != null)
                return now >= vehicle.Transit.ArrivesAt ? new HireAction(HireActionKind.Arrive, transit: vehicle.Transit) : HireAction.None;
            if (where.LeaveAlone) return HireAction.None;
            var want = Want(vehicle, now);
            if (vehicle.IsOpen)
            {
                if (vehicle.HireStop == null) return HireAction.None;
                return want != null && want.Kind == LegKind.Open && want.StopIndex == vehicle.HireStop
                    ? HireAction.None
                    : new HireAction(HireActionKind.Close);
            }
            if (want == null || vehicle.Crew == null || vehicle.Crew.Count == 0) return HireAction.None;
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(want.StopIndex);
            if (stop == null) return HireAction.None;
            var distance = Travel.Distance(where.X, where.Z, stop.X, stop.Z);
            var atStop = where.CanSellHere && (distance <= rules.StopRadiusMetres || ParkedFor(vehicle, stop, where));
            if (!atStop)
            {
                var minutes = Travel.Minutes(rules, distance, Travel.ModeFor(vehicle.VehicleType), gameSpeed);
                var arrives = now + Math.Max(1, minutes);
                // Moved away by the player too far to make it back before the stop closes: wait for the next one.
                var closes = OpenEnd(vehicle, want.StopIndex, now);
                if (closes != null && arrives >= closes) return HireAction.None;
                return new HireAction(HireActionKind.Depart, want.StopIndex, arrives);
            }
            return want.Kind == LegKind.Open ? new HireAction(HireActionKind.Open, want.StopIndex) : HireAction.None;
        }

        // The crew parked it here for this stop and nobody has moved it since.
        private static bool ParkedFor(VehicleState vehicle, ScheduleStop stop, VehicleWhere where) =>
            vehicle.Arrival != null && vehicle.Arrival.SpotKey == stop.SpotKey &&
            Travel.Distance(where.X, where.Z, vehicle.Arrival.X, vehicle.Arrival.Z) <= 3;

        // When the planned opening at that stop ends, from the plan running now.
        private static long? OpenEnd(VehicleState vehicle, int stopIndex, long now)
        {
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
                foreach (var leg in plan.Legs)
                    if (leg.Kind == LegKind.Open && leg.StopIndex == stopIndex && leg.End > now) return leg.End;
            return null;
        }

        // Puts the vehicle in transit to the stop, from where it stands (x, y, z, heading).
        public static void Depart(VehicleState vehicle, HireAction action, long now, double x, double y, double z, double heading)
        {
            if (action.Kind != HireActionKind.Depart) throw new ArgumentException("Not a departure.", nameof(action));
            var stop = vehicle.Schedule!.Stops[action.Stop];
            vehicle.Service = null;
            vehicle.IsOpen = false;
            vehicle.HireStop = null;
            vehicle.Arrival = null;
            vehicle.Transit = new TransitState
            {
                FromX = x,
                FromY = y,
                FromZ = z,
                FromHeading = heading,
                ToSpotKey = stop.SpotKey,
                ToX = stop.X,
                ToZ = stop.Z,
                ToY = stop.Y,
                ToHeading = stop.Heading,
                DepartsAt = now,
                ArrivesAt = action.ArrivesAt
            };
        }

        // The transit is over and the vehicle stands at (x, z): at its stop, or back where it left from when the
        // stop had no room.
        public static void Arrived(VehicleState vehicle, double x, double z, bool atStop)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var transit = vehicle.Transit;
            vehicle.Transit = null;
            vehicle.Arrival = atStop && transit != null ? new ArrivalState { SpotKey = transit.ToSpotKey, X = x, Z = z } : null;
        }

        // Minutes of [from, to) the crew is on shift: from the first leg of a planned day to its last.
        public static long ShiftMinutes(VehicleState vehicle, long from, long to)
        {
            if (vehicle?.Shifts == null || to <= from) return 0;
            long minutes = 0;
            foreach (var plan in vehicle.Shifts)
            {
                if (plan.Legs.Count == 0) continue;
                var start = Math.Max(from, plan.Legs[0].Start);
                var end = Math.Min(to, End(plan));
                if (end > start) minutes += end - start;
            }
            return Math.Min(minutes, to - from);
        }

        // The hours and weekdays a route asks of its crew each week, for the game's job demands.
        public static (int Hours, List<DayOfWeek> Days) WeeklyWork(HireSchedule? schedule)
        {
            if (schedule?.Stops == null || schedule.Stops.Count == 0) return (0, new List<DayOfWeek>());
            var days = schedule.Days == null || schedule.Days.Count == 0
                ? Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToList()
                : schedule.Days.Distinct().OrderBy(d => d).ToList();
            var first = schedule.Stops.Min(s => s.OpenAt);
            var last = schedule.Stops.Max(s => s.CloseAt);
            var hours = (int)Math.Round(Math.Max(0, last - first) / 60.0 * days.Count);
            return (hours, days);
        }

        private static long End(ShiftPlan plan) => plan.Legs.Count == 0 ? 0 : plan.Legs.Max(l => l.End);
    }

    public static class RouteEditor
    {
        // Adds a stop in time order. The route is unchanged when the result is invalid.
        public static ScheduleProblem AddStop(HireSchedules schedules, HireSchedule schedule, ScheduleStop stop)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            schedule.Stops ??= new List<ScheduleStop>();
            var candidate = new HireSchedule
            {
                Days = schedule.Days,
                Stops = schedule.Stops.Concat(new[] { stop }).OrderBy(s => s.OpenAt).ToList()
            };
            var problem = schedules.Validate(candidate);
            if (problem == ScheduleProblem.None) schedule.Stops = candidate.Stops;
            return problem;
        }
    }
}
