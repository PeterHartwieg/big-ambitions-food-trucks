using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Tests.Fleet;

// Week plans for tests that are about something else: stops on chosen weekdays, crew on shift around the clock.
internal static class WeekTestData
{
    // Every crew member on shift all day, every day: the vehicle runs as if shifts didn't exist.
    public static T AllDay<T>(T vehicle) where T : VehicleState
    {
        vehicle.Schedule ??= new HireSchedule();
        vehicle.Schedule.CrewShifts = (vehicle.Crew ?? new List<string>())
            .SelectMany((id, i) => WeekPlan.Days.Select(d => new CrewShift
            {
                EmployeeId = id, Day = d, Start = 0, End = 24, Station = WeekPlan.Serving(i + 1)
            })).ToList();
        return vehicle;
    }

    // The same stops on each of the days (every day when none are named), in the kept order.
    public static List<ScheduleStop> On(IEnumerable<ScheduleStop> stops, params DayOfWeek[] days) =>
        WeekPlan.Ordered((days.Length == 0 ? WeekPlan.Days : days).SelectMany(d => stops.Select(s => WeekPlan.CopyStop(s, d))));

    public static HireSchedule EveryDay(params ScheduleStop[] stops) => new() { Stops = On(stops) };
}
