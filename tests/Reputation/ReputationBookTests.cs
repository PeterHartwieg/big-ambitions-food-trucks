using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class ReputationBookTests
{
    private static readonly ReputationBook book = new(ShippedData.Rules);

    [Fact]
    public void AnOpenDayWithoutCleaningAddsHealthPoints()
    {
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "c1" };
        state.Vehicles.Add(cart);
        book.Opened(cart, 3, 42);
        ReputationBook.AddMinutes(cart, 3, "10:20", 90);

        book.CloseDaysBefore(state, 4);

        Assert.Equal(ShippedData.Rules.Health.UncleanedDayPoints, book.Points(cart));
        Assert.Empty(cart.Days);
    }

    [Fact]
    public void ACleanedDayAddsNothing()
    {
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "c1" };
        state.Vehicles.Add(cart);
        book.Opened(cart, 3, 42);
        ReputationBook.Day(cart, 3).Cleaned = true;

        book.CloseDaysBefore(state, 4);

        Assert.Equal(0, book.Points(cart));
    }

    [Fact]
    public void RegularsBuildAtASpotVisitedForAnHour()
    {
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "c1" };
        state.Vehicles.Add(cart);
        ReputationBook.AddMinutes(cart, 3, "10:20", 40);
        ReputationBook.AddMinutes(cart, 3, "10:20", 30);

        book.CloseDaysBefore(state, 4);

        Assert.True(book.RegularsFactor(state, "10:20", 10) > 1.0);
        Assert.Equal(1.0, book.RegularsFactor(state, "10:20", 11));
    }

    [Fact]
    public void DaysCloseInOrderAndTodayStays()
    {
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "c1" };
        state.Vehicles.Add(cart);
        ReputationBook.AddMinutes(cart, 2, "a", 90);
        ReputationBook.AddMinutes(cart, 3, "a", 90);
        ReputationBook.AddMinutes(cart, 4, "a", 90);

        book.CloseDaysBefore(state, 4);

        Assert.Equal(3, state.Regulars.LastUpdatedDay);
        Assert.Equal(new[] { 4 }, cart.Days.Select(d => d.Day).ToArray());
    }

    [Fact]
    public void DayRecordsSurviveASave()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "c1" };
        state.Vehicles.Add(cart);
        ReputationBook.AddMinutes(cart, 3, "a", 90);
        ReputationBook.Day(cart, 3).SoldStale = true;

        var loaded = codec.Load(codec.Serialize(state)).State!.Vehicles[0];

        Assert.True(loaded.Days.Single().SoldStale);
        Assert.Equal(90, loaded.Days.Single().Visits.Single().Minutes);
    }

    [Fact]
    public void TheOldestFoodSellsFirstAndGoesStaleAfterThreeDays()
    {
        var cart = new VehicleState { VehicleId = "c1" };
        ReputationBook.TrackStock(cart, new Dictionary<string, int> { ["hotdog"] = 10 }, 3);
        // Topped up on day 5: ten old, twenty new.
        ReputationBook.TrackStock(cart, new Dictionary<string, int> { ["hotdog"] = 30 }, 5);

        Assert.True(book.Sold(cart, "hotdog", 6, 6));
        Assert.True(cart.Days.Single(d => d.Day == 6).SoldStale);

        // The old ten sold (one above, nine more): what is left came on day 5.
        ReputationBook.Consume(cart, "hotdog", 9);
        ReputationBook.TrackStock(cart, new Dictionary<string, int> { ["hotdog"] = 20 }, 6);
        Assert.False(book.IsStale(cart, "hotdog", 7));
        Assert.True(book.IsStale(cart, "hotdog", 8));

        ReputationBook.TrackStock(cart, new Dictionary<string, int>(), 8);
        Assert.Empty(cart.StockBatches);
    }

    [Fact]
    public void ASaleTakesTheOldestUnitBeforeARefillIsSeen()
    {
        var cart = new VehicleState { VehicleId = "c1" };
        ReputationBook.TrackStock(cart, new Dictionary<string, int> { ["hotdog"] = 1 }, 1);

        // The last old unit sells, then a fresh box comes on before the next look at the cargo.
        book.Sold(cart, "hotdog", 2, 2);
        ReputationBook.TrackStock(cart, new Dictionary<string, int> { ["hotdog"] = 50 }, 5);

        Assert.Equal(5, cart.StockBatches["hotdog"].Single().Day);
    }

    [Fact]
    public void CleaningAfterALateShiftReachesTheNightsDay()
    {
        var cart = new VehicleState { VehicleId = "c1" };
        book.Opened(cart, 5, 1);

        // Cleaned on day 6, the morning after.
        ReputationBook.Cleaned(cart, 6, closedThrough: 4);

        Assert.True(cart.Days.Single(d => d.Day == 5).Cleaned);
        Assert.True(cart.Days.Single(d => d.Day == 6).Cleaned);
        Assert.Equal(1.0, book.RegularsFactor(new SaveState(), "a", 0));
    }

    [Fact]
    public void StaleSalesMarkTheWorkingDay()
    {
        var cart = new VehicleState { VehicleId = "c1" };
        ReputationBook.TrackStock(cart, new Dictionary<string, int> { ["hotdog"] = 10 }, 3);

        // Day 6 at 01:00: three calendar days old, sold on day 5's night.
        Assert.True(book.Sold(cart, "hotdog", 6, 5));

        Assert.True(cart.Days.Single(d => d.Day == 5).SoldStale);
    }

    [Fact]
    public void ALateNightCountsForTheEveningBefore()
    {
        var cart = new VehicleState { VehicleId = "c1" };
        // Day 3, 22:00 to day 4, 02:00, with the night ending at 05:00.
        ReputationBook.AddOpenMinutes(cart, (3 - 1) * 1440 + 22 * 60, (4 - 1) * 1440 + 2 * 60, "bars", 5 * 60, 0);

        Assert.Equal(240, cart.Days.Single(d => d.Day == 3).Visits.Single().Minutes);
        Assert.Equal(3, ReputationBook.ServiceDay((4 - 1) * 1440 + 4 * 60, 5 * 60));
        Assert.Equal(4, ReputationBook.ServiceDay((4 - 1) * 1440 + 5 * 60, 5 * 60));
    }

    [Fact]
    public void MinutesForACountedDayAreDropped()
    {
        var cart = new VehicleState { VehicleId = "c1" };

        ReputationBook.AddOpenMinutes(cart, (3 - 1) * 1440 + 12 * 60, (3 - 1) * 1440 + 13 * 60, "a", 5 * 60, closedThrough: 3);

        Assert.Empty(cart.Days);
    }

    [Fact]
    public void RegularsFadeThroughDaysNobodyWorked()
    {
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "c1" };
        state.Vehicles.Add(cart);
        ReputationBook.AddMinutes(cart, 3, "a", 90);
        book.CloseDaysBefore(state, 4);
        var weekday = 10;
        Assert.True(book.RegularsFactor(state, "a", weekday) > 1.0);

        // Three weeks without opening anywhere.
        book.CloseDaysBefore(state, 25);

        Assert.Equal(24, state.Regulars.LastUpdatedDay);
        Assert.Equal(1.0, book.RegularsFactor(state, "a", 24));
    }
}
