using System.Linq;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class SoldCountsTests
{
    [Fact]
    public void CountsAddUpPerItemAndDay()
    {
        var cart = new VehicleState { VehicleId = "cart" };
        SoldCounts.Record(cart, 5, "ba:itemname_hotdog", 1);
        SoldCounts.Record(cart, 5, "ba:itemname_hotdog", 2);
        SoldCounts.Record(cart, 5, "ba:itemname_coffee", 4);
        SoldCounts.Record(cart, 5, "ba:itemname_soda", 0);

        Assert.Equal(new[] { ("ba:itemname_coffee", 4), ("ba:itemname_hotdog", 3) }, SoldCounts.On(cart, 5));
        Assert.Empty(SoldCounts.On(cart, 4));
    }

    [Fact]
    public void OnlyTodayAndYesterdayAreKept()
    {
        var cart = new VehicleState { VehicleId = "cart" };
        SoldCounts.Record(cart, 1, "ba:itemname_hotdog", 1);
        SoldCounts.Record(cart, 2, "ba:itemname_hotdog", 2);
        SoldCounts.Record(cart, 3, "ba:itemname_hotdog", 3);

        Assert.Equal(new[] { 2, 3 }, cart.Sold.Select(d => d.Day).OrderBy(d => d));
        Assert.Empty(SoldCounts.On(cart, 1));
    }

    [Fact]
    public void ACountIsCappedRatherThanOverflowing()
    {
        var cart = new VehicleState { VehicleId = "cart" };
        SoldCounts.Record(cart, 1, "ba:itemname_hotdog", int.MaxValue);
        SoldCounts.Record(cart, 1, "ba:itemname_hotdog", 1);
        Assert.Equal(int.MaxValue, SoldCounts.On(cart, 1).Single().Units);
    }

    [Fact]
    public void CountsRoundTripAndOlderSavesLoadWithNone()
    {
        var state = new SaveState();
        var cart = StreetSales.GetOrAdd(state, "cart");
        SoldCounts.Record(cart, 7, "ba:itemname_hotdog", 3);
        var codec = new SaveCodec();

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(3, SoldCounts.On(loaded.State!.Vehicles.Single(), 7).Single().Units);

        var older = codec.Load("{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart\"}]}");
        Assert.Equal(LoadStatus.Loaded, older.Status);
        Assert.Empty(older.State!.Vehicles.Single().Sold);
    }

    [Theory]
    [InlineData("[{\"day\":1,\"units\":{\"ba:itemname_hotdog\":0}}]")]
    [InlineData("[{\"day\":1,\"units\":{\"\":2}}]")]
    [InlineData("[{\"day\":1},{\"day\":1}]")]
    [InlineData("[null]")]
    public void InvalidCountsAreCorrupt(string sold)
    {
        var result = new SaveCodec().Load("{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart\",\"sold\":" + sold + "}]}");
        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Contains("Sold", result.Error);
    }
}

public class FleetReportTests
{
    [Fact]
    public void TodayAndTheWeekAddUpForOneVehicle()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 10, 500, 4);              // 2,000 sales today
        StreetSales.RecordTotal(state, "cart", 10, 3_000, 20);      // 3,000 catering today
        SalesLedger.RecordCost(state, 10, "cart", 1_200);
        SalesLedger.RecordLine(state, 10, "cart", LedgerLine.Parking, 300);
        StreetSales.Record(state, "cart", 4, 500, 2);               // 1,000 six days ago
        SalesLedger.RecordLine(state, 4, "cart", LedgerLine.Wages, 400);
        StreetSales.Record(state, "cart", 3, 500, 10);              // seven days ago: outside the week
        StreetSales.Record(state, "truck", 10, 900, 1);             // another vehicle

        var money = FleetReport.Vehicle(state, "cart", 10);
        Assert.Equal(5_000, money.TodaySalesCents);
        Assert.Equal(5_000 - 1_200 - 300, money.TodayProfitCents);
        Assert.Equal(6_000, money.WeekSalesCents);
        Assert.Equal(3_500 + 600, money.WeekProfitCents);
    }

    [Fact]
    public void AVehicleWithNothingRecordedHasZeros()
    {
        var money = FleetReport.Vehicle(new SaveState(), "cart", 3);
        Assert.Equal(0, money.TodaySalesCents);
        Assert.Equal(0, money.WeekProfitCents);
        Assert.Equal(0, FleetReport.Vehicle(null, "cart", 3).WeekSalesCents);
    }

    [Fact]
    public void TheBusinessWeekTakesOffFleetCostsAndCountsOlderDays()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 9, 1_000, 3);
        SalesLedger.RecordCost(state, 9, "cart", 500);
        SalesLedger.RecordLicence(state, 9, 1_000);
        SalesLedger.RecordEventPlaces(state, 8, 700);
        // A day recorded before the breakdown: sales and stock cost without vehicles.
        state.DailySales.Add(new DailySales { Day = 7, RevenueCents = 2_000, CostCents = 800 });
        state.DailySales.Add(new DailySales { Day = 2, RevenueCents = 9_000 });

        Assert.Equal(2_500 - 1_000 - 700 + 1_200, FleetReport.WeekProfitCents(state, 9));
    }

    [Fact]
    public void ADayBegunBeforeTheBreakdownKeepsItsEarlierProfitOnceVehiclesAreRecorded()
    {
        var state = new SaveState();
        // Recorded by an older version: $100 of sales and $30 of stock, no vehicles.
        state.DailySales.Add(new DailySales { Day = 5, RevenueCents = 10_000, CostCents = 3_000 });
        // The same day, after the update: a $20 wage, then a $5 sale with $2 of stock.
        SalesLedger.RecordLine(state, 5, "cart", LedgerLine.Wages, 2_000);
        StreetSales.Record(state, "cart", 5, 500, 1);
        SalesLedger.RecordCost(state, 5, "cart", 200);

        Assert.Equal(7_000 - 2_000 + 300, FleetReport.WeekProfitCents(state, 5));
        // The vehicle shows only what was itemised.
        Assert.Equal(500 - 200 - 2_000, FleetReport.Vehicle(state, "cart", 5).TodayProfitCents);
        // And the total matches what Econoview's Street food group adds up to once the day is in the summary.
        SalesLedger.MarkInSummary(state, 5);
        var statement = StreetFoodStatement.Build(state, new[] { 5 }, new[] { 2_000L }, new[] { 0L });
        Assert.Equal(statement.Total[0], FleetReport.WeekProfitCents(state, 5));
    }
}
