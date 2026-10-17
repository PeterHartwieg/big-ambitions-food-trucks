using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    // Weekdays with the same part of their route day uncovered: the whole day, or from Start to End (minutes from that
    // weekday's midnight, past 1440 for a late route). BeforeShift: the gap opens the route day, so the morning restock and
    // the way to the first stop start before anyone's shift.
    public sealed class UncoveredDays
    {
        public UncoveredDays(IReadOnlyList<DayOfWeek> days, bool wholeDay, int start, int end, bool beforeShift)
        {
            Days = days; WholeDay = wholeDay; Start = start; End = end; BeforeShift = beforeShift;
        }

        public IReadOnlyList<DayOfWeek> Days { get; }
        public bool WholeDay { get; }
        public int Start { get; }
        public int End { get; }
        public bool BeforeShift { get; }
    }

    // Why a route has its to-do (HireRunner.RouteProblem): the problem, and for Unreachable which stop, for Uncovered which
    // days and hours (plan, "AI playthrough", agreed with Peter on 17 October 2026: the to-do names the real cause).
    public sealed class RouteCause
    {
        public RouteCause(ScheduleProblem problem, DayOfWeek? day = null, int? stopNumber = null, IReadOnlyList<UncoveredDays>? uncovered = null)
        {
            Problem = problem; Day = day; StopNumber = stopNumber; Uncovered = uncovered ?? Array.Empty<UncoveredDays>();
        }

        public ScheduleProblem Problem { get; }
        // Unreachable: the stop's weekday and its number on that day's route, from 1.
        public DayOfWeek? Day { get; }
        public int? StopNumber { get; }
        // Uncovered: groups of weekdays, Monday first, in the order their first day comes.
        public IReadOnlyList<UncoveredDays> Uncovered { get; }

        // The cause as one line, from the mod's texts. `text(key, values)` localizes a key with its placeholders; `walk` is
        // a pushed cart's way between stops (a walk) rather than a drive.
        public string Describe(Func<string, IReadOnlyDictionary<string, string>, string> text, bool walk)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string T(string key, params (string Name, string Value)[] values) =>
                text(key, values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal));
            switch (Problem)
            {
                case ScheduleProblem.Overlap:
                    return T("foodtrucks:route_cause_overlap");
                case ScheduleProblem.Unreachable when Day != null && StopNumber != null:
                    return T("foodtrucks:route_cause_unreachable", ("number", StopNumber.Value.ToString()), ("day", DayName(text, Day.Value)),
                        ("way", T(walk ? "foodtrucks:route_cause_way_walk" : "foodtrucks:route_cause_way_drive")));
                case ScheduleProblem.Uncovered when Uncovered.Count > 0:
                {
                    var first = Uncovered[0];
                    var days = Days(text, first.Days);
                    var line = first.WholeDay
                        ? T("foodtrucks:route_cause_nobody", ("days", days))
                        : T("foodtrucks:route_cause_nobody_hours", ("from", Clock(first.Start)), ("to", Clock(first.End)), ("days", days));
                    if (!first.WholeDay && first.BeforeShift)
                        line = T(walk ? "foodtrucks:route_cause_before_shift_walk" : "foodtrucks:route_cause_before_shift_drive", ("cause", line));
                    if (Uncovered.Count > 1) line = T("foodtrucks:route_cause_more", ("cause", line));
                    return line;
                }
                default:
                    return T("foodtrucks:route_cause_times");
            }
        }

        public static string Clock(int minute)
        {
            var m = ((minute % 1440) + 1440) % 1440;
            return $"{m / 60:D2}:{m % 60:D2}";
        }

        private static string DayName(Func<string, IReadOnlyDictionary<string, string>, string> text, DayOfWeek day) =>
            text("foodtrucks:weekday_" + day.ToString().ToLowerInvariant(), new Dictionary<string, string>());

        // "Monday to Friday" for three or more days in a row, "Saturday and Sunday", "Monday, Wednesday and Friday".
        public static string Days(Func<string, IReadOnlyDictionary<string, string>, string> text, IReadOnlyList<DayOfWeek> days)
        {
            var order = days.OrderBy(WeekPlan.Position).ToList();
            var parts = new List<string>();
            for (var i = 0; i < order.Count;)
            {
                var j = i;
                while (j + 1 < order.Count && WeekPlan.Position(order[j + 1]) == WeekPlan.Position(order[j]) + 1) j++;
                if (j - i >= 2)
                    parts.Add(text("foodtrucks:days_range", new Dictionary<string, string> { ["first"] = DayName(text, order[i]), ["last"] = DayName(text, order[j]) }));
                else
                    for (var k = i; k <= j; k++) parts.Add(DayName(text, order[k]));
                i = j + 1;
            }
            if (parts.Count <= 1) return parts.FirstOrDefault() ?? "";
            return text("foodtrucks:days_and", new Dictionary<string, string>
            {
                ["list"] = string.Join(", ", parts.Take(parts.Count - 1)), ["last"] = parts[parts.Count - 1]
            });
        }
    }
}
