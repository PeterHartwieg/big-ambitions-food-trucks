using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// The BizMan vehicle page's route editing (time pickers, drag to reorder) and the map pick's stop snapping.
public class RoutePageTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireSchedules schedules = new(rules);
    private const int Step = 30;

    private static ScheduleStop Stop(string key, double x, int open, int close) =>
        new() { SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static VehicleState Truck(params ScheduleStop[] stops) => new()
    {
        VehicleId = "t1",
        VehicleType = "truck",
        Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = stops.ToList() }
    };

    private static string[] Keys(VehicleState v) => v.Schedule!.Stops.Select(s => s.SpotKey).ToArray();

    [Fact]
    public void NewTimesKeepTheStopsInTimeOrderAndTheOpenWindowFollowsItsStop()
    {
        var truck = Truck(Stop("office", 100, 11 * 60, 14 * 60), Stop("bars", 200, 18 * 60, 23 * 60));
        truck.HireStop = 0;
        truck.Transit = new TransitState { ToStopIndex = 1, ToSpotKey = "bars" };

        // The office stop moves to the morning after the bars: still a valid route, now second.
        Assert.Equal(ScheduleProblem.None, RouteEditor.SetTimes(schedules, truck, 0, 23 * 60 + 30, 26 * 60));

        Assert.Equal(new[] { "bars", "office" }, Keys(truck));
        Assert.Equal(1, truck.HireStop);
        Assert.Equal(0, truck.Transit.ToStopIndex);
    }

    [Fact]
    public void TimesThatOverlapAnotherStopAreRefusedAndNothingChanges()
    {
        var truck = Truck(Stop("office", 100, 11 * 60, 14 * 60), Stop("bars", 200, 18 * 60, 23 * 60));

        Assert.Equal(ScheduleProblem.Overlap, RouteEditor.SetTimes(schedules, truck, 1, 13 * 60, 20 * 60));
        Assert.Equal(ScheduleProblem.BadTimes, RouteEditor.SetTimes(schedules, truck, 1, 18 * 60, 17 * 60));

        Assert.Equal(new[] { "office", "bars" }, Keys(truck));
        Assert.Equal(18 * 60, truck.Schedule!.Stops[1].OpenAt);
        Assert.Equal(23 * 60, truck.Schedule.Stops[1].CloseAt);
    }

    [Fact]
    public void DraggingAStopMovesThePlaceAndLeavesTheTimeSlotsWhereTheyWere()
    {
        var truck = Truck(Stop("office", 100, 8 * 60, 10 * 60), Stop("park", 200, 11 * 60, 14 * 60), Stop("bars", 300, 18 * 60, 23 * 60));
        truck.HireStop = 2;
        truck.Arrival = new ArrivalState { SpotKey = "office", StopIndex = 0, X = 100, Z = 0 };

        Assert.Equal(ScheduleProblem.None, RouteEditor.MovePlace(schedules, truck, 2, 0));

        Assert.Equal(new[] { "bars", "office", "park" }, Keys(truck));
        var stops = truck.Schedule!.Stops;
        Assert.Equal(new[] { 8 * 60, 11 * 60, 18 * 60 }, stops.Select(s => s.OpenAt).ToArray());
        Assert.Equal(new[] { 10 * 60, 14 * 60, 23 * 60 }, stops.Select(s => s.CloseAt).ToArray());
        // The places keep their positions; the window open at the bars and the arrival at the office follow them.
        Assert.Equal(300, stops[0].X);
        Assert.Equal(0, truck.HireStop);
        Assert.Equal(1, truck.Arrival.StopIndex);
    }

    [Fact]
    public void ADragOutsideTheListChangesNothing()
    {
        var truck = Truck(Stop("office", 100, 8 * 60, 10 * 60), Stop("park", 200, 11 * 60, 14 * 60));

        Assert.Equal(ScheduleProblem.NoStops, RouteEditor.MovePlace(schedules, truck, 0, 5));
        Assert.Equal(ScheduleProblem.None, RouteEditor.MovePlace(schedules, truck, 1, 1));

        Assert.Equal(new[] { "office", "park" }, Keys(truck));
    }

    [Fact]
    public void ThePickersOfferOnlyTimesThatFitBetweenTheOtherStops()
    {
        var route = new HireSchedule { Stops = { Stop("office", 100, 11 * 60, 14 * 60), Stop("bars", 200, 18 * 60, 23 * 60) } };

        var opens = RouteEditor.OpenTimes(route, Step, rules.LatestCloseMinute);
        Assert.Contains(10 * 60 + 30, opens);
        Assert.DoesNotContain(11 * 60, opens);
        Assert.DoesNotContain(13 * 60 + 30, opens);
        Assert.Contains(14 * 60, opens);
        Assert.Contains(17 * 60 + 30, opens);
        Assert.DoesNotContain(22 * 60, opens);
        Assert.Contains(23 * 60, opens);

        // From 14:00 a stop can run up to the bars' opening.
        var closes = RouteEditor.CloseTimes(route, 14 * 60, Step, rules.LatestCloseMinute);
        Assert.Equal(14 * 60 + 30, closes.First());
        Assert.Equal(18 * 60, closes.Last());

        // The stop being edited doesn't stand in its own way.
        Assert.Contains(12 * 60, RouteEditor.OpenTimes(route, Step, rules.LatestCloseMinute, except: 0));
        Assert.Equal(rules.LatestCloseMinute, RouteEditor.CloseTimes(route, 23 * 60, Step, rules.LatestCloseMinute, except: 1).Last());
    }

    [Fact]
    public void ThePickersSeeAStopRunningPastMidnightAsTheNextDaysToo()
    {
        // The bars run 22:00 to 02:00 the next morning.
        var route = new HireSchedule { Stops = { Stop("bars", 200, 22 * 60, 26 * 60) } };

        var opens = RouteEditor.OpenTimes(route, Step, rules.LatestCloseMinute);
        Assert.DoesNotContain(60, opens);
        Assert.DoesNotContain(90, opens);
        Assert.Contains(2 * 60, opens);
        // From 00:00 nothing fits before the bars' carry-over ends at 02:00.
        Assert.Empty(RouteEditor.CloseTimes(route, 0, Step, rules.LatestCloseMinute));
        // An early stop from 02:00 runs up to the bars' opening, not into the night.
        Assert.Equal(22 * 60, RouteEditor.CloseTimes(route, 2 * 60, Step, rules.LatestCloseMinute).Last());
    }

    [Fact]
    public void AStopIsNeverOfferedLongerThanADay()
    {
        var closes = RouteEditor.CloseTimes(new HireSchedule(), 0, Step, rules.LatestCloseMinute);
        Assert.Equal(24 * 60, closes.Last());
    }

    [Fact]
    public void RoutesThatRunIntoTheNextDayAreRefused()
    {
        // The last stop's carry-over into the first stop's next day.
        var wrap = new HireSchedule { Stops = { Stop("early", 100, 60, 4 * 60), Stop("bars", 200, 22 * 60, 26 * 60) } };
        Assert.Equal(ScheduleProblem.Overlap, schedules.Validate(wrap));
        // Touching ends are fine.
        wrap.Stops[0].OpenAt = 2 * 60;
        Assert.Equal(ScheduleProblem.None, schedules.Validate(wrap));
        // A stop open longer than a day runs into its own opening.
        Assert.Equal(ScheduleProblem.BadTimes, schedules.Validate(new HireSchedule { Stops = { Stop("all day", 100, 0, 25 * 60) } }));
        // And the editor refuses both, changing nothing.
        var truck = Truck(Stop("early", 100, 2 * 60, 4 * 60), Stop("bars", 200, 22 * 60, 23 * 60));
        Assert.Equal(ScheduleProblem.Overlap, RouteEditor.SetTimes(schedules, truck, 1, 22 * 60, 26 * 60 + 30));
        Assert.Equal(23 * 60, truck.Schedule!.Stops[1].CloseAt);
    }

    [Fact]
    public void TheOvernightRuleAppliesOnlyWhenTheRouteRunsTwoDaysInARow()
    {
        var stops = new[] { Stop("early", 100, 60, 4 * 60), Stop("bars", 200, 22 * 60, 26 * 60) };
        // Monday, Wednesday and Friday: no night runs into a working morning.
        var alternate = new HireSchedule { Days = { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, Stops = stops.ToList() };
        Assert.Equal(ScheduleProblem.None, schedules.Validate(alternate));
        Assert.Contains(60, RouteEditor.OpenTimes(new HireSchedule { Days = alternate.Days, Stops = { stops[1] } }, Step, rules.LatestCloseMinute));
        // Monday to Friday: Monday night runs into Tuesday's early stop.
        var weekdays = new HireSchedule { Days = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, Stops = stops.ToList() };
        Assert.Equal(ScheduleProblem.Overlap, schedules.Validate(weekdays));
        // Saturday and Sunday run in a row (Saturday into Sunday).
        Assert.True(HireSchedules.RunsOnConsecutiveDays(new HireSchedule { Days = { DayOfWeek.Saturday, DayOfWeek.Sunday } }));
        Assert.True(HireSchedules.RunsOnConsecutiveDays(new HireSchedule { Days = { DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday } }));
        Assert.False(HireSchedules.RunsOnConsecutiveDays(new HireSchedule { Days = { DayOfWeek.Saturday } }));
    }

    [Fact]
    public void NewDaysThatTheStopsDontFitAreRefusedAndARouteThatFailsSaysWhy()
    {
        var truck = Truck(Stop("early", 100, 60, 4 * 60), Stop("bars", 200, 22 * 60, 26 * 60));
        truck.Schedule!.Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday };
        var runner = new HireRunner(rules);
        Assert.Equal(ScheduleProblem.None, runner.RouteProblem(truck));

        Assert.Equal(ScheduleProblem.Overlap, RouteEditor.SetDays(schedules, truck, Array.Empty<DayOfWeek>()));
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, truck.Schedule.Days);
        Assert.Equal(ScheduleProblem.None, RouteEditor.SetDays(schedules, truck, new[] { DayOfWeek.Friday }));
        Assert.Equal(new[] { DayOfWeek.Friday }, truck.Schedule.Days);

        // A route saved under the old rules (every day, a night into the morning): no plan, and the reason.
        truck.Schedule.Days = new List<DayOfWeek>();
        Assert.Equal(ScheduleProblem.Overlap, runner.RouteProblem(truck));
        runner.EnsurePlan(truck, HireSchedules.Absolute(1, 0), 0, 0);
        Assert.Empty(truck.Shifts!);
        Assert.Equal(ScheduleProblem.None, runner.RouteProblem(Truck()));
    }

    [Fact]
    public void ANewOpeningTimeKeepsTheStopsLengthWhereItFits()
    {
        var route = new HireSchedule { Stops = { Stop("office", 100, 11 * 60, 14 * 60), Stop("bars", 200, 18 * 60, 23 * 60) } };

        // Three hours from 15:00 run to 18:00, right up to the bars.
        Assert.Equal(18 * 60, RouteEditor.CloseFor(route, 0, 15 * 60, Step, rules.LatestCloseMinute));
        // From 16:00 they are cut short at the bars.
        Assert.Equal(18 * 60, RouteEditor.CloseFor(route, 0, 16 * 60, Step, rules.LatestCloseMinute));
        // Inside the bars' hours nothing fits.
        Assert.Null(RouteEditor.CloseFor(route, 0, 19 * 60, Step, rules.LatestCloseMinute));
    }

    [Theory]
    [InlineData(HatchVerdict.Pavement, HatchVerdict.WrongWay, HeadingChoice.Along)]
    [InlineData(HatchVerdict.WrongWay, HatchVerdict.Pavement, HeadingChoice.Reverse)]
    [InlineData(HatchVerdict.Unclear, HatchVerdict.Pavement, HeadingChoice.Reverse)]
    [InlineData(HatchVerdict.Unclear, HatchVerdict.Unclear, HeadingChoice.Along)]
    [InlineData(HatchVerdict.NoPavement, HatchVerdict.Unclear, HeadingChoice.Reverse)]
    [InlineData(HatchVerdict.WrongWay, HatchVerdict.NoPavement, HeadingChoice.None)]
    public void TheTruckStandsWithItsHatchToThePavement(HatchVerdict along, HatchVerdict reverse, HeadingChoice expected) =>
        Assert.Equal(expected, StopSnap.ChooseHeading(along, reverse));

    [Fact]
    public void HeadingsAreUnityYaw()
    {
        Assert.Equal(0, StopSnap.HeadingDegrees(0, 1), 9);
        Assert.Equal(90, StopSnap.HeadingDegrees(1, 0), 9);
        Assert.Equal(180, StopSnap.HeadingDegrees(0, -1), 9);
        Assert.Equal(270, StopSnap.HeadingDegrees(-1, 0), 9);
    }

    // Two parking lanes along z, 2.4 m wide: one by the entrance (x 3 to 5.4) from z 0 to 12, one further off
    // (x -20 to -17.6) from z -10 to 30.
    private static bool Legal(double x, double z, double _, double __) =>
        (x >= 2.9 && x <= 5.5 && z >= -0.1 && z <= 12.1) || (x >= -20.1 && x <= -17.5 && z >= -10.1 && z <= 30.1);

    private static IEnumerable<(double X, double Z)> Axes(double x, double z) => new[] { (0.0, 1.0), (1.0, 0.0) };

    [Fact]
    public void AStopSnapsToTheNearestLegalPlaceTheHeadingAccepts()
    {
        var found = StopSnap.Nearest(0, 6, Legal, Axes, WheelFootprint.Truck, (_, _, ax, az) => StopSnap.HeadingDegrees(ax, az));

        Assert.NotNull(found);
        Assert.Equal(4.2, found!.X, 1);
        Assert.InRange(found.Z, 2.0, 10.0);
        Assert.Equal(0, found.Heading, 6);
        Assert.True(found.Distance < 6);
    }

    [Fact]
    public void APlaceWithTheHatchToTheRoadBothWaysIsPassedOver()
    {
        // The near lane is a median: road by the hatch either way round. The far lane is taken instead.
        var found = StopSnap.Nearest(0, 6, Legal, Axes, WheelFootprint.Truck, (x, _, ax, az) => x > 0 ? null : StopSnap.HeadingDegrees(-ax, -az));

        Assert.NotNull(found);
        Assert.True(found!.X < -17);
        Assert.Equal(180, found.Heading, 6);
    }

    [Fact]
    public void NoPlaceWithinReachIsNoStop()
    {
        Assert.Null(StopSnap.Nearest(0, 6, Legal, Axes, WheelFootprint.Truck, (x, _, _, _) => x > 0 ? null : 0, maxMetres: 10));
        Assert.Null(StopSnap.Nearest(100, 100, Legal, Axes, WheelFootprint.Truck, (_, _, _, _) => 0));
    }

    [Fact]
    public void ACartGoesToThePavementSideJustPastTheKerb()
    {
        var pavementRight = new SideReading(2.5, null);
        var roadLeft = new SideReading(null, 3.0);

        Assert.Equal(1, StopSnap.PavementSide(pavementRight, roadLeft));
        Assert.Equal(-1, StopSnap.PavementSide(roadLeft, pavementRight));
        Assert.Equal(0, StopSnap.PavementSide(roadLeft, roadLeft));
        // A median with pavement both sides: the nearer.
        Assert.Equal(-1, StopSnap.PavementSide(new SideReading(3.0, null), new SideReading(2.0, null)));
        Assert.Equal(new[] { 3.25, 3.75, 4.25 }, StopSnap.CartOffsets(2.5).ToArray());
    }
}
