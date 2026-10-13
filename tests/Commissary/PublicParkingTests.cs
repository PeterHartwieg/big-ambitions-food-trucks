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
    public void CoverageRequiresTheKindParameterToMatchTheRentedSpace(string? kind)
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

    [Theory]
    [InlineData(60f, 65f, true, 60f)]
    [InlineData(60f, 65f, false, 65f)]
    [InlineData(60f, 60f, true, 60f)]
    [InlineData(60f, 0f, true, 0f)]
    public void OnlyAnIncrementInACoveredHourIsRemoved(float before, float after, bool covered, float expected)
    {
        Assert.Equal(expected, Commissaries.PublicParkingAfterHour(before, after, covered));
    }

    [Fact]
    public void BayHoursBeforeAndAfterStreetParkingLeaveAllStreetHoursDue()
    {
        var state = Rented();
        var balance = 0f;
        foreach (var bay in new[] { "a", "a", null, null, null, "a", "a" })
            balance = Commissaries.PublicParkingAfterHour(balance, balance + 5f,
                Commissaries.PublicParkingCovered(state, "v", "truck", bay));
        Assert.Equal(15f, balance);
    }

    [Fact]
    public void MidnightChargesYesterdayBeforeTheNewDaysBayHour()
    {
        var state = Rented();
        var balance = Commissaries.PublicParkingAfterHour(0f, 5f, false);
        // Arriving at 23:50 adds no covered hourly tick before RunDaily collects yesterday's balance.
        var chargedYesterday = balance;
        balance = 0f;
        balance = Commissaries.PublicParkingAfterHour(balance, balance + 5f,
            Commissaries.PublicParkingCovered(state, "v", "truck", "a"));
        Assert.Equal(5f, chargedYesterday);
        Assert.Equal(0f, balance);
        // Today's later street hour belongs to the next midnight's bill.
        balance = Commissaries.PublicParkingAfterHour(balance, balance + 5f, false);
        Assert.Equal(5f, balance);
    }
}
