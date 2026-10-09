using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class SalesSimulatorTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string Burger = "ba:itemname_burger";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Soda = "ba:itemname_sodacan";

    // These tests predate the walk-in: customers join the queue the minute they arrive (SalesWalkInTests
    // covers the walk). Everything else is the shipped queue rules.
    internal static readonly QueueRules NoWalk = new(ShippedData.Rules.Queue.PatienceMinutes, ShippedData.Rules.Queue.PatienceSpread,
        ShippedData.Rules.Queue.RainPatience, ShippedData.Rules.Queue.MaxQueue);

    private static readonly SalesSimulator simulator = new(ShippedData.Tables, NoWalk);

    private static SalesInput Input(double perHour, int servers = 1, int units = 10000, params string[] menu)
    {
        menu = menu.Length == 0 ? new[] { HotDog } : menu;
        return new SalesInput
        {
            Menu = menu,
            Stock = menu.ToDictionary(item => item, _ => units),
            CustomersPerHour = perHour,
            Servers = servers
        };
    }

    // Runs minute by minute, applying the draws to the stock as the game side would.
    private static List<SalesResult> Run(ServiceState state, SalesInput input, int minutes)
    {
        var stock = input.Stock.ToDictionary(p => p.Key, p => p.Value);
        var results = new List<SalesResult>();
        for (var i = 0; i < minutes; i++)
        {
            input.Stock = stock;
            var result = simulator.Advance(state, input, 1);
            foreach (var draw in result.Draws) stock[draw.Key] -= draw.Value;
            results.Add(result);
            if (result.SoldOut) break;
        }
        return results;
    }

    private static List<Sale> Merge(IEnumerable<SalesResult> results) => results.SelectMany(r => r.Sales).ToList();

    private static int Served(IEnumerable<SalesResult> results) => results.Sum(r => r.Count(CustomerExit.Served));

    [Fact]
    public void ACategorySellsOnlyItsChosenItem()
    {
        // Hot dogs and burgers are both fast food; burgers have fewer units but are the chosen item.
        // Once they run out, fast food customers walk on rather than buy hot dogs they may not accept.
        var input = Input(30, units: 0, menu: new[] { HotDog, Burger });
        input.Stock = new Dictionary<string, int> { [HotDog] = 50, [Burger] = 3 };
        input.CategoryItems = new Dictionary<string, string> { ["fastfood"] = Burger };
        var results = Run(SalesSimulator.Open(11), input, 240);
        var sales = Merge(results);

        Assert.Equal(3, sales.Count);
        Assert.All(sales, s => Assert.Equal(Burger, s.Item));
        Assert.True(results.Sum(r => r.Count(CustomerExit.NothingLeft)) > 0);
    }

    [Fact]
    public void AnOrderWhoseItemIsNoLongerOfferedGetsTheCurrentOne()
    {
        // A burger order is at the window when the burger's price stops being accepted: the customer
        // gets the category's new item instead of a burger at a refused price.
        var state = SalesSimulator.Open(13);
        var input = Input(200, units: 0, menu: new[] { HotDog, Burger });
        input.Stock = new Dictionary<string, int> { [HotDog] = 50, [Burger] = 50 };
        input.CategoryItems = new Dictionary<string, string> { ["fastfood"] = Burger };
        simulator.Advance(state, input, 1);
        Assert.Single(state.Window);
        Assert.Equal(Burger, state.Window[0].Item);
        var waiting = state.Window[0].Id;

        input.CategoryItems = new Dictionary<string, string> { ["fastfood"] = HotDog };
        var sales = Merge(Run(state, input, 10));

        Assert.Equal(HotDog, Assert.Single(sales, s => s.CustomerId == waiting).Item);
        Assert.DoesNotContain(sales, s => s.Item == Burger);
    }

    [Fact]
    public void AnOrderWhoseCategoryIsNoLongerOfferedWalksOn()
    {
        var state = SalesSimulator.Open(13);
        var input = Input(200, units: 0, menu: new[] { HotDog, Burger });
        input.Stock = new Dictionary<string, int> { [HotDog] = 50, [Burger] = 50 };
        input.CategoryItems = new Dictionary<string, string> { ["fastfood"] = Burger };
        simulator.Advance(state, input, 1);
        var waiting = Assert.Single(state.Window).Id;

        input.CategoryItems = new Dictionary<string, string>();
        var results = Run(state, input, 10);

        Assert.DoesNotContain(Merge(results), s => s.CustomerId == waiting);
        Assert.Contains(results.SelectMany(r => r.Left), l => l.Id == waiting && l.Exit == CustomerExit.NothingLeft);
    }

    [Fact]
    public void FoodBuyersAddOnlyTheChosenDrink()
    {
        const string Water = "ba:itemname_water";
        var input = Input(30, units: 0, menu: new[] { HotDog, Soda, Water });
        input.Stock = new Dictionary<string, int> { [HotDog] = 500, [Soda] = 5, [Water] = 500 };
        input.CustomerShares = new Dictionary<string, double> { ["fastfood"] = 1.0 };
        input.AddOnsPerCustomer = 1.0;
        input.AddOnItem = Soda;
        var sales = Merge(Run(SalesSimulator.Open(12), input, 240));

        Assert.Equal(5, sales.Count(s => s.AddOn));
        Assert.All(sales.Where(s => s.AddOn), s => Assert.Equal(Soda, s.Item));
        Assert.DoesNotContain(sales, s => s.Item == Water);
    }

    [Fact]
    public void WithoutAChosenItemACategorySellsWhatItHasMostOf()
    {
        var input = Input(30, units: 0, menu: new[] { HotDog, Burger });
        input.Stock = new Dictionary<string, int> { [HotDog] = 50, [Burger] = 3 };
        var sales = Merge(Run(SalesSimulator.Open(11), input, 60));

        Assert.NotEmpty(sales);
        Assert.All(sales, s => Assert.Equal(HotDog, s.Item));
    }

    [Fact]
    public void TheSameSeedGivesTheSameDay()
    {
        var first = SalesSimulator.Open(42);
        var second = SalesSimulator.Open(42);

        var a = simulator.Advance(first, Input(30), 600);
        var b = simulator.Advance(second, Input(30), 600);

        Assert.Equal(a.Sales.Select(s => (s.CustomerId, s.Item, s.Minute)), b.Sales.Select(s => (s.CustomerId, s.Item, s.Minute)));
        Assert.Equal(a.Left, b.Left);
        Assert.Equal(first.RandomState, second.RandomState);
    }

    [Fact]
    public void DefaultServiceInputsMatchExplicitDefaults()
    {
        var first = SalesSimulator.Open(42);
        var second = SalesSimulator.Open(42);
        var defaults = Input(100, menu: new[] { HotDog, Soda });
        var explicitDefaults = Input(100, menu: new[] { HotDog, Soda });
        defaults.AddOnsPerCustomer = explicitDefaults.AddOnsPerCustomer = 0.2;
        explicitDefaults.ServiceSpeed = 1.0;
        explicitDefaults.QueueLimit = 0;

        var a = simulator.Advance(first, defaults, 600);
        var b = simulator.Advance(second, explicitDefaults, 600);

        Assert.Equal(a.Sales.Select(s => (s.CustomerId, s.Item, s.AddOn, s.Minute)), b.Sales.Select(s => (s.CustomerId, s.Item, s.AddOn, s.Minute)));
        Assert.Equal(a.Joined, b.Joined);
        Assert.Equal(a.Left, b.Left);
        Assert.Equal(a.Draws, b.Draws);
        Assert.Equal(a.MinutesSimulated, b.MinutesSimulated);
        Assert.Equal(a.Skipped, b.Skipped);
        Assert.Equal(a.SoldOut, b.SoldOut);
        Assert.Equal(Newtonsoft.Json.JsonConvert.SerializeObject(first), Newtonsoft.Json.JsonConvert.SerializeObject(second));
    }

    [Fact]
    public void AnotherSeedGivesOtherCustomers()
    {
        var a = simulator.Advance(SalesSimulator.Open(1), Input(30), 600);
        var b = simulator.Advance(SalesSimulator.Open(2), Input(30), 600);

        Assert.NotEqual(a.Sales.Select(s => s.Minute), b.Sales.Select(s => s.Minute));
    }

    [Fact]
    public void MinuteTicksMatchOneLongCall()
    {
        var ticked = SalesSimulator.Open(7);
        var whole = SalesSimulator.Open(7);

        var byMinute = Enumerable.Range(0, 300).Select(_ => simulator.Advance(ticked, Input(25), 1)).ToList();
        var once = simulator.Advance(whole, Input(25), 300);

        Assert.Equal(once.Sales.Select(s => s.CustomerId), byMinute.SelectMany(r => r.Sales).Select(s => s.CustomerId));
        Assert.Equal(whole.RandomState, ticked.RandomState);
    }

    [Fact]
    public void ASaveAndLoadReplaysTheSameMinutes()
    {
        var codec = new SaveCodec();
        var kept = SalesSimulator.Open(99);
        simulator.Advance(kept, Input(40), 45);
        var save = new SaveState();
        save.Vehicles.Add(new VehicleState { VehicleId = "cart-1", IsOpen = true, Service = kept });

        var loaded = codec.Load(codec.Serialize(save)).State!.Vehicles[0].Service!;
        var afterLoad = simulator.Advance(loaded, Input(40), 120);
        var withoutSave = simulator.Advance(kept, Input(40), 120);

        Assert.Equal(withoutSave.Sales.Select(s => (s.CustomerId, s.Minute)), afterLoad.Sales.Select(s => (s.CustomerId, s.Minute)));
        Assert.Equal(withoutSave.Left, afterLoad.Left);
    }

    [Fact]
    public void QuietSpotSellsToNearlyEveryoneWhoComes()
    {
        var results = Run(SalesSimulator.Open(3), Input(10), 60 * 100);

        // 10 an hour for 100 hours, all served by a worker who could do 20.
        Assert.InRange(Served(results), 900, 1100);
        Assert.Equal(0, results.Sum(r => r.Count(CustomerExit.GaveUp) + r.Count(CustomerExit.QueueFull)));
    }

    [Fact]
    public void ConversionThinsTheCustomers()
    {
        var input = Input(20);
        input.Conversion = 0.5;

        var results = Run(SalesSimulator.Open(4), input, 60 * 100);

        Assert.InRange(Served(results), 900, 1100);
    }

    [Fact]
    public void OneWorkerServesTwentyHotDogsAnHourWhateverTheCrowd()
    {
        // Hot dogs take 3 minutes: one worker serves 20 an hour, the rest queue and give up.
        var results = Run(SalesSimulator.Open(5), Input(60), 600);

        Assert.InRange(Served(results), 198, 201);
        Assert.True(results.Sum(r => r.Count(CustomerExit.GaveUp) + r.Count(CustomerExit.QueueFull)) > 300);
    }

    [Theory]
    [InlineData(2.0, 398, 400)]
    [InlineData(0.5, 98, 100)]
    public void ServiceSpeedScalesHotDogsPerHour(double speed, int minimum, int maximum)
    {
        var input = Input(200);
        input.ServiceSpeed = speed;

        var results = Run(SalesSimulator.Open(5), input, 600);

        Assert.InRange(Served(results), minimum, maximum);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnusableServiceSpeedsUseTheBaseTime(double speed)
    {
        var input = Input(200);
        input.ServiceSpeed = speed;

        var results = Run(SalesSimulator.Open(5), input, 600);

        Assert.InRange(Served(results), 198, 200);
    }

    [Fact]
    public void ServiceSpeedIsCappedAtTen()
    {
        var capped = Input(3600);
        capped.ServiceSpeed = 10;
        var above = Input(3600);
        above.ServiceSpeed = 100;

        var a = simulator.Advance(SalesSimulator.Open(5), capped, 60);
        var b = simulator.Advance(SalesSimulator.Open(5), above, 60);

        Assert.InRange(a.Count(CustomerExit.Served), 198, 200);
        Assert.Equal(a.Sales.Select(s => (s.CustomerId, s.Minute)), b.Sales.Select(s => (s.CustomerId, s.Minute)));
        Assert.Equal(a.Left, b.Left);
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(100, 12)]
    [InlineData(0, 12)]
    [InlineData(-1, 12)]
    public void QueueLimitsTurnExcessArrivalsAway(int limit, int expected)
    {
        var state = SalesSimulator.Open(5);
        var input = Input(3600, servers: 0);
        input.QueueLimit = limit;
        var turnedAway = 0;
        var longest = 0;
        for (var minute = 0; minute < 60; minute++)
        {
            var result = simulator.Advance(state, input, 1);
            Assert.InRange(state.Queue.Count, 0, expected);
            Assert.True(result.Joined.Count <= expected);
            turnedAway += result.Count(CustomerExit.QueueFull);
            longest = Math.Max(longest, state.Queue.Count);
        }

        // Someone whose patience ends at a minute's close can leave the last minute one short.
        Assert.Equal(expected, longest);
        Assert.True(turnedAway > 0);
    }

    [Fact]
    public void OrdersAtTheWindowDoNotCountTowardTheQueueLimit()
    {
        var state = SalesSimulator.Open(5);
        var input = Input(3600);
        input.ServiceSpeed = 0.5;
        input.QueueLimit = 6;

        simulator.Advance(state, input, 2);

        Assert.Equal(6, state.Queue.Count);
        Assert.Single(state.Window);
    }

    [Fact]
    public void ThreeWorkersServeThreeTimesAsMany()
    {
        var results = Run(SalesSimulator.Open(5), Input(200, servers: 3), 600);

        Assert.InRange(Served(results), 597, 600);
    }

    [Fact]
    public void ASlowItemSlowsTheQueue()
    {
        // Burgers take 5 minutes: 12 an hour.
        var results = Run(SalesSimulator.Open(6), Input(60, menu: Burger), 600);

        Assert.InRange(Served(results), 118, 121);
    }

    [Fact]
    public void NobodyWaitsLongerThanTheirPatience()
    {
        var rules = ShippedData.Rules.Queue;
        var state = SalesSimulator.Open(8);
        var arrived = new Dictionary<long, long>();
        var waits = new List<long>();
        for (var minute = 0; minute < 600; minute++)
        {
            var result = simulator.Advance(state, Input(30, servers: 0), 1);
            foreach (var id in result.Joined) arrived[id] = minute;
            foreach (var (id, exit) in result.Left)
                if (exit == CustomerExit.GaveUp) waits.Add(minute + 1 - arrived[id]);
        }

        Assert.NotEmpty(waits);
        Assert.InRange(waits.Min(), Math.Floor(rules.PatienceMinutes * (1 - rules.PatienceSpread)), rules.PatienceMinutes);
        Assert.InRange(waits.Max(), rules.PatienceMinutes, Math.Ceiling(rules.PatienceMinutes * (1 + rules.PatienceSpread)));
        Assert.InRange(waits.Average(), rules.PatienceMinutes - 2, rules.PatienceMinutes + 2);
    }

    [Fact]
    public void RainMakesCustomersGiveUpSooner()
    {
        var rules = ShippedData.Rules.Queue;
        var input = Input(30, servers: 0);
        input.Raining = true;
        var state = SalesSimulator.Open(8);
        var arrived = new Dictionary<long, long>();
        var waits = new List<long>();
        for (var minute = 0; minute < 600; minute++)
        {
            var result = simulator.Advance(state, input, 1);
            foreach (var id in result.Joined) arrived[id] = minute;
            foreach (var (id, exit) in result.Left)
                if (exit == CustomerExit.GaveUp) waits.Add(minute + 1 - arrived[id]);
        }

        Assert.InRange(waits.Average(), rules.PatienceMinutes * rules.RainPatience - 2, rules.PatienceMinutes * rules.RainPatience + 2);
    }

    [Fact]
    public void RainStartingThinsAQueueThatIsAlreadyWaiting()
    {
        var state = SalesSimulator.Open(9);
        simulator.Advance(state, Input(30, servers: 0), 10);
        var waiting = state.Queue.Count;
        var rain = Input(0, servers: 0);
        rain.Raining = true;

        var result = simulator.Advance(state, rain, 5);

        Assert.True(waiting > 0);
        // Ten minutes in, rain cuts patience to 9 to 15 minutes: the first arrivals leave at once.
        Assert.True(result.Count(CustomerExit.GaveUp) > 0);
    }

    [Fact]
    public void ACustomerWhoseWaitRainEndedIsNotServed()
    {
        // Waited 15 minutes with patience 20: in rain (12) they should leave before the free worker calls them.
        var state = SalesSimulator.Open(32);
        state.Minute = 15;
        state.NextCustomerId = 2;
        state.Queue.Add(new ServiceCustomer { Id = 1, ArrivedAtMinute = 0, Patience = 20, Category = "fastfood" });
        var rain = Input(0);
        rain.Raining = true;

        var result = simulator.Advance(state, rain, 1);

        Assert.Equal(new[] { (1L, CustomerExit.GaveUp) }, result.Left);
        Assert.Empty(state.Window);
    }

    [Fact]
    public void ThePlayerCantServeSomeoneWhoHasGivenUp()
    {
        var state = SalesSimulator.Open(33);
        state.Minute = 15;
        state.NextCustomerId = 2;
        state.Queue.Add(new ServiceCustomer { Id = 1, ArrivedAtMinute = 0, Patience = 20, Category = "fastfood" });
        var rain = Input(0, servers: 0);
        rain.Raining = true;

        var result = simulator.ServeHead(state, rain);

        Assert.Empty(result.Sales);
        Assert.Contains((1L, CustomerExit.GaveUp), result.Left);
    }

    [Fact]
    public void AddOnsWithoutSharesStillAverageTheirRate()
    {
        var input = Input(15, menu: new[] { HotDog, Soda });
        input.AddOnsPerCustomer = 0.2;

        var sales = Merge(Run(SalesSimulator.Open(34), input, 60 * 100));

        var customers = sales.Where(s => !s.AddOn).Select(s => s.CustomerId).Distinct().Count();
        Assert.InRange(sales.Count(s => s.AddOn) / (double)customers, 0.16, 0.24);
    }

    [Fact]
    public void TheLastHotDogStillBringsItsDrink()
    {
        var state = SalesSimulator.Open(35);
        state.NextCustomerId = 2;
        state.Queue.Add(new ServiceCustomer { Id = 1, Patience = 20, Category = "fastfood" });
        var input = Input(0, menu: new[] { HotDog, Soda });
        input.Stock = new Dictionary<string, int> { [HotDog] = 1, [Soda] = 10 };
        input.AddOnsPerCustomer = 0.5;

        var result = simulator.Advance(state, input, 5);

        // Without shares the main share is a half, so 0.5 per customer is one drink per hot dog.
        Assert.Contains(result.Sales, s => s.AddOn && s.Item == Soda);
    }

    [Fact]
    public void SellingOutCountsWaitingCustomersAsClosedNotGaveUp()
    {
        var state = SalesSimulator.Open(36);
        state.Minute = 19;
        state.NextCustomerId = 3;
        state.Window.Add(new ServiceCustomer { Id = 1, Patience = 20, Category = "fastfood", Item = HotDog, MinutesLeft = 0.5 });
        state.Queue.Add(new ServiceCustomer { Id = 2, ArrivedAtMinute = 0, Patience = 20, Category = "fastfood" });

        var result = simulator.Advance(state, Input(0, units: 1), 1);

        Assert.True(result.SoldOut);
        Assert.Contains((2L, CustomerExit.Closed), result.Left);
        Assert.DoesNotContain((2L, CustomerExit.GaveUp), result.Left);
    }

    [Fact]
    public void TheOrderThatFinishesFirstGetsTheLastUnit()
    {
        var state = SalesSimulator.Open(37);
        state.NextCustomerId = 3;
        state.Window.Add(new ServiceCustomer { Id = 1, Patience = 20, Category = "fastfood", Item = HotDog, MinutesLeft = 0.8 });
        state.Window.Add(new ServiceCustomer { Id = 2, Patience = 20, Category = "fastfood", Item = HotDog, MinutesLeft = 0.2 });

        var result = simulator.Advance(state, Input(0, servers: 2, units: 1), 1);

        Assert.Equal(2, Assert.Single(result.Sales).CustomerId);
        Assert.Contains((1L, CustomerExit.Closed), result.Left);
    }

    [Fact]
    public void AFreedWorkerStartsTheNextCustomerMidMinute()
    {
        // A soda takes a minute: a worker with 0.5 left finishes, then makes half of the next one.
        var state = SalesSimulator.Open(38);
        state.NextCustomerId = 3;
        state.Window.Add(new ServiceCustomer { Id = 1, Patience = 20, Category = "colddrinks", Item = Soda, MinutesLeft = 0.5 });
        state.Queue.Add(new ServiceCustomer { Id = 2, Patience = 20, Category = "colddrinks" });

        simulator.Advance(state, Input(0, menu: Soda), 1);

        Assert.Equal(2, Assert.Single(state.Window).Id);
        Assert.Equal(0.5, state.Window[0].MinutesLeft, 9);
    }

    [Fact]
    public void TheQueueNeverPassesItsCap()
    {
        var state = SalesSimulator.Open(10);
        var results = new List<SalesResult>();
        for (var i = 0; i < 120; i++)
        {
            results.Add(simulator.Advance(state, Input(300, servers: 0), 1));
            Assert.True(state.Queue.Count <= ShippedData.Rules.Queue.MaxQueue);
        }

        Assert.Equal(ShippedData.Rules.Queue.MaxQueue, state.Queue.Count);
        Assert.True(results.Sum(r => r.Count(CustomerExit.QueueFull)) > 0);
    }

    [Fact]
    public void EachSaleDrawsOneUnit()
    {
        var input = Input(40, units: 1000, menu: new[] { HotDog, Coffee });
        var result = simulator.Advance(SalesSimulator.Open(11), input, 300);

        Assert.Equal(result.Sales.Count, result.Draws.Values.Sum());
        foreach (var item in result.Draws.Keys)
            Assert.Equal(result.Sales.Count(s => s.Item == item), result.Draws[item]);
    }

    [Fact]
    public void SellingOutStopsSalesAndSendsTheQueueAway()
    {
        var state = SalesSimulator.Open(12);
        var results = Run(state, Input(60, units: 15), 600);

        Assert.Equal(15, Served(results));
        Assert.True(results.Last().SoldOut);
        Assert.Empty(state.Queue);
        Assert.Empty(state.Window);
        Assert.Contains(results.Last().Left, l => l.Exit == CustomerExit.Closed);
    }

    [Fact]
    public void AnEmptyVehicleIsSoldOutAtOnce()
    {
        var result = simulator.Advance(SalesSimulator.Open(13), Input(60, units: 0), 10);

        Assert.True(result.SoldOut);
        Assert.Empty(result.Sales);
        Assert.Equal(0, result.MinutesSimulated);
    }

    [Fact]
    public void ASoldOutItemDropsOffTheMenu()
    {
        var input = Input(40, units: 1000, menu: new[] { HotDog, Coffee });
        input.Stock = new Dictionary<string, int> { [HotDog] = 0, [Coffee] = 1000 };

        var result = simulator.Advance(SalesSimulator.Open(14), input, 300);

        Assert.DoesNotContain(result.Sales, s => s.Item == HotDog);
        Assert.Contains(result.Sales, s => s.Item == Coffee);
        Assert.False(result.SoldOut);
    }

    [Fact]
    public void CustomersWhoseFoodRanOutLeave()
    {
        var input = Input(40, units: 1000, menu: new[] { HotDog, Coffee });
        input.Stock = new Dictionary<string, int> { [HotDog] = 0, [Coffee] = 1000 };
        input.CustomerShares = new Dictionary<string, double> { ["fastfood"] = 1.0 };

        var result = simulator.Advance(SalesSimulator.Open(15), input, 60);

        Assert.Empty(result.Sales);
        Assert.True(result.Count(CustomerExit.NothingLeft) > 0);
        Assert.Empty(result.Joined);
    }

    [Fact]
    public void ASoldOutCategoryDoesntTurnIntoDrinkSales()
    {
        var input = Input(30, servers: 3, menu: new[] { HotDog, Soda });
        input.CustomerShares = new Dictionary<string, double> { ["fastfood"] = 0.9, ["colddrinks"] = 0.1 };
        var stocked = simulator.Advance(SalesSimulator.Open(30), input, 600);
        input.Stock = new Dictionary<string, int> { [HotDog] = 0, [Soda] = 10000 };

        var soldOut = simulator.Advance(SalesSimulator.Open(30), input, 600);

        var drinksBefore = stocked.Sales.Count(s => s.Item == Soda && !s.AddOn);
        var drinksAfter = soldOut.Sales.Count(s => s.Item == Soda && !s.AddOn);
        Assert.InRange(drinksAfter, drinksBefore * 0.5, drinksBefore * 1.5 + 5);
        Assert.True(soldOut.Count(CustomerExit.NothingLeft) > 200);
    }

    [Fact]
    public void SellingTheLastUnitSendsTheQueueAwayAsClosed()
    {
        var state = SalesSimulator.Open(31);
        simulator.Advance(state, Input(200, servers: 0), 5);
        Assert.True(state.Queue.Count > 3);

        var result = simulator.Advance(state, Input(0, servers: 1, units: 1), 10);

        Assert.True(result.SoldOut);
        Assert.Equal(1, result.Count(CustomerExit.Served));
        Assert.Equal(0, result.Count(CustomerExit.NothingLeft));
        Assert.True(result.Count(CustomerExit.Closed) > 2);
    }

    [Fact]
    public void CustomerSharesPickTheCategory()
    {
        var input = Input(30, servers: 3, menu: new[] { HotDog, Coffee });
        input.CustomerShares = new Dictionary<string, double> { ["coffee"] = 0.75, ["fastfood"] = 0.25 };

        var result = Merge(Run(SalesSimulator.Open(16), input, 60 * 40));

        var coffeeShare = result.Count(s => s.Item == Coffee) / (double)result.Count;
        Assert.InRange(coffeeShare, 0.70, 0.80);
    }

    [Fact]
    public void AddOnDrinksRideWithMainOrders()
    {
        var input = Input(15, menu: new[] { HotDog, Soda });
        input.CustomerShares = new Dictionary<string, double> { ["fastfood"] = 0.8, ["colddrinks"] = 0.2 };
        input.AddOnsPerCustomer = 0.16;

        var result = Merge(Run(SalesSimulator.Open(17), input, 60 * 100));

        var mains = result.Count(s => !s.AddOn && s.Item == HotDog);
        var addOns = result.Where(s => s.AddOn).ToList();
        Assert.All(addOns, s => Assert.Equal(Soda, s.Item));
        // 0.16 per customer over the 80% who came for food: 0.2 per hot dog.
        Assert.InRange(addOns.Count / (double)mains, 0.17, 0.23);
        Assert.DoesNotContain(addOns, a => result.Any(s => !s.AddOn && s.CustomerId == a.CustomerId && s.Item == Soda));
    }

    [Fact]
    public void ThePlayerServesTheHeadOfTheQueue()
    {
        var state = SalesSimulator.Open(18);
        simulator.Advance(state, Input(30, servers: 0), 10);
        var head = state.Queue[0].Id;
        var waiting = state.Queue.Count;

        var result = simulator.ServeHead(state, Input(30, servers: 0));

        Assert.Single(result.Sales);
        Assert.Equal(head, result.Sales[0].CustomerId);
        Assert.Equal(waiting - 1, state.Queue.Count);
    }

    [Fact]
    public void ServingAnEmptyQueueSellsNothing()
    {
        var result = simulator.ServeHead(SalesSimulator.Open(19), Input(30, servers: 0));

        Assert.Empty(result.Sales);
    }

    [Fact]
    public void AHireGoingHomePutsTheirOrderBackAtTheHead()
    {
        var state = SalesSimulator.Open(20);
        simulator.Advance(state, Input(200, servers: 3, menu: Burger), 3);
        Assert.Equal(3, state.Window.Count);

        simulator.Advance(state, Input(0, servers: 1, menu: Burger), 1);

        Assert.Single(state.Window);
        Assert.Null(state.Queue[0].Item);
        // Back in line, their wait starts again.
        Assert.Equal(state.Minute - 1, state.Queue[0].ArrivedAtMinute);
    }

    [Fact]
    public void ClosingSendsEveryoneAway()
    {
        var state = SalesSimulator.Open(21);
        simulator.Advance(state, Input(100), 15);
        var waiting = state.Queue.Count + state.Window.Count;

        var result = SalesSimulator.Close(state);

        Assert.Equal(waiting, result.Count(CustomerExit.Closed));
        Assert.Empty(state.Queue);
        Assert.Empty(state.Window);
    }

    [Fact]
    public void TooManyMinutesAreSkippedLikeSalesTick()
    {
        var state = SalesSimulator.Open(22);

        var result = simulator.Advance(state, Input(30), SalesSimulator.MaxMinutesPerCall + 1);

        Assert.True(result.Skipped);
        Assert.Equal(0, state.Minute);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-5)]
    [InlineData(0)]
    public void UnusableRatesBringNobody(double perHour)
    {
        var result = simulator.Advance(SalesSimulator.Open(23), Input(perHour), 120);

        Assert.Empty(result.Joined);
        Assert.Empty(result.Sales);
    }

    [Fact]
    public void ItemsOffTheMenuAreNeverSold()
    {
        var input = Input(30);
        input.Stock = new Dictionary<string, int> { [HotDog] = 100, [Coffee] = 100 };

        var result = simulator.Advance(SalesSimulator.Open(24), input, 300);

        Assert.All(result.Sales, s => Assert.Equal(HotDog, s.Item));
    }

    [Fact]
    public void CustomersWithAnUnknownCategoryLeaveWhenLoaded()
    {
        var state = SalesSimulator.Open(25);
        state.NextCustomerId = 3;
        state.Queue.Add(new ServiceCustomer { Id = 1, Patience = 20, Category = "noodles" });
        state.Queue.Add(new ServiceCustomer { Id = 2, Patience = 20, Category = "fastfood" });

        var result = simulator.Advance(state, Input(0), 5);

        Assert.Contains((1L, CustomerExit.NothingLeft), result.Left);
        Assert.Contains(result.Sales, s => s.CustomerId == 2);
    }

    [Fact]
    public void SeedsAreStable()
    {
        // Fixed values: a change here would give every saved vehicle other customers.
        Assert.Equal(SeededRandom.SeedFor("cart-1", 3), SeededRandom.SeedFor("cart-1", 3));
        Assert.NotEqual(SeededRandom.SeedFor("cart-1", 3), SeededRandom.SeedFor("cart-1", 4));
        Assert.Equal(-3802972073786313226L, SeededRandom.SeedFor("cart-1", 3));
        Assert.Equal(0xE220A8397B1DCDAFUL, new SeededRandom(0).NextULong());
    }
}

public class ServiceStateSaveTests
{
    [Theory]
    [InlineData("{\"id\":1,\"patience\":0,\"category\":\"fastfood\"}")]
    [InlineData("{\"id\":5,\"patience\":20,\"category\":\"fastfood\"}")]
    [InlineData("{\"id\":1,\"patience\":20,\"category\":\"fastfood\",\"minutesLeft\":-1}")]
    [InlineData("{\"id\":1,\"patience\":20,\"arrivedAtMinute\":9,\"category\":\"fastfood\"}")]
    [InlineData("null")]
    public void BadQueueEntriesMakeTheSaveCorrupt(string customer)
    {
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"cart-1\",\"service\":{\"minute\":5,\"nextCustomerId\":2,\"queue\":[" +
                   customer + "],\"window\":[]}}],\"dailySales\":[]}";

        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load(json).Status);
    }

    [Fact]
    public void AVehicleWithoutServiceStillLoads()
    {
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"cart-1\"}],\"dailySales\":[]}";

        var result = new SaveCodec().Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Null(result.State!.Vehicles[0].Service);
    }
}
