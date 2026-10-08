using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class HireSchedulesTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireSchedules schedules = new(rules);

    private static ScheduleStop Stop(string key, double x, int open, int close) =>
        new() { SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static HireSchedule Route(params ScheduleStop[] stops) => new() { Stops = stops.ToList() };

    [Theory]
    [InlineData(1000, TravelMode.Drive, 1.0, 40)]
    [InlineData(1000, TravelMode.Drive, 2.0, 80)]
    [InlineData(12, TravelMode.Push, 1.0, 10)]
    [InlineData(100, TravelMode.Push, 1.0, 84)]
    [InlineData(0, TravelMode.Drive, 1.0, 0)]
    [InlineData(double.NaN, TravelMode.Drive, 1.0, 0)]
    public void TravelFollowsTheGamesTiming(double metres, TravelMode mode, double speed, int minutes)
    {
        Assert.Equal(minutes, Travel.Minutes(rules, metres, mode, speed));
    }

    [Fact]
    public void CartsArePushedAndTrucksDriven()
    {
        Assert.Equal(TravelMode.Push, Travel.ModeFor("cart"));
        Assert.Equal(TravelMode.Drive, Travel.ModeFor("truck"));
        // Saved before Milestone 4, when every vehicle was a cart.
        Assert.Equal(TravelMode.Push, Travel.ModeFor(""));
    }

    [Fact]
    public void DepartingClosesTheQueue()
    {
        var route = Route(Stop("office", 2000, 11 * 60, 14 * 60));
        var plan = schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, 0, TravelMode.Drive);
        var service = FoodTrucks.Core.Selling.SalesSimulator.Open(1);
        service.NextCustomerId = 2;
        service.Queue.Add(new ServiceCustomer { Id = 1, Patience = 20, Category = "fastfood" });
        var vehicle = new VehicleState { VehicleId = "t1", IsOpen = true, Service = service };

        var closed = HireSchedules.Depart(vehicle, route.Stops[0], plan.Legs[0]);

        Assert.Null(vehicle.Service);
        Assert.Equal(1, closed!.Count(FoodTrucks.Core.Selling.CustomerExit.Closed));
    }

    [Fact]
    public void TheTruckLeavesInTimeToOpenOnTime()
    {
        // 2 km to the first stop is 80 minutes; 3 km between stops is 120.
        var route = Route(Stop("office", 2000, 11 * 60, 14 * 60), Stop("bars", 5000, 18 * 60, 23 * 60));

        var plan = schedules.PlanDay(route, 3, DayOfWeek.Wednesday, 0, 0, HireSchedules.Absolute(3, 6 * 60), TravelMode.Drive);

        var day = HireSchedules.Absolute(3, 0);
        Assert.Collection(plan.Legs,
            l => Assert.Equal((LegKind.Travel, 0, day + 9 * 60 + 40, day + 11 * 60), (l.Kind, l.StopIndex, l.Start, l.End)),
            l => Assert.Equal((LegKind.Open, 0, day + 11 * 60, day + 14 * 60), (l.Kind, l.StopIndex, l.Start, l.End)),
            l => Assert.Equal((LegKind.Travel, 1, day + 16 * 60, day + 18 * 60), (l.Kind, l.StopIndex, l.Start, l.End)),
            l => Assert.Equal((LegKind.Open, 1, day + 18 * 60, day + 23 * 60), (l.Kind, l.StopIndex, l.Start, l.End)));
        Assert.Equal(5000, plan.Metres);
        Assert.Equal(23 * 60 - (9 * 60 + 40), plan.ShiftHours * 60, 9);
    }

    [Fact]
    public void ALateStartOpensOnArrival()
    {
        var route = Route(Stop("office", 2000, 11 * 60, 14 * 60));

        var plan = schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, HireSchedules.Absolute(1, 10 * 60 + 30), TravelMode.Drive);

        Assert.Equal(HireSchedules.Absolute(1, 11 * 60 + 50), plan.Legs[1].Start);
        Assert.Equal(HireSchedules.Absolute(1, 14 * 60), plan.Legs[1].End);
    }

    [Fact]
    public void AStopOutOfReachIsSkipped()
    {
        // Pushing a cart 1 km takes about 14 game hours.
        var route = Route(Stop("near", 50, 11 * 60, 14 * 60), Stop("far", 1050, 15 * 60, 18 * 60));

        var plan = schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, HireSchedules.Absolute(1, 8 * 60), TravelMode.Push);

        Assert.Equal(new[] { 1 }, plan.Skipped);
        Assert.All(plan.Legs, l => Assert.Equal(0, l.StopIndex));
    }

    [Fact]
    public void NoTravelLegWhenAlreadyThere()
    {
        var route = Route(Stop("here", 0, 11 * 60, 14 * 60));

        var plan = schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, 0, TravelMode.Drive);

        Assert.Single(plan.Legs);
        Assert.Equal(LegKind.Open, plan.Legs[0].Kind);
    }

    [Fact]
    public void ALateStopRunsPastMidnight()
    {
        var route = Route(Stop("bars", 0, 22 * 60, 26 * 60));

        var plan = schedules.PlanDay(route, 1, DayOfWeek.Friday, 0, 0, 0, TravelMode.Drive);

        Assert.Equal(HireSchedules.Absolute(2, 2 * 60), plan.Legs[0].End);
    }

    [Fact]
    public void DaysOffGiveNoPlan()
    {
        var route = Route(Stop("office", 0, 11 * 60, 14 * 60));
        route.Days = new List<DayOfWeek> { DayOfWeek.Monday };

        Assert.Empty(schedules.PlanDay(route, 2, DayOfWeek.Tuesday, 0, 0, 0, TravelMode.Drive).Legs);
        Assert.NotEmpty(schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, 0, TravelMode.Drive).Legs);
    }

    [Fact]
    public void LegAtFindsTheRunningLeg()
    {
        var route = Route(Stop("office", 2000, 11 * 60, 14 * 60));
        var plan = schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, 0, TravelMode.Drive);

        Assert.Equal(LegKind.Travel, plan.LegAt(10 * 60)!.Value.Kind);
        Assert.Equal(LegKind.Open, plan.LegAt(11 * 60)!.Value.Kind);
        Assert.Null(plan.LegAt(14 * 60));
        Assert.Null(plan.LegAt(9 * 60));
    }

    [Theory]
    [InlineData(ScheduleProblem.BadTimes, 600, 600)]
    [InlineData(ScheduleProblem.BadTimes, -1, 600)]
    [InlineData(ScheduleProblem.BadTimes, 1300, 1700)]
    [InlineData(ScheduleProblem.Overlap, 700, 900)]
    public void BadSchedulesAreRefused(ScheduleProblem problem, int open, int close)
    {
        var route = Route(Stop("a", 0, 660, 840), Stop("b", 0, open, close));
        if (problem == ScheduleProblem.BadTimes) route.Stops.RemoveAt(0);

        Assert.Equal(problem, schedules.Validate(route));
        Assert.Empty(schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, 0, TravelMode.Drive).Legs);
    }

    [Fact]
    public void OtherScheduleProblems()
    {
        Assert.Equal(ScheduleProblem.NoStops, schedules.Validate(new HireSchedule()));
        Assert.Equal(ScheduleProblem.EmptySpotKey, schedules.Validate(Route(Stop("", 0, 600, 700))));
        Assert.Equal(ScheduleProblem.BadPosition, schedules.Validate(Route(Stop("a", double.NaN, 600, 700))));
        var many = Route(Enumerable.Range(0, 7).Select(i => Stop($"s{i}", 0, i * 100, i * 100 + 50)).ToArray());
        Assert.Equal(ScheduleProblem.TooManyStops, schedules.Validate(many));
    }

    [Fact]
    public void TheVehicleIsInTransitUntilItArrives()
    {
        var route = Route(Stop("office", 2000, 11 * 60, 14 * 60));
        var plan = schedules.PlanDay(route, 1, DayOfWeek.Monday, 0, 0, 0, TravelMode.Drive);
        var vehicle = new VehicleState { VehicleId = "t1", IsOpen = true };

        HireSchedules.Depart(vehicle, route.Stops[0], plan.Legs[0]);

        Assert.False(vehicle.IsOpen);
        Assert.Null(HireSchedules.ArriveIfDue(vehicle, plan.Legs[0].End - 1));
        var arrived = HireSchedules.ArriveIfDue(vehicle, plan.Legs[0].End);
        Assert.Equal("office", arrived!.ToSpotKey);
        Assert.Equal(2000, arrived.ToX);
        Assert.Null(vehicle.Transit);
    }

    [Fact]
    public void ScheduleAndTransitSurviveASave()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(new VehicleState
        {
            VehicleId = "t1",
            Schedule = Route(Stop("office", 2000, 660, 840)),
            Transit = new TransitState { ToSpotKey = "office", DepartsAt = 5, ArrivesAt = 45 }
        });

        var loaded = codec.Load(codec.Serialize(state)).State!.Vehicles[0];

        Assert.Equal(840, loaded.Schedule!.Stops[0].CloseAt);
        Assert.Equal(45, loaded.Transit!.ArrivesAt);
    }

    [Fact]
    public void ATransitArrivingBeforeItLeavesIsCorrupt()
    {
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"t1\",\"transit\":{\"toSpotKey\":\"a\",\"departsAt\":10,\"arrivesAt\":5}}],\"dailySales\":[]}";

        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load(json).Status);
    }
}
