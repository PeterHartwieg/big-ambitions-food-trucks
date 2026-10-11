using System;
using System.Linq;
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
}
