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
        public VehicleWhere(double x, double z, bool leaveAlone, bool canSellHere = true, bool hasStock = true, bool stayPut = false)
        {
            X = x;
            Z = z;
            LeaveAlone = leaveAlone;
            CanSellHere = canSellHere;
            HasStock = hasStock;
            StayPut = stayPut;
        }

        public double X { get; }
        public double Z { get; }
        // The player pushes or drives it, or it stands in a building or garage: the crew leaves it alone.
        public bool LeaveAlone { get; }
        // False where the vehicle may not sell even near a stop (a truck parked illegally): the crew moves it.
        public bool CanSellHere { get; }
        // False when there is nothing on board the crew could sell: no point driving to a stop.
        public bool HasStock { get; }
        // The player is right by it or used it a moment ago (stepped out to buy stock, say): the crew opens and
        // closes as usual but doesn't drive it away.
        public bool StayPut { get; }
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
            if (vehicle.Skipped != null && vehicle.Skipped.Until <= now) vehicle.Skipped = null;
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
            // The stop's hours may have changed: a trip on its way finds its closing time from the new plan.
            if (vehicle.Transit != null) vehicle.Transit.ToCloseAt = null;
        }

        // Where to plan a vehicle's day from: where it is going while on its way; the stop itself while it
        // stands where the crew parked it for that stop (so a replan has no few-metre trip in it); else where it is.
        public static (double X, double Z) PlanFrom(VehicleState vehicle, double x, double z)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.Transit != null) return (vehicle.Transit.ToX, vehicle.Transit.ToZ);
            var arrival = vehicle.Arrival;
            if (arrival != null && Travel.Distance(x, z, arrival.X, arrival.Z) <= 3)
            {
                var stop = vehicle.Schedule?.Stops?.FirstOrDefault(s => s.SpotKey == arrival.SpotKey);
                if (stop != null) return (stop.X, stop.Z);
            }
            return (x, z);
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
                // Still the stop it is open at, even if a fresh plan has a short leg "to" it first.
                return want != null && want.StopIndex == vehicle.HireStop
                    ? HireAction.None
                    : new HireAction(HireActionKind.Close);
            }
            if (want == null || vehicle.Crew == null || vehicle.Crew.Count == 0) return HireAction.None;
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(want.StopIndex);
            if (stop == null) return HireAction.None;
            if (vehicle.Skipped != null && vehicle.Skipped.SpotKey == stop.SpotKey && now < vehicle.Skipped.Until) return HireAction.None;
            var distance = Travel.Distance(where.X, where.Z, stop.X, stop.Z);
            // Where the crew parked it for this stop counts as the stop even if it reads as no place to sell:
            // moving it would only bring it back there. Opening then says why not, once a day.
            var atStop = ParkedFor(vehicle, stop, where) || (where.CanSellHere && distance <= rules.StopRadiusMetres);
            if (!atStop)
            {
                if (where.StayPut || !where.HasStock) return HireAction.None;
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
                ToStopIndex = action.Stop,
                ToCloseAt = OpenEnd(vehicle, action.Stop, now),
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

        // The crew stops trying the opening running at that minute until it ends: a window that closed on an
        // error would otherwise open again at once. An error right at closing time belongs to the opening that
        // just ended, so nothing later is given up.
        public static void GiveUp(VehicleState vehicle, int stopIndex, long minute)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(stopIndex);
            if (stop == null) return;
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
                foreach (var leg in plan.Legs)
                    if (leg.Kind == LegKind.Open && leg.StopIndex == stopIndex && leg.Start < minute && leg.End > minute)
                    {
                        vehicle.Skipped = new SkipState { SpotKey = stop.SpotKey, Until = leg.End };
                        return;
                    }
        }

        // Shift minutes as the plans will be once today's exists, without saving anything: the game's payroll
        // can count midnight's hour before the routes have planned the new day.
        public long PlannedShiftMinutes(VehicleState vehicle, long from, long to, double x, double z, double gameSpeed = 1.0)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var preview = new VehicleState
            {
                VehicleId = vehicle.VehicleId,
                VehicleType = vehicle.VehicleType,
                Crew = vehicle.Crew,
                Schedule = vehicle.Schedule,
                Shifts = (vehicle.Shifts ?? new List<ShiftPlan>()).ToList()
            };
            // EnsurePlan drops only plans that ended by `from`, which count nothing here anyway.
            EnsurePlan(preview, from, x, z, gameSpeed);
            return ShiftMinutes(preview, from, to);
        }

        // The transit is over and the vehicle stands at (x, z): at its stop, or back where it left from when the
        // stop had no room. A stop given up on stays given up until its opening ends (or for an hour when the
        // plan has no opening there), so the crew doesn't set off for it again and again.
        public static void Arrived(VehicleState vehicle, double x, double z, bool atStop, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var transit = vehicle.Transit;
            vehicle.Transit = null;
            vehicle.Arrival = atStop && transit != null ? new ArrivalState { SpotKey = transit.ToSpotKey, X = x, Z = z } : null;
            if (atStop || transit == null) return;
            // The opening this trip was for, not a later one at the same stop.
            var index = transit.ToStopIndex ?? vehicle.Schedule?.Stops?.FindIndex(s => s.SpotKey == transit.ToSpotKey) ?? -1;
            var until = transit.ToCloseAt ?? (index >= 0 ? OpenEnd(vehicle, index, now) : null);
            if (until != null && until <= now) until = now + 1;
            vehicle.Skipped = new SkipState { SpotKey = transit.ToSpotKey, Until = until ?? now + 60 };
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

        // The hours and weekdays a route asks of its crew each week, for the game's job demands, counted the way
        // the payroll counts them: an hour the shift covers at least half of is a worked hour, and a late stop's
        // hours after midnight fall on the next weekday. The shift runs from the first opening to the last
        // closing; driving to the first stop isn't known ahead, so it isn't in it.
        public static (int Hours, List<DayOfWeek> Days) WeeklyWork(HireSchedule? schedule)
        {
            if (schedule?.Stops == null || schedule.Stops.Count == 0) return (0, new List<DayOfWeek>());
            var routeDays = schedule.Days == null || schedule.Days.Count == 0
                ? Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToList()
                : schedule.Days.Distinct().ToList();
            var first = schedule.Stops.Min(s => s.OpenAt);
            var last = schedule.Stops.Max(s => s.CloseAt);
            var hours = new List<int>();
            for (var hour = 0; hour < 48; hour++)
                if (Math.Min(last, hour * 60 + 60) - Math.Max(first, hour * 60) >= 30) hours.Add(hour);
            // Hours of the week, each counted once: a late stop's night can meet the next day's early start.
            var week = new HashSet<int>();
            foreach (var day in routeDays)
                foreach (var hour in hours) week.Add(((int)day * 24 + hour) % (7 * 24));
            var days = week.Select(h => (DayOfWeek)(h / 24)).Distinct().OrderBy(d => d).ToList();
            return (week.Count, days);
        }

        private static long End(ShiftPlan plan) => plan.Legs.Count == 0 ? 0 : plan.Legs.Max(l => l.End);
    }

    public static class RouteEditor
    {
        // Adds a stop to the vehicle's route in time order. Plans kept from yesterday and the open window's stop
        // follow the stops' new places.
        public static ScheduleProblem AddStop(HireSchedules schedules, VehicleState vehicle, ScheduleStop stop)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule ?? new HireSchedule();
            var before = schedule.Stops?.ToList() ?? new List<ScheduleStop>();
            var problem = AddStop(schedules, schedule, stop);
            if (problem != ScheduleProblem.None) return problem;
            vehicle.Schedule = schedule;
            Remap(vehicle, i => i >= 0 && i < before.Count ? schedule.Stops.IndexOf(before[i]) : -1);
            return problem;
        }

        // Removes a stop. A window the crew opened there must be closed first; legs for it are dropped.
        public static void RemoveStop(VehicleState vehicle, int index)
        {
            if (vehicle?.Schedule?.Stops == null || index < 0 || index >= vehicle.Schedule.Stops.Count) return;
            vehicle.Schedule.Stops.RemoveAt(index);
            var count = vehicle.Schedule.Stops.Count + 1;
            Remap(vehicle, i => i == index || i < 0 || i >= count ? -1 : i > index ? i - 1 : i);
            if (vehicle.Schedule.Stops.Count == 0) vehicle.Schedule = null;
        }

        private static void Remap(VehicleState vehicle, Func<int, int> map)
        {
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
            {
                plan.Legs.RemoveAll(l => map(l.StopIndex) < 0);
                foreach (var leg in plan.Legs) leg.StopIndex = map(leg.StopIndex);
            }
            if (vehicle.HireStop != null)
            {
                var moved = map(vehicle.HireStop.Value);
                vehicle.HireStop = moved < 0 ? (int?)null : moved;
            }
            // A removed destination falls back to finding the stop by its spot.
            if (vehicle.Transit?.ToStopIndex != null)
            {
                var moved = map(vehicle.Transit.ToStopIndex.Value);
                vehicle.Transit.ToStopIndex = moved < 0 ? (int?)null : moved;
            }
        }

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
