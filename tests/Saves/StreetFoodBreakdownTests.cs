using System;
using System.Linq;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class VehicleLedgerTests
{
    [Fact]
    public void SalesAndCateringGoToTheirVehicleApart()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 3, 400, 2);
        StreetSales.RecordTotal(state, "truck", 3, 6_000, 40);
        SalesLedger.RecordCost(state, 3, "cart", 150);
        SalesLedger.RecordCost(state, 3, "truck", 2_000);

        var day = state.DailySales.Single();
        Assert.Equal(6_800, day.RevenueCents);
        Assert.Equal(2_150, day.CostCents);
        var cart = day.ByVehicle.Single(v => v.VehicleId == "cart");
        var truck = day.ByVehicle.Single(v => v.VehicleId == "truck");
        Assert.Equal(800, cart.SalesCents);
        Assert.Equal(0, cart.CateringCents);
        Assert.Equal(150, cart.StockCents);
        Assert.Equal(0, truck.SalesCents);
        Assert.Equal(6_000, truck.CateringCents);
        Assert.Equal(2_000, truck.StockCents);
    }

    [Fact]
    public void EachLineAddsUpPerVehicleAndDay()
    {
        var state = new SaveState();
        SalesLedger.RecordLine(state, 1, "cart", LedgerLine.Permit, 25_000);
        SalesLedger.RecordLine(state, 1, "cart", LedgerLine.Wages, 1_000);
        SalesLedger.RecordLine(state, 1, "cart", LedgerLine.Wages, 500);
        SalesLedger.RecordLine(state, 2, "cart", LedgerLine.Parking, 300);
        SalesLedger.RecordLine(state, 2, "cart", LedgerLine.Parking, 0);
        SalesLedger.RecordLine(state, 2, "cart", LedgerLine.Parking, -5);

        Assert.Equal(2, state.DailySales.Count);
        var first = state.DailySales.Single(d => d.Day == 1).ByVehicle.Single();
        Assert.Equal(25_000, first.PermitCents);
        Assert.Equal(1_500, first.WagesCents);
        Assert.Equal(300, state.DailySales.Single(d => d.Day == 2).ByVehicle.Single().ParkingCents);
    }

    [Fact]
    public void AmountsAreCappedRatherThanOverflowing()
    {
        var state = new SaveState();
        SalesLedger.RecordLine(state, 1, "cart", LedgerLine.Wages, long.MaxValue);
        SalesLedger.RecordLine(state, 1, "cart", LedgerLine.Wages, 1);
        Assert.Equal(long.MaxValue, state.DailySales.Single().ByVehicle.Single().WagesCents);
    }

    [Fact]
    public void AnEmptyVehicleIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SalesLedger.RecordLine(new SaveState(), 1, "", LedgerLine.Sales, 1));
    }

    [Fact]
    public void GameCostsStopOnceTheDayIsInTheSummary()
    {
        var state = new SaveState();
        Assert.True(SalesLedger.RecordGameCost(state, 4, "truck", LedgerLine.Wages, 1_200));
        SalesLedger.MarkInSummary(state, 4);
        Assert.False(SalesLedger.RecordGameCost(state, 4, "truck", LedgerLine.Parking, 12_500));

        var truck = state.DailySales.Single().ByVehicle.Single();
        Assert.Equal(1_200, truck.WagesCents);
        Assert.Equal(0, truck.ParkingCents);
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesLedger.RecordGameCost(state, 5, "truck", LedgerLine.Sales, 1));
    }

    [Fact]
    public void LicenceAndEventPlacesBelongToTheFleet()
    {
        var state = new SaveState();
        SalesLedger.RecordLicence(state, 2, 50_000);
        SalesLedger.RecordEventPlaces(state, 2, 20_000);
        SalesLedger.RecordEventPlaces(state, 2, -30_000);

        var day = state.DailySales.Single();
        Assert.Equal(50_000, day.LicenceCents);
        Assert.Equal(-10_000, day.EventPlacesCents);
        Assert.Empty(day.ByVehicle);
    }

    [Fact]
    public void TheDayProfitTakesOffTheModsCostsButNotTheGames()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 1, 1_000, 3);
        SalesLedger.RecordCost(state, 1, "cart", 900);
        SalesLedger.RecordLine(state, 1, "cart", LedgerLine.Permit, 500);
        SalesLedger.RecordGameCost(state, 1, "cart", LedgerLine.Wages, 700);
        SalesLedger.RecordGameCost(state, 1, "cart", LedgerLine.Parking, 100);
        SalesLedger.RecordLicence(state, 1, 200);
        SalesLedger.RecordEventPlaces(state, 1, 300);

        Assert.Equal(3_000 - 900 - 500 - 200 - 300, SalesLedger.DayProfitCents(state, 1));
        Assert.Equal(0, SalesLedger.SummaryCents(state, 1));

        SalesLedger.MarkInSummary(state, 1);
        Assert.Equal(1_100, state.DailySales.Single().SummaryCents);
        Assert.Equal(1_100, SalesLedger.SummaryCents(state, 1));
    }

    [Fact]
    public void DaysMarkedByOlderVersionsHoldSalesLessStock()
    {
        var state = new SaveState();
        state.DailySales.Add(new DailySales { Day = 7, RevenueCents = 5_000, CostCents = 1_500, InSummary = true });
        Assert.Equal(3_500, SalesLedger.SummaryCents(state, 7));
    }

    [Fact]
    public void NewFieldsSurviveARoundTripAndOldSavesLoad()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        StreetSales.Record(state, "cart", 1, 250, 4);
        SalesLedger.RecordGameCost(state, 1, "cart", LedgerLine.Wages, 800);
        SalesLedger.RecordLicence(state, 1, 50_000);
        SalesLedger.RecordEventPlaces(state, 1, -2_000);
        SalesLedger.MarkInSummary(state, 1);

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var day = loaded.State!.DailySales.Single();
        Assert.Equal(1_000, day.ByVehicle.Single().SalesCents);
        Assert.Equal(800, day.ByVehicle.Single().WagesCents);
        Assert.Equal(50_000, day.LicenceCents);
        Assert.Equal(-2_000, day.EventPlacesCents);
        Assert.Equal(1_000 - 50_000 + 2_000, day.SummaryCents);

        var old = codec.Load("{\"schemaVersion\":1,\"licence\":{},\"dailySales\":[{\"day\":1,\"revenueCents\":500,\"costCents\":100,\"inSummary\":true}]}");
        Assert.Equal(LoadStatus.Loaded, old.Status);
        var oldDay = old.State!.DailySales.Single();
        Assert.Empty(oldDay.ByVehicle);
        Assert.Null(oldDay.SummaryCents);
        Assert.Equal(400, SalesLedger.SummaryCents(old.State, 1));
    }

    [Fact]
    public void UnknownVehicleFieldsAreKept()
    {
        var codec = new SaveCodec();
        var json = "{\"schemaVersion\":1,\"licence\":{},\"dailySales\":[{\"day\":1,\"byVehicle\":[{\"vehicleId\":\"cart\",\"salesCents\":5,\"tipsCents\":9}]}]}";
        var loaded = codec.Load(json);
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Contains("\"tipsCents\":9", codec.Serialize(loaded.State!));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"vehicleId\":\"\"}]")]
    [InlineData("[{\"vehicleId\":\"cart\"},{\"vehicleId\":\"cart\"}]")]
    [InlineData("[{\"vehicleId\":\"cart\",\"wagesCents\":-1}]")]
    public void BadVehicleLinesAreCorrupt(string byVehicle)
    {
        var codec = new SaveCodec();
        var result = codec.Load("{\"schemaVersion\":1,\"licence\":{},\"dailySales\":[{\"day\":1,\"byVehicle\":" + byVehicle + "}]}");
        Assert.Equal(LoadStatus.Corrupt, result.Status);
    }
}

public class StreetFoodStatementTests
{
    private static readonly int[] Days = { 9, 8, 7, 6 };
    private static readonly long[] NoGameCosts = { 0, 0, 0, 0 };

    [Fact]
    public void VehiclesShowTheirLinesAndTheTotalIsTheSummaryPlusMovedCosts()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 9, 500, 10);
        SalesLedger.RecordCost(state, 9, "cart", 2_000);
        SalesLedger.RecordGameCost(state, 9, "cart", LedgerLine.Wages, 1_500);
        StreetSales.RecordTotal(state, "truck", 9, 8_000, 40);
        SalesLedger.RecordGameCost(state, 9, "truck", LedgerLine.Parking, 12_500);
        SalesLedger.RecordLicence(state, 9, 1_000);
        SalesLedger.MarkInSummary(state, 9);

        var statement = StreetFoodStatement.Build(state, Days, new long[] { 4_000, 0, 0, 0 }, new long[] { 20_000, 0, 0, 0 });

        Assert.Equal(new long[] { 1_500, 0, 0, 0 }, statement.MovedWages);
        Assert.Equal(new long[] { 12_500, 0, 0, 0 }, statement.MovedParking);
        Assert.Equal(2, statement.Vehicles.Count);
        var cart = statement.Vehicles[0];
        Assert.Equal("cart", cart.VehicleId);
        Assert.Equal(new[] { LedgerLine.Sales, LedgerLine.Stock, LedgerLine.Wages }, cart.Lines.Keys.ToArray());
        Assert.Equal(5_000 - 2_000 - 1_500, cart.Total[0]);
        var truck = statement.Vehicles[1];
        Assert.Equal(new[] { LedgerLine.Catering, LedgerLine.Parking }, truck.Lines.Keys.ToArray());
        Assert.Equal(8_000 - 12_500, truck.Total[0]);
        Assert.Equal(-1_000, statement.Licence[0]);
        Assert.Equal(0, statement.NotBrokenDown[0]);

        // The game's rows lose the moved costs and the group gains them: the statement still adds up.
        var summary = SalesLedger.SummaryCents(state, 9);
        Assert.Equal(summary - 1_500 - 12_500, statement.Total[0]);
        Assert.Equal(new long[] { 0, 0, 0 }, statement.Total.Skip(1).ToArray());
    }

    [Fact]
    public void ADayMarkedByAnOlderVersionIsNotBrokenDown()
    {
        var state = new SaveState();
        state.DailySales.Add(new DailySales { Day = 8, RevenueCents = 3_000, CostCents = 1_000, InSummary = true });

        var statement = StreetFoodStatement.Build(state, Days, NoGameCosts, NoGameCosts);

        Assert.Empty(statement.Vehicles);
        Assert.Equal(new long[] { 0, 2_000, 0, 0 }, statement.NotBrokenDown);
        Assert.Equal(new long[] { 0, 2_000, 0, 0 }, statement.Total);
    }

    [Fact]
    public void ADayUpdatedMidwayShowsTheEarlierPartAsNotBrokenDown()
    {
        var state = new SaveState();
        // Sales recorded by an older version that day (no vehicle lines), then one with the breakdown.
        state.DailySales.Add(new DailySales { Day = 9, RevenueCents = 1_000, CostCents = 400 });
        StreetSales.Record(state, "cart", 9, 500, 2);
        SalesLedger.RecordCost(state, 9, "cart", 300);
        SalesLedger.MarkInSummary(state, 9);

        var statement = StreetFoodStatement.Build(state, Days, NoGameCosts, NoGameCosts);

        Assert.Equal(700, statement.Vehicles.Single().Total[0]);
        Assert.Equal(600, statement.NotBrokenDown[0]);
        Assert.Equal(SalesLedger.SummaryCents(state, 9), statement.Total[0]);
    }

    [Fact]
    public void DaysNotInTheSummaryShowNothing()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 9, 500, 2);
        SalesLedger.RecordGameCost(state, 9, "cart", LedgerLine.Wages, 100);

        var statement = StreetFoodStatement.Build(state, Days, new long[] { 100, 0, 0, 0 }, NoGameCosts);

        Assert.True(statement.IsEmpty);
        Assert.Equal(new long[4], statement.MovedWages);
    }

    [Fact]
    public void WagesBiggerThanTheGamesRowStayInIt()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart", 9, 500, 2);
        SalesLedger.RecordGameCost(state, 9, "cart", LedgerLine.Wages, 1_000);
        SalesLedger.RecordGameCost(state, 9, "cart", LedgerLine.Parking, 300);
        SalesLedger.MarkInSummary(state, 9);

        // The game's row has less than the ledger (a wage it never stored): nothing is moved that day.
        var statement = StreetFoodStatement.Build(state, Days, new long[] { 800, 0, 0, 0 }, new long[] { 302, 0, 0, 0 });

        Assert.Equal(0, statement.MovedWages[0]);
        Assert.Equal(300, statement.MovedParking[0]);
        var cart = statement.Vehicles.Single();
        Assert.False(cart.Lines.ContainsKey(LedgerLine.Wages));
        Assert.Equal(1_000 - 300, cart.Total[0]);
        Assert.Equal(SalesLedger.SummaryCents(state, 9) - 300, statement.Total[0]);
    }

    [Fact]
    public void EventRefundsShowAsIncome()
    {
        var state = new SaveState();
        SalesLedger.RecordEventPlaces(state, 7, -5_000);
        SalesLedger.MarkInSummary(state, 7);

        var statement = StreetFoodStatement.Build(state, Days, NoGameCosts, NoGameCosts);

        Assert.Equal(new long[] { 0, 0, 5_000, 0 }, statement.EventPlaces);
        Assert.Equal(new long[] { 0, 0, 0, 0 }, statement.NotBrokenDown);
        Assert.Equal(new long[] { 0, 0, 5_000, 0 }, statement.Total);
    }

    [Fact]
    public void ColumnsWithoutASummaryAndNoStateAreEmpty()
    {
        Assert.True(StreetFoodStatement.Build(null, Days, NoGameCosts, NoGameCosts).IsEmpty);
        Assert.Throws<ArgumentException>(() => StreetFoodStatement.Build(new SaveState(), Days, new long[3], NoGameCosts));
    }
}

public class CrewLogTests
{
    [Fact]
    public void AWageIsSplitOverTheVehiclesWorkedByHours()
    {
        var state = new SaveState();
        for (var i = 0; i < 6; i++) CrewLog.AddHour(state, 3, "emp", "truckA");
        for (var i = 0; i < 2; i++) CrewLog.AddHour(state, 3, "emp", "truckB");

        var split = CrewLog.Split(state, 3, "emp", 8_000, 8);

        Assert.Equal(new[] { ("truckA", 6_000L), ("truckB", 2_000L) }, split.ToArray());
    }

    [Fact]
    public void LeftoverCentsGoToTheVehicleWithMostHours()
    {
        var state = new SaveState();
        CrewLog.AddHour(state, 1, "emp", "a");
        CrewLog.AddHour(state, 1, "emp", "b");
        CrewLog.AddHour(state, 1, "emp", "b");

        var split = CrewLog.Split(state, 1, "emp", 1_000, 3);

        Assert.Equal(1_000, split.Sum(s => s.Cents));
        Assert.Equal(new[] { ("a", 333L), ("b", 667L) }, split.ToArray());
    }

    [Fact]
    public void HoursNotLoggedStayWithNoVehicle()
    {
        var state = new SaveState();
        for (var i = 0; i < 3; i++) CrewLog.AddHour(state, 1, "emp", "a");

        // Six hours paid, three of them logged on a vehicle: half the wage is the vehicle's.
        Assert.Equal(new[] { ("a", 600L) }, CrewLog.Split(state, 1, "emp", 1_200, 6).ToArray());
    }

    [Fact]
    public void OtherDaysAndEmployeesDontCount()
    {
        var state = new SaveState();
        CrewLog.AddHour(state, 1, "emp", "a");
        CrewLog.AddHour(state, 2, "emp", "b");
        CrewLog.AddHour(state, 1, "other", "c");

        Assert.Equal(new[] { ("a", 500L) }, CrewLog.Split(state, 1, "emp", 500, 1).ToArray());
        Assert.Empty(CrewLog.Split(state, 3, "emp", 500, 1));
        Assert.Empty(CrewLog.Split(state, 1, "emp", 0, 1));
    }

    [Fact]
    public void PaidHoursAreDroppedAndOldOnesPruned()
    {
        var state = new SaveState();
        CrewLog.AddHour(state, 1, "emp", "a");
        CrewLog.AddHour(state, 5, "emp", "a");
        CrewLog.AddHour(state, 5, "other", "a");

        CrewLog.Consume(state, 5, "emp");
        Assert.False(CrewLog.HasHours(state, 5, "emp"));
        Assert.True(CrewLog.HasHours(state, 5, "other"));

        CrewLog.Prune(state, 3);
        Assert.False(CrewLog.HasHours(state, 1, "emp"));
        Assert.Single(state.CrewHours);
    }

    [Fact]
    public void HoursSurviveARoundTripAndOldSavesReadNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        CrewLog.AddHour(state, 4, "emp", "truck");
        CrewLog.AddHour(state, 4, "emp", "truck");

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var hours = loaded.State!.CrewHours.Single();
        Assert.Equal((4, "emp", "truck", 2), (hours.Day, hours.EmployeeId, hours.VehicleId, hours.Hours));

        var old = codec.Load("{\"schemaVersion\":1,\"licence\":{}}");
        Assert.Equal(LoadStatus.Loaded, old.Status);
        Assert.Empty(old.State!.CrewHours);

        var bad = codec.Load("{\"schemaVersion\":1,\"licence\":{},\"crewHours\":[{\"day\":1,\"employeeId\":\"e\",\"vehicleId\":\"v\",\"hours\":0}]}");
        Assert.Equal(LoadStatus.Corrupt, bad.Status);
    }
}

public class CrewWageAttributionTests
{
    [Fact]
    public void TheMarkerIsSetOnceForTheNextDay()
    {
        var state = new SaveState();
        CrewLog.Begin(state, 10);
        CrewLog.Begin(state, 12);
        Assert.Equal(11, state.CrewHoursSince);
    }

    [Fact]
    public void ALoadLateAtNightCoversTheNextDay()
    {
        // Loaded at 23:30 on day 10; the first hourly count comes at midnight, on day 11.
        var state = new SaveState();
        CrewLog.Begin(state, 10);
        CrewLog.Begin(state, 11);
        Assert.Equal(11, state.CrewHoursSince);
        // Day 11's wage for shop hours only, the hire on a truck's crew at payment: not the truck's.
        Assert.Empty(CrewLog.Attribute(state, 11, "e1", 4800, 6, "truck"));
    }

    [Fact]
    public void OnlyDaysBeforeTheMarkerFallBackToTheCrew()
    {
        var state = new SaveState();
        CrewLog.Begin(state, 10);

        // Day 10: hours counted before this version ran weren't logged, so the crew at payment takes the wage.
        Assert.Equal(new[] { ("truck", 900L) }, CrewLog.Attribute(state, 10, "emp", 900, 6, "truck").ToArray());
        // From day 11 the log decides: shop hours with no vehicle hours are no vehicle's wage.
        Assert.Empty(CrewLog.Attribute(state, 11, "emp", 900, 6, "truck"));
        Assert.Empty(CrewLog.Attribute(state, 12, "emp", 900, 6, "truck"));
    }

    [Fact]
    public void LoggedHoursWinOverTheCrewEvenBeforeTheMarker()
    {
        var state = new SaveState();
        CrewLog.Begin(state, 10);
        CrewLog.AddHour(state, 10, "emp", "cart");
        Assert.Equal(new[] { ("cart", 900L) }, CrewLog.Attribute(state, 10, "emp", 900, 1, "truck").ToArray());
    }

    [Fact]
    public void SavesWithoutTheMarkerFallBackAndKeepItAcrossARoundTrip()
    {
        var state = new SaveState();
        Assert.Equal(new[] { ("truck", 500L) }, CrewLog.Attribute(state, 3, "emp", 500, 2, "truck").ToArray());
        Assert.Empty(CrewLog.Attribute(state, 3, "emp", 500, 2, null));

        var codec = new SaveCodec();
        Assert.Null(codec.Load("{\"schemaVersion\":1,\"licence\":{}}").State!.CrewHoursSince);
        CrewLog.Begin(state, 3);
        Assert.Equal(4, codec.Load(codec.Serialize(state)).State!.CrewHoursSince);
    }
}
