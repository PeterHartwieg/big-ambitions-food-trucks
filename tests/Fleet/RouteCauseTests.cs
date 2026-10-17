using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// The route to-do names its real cause (agreed with Peter, 17 October 2026).
public class RouteCauseTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly (double X, double Z) Bay = (-2000, 0);

    // The shipped English texts, as the game localizes them.
    private static readonly Dictionary<string, string> En = new()
    {
        ["foodtrucks:route_cause_times"] = "check the stops' times",
        ["foodtrucks:route_cause_overlap"] = "two stops' times overlap",
        ["foodtrucks:route_cause_unreachable"] = "stop {number} on {day} opens before the {way} there can end",
        ["foodtrucks:route_cause_way_drive"] = "drive",
        ["foodtrucks:route_cause_way_walk"] = "walk",
        ["foodtrucks:route_cause_nobody"] = "nobody on shift {days}",
        ["foodtrucks:route_cause_nobody_hours"] = "nobody on shift {from}–{to} {days}",
        ["foodtrucks:route_cause_before_shift_drive"] = "{cause}: the restock and drive start before the shift",
        ["foodtrucks:route_cause_before_shift_walk"] = "{cause}: the restock and walk start before the shift",
        ["foodtrucks:route_cause_more"] = "{cause}, and on other days",
        ["foodtrucks:days_range"] = "{first} to {last}",
        ["foodtrucks:days_and"] = "{list} and {last}",
    };

    private static string Text(string key, IReadOnlyDictionary<string, string> values)
    {
        var text = key.StartsWith("foodtrucks:weekday_") ? char.ToUpperInvariant(key[19]) + key.Substring(20) : En[key];
        return values.Aggregate(text, (t, v) => t.Replace("{" + v.Key + "}", v.Value));
    }

    private static ScheduleStop Stop(DayOfWeek day, double x, int open, int close) =>
        new() { Day = day, SpotKey = "s" + x, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static VehicleState Truck(IEnumerable<ScheduleStop> stops) => new()
    {
        VehicleId = "t1", VehicleType = "truck", HasPermit = true, Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = WeekPlan.Ordered(stops) }
    };

    private static readonly DayOfWeek[] Week =
        { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

    [Fact]
    public void NobodyOnShiftForWholeDaysNamesTheDays()
    {
        var truck = Truck(Week.Select(d => Stop(d, 0, 660, 840)));
        foreach (var d in Week.Take(5)) WeekPlan.SetShift(rules, truck, "e1", d, 8, 17);

        var cause = new HireRunner(rules).RouteCauseOf(truck, home: Bay)!;

        Assert.Equal(ScheduleProblem.Uncovered, cause.Problem);
        Assert.Equal("nobody on shift Saturday and Sunday", cause.Describe(Text, walk: false));
    }

    [Fact]
    public void AShiftStartingAfterTheRestockNamesTheHoursAndWhy()
    {
        var truck = Truck(Week.Select(d => Stop(d, 0, 660, 840)));
        foreach (var d in Week) WeekPlan.SetShift(rules, truck, "e1", d, 10, 17);
        var cause = new HireRunner(rules).RouteCauseOf(truck, home: Bay)!;
        var gap = Assert.Single(cause.Uncovered);
        Assert.True(gap.BeforeShift);
        Assert.Equal(7, gap.Days.Count);
        Assert.Equal($"nobody on shift {RouteCause.Clock(gap.Start)}–10:00 Monday to Sunday: the restock and walk start before the shift",
            cause.Describe(Text, walk: true));
        // Days with other gaps are said to be there too, and the line stays one line.
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Sunday, 8, 12);
        var two = new HireRunner(rules).RouteCauseOf(truck, home: Bay)!;
        Assert.Equal(2, two.Uncovered.Count);
        Assert.EndsWith(", and on other days", two.Describe(Text, walk: false));
        Assert.Contains("Monday to Saturday", two.Describe(Text, walk: false));
    }

    [Fact]
    public void AnUnreachableStopIsNamedByDayAndNumber()
    {
        var truck = Truck(new[] { Stop(DayOfWeek.Tuesday, 0, 600, 720), Stop(DayOfWeek.Tuesday, 1000, 720, 840) });
        var cause = new HireRunner(rules).RouteCauseOf(truck)!;
        Assert.Equal((ScheduleProblem.Unreachable, DayOfWeek.Tuesday, 2), (cause.Problem, cause.Day!.Value, cause.StopNumber!.Value));
        Assert.Equal("stop 2 on Tuesday opens before the drive there can end", cause.Describe(Text, walk: false));
    }

    [Fact]
    public void ValidatesProblemsSayToCheckTheTimesAndNoProblemIsNull()
    {
        var overlap = Truck(new[] { Stop(DayOfWeek.Monday, 0, 600, 800), Stop(DayOfWeek.Monday, 0, 700, 900) });
        var cause = new HireRunner(rules).RouteCauseOf(overlap)!;
        Assert.Equal(ScheduleProblem.Overlap, cause.Problem);
        Assert.Equal("two stops' times overlap", cause.Describe(Text, walk: false));
        Assert.Equal("check the stops' times", new RouteCause(ScheduleProblem.BadTimes).Describe(Text, walk: false));
        Assert.Null(new HireRunner(rules).RouteCauseOf(new VehicleState { VehicleId = "x", VehicleType = "cart" }));
    }

    [Fact]
    public void DaysReadAsRangesAndLists()
    {
        Assert.Equal("Monday, Wednesday and Friday", RouteCause.Days(Text, new[] { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday }));
        Assert.Equal("Monday to Wednesday and Saturday", RouteCause.Days(Text, new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Saturday }));
        Assert.Equal("Sunday", RouteCause.Days(Text, new[] { DayOfWeek.Sunday }));
    }
}
