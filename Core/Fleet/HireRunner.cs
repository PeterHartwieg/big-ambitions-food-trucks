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
        Close,
        Depart,
        Arrive,
        Open,
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

    public readonly struct VehicleWhere
    {
        public VehicleWhere(double x, double z, bool leaveAlone, bool canSellHere = true, bool hasStock = true, bool stayPut = false,
            bool canRestock = false, string? stockKey = null, IReadOnlyCollection<int>? bayStops = null, (double X, double Z)? home = null,
            bool canServe = true)
        {
            CanServe = canServe;
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
        public bool LeaveAlone { get; }
        public bool CanSellHere { get; }
        public bool HasStock { get; }
        public bool StayPut { get; }
        public bool CanRestock { get; }
        public string? StockKey { get; }
        public IReadOnlyCollection<int>? BayStops { get; }
        public (double X, double Z)? Home { get; }

        public VehicleWhere WithStayPut(bool stayPut) =>
            new VehicleWhere(X, Z, LeaveAlone, CanSellHere, HasStock, stayPut, CanRestock, StockKey, BayStops, Home, CanServe);

        public bool CanServe { get; }
    }

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

        public ScheduleProblem RouteProblem(VehicleState vehicle, double gameSpeed = 1.0, (double X, double Z)? home = null,
            IReadOnlyCollection<int>? bayStops = null) =>
            RouteProblems(vehicle, gameSpeed, home, bayStops).DefaultIfEmpty(ScheduleProblem.None).First();

        public List<ScheduleProblem> RouteProblems(VehicleState vehicle, double gameSpeed = 1.0, (double X, double Z)? home = null,
            IReadOnlyCollection<int>? bayStops = null)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var problems = new List<ScheduleProblem>();
            var schedule = vehicle.Schedule;
            if (schedule?.Stops == null || schedule.Stops.Count == 0) return problems;
            var problem = schedules.Validate(schedule);
            if (problem != ScheduleProblem.None)
            {
                problems.Add(problem);
                return problems;
            }
            var mode = Travel.ModeFor(vehicle.VehicleType);
            if (schedules.UnreachableStop(schedule, mode, gameSpeed) >= 0) problems.Add(ScheduleProblem.Unreachable);
            if (vehicle.Crew != null && vehicle.Crew.Count > 0 &&
                WeekPlan.AnyGap(WeekPlan.RouteDays(schedules, schedule, mode, gameSpeed, home, bayStops), schedule, vehicle.Crew))
                problems.Add(ScheduleProblem.Uncovered);
            return problems;
        }

        public RouteCause? RouteCauseOf(VehicleState vehicle, double gameSpeed = 1.0, (double X, double Z)? home = null,
            IReadOnlyCollection<int>? bayStops = null)
        {
            var problem = RouteProblem(vehicle, gameSpeed, home, bayStops);
            if (problem == ScheduleProblem.None) return null;
            var schedule = vehicle.Schedule!;
            var mode = Travel.ModeFor(vehicle.VehicleType);
            if (problem == ScheduleProblem.Unreachable)
            {
                var index = schedules.UnreachableStop(schedule, mode, gameSpeed);
                var stop = schedule.Stops[index];
                return new RouteCause(problem, stop.Day, WeekPlan.StopsOf(schedule, stop.Day).IndexOf(index) + 1);
            }
            if (problem != ScheduleProblem.Uncovered) return new RouteCause(problem);
            var groups = new List<(bool Whole, int Start, int End, bool Before, List<DayOfWeek> Days)>();
            foreach (var routeDay in WeekPlan.RouteDays(schedules, schedule, mode, gameSpeed, home, bayStops))
            {
                var gaps = WeekPlan.Gaps(routeDay, schedule, vehicle.Crew);
                if (gaps.Count == 0) continue;
                var gap = gaps[0];
                var whole = gaps.Count == 1 && gap.Start <= routeDay.Start && gap.End >= routeDay.End;
                var before = gap.Start <= routeDay.Start && (gap.Kind == LegKind.Restock || gap.Kind == LegKind.Travel);
                var key = (whole, whole ? 0 : gap.Start, whole ? 0 : gap.End, !whole && before);
                var group = groups.FindIndex(g => (g.Whole, g.Start, g.End, g.Before) == key);
                if (group < 0) groups.Add((key.Item1, key.Item2, key.Item3, key.Item4, new List<DayOfWeek> { routeDay.Day }));
                else groups[group].Days.Add(routeDay.Day);
            }
            return new RouteCause(problem, uncovered: groups.Select(g => new UncoveredDays(g.Days, g.Whole, g.Start, g.End, g.Before)).ToList());
        }

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
            var origin = HireSchedules.Absolute(day, 0);
            var coverage = WeekPlan.CoverageOf(schedule, vehicle.Crew, origin - 2 * 1440, origin + 4 * 1440);
            var mode = Travel.ModeFor(vehicle.VehicleType);
            bool Add(int d, bool keepEmpty, long? startsBefore)
            {
                if (d < 1 || vehicle.Shifts.Any(p => p.Day == d)) return false;
                var (px, pz, from) = (x, z, now);
                var running = vehicle.Shifts.Where(p => p.Legs.Count > 0 && End(p) > now).OrderBy(End).LastOrDefault();
                if (running != null)
                {
                    var lastIndex = running.Legs.OrderBy(l => l.End).Last().StopIndex;
                    var last = lastIndex == HireSchedules.HomeIndex ? null : schedule.Stops.ElementAtOrDefault(lastIndex);
                    if (last != null) (px, pz) = (last.X, last.Z);
                    else if (lastIndex == HireSchedules.HomeIndex && home != null) (px, pz) = home.Value;
                    from = End(running);
                }
                var plan = schedules.PlanDay(schedule, d, GameDays.Weekday(d), px, pz, from, mode, gameSpeed, home, bayStops, coverage);
                if (!keepEmpty && plan.Legs.Count == 0) return false;
                if (startsBefore != null && (plan.Legs.Count == 0 || plan.Legs[0].Start >= startsBefore)) return false;
                vehicle.Shifts.Add(new ShiftPlan
                {
                    Day = d,
                    Legs = plan.Legs.Select(l => new ShiftLeg { Kind = l.Kind, StopIndex = l.StopIndex, Start = l.Start, End = l.End }).ToList()
                });
                return true;
            }
            if (now < origin + Math.Max(0, rules.LatestCloseMinute - 1440)) Add(day - 1, keepEmpty: false, startsBefore: null);
            Add(day, keepEmpty: true, startsBefore: null);
            if (now >= origin + 720) Add(day + 1, keepEmpty: false, startsBefore: origin + 1440);
        }

        public static void Replan(VehicleState vehicle, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var day = DayOf(now);
            vehicle.Shifts?.RemoveAll(p => p.Day >= day);
            if (vehicle.Transit != null) vehicle.Transit.ToCloseAt = null;
        }

        public static void ReplanShifts(VehicleState vehicle, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            vehicle.Shifts?.Clear();
            if (vehicle.Transit != null) vehicle.Transit.ToCloseAt = null;
        }

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

        public static ShiftLeg? Want(VehicleState vehicle, long now)
        {
            if (vehicle?.Shifts == null) return null;
            foreach (var plan in vehicle.Shifts)
                foreach (var leg in plan.Legs)
                    if (now >= leg.Start && now < leg.End) return leg;
            return null;
        }

        private static bool Covered(VehicleState vehicle, long from, long to)
        {
            var coverage = WeekPlan.CoverageOf(vehicle.Schedule, vehicle.Crew, from - 1440, Math.Max(from, to) + 1440);
            return to <= from ? coverage.Covers(from) : coverage.Earliest(from, to - from, to) == from;
        }

        private static bool NextLegAtAStop(VehicleState vehicle, long now)
        {
            var next = (vehicle.Shifts ?? new List<ShiftPlan>()).SelectMany(p => p.Legs).Where(l => l.End > now)
                .OrderBy(l => l.Start).FirstOrDefault();
            return next != null && next.StopIndex != HireSchedules.HomeIndex && next.Kind != LegKind.Restock;
        }

        public HireAction Step(VehicleState vehicle, long now, VehicleWhere where, double gameSpeed = 1.0)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.Transit != null)
                return now >= vehicle.Transit.ArrivesAt ? new HireAction(HireActionKind.Arrive, transit: vehicle.Transit) : HireAction.None;
            if (where.LeaveAlone) return HireAction.None;
            if (where.CanRestock) vehicle.ReturnPending = false;
            var want = Want(vehicle, now);
            if (vehicle.IsOpen)
            {
                if (vehicle.HireStop == null)
                    return vehicle.CrewWindowLost ? new HireAction(HireActionKind.Close) : HireAction.None;
                if (!where.CanRestock) vehicle.ReturnPending = true;
                if (!where.CanServe) return new HireAction(HireActionKind.Close);
                return want != null && want.StopIndex == vehicle.HireStop && Covered(vehicle, now, now)
                    ? HireAction.None
                    : new HireAction(HireActionKind.Close);
            }
            vehicle.CrewWindowLost = false;
            if (vehicle.Crew == null || vehicle.Crew.Count == 0) return HireAction.None;
            if (want != null && want.StopIndex == HireSchedules.HomeIndex)
            {
                vehicle.ReturnPending = true;
                return Home(vehicle, now, where, gameSpeed);
            }
            if (want == null)
                return vehicle.ReturnPending && !NextLegAtAStop(vehicle, now) ? Home(vehicle, now, where, gameSpeed) : HireAction.None;
            if (want.Kind == LegKind.Restock)
                return !where.StayPut && CrewRestock.Due(vehicle, want, where) ? new HireAction(HireActionKind.Restock, want.StopIndex) : HireAction.None;
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(want.StopIndex);
            if (stop == null) return HireAction.None;
            if (vehicle.Skipped != null && now < vehicle.Skipped.Until &&
                (vehicle.Skipped.StopIndex != null ? vehicle.Skipped.StopIndex == want.StopIndex : vehicle.Skipped.SpotKey == stop.SpotKey))
                return HireAction.None;
            var distance = Travel.Distance(where.X, where.Z, stop.X, stop.Z);
            var atStop = where.BayStops?.Contains(want.StopIndex) == true
                ? where.CanRestock
                : ParkedFor(vehicle, want.StopIndex, stop, where) || (where.CanSellHere && distance <= rules.StopRadiusMetres);
            if (!atStop)
            {
                if (where.StayPut) return HireAction.None;
                if (CrewRestock.Due(vehicle, want, where)) return new HireAction(HireActionKind.Restock, want.StopIndex);
                // Sold out: no point driving to a pitch, but a stop in its own bay is where it restocks (review round 2).
                // With a bay to go back to, it drives there now, while someone is on shift: the plan's drive back is
                // timed from the stop it won't reach, and from where it stands it may not fit the shift any more, which
                // left a crewed truck away from its bay (and fined at 04:00) until its next shift (soak seed 3).
                if (!where.HasStock && where.BayStops?.Contains(want.StopIndex) != true) return Home(vehicle, now, where, gameSpeed);
                var minutes = Travel.Minutes(rules, distance, Travel.ModeFor(vehicle.VehicleType), gameSpeed);
                var arrives = now + Math.Max(1, minutes);
                var closes = OpenEnd(vehicle, want.StopIndex, now);
                if (closes != null && arrives >= closes) return HireAction.None;
                if (!Covered(vehicle, now, arrives)) return HireAction.None;
                return new HireAction(HireActionKind.Depart, want.StopIndex, arrives);
            }
            if (!where.HasStock && !where.StayPut && CrewRestock.Due(vehicle, want, where)) return new HireAction(HireActionKind.Restock, want.StopIndex);
            if (want.Kind != LegKind.Open || !Covered(vehicle, now, now) || !where.CanServe) return HireAction.None;
            if (!where.CanRestock) vehicle.ReturnPending = true;
            return new HireAction(HireActionKind.Open, want.StopIndex);
        }

        public bool WouldMove(VehicleState vehicle, long now, VehicleWhere where, double gameSpeed = 1.0)
        {
            var kind = Step(vehicle, now, where, gameSpeed).Kind;
            return kind == HireActionKind.Depart || kind == HireActionKind.Restock;
        }

        private HireAction Home(VehicleState vehicle, long now, VehicleWhere where, double gameSpeed)
        {
            if (where.Home == null || where.CanRestock || where.StayPut) return HireAction.None;
            if (vehicle.Skipped != null && now < vehicle.Skipped.Until && vehicle.Skipped.SpotKey == HireSchedules.HomeSpotKey) return HireAction.None;
            var (x, z) = where.Home.Value;
            var minutes = Travel.Between(rules, where.X, where.Z, x, z, Travel.ModeFor(vehicle.VehicleType), gameSpeed);
            var arrives = now + Math.Max(1, minutes);
            if (!Covered(vehicle, now, arrives)) return HireAction.None;
            return new HireAction(HireActionKind.Depart, HireSchedules.HomeIndex, arrives);
        }

        public static void Restocked(VehicleState vehicle, long now, string? stockKeyAfter = null)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var want = Want(vehicle, now);
            if (want != null) vehicle.RestockedFor = want.Kind == LegKind.Restock ? want.End : want.Start;
            vehicle.RestockStockKey = stockKeyAfter;
        }

        private static bool ParkedFor(VehicleState vehicle, int stopIndex, ScheduleStop stop, VehicleWhere where) =>
            vehicle.Arrival != null &&
            (vehicle.Arrival.StopIndex != null ? vehicle.Arrival.StopIndex == stopIndex : vehicle.Arrival.SpotKey == stop.SpotKey) &&
            Travel.Distance(where.X, where.Z, vehicle.Arrival.X, vehicle.Arrival.Z) <= 3;

        private static long? OpenEnd(VehicleState vehicle, int stopIndex, long now)
        {
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
                foreach (var leg in plan.Legs)
                    if (leg.Kind == LegKind.Open && leg.StopIndex == stopIndex && leg.End > now) return leg.End;
            return null;
        }

        public static void Depart(VehicleState vehicle, HireAction action, long now, double x, double y, double z, double heading,
            ScheduleStop? home = null)
        {
            if (action.Kind != HireActionKind.Depart) throw new ArgumentException("Not a departure.", nameof(action));
            var toHome = action.Stop == HireSchedules.HomeIndex;
            if (toHome && home == null) throw new ArgumentException("A trip back to the bay needs the bay.", nameof(home));
            vehicle.ReturnPending = true;
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

        public static void Arrived(VehicleState vehicle, double x, double z, bool atStop, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var transit = vehicle.Transit;
            vehicle.Transit = null;
            vehicle.Arrival = atStop && transit != null ? new ArrivalState { SpotKey = transit.ToSpotKey, StopIndex = transit.ToStopIndex, X = x, Z = z } : null;
            if (atStop && transit?.ToSpotKey == HireSchedules.HomeSpotKey) vehicle.ReturnPending = false;
            if (atStop || transit == null) return;
            var index = transit.ToStopIndex ?? vehicle.Schedule?.Stops?.FindIndex(s => s.SpotKey == transit.ToSpotKey) ?? -1;
            var until = transit.ToCloseAt ?? (index >= 0 ? OpenEnd(vehicle, index, now) : null);
            if (until != null && until <= now) until = now + 1;
            vehicle.Skipped = new SkipState { SpotKey = transit.ToSpotKey, StopIndex = index >= 0 ? index : (int?)null, Until = until ?? now + 60 };
        }

        private static long End(ShiftPlan plan) => plan.Legs.Count == 0 ? 0 : plan.Legs.Max(l => l.End);
    }

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
        public int? Stuck { get; }
        public ScheduleProblem Problem { get; }
    }

}
