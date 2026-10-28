using FoodTrucks.Core.Pitches;
using Xunit;

namespace FoodTrucks.Core.Tests.Pitches;

public class UnusedPlaceRefundTests
{
    [Theory]
    [InlineData(0, 16000)]
    [InlineData(1, 8000)]
    [InlineData(2, 0)]
    [InlineData(3, 0)] // More distinct historical holders than bookings.
    public void RefundsOnlyUnusedBookings(int distinctVehicles, long expected)
    {
        var city = new CityEvent { Booked = 2, FeeCents = 8000 };
        for (var i = 0; i < distinctVehicles; i++)
        {
            city.Holds.Add(new PlaceHold { VehicleId = "truck-" + i, From = 600, Until = 650 });
            city.Holds.Add(new PlaceHold { VehicleId = "truck-" + i, From = 660 });
        }
        Assert.Equal(expected, CityEvents.UnusedPlaceRefund(city));
    }

    [Fact]
    public void SameMinuteHoldsUseNoBookingAndSequentialDistinctVehiclesEachUseOne()
    {
        var city = new CityEvent { Booked = 2, FeeCents = 8000 };
        city.Holds.Add(new PlaceHold { VehicleId = "zero", From = 600, Until = 600 });
        Assert.Equal(16000, CityEvents.UnusedPlaceRefund(city));
        city.Holds.Add(new PlaceHold { VehicleId = "first", From = 600, Until = 650 });
        Assert.Equal(8000, CityEvents.UnusedPlaceRefund(city));
        // Conservative lifetime use: two different vehicles consume two bookings even without overlap.
        city.Holds.Add(new PlaceHold { VehicleId = "second", From = 650, Until = 700 });
        Assert.Equal(0, CityEvents.UnusedPlaceRefund(city));
    }
}
