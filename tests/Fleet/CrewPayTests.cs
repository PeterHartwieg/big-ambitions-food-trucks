using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class CrewPayTests
{
    private static long At(int day, int hour, int minute = 0) => HireSchedules.Absolute(day, hour * 60 + minute);

    private static VehicleState Truck(bool open = false, int? hireStop = null) => new()
    {
        VehicleId = "t1",
        VehicleType = "truck",
        Crew = new List<string> { "e1" },
        IsOpen = open,
        HireStop = hireStop
    };

    private static void Leg(VehicleState vehicle, long start, long end) =>
        vehicle.Shifts.Add(new ShiftPlan { Day = HireRunner.DayOf(start), Legs = { new ShiftLeg { Kind = LegKind.Open, StopIndex = 0, Start = start, End = end } } });

    [Fact]
    public void CrewServeAWindowThePlayerOpenedWithNoRoute()
    {
        var truck = Truck(open: true);

        Assert.True(CrewPay.OnShift(truck, At(3, 15)));
        Assert.True(CrewPay.PlayerWindow(truck));
    }

    [Fact]
    public void AClosedVehicleHasItsCrewOnlyInRouteHours()
    {
        var truck = Truck();
        Assert.False(CrewPay.OnShift(truck, At(3, 12)));

        Leg(truck, At(3, 11), At(3, 14));
        Assert.True(CrewPay.OnShift(truck, At(3, 12)));
        Assert.False(CrewPay.OnShift(truck, At(3, 15)));
    }

    [Fact]
    public void NobodyServesOnTheWayOrWithoutCrew()
    {
        var moving = Truck(open: true);
        moving.Transit = new TransitState { ToSpotKey = "s", DepartsAt = 0, ArrivesAt = 10 };
        Assert.False(CrewPay.OnShift(moving, At(3, 12)));

        var empty = Truck(open: true);
        empty.Crew.Clear();
        Assert.False(CrewPay.OnShift(empty, At(3, 12)));
        Assert.False(CrewPay.PlayerWindow(empty));
    }

    [Fact]
    public void ARouteWindowIsPaidByTheRouteNotAsAnOpenHour()
    {
        var truck = Truck(open: true, hireStop: 0);

        Assert.True(CrewPay.OnShift(truck, At(3, 12)));
        Assert.False(CrewPay.PlayerWindow(truck));
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 12), 0));
    }

    [Fact]
    public void AnOpenHourIsDueInItsFirstHalfOnly()
    {
        var truck = Truck(open: true);

        Assert.True(CrewPay.OpenHourDue(truck, At(3, 15), 0));
        Assert.True(CrewPay.OpenHourDue(truck, At(3, 15, 29), 0));
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 15, 30), 0));
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 15, 59), 0));
    }

    [Fact]
    public void AnHourTheRoutePaysIsNotAnOpenHour()
    {
        var truck = Truck(open: true);

        Assert.False(CrewPay.OpenHourDue(truck, At(3, 15, 10), 30));
        Assert.True(CrewPay.OpenHourDue(truck, At(3, 15, 10), 29));
    }

    [Fact]
    public void EachHireIsPaidOncePerHour()
    {
        var state = new SaveState();
        var hour = CrewPay.HourOf(At(3, 15, 10));

        Assert.True(CrewPay.TryPay(state, "e1", hour));
        // The route's count, a second window or a move to another vehicle in the same hour adds nothing.
        Assert.False(CrewPay.TryPay(state, "e1", hour));
        Assert.False(CrewPay.Unpaid(state, "e1", hour));
        Assert.True(CrewPay.TryPay(state, "e2", hour));
        Assert.True(CrewPay.TryPay(state, "e1", hour + 1));
        // An earlier hour never comes round again.
        Assert.False(CrewPay.TryPay(state, "e1", hour));
    }

    [Fact]
    public void HoursAreCountedByTheGameDay()
    {
        Assert.Equal(2 * 24 + 15, CrewPay.HourOf(At(3, 15, 59)));
        Assert.Equal(3 * 24, CrewPay.HourOf(At(4, 0)));
    }

    [Fact]
    public void OldPaidHoursArePruned()
    {
        var state = new SaveState();
        CrewPay.TryPay(state, "e1", 10);
        CrewPay.TryPay(state, "e2", 100);

        CrewPay.Prune(state, 50);

        Assert.False(state.CrewPaidHour.ContainsKey("e1"));
        Assert.True(state.CrewPaidHour.ContainsKey("e2"));
    }

    [Fact]
    public void PaidHoursSurviveASaveAndOldSavesReadNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        CrewPay.TryPay(state, "e1", 87);

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.False(CrewPay.Unpaid(loaded.State!, "e1", 87));
        Assert.True(CrewPay.Unpaid(loaded.State!, "e1", 88));

        var old = codec.Load("{\"schemaVersion\":1,\"licence\":{}}");
        Assert.Equal(LoadStatus.Loaded, old.Status);
        Assert.Empty(old.State!.CrewPaidHour);
        Assert.True(CrewPay.TryPay(old.State!, "e1", 87));
    }
}
