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
        new() { SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    // Day 3 is a Wednesday.
    private static long At(int day, int hour, int minute = 0) => HireSchedules.Absolute(day, hour * 60 + minute);

    private static VehicleState Truck(params ScheduleStop[] stops) => new()
    {
        VehicleId = "t1",
        VehicleType = "truck",
        HasPermit = true,
        Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = stops.ToList() }
    };

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
    public void ShiftMinutesCoverTheWholeShiftDrivingIncluded()
    {
        var truck = Truck(Stop("office", 2000, 11 * 60, 14 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        // The shift runs 9:40 to 14:00.
        Assert.Equal(20, HireRunner.ShiftMinutes(truck, At(3, 9), At(3, 10)));
        Assert.Equal(60, HireRunner.ShiftMinutes(truck, At(3, 12), At(3, 13)));
        Assert.Equal(0, HireRunner.ShiftMinutes(truck, At(3, 14), At(3, 15)));
    }

    [Fact]
    public void WeeklyWorkForTheGamesJobDemands()
    {
        var route = new HireSchedule
        {
            Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            Stops = new List<ScheduleStop> { Stop("a", 0, 11 * 60, 14 * 60), Stop("b", 0, 18 * 60, 22 * 60) }
        };

        var (hours, days) = HireRunner.WeeklyWork(route);

        Assert.Equal(22, hours);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, days);
        Assert.Equal(7, HireRunner.WeeklyWork(new HireSchedule { Stops = route.Stops }).Days.Count);
    }

    [Fact]
    public void WeeklyWorkCountsHoursAsThePayrollDoes()
    {
        // 11:30 to 12:30 covers half of two hours: two worked hours.
        var lunch = new HireSchedule { Days = new List<DayOfWeek> { DayOfWeek.Monday }, Stops = new List<ScheduleStop> { Stop("a", 0, 11 * 60 + 30, 12 * 60 + 30) } };
        Assert.Equal(2, HireRunner.WeeklyWork(lunch).Hours);

        // Friday 22:00 to 02:00 works Friday night and Saturday morning.
        var late = new HireSchedule { Days = new List<DayOfWeek> { DayOfWeek.Friday }, Stops = new List<ScheduleStop> { Stop("b", 0, 22 * 60, 26 * 60) } };
        var (hours, days) = HireRunner.WeeklyWork(late);
        Assert.Equal(4, hours);
        Assert.Equal(new[] { DayOfWeek.Friday, DayOfWeek.Saturday }, days);

        // Every day 01:00 to 27:00 (03:00): the nights overlap the next morning's hours, counted once.
        var allDay = new HireSchedule { Stops = new List<ScheduleStop> { Stop("c", 0, 60, 27 * 60) } };
        Assert.Equal(7 * 24, HireRunner.WeeklyWork(allDay).Hours);
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
        Assert.Null(truck.Schedule);
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
    public void MidnightsHourCountsFromAPreviewWithoutSavingIt()
    {
        var truck = Truck(Stop("early", 0, 0, 3 * 60));
        runner.EnsurePlan(truck, At(3, 6), 0, 0);

        Assert.Equal(60, runner.PlannedShiftMinutes(truck, At(4, 0), At(4, 1), 0, 0));
        Assert.DoesNotContain(truck.Shifts, p => p.Day == 4);
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
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"t1\",\"crew\":[\"e1\",\"e1\"]}],\"dailySales\":[]}";

        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load(json).Status);
    }
}
