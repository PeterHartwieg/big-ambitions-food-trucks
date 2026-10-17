using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class DaySummaryTests
{
    private static SaveState Day3()
    {
        var state = new SaveState();
        var truck = StreetSales.GetOrAdd(state, "truck");
        truck.VehicleType = "truck";
        StreetSales.Record(state, "truck", 3, 1_300, 4);
        SalesLedger.RecordCost(state, 3, "truck", 600);
        SalesLedger.RecordGameCost(state, 3, "truck", LedgerLine.Wages, 20_000);
        SalesLedger.RecordCommissaryRent(state, 3, "truck", 1_644);
        SalesLedger.RecordOvernightFine(state, 3, "truck", 5_000);
        SalesLedger.RecordDeliveryFees(state, 3, 25_000);
        SoldCounts.Record(truck, 3, "ba:itemname_hotdog", 3);
        SoldCounts.Record(truck, 3, "foodtrucks:itemname_tacos", 1);
        ReputationBook.AddMinutes(truck, 3, "spot-a", 180);
        ReputationBook.AddMinutes(truck, 3, "spot-b", 90);
        state.Commissary.FeesPaid.Add(new CommissaryFeePaid { CommissaryId = "hells-kitchen", Day = 3 });
        var cart = StreetSales.GetOrAdd(state, "cart");
        cart.VehicleType = "cart";
        return state;
    }

    [Fact]
    public void AVehiclesLinesSoldUnitsAndStopsForADayStillKept()
    {
        var report = DaySummary.For(Day3(), 3, 4);
        var truck = report.Vehicles.Single(v => v.VehicleId == "truck");
        Assert.Equal(("truck", 5_200L, 600L, 20_000L, 1_644L, 5_000L), (truck.VehicleType, truck.SalesCents, truck.StockCostCents, truck.WagesCents, truck.CommissaryRentCents, truck.OvernightFineCents));
        Assert.Equal(5_200 - 600 - 20_000 - 1_644 - 5_000, truck.ProfitCents);
        Assert.Equal(new[] { ("ba:itemname_hotdog", 3), ("foodtrucks:itemname_tacos", 1) }, truck.Sold);
        Assert.Equal((2, 270), (truck.StopsServed!.Value, truck.MinutesOpen!.Value));
        Assert.Equal((5_200L, 600L, 25_000L), (report.RevenueCents, report.StockCostCents, report.DeliveryFeesCents));
        Assert.Equal(5_200 - 600 - 25_000 - 20_000 - 1_644 - 5_000, report.ProfitCents);
        Assert.Equal(new[] { "hells-kitchen" }, report.FeesPaidAt);
        // A vehicle with nothing that day is still listed while the player has it.
        Assert.Equal(0, report.Vehicles.Single(v => v.VehicleId == "cart").ProfitCents);
    }

    [Fact]
    public void ShortLivedRecordsAreNullOnceTheDayIsGone()
    {
        var truck = DaySummary.For(Day3(), 3, 5).Vehicles.Single(v => v.VehicleId == "truck");
        Assert.Null(truck.Sold);
        Assert.Null(truck.StopsServed);
        Assert.Null(truck.MinutesOpen);
        Assert.Equal(5_200, truck.SalesCents);
    }

    [Fact]
    public void ADayWithoutRecordsIsEmpty()
    {
        var report = DaySummary.For(Day3(), 9, 9);
        Assert.Equal(0, report.RevenueCents);
        Assert.False(report.InSummary);
        Assert.All(report.Vehicles, v => Assert.Equal(0, v.ProfitCents));
        Assert.Empty(DaySummary.For(null, 1, 1).Vehicles);
    }
}
