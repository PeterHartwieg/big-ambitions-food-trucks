using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Marketing;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Marketing;

public class MarketingFeesTests
{
    private static readonly MarketingRules Rules = new();

    // A cart booked at `level`; with `paidThrough`, that day was paid with it, as Renew writes it.
    private static VehicleState Cart(string? level = null, int? paidThrough = null, long paidCents = 0) =>
        new()
        {
            VehicleId = "cart-1", VehicleType = "cart", MarketingLevel = level, MarketingPaidThroughDay = paidThrough,
            MarketingPaidLevel = paidThrough == null ? null : level, MarketingPaidCents = paidCents
        };

    // The game's payment: records each charge and takes it if `accept` says so.
    private sealed class Till
    {
        public readonly List<MarketingCharge> Charges = new();
        public bool Accept = true;
        public bool Pay(MarketingCharge charge)
        {
            Charges.Add(charge);
            return Accept;
        }
    }

    [Fact]
    public void ANewDayChargesTheLevelOnceAndAdvancesTheCursor()
    {
        var cart = Cart("flyers", paidThrough: 3, paidCents: 2000);
        var till = new Till();

        var charge = MarketingFees.Renew(cart, Rules, 4, till.Pay);

        Assert.NotNull(charge);
        Assert.True(charge!.Paid);
        Assert.Equal(("cart-1", "flyers", 4, 2000L), (charge.VehicleId, charge.Level, charge.Day, charge.Cents));
        Assert.Equal(4, cart.MarketingPaidThroughDay);
        Assert.Equal(2000, cart.MarketingPaidCents);
        Assert.Null(MarketingFees.Renew(cart, Rules, 4, till.Pay));
        Assert.Single(till.Charges);
        Assert.Equal(1.10, MarketingFees.Factor(cart, Rules, 4));
    }

    [Fact]
    public void TrucksPayTheTruckPrice()
    {
        var truck = new VehicleState { VehicleId = "truck-1", VehicleType = "truck", MarketingLevel = "citywide" };
        Assert.Equal(24000, MarketingFees.Renew(truck, Rules, 2, new Till().Pay)!.Cents);
    }

    [Fact]
    public void SkippedDaysAreNeitherChargedNorMarketed()
    {
        var cart = Cart("local", paidThrough: 2, paidCents: 4500);
        var till = new Till();

        var charge = MarketingFees.Renew(cart, Rules, 6, till.Pay);

        Assert.Equal(6, Assert.Single(till.Charges).Day);
        Assert.Equal(4500, charge!.Cents);
        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 5));
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 6));
    }

    [Fact]
    public void NoLevelNoneOrAnUnknownLevelChargesNothingAndKeepsTheCursor()
    {
        var till = new Till();
        foreach (var level in new[] { null, "none", "billboards", "" })
        {
            var cart = Cart(level, paidThrough: 2, paidCents: 2000);
            Assert.Null(MarketingFees.Renew(cart, Rules, 5, till.Pay));
            Assert.Equal(2, cart.MarketingPaidThroughDay);
            Assert.Equal(level, cart.MarketingLevel);
            Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 2));
        }
        Assert.Empty(till.Charges);
    }

    [Fact]
    public void AVehicleKindWithoutAPriceIsNeitherChargedNorMarketed()
    {
        var van = new VehicleState { VehicleId = "van-1", VehicleType = "van", MarketingLevel = "flyers", MarketingPaidThroughDay = 3 };
        Assert.Null(MarketingFees.Renew(van, Rules, 4, new Till().Pay));
        Assert.Equal(1.0, MarketingFees.Factor(van, Rules, 3));
        Assert.Null(MarketingFees.Booked(van, Rules));
        Assert.Throws<InvalidOperationException>(() => MarketingFees.Choose(van, Rules, "flyers", 4));
    }

    [Fact]
    public void ARefusedDayTurnsTheLevelOffAndLeavesTheDayUnpaid()
    {
        var cart = Cart("citywide", paidThrough: 3, paidCents: 8000);
        var till = new Till { Accept = false };

        var charge = MarketingFees.Renew(cart, Rules, 4, till.Pay);

        Assert.False(charge!.Paid);
        Assert.Equal(8000, charge.Cents);
        Assert.Null(cart.MarketingLevel);
        Assert.Equal(3, cart.MarketingPaidThroughDay);
        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 4));
        Assert.Equal(1.30, MarketingFees.Factor(cart, Rules, 3));
        Assert.Null(MarketingFees.Renew(cart, Rules, 5, till.Pay));
    }

    [Fact]
    public void PickingALevelBooksItFromTheNextDayWithoutCharging()
    {
        var cart = Cart();
        var till = new Till();

        MarketingFees.Choose(cart, Rules, "flyers", 7);

        Assert.Equal(("flyers", 8), (cart.MarketingLevel, cart.MarketingStartsDay));
        Assert.Equal("flyers", MarketingFees.Booked(cart, Rules)!.Id);
        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 7));
        // A renewal the same day (a load) charges nothing; the next day charges the day's fee.
        Assert.Null(MarketingFees.Renew(cart, Rules, 7, till.Pay));
        Assert.Empty(till.Charges);
        var charge = MarketingFees.Renew(cart, Rules, 8, till.Pay);
        Assert.Equal((8, 2000L, true), (charge!.Day, charge.Cents, charge.Paid));
        Assert.Equal(1.10, MarketingFees.Factor(cart, Rules, 8));
        Assert.Null(cart.MarketingStartsDay);
        Assert.Equal(2000, MarketingFees.Renew(cart, Rules, 9, till.Pay)!.Cents);
    }

    [Fact]
    public void ABookingLateInTheDayIsChargedOnceForTheNextDay()
    {
        // Playtest, 21 October 2026: picked at 23:42, a level was charged for that day and again at midnight.
        var cart = Cart();
        var till = new Till();

        MarketingFees.Choose(cart, Rules, "local", 6);
        MarketingFees.Renew(cart, Rules, 7, till.Pay);

        Assert.Equal((7, 4500L), (Assert.Single(till.Charges).Day, till.Charges[0].Cents));
    }

    [Fact]
    public void ADayKeepsTheLevelItPaidForWhateverIsBookedThatDay()
    {
        var cart = Cart();
        var till = new Till();
        MarketingFees.Choose(cart, Rules, "local", 6);
        MarketingFees.Renew(cart, Rules, 7, till.Pay);

        MarketingFees.Choose(cart, Rules, "citywide", 7);
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 7));
        MarketingFees.Choose(cart, Rules, "none", 7);
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 7));
        Assert.Null(MarketingFees.Booked(cart, Rules));
        MarketingFees.Choose(cart, Rules, "flyers", 7);

        // No refunds and no difference charges: the day's one fee stands, and the last booking runs tomorrow.
        Assert.Single(till.Charges);
        Assert.Equal(2000, MarketingFees.Renew(cart, Rules, 8, till.Pay)!.Cents);
        Assert.Equal(1.10, MarketingFees.Factor(cart, Rules, 8));
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 7));
    }

    [Fact]
    public void StoppingEndsTheLevelAfterToday()
    {
        var cart = Cart("citywide", paidThrough: 7, paidCents: 8000);
        var till = new Till();

        MarketingFees.Choose(cart, Rules, "none", 7);

        Assert.Equal(1.30, MarketingFees.Factor(cart, Rules, 7));
        Assert.Null(MarketingFees.Renew(cart, Rules, 8, till.Pay));
        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 8));
        Assert.Empty(till.Charges);
    }

    [Fact]
    public void ABookingRenewedDaysLaterChargesOnlyTheDayItRuns()
    {
        var cart = Cart();
        var till = new Till();
        MarketingFees.Choose(cart, Rules, "local", 5);

        var charge = MarketingFees.Renew(cart, Rules, 8, till.Pay);

        Assert.Equal((8, 4500L), (Assert.Single(till.Charges).Day, charge!.Cents));
        foreach (var day in new[] { 5, 6, 7 }) Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, day));
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 8));
        Assert.Null(cart.MarketingPreviousDay);
    }

    [Fact]
    public void BookingDoesNotChangeTheDaysAlreadyPaid()
    {
        var cart = Cart();
        var till = new Till();
        MarketingFees.Choose(cart, Rules, "flyers", 6);
        MarketingFees.Renew(cart, Rules, 7, till.Pay);
        MarketingFees.Choose(cart, Rules, "local", 7);
        MarketingFees.Renew(cart, Rules, 8, till.Pay);

        MarketingFees.Choose(cart, Rules, "citywide", 8);

        Assert.Equal(1.10, MarketingFees.Factor(cart, Rules, 7));
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 8));
    }

    [Fact]
    public void YesterdaysLevelStillCountsForYesterdayAfterTodaysRenewalIsPaid()
    {
        var cart = Cart();
        var till = new Till();
        MarketingFees.Choose(cart, Rules, "local", 1);
        Assert.True(MarketingFees.Renew(cart, Rules, 2, till.Pay)!.Paid);
        MarketingFees.Choose(cart, Rules, "citywide", 2);

        Assert.True(MarketingFees.Renew(cart, Rules, 3, till.Pay)!.Paid);

        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 2));
        Assert.Equal(1.30, MarketingFees.Factor(cart, Rules, 3));
        Assert.Equal((2, "local"), (cart.MarketingPreviousDay, cart.MarketingPreviousLevel));
    }

    [Fact]
    public void YesterdaysLevelStillCountsForYesterdayAfterTodaysRenewalIsRefused()
    {
        var cart = Cart();
        MarketingFees.Choose(cart, Rules, "local", 1);
        MarketingFees.Renew(cart, Rules, 2, new Till().Pay);

        Assert.False(MarketingFees.Renew(cart, Rules, 3, new Till { Accept = false }.Pay)!.Paid);

        Assert.Null(cart.MarketingLevel);
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 2));
        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 3));
    }

    [Fact]
    public void OnlyTheLastTwoPaidDaysAreKept()
    {
        var cart = Cart();
        var till = new Till();
        MarketingFees.Choose(cart, Rules, "flyers", 3);
        MarketingFees.Renew(cart, Rules, 4, till.Pay);
        MarketingFees.Choose(cart, Rules, "local", 4);
        MarketingFees.Renew(cart, Rules, 5, till.Pay);
        MarketingFees.Renew(cart, Rules, 6, till.Pay);

        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 4));
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 5));
        Assert.Equal(1.20, MarketingFees.Factor(cart, Rules, 6));
        Assert.Equal(1.0, MarketingFees.Factor(cart, Rules, 3));
    }

    [Fact]
    public void ALevelPricedAtZeroRunsWithoutAPayment()
    {
        var free = new MarketingRules(new[]
        {
            new MarketingLevel("none", 1.0, new Dictionary<string, long> { ["cart"] = 0 }),
            new MarketingLevel("sign", 1.1, new Dictionary<string, long> { ["cart"] = 0 }),
        });
        var state = new SaveState();
        var cart = Cart();
        state.Vehicles.Add(cart);
        var till = new Till();

        MarketingFees.Choose(cart, free, "sign", 3);
        Assert.Equal(1.0, MarketingFees.Factor(cart, free, 3));
        Assert.Null(MarketingFees.Renew(cart, free, 4, till.Pay));
        Assert.Equal(4, cart.MarketingPaidThroughDay);
        Assert.Equal(1.1, MarketingFees.Factor(cart, free, 4));
        Assert.Null(MarketingFees.Renew(cart, free, 5, till.Pay));
        Assert.Equal(1.1, MarketingFees.Factor(cart, free, 4));
        Assert.Equal(1.1, MarketingFees.Factor(cart, free, 5));
        Assert.Empty(till.Charges);
        Assert.Empty(state.DailySales);
    }

    [Fact]
    public void BadArgumentsThrow()
    {
        var till = new Till();
        Assert.Throws<ArgumentException>(() => MarketingFees.Choose(Cart(), Rules, "billboards", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketingFees.Choose(Cart(), Rules, "flyers", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MarketingFees.Renew(Cart(), Rules, 0, till.Pay));
        Assert.Throws<ArgumentNullException>(() => MarketingFees.Renew(null!, Rules, 1, till.Pay));
        Assert.Throws<ArgumentNullException>(() => MarketingFees.Renew(Cart(), null!, 1, till.Pay));
        Assert.Throws<ArgumentNullException>(() => MarketingFees.Renew(Cart(), Rules, 1, null!));
        Assert.Equal(1.0, MarketingFees.Factor(null, Rules, 1));
    }

    [Fact]
    public void MarketingFieldsRoundTripAndOlderSavesReadNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        var saved = Cart("local", paidThrough: 9, paidCents: 4500);
        saved.MarketingPreviousDay = 8;
        saved.MarketingPreviousLevel = "flyers";
        saved.MarketingPaidLevel = "citywide";
        saved.MarketingStartsDay = 10;
        state.Vehicles.Add(saved);
        var loaded = Assert.Single(codec.Load(codec.Serialize(state)).State!.Vehicles);
        Assert.Equal(("local", 9, 4500L), (loaded.MarketingLevel, loaded.MarketingPaidThroughDay, loaded.MarketingPaidCents));
        Assert.Equal((8, "flyers"), (loaded.MarketingPreviousDay, loaded.MarketingPreviousLevel));
        Assert.Equal(("citywide", 10), (loaded.MarketingPaidLevel, loaded.MarketingStartsDay));

        var older = codec.Load($"{{\"schemaVersion\":{SaveState.CurrentSchemaVersion},\"vehicles\":[{{\"vehicleId\":\"cart-1\"}}]}}");
        Assert.Equal(LoadStatus.Loaded, older.Status);
        var vehicle = Assert.Single(older.State!.Vehicles);
        Assert.Null(vehicle.MarketingLevel);
        Assert.Null(vehicle.MarketingPaidThroughDay);
        Assert.Equal(0, vehicle.MarketingPaidCents);
        Assert.Null(vehicle.MarketingPreviousDay);
        Assert.Null(vehicle.MarketingPreviousLevel);
        Assert.Null(vehicle.MarketingPaidLevel);
        Assert.Null(vehicle.MarketingStartsDay);
    }

    [Fact]
    public void AnUnknownLevelLoadsKeptAndRunsAsNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(Cart("billboards", paidThrough: 9, paidCents: 25000));
        var json = codec.Serialize(state);

        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        var loaded = Assert.Single(result.State!.Vehicles);
        Assert.Equal("billboards", loaded.MarketingLevel);
        Assert.Equal(1.0, MarketingFees.Factor(loaded, Rules, 9));
        Assert.Null(MarketingFees.Renew(loaded, Rules, 10, new Till().Pay));
        Assert.Equal(json, codec.Serialize(result.State));
    }

    [Theory]
    [InlineData("marketingPaidThroughDay", -1)]
    [InlineData("marketingPaidCents", -1)]
    [InlineData("marketingPreviousDay", -1)]
    [InlineData("marketingStartsDay", -1)]
    public void NegativeMarketingFieldsAreCorrupt(string field, int value)
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Vehicles.Add(Cart("flyers"));
        var root = JObject.Parse(codec.Serialize(state));
        root["vehicles"]![0]![field] = value;
        Assert.Equal(LoadStatus.Corrupt, codec.Load(root.ToString()).Status);
    }
}
