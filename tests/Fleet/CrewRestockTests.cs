using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class CrewRestockTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string HotDogGrill = "ba:itemname_hotdoggrill";
    private const string Grill = "ba:itemname_industrialgrill";
    private const string Burger = "ba:itemname_burger";
    private const string Kabob = "ba:itemname_kabob";

    private static readonly HireRunner runner = new(ShippedData.Rules.Fleet);

    private static long At(int day, int hour, int minute = 0) => HireSchedules.Absolute(day, hour * 60 + minute);

    private static VehicleState Truck()
    {
        var truck = new VehicleState
        {
            VehicleId = "t1",
            VehicleType = "truck",
            HasPermit = true,
            Crew = new List<string> { "e1" },
            Schedule = new HireSchedule { Stops = new List<ScheduleStop> { new() { Day = System.DayOfWeek.Wednesday, SpotKey = "office", X = 2000, OpenAt = 11 * 60, CloseAt = 14 * 60 } } }
        };
        WeekTestData.AllDay(truck);
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        return truck;
    }

    private static VehicleWhere Bay(bool hasStock = true, bool canRestock = true, bool stayPut = false, string? stockKey = null) =>
        new(0, 0, leaveAlone: false, hasStock: hasStock, stayPut: stayPut, canRestock: canRestock, stockKey: stockKey);

    private static VehicleState ThroughTheBay()
    {
        var truck = new VehicleState
        {
            VehicleId = "t1",
            VehicleType = "truck",
            HasPermit = true,
            Crew = new List<string> { "e1" },
            Schedule = new HireSchedule
            {
                Stops = new List<ScheduleStop>
                {
                    new() { Day = System.DayOfWeek.Wednesday, SpotKey = "pitch", X = 2000, OpenAt = 11 * 60, CloseAt = 13 * 60 },
                    new() { Day = System.DayOfWeek.Wednesday, SpotKey = "bay", X = 0, OpenAt = 14 * 60 + 30, CloseAt = 15 * 60 },
                    new() { Day = System.DayOfWeek.Wednesday, SpotKey = "pitch-again", X = 2000, OpenAt = 16 * 60, CloseAt = 18 * 60 }
                }
            }
        };
        WeekTestData.AllDay(truck);
        runner.EnsurePlan(truck, At(3, 6), 2000, 0);
        return truck;
    }

    [Fact]
    public void ASoldOutTruckDrivesBackToItsBayRestocksAndSetsOffAgain()
    {
        var truck = ThroughTheBay();
        var bayStops = new[] { 1 };
        var leave = runner.Step(truck, At(3, 13, 10), new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false, bayStops: bayStops));
        Assert.Equal((HireActionKind.Depart, 1, At(3, 14, 30)), (leave.Kind, leave.Stop, leave.ArrivesAt));
        HireRunner.Depart(truck, leave, At(3, 13, 10), 2000, 0, 0, 0);
        var arrive = runner.Step(truck, At(3, 14, 30), Bay(hasStock: false));
        Assert.Equal(HireActionKind.Arrive, arrive.Kind);
        HireRunner.Arrived(truck, 0, 0, atStop: true, At(3, 14, 30));
        Assert.Equal((HireActionKind.Restock, 1), (runner.Step(truck, At(3, 14, 30), Bay(hasStock: false)).Kind, runner.Step(truck, At(3, 14, 30), Bay(hasStock: false)).Stop));
        HireRunner.Restocked(truck, At(3, 14, 30));
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 14, 30), Bay()).Kind);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 15), Bay()).Kind);
        HireRunner.Restocked(truck, At(3, 15));
        var back = runner.Step(truck, At(3, 15), Bay());
        Assert.Equal((HireActionKind.Depart, 2, At(3, 16, 20)), (back.Kind, back.Stop, back.ArrivesAt));
    }

    [Fact]
    public void ABayStopCountsAsReachedOnlyInTheBay()
    {
        var truck = ThroughTheBay();
        var bayStops = new[] { 1 };
        var off = runner.Step(truck, At(3, 14, 30), new VehicleWhere(20, 0, leaveAlone: false, hasStock: false, bayStops: bayStops));
        Assert.Equal((HireActionKind.Depart, 1), (off.Kind, off.Stop));
        truck.Arrival = new ArrivalState { SpotKey = "bay", StopIndex = 1, X = 20, Z = 0 };
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 14, 30), new VehicleWhere(20, 0, leaveAlone: false, bayStops: bayStops)).Kind);
        var inBay = new VehicleWhere(20, 0, leaveAlone: false, hasStock: false, canRestock: true, bayStops: bayStops);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 14, 30), inBay).Kind);
        HireRunner.Restocked(truck, At(3, 14, 30));
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 14, 30), new VehicleWhere(20, 0, leaveAlone: false, canRestock: true, bayStops: bayStops)).Kind);
        Assert.Equal(HireActionKind.Open, runner.Step(ThroughTheBay(), At(3, 11), new VehicleWhere(1980, 0, leaveAlone: false, bayStops: bayStops)).Kind);
    }

    [Fact]
    public void ASoldOutTruckNeverDrivesToAStopOutsideItsBay()
    {
        var truck = ThroughTheBay();
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 10), new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false)).Kind);
        var empty = runner.Step(truck, At(3, 15), new VehicleWhere(0, 0, leaveAlone: false, hasStock: false, bayStops: new[] { 1 }));
        Assert.Equal(HireActionKind.None, empty.Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 10), new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false, stayPut: true, bayStops: new[] { 1 })).Kind);
    }

    [Fact]
    public void ASoldOutTruckWithABayDrivesBackThereInsteadOfWaitingForTheDriveBack()
    {
        var truck = Truck();
        var soldOut = new VehicleWhere(500, 0, leaveAlone: false, hasStock: false, home: (0, 0));
        var home = runner.Step(truck, At(3, 9, 40), soldOut);
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex, At(3, 10)), (home.Kind, home.Stop, home.ArrivesAt));
        HireRunner.Depart(truck, home, At(3, 9, 40), 500, 0, 0, 0, new ScheduleStop { SpotKey = HireSchedules.HomeSpotKey });
        HireRunner.Arrived(truck, 0, 0, atStop: true, At(3, 10));
        Assert.False(truck.ReturnPending);
        var bay = new VehicleWhere(0, 0, leaveAlone: false, hasStock: false, canRestock: true, home: (0, 0));
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 10), bay).Kind);
        HireRunner.Restocked(truck, At(3, 10));
        Assert.Equal((HireActionKind.Depart, 0), (runner.Step(truck, At(3, 10), Bay()).Kind, runner.Step(truck, At(3, 10), Bay()).Stop));
        var late = Truck();
        late.Schedule!.CrewShifts.RemoveAll(c => c.Day != System.DayOfWeek.Wednesday);
        late.Schedule.CrewShifts.ForEach(c => c.End = 10);
        Assert.Equal(HireActionKind.None, runner.Step(late, At(3, 9, 45), soldOut).Kind);
    }

    [Fact]
    public void InTheBayTheCrewRestocksOnceBeforeItSetsOff()
    {
        var truck = Truck();
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9), Bay()).Kind);
        var restock = runner.Step(truck, At(3, 9, 40), Bay());
        Assert.Equal((HireActionKind.Restock, 0), (restock.Kind, restock.Stop));
        HireRunner.Restocked(truck, At(3, 9, 40));
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 9, 40), Bay()).Kind);
        Assert.Equal(HireActionKind.Depart, runner.Step(Truck(), At(3, 9, 40), Bay(canRestock: false)).Kind);
    }

    [Fact]
    public void ACrewWithNothingToSellRestocksThenDecidesAndTriesOncePerLeg()
    {
        var truck = Truck();
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 9, 40), Bay(hasStock: false)).Kind);
        HireRunner.Restocked(truck, At(3, 9, 40));
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9, 41), Bay(hasStock: false)).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(Truck(), At(3, 9, 40), Bay(stayPut: true)).Kind);
    }

    [Fact]
    public void AtAStopInTheBayItRestocksOnlyWithNothingToSell()
    {
        var truck = Truck();
        var atStop = new VehicleWhere(2000, 0, leaveAlone: false, canRestock: true);
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 11), atStop).Kind);
        var empty = new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false, canRestock: true);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 11), empty).Kind);
        HireRunner.Restocked(truck, At(3, 11));
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 11), empty).Kind);
    }

    [Fact]
    public void ShortNamesThePiecesFoodsTheCommissaryHadNoneOf()
    {
        var tables = ShippedData.Tables;
        var cargo = new[] { new CargoLine(HotDogGrill, 1, false), new CargoLine(HotDog, 80, false), new CargoLine(Grill, 1, false), new CargoLine(Burger, 10, false) };
        Assert.Equal(new[] { Burger, Kabob }, CrewRestock.Short(tables, "truck", cargo, new Dictionary<string, int>()).OrderBy(x => x));
        Assert.Empty(CrewRestock.Short(tables, "truck", cargo, new Dictionary<string, int> { [Kabob] = 500 }));
        Assert.DoesNotContain(HotDog, CrewRestock.Short(tables, "truck", cargo, new Dictionary<string, int>()));
    }

    [Fact]
    public void TheShortageIsToldOncePerVehicleAndDayAndSurvivesASave()
    {
        var truck = Truck();
        Assert.True(CrewRestock.TellShort(truck, 3));
        Assert.False(CrewRestock.TellShort(truck, 3));
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(truck);
        HireRunner.Restocked(truck, At(3, 9, 40));
        var loaded = codec.Load(codec.Serialize(state)).State!.Vehicles.Single();
        Assert.False(CrewRestock.TellShort(loaded, 3));
        Assert.Equal(truck.RestockedFor, loaded.RestockedFor);
        Assert.True(CrewRestock.TellShort(loaded, 4));
    }

    [Fact]
    public void AwayFromItsBayNoRestockAndATooLateTripStillRestocksOnce()
    {
        Assert.Equal(HireActionKind.Depart, runner.Step(Truck(), At(3, 9, 40), Bay(canRestock: false)).Kind);
        var truck = Truck();
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 13, 30), Bay()).Kind);
        HireRunner.Restocked(truck, At(3, 13, 30));
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 30), Bay()).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 45), Bay()).Kind);
    }

    [Fact]
    public void AnEmptyCommissaryStillNamesWhatWasMissing()
    {
        var cargo = new[] { new CargoLine(HotDogGrill, 1, false) };
        Assert.Equal(new[] { HotDog }, CrewRestock.Short(ShippedData.Tables, "truck", cargo, new Dictionary<string, int>()));
    }

    [Fact]
    public void ADeliveryInTheSameLegBringsOneMoreRestockBeforeLeaving()
    {
        var truck = Truck();
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 9, 40), Bay(hasStock: false, stockKey: "hotdog=0")).Kind);
        HireRunner.Restocked(truck, At(3, 9, 40), "hotdog=0");
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9, 41), Bay(hasStock: false, stockKey: "hotdog=0")).Kind);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 9, 45), Bay(hasStock: false, stockKey: "hotdog=500")).Kind);
        HireRunner.Restocked(truck, At(3, 9, 45), "hotdog=420");
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 9, 45), Bay(stockKey: "hotdog=420")).Kind);
    }

    [Fact]
    public void TheStockKeyListsTheVehiclesFoodsOnly()
    {
        var cargo = new[] { new CargoLine(HotDogGrill, 1, false), new CargoLine(Grill, 1, false) };
        var stock = new Dictionary<string, int> { [HotDog] = 500, [Burger] = 20, ["ba:itemname_cupofcoffee"] = 9 };
        var key = CrewRestock.StockKey(ShippedData.Tables, "truck", cargo, stock);
        Assert.Equal($"{Burger}=20;{HotDog}=500;{Kabob}=0", key);
        stock["ba:itemname_cupofcoffee"] = 0;
        Assert.Equal(key, CrewRestock.StockKey(ShippedData.Tables, "truck", cargo, stock));
    }
}
