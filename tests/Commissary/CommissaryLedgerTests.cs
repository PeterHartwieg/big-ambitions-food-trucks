using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissaryLedgerTests
{
    [Fact]
    public void ExpensesAppearInVehicleAndFleetReportsAndSummaryWithoutMovingGameRows()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 4, 1000, 100);
        SalesLedger.RecordCost(state, 4, "cart", 20000);
        SalesLedger.RecordCommissaryRent(state, 4, "cart", 986);
        SalesLedger.RecordOvernightFine(state, 4, "cart", 25000);
        SalesLedger.RecordDeliveryFees(state, 4, 500);
        SalesLedger.RecordGameCost(state, 4, "cart", LedgerLine.Parking, 300);
        SalesLedger.RecordGameCost(state, 4, "cart", LedgerLine.Wages, 1000);
        SalesLedger.RecordLine(state, 4, "cart", LedgerLine.Permit, 100);
        SalesLedger.RecordLicence(state, 4, 200);
        SalesLedger.RecordEventPlaces(state, 4, 400);
        var modProfit = 100000 - 20000 - 986 - 25000 - 500 - 100 - 200 - 400;
        Assert.Equal(modProfit, SalesLedger.DayProfitCents(state, 4));
        Assert.Equal(100000 - 20000 - 986 - 25000 - 300 - 1000 - 100, FleetReport.Vehicle(state, "cart", 4).TodayProfitCents);
        Assert.Equal(modProfit - 1300, FleetReport.WeekProfitCents(state, 4));
        SalesLedger.MarkInSummary(state, 4);
        var statement = StreetFoodStatement.Build(state, new[] { 4 }, new long[] { 1000 }, new long[] { 300 });
        Assert.Equal(-986, statement.Vehicles.Single().Lines[LedgerLine.CommissaryRent][0]);
        Assert.Equal(-25000, statement.Vehicles.Single().Lines[LedgerLine.OvernightFine][0]);
        Assert.Equal(-500, statement.DeliveryFees[0]);
        Assert.Equal(0, statement.NotBrokenDown[0]);
        Assert.Equal(modProfit - 1300, statement.Total[0]);
        Assert.Equal(300, statement.MovedParking[0]);
        Assert.Equal(1000, statement.MovedWages[0]);
    }

    [Fact]
    public void CommissaryGoodsBecomeCostOfGoodsOnlyWhenSoldAndFeeIsFleetWide()
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented() };
        var rules = new CommissaryRules();
        var order = Commissaries.Order(state.Commissary, rules, "a", "taco", 10, 100, (1, 100));
        Assert.Empty(state.DailySales);
        SalesLedger.RecordDeliveryFees(state, 1, rules.DeliveryFeeCents); // as the adapter does when it is paid
        Assert.Empty(state.DailySales.Single().ByVehicle);
        Assert.Equal(0, state.DailySales.Single().CostCents);
        Assert.Equal(-25_000, SalesLedger.DayProfitCents(state, 1));
        Commissaries.Deliver(state.Commissary, rules, 2, 480, CommissariesTests.Paid);
        var taken = Commissaries.Restock(state.Commissary, "a", new System.Collections.Generic.Dictionary<string, int> { ["taco"] = 4 });
        Assert.Equal(100, taken.Single().UnitPriceCents);
        Assert.Equal(0, state.DailySales.Single().CostCents);
        StreetSales.Record(state, "cart", 2, 200, 4);
        SalesLedger.RecordCost(state, 2, "cart", 4 * taken.Single().UnitPriceCents);
        Assert.Equal(400, SalesLedger.DayProfitCents(state, 2));
        Assert.Equal(400 - 25_000, FleetReport.WeekProfitCents(state, 2));
    }

    [Fact]
    public void FailedPaymentsWaitUntilPaidAndBookOnTheActualPaymentDay()
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented() };
        state.Commissary.Charges.Add(Assert.Single(Commissaries.RentDue(state.Commissary, new CommissaryRules(), 1)));
        Assert.Empty(state.DailySales);
        var loaded = new SaveCodec().Load(new SaveCodec().Serialize(state)).State!;
        var retry = loaded.Commissary.Charges.Single();
        SalesLedger.RecordCommissaryRent(loaded, 3, retry.VehicleId, retry.Cents);
        loaded.Commissary.Charges.Remove(retry);
        Assert.Equal(0, SalesLedger.DayProfitCents(loaded, 1));
        Assert.Equal(-986, SalesLedger.DayProfitCents(loaded, 3));
        Assert.Empty(loaded.Commissary.Charges);
    }

    [Fact]
    public void FeesAreBookedOnTheDeliveryDayAndTheRefundLeavesThemBooked()
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented() };
        var rules = new CommissaryRules();
        Commissaries.Order(state.Commissary, rules, "a", "taco", 10, 100, (1, 100));
        Assert.Empty(state.DailySales); // booking takes no money
        var delivered = Commissaries.Deliver(state.Commissary, rules, 2, 480, CommissariesTests.Paid).Delivered.Single();
        SalesLedger.RecordDeliveryFees(state, 2, delivered.FeeCents);
        Assert.Equal(25_000, delivered.FeeCents);
        Commissaries.Order(state.Commissary, rules, "a", "machine", 1, 2000, (2, 500));

        var refund = Commissaries.Refund(state.Commissary);
        Assert.Equal(delivered.GoodsCents, refund.Total);
        Assert.Equal(1, refund.CancelledOrders);
        Assert.Equal(-delivered.FeeCents, SalesLedger.DayProfitCents(state, 2));
        Assert.Equal(-delivered.FeeCents, FleetReport.WeekProfitCents(state, 3));
        Assert.All(state.DailySales, d => Assert.Equal(0, d.CostCents));
        Assert.Equal(LoadStatus.Loaded, new SaveCodec().Load(new SaveCodec().Serialize(state)).Status);
    }

    [Fact]
    public void AddedMoneyFieldsRoundTripWithUnknownDailyAndVehicleFields()
    {
        var state = new SaveState();
        SalesLedger.RecordCommissaryRent(state, 3, "cart", 986);
        SalesLedger.RecordOvernightFine(state, 3, "cart", 25000);
        SalesLedger.RecordDeliveryFees(state, 3, 100);
        state.DailySales.Single().ExtensionData["futureDay"] = new JValue(4);
        state.DailySales.Single().ByVehicle.Single().ExtensionData["futureVehicle"] = new JValue(5);
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var day = loaded.State!.DailySales.Single();
        Assert.Equal(986, day.ByVehicle.Single().CommissaryRentCents);
        Assert.Equal(25000, day.ByVehicle.Single().OvernightFineCents);
        Assert.Equal(100, day.DeliveryFeesCents);
        Assert.Equal(4, (int)day.ExtensionData["futureDay"]);
        Assert.Equal(5, (int)day.ByVehicle.Single().ExtensionData["futureVehicle"]);
    }

    [Fact]
    public void DeliveryFeeRefundsAreIncomeAndNonpositiveVehicleCostsAreIgnored()
    {
        var state = new SaveState();
        SalesLedger.RecordDeliveryFees(state, 1, 100);
        SalesLedger.RecordDeliveryFees(state, 1, -150);
        SalesLedger.RecordCommissaryRent(state, 1, "cart", 0);
        SalesLedger.RecordOvernightFine(state, 1, "cart", -1);
        Assert.Empty(state.DailySales.Single().ByVehicle);
        SalesLedger.MarkInSummary(state, 1);
        var statement = StreetFoodStatement.Build(state, new[] { 1 }, new long[] { 0 }, new long[] { 0 });
        Assert.Equal(50, statement.DeliveryFees[0]);
        Assert.Equal(50, statement.Total[0]);
        Assert.False(statement.IsEmpty);
    }

    [Fact]
    public void NewVehicleCostHelpersCapOverflowLikeExistingLedgerLines()
    {
        var state = new SaveState();
        SalesLedger.RecordCommissaryRent(state, 1, "cart", long.MaxValue);
        SalesLedger.RecordCommissaryRent(state, 1, "cart", 1);
        SalesLedger.RecordOvernightFine(state, 1, "cart", long.MaxValue);
        SalesLedger.RecordOvernightFine(state, 1, "cart", 1);
        SalesLedger.RecordDeliveryFees(state, 1, long.MaxValue);
        SalesLedger.RecordDeliveryFees(state, 1, 1);
        var day = state.DailySales.Single();
        Assert.Equal(long.MaxValue, day.ByVehicle.Single().CommissaryRentCents);
        Assert.Equal(long.MaxValue, day.ByVehicle.Single().OvernightFineCents);
        Assert.Equal(long.MaxValue, day.DeliveryFeesCents);
    }

    [Theory]
    [InlineData("commissaryRentCents")]
    [InlineData("overnightFineCents")]
    public void NegativeVehicleExpensesAreCorrupt(string field) =>
        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load("{\"schemaVersion\":1,\"dailySales\":[{\"day\":1,\"byVehicle\":[{\"vehicleId\":\"v\",\"" + field + "\":-1}]}]}").Status);
}
