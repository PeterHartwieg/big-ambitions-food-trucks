using System;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class CrewTakingsTests
{
    [Fact]
    public void SalesPileUpPerVehicleAndDay()
    {
        var state = new SaveState();
        CrewTakings.Accrue(state, "truck", 3, 500);
        CrewTakings.Accrue(state, "truck", 3, 250);
        CrewTakings.Accrue(state, "cart", 3, 400);
        CrewTakings.Accrue(state, "truck", 4, 100);
        var truck3 = state.CrewTakings.Single(t => t.VehicleId == "truck" && t.Day == 3);
        Assert.Equal((750L, 2), (truck3.Cents, truck3.Sales));
        Assert.Equal(3, state.CrewTakings.Count);
    }

    [Fact]
    public void OnlyEarlierDaysAreDueOldestFirstAndSettlingRemovesThem()
    {
        var state = new SaveState();
        CrewTakings.Accrue(state, "truck", 4, 100);
        CrewTakings.Accrue(state, "cart", 3, 200);
        CrewTakings.Accrue(state, "truck", 3, 300);
        Assert.Empty(CrewTakings.Due(state, 3));
        var due = CrewTakings.Due(state, 4);
        Assert.Equal(new[] { ("cart", 3), ("truck", 3) }, due.Select(t => (t.VehicleId, t.Day)));
        Assert.True(CrewTakings.Settled(state, due[0]));
        Assert.False(CrewTakings.Settled(state, due[0]));
        Assert.Equal(2, CrewTakings.All(state).Count);
        // A night slept through: every day before today is due at once, still one per vehicle and day.
        Assert.Equal(2, CrewTakings.Due(state, 9).Count);
    }

    [Fact]
    public void TheLedgerStillBooksSalesOnTheirDaySoTheSummaryDoesNotWaitForTheMoney()
    {
        var state = new SaveState();
        StreetSales.Record(state, "truck", 3, 500, 1);
        CrewTakings.Accrue(state, "truck", 3, 500);
        Assert.Equal(500, SalesLedger.DayProfitCents(state, 3));
        Assert.Equal(500, CrewTakings.Due(state, 4).Single().Cents);
    }

    [Fact]
    public void OverflowIsRefusedBeforeAnythingChanges()
    {
        var state = new SaveState();
        CrewTakings.Accrue(state, "truck", 3, long.MaxValue - 1);
        Assert.False(CrewTakings.CanAccrue(state, "truck", 3, 2));
        Assert.Throws<OverflowException>(() => CrewTakings.Accrue(state, "truck", 3, 2));
        Assert.Equal(long.MaxValue - 1, state.CrewTakings.Single().Cents);
        Assert.True(CrewTakings.CanAccrue(state, "truck", 4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrewTakings.Accrue(state, "truck", 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrewTakings.Accrue(state, "truck", 1, -1));
    }

    [Fact]
    public void TakingsRoundTripAndOlderSavesReadNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        CrewTakings.Accrue(state, "truck", 3, 500);
        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(500, loaded.State!.CrewTakings.Single().Cents);
        var json = JObject.Parse(codec.Serialize(new SaveState()));
        json.Remove("crewTakings");
        var old = codec.Load(json.ToString());
        Assert.Equal(LoadStatus.Loaded, old.Status);
        Assert.Empty(old.State!.CrewTakings);
        var dup = JObject.Parse(codec.Serialize(state));
        ((JArray)dup["crewTakings"]!).Add(((JArray)dup["crewTakings"]!)[0].DeepClone());
        Assert.NotEqual(LoadStatus.Loaded, codec.Load(dup.ToString()).Status);
    }

    // The adapter books each sale on GameDays.SaleDay(the minute it was simulated at, the live day). A catch-up after the
    // clock passed midnight still books 23:59 on the closing day, and the first minute after on the new one.
    [Fact]
    public void ACatchUpAcrossMidnightBooksEachSaleOnTheDayItWasSimulated()
    {
        var state = new SaveState();
        var liveDay = 6; // the game already turned the day
        foreach (var minute in new[] { HireSchedules.Absolute(5, 23 * 60 + 59) + 0.5, HireSchedules.Absolute(6, 0) + 0.2 })
        {
            var day = GameDays.SaleDay(minute, liveDay);
            StreetSales.Record(state, "truck", day, 400, 1);
            CrewTakings.Accrue(state, "truck", day, 400);
        }
        Assert.Equal(400, SalesLedger.DayProfitCents(state, 5));
        Assert.Equal(400, SalesLedger.DayProfitCents(state, 6));
        Assert.Equal((5, 400L), (CrewTakings.Due(state, 6).Single().Day, CrewTakings.Due(state, 6).Single().Cents));
        // No minute of its own: the live day.
        Assert.Equal(6, GameDays.SaleDay(null, liveDay));
    }

    [Fact]
    public void ASaleCaughtUpAfterATaxPeriodEndedCountsInThatPeriod()
    {
        var state = new SaveState();
        // A period of days 1 to 28; the clock is already on day 29 when the crew's last sale of day 28 is caught up.
        var day = GameDays.SaleDay(HireSchedules.Absolute(28, 1439) + 0.9, 29);
        StreetSales.Record(state, "cart", day, 700, 1);
        Assert.Equal(700, SalesLedger.SumCents(state, 1, 28));
        Assert.Equal(0, SalesLedger.SumCents(state, 29, 56));
    }

    [Fact]
    public void ADayIsDueOnlyOnceTheVehicleHasSoldPastItsEnd()
    {
        var state = new SaveState();
        CrewTakings.Accrue(state, "truck", 5, 100);
        CrewTakings.Accrue(state, "cart", 5, 200);
        var midnight = HireSchedules.Absolute(6, 0);
        // The truck's catch-up still sells 23:50 of day 5; the cart is through.
        var due = CrewTakings.Due(state, 6, id => id == "truck" ? midnight - 10 : midnight);
        Assert.Equal(new[] { "cart" }, due.Select(t => t.VehicleId));
        Assert.Equal(2, CrewTakings.Due(state, 6, _ => midnight).Count);
    }

    [Fact]
    public void PlayerAndCrewShareOneSavedTakingWithoutChangingTheBooksAtPayout()
    {
        var state = new SaveState();
        var vehicle = StreetSales.GetOrAdd(state, "truck");
        vehicle.VehicleType = "truck";
        // A player order with a drink, then a crew order on the same vehicle/day.
        foreach (var price in new long[] { 800, 200, 600 })
        {
            StreetSales.Record(state, "truck", 3, price, 1);
            CrewTakings.Accrue(state, "truck", 3, price);
            SoldCounts.Record(vehicle, 3, "food", 1);
            SalesLedger.RecordCost(state, 3, "truck", 100);
        }
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.SerializeForSave(state, "0.9.10")).State!;
        var taking = Assert.Single(loaded.CrewTakings);
        Assert.Equal((1600L, 3), (taking.Cents, taking.Sales));
        Assert.Empty(CrewTakings.Due(loaded, 3));
        var before = DaySummary.For(loaded, 3, 4);
        Assert.Equal(1600, before.RevenueCents);
        Assert.Equal(1300, before.ProfitCents);
        Assert.Equal(3, Assert.Single(Assert.Single(before.Vehicles).Sold!).Units);
        Assert.Equal(1300, FleetReport.AvgDailyProfitCents(loaded, "truck", 4));
        Assert.Same(taking, Assert.Single(CrewTakings.Due(loaded, 4)));
        Assert.True(CrewTakings.Settled(loaded, taking));
        Assert.Empty(CrewTakings.Due(loaded, 4));
        Assert.Equal(before.RevenueCents, DaySummary.For(loaded, 3, 4).RevenueCents);
        Assert.Equal(1300, SalesLedger.DayProfitCents(loaded, 3));
        Assert.Equal(1600, StreetSales.Find(loaded, "truck")!.LifetimeRevenueCents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SchemaOnePaysOnlyUnpaidCrewTakingsOnceAtMidnight(bool crewSold)
    {
        // Before the upgrade, the player's sale is both in the books and already in the bank, never in the pot.
        var oldState = new SaveState();
        StreetSales.Record(oldState, "truck", 3, 800, 1);
        long balance = 800;
        if (crewSold)
        {
            StreetSales.Record(oldState, "truck", 3, 600, 2);
            CrewTakings.Accrue(oldState, "truck", 3, 600);
            CrewTakings.Accrue(oldState, "truck", 3, 600);
        }
        oldState.ExtensionData["keptField"] = new JObject { ["value"] = 42 };
        var oldJson = new SaveCodec(1).SerializeForSave(oldState, "0.9.10");
        Assert.Equal(1, JObject.Parse(oldJson)["schemaVersion"]!.Value<int>());

        var codec = new SaveCodec();
        var result = codec.Load(oldJson);
        Assert.Equal(LoadStatus.Migrated, result.Status);
        var loaded = Assert.IsType<SaveState>(result.State);
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(oldState.SaveCount, loaded.SaveCount);
        Assert.Equal(oldState.WrittenByModVersion, loaded.WrittenByModVersion);
        Assert.True(JToken.DeepEquals(oldState.ExtensionData["keptField"], loaded.ExtensionData["keptField"]));
        Assert.Empty(CrewTakings.Due(loaded, 3));

        // At midnight, settle the saved pot only. The ledger includes the already-paid player sale.
        var midnight = HireSchedules.Absolute(4, 0);
        var due = CrewTakings.Due(loaded, 4, _ => midnight);
        Assert.Equal(crewSold ? 1 : 0, due.Count);
        foreach (var taking in due)
        {
            Assert.Equal(("truck", 3, 1200L, 2), (taking.VehicleId, taking.Day, taking.Cents, taking.Sales));
            balance += taking.Cents;
            Assert.True(CrewTakings.Settled(loaded, taking));
            Assert.False(CrewTakings.Settled(loaded, taking));
        }
        Assert.Equal(crewSold ? 2000L : 800L, balance);
        Assert.Equal(balance, SalesLedger.DayProfitCents(loaded, 3));
        Assert.Equal(balance, StreetSales.Find(loaded, "truck")!.LifetimeRevenueCents);
        Assert.Empty(CrewTakings.Due(loaded, 4));

        // Saving the upgrade and loading again cannot recreate either the paid crew pot or the old player sale.
        var saved = codec.SerializeForSave(loaded, "0.9.10");
        var reloaded = codec.Load(saved);
        Assert.Equal(LoadStatus.Loaded, reloaded.Status);
        Assert.Empty(CrewTakings.All(reloaded.State!));
        Assert.Empty(CrewTakings.Due(reloaded.State!, 5));
        Assert.Equal(balance, SalesLedger.DayProfitCents(reloaded.State!, 3));
    }

    [Fact]
    public void RemovedVehiclesRefundAllDaysEvenWithALaggingClockAndRetryUntilSettled()
    {
        var state = new SaveState();
        CrewTakings.Accrue(state, "sold", 3, 500);
        CrewTakings.Accrue(state, "sold", 4, 200);
        CrewTakings.Accrue(state, "owned", 4, 300);
        // Saved after removal, before a payment succeeds: no VehicleState is required to retain the money.
        var codec = new SaveCodec();
        state = codec.Load(codec.Serialize(state)).State!;
        var due = CrewTakings.Due(state, 4, _ => 0, id => id == "owned");
        Assert.Equal(new[] { ("sold", 3), ("sold", 4) }, due.Select(t => (t.VehicleId, t.Day)));
        Assert.Equal(700, due.Sum(t => t.Cents));
        Assert.Equal(due, CrewTakings.Due(state, 4, _ => 0, id => id == "owned"));
        foreach (var taking in due) Assert.True(CrewTakings.Settled(state, taking));
        Assert.Empty(CrewTakings.Due(state, 4, _ => 0, id => id == "owned"));
        // Uninstall pays even the still-owned vehicle's current day; repeated settlement can't pay twice.
        var uninstall = Assert.Single(CrewTakings.All(state));
        Assert.Equal(300, uninstall.Cents);
        Assert.True(CrewTakings.Settled(state, uninstall));
        Assert.False(CrewTakings.Settled(state, uninstall));
        Assert.Empty(CrewTakings.All(state));
    }
}
