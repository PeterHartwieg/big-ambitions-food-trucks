using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// The runner with the week plan (review round 1): the drive back is owed until the vehicle is in its bay, drives need
// someone on shift all the way, tomorrow starts before midnight when its restock does, shift edits replan yesterday's
// night, and a paste that takes the open stop never leaves the window open as the player's.
public class WeekPlanRunnerTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireSchedules schedules = new(rules);
    private static readonly HireRunner runner = new(rules);
    // The bay 2 km from the stops at x 0: 80 minutes' drive.
    private static readonly (double X, double Z) Bay = (-2000, 0);

    private static long At(int day, int hour, int minute = 0) => HireSchedules.Absolute(day, hour * 60 + minute);

    private static ScheduleStop Stop(DayOfWeek day, string key, int open, int close) =>
        new() { Day = day, SpotKey = key, X = 0, Z = 0, OpenAt = open, CloseAt = close };

    private static VehicleState Truck(params ScheduleStop[] stops) => new()
    {
        VehicleId = "t1", VehicleType = "truck", HasPermit = true, Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = WeekPlan.Ordered(stops) }
    };

    private static VehicleWhere AtStop(bool stayPut = false) => new(0, 0, leaveAlone: false, stayPut: stayPut, home: Bay);
    private static VehicleWhere InBay() => new(Bay.X, Bay.Z, leaveAlone: false, canRestock: true, home: Bay);

    // Day 3 (Wednesday): out to the 11:00-14:00 stop from the bay, parked there.
    private static VehicleState OutAtTheStop(int shiftEnd)
    {
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, shiftEnd);
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);
        HireRunner.Restocked(truck, At(3, 9, 10));
        var leave = runner.Step(truck, At(3, 9, 40), InBay());
        Assert.Equal(HireActionKind.Depart, leave.Kind);
        HireRunner.Depart(truck, leave, At(3, 9, 40), Bay.X, 0, Bay.Z, 0);
        HireRunner.Arrived(truck, 0, 0, atStop: true, At(3, 11));
        Assert.True(truck.ReturnPending);
        return truck;
    }

    [Fact]
    public void ADelayedDriveBackGoesWhenThePlayerLetsGoAndTheShiftCoversIt()
    {
        var truck = OutAtTheStop(shiftEnd: 18);
        // The player stays by it past the planned drive back (14:00-15:20).
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 14), AtStop(stayPut: true)).Kind);
        runner.EnsurePlan(truck, At(3, 16), 0, 0, home: Bay);
        var back = runner.Step(truck, At(3, 16), AtStop());
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex, At(3, 17, 20)), (back.Kind, back.Stop, back.ArrivesAt));
        HireRunner.Depart(truck, back, At(3, 16), 0, 0, 0, 0, new ScheduleStop { SpotKey = "bay", X = Bay.X, Z = Bay.Z });
        HireRunner.Arrived(truck, Bay.X, Bay.Z, atStop: true, At(3, 17, 20));
        Assert.False(truck.ReturnPending);
    }

    [Fact]
    public void AfterALateStopTheDriveBackGoesThoughTomorrowIsPlanned()
    {
        // Sunday's stop 22:00-02:00 and Monday's 11:00-14:00, 2 km from the bay (day 7 is a Sunday).
        var truck = Truck(Stop(DayOfWeek.Sunday, "bars", 1320, 1560), Stop(DayOfWeek.Monday, "office", 660, 840));
        truck.Crew.Add("e2");
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Sunday, 12, 24);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 0, 4);
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Monday, 8, 20);
        runner.EnsurePlan(truck, At(7, 23), 0, 0, home: Bay);
        truck.ReturnPending = true;
        runner.EnsurePlan(truck, At(8, 0, 1), 0, 0, home: Bay);
        Assert.Contains(truck.Shifts, p => p.Day == 8 && p.Legs.Count > 0);

        var back = runner.Step(truck, At(8, 2), AtStop());
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex, At(8, 3, 20)), (back.Kind, back.Stop, back.ArrivesAt));
        // Held by the player past the planned drive back: Monday's restock comes next, so it still goes (02:40 to 04:00 is
        // on shift); at 03:30 the drive would outrun the 04:00 shift end, so it waits for Monday's 08:00 shift.
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(8, 2, 40), AtStop()).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(8, 3, 30), AtStop()).Kind);
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(8, 8), AtStop()).Kind);
    }

    [Fact]
    public void TodaysDriveBackGoesWhenTomorrowWasPlannedAtNoon()
    {
        // Wednesday 11:00-14:00, and Thursday's 00:30 stop whose restock starts Wednesday 22:40.
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840), Stop(DayOfWeek.Thursday, "night market", 30, 90));
        truck.Crew.Add("e2");
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Wednesday, 9, 15);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 14, 24);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Thursday, 0, 3);
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);
        truck.ReturnPending = true;
        runner.EnsurePlan(truck, At(3, 14), 0, 0, home: Bay);
        Assert.Contains(truck.Shifts, p => p.Day == 4);

        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 14), AtStop()).Kind);
        // Held until 16:00, after the planned drive back: the next leg is the restock, so it drives back.
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 16), AtStop()).Kind);
    }

    [Fact]
    public void AnOwedDriveBackWaitsWhenTheNextLegIsAStop()
    {
        // Stops 11:00-12:00 and 15:00-16:00 at the same place; between them nothing to do and the drive back owed.
        var truck = Truck(Stop(DayOfWeek.Wednesday, "a", 660, 720), Stop(DayOfWeek.Wednesday, "b", 900, 960));
        WeekTestData.AllDay(truck);
        runner.EnsurePlan(truck, At(3, 6), 0, 0, home: Bay);
        truck.ReturnPending = true;

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13), AtStop()).Kind);
    }

    [Fact]
    public void ARestockReplannedByAShiftEditIsntMadeTwice()
    {
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 18);
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 9, 10), InBay()).Kind);
        HireRunner.Restocked(truck, At(3, 9, 10));

        HireRunner.ReplanShifts(truck, At(3, 9, 15));
        runner.EnsurePlan(truck, At(3, 9, 15), Bay.X, Bay.Z, home: Bay);

        Assert.Equal(LegKind.Restock, HireRunner.Want(truck, At(3, 9, 20))!.Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9, 20), InBay()).Kind);
    }

    [Fact]
    public void OpeningAwayFromTheBayWithoutDrivingThereOwesTheDriveBack()
    {
        // Already parked at its Wednesday 11:00-14:00 pitch; shifts Wednesday 09:00-14:00 and Friday 09:00-12:00.
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 14);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Friday, 9, 12);
        runner.EnsurePlan(truck, At(3, 6), 0, 0, home: Bay);

        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 11), AtStop()).Kind);
        Assert.True(truck.ReturnPending);
        truck.IsOpen = true;
        truck.HireStop = 0;
        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(3, 14), AtStop()).Kind);
        truck.IsOpen = false;
        truck.HireStop = null;
        runner.EnsurePlan(truck, At(5, 9), 0, 0, home: Bay);
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex), (runner.Step(truck, At(5, 9), AtStop()).Kind, runner.Step(truck, At(5, 9), AtStop()).Stop));
    }

    [Fact]
    public void AWindowLoadedOpenAwayFromTheBayOwesTheDriveBack()
    {
        // Saved open at the Wednesday 11:00-14:00 pitch by a build that didn't arm the drive back on opening; shifts
        // Wednesday 09:00-14:00 and Friday 09:00-12:00.
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 14);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Friday, 9, 12);
        truck.IsOpen = true;
        truck.HireStop = 0;
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(truck);
        truck = codec.Load(codec.Serialize(state)).State!.Vehicles.Single();
        Assert.False(truck.ReturnPending);
        runner.EnsurePlan(truck, At(3, 12), 0, 0, home: Bay);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), AtStop()).Kind);
        Assert.True(truck.ReturnPending);
        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(3, 14), AtStop()).Kind);
        truck.IsOpen = false;
        truck.HireStop = null;
        runner.EnsurePlan(truck, At(5, 9), 0, 0, home: Bay);
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(5, 9), AtStop()).Kind);
    }

    [Fact]
    public void AWindowWithNobodyAbleToServeCloses()
    {
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 14);
        runner.EnsurePlan(truck, At(3, 6), 0, 0, home: Bay);
        truck.IsOpen = true;
        truck.HireStop = 0;
        var away = new VehicleWhere(0, 0, leaveAlone: false, home: Bay, canServe: false);

        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(3, 12), away).Kind);
        truck.IsOpen = false;
        truck.HireStop = null;
        Assert.NotEqual(HireActionKind.Open, runner.Step(truck, At(3, 12), away).Kind);
    }

    [Fact]
    public void ASecondShiftOnATakenRowIsRefusedAndTheSaveStillLoads()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 660, 840));
        truck.Crew.Add("e2");
        Assert.Equal(ShiftProblem.None, WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 9, 17, "serving1"));
        Assert.Equal(ShiftProblem.None, WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Monday, 8, 12));
        var before = truck.Schedule!.CrewShifts.Select(s => $"{s.EmployeeId} {s.Start}-{s.End} {s.Station}").ToList();

        Assert.Equal(ShiftProblem.Clash, WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Monday, 9, 17, "serving1"));

        Assert.Equal(before, truck.Schedule.CrewShifts.Select(s => $"{s.EmployeeId} {s.Start}-{s.End} {s.Station}"));
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(truck);
        Assert.Equal(LoadStatus.Loaded, codec.Load(codec.Serialize(state)).Status);
    }

    [Fact]
    public void ADriveBackThatWouldOutrunTheShiftWaits()
    {
        var truck = OutAtTheStop(shiftEnd: 16);
        // 15:00 plus 80 minutes is 16:20, after the shift: it waits (and the next shift takes it back).
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 15), AtStop()).Kind);
        Assert.True(truck.ReturnPending);
    }

    // Peter, 16 October 2026: the Schedule tab's day toggle closes the route day. Closed while the window is open at
    // the stop, the window closes and the drive back still goes with the shift.
    [Fact]
    public void ClosingTodayAtTheStopClosesTheWindowAndStillDrivesBack()
    {
        var truck = OutAtTheStop(shiftEnd: 18);
        truck.IsOpen = true;
        truck.HireStop = 0;
        Assert.Equal(ScheduleProblem.None, RouteEditor.SetClosed(schedules, truck, DayOfWeek.Wednesday, true));
        HireRunner.Replan(truck, At(3, 12));
        runner.EnsurePlan(truck, At(3, 12), 0, 0, home: Bay);

        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(3, 12), AtStop()).Kind);
        truck.IsOpen = false;
        truck.HireStop = null;
        Assert.True(truck.ReturnPending);
        var back = runner.Step(truck, At(3, 12), AtStop());
        Assert.Equal(HireActionKind.Depart, back.Kind);
        Assert.Equal(HireSchedules.HomeIndex, back.Stop);
    }

    [Fact]
    public void AReplanAfterTheLastStopKeepsTheDriveBack()
    {
        var truck = OutAtTheStop(shiftEnd: 18);
        HireRunner.Replan(truck, At(3, 14, 10));
        runner.EnsurePlan(truck, At(3, 14, 10), 0, 0, home: Bay);
        Assert.DoesNotContain(truck.Shifts.Single().Legs, l => l.StopIndex == HireSchedules.HomeIndex);

        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 14, 10), AtStop()).Kind);
    }

    [Fact]
    public void ADriveBackOwedOverDaysWithoutShiftsGoesWithTheNextShift()
    {
        var truck = OutAtTheStop(shiftEnd: 14);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Friday, 9, 12);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 15), AtStop()).Kind);
        runner.EnsurePlan(truck, At(4, 9), 0, 0, home: Bay);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(4, 9), AtStop()).Kind);

        // Saved and loaded on Thursday night: still owed.
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(truck);
        truck = codec.Load(codec.Serialize(state)).State!.Vehicles.Single();

        runner.EnsurePlan(truck, At(5, 9), 0, 0, home: Bay);
        var back = runner.Step(truck, At(5, 9), AtStop());
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex), (back.Kind, back.Stop));
    }

    [Fact]
    public void ADepartureForAStopNeedsTheShiftForTheWholeDrive()
    {
        // On shift 09:00-11:00 and 12:00-14:00 only; held by the player until 10:30, the 80-minute drive would end at 11:50.
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 11);
        truck.Crew.Add("e2");
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Wednesday, 12, 14);
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z);
        var away = new VehicleWhere(Bay.X, Bay.Z, leaveAlone: false);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 10, 30), away).Kind);
    }

    [Fact]
    public void TomorrowStartsBeforeMidnightWhenItsRestockDoes()
    {
        // Monday's 00:30 stop: restock from Sunday 22:40, leave at 23:10 (day 7 is a Sunday).
        var truck = Truck(Stop(DayOfWeek.Monday, "night market", 30, 90));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Sunday, 22, 24);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 0, 3);

        runner.EnsurePlan(truck, At(7, 21), Bay.X, Bay.Z, home: Bay);

        var monday = truck.Shifts.Single(p => p.Day == 8);
        Assert.Equal((LegKind.Restock, At(7, 22, 40)), (monday.Legs[0].Kind, monday.Legs[0].Start));
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(7, 22, 40), InBay()).Kind);
        Assert.Equal(WeekPlan.RouteDayOf(schedules, truck.Schedule, DayOfWeek.Monday, TravelMode.Drive, home: Bay).Start, -80);
        // An edit drops the early plan; it comes back.
        HireRunner.Replan(truck, At(7, 21, 30));
        Assert.DoesNotContain(truck.Shifts, p => p.Day == 8);
        runner.EnsurePlan(truck, At(7, 21, 30), Bay.X, Bay.Z, home: Bay);
        Assert.Contains(truck.Shifts, p => p.Day == 8);
    }

    [Fact]
    public void AShiftAddedAfterMidnightPicksUpYesterdaysLateStop()
    {
        // Sunday's stop 23:00-02:00 at the bay's door, staffed only until midnight at first.
        var truck = Truck(Stop(DayOfWeek.Sunday, "bars", 1380, 1560));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Sunday, 20, 24);
        runner.EnsurePlan(truck, At(7, 20), 0, 0);
        Assert.Equal(At(8, 0), truck.Shifts.Single().Legs.Last().End);

        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 0, 3);
        HireRunner.ReplanShifts(truck, At(8, 0, 10));
        runner.EnsurePlan(truck, At(8, 0, 10), 0, 0);

        var leg = HireRunner.Want(truck, At(8, 1))!;
        Assert.Equal((LegKind.Open, 0, At(8, 2)), (leg.Kind, leg.StopIndex, leg.End));
    }

    [Fact]
    public void APasteOverTheOpenStopNeverLeavesTheWindowAsThePlayers()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 660, 840), Stop(DayOfWeek.Tuesday, "b", 660, 840));
        WeekTestData.AllDay(truck);
        truck.IsOpen = true;
        truck.HireStop = 1;
        var copy = RouteEditor.CopyDay(truck.Schedule, DayOfWeek.Monday);
        Assert.True(RouteEditor.TakesOpenStop(truck, new[] { DayOfWeek.Tuesday }));
        Assert.False(RouteEditor.TakesOpenStop(truck, new[] { DayOfWeek.Friday }));

        Assert.Equal(ScheduleProblem.None, RouteEditor.PasteDay(schedules, truck, copy, DayOfWeek.Tuesday));

        Assert.Null(truck.HireStop);
        Assert.True(truck.CrewWindowLost);
        Assert.False(CrewPay.PlayerWindow(truck));
        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(2, 12), new VehicleWhere(0, 0, leaveAlone: false)).Kind);
        truck.IsOpen = false;
        runner.Step(truck, At(2, 12), new VehicleWhere(0, 0, leaveAlone: false));
        Assert.False(truck.CrewWindowLost);
    }

    [Fact]
    public void AnOpenWindowClosesWhenNobodyIsOnShift()
    {
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 14);
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        truck.IsOpen = true;
        truck.HireStop = 0;
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), new VehicleWhere(0, 0, leaveAlone: false)).Kind);
        // The shift is cut to 12:00 without a replan yet: it closes all the same.
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 12);
        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(3, 12, 30), new VehicleWhere(0, 0, leaveAlone: false)).Kind);
    }

    [Fact]
    public void BothProblemsAreReported()
    {
        // Two stops 1 km apart back to back (unreachable), nobody on shift (uncovered).
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 600, 720), Stop(DayOfWeek.Monday, "b", 720, 840));
        truck.Schedule!.Stops[1].X = 1000;

        var problems = new HireRunner(rules).RouteProblems(truck);

        Assert.Equal(new[] { ScheduleProblem.Unreachable, ScheduleProblem.Uncovered }, problems);
        Assert.Equal(ScheduleProblem.Unreachable, new HireRunner(rules).RouteProblem(truck));
    }
}
