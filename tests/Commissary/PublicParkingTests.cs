using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class PublicParkingTests
{
    private static CommissaryState Rented(string kind = "truck")
    {
        var state = new CommissaryState();
        Commissaries.Unlock(state, "a");
        Commissaries.Unlock(state, "b");
        Commissaries.Rent(state, new CommissaryRules(), "v", kind, "a", 1);
        return state;
    }

    [Theory]
    [InlineData("cart")]
    [InlineData("truck")]
    public void ActiveRentCoversBothFoodKindsWithoutRequiringTodaysPayment(string kind)
    {
        var state = Rented(kind);
        Assert.True(Commissaries.PublicParkingCovered(state, "v", kind, "a"));
        // A failed rent payment remains an active space, as it does for the overnight check.
        state.Charges.AddRange(Commissaries.RentDue(state, new CommissaryRules(), 1));
        Assert.True(Commissaries.PublicParkingCovered(state, "v", kind, "a"));
        Assert.Single(state.Spaces);
        Assert.Single(state.Charges);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("car")]
    [InlineData("van")]
    [InlineData("cart")]
    public void ATruckSpaceDoesNotCoverOtherVehicleKinds(string? kind)
    {
        Assert.False(Commissaries.PublicParkingCovered(Rented(), "v", kind, "a"));
    }

    [Fact]
    public void EachVehicleNeedsItsOwnSpaceAtTheBayWhereItStands()
    {
        var state = Rented();
        Commissaries.Rent(state, new CommissaryRules(), "other", "truck", "b", 1);
        var parked = new[] { (Id: "v", Bay: "a"), (Id: "other", Bay: "a"), (Id: "unrented", Bay: "a") };
        Assert.Equal(new[] { "v" }, parked.Where(v => Commissaries.PublicParkingCovered(state, v.Id, "truck", v.Bay)).Select(v => v.Id));
        Assert.False(Commissaries.PublicParkingCovered(state, "v", "truck", "b"));
        Assert.True(Commissaries.PublicParkingCovered(state, "other", "truck", "b"));
    }

    [Fact]
    public void MovingOrReleasingTheSpaceChangesCoverageImmediately()
    {
        var state = Rented();
        Commissaries.Rent(state, new CommissaryRules(), "v", "truck", "b", 2);
        Assert.False(Commissaries.PublicParkingCovered(state, "v", "truck", "a"));
        Assert.True(Commissaries.PublicParkingCovered(state, "v", "truck", "b"));
        Commissaries.Release(state, "v");
        Assert.False(Commissaries.PublicParkingCovered(state, "v", "truck", "b"));
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(15, true, true)]
    [InlineData(15.01, true, false)]
    [InlineData(0, false, false)]
    public void TheExistingBayRadiusAndLegalParkingTestDecideCoverage(double metres, bool legal, bool covered)
    {
        var points = new[] { new CommissaryBayPoint { X = 100, Z = 200, AlongX = 1 } };
        var bayId = CommissaryBays.InBay(points, 100 + metres, 200, 15, legal) ? "a" : null;
        Assert.Equal(covered, Commissaries.PublicParkingCovered(Rented(), "v", "truck", bayId));
    }

    [Fact]
    public void AnUnresolvedOrMissingBayCannotWaivePublicParking()
    {
        foreach (var points in new IEnumerable<CommissaryBayPoint>?[] { null, new CommissaryBayPoint[0] })
        {
            var bayId = CommissaryBays.InBay(points, 100, 200, 15, true) ? "a" : null;
            Assert.False(Commissaries.PublicParkingCovered(Rented(), "v", "truck", bayId));
        }
    }
}
