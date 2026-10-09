using System.Linq;
using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class CrewPayTests
{
    private static bool Credit(string employee, string vehicle, long hour) => true;

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
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 12)));
    }

    [Fact]
    public void AnOpenHourIsDueInItsFirstHalfOnly()
    {
        var truck = Truck(open: true);

        Assert.True(CrewPay.OpenHourDue(truck, At(3, 15)));
        Assert.True(CrewPay.OpenHourDue(truck, At(3, 15, 29)));
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 15, 30)));
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 15, 59)));
    }

    [Fact]
    public void AWindowSoldOutBeforeTheHourNeverEarnsIt()
    {
        // The sales reach 12:59, sell out and close; the minute 13:00 is never reached with the window open.
        var truck = Truck(open: true);
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 12, 59)));
        truck.IsOpen = false;
        Assert.False(CrewPay.OpenHourDue(truck, At(3, 13)));
    }

    [Fact]
    public void RouteHoursDoNotDecideAnOpenHourTheMarkerDoes()
    {
        // A 12-14 route, and a hire added at 12:10 to a window the player opened: the route's count at 12:00 didn't
        // include them, so the hour is theirs to earn.
        var state = new SaveState();
        var truck = Truck(open: true);
        Leg(truck, At(3, 12), At(3, 14));
        var hour = CrewPay.HourOf(At(3, 12, 10));

        Assert.True(CrewPay.OpenHourDue(truck, At(3, 12, 10)));
        Assert.True(CrewPay.Unpaid(state, "late", hour));
        // One the route did count at 12:00 isn't counted again.
        CrewPay.TryPay(state, "e1", hour);
        Assert.False(CrewPay.Unpaid(state, "e1", hour));
    }

    [Fact]
    public void AnHourTheGameCountedIsNotCountedAgain()
    {
        // A shop worker counted by the game at 12:00, then unassigned and put on an open truck at 12:10.
        var state = new SaveState();
        var hour = CrewPay.HourOf(At(3, 12));
        CrewPay.GameCounted(state, "shop", hour);

        Assert.False(CrewPay.Unpaid(state, "shop", hour));
        Assert.False(CrewPay.TryPay(state, "shop", hour));
        Assert.True(CrewPay.TryPay(state, "shop", hour + 1));
        // It never moves a marker back.
        CrewPay.GameCounted(state, "shop", hour);
        Assert.Equal(hour + 1, state.CrewPaidHour["shop"]);
    }

    [Fact]
    public void OnlyTheCrewWhoServeArePaid()
    {
        var crew = new[] { ("a", 40.0), ("b", 70.0), ("c", 55.0) };

        var alone = CrewPay.Team(crew, null, 2);
        Assert.Equal(new[] { "b", "c" }, alone.Crew);
        Assert.Equal(new[] { 70.0, 55.0 }, alone.Skills);

        // The player at the window takes a place from the weakest.
        var withPlayer = CrewPay.Team(crew, 60.0, 2);
        Assert.Equal(new[] { "b" }, withPlayer.Crew);
        Assert.Equal(new[] { 70.0, 60.0 }, withPlayer.Skills);

        // On a tie the crew member serves, as before.
        Assert.Equal(new[] { "a" }, CrewPay.Team(new[] { ("a", 50.0) }, 50.0, 1).Crew);
        Assert.Empty(CrewPay.Team(new[] { ("a", 40.0) }, 90.0, 1).Crew);
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
    public void OldMarkersArePrunedAfterADay()
    {
        var state = new SaveState();
        CrewPay.TryPay(state, "e1", 10);
        CrewPay.TryPay(state, "e2", 26);
        CrewPay.Served(state, "e3", "t1", 10 * 60 + 5);
        CrewPay.Served(state, "e4", "t1", 30 * 60 + 5);

        CrewPay.Prune(state, 50);

        Assert.False(state.CrewPaidHour.ContainsKey("e1"));
        Assert.True(state.CrewPaidHour.ContainsKey("e2"));
        Assert.False(state.CrewServedHour.ContainsKey("e3"));
        Assert.True(state.CrewServedHour.ContainsKey("e4"));
    }

    [Fact]
    public void ServingInTheFirstHalfEarnsTheHourWhenItEnds()
    {
        var state = new SaveState();
        CrewPay.Served(state, "e1", "t1", At(3, 13, 29));
        CrewPay.Served(state, "e2", "t1", At(3, 13, 30));

        // Nothing is decided while the hour runs.
        Assert.Empty(CrewPay.CloseHours(state, CrewPay.HourOf(At(3, 13)), Credit));
        var paid = CrewPay.CloseHours(state, CrewPay.HourOf(At(3, 14)), Credit);

        Assert.Equal(new[] { ("e1", "t1", CrewPay.HourOf(At(3, 13))) }, paid);
        // Decided once.
        Assert.Empty(CrewPay.CloseHours(state, CrewPay.HourOf(At(3, 15)), Credit));
        Assert.Empty(state.CrewServedHour);
    }

    [Fact]
    public void ASkipAcrossAnHourAndARouteStartPaysBothInOrder()
    {
        // A skip from 12:59 to 14:01 with a route starting at 14:00: at 13:00 the sales are caught up (nothing served
        // yet), at 14:00 the sales reach 14:00 first, so 13:10's serving is decided before the route stamps 14:00.
        var state = new SaveState();
        Assert.Empty(CrewPay.CloseHours(state, CrewPay.HourOf(At(3, 13)), Credit));
        CrewPay.Served(state, "e1", "t1", At(3, 13, 10));
        Assert.Single(CrewPay.CloseHours(state, CrewPay.HourOf(At(3, 14)), Credit));
        Assert.True(CrewPay.TryPay(state, "e1", CrewPay.HourOf(At(3, 14))));
    }

    [Fact]
    public void AnHourTheRouteOrTheGameCountedIsNotPaidAgainAsAnOpenHour()
    {
        var state = new SaveState();
        var hour = CrewPay.HourOf(At(3, 12));
        CrewPay.TryPay(state, "route", hour);
        CrewPay.GameCounted(state, "shop", hour);
        CrewPay.Served(state, "route", "t1", At(3, 12, 10));
        CrewPay.Served(state, "shop", "t1", At(3, 12, 10));
        CrewPay.Served(state, "late", "t1", At(3, 12, 10));

        var paid = CrewPay.CloseHours(state, hour + 1, Credit);

        Assert.Equal(new[] { "late" }, paid.Select(p => p.EmployeeId));
    }

    [Fact]
    public void AnHourMarkedLongAgoStillCountsAsPaidUntilPruned()
    {
        // With the decisions in order, an older marker never blocks a later hour, and a marker from earlier today
        // still blocks its own hour while the sales replay sits in it.
        var state = new SaveState();
        var eleven = CrewPay.HourOf(At(3, 11));
        CrewPay.TryPay(state, "e1", eleven);
        CrewPay.Prune(state, eleven + 3);
        CrewPay.Served(state, "e1", "t1", At(3, 11, 10));

        Assert.Empty(CrewPay.CloseHours(state, eleven + 3, Credit));
    }

    [Fact]
    public void TheLastHourBeforeMidnightBelongsToTheClosingDay()
    {
        var state = new SaveState();
        CrewPay.Served(state, "e1", "t1", At(3, 23, 10));

        var paid = CrewPay.CloseHours(state, CrewPay.HourOf(At(4, 0)), Credit);

        Assert.Equal(3, CrewPay.DayOfHour(paid.Single().Hour));
        Assert.Equal(4, CrewPay.DayOfHour(CrewPay.HourOf(At(4, 0))));
    }

    [Fact]
    public void TheFirstVehicleInAnHourKeepsIt()
    {
        var state = new SaveState();
        CrewPay.Served(state, "e1", "t1", At(3, 12, 5));
        CrewPay.Served(state, "e1", "t2", At(3, 12, 20));

        Assert.Equal("t1", CrewPay.CloseHours(state, CrewPay.HourOf(At(3, 13)), Credit).Single().VehicleId);
    }

    [Fact]
    public void PaidHoursSurviveASaveAndOldSavesReadNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        CrewPay.TryPay(state, "e1", 87);
        CrewPay.Served(state, "e2", "t1", 88 * 60 + 5);

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.False(CrewPay.Unpaid(loaded.State!, "e1", 87));
        Assert.True(CrewPay.Unpaid(loaded.State!, "e1", 88));
        Assert.Equal(("t1", 88L), (loaded.State!.CrewServedHour["e2"].VehicleId, loaded.State!.CrewServedHour["e2"].Hour));

        var old = codec.Load("{\"schemaVersion\":1,\"licence\":{}}");
        Assert.Equal(LoadStatus.Loaded, old.Status);
        Assert.Empty(old.State!.CrewPaidHour);
        Assert.Empty(old.State!.CrewServedHour);
        Assert.True(CrewPay.TryPay(old.State!, "e1", 87));
    }

    [Fact]
    public void AnHourEarnedBeforeMovingToAShopIsStillPaidOnTheTruck()
    {
        // Serves the truck at 12:10, assigned to a shop at 12:40 with a shift from 13:00. At 13:00 the hour that ended is
        // decided before the game counts the shop's 13:00, so 12:00 is credited to the truck.
        var state = new SaveState();
        var noon = CrewPay.HourOf(At(3, 12));
        CrewPay.Served(state, "e1", "t1", At(3, 12, 10));

        var credited = new List<(string, string, long)>();
        var paid = CrewPay.CloseHours(state, noon + 1, (e, v, h) => { credited.Add((e, v, h)); return true; });
        CrewPay.GameCounted(state, "e1", noon + 1);

        Assert.Equal(new[] { ("e1", "t1", noon) }, credited);
        Assert.Single(paid);
        Assert.Equal(noon + 1, state.CrewPaidHour["e1"]);
    }

    [Fact]
    public void AnHourIsStampedPaidOnlyOnceItIsCredited()
    {
        var state = new SaveState();
        var noon = CrewPay.HourOf(At(3, 12));
        CrewPay.Served(state, "e1", "t1", At(3, 12, 10));

        Assert.Empty(CrewPay.CloseHours(state, noon + 1, (e, v, h) => false));
        Assert.True(CrewPay.Unpaid(state, "e1", noon));
        Assert.True(state.CrewServedHour.ContainsKey("e1"));

        // Tried again at the next decision.
        Assert.Single(CrewPay.CloseHours(state, noon + 2, Credit));
        Assert.False(CrewPay.Unpaid(state, "e1", noon));
        Assert.Empty(state.CrewServedHour);
    }

    [Fact]
    public void SettlingEverythingIncludesTheHourStillRunning()
    {
        // The uninstall step at 12:20 after serving at 12:05: the running hour is paid now, and its marker stays so
        // play carrying on doesn't pay it again.
        var state = new SaveState();
        var noon = CrewPay.HourOf(At(3, 12));
        CrewPay.Served(state, "e1", "t1", At(3, 12, 5));

        Assert.Single(CrewPay.CloseHours(state, long.MaxValue, Credit));
        Assert.False(CrewPay.Unpaid(state, "e1", noon));
        Assert.Empty(CrewPay.CloseHours(state, noon + 1, Credit));
    }

    [Fact]
    public void AnHourPaidSinceANoteFailedIsDroppedNotPaidAgain()
    {
        var state = new SaveState();
        var noon = CrewPay.HourOf(At(3, 12));
        CrewPay.Served(state, "e1", "t1", At(3, 12, 10));
        CrewPay.CloseHours(state, noon + 1, (e, v, h) => false);
        CrewPay.GameCounted(state, "e1", noon + 1);

        Assert.Empty(CrewPay.CloseHours(state, noon + 2, Credit));
        Assert.Empty(state.CrewServedHour);
    }

    // A crew member who worked the 12:00 route hour on t1 (on today's counters) and served a window the player
    // opened on t2 at 13:10, whose employment changes at 13:40.
    private static SaveState WorkedTodayAndServing(out long one)
    {
        var state = new SaveState();
        var noon = CrewPay.HourOf(At(3, 12));
        CrewPay.TryPay(state, "e1", noon);
        CrewLog.AddHour(state, 3, "e1", "t1");
        CrewPay.Served(state, "e1", "t2", At(3, 13, 10));
        one = noon + 1;
        return state;
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("warehouse driver")]
    [InlineData("replacement")]
    [InlineData("fired")]
    public void LeavingTheCrewPaysEverythingOwedOnceOnTheVehicles(string change)
    {
        // Every change of employment settles the same way; the game's own payout afterwards finds nothing of the mod's.
        Assert.NotNull(change);
        var state = WorkedTodayAndServing(out var one);

        var owed = CrewPay.Owed(state, "e1", 3);
        Assert.Equal(new[] { ("t1", 1), ("t2", 1) }, owed.ByVehicle);
        Assert.Equal(1, owed.OnCounters);
        Assert.Equal(one, owed.PendingHour);
        Assert.Equal(2, owed.Hours);

        CrewPay.Settled(state, "e1", 3, owed);

        // Paid exactly once: nothing owed again, the midnight split has no hours for it, the next decision pays nothing.
        Assert.Equal(0, CrewPay.Owed(state, "e1", 3).Hours);
        Assert.False(CrewLog.HasHours(state, 3, "e1"));
        Assert.Empty(CrewPay.CloseHours(state, one + 1, Credit));
        Assert.False(CrewPay.Unpaid(state, "e1", one));
    }

    [Fact]
    public void AnHourTheGameCountedIsNotOwed()
    {
        var state = new SaveState();
        var noon = CrewPay.HourOf(At(3, 12));
        CrewPay.Served(state, "e1", "t1", At(3, 12, 10));
        CrewPay.GameCounted(state, "e1", noon);

        var owed = CrewPay.Owed(state, "e1", 3);
        Assert.Equal(0, owed.Hours);
        CrewPay.Settled(state, "e1", 3, owed);
        Assert.Empty(state.CrewServedHour);
    }

    [Fact]
    public void OtherDaysAndOtherHiresAreLeftAlone()
    {
        var state = new SaveState();
        CrewLog.AddHour(state, 2, "e1", "t1");
        CrewLog.AddHour(state, 3, "e2", "t1");

        var owed = CrewPay.Owed(state, "e1", 3);
        Assert.Equal(0, owed.Hours);
        CrewPay.Settled(state, "e1", 3, owed);
        Assert.True(CrewLog.HasHours(state, 2, "e1"));
        Assert.True(CrewLog.HasHours(state, 3, "e2"));
    }

    [Fact]
    public void ARetirementInAMidnightSkipPaysNoHourAfterMidnight()
    {
        // A skip from 23:59 to 00:10 in one frame: the payroll closes 23:00 into the closing day, then the game's daily
        // run retires the worker with the clock already at 00:10. The sales go only to 00:00, so no service is noted
        // after midnight and nothing for 00:00 is owed.
        var state = new SaveState();
        CrewPay.Served(state, "e1", "t1", At(3, 23, 10));
        var midnight = At(4, 0);
        Assert.Single(CrewPay.CloseHours(state, CrewPay.HourOf(midnight), Credit));

        var limit = CrewPay.CatchUpLimit(inGameTick: true, midnight, At(4, 0, 10));
        Assert.Equal(midnight, limit);
        // The sales catch up to `limit`: the last minute they reach is 23:59, never 00:00.
        for (var minute = At(3, 23, 59); minute < limit; minute++) CrewPay.Served(state, "e1", "t1", minute);

        var owed = CrewPay.Owed(state, "e1", 4);
        Assert.Equal(0, owed.Hours);
        Assert.Null(owed.PendingHour);
        Assert.Empty(state.CrewServedHour);
    }

    [Fact]
    public void AChangeThePlayerMakesCatchesUpToTheLiveMinute()
    {
        Assert.Equal(At(3, 12, 20), CrewPay.CatchUpLimit(inGameTick: false, At(3, 12), At(3, 12, 20)));
        Assert.Equal(At(3, 12), CrewPay.CatchUpLimit(inGameTick: true, At(3, 12), At(3, 12, 20)));
    }
}
