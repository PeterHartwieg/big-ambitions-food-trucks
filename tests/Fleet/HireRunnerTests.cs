using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class HireRunnerTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireRunner runner = new(rules);

    private static ScheduleStop Stop(string key, double x, int open, int close) =>
        new() { Day = DayOfWeek.Wednesday, SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    // Day 3 is a Wednesday.
    private static long At(int day, int hour, int minute = 0) => HireSchedules.Absolute(day, hour * 60 + minute);

    // Stops on Wednesday; the crew on shift around the clock.
    private static VehicleState Truck(params ScheduleStop[] stops) => WeekTestData.AllDay(new VehicleState
    {
        VehicleId = "t1",
        VehicleType = "truck",
        HasPermit = true,
        Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = stops.ToList() }
    });

    private static VehicleWhere At(double x) => new(x, 0, leaveAlone: false);

    [Fact]
    public void AHiredTruckDrivesOpensClosesAndMovesOn()
    {
        // 2 km to the office is 80 minutes, 3 km on to the bars 120.
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60), Stop("bars", 5000, 18 * 60, 23 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9), At(0)).Kind);
        var leave = runner.Step(truck, At(3, 9, 40), At(0));
        Assert.Equal((HireActionKind.Depart, 0, At(3, 11)), (leave.Kind, leave.Stop, leave.ArrivesAt));
        HireRunner.Depart(truck, leave, At(3, 9, 40), 0, 0, 0, 0);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 10, 59), At(0)).Kind);
        var arrive = runner.Step(truck, At(3, 11), At(0));
        Assert.Equal(HireActionKind.Arrive, arrive.Kind);
        Assert.Equal("office", arrive.Transit!.ToSpotKey);
        HireRunner.Arrived(truck, 2003, 0, atStop: true, At(3, 11));

        var open = runner.Step(truck, At(3, 11), At(2003));
        Assert.Equal((HireActionKind.Open, 0), (open.Kind, open.Stop));
        truck.IsOpen = true;
        truck.HireStop = 0;
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13), At(2003)).Kind);
        Assert.Equal(HireActionKind.Close, runner.Step(truck, At(3, 14), At(2003)).Kind);
        truck.IsOpen = false;
        truck.HireStop = null;

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 15), At(2000)).Kind);
        var next = runner.Step(truck, At(3, 16), At(2000));
        Assert.Equal((HireActionKind.Depart, 1, At(3, 18)), (next.Kind, next.Stop, next.ArrivesAt));
    }

    [Fact]
    public void TravelIsTimedFromWhereTheVehicleReallyIs()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        // The player left it 1 km from the stop during opening hours: 40 minutes from now.
        var leave = runner.Step(truck, At(3, 12), At(1000));

        Assert.Equal(At(3, 12, 40), leave.ArrivesAt);
    }

    [Fact]
    public void ThePlayerKeepsWhatTheyHaveOrOpened()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), new VehicleWhere(0, 0, leaveAlone: true)).Kind);
        truck.IsOpen = true;
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), At(0)).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 16), At(0)).Kind);
    }

    [Fact]
    public void NoCrewNoRoute()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60));
        truck.Crew.Clear();
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        Assert.Empty(truck.Shifts);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), At(0)).Kind);
    }

    [Fact]
    public void AHireTakenOnMidDaySkipsWhatIsOver()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60), Stop("bars", 0, 18 * 60, 23 * 60));
        runner.EnsurePlan(truck, At(3, 15), 0, 0);

        Assert.Equal(At(3, 18), truck.Shifts.Single().Legs.Single().Start);
        Assert.Equal(1, truck.Shifts.Single().Legs.Single().StopIndex);
    }

    [Fact]
    public void ALateStopKeepsRunningPastMidnightAndTheNextDayStartsAfterIt()
    {
        var truck = Truck(Stop("bars", 0, 22 * 60, 26 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        runner.EnsurePlan(truck, At(4, 0, 30), 0, 0);

        Assert.Equal(new[] { 3, 4 }, truck.Shifts.Select(p => p.Day).ToArray());
        Assert.Equal(0, HireRunner.Want(truck, At(4, 1))!.StopIndex);
        runner.EnsurePlan(truck, At(4, 3), 0, 0);
        Assert.Equal(new[] { 4 }, truck.Shifts.Select(p => p.Day).ToArray());
    }

    [Fact]
    public void WithNobodyOnShiftTheTruckWaitsWhereItIsUntilAShiftStarts()
    {
        // The shift starts at 10:00, after the planned 9:40 departure: it leaves at 10:00 and opens on arrival at 11:20.
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        truck.Schedule!.CrewShifts.Clear();
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 10, 13);
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        var legs = truck.Shifts.Single().Legs;
        Assert.Equal((LegKind.Travel, At(3, 10), At(3, 11, 20)), (legs[0].Kind, legs[0].Start, legs[0].End));
        // Open only while someone is on shift: until 13:00, not the stop's 14:00.
        Assert.Equal((LegKind.Open, At(3, 11, 20), At(3, 13)), (legs[1].Kind, legs[1].Start, legs[1].End));
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9, 40), At(0)).Kind);
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 10), At(0)).Kind);
    }

    [Fact]
    public void AStopWithNobodyOnShiftAtAllIsSkipped()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60), Stop("bars", 0, 18 * 60, 22 * 60));
        truck.Schedule!.CrewShifts.Clear();
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 17, 23);
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        var plan = truck.Shifts.Single();
        Assert.Equal(new[] { 1 }, plan.Legs.Select(l => l.StopIndex).Distinct().ToArray());
    }

    [Fact]
    public void WeeklyWorkForTheGamesJobDemandsFollowsTheShifts()
    {
        var truck = Truck(Stop("a", 0, 11 * 60, 14 * 60));
        truck.Crew.Add("e2");
        truck.Schedule!.CrewShifts.Clear();
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 10, 15);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Friday, 17, 23);
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Friday, 18, 24);

        var (hours, days) = WeekPlan.WeeklyWork(truck.Schedule, "e1");

        Assert.Equal(11, hours);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, days);
        Assert.Equal(6, WeekPlan.WeekHours(truck.Schedule, "e2"));
        Assert.Equal(0, WeekPlan.WeekHours(truck.Schedule, "nobody"));
    }

    [Fact]
    public void ShiftsAreWholeHoursUpToTwelveOnePerDay()
    {
        var truck = Truck(Stop("a", 0, 11 * 60, 14 * 60));
        truck.Schedule!.CrewShifts.Clear();

        Assert.Equal(ShiftProblem.TooLong, WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 6, 19));
        Assert.Equal(ShiftProblem.BadHours, WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 20, 25));
        Assert.Equal(ShiftProblem.BadHours, WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 9, 9));
        Assert.Equal(ShiftProblem.NotCrew, WeekPlan.SetShift(rules, truck, "stranger", DayOfWeek.Monday, 9, 10));
        Assert.Equal(ShiftProblem.None, WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 12, 24));
        Assert.Equal(ShiftProblem.None, WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 8, 12));

        Assert.Equal((8, 12), (truck.Schedule.CrewShifts.Single().Start, truck.Schedule.CrewShifts.Single().End));
        // Monday is day 1: on shift 08:00 to 12:00, not at 12:00.
        Assert.True(WeekPlan.OnShift(truck.Schedule, "e1", At(1, 8)));
        Assert.False(WeekPlan.OnShift(truck.Schedule, "e1", At(1, 12)));
        Assert.True(WeekPlan.OnShift(truck.Schedule, "e1", At(8, 11, 59)));
    }

    [Fact]
    public void StopsAreAddedInTimeOrderAndOverlapsRefused()
    {
        var schedules = new HireSchedules(rules);
        var route = new HireSchedule();

        Assert.Equal(ScheduleProblem.None, RouteEditor.AddStop(schedules, route, Stop("bars", 0, 18 * 60, 23 * 60)));
        Assert.Equal(ScheduleProblem.None, RouteEditor.AddStop(schedules, route, Stop("office", 0, 11 * 60, 14 * 60)));
        Assert.Equal(ScheduleProblem.Overlap, RouteEditor.AddStop(schedules, route, Stop("late lunch", 0, 13 * 60, 15 * 60)));

        Assert.Equal(new[] { "office", "bars" }, route.Stops.Select(s => s.SpotKey).ToArray());
    }

    [Fact]
    public void ATruckParkedFurtherOutForItsStopCountsAsThere()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        HireRunner.Depart(truck, runner.Step(truck, At(3, 9, 40), At(0)), At(3, 9, 40), 0, 0, 0, 0);

        // The nearest free space was 50 m on, beyond the 25 m stop radius.
        HireRunner.Arrived(truck, 2050, 0, atStop: true, At(3, 11));

        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 11), At(2050)).Kind);
        // Moved away by someone else, it goes back.
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 11), At(2080)).Kind);
    }

    [Fact]
    public void AStopWithNoRoomIsGivenUpUntilItCloses()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60), Stop("bars", 4000, 18 * 60, 23 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        HireRunner.Depart(truck, runner.Step(truck, At(3, 9, 40), At(0)), At(3, 9, 40), 0, 0, 0, 0);

        // No parking near the office: back where it left from.
        HireRunner.Arrived(truck, 0, 0, atStop: false, At(3, 11, 20));

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), At(0)).Kind);
        runner.EnsurePlan(truck, At(3, 14), 0, 0);
        Assert.Null(truck.Skipped);
        // The plan leaves the office for the bars at 16:40 (2 km, 80 minutes).
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 16, 40), At(0)).Kind);
    }

    [Fact]
    public void EditingTheRouteKeepsIndicesPointingAtTheSameStops()
    {
        var schedules = new HireSchedules(rules);
        var truck = Truck(Stop("bars", 0, 22 * 60, 26 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        runner.EnsurePlan(truck, At(4, 0, 30), 0, 0);
        truck.IsOpen = true;
        truck.HireStop = 0;

        // An earlier stop goes in front of the late one after midnight.
        Assert.Equal(ScheduleProblem.None, RouteEditor.AddStop(schedules, truck, Stop("office", 0, 11 * 60, 14 * 60)));

        Assert.Equal(1, truck.HireStop);
        Assert.Equal("bars", truck.Schedule!.Stops[HireRunner.Want(truck, At(4, 1))!.StopIndex].SpotKey);

        RouteEditor.RemoveStop(truck, 0);
        Assert.Equal(0, truck.HireStop);
        Assert.Equal("bars", truck.Schedule!.Stops[HireRunner.Want(truck, At(4, 1))!.StopIndex].SpotKey);
        RouteEditor.RemoveStop(truck, 0);
        Assert.Null(truck.HireStop);
        // The shifts keep the week plan.
        Assert.Empty(truck.Schedule!.Stops);
        Assert.Null(HireRunner.Want(truck, At(4, 1)));
    }

    [Fact]
    public void ThePlayerNearbyOrNoStockKeepsTheCrewFromDrivingOff()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), new VehicleWhere(0, 0, leaveAlone: false, stayPut: true)).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), new VehicleWhere(0, 0, leaveAlone: false, hasStock: false)).Kind);
        // At the stop, staying put doesn't stop it opening.
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 12), new VehicleWhere(2000, 0, leaveAlone: false, stayPut: true)).Kind);
    }

    [Fact]
    public void WouldMoveSaysTheCrewWaitsOnlyForThePlayer()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        var where = new VehicleWhere(0, 0, leaveAlone: false, stayPut: true);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), where).Kind);
        // Without the player by it, the crew would set off now.
        Assert.True(runner.WouldMove(truck, At(3, 12), where.WithStayPut(false)));
        // At the stop it opens rather than moves; with nothing to sell it wouldn't leave either.
        Assert.False(runner.WouldMove(truck, At(3, 12), new VehicleWhere(2000, 0, leaveAlone: false)));
        Assert.False(runner.WouldMove(truck, At(3, 12), new VehicleWhere(0, 0, leaveAlone: false, hasStock: false)));
    }

    [Fact]
    public void ALegOutOfRangeIsDroppedOnEdit()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60));
        truck.Shifts.Add(new ShiftPlan { Day = 3, Legs = new List<ShiftLeg> { new() { Kind = LegKind.Open, StopIndex = 5, Start = 1, End = 2 } } });

        Assert.Equal(ScheduleProblem.None, RouteEditor.AddStop(new HireSchedules(rules), truck, Stop("bars", 0, 18 * 60, 22 * 60)));

        Assert.Empty(truck.Shifts.Single().Legs);
    }

    [Fact]
    public void WhereTheCrewParkedItIsTheStopEvenIfItCantSellThere()
    {
        var cart = Truck(Stop("corner", 0, 11 * 60, 14 * 60));
        cart.VehicleType = "cart";
        runner.EnsurePlan(cart, At(3, 6), 0, 0);
        cart.Arrival = new ArrivalState { SpotKey = "corner", X = 0, Z = 0 };

        var step = runner.Step(cart, At(3, 12), new VehicleWhere(0, 0, leaveAlone: false, canSellHere: false));

        Assert.Equal(HireActionKind.Open, step.Kind);
    }

    [Fact]
    public void GivingUpAfterTheStopClosedDoesntCostTomorrowsVisit()
    {
        var truck = Truck(Stop("bars", 2000, 22 * 60, 23 * 60 + 30));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        HireRunner.Depart(truck, runner.Step(truck, At(3, 21), At(0)), At(3, 21), 0, 0, 0, 0);
        runner.EnsurePlan(truck, At(4, 0, 30), 0, 0);

        // No parking until well after midnight: back where it left from.
        HireRunner.Arrived(truck, 0, 0, atStop: false, At(4, 0, 30));

        Assert.True(truck.Skipped!.Until <= At(4, 0, 31));
    }

    [Fact]
    public void EditingTheRouteWhileOpenKeepsTheWindowOpen()
    {
        var schedules = new HireSchedules(rules);
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        truck.IsOpen = true;
        truck.HireStop = 0;
        truck.Arrival = new ArrivalState { SpotKey = "office", X = 2040, Z = 0 };

        RouteEditor.AddStop(schedules, truck, Stop("bars", 4000, 18 * 60, 23 * 60));
        HireRunner.Replan(truck, At(3, 12));
        // Planned from where the truck stands, 40 m out: a short leg to the stop it is open at.
        runner.EnsurePlan(truck, At(3, 12), 2040, 0);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), At(2040)).Kind);
    }

    [Fact]
    public void PlansStartFromTheStopOnlyWhileTheVehicleIsWhereTheCrewLeftIt()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        truck.Arrival = new ArrivalState { SpotKey = "office", X = 2040, Z = 0 };

        Assert.Equal((2000.0, 0.0), HireRunner.PlanFrom(truck, 2041, 0));
        Assert.Equal((500.0, 0.0), HireRunner.PlanFrom(truck, 500, 0));
    }

    [Fact]
    public void AStopGivenUpAfterAnErrorStaysClosedUntilItsHoursEnd()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        HireRunner.GiveUp(truck, 0, At(3, 12));

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 12), At(0)).Kind);
        Assert.Equal(At(3, 14), truck.Skipped!.Until);
    }

    [Fact]
    public void AnErrorAtClosingTimeGivesNothingUp()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60), Stop("office", 0, 17 * 60, 20 * 60));
        truck.Schedule!.Stops[1].SpotKey = "office";
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        HireRunner.GiveUp(truck, 0, At(3, 14));

        Assert.Null(truck.Skipped);
    }

    [Fact]
    public void AVehicleThatCantSellWhereItStandsIsMoved()
    {
        var truck = Truck(Stop("office", 0, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        var step = runner.Step(truck, At(3, 12), new VehicleWhere(5, 0, leaveAlone: false, canSellHere: false));

        Assert.Equal(HireActionKind.Depart, step.Kind);
    }

    [Fact]
    public void NoDepartureThatArrivesAfterTheStopCloses()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60), Stop("bars", 2000, 18 * 60, 23 * 60));
        runner.EnsurePlan(truck, At(3, 6), 2000, 0);

        // The player left it 2 km away at 13:00: 80 minutes back, after 14:00.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13), At(0)).Kind);
    }

    [Fact]
    public void ReplanningKeepsALateStopRunningPastMidnight()
    {
        var truck = Truck(Stop("bars", 0, 22 * 60, 26 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        runner.EnsurePlan(truck, At(4, 0, 30), 0, 0);

        HireRunner.Replan(truck, At(4, 0, 30));

        Assert.Equal(0, HireRunner.Want(truck, At(4, 1))!.StopIndex);
    }

    [Fact]
    public void ATeamServesAtItsMeanSpeed()
    {
        var service = ShippedData.Rules.Service;

        var team = service.Team(new double[] { 0, 100 });

        Assert.Equal((service.Speed(0) + service.Speed(100)) / 2, team.Speed, 9);
        Assert.Equal(50, team.Skill);
        Assert.Equal(1.0, service.Team(Array.Empty<double>()).Speed);
    }

    [Fact]
    public void CrewAndShiftsSurviveASave()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        truck.HireStop = 0;
        state.Vehicles.Add(truck);

        var loaded = codec.Load(codec.Serialize(state)).State!.Vehicles[0];

        Assert.Equal(new[] { "e1" }, loaded.Crew);
        Assert.Equal(LegKind.Open, loaded.Shifts[0].Legs[1].Kind);
        Assert.Equal(0, loaded.HireStop);
    }

    [Fact]
    public void ADuplicateCrewMemberIsCorrupt()
    {
        var json = "{\"schemaVersion\":2,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"t1\",\"crew\":[\"e1\",\"e1\"]}],\"dailySales\":[]}";

        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load(json).Status);
    }
}
