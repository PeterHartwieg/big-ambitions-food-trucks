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

    // One stop 2 km from the bay at x 0, open 11:00 to 14:00 on day 3.
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

    // A route through the bay: the pitch at x 2000 until 13:00, the commissary's bay at x 0 from 14:30, the pitch again
    // from 16:00. 2 km is 80 minutes each way.
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
        // Sold out at the pitch after it closed: it leaves for the bay stop all the same.
        var leave = runner.Step(truck, At(3, 13, 10), new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false, bayStops: bayStops));
        Assert.Equal((HireActionKind.Depart, 1, At(3, 14, 30)), (leave.Kind, leave.Stop, leave.ArrivesAt));
        HireRunner.Depart(truck, leave, At(3, 13, 10), 2000, 0, 0, 0);
        var arrive = runner.Step(truck, At(3, 14, 30), Bay(hasStock: false));
        Assert.Equal(HireActionKind.Arrive, arrive.Kind);
        HireRunner.Arrived(truck, 0, 0, atStop: true, At(3, 14, 30));
        // In the bay with nothing on board: it restocks, then opens the bay stop with its food.
        Assert.Equal((HireActionKind.Restock, 1), (runner.Step(truck, At(3, 14, 30), Bay(hasStock: false)).Kind, runner.Step(truck, At(3, 14, 30), Bay(hasStock: false)).Stop));
        HireRunner.Restocked(truck, At(3, 14, 30));
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 14, 30), Bay()).Kind);
        // After the bay stop it tops up for the new leg (once, as any leg leaving the bay) and sets off for the pitch again.
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
        // Parked 20 m off the bay stop, inside the usual 25 m stop radius but not in the bay: not there yet, it moves.
        var off = runner.Step(truck, At(3, 14, 30), new VehicleWhere(20, 0, leaveAlone: false, hasStock: false, bayStops: bayStops));
        Assert.Equal((HireActionKind.Depart, 1), (off.Kind, off.Stop));
        // Even parked there by the crew for that stop.
        truck.Arrival = new ArrivalState { SpotKey = "bay", StopIndex = 1, X = 20, Z = 0 };
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 14, 30), new VehicleWhere(20, 0, leaveAlone: false, bayStops: bayStops)).Kind);
        // Placed in the bay: it restocks there and then opens.
        var inBay = new VehicleWhere(20, 0, leaveAlone: false, hasStock: false, canRestock: true, bayStops: bayStops);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 14, 30), inBay).Kind);
        HireRunner.Restocked(truck, At(3, 14, 30));
        Assert.Equal(HireActionKind.Open, runner.Step(truck, At(3, 14, 30), new VehicleWhere(20, 0, leaveAlone: false, canRestock: true, bayStops: bayStops)).Kind);
        // An ordinary stop keeps the 25 m radius.
        Assert.Equal(HireActionKind.Open, runner.Step(ThroughTheBay(), At(3, 11), new VehicleWhere(1980, 0, leaveAlone: false, bayStops: bayStops)).Kind);
    }

    [Fact]
    public void ASoldOutTruckNeverDrivesToAStopOutsideItsBay()
    {
        var truck = ThroughTheBay();
        // Without the bay among its stops (no space there, say), sold out it stays where it is.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 10), new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false)).Kind);
        // Sold out at the bay without a restock (the commissary was out): not on to the pitch.
        var empty = runner.Step(truck, At(3, 15), new VehicleWhere(0, 0, leaveAlone: false, hasStock: false, bayStops: new[] { 1 }));
        Assert.Equal(HireActionKind.None, empty.Kind);
        // And the player by the vehicle keeps it there even for the bay.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 10), new VehicleWhere(2000, 0, leaveAlone: false, hasStock: false, stayPut: true, bayStops: new[] { 1 })).Kind);
    }

    [Fact]
    public void ASoldOutTruckWithABayDrivesBackThereInsteadOfWaitingForTheDriveBack()
    {
        // Moved by the player to x 500 and sold out, with its rented bay at x 0 but not among its stops: it doesn't
        // wait for the plan's drive back (timed from the pitch it won't reach, which may not fit the shift from here),
        // it drives to the bay now (soak seed 3: a truck left out for four nights).
        var truck = Truck();
        var soldOut = new VehicleWhere(500, 0, leaveAlone: false, hasStock: false, home: (0, 0));
        var home = runner.Step(truck, At(3, 9, 40), soldOut);
        Assert.Equal((HireActionKind.Depart, HireSchedules.HomeIndex, At(3, 10)), (home.Kind, home.Stop, home.ArrivesAt));
        HireRunner.Depart(truck, home, At(3, 9, 40), 500, 0, 0, 0, new ScheduleStop { SpotKey = HireSchedules.HomeSpotKey });
        HireRunner.Arrived(truck, 0, 0, atStop: true, At(3, 10));
        Assert.False(truck.ReturnPending);
        // In the bay it restocks and sets off for the pitch.
        var bay = new VehicleWhere(0, 0, leaveAlone: false, hasStock: false, canRestock: true, home: (0, 0));
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 10), bay).Kind);
        HireRunner.Restocked(truck, At(3, 10));
        Assert.Equal((HireActionKind.Depart, 0), (runner.Step(truck, At(3, 10), Bay()).Kind, runner.Step(truck, At(3, 10), Bay()).Stop));
        // With nobody on shift for the whole drive it stays.
        var late = Truck();
        late.Schedule!.CrewShifts.RemoveAll(c => c.Day != System.DayOfWeek.Wednesday);
        late.Schedule.CrewShifts.ForEach(c => c.End = 10);
        Assert.Equal(HireActionKind.None, runner.Step(late, At(3, 9, 45), soldOut).Kind);
    }

    [Fact]
    public void InTheBayTheCrewRestocksOnceBeforeItSetsOff()
    {
        var truck = Truck();
        // 80 minutes to the stop: it leaves at 09:40, restocking first.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9), Bay()).Kind);
        var restock = runner.Step(truck, At(3, 9, 40), Bay());
        Assert.Equal((HireActionKind.Restock, 0), (restock.Kind, restock.Stop));
        HireRunner.Restocked(truck, At(3, 9, 40));
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, At(3, 9, 40), Bay()).Kind);
        // Without the bay (or with nothing there to load) it just leaves.
        Assert.Equal(HireActionKind.Depart, runner.Step(Truck(), At(3, 9, 40), Bay(canRestock: false)).Kind);
    }

    [Fact]
    public void ACrewWithNothingToSellRestocksThenDecidesAndTriesOncePerLeg()
    {
        var truck = Truck();
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 9, 40), Bay(hasStock: false)).Kind);
        HireRunner.Restocked(truck, At(3, 9, 40));
        // The commissary was out too: it stays, and doesn't try again for this leg.
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9, 41), Bay(hasStock: false)).Kind);
        // The player by the vehicle: the crew leaves it alone.
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
        // A hot dog grill filled up; the grill half empty with no burgers or kabobs left at the commissary.
        var cargo = new[] { new CargoLine(HotDogGrill, 1, false), new CargoLine(HotDog, 80, false), new CargoLine(Grill, 1, false), new CargoLine(Burger, 10, false) };
        Assert.Equal(new[] { Burger, Kabob }, CrewRestock.Short(tables, "truck", cargo, new Dictionary<string, int>()).OrderBy(x => x));
        // Kabobs still there (the cargo refused them, say): not a shortage.
        Assert.Empty(CrewRestock.Short(tables, "truck", cargo, new Dictionary<string, int> { [Kabob] = 500 }));
        // Hot dogs out at the commissary but the grill is full: nothing missing.
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
        // Not in its own bay: it just sets off.
        Assert.Equal(HireActionKind.Depart, runner.Step(Truck(), At(3, 9, 40), Bay(canRestock: false)).Kind);
        // At 13:30 a trip of 80 minutes would arrive after the stop closes at 14:00: it restocks once, then stays.
        var truck = Truck();
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 13, 30), Bay()).Kind);
        HireRunner.Restocked(truck, At(3, 13, 30));
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 30), Bay()).Kind);
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 13, 45), Bay()).Kind);
    }

    [Fact]
    public void AnEmptyCommissaryStillNamesWhatWasMissing()
    {
        // An empty hot dog truck, nothing at the commissary: hot dogs are short.
        var cargo = new[] { new CargoLine(HotDogGrill, 1, false) };
        Assert.Equal(new[] { HotDog }, CrewRestock.Short(ShippedData.Tables, "truck", cargo, new Dictionary<string, int>()));
    }

    [Fact]
    public void ADeliveryInTheSameLegBringsOneMoreRestockBeforeLeaving()
    {
        var truck = Truck();
        // 09:40: nothing on board, nothing at the commissary. It tries, finds nothing, and waits.
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, At(3, 9, 40), Bay(hasStock: false, stockKey: "hotdog=0")).Kind);
        HireRunner.Restocked(truck, At(3, 9, 40), "hotdog=0");
        Assert.Equal(HireActionKind.None, runner.Step(truck, At(3, 9, 41), Bay(hasStock: false, stockKey: "hotdog=0")).Kind);
        // 09:45: a delivery came in. One more attempt; after loading, the stock left is the new key.
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
