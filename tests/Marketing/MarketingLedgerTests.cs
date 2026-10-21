using System.Linq;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Marketing;

public class MarketingLedgerTests
{
    [Fact]
    public void MarketingIsAVehicleCostInEveryReport()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 4, 1000, 100);
        SalesLedger.RecordCommissaryRent(state, 4, "cart", 986);
        SalesLedger.RecordMarketing(state, 4, "cart", 3000);
        var profit = 100000 - 986 - 3000;

        Assert.Equal(3000, state.DailySales.Single().ByVehicle.Single().MarketingCents);
        Assert.Equal(profit, SalesLedger.DayProfitCents(state, 4));
        Assert.Equal(profit, FleetReport.Vehicle(state, "cart", 4).TodayProfitCents);
        Assert.Equal(profit, FleetReport.WeekProfitCents(state, 4));
        Assert.Equal(profit, FleetReport.AvgDailyProfitCents(state, "cart", 5));
        var summary = DaySummary.For(state, 4, 4).Vehicles.Single();
        Assert.Equal(3000, summary.MarketingCents);
        Assert.Equal(profit, summary.ProfitCents);

        SalesLedger.MarkInSummary(state, 4);
        var statement = StreetFoodStatement.Build(state, new[] { 4 }, new long[] { 0 }, new long[] { 0 });
        Assert.Equal(-3000, statement.Vehicles.Single().Lines[LedgerLine.Marketing][0]);
        Assert.Equal(0, statement.NotBrokenDown[0]);
        Assert.Equal(profit, statement.Total[0]);
    }

    [Fact]
    public void MarketingRoundTripsIgnoresNothingAtOrBelowZeroAndCapsOverflow()
    {
        var state = new SaveState();
        SalesLedger.RecordMarketing(state, 1, "cart", 0);
        SalesLedger.RecordMarketing(state, 1, "cart", -5);
        Assert.Empty(state.DailySales);
        SalesLedger.RecordMarketing(state, 1, "cart", long.MaxValue);
        SalesLedger.RecordMarketing(state, 1, "cart", 1);
        Assert.Equal(long.MaxValue, state.DailySales.Single().ByVehicle.Single().MarketingCents);

        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(long.MaxValue, loaded.State!.DailySales.Single().ByVehicle.Single().MarketingCents);
    }
}
