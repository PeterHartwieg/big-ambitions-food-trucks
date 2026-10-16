using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    public enum ShiftProblem
    {
        None,
        // Not whole hours from 0 to 24 with the start before the end.
        BadHours,
        // Longer than FleetRules.MaxShiftHours.
        TooLong,
        // The employee isn't on the vehicle's crew.
        NotCrew
    }

    // A part of a route day with nobody on shift: minutes from the weekday's midnight (may pass 1440), and the leg
    // running when it starts (where the vehicle waits closed).
    public readonly struct CoverageGap
    {
        public CoverageGap(int start, int end, LegKind? kind, int stopIndex)
        {
            Start = start;
            End = end;
            Kind = kind;
            StopIndex = stopIndex;
        }

        public int Start { get; }
        public int End { get; }
        public LegKind? Kind { get; }
        public int StopIndex { get; }
    }

    // One weekday's route as planned from the vehicle's bay (or its first stop without one), with someone always on shift:
    // what the page's band shows and what the shifts must cover. Minutes count from that weekday's midnight; a route day
    // can start before it (a restock before 00:00) and end after it.
    public sealed class RouteDay
    {
        internal RouteDay(DayOfWeek day, DayPlan plan)
        {
            Day = day;
            Plan = plan;
        }

        public DayOfWeek Day { get; }
        public DayPlan Plan { get; }
        public bool Empty => Plan.Legs.Count == 0;
        public int Start => (int)Plan.ShiftStart;
        public int End => (int)Plan.ShiftEnd;
    }

    // What a crew member wants per week (the game's HoursWorkingPerWeek job demand); null bounds: no demand.
    public sealed class CrewWish
    {
        public CrewWish(string employeeId, int? minHours = null, int? maxHours = null)
        {
            EmployeeId = employeeId ?? throw new ArgumentNullException(nameof(employeeId));
            MinHours = minHours;
            MaxHours = maxHours;
        }

        public string EmployeeId { get; }
        public int? MinHours { get; }
        public int? MaxHours { get; }

        // Whether a week of `hours` is inside the range (the game's HoursWorkingPerWeek.Fulfilled on assigned hours).
        public bool Fulfilled(int hours) => (MinHours == null || hours >= MinHours) && (MaxHours == null || hours <= MaxHours);
    }

    // The vehicle week plan (Peter, 16 October 2026): per-weekday stops and per-weekday crew shifts, who is on shift
    // when, the route day each weekday asks for, the parts of it nobody covers, and the auto schedule.
    public static class WeekPlan
    {
        // Monday first, as the game's schedule screen.
        public static readonly IReadOnlyList<DayOfWeek> Days = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
            DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        public static int Position(DayOfWeek day) => ((int)day + 6) % 7;
        public static DayOfWeek Next(DayOfWeek day) => (DayOfWeek)(((int)day + 1) % 7);
        public static DayOfWeek Previous(DayOfWeek day) => (DayOfWeek)(((int)day + 6) % 7);

        // The global indices of a weekday's stops, in list order (time order).
        public static List<int> StopsOf(HireSchedule? schedule, DayOfWeek day)
        {
            var result = new List<int>();
            var stops = schedule?.Stops;
            if (stops == null) return result;
            for (var i = 0; i < stops.Count; i++)
                if (stops[i] != null && stops[i].Day == day) result.Add(i);
            return result;
        }

        public static bool IsClosed(HireSchedule? schedule, DayOfWeek day) => schedule?.Closed != null && schedule.Closed.Contains(day);

        // The day has stops and isn't closed.
        public static bool RunsOn(HireSchedule? schedule, DayOfWeek day) => !IsClosed(schedule, day) && StopsOf(schedule, day).Count > 0;

        public static bool HasStops(HireSchedule? schedule) => schedule?.Stops != null && schedule.Stops.Count > 0;

        // Whether any weekday runs.
        public static bool RunsAnyDay(HireSchedule? schedule) => Days.Any(d => RunsOn(schedule, d));

        // The stops in their kept order: grouped by weekday, Monday first, each day's by opening (stable).
        public static List<ScheduleStop> Ordered(IEnumerable<ScheduleStop> stops) =>
            stops.OrderBy(s => Position(s.Day)).ThenBy(s => s.OpenAt).ToList();

        // A save from before the week plan: one stop list and "days" (empty meaning every day). The same stops become each
        // of those days' stops, and the vehicle's plans and stop references are dropped (they named the old list; the
        // spot keys find the stops again). Returns whether anything changed. Dev saves only: no other compatibility.
        public static bool Upgrade(VehicleState vehicle)
        {
            var schedule = vehicle?.Schedule;
            if (schedule == null) return false;
            schedule.ExtensionData ??= new Dictionary<string, JToken>();
            if (!schedule.ExtensionData.TryGetValue("days", out var token)) return false;
            schedule.ExtensionData.Remove("days");
            var days = new List<DayOfWeek>();
            if (token is JArray array)
                foreach (var item in array)
                {
                    if (item.Type == JTokenType.Integer && item.Value<int>() >= 0 && item.Value<int>() <= 6) days.Add((DayOfWeek)item.Value<int>());
                    else if (item.Type == JTokenType.String && Enum.TryParse<DayOfWeek>(item.Value<string>(), true, out var parsed)) days.Add(parsed);
                }
            if (days.Count == 0) days.AddRange(Days);
            var old = (schedule.Stops ?? new List<ScheduleStop>()).Where(s => s != null).OrderBy(s => s.OpenAt).ToList();
            schedule.Stops = Ordered(days.Distinct().SelectMany(d => old.Select(s => CopyStop(s, d))));
            schedule.Closed ??= new List<DayOfWeek>();
            schedule.CrewShifts ??= new List<CrewShift>();
            vehicle!.Shifts?.Clear();
            vehicle.HireStop = vehicle.HireStop == null ? (int?)null : Math.Min(vehicle.HireStop.Value, Math.Max(0, schedule.Stops.Count - 1));
            if (schedule.Stops.Count == 0) vehicle.HireStop = null;
            if (vehicle.Arrival != null) vehicle.Arrival.StopIndex = null;
            if (vehicle.Skipped != null) vehicle.Skipped.StopIndex = null;
            if (vehicle.Transit != null) vehicle.Transit.ToStopIndex = null;
            return true;
        }

        public static ScheduleStop CopyStop(ScheduleStop stop, DayOfWeek day, int? open = null, int? close = null) => new ScheduleStop
        {
            Day = day,
            SpotKey = stop.SpotKey,
            X = stop.X,
            Y = stop.Y,
            Z = stop.Z,
            Heading = stop.Heading,
            OpenAt = open ?? stop.OpenAt,
            CloseAt = close ?? stop.CloseAt
        };

        // ---- Shifts ----

        public static CrewShift? ShiftOf(HireSchedule? schedule, string employeeId, DayOfWeek day) =>
            schedule?.CrewShifts?.FirstOrDefault(s => s != null && s.Day == day && string.Equals(s.EmployeeId, employeeId, StringComparison.Ordinal));

        public static ShiftProblem CheckShift(FleetRules rules, int start, int end)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (start < 0 || end > 24 || start >= end) return ShiftProblem.BadHours;
            return end - start > rules.MaxShiftHours ? ShiftProblem.TooLong : ShiftProblem.None;
        }

        // One shift per crew member a day, as the game's: a new one replaces the day's old one.
        public static ShiftProblem SetShift(FleetRules rules, VehicleState vehicle, string employeeId, DayOfWeek day, int start, int end)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var problem = CheckShift(rules, start, end);
            if (problem != ShiftProblem.None) return problem;
            if (vehicle.Crew == null || !vehicle.Crew.Contains(employeeId)) return ShiftProblem.NotCrew;
            var schedule = vehicle.Schedule ??= new HireSchedule();
            schedule.CrewShifts ??= new List<CrewShift>();
            schedule.CrewShifts.RemoveAll(s => s == null || (s.Day == day && s.EmployeeId == employeeId));
            schedule.CrewShifts.Add(new CrewShift { EmployeeId = employeeId, Day = day, Start = start, End = end });
            SortShifts(schedule);
            return ShiftProblem.None;
        }

        public static bool RemoveShift(VehicleState vehicle, string employeeId, DayOfWeek day) =>
            (vehicle?.Schedule?.CrewShifts?.RemoveAll(s => s != null && s.Day == day && s.EmployeeId == employeeId) ?? 0) > 0;

        // A crew member left the vehicle: their shifts go with them.
        public static void DropCrew(HireSchedule? schedule, string employeeId) =>
            schedule?.CrewShifts?.RemoveAll(s => s == null || s.EmployeeId == employeeId);

        // Shifts of anyone no longer on the crew go.
        public static bool Prune(VehicleState vehicle)
        {
            var shifts = vehicle?.Schedule?.CrewShifts;
            if (shifts == null) return false;
            var crew = new HashSet<string>(vehicle!.Crew ?? new List<string>(), StringComparer.Ordinal);
            return shifts.RemoveAll(s => s == null || !crew.Contains(s.EmployeeId)) > 0;
        }

        internal static void SortShifts(HireSchedule schedule) =>
            schedule.CrewShifts = schedule.CrewShifts.Where(s => s != null).OrderBy(s => Position(s.Day)).ThenBy(s => s.Start)
                .ThenBy(s => s.EmployeeId, StringComparer.Ordinal).ToList();

        // Whether the crew member is on shift at that absolute game minute (day 1 is a Monday).
        public static bool OnShift(HireSchedule? schedule, string employeeId, long minute)
        {
            var shifts = schedule?.CrewShifts;
            if (shifts == null || string.IsNullOrEmpty(employeeId)) return false;
            var day = HireRunner.DayOf(minute);
            var weekday = Common.GameDays.Weekday(Math.Max(1, day));
            var hour = (int)((minute - HireSchedules.Absolute(day, 0)) / 60);
            return shifts.Any(s => s != null && s.Day == weekday && s.Start <= hour && hour < s.End &&
                                   string.Equals(s.EmployeeId, employeeId, StringComparison.Ordinal));
        }

        // The vehicle's crew on shift at that minute, in crew order.
        public static List<string> OnShiftCrew(VehicleState vehicle, long minute) =>
            (vehicle?.Crew ?? new List<string>()).Where(id => OnShift(vehicle!.Schedule, id, minute)).ToList();

        // A crew member's scheduled hours in the week, and the weekdays they work: the game's job demands read these
        // (EmployeeInstance.assignedWeeklyHours, assignedWeeklyDays).
        public static (int Hours, List<DayOfWeek> Days) WeeklyWork(HireSchedule? schedule, string employeeId)
        {
            var mine = (schedule?.CrewShifts ?? new List<CrewShift>())
                .Where(s => s != null && string.Equals(s.EmployeeId, employeeId, StringComparison.Ordinal) && s.End > s.Start).ToList();
            return (mine.Sum(s => s.End - s.Start), mine.Select(s => s.Day).Distinct().OrderBy(d => (int)d).ToList());
        }

        public static int WeekHours(HireSchedule? schedule, string employeeId) => WeeklyWork(schedule, employeeId).Hours;

        // When at least one of `crew` is on shift, over [from, to) in absolute minutes.
        public static Coverage CoverageOf(HireSchedule? schedule, IEnumerable<string>? crew, long from, long to)
        {
            var ids = new HashSet<string>(crew ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var spans = new List<(long, long)>();
            var shifts = schedule?.CrewShifts;
            if (shifts == null || ids.Count == 0 || to <= from) return new Coverage(spans);
            var first = HireRunner.DayOf(from) - 1;
            var last = HireRunner.DayOf(to);
            for (var day = Math.Max(1, first); day <= last; day++)
            {
                var weekday = Common.GameDays.Weekday(day);
                foreach (var s in shifts)
                    if (s != null && s.Day == weekday && ids.Contains(s.EmployeeId))
                        spans.Add((HireSchedules.Absolute(day, s.Start * 60), HireSchedules.Absolute(day, s.End * 60)));
            }
            return new Coverage(spans);
        }

        // How many of `crew` are on shift in each hour of the week, Monday 00:00 first (168 entries).
        public static int[] WeekCount(HireSchedule? schedule, IEnumerable<string>? crew)
        {
            var count = new int[168];
            var ids = new HashSet<string>(crew ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (var s in schedule?.CrewShifts ?? new List<CrewShift>())
                if (s != null && ids.Contains(s.EmployeeId))
                    for (var h = Math.Max(0, s.Start); h < Math.Min(24, s.End); h++) count[Position(s.Day) * 24 + h]++;
            return count;
        }

        // ---- Route days ----

        // The route day of each weekday, Monday first: planned from the bay (`home`) with the morning restock, or from the
        // first stop without one, as if someone were always on shift.
        public static IReadOnlyList<RouteDay> RouteDays(HireSchedules schedules, HireSchedule? schedule, TravelMode mode, double gameSpeed = 1.0,
            (double X, double Z)? home = null, IReadOnlyCollection<int>? bayStops = null) =>
            Days.Select(d => RouteDayOf(schedules, schedule, d, mode, gameSpeed, home, bayStops)).ToList();

        public static RouteDay RouteDayOf(HireSchedules schedules, HireSchedule? schedule, DayOfWeek day, TravelMode mode, double gameSpeed = 1.0,
            (double X, double Z)? home = null, IReadOnlyCollection<int>? bayStops = null)
        {
            if (schedules == null) throw new ArgumentNullException(nameof(schedules));
            if (schedule == null || !RunsOn(schedule, day) || schedules.Validate(schedule) != ScheduleProblem.None)
                return new RouteDay(day, DayPlan.Empty);
            var first = schedule.Stops[StopsOf(schedule, day)[0]];
            var (x, z) = home ?? (first.X, first.Z);
            // Day 1 is a Monday in absolute minutes; the plan is shifted so its minutes count from `day`'s midnight.
            var plan = schedules.PlanDay(schedule, 1, day, x, z, -1440, mode, gameSpeed, home, bayStops);
            return new RouteDay(day, plan);
        }

        // The parts of a route day nobody covers (minutes from its weekday's midnight), with the leg running at each
        // gap's start. `crew`: the vehicle's crew (shifts of anyone else don't count).
        public static IReadOnlyList<CoverageGap> Gaps(RouteDay routeDay, HireSchedule? schedule, IEnumerable<string>? crew)
        {
            if (routeDay == null) throw new ArgumentNullException(nameof(routeDay));
            if (routeDay.Empty) return Array.Empty<CoverageGap>();
            // The route day's minutes on a calendar Monday-first week, its weekday at day 8 (a Monday) + position.
            var day = 8 + Position(routeDay.Day);
            var origin = HireSchedules.Absolute(day, 0);
            var coverage = CoverageOf(schedule, crew, origin + routeDay.Start - 1440, origin + routeDay.End + 1440);
            var gaps = new List<CoverageGap>();
            foreach (var (a, b) in coverage.Gaps(origin + routeDay.Start, origin + routeDay.End))
            {
                var start = (int)(a - origin);
                var leg = routeDay.Plan.LegAt(start) ?? routeDay.Plan.Legs.LastOrDefault(l => l.End <= start);
                gaps.Add(new CoverageGap(start, (int)(b - origin), routeDay.Plan.Legs.Count == 0 ? (LegKind?)null : leg.Kind, leg.StopIndex));
            }
            return gaps;
        }

        // Whether any running weekday has a part of its route day with nobody on shift.
        public static bool AnyGap(IEnumerable<RouteDay> days, HireSchedule? schedule, IEnumerable<string>? crew)
        {
            var ids = (crew ?? Enumerable.Empty<string>()).ToList();
            return days.Any(d => Gaps(d, schedule, ids).Count > 0);
        }

        // ---- Auto schedule ----

        // Shifts for the crew on `days` (all weekdays when null), keeping the others' shifts: first the whole route day
        // with at least one person (shifts at most MaxShiftHours, ending at 24:00 and going on from 00:00 the next day,
        // an hour together at each handover), then a second person at each day's busiest stop (by `stopWeight`, else the
        // longest) where the vehicle takes two (`staff`) and that person's weekly hours stay within what they want. Whom:
        // the one furthest below the hours they want, then the fewest hours, then crew order. Deterministic.
        public static List<CrewShift> AutoSchedule(FleetRules rules, IReadOnlyList<RouteDay> routeDays, IReadOnlyList<CrewWish> crew, int staff,
            IEnumerable<CrewShift>? existing = null, IEnumerable<DayOfWeek>? days = null, Func<int, double>? stopWeight = null)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (routeDays == null) throw new ArgumentNullException(nameof(routeDays));
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            var max = Math.Max(1, Math.Min(24, rules.MaxShiftHours));
            var redo = new HashSet<DayOfWeek>(days ?? Days);
            var ids = crew.Select(c => c.EmployeeId).ToList();
            var shifts = (existing ?? Enumerable.Empty<CrewShift>())
                .Where(s => s != null && ids.Contains(s.EmployeeId) && !redo.Contains(s.Day))
                .Select(s => new CrewShift { EmployeeId = s.EmployeeId, Day = s.Day, Start = s.Start, End = s.End }).ToList();
            if (crew.Count == 0) return shifts;

            // The hours of the week each route day needs (Monday 00:00 is hour 0), spilling into the next weekday.
            var need = new bool[168];
            foreach (var routeDay in routeDays.Where(d => d != null && !d.Empty))
            {
                var first = (int)Math.Floor(routeDay.Start / 60.0);
                var last = (int)Math.Ceiling(routeDay.End / 60.0);
                for (var h = first; h < last; h++)
                {
                    var week = ((Position(routeDay.Day) * 24 + h) % 168 + 168) % 168;
                    // Only the weekdays being scheduled.
                    if (redo.Contains(Days[week / 24])) need[week] = true;
                }
            }

            int Hours(string id) => shifts.Where(s => s.EmployeeId == id).Sum(s => s.End - s.Start);
            CrewShift? On(string id, DayOfWeek day) => shifts.FirstOrDefault(s => s.EmployeeId == id && s.Day == day);
            IEnumerable<CrewWish> Ranked(IEnumerable<CrewWish> who) => who
                .Select((c, i) => (c, i))
                .OrderBy(x => Hours(x.c.EmployeeId) - (x.c.MinHours ?? 0))
                .ThenBy(x => Hours(x.c.EmployeeId))
                .ThenBy(x => crew.ToList().FindIndex(c => c.EmployeeId == x.c.EmployeeId))
                .Select(x => x.c);
            bool Allows(CrewWish who, int extra) => who.MaxHours == null || Hours(who.EmployeeId) + extra <= who.MaxHours;

            // 1. Cover every needed hour with one person.
            foreach (var day in Days.Where(redo.Contains))
            {
                var offset = Position(day) * 24;
                var h = 0;
                while (h < 24)
                {
                    if (!need[offset + h]) { h++; continue; }
                    var start = h;
                    while (h < 24 && need[offset + h]) h++;
                    foreach (var (a, b) in Chunks(start, h, max))
                    {
                        var free = crew.Where(c => On(c.EmployeeId, day) == null).ToList();
                        var pick = Ranked(free.Where(c => Allows(c, b - a))).FirstOrDefault() ?? Ranked(free).FirstOrDefault();
                        if (pick != null)
                        {
                            shifts.Add(new CrewShift { EmployeeId = pick.EmployeeId, Day = day, Start = a, End = b });
                            continue;
                        }
                        // Everyone works this day already: stretch a shift that stays within the cap.
                        var stretch = crew.Select(c => On(c.EmployeeId, day)).Where(s => s != null)
                            .Where(s => Math.Max(s!.End, b) - Math.Min(s.Start, a) <= max).OrderBy(s => Math.Max(s!.End, b) - Math.Min(s.Start, a)).FirstOrDefault();
                        if (stretch == null) continue;
                        stretch.Start = Math.Min(stretch.Start, a);
                        stretch.End = Math.Max(stretch.End, b);
                    }
                }
            }

            // 2. A second person at each day's busiest stop.
            if (staff >= 2 && crew.Count >= 2)
                foreach (var routeDay in routeDays.Where(d => d != null && !d.Empty && redo.Contains(d.Day)))
                {
                    var opens = routeDay.Plan.Legs.Where(l => l.Kind == LegKind.Open).GroupBy(l => l.StopIndex)
                        .Select(g => (Stop: g.Key, Start: g.Min(l => l.Start), End: g.Max(l => l.End))).ToList();
                    if (opens.Count == 0) continue;
                    var busiest = opens.OrderByDescending(o => stopWeight?.Invoke(o.Stop) ?? (o.End - o.Start)).ThenBy(o => o.Start).First();
                    var a = (int)Math.Max(0, Math.Floor(busiest.Start / 60.0));
                    var b = (int)Math.Min(24, Math.Ceiling(busiest.End / 60.0));
                    if (b <= a) continue;
                    var day = routeDay.Day;
                    bool Covers(CrewShift? s) => s != null && s.Start <= a && s.End >= b;
                    var onShift = crew.Count(c => Covers(On(c.EmployeeId, day)));
                    if (onShift >= 2) continue;
                    foreach (var who in Ranked(crew.Where(c => !Covers(On(c.EmployeeId, day)))))
                    {
                        var own = On(who.EmployeeId, day);
                        var start = own == null ? a : Math.Min(own.Start, a);
                        var end = own == null ? b : Math.Max(own.End, b);
                        var extra = end - start - (own == null ? 0 : own.End - own.Start);
                        if (end - start > max || !Allows(who, extra)) continue;
                        if (own == null) shifts.Add(new CrewShift { EmployeeId = who.EmployeeId, Day = day, Start = start, End = end });
                        else
                        {
                            own.Start = start;
                            own.End = end;
                        }
                        break;
                    }
                }

            var result = new HireSchedule { CrewShifts = shifts };
            SortShifts(result);
            return result.CrewShifts;
        }

        // [start, end) in pieces of at most `max` hours, an hour together at each handover, as even as they come.
        public static List<(int Start, int End)> Chunks(int start, int end, int max)
        {
            var length = end - start;
            if (length <= 0) return new List<(int, int)>();
            if (length <= max) return new List<(int, int)> { (start, end) };
            // One-hour shifts can't overlap.
            if (max < 2) return Enumerable.Range(start, length).Select(h => (h, h + 1)).ToList();
            var count = (int)Math.Ceiling((length - 1) / (double)(max - 1));
            var total = length + count - 1;
            var result = new List<(int, int)>();
            var at = start;
            for (var k = 0; k < count; k++)
            {
                var size = total / count + (k < total % count ? 1 : 0);
                var b = k == count - 1 ? end : at + size;
                result.Add((at, b));
                at = b - 1;
            }
            return result;
        }
    }
}
