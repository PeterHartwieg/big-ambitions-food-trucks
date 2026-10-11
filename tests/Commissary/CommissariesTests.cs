using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissariesTests
{
    private static readonly CommissaryRules Rules = new();
    internal static CommissaryState Rented(int day = 1)
    {
        var state = new CommissaryState();
        Commissaries.Unlock(state, "a");
        Commissaries.Rent(state, Rules, "cart", "cart", "a", day);
        return state;
    }
    internal static CommissaryStockLine Line(string item, int units, long price) =>
        new() { ItemName = item, Units = units, UnitPriceCents = price };
    private static Dictionary<string, int> Want(string item, int units) => new() { [item] = units };
    // The game takes every payment.
    internal static bool Paid(CommissaryOrder order) => true;

    [Fact]
    public void RentingRequiresAVisitAndOneVehicleHasOnlyOneSpace()
    {
        var state = new CommissaryState();
        Assert.Throws<InvalidOperationException>(() => Commissaries.Rent(state, Rules, "v", "cart", "a", 3));
        Assert.True(Commissaries.Unlock(state, "a"));
        Assert.False(Commissaries.Unlock(state, "a"));
        var first = Commissaries.Rent(state, Rules, "v", "cart", "a", 3);
        Assert.Equal(3, first.SinceDay);
        Commissaries.Unlock(state, "b");
        var moved = Commissaries.Rent(state, Rules, "v", "cart", "b", 4);
        Assert.Same(first, moved);
        Assert.Equal("b", state.Spaces.Single().CommissaryId);
        Assert.Equal(3, moved.SinceDay); // uninterrupted rental, including unsettled days
        Assert.Equal(new[] { 3, 4 }, Commissaries.RentDue(state, Rules, 4).Select(c => c.Day));
        Assert.Throws<ArgumentException>(() => Commissaries.Rent(state, Rules, "x", "spaceship", "a", 4));
        Assert.True(Commissaries.Release(state, "v"));
        Assert.False(Commissaries.Release(state, "v"));
        Assert.Empty(state.Spaces);
    }

    [Fact]
    public void FirstDayIsChargedByRentDueAndNeverChargedTwice()
    {
        var state = Rented(5);
        Assert.Empty(Commissaries.RentDue(state, Rules, 4));
        var charge = Assert.Single(Commissaries.RentDue(state, Rules, 5));
        Assert.Equal(("cart", "a", 5, 986L), (charge.VehicleId, charge.CommissaryId, charge.Day, charge.Cents));
        Assert.Empty(Commissaries.RentDue(state, Rules, 5));
        Assert.Empty(Commissaries.RentDue(state, Rules, 3));
        Assert.Equal(5, state.RentPaidThroughDay);
    }

    [Fact]
    public void RentAcrossSkippedDaysIsPerVehiclePerDayFromEachRentalStart()
    {
        var state = Rented(2);
        Commissaries.Rent(state, Rules, "truck", "truck", "a", 4);
        var charges = Commissaries.RentDue(state, Rules, 6);
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, charges.Where(c => c.VehicleId == "cart").Select(c => c.Day));
        Assert.Equal(new[] { 4, 5, 6 }, charges.Where(c => c.VehicleId == "truck").Select(c => c.Day));
        Assert.Equal(5 * 986 + 3 * 4110, charges.Sum(c => c.Cents));
        Assert.Equal(6, state.RentPaidThroughDay);
        Assert.Equal(2, Commissaries.RentDue(state, Rules, 7).Count);
    }

    [Fact]
    public void ANewSpaceAfterTodaysRentRunStillPaysItsFirstDay()
    {
        var state = Rented(3);
        Commissaries.RentDue(state, Rules, 3);
        Commissaries.Rent(state, Rules, "truck", "truck", "a", 3);
        Assert.Equal("truck", Assert.Single(Commissaries.RentDue(state, Rules, 3)).VehicleId);
        Commissaries.Unlock(state, "b");
        Commissaries.Rent(state, Rules, "cart", "cart", "b", 3);
        Assert.Empty(Commissaries.RentDue(state, Rules, 3));
        Assert.Equal("b", Commissaries.RentDue(state, Rules, 4).First(c => c.VehicleId == "cart").CommissaryId);
    }

    [Fact]
    public void AnUnknownVehicleKindKeepsItsUnprocessedRentCursor()
    {
        var state = new CommissaryState();
        state.Spaces.Add(new CommissarySpace { VehicleId = "future", Kind = "van", CommissaryId = "a", SinceDay = 2 });
        Assert.Empty(Commissaries.RentDue(state, Rules, 5));
        var updatedRules = new CommissaryRules(new Dictionary<string, long> { ["van"] = 30000 });
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, Commissaries.RentDue(state, updatedRules, 6).Select(c => c.Day));
    }

    [Fact]
    public void ReleaseStopsFutureRentButKeepsOrdersAndStock()
    {
        var state = Rented();
        Commissaries.Order(state, Rules, "a", "dumpling", 10, 21, (1, 300));
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 1, 2) });
        Commissaries.RentDue(state, Rules, 1);
        Commissaries.Release(state, "cart");
        Assert.Empty(Commissaries.RentDue(state, Rules, 5));
        Assert.Single(state.Orders);
        Assert.Single(state.Stock["a"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(479)]
    [InlineData(480)]
    [InlineData(1439)]
    public void OrdersAlwaysArriveNextMorningWithGoodsAndFeeSeparate(int minute)
    {
        var state = Rented(5);
        var order = Commissaries.Order(state, Rules, "a", "dumpling", 3, 5, (5, minute));
        Assert.Equal((1L, 5, 6, 15L, 2L, 17L), (order.Id, order.OrderedDay, order.DeliverDay, order.GoodsCents, order.FeeCents, order.TotalCents));
        Assert.Empty(Commissaries.Deliver(state, Rules, 5, 1439, Paid).Delivered);
        Assert.Empty(Commissaries.Deliver(state, Rules, 6, 479, Paid).Delivered);
        Assert.Single(Commissaries.Deliver(state, Rules, 6, 480, Paid).Delivered);
        Assert.Equal(3, state.Stock["a"].Single().Units);
        Assert.Equal(5, state.Stock["a"].Single().UnitPriceCents);
        Assert.Empty(state.Orders);
        Assert.Empty(Commissaries.Deliver(state, Rules, 6, 481, Paid).Delivered);
    }

    [Fact]
    public void MultiDayDeliverySkipDeliversOverdueOrdersEvenBeforeEightAndKeepsFutureOrders()
    {
        var state = Rented();
        Commissaries.Unlock(state, "b");
        Commissaries.Rent(state, Rules, "truck", "truck", "b", 1);
        var first = Commissaries.Order(state, Rules, "a", "taco", 2, 10, (1, 800));
        var second = Commissaries.Order(state, Rules, "b", "griddle", 1, 1000, (2, 800));
        var third = Commissaries.Order(state, Rules, "a", "taco", 3, 20, (4, 800));
        Assert.Equal(new[] { first.Id, second.Id }, Commissaries.Deliver(state, Rules, 5, 100, Paid).Delivered.Select(o => o.Id));
        Assert.Equal(third.Id, state.Orders.Single().Id);
        Assert.Single(state.Stock["a"]);
        Assert.Single(state.Stock["b"]);
        Commissaries.Deliver(state, Rules, 5, 480, Paid);
        Assert.Equal(new long[] { 10, 20 }, state.Stock["a"].Select(l => l.UnitPriceCents));
    }

    [Fact]
    public void OrdersArePaidOnDeliveryOldestFirstAndOnlyWhenDue()
    {
        var state = Rented();
        var first = Commissaries.Order(state, Rules, "a", "taco", 2, 100, (1, 100));
        var second = Commissaries.Order(state, Rules, "a", "dumpling", 1, 100, (1, 200));
        var later = Commissaries.Order(state, Rules, "a", "taco", 1, 100, (2, 100));
        var asked = new List<long>();
        Assert.Empty(Commissaries.Deliver(state, Rules, 2, 479, o => { asked.Add(o.Id); return true; }).Delivered);
        Assert.Empty(asked); // nothing is paid before it arrives
        Assert.Equal(new[] { first.Id, second.Id }, Commissaries.Due(state, Rules, 2, 480).Select(o => o.Id));
        var run = Commissaries.Deliver(state, Rules, 2, 480, o => { asked.Add(o.Id); return true; });
        Assert.Equal(new[] { first.Id, second.Id }, asked);
        Assert.Equal(new[] { first.Id, second.Id }, run.Delivered.Select(o => o.Id));
        Assert.Empty(run.Refused);
        Assert.Equal(later.Id, Assert.Single(state.Orders).Id);
        Assert.Equal(3, state.Stock["a"].Sum(l => l.Units));
    }

    [Fact]
    public void ARefusedPaymentCancelsThatOrderWithNothingDeliveredAndTheOthersStillArrive()
    {
        var state = Rented();
        var refused = Commissaries.Order(state, Rules, "a", "machine", 1, 100000, (1, 100));
        var paid = Commissaries.Order(state, Rules, "a", "dumpling", 1, 100, (1, 100));
        var run = Commissaries.Deliver(state, Rules, 2, 480, o => o.Id != refused.Id);
        Assert.Equal(refused.Id, Assert.Single(run.Refused).Id);
        Assert.Equal(paid.Id, Assert.Single(run.Delivered).Id);
        Assert.Empty(state.Orders); // the game drops a one-off delivery it can't charge; it isn't tried again
        Assert.Equal("dumpling", Assert.Single(state.Stock["a"]).ItemName);
        Assert.Empty(state.Charges);
        Assert.Empty(Commissaries.Restock(state, "a", Want("machine", 1)));
        Assert.Empty(Commissaries.Deliver(state, Rules, 3, 480, _ => throw new InvalidOperationException("nothing is due")).Delivered);
    }

    [Fact]
    public void APaymentThatThrowsKeepsTheOrdersDecidedBeforeIt()
    {
        var state = Rented();
        var first = Commissaries.Order(state, Rules, "a", "taco", 1, 100, (1, 100));
        var second = Commissaries.Order(state, Rules, "a", "taco", 1, 100, (1, 100));
        Assert.Throws<InvalidOperationException>(() =>
            Commissaries.Deliver(state, Rules, 2, 480, o => o.Id == first.Id ? true : throw new InvalidOperationException()));
        Assert.Single(state.Stock["a"]);
        Assert.Equal(second.Id, Assert.Single(state.Orders).Id);
        Assert.Equal(second.Id, Assert.Single(Commissaries.Deliver(state, Rules, 2, 481, Paid).Delivered).Id);
    }

    [Fact]
    public void OrderingNeedsBothAnUnlockedCommissaryAndASpaceThere()
    {
        var state = Rented();
        Assert.Throws<InvalidOperationException>(() => Commissaries.Order(state, Rules, "b", "taco", 1, 1, (1, 0)));
        Commissaries.Unlock(state, "b");
        Assert.Throws<InvalidOperationException>(() => Commissaries.Order(state, Rules, "b", "taco", 1, 1, (1, 0)));
        Commissaries.Release(state, "cart");
        Assert.Throws<InvalidOperationException>(() => Commissaries.Order(state, Rules, "a", "taco", 1, 1, (1, 0)));
        Assert.Empty(state.Orders);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void InvalidAmountsDoNotCreateOrders(int units, long price)
    {
        var state = Rented();
        Assert.Throws<ArgumentOutOfRangeException>(() => Commissaries.Order(state, Rules, "a", "taco", units, price, (1, 0)));
        Assert.Empty(state.Orders);
        Assert.Equal(1, state.NextOrderId);
    }

    [Fact]
    public void OverflowDoesNotCreateOrPartlyRefundAnOrder()
    {
        var state = Rented();
        Assert.Throws<OverflowException>(() => Commissaries.Order(state, Rules, "a", "taco", 2, long.MaxValue, (1, 0)));
        Assert.Throws<OverflowException>(() => Commissaries.Order(state, Rules, "a", "taco", 1, long.MaxValue, (1, 0)));
        Assert.Throws<OverflowException>(() => Commissaries.Order(state, Rules, "a", "taco", 1, 1, (int.MaxValue, 0)));
        Assert.Empty(state.Orders);
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 2, long.MaxValue) });
        Assert.Throws<OverflowException>(() => Commissaries.Refund(state));
        Assert.Single(state.Spaces);
        Assert.Single(state.Stock["a"]);
    }

    [Fact]
    public void RestockTakesFifoBatchesPartiallyAndByItemWithoutMixingPricesOrCommissaries()
    {
        var state = Rented();
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 3, 10), Line("slushie", 4, 7), Line("taco", 5, 20) });
        Commissaries.ReturnToStock(state, "b", new[] { Line("taco", 5, 1) });
        var taken = Commissaries.Restock(state, "a", new Dictionary<string, int> { ["taco"] = 6, ["slushie"] = 2, ["missing"] = 2 });
        Assert.Equal(new[] { ("taco", 3, 10L), ("slushie", 2, 7L), ("taco", 3, 20L) }, taken.Select(l => (l.ItemName, l.Units, l.UnitPriceCents)));
        Assert.Equal(new[] { ("slushie", 2), ("taco", 2) }, state.Stock["a"].Select(l => (l.ItemName, l.Units)));
        Assert.Equal(5, state.Stock["b"].Single().Units);
        Assert.Empty(Commissaries.Restock(state, "unknown", Want("taco", 1)));
        Assert.Empty(Commissaries.Restock(state, "a", Want("taco", 0)));
        Assert.Equal(2, Commissaries.Restock(state, "a", Want("taco", 20)).Single().Units);
    }

    [Fact]
    public void RejectedCargoReturnsToItsOriginalPlaceEvenWhenReturnedInReverseOrder()
    {
        var state = Rented();
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 3, 10), Line("taco", 5, 20), Line("taco", 4, 30) });
        var taken = Commissaries.Restock(state, "a", Want("taco", 6));
        Commissaries.ReturnToStock(state, "a", taken.Reverse());
        Assert.Equal(new[] { (3, 10L), (5, 20L), (4, 30L) }, state.Stock["a"].Select(l => (l.Units, l.UnitPriceCents)));
        Assert.Equal(10, Commissaries.Restock(state, "a", Want("taco", 1)).Single().UnitPriceCents);
    }

    [Fact]
    public void InvalidRestockAndReturnInputsLeaveStockUntouched()
    {
        var state = Rented();
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 3, 10) });
        Assert.Throws<ArgumentOutOfRangeException>(() => Commissaries.Restock(state, "a", Want("taco", -1)));
        Assert.Throws<ArgumentException>(() => Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 1, 10), Line("taco", -1, 10) }));
        Assert.Equal(3, state.Stock["a"].Single().Units);
    }

    [Theory]
    [InlineData("bay")]
    [InlineData("inside")]
    [InlineData("late")]
    [InlineData("transit")]
    [InlineData("property")]
    public void EachNightExemptionPreventsAFine(string exemption)
    {
        var state = Rented();
        var vehicle = new CommissaryVehicle { VehicleId = "cart", AtOwnCommissaryBay = exemption == "bay",
            PlayerInside = exemption == "inside", OnLateStop = exemption == "late", InTransit = exemption == "transit",
            AtPlayerProperty = exemption == "property" };
        Assert.Empty(Commissaries.NightCheck(state, Rules, 8, 240, new[] { vehicle }));
        Assert.Equal(8, state.NightCheckedThroughDay);
    }

    [Fact]
    public void ThePlayersOwnPropertyExemptsAVehicleWithoutASpace()
    {
        var state = new CommissaryState();
        Assert.Empty(Commissaries.NightCheck(state, Rules, 8, 240,
            new[] { new CommissaryVehicle { VehicleId = "cart", AtPlayerProperty = true } }));
        Assert.Single(Commissaries.NightCheck(state, Rules, 9, 240, new[] { new CommissaryVehicle { VehicleId = "cart" } }));
    }

    [Fact]
    public void AReportedBayWithoutARentedSpaceIsNotAnExemption()
    {
        var state = new CommissaryState();
        var fine = Assert.Single(Commissaries.NightCheck(state, Rules, 8, 240,
            new[] { new CommissaryVehicle { VehicleId = "cart", AtOwnCommissaryBay = true } }));
        Assert.Equal(("cart", 8, 25000L), (fine.VehicleId, fine.Day, fine.Cents));
    }

    [Fact]
    public void FirstCallAfterCheckTimeOnlyChecksCurrentNightAndRepeatsAreIdempotent()
    {
        var state = Rented();
        var vehicles = new[] { new CommissaryVehicle { VehicleId = "cart" } };
        Assert.Equal(100, Assert.Single(Commissaries.NightCheck(state, Rules, 100, 240, vehicles)).Day);
        Assert.Empty(Commissaries.NightCheck(state, Rules, 100, 1439, vehicles));
        Assert.Empty(Commissaries.NightCheck(state, Rules, 99, 300, vehicles));
        Assert.Equal(100, state.NightCheckedThroughDay);
    }

    [Fact]
    public void FirstCallBeforeCheckTimeTracksTodayWithoutFiningYesterday()
    {
        var state = Rented();
        var vehicles = new[] { new CommissaryVehicle { VehicleId = "cart" } };
        Assert.Empty(Commissaries.NightCheck(state, Rules, 100, 239, vehicles));
        Assert.Equal(99, state.NightCheckedThroughDay);
        Assert.Equal(100, Assert.Single(Commissaries.NightCheck(state, Rules, 100, 240, vehicles)).Day);
    }

    [Fact]
    public void SkippedNightsUseCurrentFlagsThroughTheLatestPassedCheck()
    {
        var state = Rented();
        var cart = new CommissaryVehicle { VehicleId = "cart", PlayerInside = true };
        var truck = new CommissaryVehicle { VehicleId = "truck", InTransit = true };
        Commissaries.NightCheck(state, Rules, 3, 300, new[] { cart, truck });
        cart.PlayerInside = false;
        var fines = Commissaries.NightCheck(state, Rules, 7, 239, new[] { cart, truck });
        Assert.Equal(new[] { 4, 5, 6 }, fines.Select(f => f.Day));
        Assert.All(fines, f => Assert.Equal("cart", f.VehicleId));
        Assert.Equal(7, Assert.Single(Commissaries.NightCheck(state, Rules, 7, 240, new[] { cart, truck })).Day);
    }

    [Fact]
    public void DuplicateVehicleInputsAreRefusedBeforeAdvancingTheNightCursor()
    {
        var state = Rented();
        var vehicle = new CommissaryVehicle { VehicleId = "cart" };
        Assert.Throws<ArgumentException>(() => Commissaries.NightCheck(state, Rules, 1, 240, new[] { vehicle, vehicle }));
        Assert.Null(state.NightCheckedThroughDay);
    }

    [Fact]
    public void CustomClockAndFineRulesDriveDeliveryAndNightChecks()
    {
        var state = Rented();
        var rules = new CommissaryRules(nightCheckMinute: 300, deliveryMinute: 600, overnightFineCents: 99);
        Commissaries.Order(state, rules, "a", "taco", 1, 1, (1, 1));
        Assert.Empty(Commissaries.Deliver(state, rules, 2, 599, Paid).Delivered);
        Assert.Single(Commissaries.Deliver(state, rules, 2, 600, Paid).Delivered);
        var vehicle = new[] { new CommissaryVehicle { VehicleId = "cart" } };
        Assert.Empty(Commissaries.NightCheck(state, rules, 2, 299, vehicle));
        Assert.Equal(99, Assert.Single(Commissaries.NightCheck(state, rules, 2, 300, vehicle)).Cents);
    }

    [Fact]
    public void RefundPaysBackDeliveredStockOnlyAndCancelsOpenOrdersUnpaid()
    {
        var state = Rented();
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 3, 10), Line("taco", 2, 20) });
        Commissaries.Order(state, Rules, "a", "machine", 1, 1000, (1, 1));
        state.Fines.Add(new CommissaryFine { VehicleId = "cart", Day = 1, Cents = 25000 });
        var refund = Commissaries.Refund(state);
        Assert.Equal(70, refund.GoodsCents);
        Assert.Equal(70, refund.Total); // the open order was never paid: nothing back for it
        Assert.Equal(1, refund.CancelledOrders);
        Assert.Empty(state.Stock);
        Assert.Empty(state.Orders);
        Assert.Empty(state.Spaces);
        Assert.Single(state.Unlocked);
        Assert.Single(state.Fines); // debt must still be settled
        Assert.Equal(0, Commissaries.Refund(state).Total);
    }

    [Fact]
    public void RefundRetainsRentAndFineDebts()
    {
        var state = Rented();
        Commissaries.ReturnToStock(state, "a", new[] { Line("taco", 3, 10) });
        var delivered = Commissaries.Order(state, Rules, "a", "dumpling", 2, 100, (1, 1));
        Commissaries.Deliver(state, Rules, 2, 480, Paid);
        Commissaries.Order(state, Rules, "a", "machine", 1, 1000, (2, 600));
        var rent = Assert.Single(Commissaries.RentDue(state, Rules, 1));
        state.Charges.Add(rent);
        var fine = new CommissaryFine { VehicleId = "cart", Day = 1, Cents = 25000 };
        state.Fines.Add(fine);

        var refund = Commissaries.Refund(state);
        Assert.Equal(30 + delivered.GoodsCents, refund.GoodsCents); // delivered goods, not their delivery fee
        Assert.Equal(1, refund.CancelledOrders);
        Assert.Same(rent, Assert.Single(state.Charges));
        Assert.Same(fine, Assert.Single(state.Fines));
        Assert.Empty(state.Orders);
        Assert.Empty(state.Stock);
        Assert.Empty(state.Spaces);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 1440)]
    public void InvalidGameTimesAreRejectedBeforeChanges(int day, int minute)
    {
        var state = Rented();
        Assert.Throws<ArgumentOutOfRangeException>(() => Commissaries.Order(state, Rules, "a", "taco", 1, 1, (day, minute)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Commissaries.Deliver(state, Rules, day, minute, Paid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Commissaries.NightCheck(state, Rules, day, minute, Array.Empty<CommissaryVehicle>()));
        Assert.Empty(state.Orders);
        Assert.Null(state.NightCheckedThroughDay);
    }
}
