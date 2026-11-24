using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class StopEditTests
{
    private static readonly HireSchedules schedules = new(ShippedData.Rules.Fleet);

    private static ScheduleStop Stop(DayOfWeek day, string key, double x, int open, int close) =>
        new() { Day = day, SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static VehicleState Cart(params ScheduleStop[] stops) => new()
    {
        VehicleId = "c1",
        VehicleType = "cart",
        Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = stops.ToList() }
    };

    private static VehicleState Week() => Cart(
        Stop(DayOfWeek.Monday, "office", 100, 11 * 60, 14 * 60),
        Stop(DayOfWeek.Monday, "park", 130, 16 * 60, 19 * 60),
        Stop(DayOfWeek.Tuesday, "office", 100, 11 * 60, 14 * 60),
        Stop(DayOfWeek.Tuesday, "bars", 400, 20 * 60, 26 * 60));

    private static Func<ScheduleStop, bool> Near(double x) => s => Math.Abs(s.X - x) <= 40;

    private static long At(int day, int minute) => (day - 1) * 1440L + minute;

    private static string Refusal(Action action) => Assert.Throws<StopEditRefused>(action).Message;

    [Fact]
    public void AnIndexNamesThatStopOfTheWeek()
    {
        var cart = Week();

        Assert.Equal(0, StopEdit.Pick(cart.Schedule, 0, null, null, null));
        Assert.Equal(3, StopEdit.Pick(cart.Schedule, 3, null, null, null));
    }

    [Fact]
    public void ADayAndAStopNumberNameThatDaysStop()
    {
        var cart = Week();

        Assert.Equal(1, StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, 1, null));
        Assert.Equal(2, StopEdit.Pick(cart.Schedule, null, DayOfWeek.Tuesday, 0, null));
        Assert.Equal(3, StopEdit.Pick(cart.Schedule, null, DayOfWeek.Tuesday, 1, null));
    }

    [Fact]
    public void ADayAndAnAddressNameTheOneStopByItThatDay()
    {
        var cart = Week();

        Assert.Equal(3, StopEdit.Pick(cart.Schedule, null, DayOfWeek.Tuesday, null, Near(400)));
        Assert.Equal(2, StopEdit.Pick(cart.Schedule, null, DayOfWeek.Tuesday, null, Near(90)));
    }

    [Fact]
    public void MixedIdentifiersAreRefused()
    {
        var cart = Week();

        foreach (var (day, stop, address) in new (DayOfWeek?, int?, Func<ScheduleStop, bool>?)[]
                 { (DayOfWeek.Monday, null, null), (null, 0, null), (null, null, Near(100)), (DayOfWeek.Monday, 0, null) })
            Assert.Contains("one way", Refusal(() => StopEdit.Pick(cart.Schedule, 0, day, stop, address)));
        Assert.Contains("not both", Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, 0, Near(100))));
    }

    [Fact]
    public void MissingIdentifiersAreRefused()
    {
        var cart = Week();

        Assert.Contains("Name the stop", Refusal(() => StopEdit.Pick(cart.Schedule, null, null, null, null)));
        Assert.Contains("need \"day\"", Refusal(() => StopEdit.Pick(cart.Schedule, null, null, 1, null)));
        Assert.Contains("need \"day\"", Refusal(() => StopEdit.Pick(cart.Schedule, null, null, null, Near(100))));
        Assert.Contains("give \"stop\" or \"address\"", Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, null, null)));
    }

    [Fact]
    public void AStopThatIsntThereIsRefused()
    {
        var cart = Week();

        Assert.Equal("No stop at index 4: the week has 4 stops (0 to 3).", Refusal(() => StopEdit.Pick(cart.Schedule, 4, null, null, null)));
        Assert.Equal("No stop 2 on monday: it has 2 stops (0 to 1).", Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, 2, null)));
        Assert.Equal("No stop 0 on sunday: it has no stops.", Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Sunday, 0, null)));
        Assert.Equal("No stop by that address on monday.", Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, null, Near(400))));
        Assert.Equal("No stop at index 0: the week has no stops.", Refusal(() => StopEdit.Pick(null, 0, null, null, null)));
        Assert.Equal("No stop at index 0: the week has no stops.", Refusal(() => StopEdit.Pick(Cart().Schedule, 0, null, null, null)));
    }

    [Fact]
    public void AnAddressWithTwoStopsThatDayIsAmbiguous()
    {
        var cart = Week();

        Assert.Equal("2 stops by that address on monday (week index 0, 1); name one with \"stop\" or \"index\".",
            Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, null, Near(115))));
    }

    [Fact]
    public void AMissingOpenOrCloseKeepsTheStopsOwn()
    {
        var stop = Stop(DayOfWeek.Monday, "office", 100, 11 * 60, 14 * 60);

        Assert.Equal((10 * 60, 14 * 60), StopEdit.Hours(stop, 10 * 60, null));
        Assert.Equal((11 * 60, 15 * 60), StopEdit.Hours(stop, null, 15 * 60));
        Assert.Equal((9 * 60, 12 * 60), StopEdit.Hours(stop, 9 * 60, 12 * 60));
        Assert.Contains("Give \"open\", \"close\" or both", Refusal(() => StopEdit.Hours(stop, null, null)));
    }

    [Fact]
    public void ACloseAtOrBeforeTheOpenIsAfterMidnightAsAddStopReadsIt()
    {
        var stop = Stop(DayOfWeek.Tuesday, "bars", 400, 20 * 60, 26 * 60);

        Assert.Equal((21 * 60, 26 * 60 + 30), StopEdit.Hours(stop, 21 * 60, 2 * 60 + 30));
        Assert.Equal((21 * 60, 21 * 60 + 1440), StopEdit.Hours(stop, 21 * 60, 21 * 60));
        Assert.Equal((20 * 60, 25 * 60), StopEdit.Hours(stop, null, 60));
        Assert.Equal((20 * 60, 23 * 60), StopEdit.Hours(stop, null, 23 * 60));
        Assert.Equal((22 * 60, 26 * 60), StopEdit.Hours(stop, 22 * 60, null));
    }

    [Fact]
    public void AnAcceptedEditStoresTheHoursAndFollowsTheStop()
    {
        var cart = Week();
        var bars = cart.Schedule!.Stops[3];

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 1, 15 * 60, 18 * 60, out var moved));
        Assert.Equal(1, moved);
        Assert.Equal((15 * 60, 18 * 60), (cart.Schedule.Stops[1].OpenAt, cart.Schedule.Stops[1].CloseAt));

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 3, 8 * 60, 10 * 60, out moved));
        Assert.Equal(2, moved);
        Assert.Same(bars, cart.Schedule.Stops[2]);
        Assert.Equal((8 * 60, 10 * 60), (bars.OpenAt, bars.CloseAt));
    }

    [Fact]
    public void ARefusedEditChangesNothingAndReportsTheProblem()
    {
        var cart = Week();
        cart.IsOpen = true;
        cart.HireStop = 1;
        var before = cart.Schedule!.Stops.Select(s => (s.Day, s.SpotKey, s.OpenAt, s.CloseAt)).ToList();

        Assert.Equal(ScheduleProblem.Overlap, StopEdit.Apply(schedules, cart, 1, 13 * 60, 18 * 60, out var moved));
        Assert.Equal(1, moved);
        Assert.Equal(ScheduleProblem.BadTimes, StopEdit.Apply(schedules, cart, 0, 15 * 60, 14 * 60, out moved));
        Assert.Equal(0, moved);
        Assert.Equal(ScheduleProblem.NoStops, StopEdit.Apply(schedules, Cart(), 0, 9 * 60, 10 * 60, out _));

        Assert.Equal(before, cart.Schedule.Stops.Select(s => (s.Day, s.SpotKey, s.OpenAt, s.CloseAt)).ToList());
        Assert.Equal(1, cart.HireStop);
        Assert.False(StopEdit.MustClose(cart, 1, At(1, 18 * 60)));
    }

    [Fact]
    public void TheOpenPitchClosesWhenItsNewCloseHasPassed()
    {
        var cart = Week();
        cart.IsOpen = true;
        cart.HireStop = 0;
        var now = At(1, 13 * 60);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 11 * 60, 15 * 60, out var moved));
        Assert.False(StopEdit.MustClose(cart, moved, now));

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 11 * 60, 12 * 60 + 30, out moved));
        Assert.Equal(0, cart.HireStop);
        Assert.True(StopEdit.MustClose(cart, moved, now));
        Assert.True(StopEdit.MustClose(cart, moved, At(1, 12 * 60 + 30)));
        Assert.False(StopEdit.MustClose(cart, moved, At(1, 12 * 60 + 29)));
    }

    [Fact]
    public void OnlyTheOpenPitchItselfIsClosed()
    {
        var cart = Week();
        cart.HireStop = 0;
        var now = At(1, 13 * 60);
        StopEdit.Apply(schedules, cart, 0, 11 * 60, 12 * 60, out var moved);

        Assert.False(StopEdit.MustClose(cart, moved, now));
        cart.IsOpen = true;
        cart.HireStop = 1;
        Assert.False(StopEdit.MustClose(cart, moved, now));
        cart.HireStop = null;
        Assert.False(StopEdit.MustClose(cart, moved, now));
    }

    [Fact]
    public void ALateStopFromYesterdayClosesOnceItsNewCloseAfterMidnightHasPassed()
    {
        var cart = Week();
        cart.IsOpen = true;
        cart.HireStop = 3;

        Assert.False(StopEdit.MustClose(cart, 3, At(3, 60)));
        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 3, 20 * 60, 24 * 60 + 30, out var moved));
        Assert.Equal(3, cart.HireStop);
        Assert.True(StopEdit.MustClose(cart, moved, At(3, 60)));
        Assert.False(StopEdit.MustClose(cart, moved, At(3, 29)));
        Assert.True(StopEdit.MustClose(cart, moved, At(4, 12 * 60)));
    }

    [Fact]
    public void AnAddressNamesOnlyTheStopAtItsOwnPitch()
    {
        var cart = Week();

        Assert.Equal(1, StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, null, StopEdit.AtSpot(130.5, -0.5)));
        Assert.Equal("No stop by that address on monday.",
            Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Monday, null, StopEdit.AtSpot(120, 0))));
        Assert.Equal("No stop by that address on tuesday.",
            Refusal(() => StopEdit.Pick(cart.Schedule, null, DayOfWeek.Tuesday, null, StopEdit.AtSpot(130, 0))));
    }

    [Fact]
    public void AnOpenStopTheEditMovesIsFollowedToItsNewIndex()
    {
        var cart = Week();
        cart.IsOpen = true;
        cart.HireStop = 1;
        var now = At(1, 12 * 60);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 1, 8 * 60, 10 * 60, out var moved));

        Assert.Equal(0, moved);
        Assert.Equal(0, cart.HireStop);
        Assert.True(StopEdit.MustClose(cart, moved, now));
        Assert.False(StopEdit.MustClose(cart, 1, now));
    }

    [Fact]
    public void TheCloseIsJudgedOnTheHoursTravelFittingLeaves()
    {
        var cart = Cart(Stop(DayOfWeek.Monday, "a", 0, 10 * 60, 12 * 60 + 30), Stop(DayOfWeek.Monday, "b", 1000, 13 * 60, 15 * 60));
        cart.IsOpen = true;
        cart.HireStop = 1;
        var edited = cart.Schedule!.Stops[1];
        var now = At(1, 13 * 60 + 5);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 1, 12 * 60 + 30, 13 * 60, out var moved));
        Assert.True(StopEdit.MustClose(cart, moved, now));
        RouteEditor.FitTravel(schedules, ShippedData.Rules.Fleet, cart.Schedule, TravelMode.Drive, 1.0, 30, ShippedData.Rules.Fleet.LatestCloseMinute);

        Assert.Equal((13 * 60 + 30, 14 * 60), (edited.OpenAt, edited.CloseAt));
        Assert.False(StopEdit.MustClose(cart, cart.Schedule.Stops.IndexOf(edited), now));
    }

    [Fact]
    public void ShorteningYesterdaysOpenStopAfterMidnightClosesTheCrewAtTheNewClose()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = WeekTestData.AllDay(Week());
        var bars = new VehicleWhere(400, 0, leaveAlone: false);
        runner.EnsurePlan(cart, At(2, 20 * 60), 400, 0);
        runner.EnsurePlan(cart, At(3, 15), 400, 0);
        cart.IsOpen = true;
        cart.HireStop = 3;
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(3, 31), bars).Kind);

        var now = At(3, 15);
        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 3, 20 * 60, 24 * 60 + 30, out var moved));
        Assert.False(StopEdit.MustClose(cart, moved, now));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now);
        runner.EnsurePlan(cart, now + 1, 400, 0);

        Assert.Equal(HireActionKind.None, runner.Step(cart, At(3, 29), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(3, 30), bars).Kind);
    }

    [Fact]
    public void ShorteningYesterdaysOpenStopNearItsBayKeepsTheCartOpenUntilTheNewClose()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = WeekTestData.AllDay(Cart(Stop(DayOfWeek.Monday, "bars", 400, 20 * 60, 26 * 60)));
        var bay = (X: 424.0, Z: 0.0);
        var bars = new VehicleWhere(400, 0, leaveAlone: false, home: bay);
        runner.EnsurePlan(cart, At(1, 20 * 60), 400, 0, home: bay);
        cart.IsOpen = true;
        cart.HireStop = 0;
        var now = At(2, 15);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);
        Assert.Equal(HireActionKind.None, runner.Step(cart, now, bars).Kind);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 20 * 60, 24 * 60 + 20, out var moved));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now, home: bay);
        Assert.False(StopEdit.MustClose(cart, moved, now));
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(HireActionKind.None, runner.Step(cart, now, bars).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 19), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 20), bars).Kind);
    }

    [Fact]
    public void LengtheningYesterdaysOpenStopKeepsTheCartOpenUntilTheNewClose()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = WeekTestData.AllDay(Cart(Stop(DayOfWeek.Monday, "bars", 400, 20 * 60, 25 * 60)));
        var bay = (X: 424.0, Z: 0.0);
        var bars = new VehicleWhere(400, 0, leaveAlone: false, home: bay);
        runner.EnsurePlan(cart, At(1, 20 * 60), 400, 0, home: bay);
        cart.IsOpen = true;
        cart.HireStop = 0;
        var now = At(2, 15);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 20 * 60, 26 * 60, out var moved));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now, home: bay);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 90), bars).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 119), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 120), bars).Kind);
    }

    [Fact]
    public void OnlyAnEditToYesterdaysStopForgetsYesterdaysPlanWhenTheCartIsNotServingIt()
    {
        var cart = WeekTestData.AllDay(Week());
        cart.Shifts = new List<ShiftPlan> { new() { Day = 2 }, new() { Day = 3 } };

        StopEdit.RetimeYesterday(schedules, cart, 0, At(3, 15));
        Assert.Equal(new[] { 2, 3 }, cart.Shifts.Select(p => p.Day).ToArray());
        StopEdit.RetimeYesterday(schedules, cart, 3, At(3, 15));
        Assert.Equal(new[] { 3 }, cart.Shifts.Select(p => p.Day).ToArray());
    }

    [Fact]
    public void ShorteningYesterdaysOpenStopStillDrivesBackToTheBayBeforeTodaysRoute()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = WeekTestData.AllDay(Cart(Stop(DayOfWeek.Monday, "bars", 400, 20 * 60, 26 * 60), Stop(DayOfWeek.Tuesday, "office", 100, 10 * 60, 14 * 60)));
        var bay = (X: 0.0, Z: 0.0);
        var bars = new VehicleWhere(400, 0, leaveAlone: false, home: bay);
        runner.EnsurePlan(cart, At(1, 20 * 60), 400, 0, home: bay);
        cart.IsOpen = true;
        cart.HireStop = 0;
        var now = At(2, 15);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 20 * 60, 24 * 60 + 20, out var moved));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now, home: bay);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 19), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 20), bars).Kind);
        cart.IsOpen = false;
        cart.HireStop = null;
        var back = runner.Step(cart, At(2, 20), bars);
        Assert.Equal(HireActionKind.Depart, back.Kind);
        Assert.Equal(HireSchedules.HomeIndex, back.Stop);
        Assert.Equal(LegKind.Restock, cart.Shifts!.Single(p => p.Day == 2).Legs[0].Kind);
    }

    [Fact]
    public void ALaterCrewShiftReopensYesterdaysEditedStop()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = Cart(Stop(DayOfWeek.Monday, "bars", 400, 20 * 60, 26 * 60 + 30), Stop(DayOfWeek.Tuesday, "office", 100, 10 * 60, 14 * 60));
        cart.Schedule!.CrewShifts = new List<CrewShift>
        {
            new() { EmployeeId = "e1", Day = DayOfWeek.Monday, Start = 0, End = 24, Station = WeekPlan.Serving(1) },
            new() { EmployeeId = "e1", Day = DayOfWeek.Tuesday, Start = 0, End = 1, Station = WeekPlan.Serving(1) },
            new() { EmployeeId = "e1", Day = DayOfWeek.Tuesday, Start = 2, End = 3, Station = WeekPlan.Serving(1) },
            new() { EmployeeId = "e1", Day = DayOfWeek.Tuesday, Start = 9, End = 24, Station = WeekPlan.Serving(1) },
        };
        var bars = new VehicleWhere(400, 0, leaveAlone: false);
        runner.EnsurePlan(cart, At(1, 20 * 60), 400, 0);
        cart.IsOpen = true;
        cart.HireStop = 0;
        var now = At(2, 15);
        runner.EnsurePlan(cart, now, 400, 0);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 20 * 60, 26 * 60 + 45, out var moved));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now);
        runner.EnsurePlan(cart, now, 400, 0);

        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 59), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 60), bars).Kind);
        cart.IsOpen = false;
        cart.HireStop = null;
        runner.EnsurePlan(cart, At(2, 120), 400, 0);
        var reopen = runner.Step(cart, At(2, 120), bars);
        Assert.Equal(HireActionKind.Open, reopen.Kind);
        Assert.Equal(0, reopen.Stop);
        cart.IsOpen = true;
        cart.HireStop = 0;
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 164), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 165), bars).Kind);
    }

    [Fact]
    public void EditingAnEarlierStopFromYesterdayKeepsTheLateStopServingNearItsBay()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = WeekTestData.AllDay(Cart(Stop(DayOfWeek.Monday, "park", 300, 18 * 60, 22 * 60), Stop(DayOfWeek.Monday, "bars", 400, 22 * 60 + 30, 24 * 60 + 40)));
        var bay = (X: 424.0, Z: 0.0);
        var bars = new VehicleWhere(400, 0, leaveAlone: false, home: bay);
        runner.EnsurePlan(cart, At(1, 18 * 60), 300, 0, home: bay);
        cart.IsOpen = true;
        cart.HireStop = 1;
        var now = At(2, 15);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 18 * 60, 21 * 60 + 30, out var moved));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now, home: bay);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(HireActionKind.None, runner.Step(cart, now, bars).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 39), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 40), bars).Kind);
    }

    [Fact]
    public void EditingAnEarlierStopFromYesterdayKeepsTheLateStopServingThroughItsFittedClose()
    {
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var cart = WeekTestData.AllDay(Cart(Stop(DayOfWeek.Monday, "park", 280, 18 * 60, 21 * 60 + 30), Stop(DayOfWeek.Monday, "bars", 400, 22 * 60, 24 * 60 + 40)));
        var late = cart.Schedule!.Stops[1];
        var bay = (X: 424.0, Z: 0.0);
        var bars = new VehicleWhere(400, 0, leaveAlone: false, home: bay);
        runner.EnsurePlan(cart, At(1, 18 * 60), 280, 0, home: bay);
        cart.IsOpen = true;
        cart.HireStop = 1;
        var now = At(2, 15);
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(ScheduleProblem.None, StopEdit.Apply(schedules, cart, 0, 18 * 60, 22 * 60, out var moved));
        RouteEditor.FitTravel(schedules, ShippedData.Rules.Fleet, cart.Schedule, TravelMode.Push, 1.0, 30, ShippedData.Rules.Fleet.LatestCloseMinute);
        Assert.Equal((22 * 60 + 30, 25 * 60 + 10), (late.OpenAt, late.CloseAt));
        HireRunner.Replan(cart, now);
        StopEdit.RetimeYesterday(schedules, cart, moved, now, home: bay);
        Assert.False(StopEdit.MustClose(cart, cart.HireStop!.Value, now));
        runner.EnsurePlan(cart, now, 400, 0, home: bay);

        Assert.Equal(HireActionKind.None, runner.Step(cart, now, bars).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 40), bars).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(cart, At(2, 69), bars).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(cart, At(2, 70), bars).Kind);
    }

    [Fact]
    public void TheReplyClockKeepsHoursAfterMidnight()
    {
        Assert.Equal("09:05", StopEdit.Clock(9 * 60 + 5));
        Assert.Equal("00:00", StopEdit.Clock(0));
        Assert.Equal("26:30", StopEdit.Clock(26 * 60 + 30));
    }
}
