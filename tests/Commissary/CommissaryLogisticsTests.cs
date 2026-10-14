using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissaryLogisticsTests
{
    private static readonly Dictionary<string, long> Prices = new() { ["tacos"] = 150, ["corn"] = 60, ["kabob"] = 200 };

    private static KeyValuePair<string, int> Target(string item, int amount) => new(item, amount);

    [Fact]
    public void ThePlanFillsEachTargetUpFromWhatTheCommissaryHolds()
    {
        var state = CommissariesTests.Rented();
        CommissaryLogistics.Receive(state, "a", "tacos", 30, 150, 1, "w");
        Commissaries.ReturnToStock(state, "a", new[] { CommissariesTests.Line("tacos", 20, 140) });

        var plan = CommissaryLogistics.Plan(state, "a", new[] { Target("tacos", 80), Target("corn", 40), Target("kabob", 10) }, Prices);

        Assert.True(plan.Accepted);
        Assert.Equal(new[] { ("tacos", 30, 150L), ("corn", 40, 60L), ("kabob", 10, 200L) },
            plan.Lines.Select(l => (l.ItemName, l.Units, l.UnitPriceCents)));
        Assert.Empty(plan.Unpriced);
    }

    [Fact]
    public void FullTargetsZeroTargetsAndUnpricedItemsBringNothing()
    {
        var state = CommissariesTests.Rented();
        CommissaryLogistics.Receive(state, "a", "corn", 50, 60, 1, "w");

        var plan = CommissaryLogistics.Plan(state, "a",
            new[] { Target("corn", 40), Target("tacos", 0), Target("kabob", -5), Target("gone", 10), Target("", 10) }, Prices);

        Assert.True(plan.Accepted);
        Assert.Empty(plan.Lines);
        Assert.Equal(new[] { "gone" }, plan.Unpriced);
    }

    [Fact]
    public void TwoTargetsForOneItemComeToTheLargerOne()
    {
        // The game reads the stock again after each target, so 10 then 25 of one item ends at 25.
        var state = CommissariesTests.Rented();
        var plan = CommissaryLogistics.Plan(state, "a", new[] { Target("tacos", 10), Target("corn", 5), Target("tacos", 25) }, Prices);
        Assert.Equal(new[] { ("tacos", 25), ("corn", 5) }, plan.Lines.Select(l => (l.ItemName, l.Units)));
    }

    [Fact]
    public void ACommissaryWithoutASpaceTakesNoDelivery()
    {
        var state = CommissariesTests.Rented();
        Commissaries.Release(state, "cart");
        var plan = CommissaryLogistics.Plan(state, "a", new[] { Target("tacos", 80) }, Prices);
        Assert.False(plan.Accepted);
        Assert.Empty(plan.Lines);

        // Visited but never rented, or never visited: the same.
        var visited = new CommissaryState();
        Commissaries.Unlock(visited, "a");
        Assert.False(CommissaryLogistics.Plan(visited, "a", new[] { Target("tacos", 80) }, Prices).Accepted);
        Assert.False(CommissaryLogistics.Accepts(new CommissaryState(), "a"));
        // A space at another commissary doesn't count here.
        Assert.False(CommissaryLogistics.Accepts(CommissariesTests.Rented(), "b"));
    }

    [Fact]
    public void ArrivedGoodsAreANewBatchAfterTheOthersMarkedAsLogistics()
    {
        var state = CommissariesTests.Rented();
        Commissaries.ReturnToStock(state, "a", new[] { CommissariesTests.Line("tacos", 5, 140) });
        var line = CommissaryLogistics.Receive(state, "a", "tacos", 12, 150, 3, "ba:street_x 4");

        Assert.Equal(new long[] { 1, 2 }, state.Stock["a"].Select(l => l.BatchId));
        Assert.Same(line, state.Stock["a"].Last());
        Assert.True(CommissaryLogistics.FromLogistics(line));
        Assert.False(CommissaryLogistics.FromLogistics(state.Stock["a"][0]));
        Assert.Equal(3, (int)line.OrderMetadata["day"]);
        Assert.Equal("ba:street_x 4", (string?)line.OrderMetadata["from"]);
        Assert.Equal(17, CommissaryLogistics.Held(state, "a", "tacos"));

        // Restock takes the older batch first, as for orders.
        var taken = Commissaries.Restock(state, "a", new Dictionary<string, int> { ["tacos"] = 8 });
        Assert.Equal(new[] { (1L, 5), (2L, 3) }, taken.Select(t => (t.BatchId, t.Units)));
        Assert.True(CommissaryLogistics.FromLogistics(taken[1]));
    }

    [Fact]
    public void ReceiveRefusesBadInput()
    {
        var state = CommissariesTests.Rented();
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissaryLogistics.Receive(state, "a", "tacos", 0, 150, 1, "w"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissaryLogistics.Receive(state, "a", "tacos", 5, -1, 1, "w"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissaryLogistics.Receive(state, "a", "tacos", 5, 150, 0, "w"));
        Assert.Throws<ArgumentException>(() => CommissaryLogistics.Receive(state, "a", " ", 5, 150, 1, "w"));
        Assert.Throws<OverflowException>(() => CommissaryLogistics.Receive(state, "a", "tacos", int.MaxValue, long.MaxValue / 2, 1, "w"));
        Assert.False(state.Stock.ContainsKey("a"));
        Assert.Equal(1, state.NextBatchId);
    }

    [Fact]
    public void TheRefundPaysLogisticsStockBackAndGoodsCentsCountsOnlyIt()
    {
        var state = CommissariesTests.Rented();
        Commissaries.ReturnToStock(state, "a", new[] { CommissariesTests.Line("tacos", 10, 100) });
        CommissaryLogistics.Receive(state, "a", "corn", 20, 60, 2, "w");

        Assert.Equal(1_200, CommissaryLogistics.GoodsCents(state));
        Assert.Equal(1_000 + 1_200, Commissaries.Refund(state).Total);
        Assert.Equal(0, CommissaryLogistics.GoodsCents(state));
    }

    [Fact]
    public void LogisticsBatchesSurviveASave()
    {
        var save = new SaveState();
        Commissaries.Unlock(save.Commissary, "a");
        CommissaryLogistics.Receive(save.Commissary, "a", "corn", 20, 60, 2, "w");
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(save));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var line = Assert.Single(loaded.State!.Commissary.Stock["a"]);
        Assert.True(CommissaryLogistics.FromLogistics(line));
        Assert.Equal(1_200, CommissaryLogistics.GoodsCents(loaded.State.Commissary));
    }

    [Fact]
    public void EachShippedCommissaryHasItsOwnNegativeAddressNumber()
    {
        var ids = CommissaryTablesTests.Shipped.Sites.Select(s => s.Id).ToList();
        var numbers = ids.Select(CommissaryLogistics.AddressNumber).ToList();
        Assert.All(numbers, n => Assert.True(n < 0));
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        foreach (var id in ids) Assert.Equal(id, CommissaryLogistics.IdFor(ids, CommissaryLogistics.AddressNumber(id)));
        // Fixed per id, whatever order the data lists them in.
        Assert.Equal(CommissaryLogistics.AddressNumber("hells-kitchen"), CommissaryLogistics.AddressNumber("hells-kitchen"));
        Assert.Null(CommissaryLogistics.IdFor(ids, 7));
        Assert.Null(CommissaryLogistics.IdFor(ids, -1));
    }

    [Fact]
    public void ABatchFromTheWarehouseCarriesWhatTheWarehousePaidNotTheCatalogue()
    {
        // The withdrawal's mean price per unit, in dollars, to whole cents.
        Assert.Equal(137, CommissaryLogistics.PaidCents(1.365));
        Assert.Equal(140, CommissaryLogistics.PaidCents(1.4f));
        Assert.Equal(0, CommissaryLogistics.PaidCents(0));
        Assert.Equal(0, CommissaryLogistics.PaidCents(-2));
        Assert.Equal(0, CommissaryLogistics.PaidCents(double.NaN));
        var state = CommissariesTests.Rented();
        // The plan's catalogue says 150; the warehouse paid 1.365 a unit.
        var plan = CommissaryLogistics.Plan(state, "a", new[] { new KeyValuePair<string, int>("tacos", 20) }, new Dictionary<string, long> { ["tacos"] = 150 });
        var line = CommissaryLogistics.Receive(state, "a", "tacos", plan.Lines.Single().Units, CommissaryLogistics.PaidCents(1.365), 2, "w");
        Assert.Equal(137, line.UnitPriceCents);
        Assert.Equal(20 * 137, CommissaryLogistics.GoodsCents(state));
    }
}
