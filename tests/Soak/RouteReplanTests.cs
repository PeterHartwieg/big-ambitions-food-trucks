using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Soak;

public class RouteReplanTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public void AnOpenWindowKeepsServingDuringAReplanApproachWithinItsHours()
    {
        // Seed 78, day 43: the truck opened within the stop radius without an Arrival
        // placement. Editing a different stop at 08:00 replans from its real position.
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var schedules = new HireSchedules(ShippedData.Rules.Fleet);
        var at = HireSchedules.Absolute(43, 480);
        // Day 43 is a Monday.
        var truck = new VehicleState
        {
            VehicleId = "v2", VehicleType = "truck", Crew = new() { "e" },
            IsOpen = true, HireStop = 0,
            Schedule = new HireSchedule { Stops = new()
            {
                new() { Day = System.DayOfWeek.Monday, SpotKey = "morning", X = 0, OpenAt = 420, CloseAt = 510 },
                new() { Day = System.DayOfWeek.Monday, SpotKey = "evening", X = 100, OpenAt = 1140, CloseAt = 1290 }
            } }
        };
        FoodTrucks.Core.Tests.Fleet.WeekTestData.AllDay(truck);
        Assert.Equal(ScheduleProblem.None, RouteEditor.SetTimes(schedules, truck, 1, 1110, 1290));
        HireRunner.Replan(truck, at);
        var from = HireRunner.PlanFrom(truck, 20, 0);
        runner.EnsurePlan(truck, at, from.X, from.Z);
        Assert.Equal(LegKind.Travel, HireRunner.Want(truck, at)!.Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, at, new VehicleWhere(20, 0, false)).Kind);
        Assert.True(SoakWorld.RouteSaleAllowed(truck, at));
        Assert.False(SoakWorld.RouteSaleAllowed(truck, HireSchedules.Absolute(43, 510)));

        // The exception cannot permit actual transit, or approach before the stop opens.
        truck.Transit = new TransitState();
        Assert.False(SoakWorld.RouteSaleAllowed(truck, at));
        truck.Transit = null;
        Assert.Equal(ScheduleProblem.None, RouteEditor.SetTimes(schedules, truck, 0, 481, 510));
        HireRunner.Replan(truck, at);
        runner.EnsurePlan(truck, at, from.X, from.Z);
        Assert.Equal(LegKind.Travel, HireRunner.Want(truck, at)!.Kind);
        Assert.False(SoakWorld.RouteSaleAllowed(truck, at));
    }

    [Fact]
    [Trait("Category", "Soak")]
    public void ALateStopReplannedAfterMidnightKeepsServingWithinItsHours()
    {
        // Seed 35, day 24: a bay stop open 23:30 to 02:30 since Monday 23:31; the shifts scheduled again at midnight
        // replan the rest of Monday from the bay, with a restock leg first.
        var runner = new HireRunner(ShippedData.Rules.Fleet);
        var truck = new VehicleState
        {
            VehicleId = "v0", VehicleType = "truck", Crew = new() { "e" },
            IsOpen = true, HireStop = 0,
            Schedule = new HireSchedule { Stops = new()
            {
                new() { Day = System.DayOfWeek.Monday, SpotKey = "bay", X = 0, OpenAt = 1410, CloseAt = 1590 }
            } }
        };
        FoodTrucks.Core.Tests.Fleet.WeekTestData.AllDay(truck);
        var midnight = HireSchedules.Absolute(44, 0);
        HireRunner.ReplanShifts(truck, midnight);
        runner.EnsurePlan(truck, midnight, 0, 0, home: (0, 0));
        Assert.Equal(LegKind.Restock, HireRunner.Want(truck, midnight + 3)!.Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, midnight + 3, new VehicleWhere(0, 0, false, canRestock: true, home: (0, 0))).Kind);
        Assert.True(SoakWorld.RouteSaleAllowed(truck, midnight + 3));
        Assert.False(SoakWorld.RouteSaleAllowed(truck, HireSchedules.Absolute(44, 150)));
    }
}
