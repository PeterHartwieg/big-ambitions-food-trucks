using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// Travel between stops in the route editor (Peter, 16 October 2026): a stop opens no earlier than the stop before closes
// plus the drive, rounded up to the half hour. Shipped rules: driving 0.04 min/m, pushing 1.2 m/s, latest close 27:00.
public class RouteTravelTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireSchedules schedules = new(rules);
    private const int Step = 30;

    private static ScheduleStop Stop(string key, double x, int open, int close) =>
        new() { SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static HireSchedule Route(params ScheduleStop[] stops) => new() { Stops = stops.ToList() };

    private static TravelFit Fit(HireSchedule route, TravelMode mode = TravelMode.Drive) =>
        RouteEditor.FitTravel(schedules, rules, route, mode, 1.0, Step, rules.LatestCloseMinute);

    [Fact]
    public void AShortDriveMovesTheNextStopToTheNextHalfHourAndKeepsItsLength()
    {
        // 1 km is 40 minutes: closing at 12:00, the next can open at 12:40, so 13:00.
        var route = Route(Stop("a", 0, 600, 720), Stop("b", 1000, 720, 840));

        var fit = Fit(route);

        var moved = Assert.Single(fit.Moved);
        Assert.Equal((1, 720, 840, 780, 900, 40), (moved.Index, moved.OldOpen, moved.OldClose, moved.NewOpen, moved.NewClose, moved.DriveMinutes));
        Assert.Equal((780, 900), (route.Stops[1].OpenAt, route.Stops[1].CloseAt));
        Assert.Null(fit.Stuck);
        Assert.Equal(-1, schedules.UnreachableStop(route, TravelMode.Drive));
    }

    [Fact]
    public void PetersRouteSavedBeforeTheRuleMovesItsSecondStop()
    {
        // A truck route played on 16 October 2026: 11:00-14:00, then 14:00-17:00 about a kilometre away. Fitted without
        // an edit (as the page opens and the crew plans), the second stop opens at 15:00 (14:40 rounded up) until 18:00.
        var route = Route(Stop("3rd avenue 13", 0, 660, 840), Stop("2nd avenue 7", 1000, 840, 1020));

        Assert.Equal(1, schedules.UnreachableStop(route, TravelMode.Drive));
        var fit = Fit(route);

        Assert.Equal(40, Assert.Single(fit.Moved).DriveMinutes);
        Assert.Equal((900, 1080), (route.Stops[1].OpenAt, route.Stops[1].CloseAt));
        Assert.Equal(-1, schedules.UnreachableStop(route, TravelMode.Drive));
        // A second fit finds nothing to move: the route is written once.
        Assert.Empty(Fit(route).Moved);
    }

    [Fact]
    public void AStopFarEnoughAfterTheDriveStaysWhereItIs()
    {
        var route = Route(Stop("a", 0, 600, 720), Stop("b", 1000, 780, 840));
        Assert.Empty(Fit(route).Moved);
        Assert.Equal(780, route.Stops[1].OpenAt);
    }

    [Fact]
    public void ALongDrivePushesTheLaterStopsToo()
    {
        // 3 km is 120 minutes: b moves from 12:00 to 14:00 (closing 16:00), which pushes c from 15:00 to 16:30.
        var route = Route(Stop("a", 0, 600, 720), Stop("b", 3000, 720, 840), Stop("c", 3500, 900, 1020));

        var fit = Fit(route);

        Assert.Equal(new[] { 1, 2 }, fit.Moved.Select(m => m.Index));
        Assert.Equal((840, 960), (route.Stops[1].OpenAt, route.Stops[1].CloseAt));
        Assert.Equal((990, 1110), (route.Stops[2].OpenAt, route.Stops[2].CloseAt));
    }

    [Fact]
    public void ACartIsPushedSoItsStopMovesFurtherThanATrucks()
    {
        // 100 m: a truck drives it in 4 minutes (12:30), a cart is pushed for 84 (13:30).
        var truck = Route(Stop("a", 0, 600, 720), Stop("b", 100, 720, 780));
        var cart = Route(Stop("a", 0, 600, 720), Stop("b", 100, 720, 780));

        Fit(truck, TravelMode.Drive);
        Fit(cart, TravelMode.Push);

        Assert.Equal(750, truck.Stops[1].OpenAt);
        Assert.Equal(810, cart.Stops[1].OpenAt);
    }

    [Fact]
    public void AStopWithNoRoomLeftIsReportedAndNothingChanges()
    {
        // The first stop runs to 26:30; 40 minutes on, the second would open at 27:30, past the latest close.
        var route = Route(Stop("a", 0, 600, 1590), Stop("b", 1000, 1590, 1620));

        var fit = Fit(route);

        Assert.Equal(1, fit.Stuck);
        Assert.Equal(ScheduleProblem.Unreachable, fit.Problem);
        Assert.Empty(fit.Moved);
        Assert.Equal((1590, 1620), (route.Stops[1].OpenAt, route.Stops[1].CloseAt));
    }

    [Fact]
    public void AfterAReorderTheTimesFollowTheNewOrder()
    {
        // Moving the far stop first: the near one, now second, is 2 km away and moves an hour and a half on.
        var vehicle = new VehicleState
        {
            VehicleId = "t1", VehicleType = "truck",
            Schedule = Route(Stop("near", 0, 600, 720), Stop("far", 2000, 780, 900))
        };
        Assert.Equal(ScheduleProblem.None, RouteEditor.MovePlace(schedules, vehicle, 1, 0));
        Assert.Equal(new[] { "far", "near" }, vehicle.Schedule!.Stops.Select(s => s.SpotKey));

        var fit = Fit(vehicle.Schedule);

        // far 10:00-12:00, then 80 minutes to near: 13:20, so 13:30.
        Assert.Equal((810, 930), (vehicle.Schedule.Stops[1].OpenAt, vehicle.Schedule.Stops[1].CloseAt));
        Assert.Single(fit.Moved);
    }

    [Fact]
    public void ThePickersOfferOnlyTimesTheDriveAllows()
    {
        var route = Route(Stop("a", 0, 600, 720), Stop("b", 1000, 840, 900));

        var times = RouteEditor.OpenTimes(route, Step, rules.LatestCloseMinute, except: 1,
            travelFrom: from => RouteEditor.TravelMinutes(rules, from, route.Stops[1], TravelMode.Drive));

        Assert.DoesNotContain(720, times);
        Assert.Contains(780, times);
        // Before the first stop nothing limits it.
        Assert.Contains(540, times);
    }

    [Fact]
    public void AnUnreachableStopIsTheRoutesProblemButItsCrewStillGetsAPlan()
    {
        var truck = new VehicleState
        {
            VehicleId = "t1", VehicleType = "truck", Crew = new List<string> { "e1" },
            Schedule = Route(Stop("a", 0, 600, 720), Stop("b", 1000, 720, 840))
        };
        var runner = new HireRunner(rules);

        Assert.Equal(1, schedules.UnreachableStop(truck.Schedule, TravelMode.Drive));
        Assert.Equal(ScheduleProblem.Unreachable, runner.RouteProblem(truck));
        runner.EnsurePlan(truck, HireSchedules.Absolute(3, 360), 0, 0);
        Assert.Contains(truck.Shifts.Single().Legs, l => l.Kind == LegKind.Open && l.StopIndex == 1);
    }
}

// The commissary at both ends of a route day (Peter, 16 October 2026): after the last stop the crew drives back to the
// bay where the vehicle rents its space; the next morning it leaves the bay in time for the first stop.
public class RouteHomeTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireSchedules schedules = new(rules);
    private static readonly HireRunner runner = new(rules);
    private static readonly (double X, double Z) Bay = (-2000, 0);

    private static ScheduleStop Stop(string key, double x, int open, int close) =>
        new() { SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static long At(int day, int hour, int minute = 0) => HireSchedules.Absolute(day, hour * 60 + minute);

    private static VehicleState Truck(params ScheduleStop[] stops) => new()
    {
        VehicleId = "t1", VehicleType = "truck", HasPermit = true, Crew = new List<string> { "e1" },
        Schedule = new HireSchedule { Stops = stops.ToList() }
    };

    [Fact]
    public void TheDayEndsWithTheDriveBackToTheBay()
    {
        var route = new HireSchedule { Stops = { Stop("office", 0, 660, 840), Stop("bars", 1000, 1080, 1380) } };

        var plan = schedules.PlanDay(route, 3, DayOfWeek.Wednesday, Bay.X, Bay.Z, At(3, 0), TravelMode.Drive, home: Bay);

        var back = plan.Legs.Last();
        // 3 km back from the bars at 23:00: 120 minutes.
        Assert.Equal((LegKind.Travel, HireSchedules.HomeIndex, At(3, 23), At(4, 1)), (back.Kind, back.StopIndex, back.Start, back.End));
        // The morning: 2 km from the bay is 80 minutes, leaving at 9:40 to open at 11:00.
        Assert.Equal((LegKind.Travel, 0, At(3, 9, 40)), (plan.Legs[0].Kind, plan.Legs[0].StopIndex, plan.Legs[0].Start));
        Assert.Equal(2000 + 1000 + 3000, plan.Metres, 6);
    }

    [Fact]
    public void NoSecondLegWhenTheLastStopIsTheBayAndNoneWithoutASpace()
    {
        var route = new HireSchedule { Stops = { Stop("office", 0, 660, 840), Stop("bay", Bay.X, 900, 930) } };

        var endsInBay = schedules.PlanDay(route, 3, DayOfWeek.Wednesday, 0, 0, At(3, 0), TravelMode.Drive, home: Bay, endsAtHome: true);
        var noSpace = schedules.PlanDay(route, 3, DayOfWeek.Wednesday, 0, 0, At(3, 0), TravelMode.Drive);

        Assert.DoesNotContain(endsInBay.Legs, l => l.StopIndex == HireSchedules.HomeIndex);
        Assert.DoesNotContain(noSpace.Legs, l => l.StopIndex == HireSchedules.HomeIndex);
        Assert.Equal(endsInBay.Legs.Count, noSpace.Legs.Count);
    }

    [Fact]
    public void TheCrewDrivesBackAndTheNextDayStartsFromTheBay()
    {
        var truck = Truck(Stop("office", 0, 660, 840));
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);
        var away = new VehicleWhere(0, 0, leaveAlone: false, home: Bay);

        // 14:00, the stop is over: back to the bay, 80 minutes.
        var leave = runner.Step(truck, At(3, 14), away);
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex, At(3, 15, 20)), (leave.Kind, leave.Stop, leave.ArrivesAt));
        var bay = new ScheduleStop { SpotKey = "bay", X = Bay.X, Z = Bay.Z };
        HireRunner.Depart(truck, leave, At(3, 14), 3, 0, 0, 0, bay);
        Assert.Equal(HireSchedules.HomeSpotKey, truck.Transit!.ToSpotKey);
        Assert.Null(truck.Transit.ToStopIndex);

        var arrive = runner.Step(truck, At(3, 15, 20), away);
        Assert.Equal(HireActionKind.Arrive, arrive.Kind);
        HireRunner.Arrived(truck, Bay.X, Bay.Z, atStop: true, At(3, 15, 20));
        // In the bay: nothing more to do.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 15, 30), new VehicleWhere(Bay.X, Bay.Z, leaveAlone: false, canRestock: true, home: Bay)).Kind);

        // The next day starts from the bay, where it stands.
        runner.EnsurePlan(truck, At(4, 0), Bay.X, Bay.Z, home: Bay);
        var morning = truck.Shifts.Single(p => p.Day == 4).Legs[0];
        Assert.Equal((LegKind.Travel, 0, At(4, 9, 40)), (morning.Kind, morning.StopIndex, morning.Start));
    }

    [Fact]
    public void ADriveBackRunningPastMidnightStartsTheNextDayFromTheBay()
    {
        // The bars close at 23:00 and the drive back (120 minutes) ends at 01:00: at midnight the vehicle is on its way, and
        // the new day is planned from the bay, not from where the game reads it.
        var truck = Truck(Stop("office", 0, 660, 840), Stop("bars", 1000, 1080, 1380));
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);

        runner.EnsurePlan(truck, At(4, 0), 12345, 0, home: Bay);

        var morning = truck.Shifts.Single(p => p.Day == 4).Legs[0];
        Assert.Equal((LegKind.Travel, 0, At(4, 9, 40)), (morning.Kind, morning.StopIndex, morning.Start));
    }

    [Fact]
    public void AVehicleAlreadyInTheBayOrKeptByThePlayerStays()
    {
        var truck = Truck(Stop("office", 0, 660, 840));
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);

        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 14), new VehicleWhere(Bay.X, 0, leaveAlone: false, canRestock: true, home: Bay)).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 14), new VehicleWhere(3, 0, leaveAlone: false, stayPut: true, home: Bay)).Kind);
        // Without a bay (no space) the day simply ends where the last stop was.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 14), new VehicleWhere(3, 0, leaveAlone: false)).Kind);
    }

    [Fact]
    public void TheDriveBackIsPaidLikeTheOtherDrives()
    {
        var truck = Truck(Stop("office", 0, 660, 840));
        var withBay = runner.PlannedShiftMinutes(truck, At(3, 0), At(4, 0), Bay.X, Bay.Z, home: Bay);
        var without = runner.PlannedShiftMinutes(truck, At(3, 0), At(4, 0), Bay.X, Bay.Z);

        // 80 minutes out, the stop, 80 minutes back.
        Assert.Equal(80 + 180 + 80, withBay);
        Assert.Equal(80 + 180, without);
    }

    [Fact]
    public void ALateStopsDriveBackEndsBeforeTheNightCheckOrIsOnItsWayThen()
    {
        var route = new HireSchedule { Stops = { Stop("bars", 0, 1080, 1440) } };
        var plan = schedules.PlanDay(route, 3, DayOfWeek.Wednesday, Bay.X, Bay.Z, At(3, 0), TravelMode.Drive, home: Bay);

        // Closing at midnight, 80 minutes back: in the bay at 01:20, before the 04:00 check.
        Assert.Equal(At(4, 1, 20), plan.Legs.Last().End);
        Assert.True(plan.Legs.Last().End < At(4, 4));
    }

    [Fact]
    public void ARouteChangeKeepsTheDriveBack()
    {
        var truck = Truck(Stop("office", 0, 660, 840), Stop("bars", 1000, 1080, 1380));
        runner.EnsurePlan(truck, At(3, 6), Bay.X, Bay.Z, home: Bay);

        RouteEditor.RemoveStop(truck, 0);

        Assert.Contains(truck.Shifts.Single().Legs, l => l.StopIndex == HireSchedules.HomeIndex);
        Assert.All(truck.Shifts.Single().Legs.Where(l => l.StopIndex != HireSchedules.HomeIndex), l => Assert.Equal(0, l.StopIndex));
    }

    [Fact]
    public void ASavedDriveBackLoadsAgain()
    {
        var state = new SaveState();
        var truck = StreetSales.GetOrAdd(state, "t1");
        truck.VehicleType = "truck";
        truck.Schedule = new HireSchedule { Stops = { Stop("office", 0, 660, 840) } };
        truck.Shifts = new List<ShiftPlan> { new() { Day = 3, Legs = { new ShiftLeg { Kind = LegKind.Travel, StopIndex = HireSchedules.HomeIndex, Start = 10, End = 20 } } } };
        var codec = new SaveCodec();

        var loaded = codec.Load(codec.Serialize(state));

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(HireSchedules.HomeIndex, loaded.State!.Vehicles.Single().Shifts.Single().Legs.Single().StopIndex);
    }
}
