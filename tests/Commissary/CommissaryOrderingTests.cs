using System;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissaryOrderingTests
{
    private static readonly CommissaryRules Rules = new();

    private static CommissaryOrderRequest Request(string item, int box, long unit, int boxes) => new(item, box, unit, boxes);

    [Fact]
    public void AQuotePricesEachLineAsTheOrderItBecomesWithOneFlatFee()
    {
        var quote = CommissaryOrdering.Quote(Rules, new[]
        {
            Request("dumplings", 80, 120, 2),
            Request("griddle", 1, 45_005, 1)
        });
        Assert.Equal(CommissaryQuoteProblem.None, quote.Problem);
        Assert.Equal(3, quote.Boxes);
        var dumplings = quote.Lines[0];
        Assert.Equal(160, dumplings.Units);
        Assert.Equal(9_600, dumplings.BoxPriceCents);
        Assert.Equal(19_200, dumplings.GoodsCents);
        Assert.Equal(19_200 + 45_005, quote.GoodsCents);
        // One flat fee for the delivery, not one per line or per unit.
        Assert.Equal(25_000, quote.FeeCents);
        Assert.Equal(25_000, quote.FeePerDeliveryCents);
        Assert.False(quote.SharedDelivery);
        Assert.Equal(quote.GoodsCents + 25_000, quote.TotalCents);

        // The same goods as the orders Core books from it; the fee waits for the delivery.
        var state = CommissariesTests.Rented();
        var orders = CommissaryOrdering.Place(state, Rules, "a", quote, (1, 600));
        Assert.Equal(quote.Lines.Select(l => (l.ItemName, l.Units, l.UnitPriceCents, l.GoodsCents)),
            orders.Select(o => (o.ItemName, o.Units, o.UnitPriceCents, o.GoodsCents)));
        Assert.All(orders, o => Assert.Equal(0, o.FeeCents));
        Assert.All(orders, o => Assert.Equal(2, o.DeliverDay));
        Assert.Equal(orders.Count, state.Orders.Count);
    }

    [Fact]
    public void EmptyLinesDropOutAndTheSameItemTwiceIsOneLine()
    {
        var quote = CommissaryOrdering.Quote(Rules, new[]
        {
            Request("tacos", 80, 100, 0),
            Request("corn", 80, 50, 1),
            Request("corn", 80, 50, 2)
        });
        var line = Assert.Single(quote.Lines);
        Assert.Equal(("corn", 3, 240), (line.ItemName, line.Boxes, line.Units));
        Assert.Equal(3 * 80 * 50, line.GoodsCents);

        Assert.Equal(CommissaryQuoteProblem.Empty, CommissaryOrdering.Quote(Rules, new[] { Request("tacos", 80, 100, 0) }).Problem);
        Assert.Equal(CommissaryQuoteProblem.Empty, CommissaryOrdering.Quote(Rules, Array.Empty<CommissaryOrderRequest>()).Problem);
        Assert.Equal(0, CommissaryOrdering.Quote(Rules, Array.Empty<CommissaryOrderRequest>()).FeeCents);
        Assert.Throws<ArgumentException>(() => CommissaryOrdering.Quote(Rules, new[] { Request("corn", 80, 50, 1), Request("corn", 40, 50, 1) }));
    }

    [Fact]
    public void AnOrderTakesAtMostTenBoxes()
    {
        var ten = CommissaryOrdering.Quote(Rules, new[] { Request("a", 80, 100, 6), Request("b", 1, 100, 4) });
        Assert.Equal(CommissaryQuoteProblem.None, ten.Problem);
        var eleven = CommissaryOrdering.Quote(Rules, new[] { Request("a", 80, 100, 6), Request("b", 1, 100, 5) });
        Assert.Equal(CommissaryQuoteProblem.TooManyBoxes, eleven.Problem);

        var state = CommissariesTests.Rented();
        Assert.Throws<InvalidOperationException>(() => CommissaryOrdering.Place(state, Rules, "a", eleven, (1, 600)));
        Assert.Empty(state.Orders);
    }

    [Fact]
    public void OnlyACommissaryWithASpaceTakesOrders()
    {
        var state = new CommissaryState();
        Assert.False(CommissaryOrdering.CanOrder(state, "a"));
        Commissaries.Unlock(state, "a");
        Assert.False(CommissaryOrdering.CanOrder(state, "a"));
        Commissaries.Rent(state, Rules, "cart", "cart", "a", 1);
        Assert.True(CommissaryOrdering.CanOrder(state, "a"));
        Commissaries.Unlock(state, "b");
        Assert.False(CommissaryOrdering.CanOrder(state, "b"));
        Assert.False(CommissaryOrdering.CanOrder(state, ""));

        var quote = CommissaryOrdering.Quote(Rules, new[] { Request("a", 80, 100, 1) });
        Assert.Throws<InvalidOperationException>(() => CommissaryOrdering.Place(state, Rules, "b", quote, (1, 600)));
        Assert.Empty(state.Orders);
    }

    [Fact]
    public void BadRequestsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Request(" ", 80, 100, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request("a", 0, 100, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request("a", 80, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request("a", 80, 100, -1));
        Assert.Throws<OverflowException>(() => CommissaryOrdering.Quote(Rules, new[] { Request("a", int.MaxValue, 100, 2) }));
    }

    [Fact]
    public void AnOrderSharesTheFeeOfAnotherDeliveryThereTheSameMorning()
    {
        var state = CommissariesTests.Rented();
        Commissaries.Unlock(state, "b");
        Assert.False(CommissaryOrdering.SharesDelivery(state, "a", 2));
        CommissaryOrdering.Place(state, Rules, "a", CommissaryOrdering.Quote(Rules, new[] { Request("a", 80, 100, 1) }), (1, 600));
        Assert.True(CommissaryOrdering.SharesDelivery(state, "a", 2));
        Assert.False(CommissaryOrdering.SharesDelivery(state, "a", 3));
        Assert.False(CommissaryOrdering.SharesDelivery(state, "b", 2));
        var shared = CommissaryOrdering.Quote(Rules, new[] { Request("b", 80, 100, 1) }, sharedDelivery: true);
        Assert.Equal((0L, 25_000L, true, 8_000L), (shared.FeeCents, shared.FeePerDeliveryCents, shared.SharedDelivery, shared.TotalCents));
        // A standing order that delivers that day shares it too.
        var standing = new CommissaryStandingOrder { Id = 1, CommissaryId = "b", Repeat = CommissaryRepeat.Weekly, CreatedDay = 1, NextDay = 4,
            Lines = { new CommissaryStandingLine { ItemName = "taco", Boxes = 1 } } };
        state.StandingOrders.Add(standing);
        Assert.True(CommissaryOrdering.SharesDelivery(state, "b", 11));
        Assert.False(CommissaryOrdering.SharesDelivery(state, "b", 12));
    }
}
