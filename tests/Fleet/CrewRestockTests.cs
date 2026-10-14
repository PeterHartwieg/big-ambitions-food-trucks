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
            Schedule = new HireSchedule { Stops = new List<ScheduleStop> { new() { SpotKey = "office", X = 2000, OpenAt = 11 * 60, CloseAt = 14 * 60 } } }
        };
        runner.EnsurePlan(truck, At(3, 6), 0, 0);
        return truck;
    }

    private static VehicleWhere Bay(bool hasStock = true, bool canRestock = true, bool stayPut = false) =>
        new(0, 0, leaveAlone: false, hasStock: hasStock, stayPut: stayPut, canRestock: canRestock);

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
}
