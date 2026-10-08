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
    public void FoodOnBoardForThreeDaysIsStale()
    {
        var cart = new VehicleState { VehicleId = "c1" };
        ReputationBook.TrackStock(cart, new[] { "hotdog" }, 3);
        ReputationBook.TrackStock(cart, new[] { "hotdog", "soda" }, 5);

        Assert.False(book.Sold(cart, "hotdog", 5));
        Assert.True(book.Sold(cart, "hotdog", 6));
        Assert.False(book.Sold(cart, "soda", 6));
        Assert.True(cart.Days.Single(d => d.Day == 6).SoldStale);

        // Sold out and loaded again: fresh.
        ReputationBook.TrackStock(cart, new[] { "soda" }, 7);
        ReputationBook.TrackStock(cart, new[] { "hotdog", "soda" }, 7);
        Assert.False(book.IsStale(cart, "hotdog", 8));
    }
}
