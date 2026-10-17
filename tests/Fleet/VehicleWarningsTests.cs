using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// Vehicle warnings as the game's to-dos (plan, "Street food page and vehicle warnings", Peter, 15 October 2026).
public class VehicleWarningsTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string HotDogGrill = "ba:itemname_hotdoggrill";
    private const string Machine = "ba:itemname_industrialcoffeemachine";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Tea = "ba:itemname_cupoftea";
    private const string Latte = "bakeryxx:itemname_latte";

    private static FleetRules Rules => ShippedData.Rules.Fleet;
    private static CargoLine L(string item, int amount) => new(item, amount, false);

    // A permitted vehicle with a commissary space and no route: nothing to warn about but its stock.
    private static (SaveState State, VehicleState Vehicle) Setup(string kind = "cart")
    {
        var vehicle = new VehicleState { VehicleId = "v1", VehicleType = kind, HasPermit = true };
        var state = new SaveState { Vehicles = { vehicle } };
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "v1", Kind = kind, CommissaryId = "hells-kitchen" });
        return (state, vehicle);
    }

    private static List<VehicleWarning> For(SaveState state, VehicleState vehicle, IEnumerable<CargoLine> cargo, int? crew = null) =>
        VehicleWarnings.For(ShippedData.Tables, Rules, state, vehicle, cargo, crew).ToList();

    private static HireSchedule Route(int open = 11 * 60, int close = 14 * 60) =>
        new() { Stops = WeekTestData.On(new[] { new ScheduleStop { SpotKey = "office", X = 100, Z = 100, OpenAt = open, CloseAt = close } }) };

    [Fact]
    public void ACompleteVehicleHasNoWarnings()
    {
        var (state, vehicle) = Setup();
        Assert.Empty(For(state, vehicle, new[] { L(HotDogGrill, 1), L(HotDog, 40) }));
        Assert.Empty(For(state, vehicle, new CargoLine[0]));
    }

    [Theory]
    // A cart's hot dog grill holds 40 hot dogs: low at a quarter of that or less, empty at none.
    [InlineData(40, null)]
    [InlineData(11, null)]
    [InlineData(10, VehicleWarningKind.LowStock)]
    [InlineData(1, VehicleWarningKind.LowStock)]
    [InlineData(0, VehicleWarningKind.EmptyStock)]
    public void StockFollowsTheGamesQuarterRule(int hotDogs, VehicleWarningKind? expected)
    {
        var (state, vehicle) = Setup();
        var cargo = new List<CargoLine> { L(HotDogGrill, 1) };
        if (hotDogs > 0) cargo.Add(L(HotDog, hotDogs));
        // Sold out: it sold hot dogs on its last day out.
        else vehicle.Sold.Add(Sold(4, HotDog));

        var warnings = For(state, vehicle, cargo);

        if (expected == null) Assert.Empty(warnings);
        else Assert.Equal(new VehicleWarning(expected.Value, "v1", HotDog), Assert.Single(warnings));
    }

    [Fact]
    public void StockCountsAgainstWhatTheFittedPiecesHold()
    {
        // Two grills on a truck hold 160: 40 hot dogs is a quarter, so low; with one grill (80) it is half.
        var (state, vehicle) = Setup("truck");
        Assert.Equal(VehicleWarningKind.LowStock, Assert.Single(For(state, vehicle, new[] { L(HotDogGrill, 2), L(HotDog, 40) })).Kind);
        Assert.Empty(For(state, vehicle, new[] { L(HotDogGrill, 1), L(HotDog, 40) }));
    }

    private static SoldDay Sold(int day, string item) => new() { Day = day, Units = new Dictionary<string, int> { [item] = 5 } };

    [Fact]
    public void OnlyFoodsTheVehicleStocksWarn()
    {
        // A coffee machine also makes tea, lattes and americanos; a cart carrying only coffee warns about none of them.
        var (state, vehicle) = Setup();
        Assert.Empty(For(state, vehicle, new[] { L(Machine, 1), L(Coffee, 160) }));

        // Tea it sold yesterday and has run out of is empty; coffee down to a quarter is low.
        vehicle.Sold.Add(Sold(3, Tea));
        Assert.Equal(new[] { new VehicleWarning(VehicleWarningKind.LowStock, "v1", Coffee), new VehicleWarning(VehicleWarningKind.EmptyStock, "v1", Tea) },
            For(state, vehicle, new[] { L(Machine, 1), L(Coffee, 40) }));
    }

    [Fact]
    public void FoodsMissingFromTheGameNeverWarn()
    {
        // A Bakery latte in the sales record, but the Bakery mod is gone: no to-do the player could never clear.
        var (state, vehicle) = Setup();
        vehicle.Sold.Add(Sold(3, Latte));
        var cargo = new[] { L(Machine, 1), L(Coffee, 160) };
        Assert.Equal(VehicleWarningKind.EmptyStock, Assert.Single(For(state, vehicle, cargo)).Kind);
        Assert.Empty(VehicleWarnings.For(ShippedData.Tables, Rules, state, vehicle, cargo, itemExists: i => !i.StartsWith("bakeryxx:")));
    }

    [Fact]
    public void FoodOffTheMenuHasNoStockWarning()
    {
        // The grill was taken out: the hot dogs on board are on no menu.
        var (state, vehicle) = Setup();
        Assert.Empty(For(state, vehicle, new[] { L(HotDog, 2) }));
    }

    [Fact]
    public void ARouteWithoutEquipmentWarns()
    {
        var (state, vehicle) = Setup();
        // No route: nothing to say about a bare cart.
        Assert.Empty(For(state, vehicle, new CargoLine[0]));

        vehicle.Crew = new List<string> { "e1" };
        vehicle.Schedule = Route();
        WeekTestData.AllDay(vehicle);
        Assert.Equal(new VehicleWarning(VehicleWarningKind.NoEquipment, "v1"), Assert.Single(For(state, vehicle, new CargoLine[0])));
        // Food without a machine to cook it on is no equipment either.
        Assert.Contains(new VehicleWarning(VehicleWarningKind.NoEquipment, "v1"), For(state, vehicle, new[] { L(HotDog, 20) }));
        // One machine is enough.
        Assert.DoesNotContain(For(state, vehicle, new[] { L(HotDogGrill, 1), L(HotDog, 40) }), w => w.Kind == VehicleWarningKind.NoEquipment);
    }

    [Fact]
    public void MissingCrewOnlyWithARoute()
    {
        var (state, vehicle) = Setup("truck");
        var cargo = new[] { L(HotDogGrill, 1), L(HotDog, 80) };
        Assert.Empty(For(state, vehicle, cargo, crew: 0));

        vehicle.Schedule = Route();
        Assert.Equal(new VehicleWarning(VehicleWarningKind.MissingCrew, "v1"), Assert.Single(For(state, vehicle, cargo, crew: 1)));
        Assert.Empty(For(state, vehicle, cargo, crew: 2));

        // Without the count given, the saved crew list counts (on shift around the clock here).
        vehicle.Crew = new List<string> { "e1", "e2" };
        WeekTestData.AllDay(vehicle);
        Assert.Empty(For(state, vehicle, cargo));
        // With part of the route day nobody on shift, the route has a problem.
        vehicle.Schedule.CrewShifts.RemoveAll(s => s.Day == DayOfWeek.Wednesday);
        Assert.Equal(new VehicleWarning(VehicleWarningKind.RouteProblem, "v1"), Assert.Single(For(state, vehicle, cargo)));
        WeekTestData.AllDay(vehicle);
        // A route without stops is no route.
        vehicle.Crew.Clear();
        vehicle.Schedule = new HireSchedule();
        Assert.Empty(For(state, vehicle, cargo));
    }

    [Fact]
    public void RouteProblemWhenTheRouteFailsTodaysRules()
    {
        var (state, vehicle) = Setup();
        vehicle.Crew = new List<string> { "e1" };
        vehicle.Schedule = Route(open: 14 * 60, close: 11 * 60);
        var warning = Assert.Single(For(state, vehicle, new[] { L(HotDogGrill, 1), L(HotDog, 40) }));
        Assert.Equal(VehicleWarningKind.RouteProblem, warning.Kind);
    }

    [Fact]
    public void PermitSpaceAndGrade()
    {
        var (state, vehicle) = Setup();
        var cargo = new[] { L(HotDogGrill, 1), L(HotDog, 40) };
        vehicle.HasPermit = false;
        state.Commissary.Spaces.Clear();
        vehicle.Health = new HealthState { Grade = Grade.C };

        Assert.Equal(new[] { VehicleWarningKind.NoPermit, VehicleWarningKind.NoCommissarySpace, VehicleWarningKind.HealthGradeC },
            For(state, vehicle, cargo).Select(w => w.Kind));

        vehicle.Health.Grade = Grade.B;
        Assert.DoesNotContain(VehicleWarningKind.HealthGradeC, For(state, vehicle, cargo).Select(w => w.Kind));
        // Another vehicle's space doesn't count.
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "v2", Kind = "cart", CommissaryId = "hells-kitchen" });
        Assert.Contains(VehicleWarningKind.NoCommissarySpace, For(state, vehicle, cargo).Select(w => w.Kind));
    }

    [Fact]
    public void TheLicenceWarnsInItsRenewalWindowAndOnceExpired()
    {
        var window = Rules.RenewWindowDays;
        Assert.True(window > 0);
        Assert.Empty(VehicleWarnings.Fleet(Rules, new LicenceState(), 10));

        var licence = new LicenceState { HasVendorLicence = true, LicenceExpiresOnDay = 100 };
        // Days left counts today: on day 100 - window + 1 there are `window` days left.
        Assert.Empty(VehicleWarnings.Fleet(Rules, licence, 100 - window));
        var running = Assert.Single(VehicleWarnings.Fleet(Rules, licence, 100 - window + 1));
        Assert.Equal(VehicleWarningKind.LicenceRunningOut, running.Kind);
        Assert.Null(running.VehicleId);
        Assert.True(running.IsFleet);
        Assert.Equal(VehicleWarningKind.LicenceRunningOut, Assert.Single(VehicleWarnings.Fleet(Rules, licence, 100)).Kind);
        Assert.Equal(VehicleWarningKind.LicenceExpired, Assert.Single(VehicleWarnings.Fleet(Rules, licence, 101)).Kind);
    }

    [Fact]
    public void ReconcileAddsMissingAndCompletesClearedAndDuplicates()
    {
        var low = new VehicleWarning(VehicleWarningKind.LowStock, "v1", HotDog);
        var empty = new VehicleWarning(VehicleWarningKind.EmptyStock, "v1", HotDog);
        var permit = new VehicleWarning(VehicleWarningKind.NoPermit, "v1");
        var otherPermit = new VehicleWarning(VehicleWarningKind.NoPermit, "v2");

        var (add, complete) = VehicleWarnings.Reconcile(new[] { empty, permit, otherPermit }, new[] { low, permit, permit });

        Assert.Equal(new[] { empty, otherPermit }, add);
        Assert.Equal(new[] { low, permit }, complete);
    }

    [Fact]
    public void KindNumbersStayPut()
    {
        // The game side's to-do types are 9200 + these numbers; a saved or shown to-do relies on them.
        Assert.Equal(1, (int)VehicleWarningKind.LowStock);
        Assert.Equal(9, (int)VehicleWarningKind.LicenceExpired);
    }
}
