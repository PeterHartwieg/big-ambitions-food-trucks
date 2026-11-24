using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    public sealed class StopEditRefused : Exception
    {
        public StopEditRefused(string message) : base(message) { }
    }

    public static class StopEdit
    {
        public const double SameSpotMetres = 1.0;

        public static Func<ScheduleStop, bool> AtSpot(double x, double z) =>
            s => Math.Abs(s.X - x) <= SameSpotMetres && Math.Abs(s.Z - z) <= SameSpotMetres;

        public static int Pick(HireSchedule? schedule, int? index, DayOfWeek? day, int? stop, Func<ScheduleStop, bool>? atAddress)
        {
            var stops = schedule?.Stops ?? new List<ScheduleStop>();
            if (index != null)
            {
                if (day != null || stop != null || atAddress != null)
                    throw new StopEditRefused("Name the stop one way: \"index\" alone, or \"day\" with \"stop\" or \"address\".");
                if (index.Value < 0 || index.Value >= stops.Count || stops[index.Value] == null)
                    throw new StopEditRefused($"No stop at index {index.Value}: the week has {Counted(stops.Count)}.");
                return index.Value;
            }
            if (day == null)
                throw new StopEditRefused(stop != null || atAddress != null
                    ? "\"stop\" and \"address\" need \"day\"."
                    : "Name the stop: \"index\", or \"day\" with \"stop\" or \"address\".");
            var name = day.Value.ToString().ToLowerInvariant();
            if (stop != null && atAddress != null) throw new StopEditRefused("Give \"stop\" or \"address\", not both.");
            if (stop == null && atAddress == null) throw new StopEditRefused("With \"day\", give \"stop\" or \"address\".");
            var todays = WeekPlan.StopsOf(schedule, day.Value);
            if (stop != null)
            {
                if (stop.Value < 0 || stop.Value >= todays.Count)
                    throw new StopEditRefused($"No stop {stop.Value} on {name}: it has {Counted(todays.Count)}.");
                return todays[stop.Value];
            }
            var matches = todays.Where(i => atAddress!(stops[i])).ToList();
            if (matches.Count == 0) throw new StopEditRefused($"No stop by that address on {name}.");
            if (matches.Count > 1)
                throw new StopEditRefused($"{matches.Count} stops by that address on {name} (week index {string.Join(", ", matches)}); name one with \"stop\" or \"index\".");
            return matches[0];
        }

        public static (int Open, int Close) Hours(ScheduleStop stop, int? open, int? close)
        {
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            if (open == null && close == null) throw new StopEditRefused("Give \"open\", \"close\" or both, as \"HH:MM\".");
            var newOpen = open ?? stop.OpenAt;
            if (close == null) return (newOpen, stop.CloseAt);
            var newClose = close.Value <= newOpen ? close.Value + 1440 : close.Value;
            return (newOpen, newClose);
        }

        public static ScheduleProblem Apply(HireSchedules schedules, VehicleState vehicle, int index, int open, int close, out int moved)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(index);
            var problem = RouteEditor.SetTimes(schedules, vehicle, index, open, close);
            moved = problem == ScheduleProblem.None && stop != null ? vehicle.Schedule!.Stops.IndexOf(stop) : index;
            return problem;
        }

        public static void ForgetYesterday(VehicleState vehicle, int index, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(index);
            var day = GameDays.DayOf(now);
            if (stop != null && stop.Day == WeekPlan.Previous(GameDays.Weekday(day))) vehicle.Shifts?.RemoveAll(p => p.Day == day - 1);
        }

        public static bool ClosePassed(ScheduleStop stop, long now)
        {
            if (stop == null) throw new ArgumentNullException(nameof(stop));
            var day = GameDays.DayOf(now);
            var weekday = GameDays.Weekday(day);
            var minute = now - (day - 1) * 1440L;
            if (stop.Day == weekday) return minute >= stop.CloseAt;
            if (stop.Day == WeekPlan.Previous(weekday)) return minute + 1440 >= stop.CloseAt;
            return true;
        }

        public static bool MustClose(VehicleState vehicle, int index, long now)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            var stop = vehicle.Schedule?.Stops?.ElementAtOrDefault(index);
            return stop != null && vehicle.IsOpen && vehicle.HireStop == index && ClosePassed(stop, now);
        }

        public static string Clock(int minute) => $"{minute / 60:D2}:{minute % 60:D2}";

        private static string Counted(int count) => count == 0 ? "no stops" : count == 1 ? "1 stop (0)" : $"{count} stops (0 to {count - 1})";
    }
}
