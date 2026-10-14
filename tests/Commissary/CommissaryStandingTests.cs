using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissaryStandingTests
{
    // Four boxes of each food a week, six of hot dogs.
    private static readonly CommissaryRules Rules = new(foodBoxesPerWeek: 4, foodBoxesPerWeekByItem: new Dictionary<string, int> { ["hotdog"] = 6 });

    private static CommissaryQuote Quote(params (string Item, int Boxes, bool Food)[] lines) =>
        CommissaryOrdering.Quote(Rules, lines.Select(l => new CommissaryOrderRequest(l.Item, l.Food ? 500 : 1, l.Food ? 10 : 50_000, l.Boxes, l.Food)));

    private static CommissaryPrice? Price(string item) => item == "gone" ? null : new CommissaryPrice(item == "griddle" ? 1 : 500, item == "griddle" ? 60_000 : 12);

    [Fact]
    public void TheGameWeekStartsOnDayOneAndEverySeventhDayAfter()
    {
        Assert.Equal(1, CommissarySupply.WeekStart(1));
        Assert.Equal(1, CommissarySupply.WeekStart(7));
        Assert.Equal(8, CommissarySupply.WeekStart(8));
        Assert.Equal(15, CommissarySupply.WeekStart(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissarySupply.WeekStart(0));
    }

    [Fact]
    public void ADailyOrderStartsTomorrowAndBooksEachDayAtTheDeliveryMinuteAtThatDaysPrice()
    {
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Daily, (1, 600), unlimited: true);
        Assert.Equal(2, standing.NextDay);
        Assert.Empty(CommissaryStandingOrders.Book(state, Rules, 1, 1439, Price));
        Assert.Empty(CommissaryStandingOrders.Book(state, Rules, 2, 479, Price));
        var run = Assert.Single(CommissaryStandingOrders.Book(state, Rules, 2, 480, Price));
        var order = Assert.Single(run.Orders);
        Assert.Equal((2, 2, 500, 12L, standing.Id), (order.OrderedDay, order.DeliverDay, order.Units, order.UnitPriceCents, order.StandingOrderId!.Value));
        Assert.Equal(Rules.FeeCents(6000), order.FeeCents);
        Assert.Equal(3, standing.NextDay);
        // Booked once a day, however often the step runs.
        Assert.Empty(CommissaryStandingOrders.Book(state, Rules, 2, 900, Price));
        // Paid on delivery like any order; a refusal cancels the delivery and the standing order stays.
        var refused = Commissaries.Deliver(state, Rules, 2, 480, _ => false);
        Assert.Single(refused.Refused);
        Assert.Single(state.StandingOrders);
        Assert.Single(CommissaryStandingOrders.Book(state, Rules, 3, 480, Price).Single().Orders);
    }

    [Fact]
    public void AWeeklyOrderComesBackOnItsWeekdayAndMissedDaysAreSkipped()
    {
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Weekly, (3, 100));
        Assert.Equal(DayOfWeek.Thursday, CommissaryStandingOrders.Weekday(standing));
        Assert.Single(CommissaryStandingOrders.Book(state, Rules, 4, 480, Price).Single().Orders);
        Assert.Equal(11, standing.NextDay);
        // A booking run that comes only on day 19 skips day 11 and books day 18's delivery no more.
        var late = Assert.Single(CommissaryStandingOrders.Book(state, Rules, 19, 600, Price));
        Assert.Equal(2, late.Missed);
        Assert.Empty(late.Orders);
        Assert.Equal(25, standing.NextDay);
    }

    [Fact]
    public void OneOffOrdersAreRefusedOverAFoodsWeeklyLimitAndEquipmentHasNone()
    {
        var state = CommissariesTests.Rented();
        CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 3, true)), (1, 600));
        Assert.Equal(1, CommissarySupply.Left(state, Rules, "a", "taco", 2));
        Assert.Equal(6, CommissarySupply.Left(state, Rules, "a", "hotdog", 2));
        var over = CommissarySupply.Check(state, Rules, "a", Quote(("taco", 2, true)), CommissaryRepeat.Once, 1);
        Assert.False(over.Fits);
        Assert.Equal(("taco", 1, 1, 2), (over.ItemName, over.WeekStart, over.Left, over.Needed));
        Assert.Throws<InvalidOperationException>(() => CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 2, true)), (1, 700)));
        // Machines and the setting that lifts the game's limits are outside it.
        Assert.Equal(2, CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 1, true), ("griddle", 9, false)), (1, 700)).Count);
        Assert.Single(CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 5, true)), (1, 800), unlimited: true));
    }

    [Fact]
    public void DeliveredBoxesCountForTheirWeekAndANewWeekStartsAfresh()
    {
        var state = CommissariesTests.Rented();
        CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 4, true)), (5, 600));
        Commissaries.Deliver(state, Rules, 6, 480, CommissariesTests.Paid);
        Assert.Empty(state.Orders);
        Assert.Equal(0, CommissarySupply.Left(state, Rules, "a", "taco", 7));
        // Day 8 is a Monday: a new week.
        Assert.Equal(4, CommissarySupply.Left(state, Rules, "a", "taco", 8));
        CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 1, true)), (7, 600));
        Commissaries.Deliver(state, Rules, 8, 480, CommissariesTests.Paid);
        Assert.Equal(8, state.WeekBoxes.Single().WeekStart);
        // A refused delivery doesn't count.
        CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 3, true)), (8, 600));
        Commissaries.Deliver(state, Rules, 9, 480, _ => false);
        Assert.Equal(3, CommissarySupply.Left(state, Rules, "a", "taco", 9));
    }

    [Fact]
    public void StandingOrdersHoldTheirBoxesAgainstOneOffOrdersAndAreCheckedOverTwoWeeks()
    {
        var state = CommissariesTests.Rented();
        // Day 1 for day 2: a daily box of tacos takes 6 this week (days 2 to 7), 7 a whole week: over 4.
        var daily = CommissarySupply.Check(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Daily, 1);
        Assert.False(daily.Fits);
        Assert.Throws<InvalidOperationException>(() => CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Daily, (1, 600)));
        // A weekly order of 3 hot dogs leaves 3 for one-off orders in each week it delivers.
        CommissaryStandingOrders.Create(state, Rules, "a", Quote(("hotdog", 3, true)), CommissaryRepeat.Weekly, (1, 600));
        Assert.Equal(3, CommissarySupply.Left(state, Rules, "a", "hotdog", 2));
        Assert.Equal(3, CommissarySupply.Left(state, Rules, "a", "hotdog", 9));
        Assert.Throws<InvalidOperationException>(() => CommissaryOrdering.Place(state, Rules, "a", Quote(("hotdog", 4, true)), (1, 700)));
        // A second weekly order of 4 doesn't fit next to it.
        Assert.False(CommissarySupply.Check(state, Rules, "a", Quote(("hotdog", 4, true)), CommissaryRepeat.Weekly, 1).Fits);
    }

    [Fact]
    public void AStandingOrderOverTheLimitDeliversWhatFitsEarliestBookedFirstAndStays()
    {
        var state = CommissariesTests.Rented();
        var first = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 3, true), ("griddle", 1, false)), CommissaryRepeat.Weekly, (1, 600));
        var second = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("hotdog", 2, true)), CommissaryRepeat.Weekly, (1, 600));
        // The limit is lowered later (a balance pass), and a third standing order of tacos sneaks in by data.
        second.Lines.Add(new CommissaryStandingLine { ItemName = "taco", Boxes = 2 });
        second.Lines.Add(new CommissaryStandingLine { ItemName = "gone", Boxes = 1 });
        var lower = new CommissaryRules(foodBoxesPerWeek: 4);
        var runs = CommissaryStandingOrders.Book(state, lower, 2, 480, Price);
        Assert.Equal(new[] { first.Id, second.Id }, runs.Select(r => r.Standing.Id));
        Assert.Empty(runs[0].Cut);
        Assert.Equal(new[] { ("taco", 3), ("griddle", 1) }, runs[0].Orders.Select(o => (o.ItemName, o.Units == 1 ? 1 : o.Units / 500)));
        Assert.Equal(new[] { ("taco", 2, 1) }, runs[1].Cut);
        Assert.Equal(new[] { "gone" }, runs[1].NotSold);
        Assert.Equal(new[] { ("hotdog", 1000), ("taco", 500) }, runs[1].Orders.Select(o => (o.ItemName, o.Units)));
        Assert.Equal(2, state.StandingOrders.Count);
        Assert.All(state.StandingOrders, s => Assert.Equal(9, s.NextDay));
    }

    [Fact]
    public void WithoutASpaceTheDayIsSkippedAndCancellingOrUninstallingEndsIt()
    {
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Weekly, (1, 600));
        Commissaries.Release(state, "cart");
        var run = Assert.Single(CommissaryStandingOrders.Book(state, Rules, 2, 480, Price));
        Assert.True(run.NoSpace);
        Assert.Empty(state.Orders);
        Assert.Equal(9, standing.NextDay);
        Assert.Throws<InvalidOperationException>(() => CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Weekly, (2, 600)));
        Commissaries.Rent(state, Rules, "cart", "cart", "a", 2);
        CommissaryStandingOrders.Create(state, Rules, "a", Quote(("hotdog", 1, true)), CommissaryRepeat.Weekly, (2, 600));
        Assert.True(CommissaryStandingOrders.Cancel(state, standing.Id));
        Assert.False(CommissaryStandingOrders.Cancel(state, standing.Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Once, (2, 600)));
        var refund = Commissaries.Refund(state);
        Assert.Equal(1, refund.CancelledStandingOrders);
        Assert.Empty(state.StandingOrders);
    }

    [Fact]
    public void StandingOrdersAndTheWeeksBoxesRoundTripAndBadEntriesAreSetAside()
    {
        var codec = new SaveCodec();
        var state = new SaveState { Commissary = CommissariesTests.Rented() };
        var standing = CommissaryStandingOrders.Create(state.Commissary, Rules, "a", Quote(("taco", 2, true), ("griddle", 1, false)), CommissaryRepeat.Weekly, (1, 600));
        CommissaryStandingOrders.Book(state.Commissary, Rules, 2, 480, Price);
        Commissaries.Deliver(state.Commissary, Rules, 2, 480, CommissariesTests.Paid);
        state.Commissary.StandingOrders.Add(new CommissaryStandingOrder { Id = 9, CommissaryId = "a", Repeat = CommissaryRepeat.Once, CreatedDay = 1, NextDay = 2,
            Lines = { new CommissaryStandingLine { ItemName = "taco", Boxes = 1 } } });
        var json = codec.Serialize(state);
        Assert.Contains("\"Weekly\"", json);
        var c = codec.Load(json).State!.Commissary;
        var loaded = Assert.Single(c.StandingOrders);
        Assert.Equal((standing.Id, CommissaryRepeat.Weekly, 9), (loaded.Id, loaded.Repeat, loaded.NextDay));
        Assert.Equal(new[] { ("taco", 2, true), ("griddle", 1, false) }, loaded.Lines.Select(l => (l.ItemName, l.Boxes, l.Capped)));
        Assert.Equal(2, c.NextStandingId);
        Assert.Equal(2, c.WeekBoxes.Single().Boxes);
        Assert.Equal(2, CommissarySupply.Left(c, Rules, "a", "taco", 3));
    }

    [Fact]
    public void ARefusedStandingDeliveryCountsNoBoxesAndTheOrderStays()
    {
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 2, true)), CommissaryRepeat.Weekly, (1, 600));
        Assert.Equal(2, CommissarySupply.Left(state, Rules, "a", "taco", 2));
        CommissaryStandingOrders.Book(state, Rules, 2, 480, Price);
        var run = Commissaries.Deliver(state, Rules, 2, 480, _ => false);
        Assert.Single(run.Refused);
        Assert.Empty(state.WeekBoxes);
        // Its day is over, nothing came: the week's 4 boxes are free again; next week holds its 2.
        Assert.Equal(4, CommissarySupply.Left(state, Rules, "a", "taco", 3));
        Assert.Equal(2, CommissarySupply.Left(state, Rules, "a", "taco", 9));
        Assert.Same(standing, Assert.Single(state.StandingOrders));
        Assert.Equal(9, standing.NextDay);
    }

    [Fact]
    public void NoSpaceAndNotSoldAreToldOnceUntilTheyChange()
    {
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true), ("gone", 1, true)), CommissaryRepeat.Daily, (1, 600), unlimited: true);
        var first = Assert.Single(CommissaryStandingOrders.Book(state, Rules, 2, 480, Price));
        Assert.Equal(new[] { "gone" }, first.TellNotSold);
        Assert.Empty(Assert.Single(CommissaryStandingOrders.Book(state, Rules, 3, 480, Price)).TellNotSold);
        Commissaries.Release(state, "cart");
        Assert.True(Assert.Single(CommissaryStandingOrders.Book(state, Rules, 4, 480, Price)).TellNoSpace);
        var again = Assert.Single(CommissaryStandingOrders.Book(state, Rules, 5, 480, Price));
        Assert.True(again.NoSpace);
        Assert.False(again.TellNoSpace);
        // A space again: booked, and a later loss is told again.
        Commissaries.Rent(state, Rules, "cart", "cart", "a", 6);
        Assert.False(Assert.Single(CommissaryStandingOrders.Book(state, Rules, 6, 480, Price)).NoSpace);
        Assert.False(standing.NoSpaceTold);
        Commissaries.Release(state, "cart");
        Assert.True(Assert.Single(CommissaryStandingOrders.Book(state, Rules, 7, 480, Price)).TellNoSpace);
    }

    [Fact]
    public void BringingADeliveryForwardKeepsTheScheduleAndAddsNone()
    {
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Weekly, (1, 600));
        // Day 1, 06:00: tomorrow's delivery comes now, for today.
        var run = CommissaryStandingOrders.BookEarly(state, Rules, standing, 1, 360, Price);
        var order = Assert.Single(run.Orders);
        Assert.Equal(1, order.DeliverDay);
        Assert.Equal(9, standing.NextDay);
        Assert.Single(Commissaries.Deliver(state, Rules, 1, 480, CommissariesTests.Paid).Delivered);
        // Day 2 at 08:00 books nothing: that delivery already came.
        Assert.Empty(CommissaryStandingOrders.Book(state, Rules, 2, 480, Price));
        Assert.Throws<InvalidOperationException>(() => CommissaryStandingOrders.BookEarly(state, Rules, standing, 2, 360, Price));
    }

    [Fact]
    public void AnOrderDueBeforeDayOneIsRefusedBeforeAnyPayment()
    {
        var state = CommissariesTests.Rented();
        var order = CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 1, true)), (1, 300)).Single();
        order.DeliverDay = 0;
        var asked = false;
        Assert.Throws<InvalidOperationException>(() => Commissaries.Deliver(state, Rules, 1, 480, _ => asked = true));
        Assert.False(asked);
        Assert.Single(state.Orders);
    }

    [Fact]
    public void TheFirstDayNamedOnThePhoneHoldsAcrossMidnight()
    {
        // Asked on day 3 (naming day 4); confirmed after midnight, on day 4.
        Assert.Equal(4, CommissaryStandingOrders.FirstDay(CommissaryRepeat.Weekly, 4, 4));
        Assert.Equal(5, CommissaryStandingOrders.FirstDay(CommissaryRepeat.Weekly, 4, null));
        // A call left open for days: the same weekday, the first from today.
        Assert.Equal(11, CommissaryStandingOrders.FirstDay(CommissaryRepeat.Weekly, 6, 4));
        Assert.Equal(6, CommissaryStandingOrders.FirstDay(CommissaryRepeat.Daily, 6, 4));
        var state = CommissariesTests.Rented();
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 1, true)), CommissaryRepeat.Weekly, (4, 10), firstDay: 4);
        Assert.Equal(4, standing.NextDay);
        Assert.Single(CommissaryStandingOrders.Book(state, Rules, 4, 480, Price).Single().Orders);
    }

    [Fact]
    public void ADeliveryBroughtForwardCountsInTheWeekOfItsOwnDay()
    {
        var state = CommissariesTests.Rented();
        // Week 1's tacos are all taken by day 7's order; a weekly order delivers on day 8, a new week.
        CommissaryOrdering.Place(state, Rules, "a", Quote(("taco", 4, true)), (6, 600));
        var standing = CommissaryStandingOrders.Create(state, Rules, "a", Quote(("taco", 2, true)), CommissaryRepeat.Weekly, (7, 100));
        Assert.Equal(8, standing.NextDay);
        var run = CommissaryStandingOrders.BookEarly(state, Rules, standing, 7, 300, Price);
        Assert.Empty(run.Cut);
        Assert.Equal(1000, Assert.Single(run.Orders).Units);
    }
}
