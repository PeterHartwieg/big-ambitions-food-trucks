using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    // A weekday's route and shifts, copied to paste on another day (the day tile's copy and paste, as the game's schedule
    // screen copies a whole day).
    public sealed class DayCopy
    {
        internal DayCopy(DayOfWeek from, bool closed, IReadOnlyList<ScheduleStop> stops, IReadOnlyList<CrewShift> shifts)
        {
            From = from;
            Closed = closed;
            Stops = stops;
            Shifts = shifts;
        }

        public DayOfWeek From { get; }
        public bool Closed { get; }
        public IReadOnlyList<ScheduleStop> Stops { get; }
        public IReadOnlyList<CrewShift> Shifts { get; }
    }

    // Changes to a vehicle's week plan from the BizMan page. Every change keeps the stops in their order (WeekPlan.Ordered)
    // and validates the result; a refused change leaves everything as it was. Plans kept from yesterday, the open window's
    // stop, an arrival, a stop given up on and a trip on its way follow each stop to its new index.
    public static class RouteEditor
    {
        // Adds a stop to its weekday (stop.Day) in time order.
        public static ScheduleProblem AddStop(HireSchedules schedules, VehicleState vehicle, ScheduleStop stop)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule ?? new HireSchedule();
            var before = schedule.Stops?.ToList() ?? new List<ScheduleStop>();
            var problem = AddStop(schedules, schedule, stop);
            if (problem != ScheduleProblem.None) return problem;
            vehicle.Schedule = schedule;
            Remap(vehicle, before, schedule.Stops ?? new List<ScheduleStop>());
            return problem;
        }

        // Adds a stop in time order to its weekday. The route is unchanged when the result is invalid.
        public static ScheduleProblem AddStop(HireSchedules schedules, HireSchedule schedule, ScheduleStop stop)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            schedule.Stops ??= new List<ScheduleStop>();
            var candidate = Like(schedule, WeekPlan.Ordered(schedule.Stops.Concat(new[] { stop })));
            var problem = schedules.Validate(candidate);
            if (problem == ScheduleProblem.None) schedule.Stops = candidate.Stops;
            return problem;
        }

        // Removes a stop. A window the crew opened there must be closed first; legs for it are dropped. The week plan
        // stays while it has shifts or closed days.
        public static void RemoveStop(VehicleState vehicle, int index)
        {
            if (vehicle?.Schedule?.Stops == null || index < 0 || index >= vehicle.Schedule.Stops.Count) return;
            var before = vehicle.Schedule.Stops.ToList();
            vehicle.Schedule.Stops.RemoveAt(index);
            Remap(vehicle, before, vehicle.Schedule.Stops);
            DropIfEmpty(vehicle);
        }

        // The day tile's tick box: a closed day keeps its stops but doesn't run. Opening a day can be refused (its night
        // would run into the next day's morning, or the day before's into its own).
        public static ScheduleProblem SetClosed(HireSchedules schedules, VehicleState vehicle, DayOfWeek day, bool closed)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule ??= new HireSchedule();
            schedule.Closed ??= new List<DayOfWeek>();
            var list = schedule.Closed.Where(d => d != day).ToList();
            if (closed) list.Add(day);
            if (!closed && schedule.Stops != null && schedule.Stops.Count > 0)
            {
                var candidate = Like(schedule, schedule.Stops.ToList());
                candidate.Closed = list;
                var problem = schedules.Validate(candidate);
                if (problem != ScheduleProblem.None) return problem;
            }
            schedule.Closed = list.OrderBy(WeekPlan.Position).ToList();
            DropIfEmpty(vehicle);
            return ScheduleProblem.None;
        }

        // New opening hours for one stop (the BizMan page's time pickers). The day's stops stay in time order, so the
        // stop can move in the list. The route is unchanged when the result is invalid.
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
            var sorted = WeekPlan.Ordered(before);
            var problem = schedules.Validate(Like(schedule, sorted));
            if (problem != ScheduleProblem.None)
            {
                stop.OpenAt = oldOpen;
                stop.CloseAt = oldClose;
                return problem;
            }
            schedule.Stops = sorted;
            Remap(vehicle, before, sorted);
            return problem;
        }

        // Drag to reorder on the BizMan page, within one weekday: the places change order and the opening hours stay in
        // their slots, so the day's stops stay in time order. A stop is its place: the open window's stop, plans and a
        // trip on its way follow the place to its new slot, and the next replan sorts out what runs when.
        public static ScheduleProblem MovePlace(HireSchedules schedules, VehicleState vehicle, int from, int to)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var schedule = vehicle.Schedule;
            if (schedule?.Stops == null || from < 0 || from >= schedule.Stops.Count || to < 0 || to >= schedule.Stops.Count)
                return ScheduleProblem.NoStops;
            if (from == to) return ScheduleProblem.None;
            var day = schedule.Stops[from].Day;
            if (schedule.Stops[to].Day != day) return ScheduleProblem.Overlap;
            var before = schedule.Stops.ToList();
            var slots = WeekPlan.StopsOf(schedule, day);
            var places = slots.Select(i => before[i]).ToList();
            var times = places.Select(s => (s.OpenAt, s.CloseAt)).ToList();
            var place = before[from];
            places.Remove(place);
            places.Insert(slots.IndexOf(to), place);
            var candidate = before.ToList();
            for (var k = 0; k < slots.Count; k++) candidate[slots[k]] = WeekPlan.CopyStop(places[k], day, times[k].OpenAt, times[k].CloseAt);
            var problem = schedules.Validate(Like(schedule, candidate));
            if (problem != ScheduleProblem.None) return problem;
            var moved = before.ToList();
            for (var k = 0; k < slots.Count; k++)
            {
                places[k].OpenAt = times[k].OpenAt;
                places[k].CloseAt = times[k].CloseAt;
                moved[slots[k]] = places[k];
            }
            schedule.Stops = moved;
            Remap(vehicle, before, moved);
            return problem;
        }

        // Opening times on `day`, every `step` minutes, at which a stop of at least `step` minutes fits between that day's
        // other stops (all but `except`) and the neighbouring days' nights and mornings: what the time pickers offer. With
        // `travelFrom` (minutes from another stop to this one) a time must also leave the drive from the day's stop that
        // would come before it: no earlier than that stop's close plus the drive.
        public static List<int> OpenTimes(HireSchedule? schedule, DayOfWeek day, int step, int latestClose, int? except = null,
            Func<ScheduleStop, int>? travelFrom = null)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var others = Others(schedule, day, except);
            var busy = Busy(schedule, day, except);
            var times = new List<int>();
            for (var open = 0; open < 1440; open += step)
            {
                if (open + step > latestClose || !Free(busy, open, open + step)) continue;
                var before = others.Where(s => s.CloseAt <= open).OrderBy(s => s.CloseAt).LastOrDefault();
                if (travelFrom != null && before != null && open < before.CloseAt + travelFrom(before)) continue;
                times.Add(open);
            }
            return times;
        }

        // Closing times on `day`, every `step` minutes after `open`, up to the next stop's opening or the latest close.
        public static List<int> CloseTimes(HireSchedule? schedule, DayOfWeek day, int open, int step, int latestClose, int? except = null)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var busy = Busy(schedule, day, except);
            var times = new List<int>();
            // At most a day long, so it never runs into its own opening the next day.
            for (var close = open + step; close <= latestClose && close <= open + 1440; close += step)
            {
                if (!Free(busy, open, close)) break;
                times.Add(close);
            }
            return times;
        }

        // A new opening time for a stop keeps its length where it can: the closing moves with it, cut short at the
        // next stop or the latest close, and never less than `step` after opening. Null when nothing fits.
        public static int? CloseFor(HireSchedule? schedule, int index, int newOpen, int step, int latestClose)
        {
            var stop = schedule?.Stops?.ElementAtOrDefault(index);
            if (stop == null) return null;
            var length = Math.Max(step, stop.CloseAt - stop.OpenAt);
            var closes = CloseTimes(schedule, stop.Day, newOpen, step, latestClose, index);
            if (closes.Count == 0) return null;
            var wanted = newOpen + length;
            return closes.Where(c => c <= wanted).DefaultIfEmpty(closes[0]).Max();
        }

        // Minutes of the drive (or push) between two stops, as the crew's plan counts it.
        public static int TravelMinutes(FleetRules rules, ScheduleStop from, ScheduleStop to, TravelMode mode, double gameSpeed = 1.0) =>
            Travel.Between(rules, from.X, from.Z, to.X, to.Z, mode, gameSpeed);

        // After any change to a route (a stop added, moved, retimed or removed): on each weekday, each stop opens no
        // earlier than the stop before closes plus the drive between them, rounded up to `step`. A stop that opens too
        // early moves to that time and keeps its length (at least `step`), closing no later than `latestClose`. The route
        // is changed only when every stop fits and the result validates; otherwise nothing changes and Stuck names the
        // first stop that doesn't fit (or the whole route, with Problem, when the moved times don't validate).
        public static TravelFit FitTravel(HireSchedules schedules, FleetRules rules, HireSchedule schedule, TravelMode mode, double gameSpeed,
            int step, int latestClose)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (schedule?.Stops == null || schedule.Stops.Count < 2) return TravelFit.Nothing;
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            var times = schedule.Stops.Select(s => (Open: s.OpenAt, Close: s.CloseAt)).ToList();
            var moved = new List<RetimedStop>();
            foreach (var day in WeekPlan.Days)
            {
                var stops = WeekPlan.StopsOf(schedule, day);
                for (var k = 1; k < stops.Count; k++)
                {
                    var (p, i) = (stops[k - 1], stops[k]);
                    var drive = TravelMinutes(rules, schedule.Stops[p], schedule.Stops[i], mode, gameSpeed);
                    var ready = times[p].Close + drive;
                    if (times[i].Open >= ready) continue;
                    var open = (ready + step - 1) / step * step;
                    var length = Math.Max(step, times[i].Close - times[i].Open);
                    var close = Math.Min(open + length, latestClose);
                    if (open >= 1440 || close - open < step) return new TravelFit(Array.Empty<RetimedStop>(), i, ScheduleProblem.Unreachable);
                    moved.Add(new RetimedStop(i, times[i].Open, times[i].Close, open, close, drive));
                    times[i] = (open, close);
                }
            }
            if (moved.Count == 0) return TravelFit.Nothing;
            var candidate = Like(schedule, schedule.Stops.Select((s, i) => WeekPlan.CopyStop(s, s.Day, times[i].Open, times[i].Close)).ToList());
            var problem = schedules.Validate(candidate);
            if (problem != ScheduleProblem.None) return new TravelFit(Array.Empty<RetimedStop>(), moved[0].Index, problem);
            for (var i = 0; i < times.Count; i++)
            {
                schedule.Stops[i].OpenAt = times[i].Open;
                schedule.Stops[i].CloseAt = times[i].Close;
            }
            return new TravelFit(moved, null, ScheduleProblem.None);
        }

        // ---- The day tile's copy, paste and clear ----

        public static DayCopy CopyDay(HireSchedule? schedule, DayOfWeek day) => new DayCopy(day, WeekPlan.IsClosed(schedule, day),
            WeekPlan.StopsOf(schedule, day).Select(i => WeekPlan.CopyStop(schedule!.Stops[i], day)).ToList(),
            (schedule?.CrewShifts ?? new List<CrewShift>()).Where(s => s != null && s.Day == day)
                .Select(s => new CrewShift { EmployeeId = s.EmployeeId, Day = day, Start = s.Start, End = s.End }).ToList());

        // The copied day replaces `day`'s stops, shifts (of crew still on the vehicle) and closed mark. Refused, with
        // nothing changed, when the week wouldn't validate (a night running into the next morning).
        public static ScheduleProblem PasteDay(HireSchedules schedules, VehicleState vehicle, DayCopy copy, DayOfWeek day) =>
            PasteDays(schedules, vehicle, copy, new[] { day });

        // The copied day on every other weekday ("Paste to all days").
        public static ScheduleProblem PasteToAllDays(HireSchedules schedules, VehicleState vehicle, DayCopy copy) =>
            PasteDays(schedules, vehicle, copy, WeekPlan.Days.Where(d => d != copy.From).ToList());

        private static ScheduleProblem PasteDays(HireSchedules schedules, VehicleState vehicle, DayCopy copy, IReadOnlyCollection<DayOfWeek> days)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            var schedule = vehicle.Schedule ?? new HireSchedule();
            var before = schedule.Stops?.ToList() ?? new List<ScheduleStop>();
            var stops = WeekPlan.Ordered(before.Where(s => !days.Contains(s.Day))
                .Concat(days.SelectMany(d => copy.Stops.Select(s => WeekPlan.CopyStop(s, d)))));
            var closed = (schedule.Closed ?? new List<DayOfWeek>()).Where(d => !days.Contains(d)).ToList();
            if (copy.Closed) closed.AddRange(days);
            var candidate = Like(schedule, stops);
            candidate.Closed = closed;
            if (stops.Count > 0)
            {
                var problem = schedules.Validate(candidate);
                if (problem != ScheduleProblem.None) return problem;
            }
            var crew = new HashSet<string>(vehicle.Crew ?? new List<string>(), StringComparer.Ordinal);
            schedule.Stops = stops;
            schedule.Closed = closed.Distinct().OrderBy(WeekPlan.Position).ToList();
            schedule.CrewShifts = (schedule.CrewShifts ?? new List<CrewShift>()).Where(s => s != null && !days.Contains(s.Day))
                .Concat(days.SelectMany(d => copy.Shifts.Where(s => crew.Contains(s.EmployeeId))
                    .Select(s => new CrewShift { EmployeeId = s.EmployeeId, Day = d, Start = s.Start, End = s.End })))
                .ToList();
            WeekPlan.SortShifts(schedule);
            vehicle.Schedule = schedule;
            Remap(vehicle, before, stops);
            DropIfEmpty(vehicle);
            return ScheduleProblem.None;
        }

        // The day's stops and shifts go; the day is then simply without a route.
        public static void ClearDay(VehicleState vehicle, DayOfWeek day)
        {
            var schedule = vehicle?.Schedule;
            if (schedule == null) return;
            var before = schedule.Stops?.ToList() ?? new List<ScheduleStop>();
            schedule.Stops = before.Where(s => s.Day != day).ToList();
            schedule.CrewShifts?.RemoveAll(s => s == null || s.Day == day);
            schedule.Closed?.RemoveAll(d => d == day);
            Remap(vehicle!, before, schedule.Stops);
            DropIfEmpty(vehicle!);
        }

        // ---- Helpers ----

        private static HireSchedule Like(HireSchedule schedule, List<ScheduleStop> stops) => new HireSchedule
        {
            Stops = stops,
            Closed = schedule.Closed?.ToList() ?? new List<DayOfWeek>(),
            CrewShifts = schedule.CrewShifts ?? new List<CrewShift>()
        };

        private static void DropIfEmpty(VehicleState vehicle)
        {
            var schedule = vehicle.Schedule;
            if (schedule == null) return;
            if ((schedule.Stops == null || schedule.Stops.Count == 0) && (schedule.CrewShifts == null || schedule.CrewShifts.Count == 0) &&
                (schedule.Closed == null || schedule.Closed.Count == 0))
                vehicle.Schedule = null;
        }

        private static List<ScheduleStop> Others(HireSchedule? schedule, DayOfWeek day, int? except) =>
            (schedule?.Stops ?? new List<ScheduleStop>()).Where((s, i) => s != null && s.Day == day && i != except).ToList();

        // The minutes (from `day`'s midnight) taken by the day's other stops, by the day before's stops running past
        // midnight when that day runs, and by the next day's stops when it runs.
        private static List<(int Open, int Close)> Busy(HireSchedule? schedule, DayOfWeek day, int? except)
        {
            var busy = Others(schedule, day, except).Select(s => (s.OpenAt, s.CloseAt)).ToList();
            var previous = WeekPlan.Previous(day);
            if (WeekPlan.RunsOn(schedule, previous))
                busy.AddRange(Others(schedule, previous, except).Select(s => (s.OpenAt - 1440, s.CloseAt - 1440)));
            var next = WeekPlan.Next(day);
            if (WeekPlan.RunsOn(schedule, next))
                busy.AddRange(Others(schedule, next, except).Select(s => (s.OpenAt + 1440, s.CloseAt + 1440)));
            return busy;
        }

        // Touching ends are fine, as Validate has it.
        private static bool Free(List<(int Open, int Close)> busy, int open, int close) =>
            busy.All(b => close <= b.Open || open >= b.Close);

        // Each stop of `before` is now at its index in `after` (or gone): plans, the open window, an arrival, a stop given
        // up on and a trip on its way follow.
        private static void Remap(VehicleState vehicle, List<ScheduleStop> before, List<ScheduleStop> after)
        {
            int Map(int i) => i >= 0 && i < before.Count ? after.IndexOf(before[i]) : -1;
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
            {
                // The drive back to the bay belongs to no stop and stays.
                plan.Legs.RemoveAll(l => l.StopIndex != HireSchedules.HomeIndex && Map(l.StopIndex) < 0);
                foreach (var leg in plan.Legs)
                    if (leg.StopIndex != HireSchedules.HomeIndex) leg.StopIndex = Map(leg.StopIndex);
            }
            if (vehicle.HireStop != null)
            {
                var moved = Map(vehicle.HireStop.Value);
                vehicle.HireStop = moved < 0 ? (int?)null : moved;
            }
            if (vehicle.Arrival?.StopIndex != null)
            {
                var moved = Map(vehicle.Arrival.StopIndex.Value);
                vehicle.Arrival.StopIndex = moved < 0 ? (int?)null : moved;
            }
            if (vehicle.Skipped?.StopIndex != null)
            {
                var moved = Map(vehicle.Skipped.StopIndex.Value);
                vehicle.Skipped.StopIndex = moved < 0 ? (int?)null : moved;
            }
            // A removed destination falls back to finding the stop by its spot.
            if (vehicle.Transit?.ToStopIndex != null)
            {
                var moved = Map(vehicle.Transit.ToStopIndex.Value);
                vehicle.Transit.ToStopIndex = moved < 0 ? (int?)null : moved;
            }
        }
    }
}
