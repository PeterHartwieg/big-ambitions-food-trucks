using System.Linq;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class LedgerCostTests
{
    [Fact]
    public void ProfitIsSalesLessCost()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 3, 1_165);
        SalesLedger.RecordCost(state, 3, 445);

        Assert.Equal(720, SalesLedger.DayProfitCents(state, 3));
        Assert.Equal(0, SalesLedger.DayProfitCents(state, 4));
        Assert.Equal(1_165, SalesLedger.SumCents(state, 3, 3));
    }

    [Fact]
    public void CostAloneCreatesTheDay()
    {
        var state = new SaveState();
        SalesLedger.RecordCost(state, 2, 100);

        Assert.Equal(-100, SalesLedger.DayProfitCents(state, 2));
        Assert.Single(state.DailySales);
    }

    [Fact]
    public void ZeroOrNegativeCostIsIgnored()
    {
        var state = new SaveState();
        SalesLedger.RecordCost(state, 2, 0);
        SalesLedger.RecordCost(state, 2, -5);

        Assert.Empty(state.DailySales);
    }

    [Fact]
    public void CostSurvivesARoundTripAndOldSavesReadZero()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        SalesLedger.Record(state, 1, 500);
        SalesLedger.RecordCost(state, 1, 200);

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(200, loaded.State!.DailySales.Single().CostCents);

        var old = codec.Load("{\"schemaVersion\":1,\"licence\":{},\"dailySales\":[{\"day\":1,\"revenueCents\":500}]}");
        Assert.Equal(LoadStatus.Loaded, old.Status);
        Assert.Equal(0, old.State!.DailySales.Single().CostCents);
    }

    [Fact]
    public void NegativeCostInASaveIsCorrupt()
    {
        var codec = new SaveCodec();
        var result = codec.Load("{\"schemaVersion\":1,\"licence\":{},\"dailySales\":[{\"day\":1,\"revenueCents\":500,\"costCents\":-1}]}");
        Assert.Equal(LoadStatus.Corrupt, result.Status);
    }
}

public class LedgerSummaryTests
{
    [Fact]
    public void DaysAreMarkedAndSurviveARoundTrip()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        SalesLedger.Record(state, 4, 1_000);
        Assert.False(SalesLedger.IsInSummary(state, 4));

        SalesLedger.MarkInSummary(state, 4);
        SalesLedger.MarkInSummary(state, 5);
        Assert.True(SalesLedger.IsInSummary(state, 4));
        Assert.False(SalesLedger.IsInSummary(state, 5));

        var loaded = codec.Load(codec.Serialize(state));
        Assert.True(SalesLedger.IsInSummary(loaded.State, 4));
    }
}
