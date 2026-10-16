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
        public ScheduleProblem RouteProblem(VehicleState vehicle, double gameSpeed = 1.0)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule;
            if (schedule?.Stops == null || schedule.Stops.Count == 0) return ScheduleProblem.None;
            var problem = schedules.Validate(schedule);
            if (problem != ScheduleProblem.None) return problem;
            return schedules.UnreachableStop(schedule, Travel.ModeFor(vehicle.VehicleType), gameSpeed) >= 0 ? ScheduleProblem.Unreachable : ScheduleProblem.None;
        }

        // A crewed vehicle with a valid route gets a plan for the day that contains `now`, starting from where
        // it will be: the last stop of a plan still running past midnight, or where it stands. A plan made in
        // the middle of the day starts now, so stops already over are skipped. Old plans are dropped.
        // `home`: the vehicle's own commissary bay, where each day ends (unless `endsAtHome`: the route's last stop is
        // already there); a day after one that ended there starts from the bay.
        public void EnsurePlan(VehicleState vehicle, long now, double x, double z, double gameSpeed = 1.0,
            (double X, double Z)? home = null, bool endsAtHome = false)
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
            var plan = schedules.PlanDay(schedule, day, GameDays.Weekday(day), x, z, from, Travel.ModeFor(vehicle.VehicleType), gameSpeed, home, endsAtHome);
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
            if (want != null) vehicle.RestockedFor = want.Start;
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

        // Shift minutes as the plans will be once today's exists, without saving anything: the game's payroll
        // can count midnight's hour before the routes have planned the new day.
        public long PlannedShiftMinutes(VehicleState vehicle, long from, long to, double x, double z, double gameSpeed = 1.0,
            (double X, double Z)? home = null, bool endsAtHome = false)
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
            EnsurePlan(preview, from, x, z, gameSpeed, home, endsAtHome);
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
            vehicle.Arrival = atStop && transit != null ? new ArrivalState { SpotKey = transit.ToSpotKey, StopIndex = transit.ToStopIndex, X = x, Z = z } : null;
            if (atStop || transit == null) return;
            // The opening this trip was for, not a later one at the same stop.
            var index = transit.ToStopIndex ?? vehicle.Schedule?.Stops?.FindIndex(s => s.SpotKey == transit.ToSpotKey) ?? -1;
            var until = transit.ToCloseAt ?? (index >= 0 ? OpenEnd(vehicle, index, now) : null);
            if (until != null && until <= now) until = now + 1;
            vehicle.Skipped = new SkipState { SpotKey = transit.ToSpotKey, StopIndex = index >= 0 ? index : (int?)null, Until = until ?? now + 60 };
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

        // New opening hours for one stop (the BizMan page's time pickers). The stops stay in time order, so the stop
        // can move in the list; plans and the open window's stop follow it. The route is unchanged when the result
        // is invalid.
        // New days for the route; refused, and nothing changed, when the stops don't fit them (a night running into a
        // morning that now follows a working day).
        public static ScheduleProblem SetDays(HireSchedules schedules, VehicleState vehicle, IEnumerable<DayOfWeek> days)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (vehicle?.Schedule == null) throw new ArgumentNullException(nameof(vehicle));
            var list = (days ?? Enumerable.Empty<DayOfWeek>()).Distinct().ToList();
            var schedule = vehicle.Schedule;
            if (schedule.Stops != null && schedule.Stops.Count > 0)
            {
                var problem = schedules.Validate(new HireSchedule { Days = list, Stops = schedule.Stops.ToList() });
                if (problem != ScheduleProblem.None) return problem;
            }
            schedule.Days = list;
            return ScheduleProblem.None;
        }

        public static ScheduleProblem SetTimes(HireSchedules schedules, VehicleState vehicle, int index, int open, int close)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule;
            if (schedule?.Stops == null || index < 0 || index >= schedule.Stops.Count) return ScheduleProblem.NoStops;
            var before = schedule.Stops.ToList();
            var stop = before[index];
            var (oldOpen, oldClose) = (stop.OpenAt, stop.CloseAt);
            stop.OpenAt = open;
            stop.CloseAt = close;
            // OrderBy is stable: stops with the same opening keep their order.
            var sorted = before.OrderBy(s => s.OpenAt).ToList();
            var problem = schedules.Validate(new HireSchedule { Days = schedule.Days, Stops = sorted });
            if (problem != ScheduleProblem.None)
            {
                stop.OpenAt = oldOpen;
                stop.CloseAt = oldClose;
                return problem;
            }
            schedule.Stops = sorted;
            Remap(vehicle, i => i >= 0 && i < before.Count ? sorted.IndexOf(before[i]) : -1);
            return problem;
        }

        // Drag to reorder on the BizMan page: the places change order and the opening hours stay in their slots, so
        // the stops stay in time order. A stop is its place: the open window's stop, plans and a trip on its way
        // follow the place to its new slot, and the next replan sorts out what runs when.
        public static ScheduleProblem MovePlace(HireSchedules schedules, VehicleState vehicle, int from, int to)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule;
            if (schedule?.Stops == null || from < 0 || from >= schedule.Stops.Count || to < 0 || to >= schedule.Stops.Count)
                return ScheduleProblem.NoStops;
            if (from == to) return ScheduleProblem.None;
            var before = schedule.Stops.ToList();
            var slots = before.Select(s => (s.OpenAt, s.CloseAt)).ToList();
            var moved = before.ToList();
            var place = moved[from];
            moved.RemoveAt(from);
            moved.Insert(to, place);
            var candidate = new HireSchedule
            {
                Days = schedule.Days,
                Stops = moved.Select((s, i) => Copy(s, slots[i].OpenAt, slots[i].CloseAt)).ToList()
            };
            var problem = schedules.Validate(candidate);
            if (problem != ScheduleProblem.None) return problem;
            for (var i = 0; i < moved.Count; i++)
            {
                moved[i].OpenAt = slots[i].OpenAt;
                moved[i].CloseAt = slots[i].CloseAt;
            }
            schedule.Stops = moved;
            Remap(vehicle, i => i >= 0 && i < before.Count ? moved.IndexOf(before[i]) : -1);
            return problem;
        }

        // Opening times, every `step` minutes of the day, at which a stop of at least `step` minutes fits between the
        // route's other stops (all but `except`): what the time pickers offer. With `travelFrom` (minutes from another stop
        // to this one) a time must also leave the drive from the stop that would come before it: no earlier than that
        // stop's close plus the drive.
        public static List<int> OpenTimes(HireSchedule? schedule, int step, int latestClose, int? except = null,
            Func<ScheduleStop, int>? travelFrom = null)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var others = Others(schedule, except);
            var overnight = HireSchedules.RunsOnConsecutiveDays(schedule);
            var times = new List<int>();
            for (var open = 0; open < 1440; open += step)
            {
                if (open + step > latestClose || !Free(others, open, open + step, overnight)) continue;
                var before = others.Where(s => s.CloseAt <= open).OrderBy(s => s.CloseAt).LastOrDefault();
                if (travelFrom != null && before != null && open < before.CloseAt + travelFrom(before)) continue;
                times.Add(open);
            }
            return times;
        }

        // Minutes of the drive (or push) between two stops, as the crew's plan counts it.
        public static int TravelMinutes(FleetRules rules, ScheduleStop from, ScheduleStop to, TravelMode mode, double gameSpeed = 1.0) =>
            Travel.Between(rules, from.X, from.Z, to.X, to.Z, mode, gameSpeed);

        // After any change to a route (a stop added, moved, retimed or removed): each stop, in order, opens no earlier
        // than the stop before closes plus the drive between them, rounded up to `step`. A stop that opens too early
        // moves to that time and keeps its length (at least `step`), closing no later than `latestClose`. The route is
        // changed only when every stop fits and the result validates; otherwise nothing changes and Stuck names the first
        // stop that doesn't fit (or the whole route, with Problem, when the moved times don't validate).
        public static TravelFit FitTravel(HireSchedules schedules, FleetRules rules, HireSchedule schedule, TravelMode mode, double gameSpeed,
            int step, int latestClose)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (schedule?.Stops == null || schedule.Stops.Count < 2) return TravelFit.Nothing;
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var times = schedule.Stops.Select(s => (Open: s.OpenAt, Close: s.CloseAt)).ToList();
            var moved = new List<RetimedStop>();
            for (var i = 1; i < times.Count; i++)
            {
                var drive = TravelMinutes(rules, schedule.Stops[i - 1], schedule.Stops[i], mode, gameSpeed);
                var ready = times[i - 1].Close + drive;
                if (times[i].Open >= ready) continue;
                var open = (ready + step - 1) / step * step;
                var length = Math.Max(step, times[i].Close - times[i].Open);
                var close = Math.Min(open + length, latestClose);
                if (open >= 1440 || close - open < step) return new TravelFit(Array.Empty<RetimedStop>(), i, ScheduleProblem.Unreachable);
                moved.Add(new RetimedStop(i, times[i].Open, times[i].Close, open, close, drive));
                times[i] = (open, close);
            }
            if (moved.Count == 0) return TravelFit.Nothing;
            var candidate = new HireSchedule
            {
                Days = schedule.Days,
                Stops = schedule.Stops.Select((s, i) => Copy(s, times[i].Open, times[i].Close)).ToList()
            };
            var problem = schedules.Validate(candidate);
            if (problem != ScheduleProblem.None) return new TravelFit(Array.Empty<RetimedStop>(), moved[0].Index, problem);
            for (var i = 0; i < times.Count; i++)
            {
                schedule.Stops[i].OpenAt = times[i].Open;
                schedule.Stops[i].CloseAt = times[i].Close;
            }
            return new TravelFit(moved, null, ScheduleProblem.None);
        }

        // Closing times, every `step` minutes after `open`, up to the next stop's opening or the latest close.
        public static List<int> CloseTimes(HireSchedule? schedule, int open, int step, int latestClose, int? except = null)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var others = Others(schedule, except);
            var overnight = HireSchedules.RunsOnConsecutiveDays(schedule);
            var times = new List<int>();
            // At most a day long, so it never runs into its own opening the next day.
            for (var close = open + step; close <= latestClose && close <= open + 1440; close += step)
            {
                if (!Free(others, open, close, overnight)) break;
                times.Add(close);
            }
            return times;
        }

        // A new opening time for a stop keeps its length where it can: the closing moves with it, cut short at the
        // next stop or the latest close, and never less than `step` after opening. Null when nothing fits.
        public static int? CloseFor(HireSchedule? schedule, int index, int newOpen, int step, int latestClose)
        {
            var stop = schedule?.Stops?.ElementAtOrDefault(index);
            var length = stop == null ? step : Math.Max(step, stop.CloseAt - stop.OpenAt);
            var closes = CloseTimes(schedule, newOpen, step, latestClose, index);
            if (closes.Count == 0) return null;
            var wanted = newOpen + length;
            return closes.Where(c => c <= wanted).DefaultIfEmpty(closes[0]).Max();
        }

        private static List<ScheduleStop> Others(HireSchedule? schedule, int? except) =>
            (schedule?.Stops ?? new List<ScheduleStop>()).Where((s, i) => s != null && i != except).ToList();

        // [open, close) meets none of the other stops' hours, on the same day or, on a route that runs two days in a row,
        // across midnight (a stop running past midnight meets the next day's early stops). Touching ends are fine, as
        // Validate has it.
        private static bool Free(List<ScheduleStop> others, int open, int close, bool overnight) =>
            others.All(s => Apart(open, close, s.OpenAt, s.CloseAt) &&
                            (!overnight || (Apart(open, close, s.OpenAt + 1440, s.CloseAt + 1440) &&
                                            Apart(open, close, s.OpenAt - 1440, s.CloseAt - 1440))));

        private static bool Apart(int open, int close, int otherOpen, int otherClose) => close <= otherOpen || open >= otherClose;

        private static ScheduleStop Copy(ScheduleStop stop, int open, int close) => new ScheduleStop
        {
            SpotKey = stop.SpotKey,
            X = stop.X,
            Y = stop.Y,
            Z = stop.Z,
            Heading = stop.Heading,
            OpenAt = open,
            CloseAt = close
        };

        private static void Remap(VehicleState vehicle, Func<int, int> map)
        {
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
            {
                // The drive back to the bay belongs to no stop and stays.
                plan.Legs.RemoveAll(l => l.StopIndex != HireSchedules.HomeIndex && map(l.StopIndex) < 0);
                foreach (var leg in plan.Legs)
                    if (leg.StopIndex != HireSchedules.HomeIndex) leg.StopIndex = map(leg.StopIndex);
            }
            if (vehicle.HireStop != null)
            {
                var moved = map(vehicle.HireStop.Value);
                vehicle.HireStop = moved < 0 ? (int?)null : moved;
            }
            if (vehicle.Arrival?.StopIndex != null)
            {
                var moved = map(vehicle.Arrival.StopIndex.Value);
                vehicle.Arrival.StopIndex = moved < 0 ? (int?)null : moved;
            }
            if (vehicle.Skipped?.StopIndex != null)
            {
                var moved = map(vehicle.Skipped.StopIndex.Value);
                vehicle.Skipped.StopIndex = moved < 0 ? (int?)null : moved;
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
