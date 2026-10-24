using FoodTrucks.Core.Pitches;
using Xunit;

namespace FoodTrucks.Core.Tests.Pitches;

public class UnusedPlaceRefundTests
{
    [Theory]
    [InlineData(5, 0, 16000)] // Future: neither place used.
    [InlineData(3, 1, 8000)]  // Running: one vehicle already claimed.
    [InlineData(1, 0, 16000)] // Ended and unclaimed.
    [InlineData(1, 1, 8000)]  // Ended with one unused place.
    [InlineData(1, 2, 0)]
    [InlineData(1, 3, 0)]    // More distinct historical holders than bookings.
    public void RefundsOnlyUnusedBookings(int eventDay, int distinctVehicles, long expected)
    {
        var city = new CityEvent { Day = eventDay, Start = 600, End = 960, Booked = 2, FeeCents = 8000 };
        for (var i = 0; i < distinctVehicles; i++)
        {
            city.Holds.Add(new PlaceHold { VehicleId = "truck-" + i, From = 600, Until = 650 });
            city.Holds.Add(new PlaceHold { VehicleId = "truck-" + i, From = 660 });
        }
        Assert.Equal(expected, CityEvents.UnusedPlaceRefund(city));
    }
}
