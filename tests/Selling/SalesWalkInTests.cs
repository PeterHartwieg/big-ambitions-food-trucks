using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

// Customers walk up to the line before they join the queue (queue.walkInMinutes in rules.json).
public class SalesWalkInTests
{
    private const string HotDog = "ba:itemname_hotdog";

    private static readonly SalesSimulator simulator = new(ShippedData.Tables, ShippedData.Rules.Queue);

    private static SalesInput Input(double perHour, int servers = 1, int queueLimit = 0) => new()
    {
        Menu = new[] { HotDog },
        Stock = new Dictionary<string, int> { [HotDog] = 100000 },
        CustomersPerHour = perHour,
        Servers = servers,
        QueueLimit = queueLimit
    };

    private static ServiceCustomer Walking(long id, long joinsAt, double patience = 20) =>
        new() { Id = id, ArrivedAtMinute = 0, JoinsAtMinute = joinsAt, Patience = patience, Category = "fastfood" };

    [Fact]
    public void ArrivalsWalkUpBeforeTheyJoinTheQueue()
    {
        var state = SalesSimulator.Open(3);

        var first = simulator.Advance(state, Input(600, servers: 2), 1);

        Assert.NotEmpty(first.Approached);
        Assert.Empty(first.Joined);
        Assert.Empty(first.Sales);
        Assert.Empty(state.Queue);
        Assert.Equal(first.Approached, state.Approaching.Select(c => c.Id));
        var rules = ShippedData.Rules.Queue;
        Assert.All(state.Approaching, c => Assert.InRange(c.JoinsAtMinute, rules.WalkInMinMinutes, rules.WalkInMaxMinutes));
    }

    [Fact]
    public void NobodyIsServedBeforeTheShortestWalkIsOver()
    {
        var state = SalesSimulator.Open(3);
        var input = Input(600, servers: 3);

        var early = simulator.Advance(state, input, ShippedData.Rules.Queue.WalkInMinMinutes);

        Assert.Empty(early.Sales);
        Assert.Empty(early.Joined);
        Assert.Empty(state.Window);
        Assert.Empty(state.Queue);

        var later = simulator.Advance(state, input, 1);
        Assert.NotEmpty(later.Joined);
        // Everyone sold to had joined the queue first.
        var joined = new HashSet<long>(early.Joined.Concat(later.Joined));
        Assert.All(later.Sales, s => Assert.Contains(s.CustomerId, joined));
    }

    [Fact]
    public void ACustomerJoinsAtTheEndOfTheirWalk()
    {
        var state = SalesSimulator.Open(1);
        state.NextCustomerId = 2;
        state.Approaching.Add(Walking(1, joinsAt: 5));
        var input = Input(0, servers: 0);

        var walking = simulator.Advance(state, input, 5);
        Assert.Empty(walking.Joined);
        Assert.Single(state.Approaching);

        var arrived = simulator.Advance(state, input, 1);
        Assert.Equal(new long[] { 1 }, arrived.Joined);
        Assert.Empty(state.Approaching);
        Assert.Equal(5, Assert.Single(state.Queue).ArrivedAtMinute);
    }

    [Fact]
    public void PatienceCountsFromJoiningNotFromArriving()
    {
        var state = SalesSimulator.Open(1);
        state.NextCustomerId = 2;
        // Turned up at minute 0, joins at 10, and waits 5 minutes in line.
        state.Approaching.Add(Walking(1, joinsAt: 10, patience: 5));
        var input = Input(0, servers: 0);

        var waiting = simulator.Advance(state, input, 14);
        Assert.DoesNotContain(waiting.Left, l => l.Id == 1);
        Assert.Single(state.Queue);

        var tired = simulator.Advance(state, input, 1);
        Assert.Contains((1L, CustomerExit.GaveUp), tired.Left);
        Assert.Empty(state.Queue);
    }

    [Fact]
    public void RainDoesNotShortenTheWalk()
    {
        var state = SalesSimulator.Open(1);
        state.NextCustomerId = 2;
        state.Approaching.Add(Walking(1, joinsAt: 13, patience: 5));
        var input = Input(0, servers: 0);
        input.Raining = true;

        var result = simulator.Advance(state, input, 14);

        Assert.Empty(result.Left);
        Assert.Single(state.Queue);
    }

    [Fact]
    public void TheQueueTakesCustomersInTheOrderTheyReachIt()
    {
        var state = SalesSimulator.Open(1);
        state.NextCustomerId = 5;
        state.Approaching.Add(Walking(1, joinsAt: 9));
        state.Approaching.Add(Walking(2, joinsAt: 4));
        state.Approaching.Add(Walking(4, joinsAt: 6));
        state.Approaching.Add(Walking(3, joinsAt: 6));

        simulator.Advance(state, Input(0, servers: 0), 10);

        Assert.Equal(new long[] { 2, 3, 4, 1 }, state.Queue.Select(c => c.Id));
    }

    [Fact]
    public void AFullQueueIsFoundOnReachingIt()
    {
        var state = SalesSimulator.Open(1);
        state.NextCustomerId = 4;
        state.Approaching.Add(Walking(1, joinsAt: 2));
        state.Approaching.Add(Walking(2, joinsAt: 2));
        state.Approaching.Add(Walking(3, joinsAt: 3));

        var result = simulator.Advance(state, Input(0, servers: 0, queueLimit: 1), 4);

        Assert.Equal(new long[] { 1 }, result.Joined);
        Assert.Contains((2L, CustomerExit.QueueFull), result.Left);
        Assert.Contains((3L, CustomerExit.QueueFull), result.Left);
        Assert.Equal(1, Assert.Single(state.Queue).Id);
    }

    [Fact]
    public void ClosingSendsAwayThoseStillWalkingUp()
    {
        var state = SalesSimulator.Open(3);
        simulator.Advance(state, Input(600), 9);
        var walking = state.Approaching.Select(c => c.Id).ToList();
        Assert.NotEmpty(walking);

        var closed = SalesSimulator.Close(state);

        Assert.All(walking, id => Assert.Contains((id, CustomerExit.Closed), closed.Left));
        Assert.Empty(state.Approaching);
        Assert.Empty(state.Queue);
        Assert.Empty(state.Window);
    }

    [Fact]
    public void SellingOutSendsAwayThoseStillWalkingUp()
    {
        var state = SalesSimulator.Open(3);
        var input = Input(600, servers: 3);
        input.Stock = new Dictionary<string, int> { [HotDog] = 3 };

        var results = new List<SalesResult>();
        for (var i = 0; i < 30 && !results.Any(r => r.SoldOut); i++)
        {
            var result = simulator.Advance(state, input, 1);
            input.Stock = new Dictionary<string, int> { [HotDog] = input.Stock[HotDog] - result.Draws.GetValueOrDefault(HotDog) };
            results.Add(result);
        }

        Assert.True(results.Last().SoldOut);
        Assert.Contains(results.Last().Left, l => l.Exit == CustomerExit.Closed);
        Assert.Empty(state.Approaching);
    }

    [Fact]
    public void TheWalkDoesNotChangeHowManyAWindowServes()
    {
        // Busy and quiet hours alike: the walk delays each customer but loses none.
        foreach (var perHour in new[] { 30.0, 90.0, 300.0 })
        {
            var walk = Serve(simulator, perHour);
            var none = Serve(new SalesSimulator(ShippedData.Tables, SalesSimulatorTests.NoWalk), perHour);
            Assert.InRange(walk, none * 0.95, none * 1.05);
        }

        static int Serve(SalesSimulator sim, double perHour)
        {
            var state = SalesSimulator.Open(11);
            var input = Input(perHour, queueLimit: 6);
            return Enumerable.Range(0, 20).Sum(_ => sim.Advance(state, input, 60).Count(CustomerExit.Served));
        }
    }

    [Fact]
    public void TheSameSeedWalksTheSameCustomers()
    {
        var a = SalesSimulator.Open(8);
        var b = SalesSimulator.Open(8);

        var ra = simulator.Advance(a, Input(120), 30);
        var rb = simulator.Advance(b, Input(120), 30);

        Assert.Equal(ra.Approached, rb.Approached);
        Assert.Equal(ra.Joined, rb.Joined);
        Assert.Equal(a.Approaching.Select(c => c.JoinsAtMinute), b.Approaching.Select(c => c.JoinsAtMinute));
    }
}

public class WalkInSaveTests
{
    private static string Save(string service) =>
        "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"cart-1\",\"service\":" + service + "}],\"dailySales\":[]}";

    [Fact]
    public void CustomersWalkingUpSurviveASaveAndLoad()
    {
        var state = new SaveState();
        var service = SalesSimulator.Open(4);
        new SalesSimulator(ShippedData.Tables, ShippedData.Rules.Queue).Advance(service, new SalesInput
        {
            Menu = new[] { "ba:itemname_hotdog" },
            Stock = new Dictionary<string, int> { ["ba:itemname_hotdog"] = 1000 },
            CustomersPerHour = 300,
            Servers = 1
        }, 20);
        Assert.NotEmpty(service.Approaching);
        state.Vehicles.Add(new VehicleState { VehicleId = "cart-1", IsOpen = true, Service = service });
        var codec = new SaveCodec();

        var loaded = codec.Load(codec.Serialize(state));

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var back = loaded.State!.Vehicles[0].Service!;
        Assert.Equal(service.Approaching.Select(c => (c.Id, c.ArrivedAtMinute, c.JoinsAtMinute, c.Patience, c.Category)),
            back.Approaching.Select(c => (c.Id, c.ArrivedAtMinute, c.JoinsAtMinute, c.Patience, c.Category)));
        Assert.Equal(service.Queue.Select(c => c.Id), back.Queue.Select(c => c.Id));
    }

    [Fact]
    public void AQueueSavedBeforeTheWalkInLoadsWithNobodyWalkingUp()
    {
        var json = Save("{\"minute\":5,\"nextCustomerId\":2,\"queue\":[{\"id\":1,\"patience\":20,\"category\":\"fastfood\"}],\"window\":[]}");

        var loaded = new SaveCodec().Load(json);

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var service = loaded.State!.Vehicles[0].Service!;
        Assert.Empty(service.Approaching);
        Assert.Single(service.Queue);
    }

    [Theory]
    // An ID at or above NextCustomerId, one shared with the queue, and a negative join minute.
    [InlineData("[{\"id\":4,\"patience\":20,\"category\":\"fastfood\",\"joinsAtMinute\":9}]")]
    [InlineData("[{\"id\":1,\"patience\":20,\"category\":\"fastfood\",\"joinsAtMinute\":9}]")]
    [InlineData("[{\"id\":3,\"patience\":20,\"category\":\"fastfood\",\"joinsAtMinute\":-1}]")]
    [InlineData("[{\"id\":3,\"patience\":0,\"category\":\"fastfood\",\"joinsAtMinute\":9}]")]
    public void BadWalkingCustomersMakeTheSaveCorrupt(string approaching)
    {
        var json = Save("{\"minute\":5,\"nextCustomerId\":4,\"queue\":[{\"id\":1,\"patience\":20,\"category\":\"fastfood\"}],\"window\":[]," +
                        "\"approaching\":" + approaching + "}");

        Assert.Equal(LoadStatus.Corrupt, new SaveCodec().Load(json).Status);
    }
}
