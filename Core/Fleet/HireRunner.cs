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
        Open,
        // Load from the commissary's stock: the vehicle stands in the bay where it rents its space (CrewRestock).
        Restock
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
        public VehicleWhere(double x, double z, bool leaveAlone, bool canSellHere = true, bool hasStock = true, bool stayPut = false,
            bool canRestock = false, string? stockKey = null, IReadOnlyCollection<int>? bayStops = null, (double X, double Z)? home = null)
        {
            Home = home;
            CanRestock = canRestock;
            BayStops = bayStops;
            StockKey = stockKey;
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
        // In the bay of the commissary where it rents its space: the crew restocks there, or learns what it is short of.
        public bool CanRestock { get; }
        // The commissary's stock of the vehicle's foods there (CrewRestock.StockKey); null when not known.
        public string? StockKey { get; }
        // The route's stops that lie in the bay of the commissary where the vehicle rents its space, where the crew
        // would restock. A vehicle with nothing to sell may still leave for one of these, never for another stop. Such a
        // stop counts as reached only when the vehicle is in the bay by the restock test (CanRestock), not within the
        // usual stop radius, so a truck parked 20 m off never takes it for its bay. Null or empty means none.
        public IReadOnlyCollection<int>? BayStops { get; }
        // The bay of the commissary where it rents its space, which the crew drives back to after the last stop
        // (HireSchedules.HomeIndex); null without a space (or while its bay isn't known).
        public (double X, double Z)? Home { get; }
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

        // Why the vehicle's route gets no plan: None when it has no stops or is valid. A route saved under older rules can
        // fail today's (a stop running into the next morning's, say); the game side logs it and BizMan shows it.
        // Unreachable (a stop opening before the drive from the one before can end) comes after Validate's: the route still
        // gets its plan, the stop opening late.
        // Uncovered (part of a route day with nobody of the crew on shift) comes last, and only for a vehicle with crew: one
        // without has the missing-crew warning. `home` and `bayStops` as for EnsurePlan.
        public ScheduleProblem RouteProblem(VehicleState vehicle, double gameSpeed = 1.0, (double X, double Z)? home = null,
            IReadOnlyCollection<int>? bayStops = null)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule;
            if (schedule?.Stops == null || schedule.Stops.Count == 0) return ScheduleProblem.None;
            var problem = schedules.Validate(schedule);
            if (problem != ScheduleProblem.None) return problem;
            var mode = Travel.ModeFor(vehicle.VehicleType);
            if (schedules.UnreachableStop(schedule, mode, gameSpeed) >= 0) return ScheduleProblem.Unreachable;
            if (vehicle.Crew == null || vehicle.Crew.Count == 0) return ScheduleProblem.None;
            return WeekPlan.AnyGap(WeekPlan.RouteDays(schedules, schedule, mode, gameSpeed, home, bayStops), schedule, vehicle.Crew)
                ? ScheduleProblem.Uncovered
                : ScheduleProblem.None;
        }

        // A crewed vehicle with a valid route gets a plan for the day that contains `now`, starting from where
        // it will be: the last stop of a plan still running past midnight, or where it stands. A plan made in
        // the middle of the day starts now, so stops already over are skipped. Old plans are dropped.
        // `home`: the vehicle's own commissary bay, where each day starts with the restock when the vehicle is there and
        // ends (unless the day's last stop is one of `bayStops`, stops already in that bay); a day after one that ended
        // there starts from the bay. The plan follows the crew's shifts (WeekPlan.CoverageOf): it moves and opens only
        // while someone is on shift.
        public void EnsurePlan(VehicleState vehicle, long now, double x, double z, double gameSpeed = 1.0,
            (double X, double Z)? home = null, IReadOnlyCollection<int>? bayStops = null)
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
                var lastIndex = running.Legs[running.Legs.Count - 1].StopIndex;
                var last = lastIndex == HireSchedules.HomeIndex ? null : schedule.Stops.ElementAtOrDefault(lastIndex);
                if (last != null)
                {
                    x = last.X;
                    z = last.Z;
                }
                else if (lastIndex == HireSchedules.HomeIndex && home != null) (x, z) = home.Value;
                from = End(running);
            }
            var origin = HireSchedules.Absolute(day, 0);
            var coverage = WeekPlan.CoverageOf(schedule, vehicle.Crew, origin - 1440, origin + 3 * 1440);
            var plan = schedules.PlanDay(schedule, day, GameDays.Weekday(day), x, z, from, Travel.ModeFor(vehicle.VehicleType), gameSpeed, home,
                bayStops, coverage);
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
                var stop = arrival.StopIndex != null ? vehicle.Schedule?.Stops?.ElementAtOrDefault(arrival.StopIndex.Value)
                    : vehicle.Schedule?.Stops?.FirstOrDefault(s => s.SpotKey == arrival.SpotKey);
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
        // from where it really is), and one at i opens once the stop's hours have begun. A vehicle in its commissary's
        // bay restocks first, once per leg (CrewRestock): before it leaves, or at its stop with nothing to sell. A vehicle
        // with nothing to sell leaves only for a stop in its own commissary's bay (VehicleWhere.BayStops).
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
            if (want.StopIndex == HireSchedules.HomeIndex) return Home(vehicle, now, where, gameSpeed);
            // The morning restock in the bay before it leaves: nothing elsewhere, the drive then leaves from where it is.
            if (want.Kind == LegKind.Restock)
                return !where.StayPut && CrewRestock.Due(vehicle, want, where) ? new HireAction(HireActionKind.Restock, want.StopIndex) : HireAction.None;
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(want.StopIndex);
            if (stop == null) return HireAction.None;
            if (vehicle.Skipped != null && now < vehicle.Skipped.Until &&
                (vehicle.Skipped.StopIndex != null ? vehicle.Skipped.StopIndex == want.StopIndex : vehicle.Skipped.SpotKey == stop.SpotKey))
                return HireAction.None;
            var distance = Travel.Distance(where.X, where.Z, stop.X, stop.Z);
            // Where the crew parked it for this stop counts as the stop even if it reads as no place to sell:
            // moving it would only bring it back there. Opening then says why not, once a day.
            // A stop in its own bay is reached only in the bay (review round 3): elsewhere it couldn't restock there.
            var atStop = where.BayStops?.Contains(want.StopIndex) == true
                ? where.CanRestock
                : ParkedFor(vehicle, want.StopIndex, stop, where) || (where.CanSellHere && distance <= rules.StopRadiusMetres);
            if (!atStop)
            {
                if (where.StayPut) return HireAction.None;
                if (CrewRestock.Due(vehicle, want, where)) return new HireAction(HireActionKind.Restock, want.StopIndex);
                // Sold out: no point driving to a pitch, but a stop in its own bay is where it restocks (review round 2).
                if (!where.HasStock && where.BayStops?.Contains(want.StopIndex) != true) return HireAction.None;
                var minutes = Travel.Minutes(rules, distance, Travel.ModeFor(vehicle.VehicleType), gameSpeed);
                var arrives = now + Math.Max(1, minutes);
                // Moved away by the player too far to make it back before the stop closes: wait for the next one.
                var closes = OpenEnd(vehicle, want.StopIndex, now);
                if (closes != null && arrives >= closes) return HireAction.None;
                return new HireAction(HireActionKind.Depart, want.StopIndex, arrives);
            }
            if (!where.HasStock && !where.StayPut && CrewRestock.Due(vehicle, want, where)) return new HireAction(HireActionKind.Restock, want.StopIndex);
            return want.Kind == LegKind.Open ? new HireAction(HireActionKind.Open, want.StopIndex) : HireAction.None;
        }

        // The drive back to its own bay after the last stop: nothing once it is in the bay (or has no bay to go to), or
        // while the player keeps it, or after a failed try until the hour is out; else it leaves for the bay.
        private HireAction Home(VehicleState vehicle, long now, VehicleWhere where, double gameSpeed)
        {
            if (where.Home == null || where.CanRestock || where.StayPut) return HireAction.None;
            if (vehicle.Skipped != null && now < vehicle.Skipped.Until && vehicle.Skipped.SpotKey == HireSchedules.HomeSpotKey) return HireAction.None;
            var (x, z) = where.Home.Value;
            var minutes = Travel.Between(rules, where.X, where.Z, x, z, Travel.ModeFor(vehicle.VehicleType), gameSpeed);
            return new HireAction(HireActionKind.Depart, HireSchedules.HomeIndex, now + Math.Max(1, minutes));
        }

        // The crew restocked (or tried to) for the leg running now, leaving the commissary's stock of its foods at
        // `stockKeyAfter`: not again in this leg unless that stock changes (CrewRestock.Due).
        public static void Restocked(VehicleState vehicle, long now, string? stockKeyAfter = null)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var want = Want(vehicle, now);
            // A morning restock counts for the drive that follows it (it starts as the restock ends).
            if (want != null) vehicle.RestockedFor = want.Kind == LegKind.Restock ? want.End : want.Start;
            vehicle.RestockStockKey = stockKeyAfter;
        }

        // The crew parked it here for this stop and nobody has moved it since.
        private static bool ParkedFor(VehicleState vehicle, int stopIndex, ScheduleStop stop, VehicleWhere where) =>
            vehicle.Arrival != null &&
            (vehicle.Arrival.StopIndex != null ? vehicle.Arrival.StopIndex == stopIndex : vehicle.Arrival.SpotKey == stop.SpotKey) &&
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
        // A trip back to the bay (action.Stop is HomeIndex) goes to `home`, a place in the bay the game side chose.
        public static void Depart(VehicleState vehicle, HireAction action, long now, double x, double y, double z, double heading,
            ScheduleStop? home = null)
        {
            if (action.Kind != HireActionKind.Depart) throw new ArgumentException("Not a departure.", nameof(action));
            var toHome = action.Stop == HireSchedules.HomeIndex;
            if (toHome && home == null) throw new ArgumentException("A trip back to the bay needs the bay.", nameof(home));
            var stop = toHome ? home! : vehicle.Schedule!.Stops[action.Stop];
            vehicle.Service = null;
            vehicle.IsOpen = false;
            vehicle.HireStop = null;
            vehicle.Arrival = null;
            vehicle.Transit = new TransitState
            {
                ToStopIndex = toHome ? (int?)null : action.Stop,
                ToCloseAt = toHome ? null : OpenEnd(vehicle, action.Stop, now),
                FromX = x,
                FromY = y,
                FromZ = z,
                FromHeading = heading,
                ToSpotKey = toHome ? HireSchedules.HomeSpotKey : stop.SpotKey,
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
        // just ended, so nothing later is given up; one at the opening minute belongs to the new opening.
        public static void GiveUp(VehicleState vehicle, int stopIndex, long minute)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(stopIndex);
            if (stop == null) return;
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
                foreach (var leg in plan.Legs)
                    if (leg.Kind == LegKind.Open && leg.StopIndex == stopIndex && leg.Start <= minute && leg.End > minute)
                    {
                        vehicle.Skipped = new SkipState { SpotKey = stop.SpotKey, StopIndex = stopIndex, Until = leg.End };
                        return;
                    }
        }

        // The transit is over and the vehicle stands at (x, z): at its stop, or back where it left from when the
        // stop had no room. A stop given up on stays given up until its opening ends (or for an hour when the
        // plan has no opening there), so the crew doesn't set off for it again and again.
        public static void Arrived(VehicleState vehicle, double x, double z, bool atStop, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var transit = vehicle.Transit;
            vehicle.Transit = null;
            vehicle.Arrival = atStop && transit != null ? new ArrivalState { SpotKey = transit.ToSpotKey, StopIndex = transit.ToStopIndex, X = x, Z = z } : null;
            if (atStop || transit == null) return;
            // The opening this trip was for, not a later one at the same stop.
            var index = transit.ToStopIndex ?? vehicle.Schedule?.Stops?.FindIndex(s => s.SpotKey == transit.ToSpotKey) ?? -1;
            var until = transit.ToCloseAt ?? (index >= 0 ? OpenEnd(vehicle, index, now) : null);
            if (until != null && until <= now) until = now + 1;
            vehicle.Skipped = new SkipState { SpotKey = transit.ToSpotKey, StopIndex = index >= 0 ? index : (int?)null, Until = until ?? now + 60 };
        }

        private static long End(ShiftPlan plan) => plan.Legs.Count == 0 ? 0 : plan.Legs.Max(l => l.End);
    }

    // A stop the route editor moved later for the drive from the stop before.
    public sealed class RetimedStop
    {
        public RetimedStop(int index, int oldOpen, int oldClose, int newOpen, int newClose, int driveMinutes)
        {
            Index = index;
            OldOpen = oldOpen;
            OldClose = oldClose;
            NewOpen = newOpen;
            NewClose = newClose;
            DriveMinutes = driveMinutes;
        }

        public int Index { get; }
        public int OldOpen { get; }
        public int OldClose { get; }
        public int NewOpen { get; }
        public int NewClose { get; }
        public int DriveMinutes { get; }
    }

    public sealed class TravelFit
    {
        public TravelFit(IReadOnlyList<RetimedStop> moved, int? stuck, ScheduleProblem problem)
        {
            Moved = moved;
            Stuck = stuck;
            Problem = problem;
        }

        public static readonly TravelFit Nothing = new TravelFit(Array.Empty<RetimedStop>(), null, ScheduleProblem.None);

        public IReadOnlyList<RetimedStop> Moved { get; }
        // The first stop that can't be moved late enough (the route is left as it was); null when all fit.
        public int? Stuck { get; }
        public ScheduleProblem Problem { get; }
    }

}
