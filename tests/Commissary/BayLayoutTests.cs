using System;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class BayLayoutTests
{
    [Fact]
    public void ThePaintAxisIsTheRightAxisOfAMarkerFacingAwayFromTheDoor()
    {
        var (x, z) = BayLayout.PaintAxis(0, 1);
        Assert.Equal(-1, x, 9);
        Assert.Equal(0, z, 9);
        (x, z) = BayLayout.PaintAxis(2, 0);
        Assert.Equal(0, x, 9);
        Assert.Equal(1, z, 9);
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.6, -0.8)]
    public void AnAsymmetricStretchIsPaintedOnTheSideItWasFound(double acrossX, double acrossZ)
    {
        foreach (var sign in new[] { 1.0, -1.0 })
        {
            var axis = BayLayout.PaintAxis(acrossX, acrossZ);
            var kerb = (X: sign * axis.X, Z: sign * axis.Z);
            bool Parking(double wx, double wz)
            {
                var t = wx * kerb.X + wz * kerb.Z;
                return t >= -0.01 && t <= 8.01;
            }
            var found = BayLayout.Stretches(12, x => Parking(axis.X * x, axis.Z * x));
            Assert.Equal(BayStretchSource.Probed, found.Source);
            var (from, to) = Assert.Single(found.Spans);
            Assert.Equal(8, to - from, 6);
            foreach (var local in new[] { from, (from + to) / 2, to })
                Assert.True(Parking(axis.X * local, axis.Z * local));
            Assert.False(Parking(axis.X * -(from + to) / 2, axis.Z * -(from + to) / 2));
        }
    }

    [Fact]
    public void NoParkingAnywhereDrawsNothing()
    {
        var found = BayLayout.Stretches(9.6, _ => false);
        Assert.Equal(BayStretchSource.NoneUsable, found.Source);
        Assert.Empty(found.Spans);
    }

    [Fact]
    public void ParkingTooShortEverywhereDrawsNothing()
    {
        var found = BayLayout.Stretches(10, x => x >= 4 && x <= 5);
        Assert.Equal(BayStretchSource.NoneUsable, found.Source);
        Assert.Empty(found.Spans);
    }

    [Fact]
    public void StretchesAreSplitAtGapsAndClippedToTheReach()
    {
        var found = BayLayout.Stretches(10, x => x < -2 || x > 1);
        Assert.Equal(new[] { (-10.0, -2.5), (1.5, 10.0) }, found.Spans.ToArray());
    }

    [Fact]
    public void EdgesAreCutAtEveryWholeMetre()
    {
        Assert.Equal(new[] { -2.4, -2, -1, 0, 1, 1.5 }, BayLayout.Cuts(-2.4, 1.5));
        Assert.Equal(new[] { 0.2, 0.35 }, BayLayout.Cuts(0.2, 0.35));
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, BayLayout.Cuts(1, 3));
    }

    private static readonly (double, double)[] Stretch = { (-10.0, 14.5) };

    [Fact]
    public void ACarWithItsMiddleOutsideButItsBodyInThePaintIsInTheBay()
    {
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 17.0, 0, 1, 0, 2.9, 0.9, 0));
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 16.4, 0, 1, 0, 2.4, 0.9, 0));
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 17.1, 0, 1, 0, 2.4, 0.9, 0.1));
    }

    [Fact]
    public void ACarAlongsideInTheDrivingLaneIsNotInTheBay()
    {
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 0, 3.1, 1, 0, 2.4, 0.9, 0.1));
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 0, 1.9, 1, 0, 2.4, 0.9, 0.1));
    }

    [Fact]
    public void ACarTurnedAcrossTheLaneReachesFurtherAcross()
    {
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 0, 3.4, 0, 1, 2.4, 0.9, 0));
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 0, 3.4, 1, 0, 2.4, 0.9, 0));
        Assert.True(BayKeepOut.Overlaps(new[] { (0.0, 2.0) }, 2.35, 1, 0, 1, 0, 2.4, 0.9, 0));
    }

    [Fact]
    public void AnAngledCarClearOfTheCornerIsNotInTheBay()
    {
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 16.5, 3.2, 1, 1, 2.4, 0.9, 0.1));
        Assert.False(BayKeepOut.Overlaps(new[] { (0.0, 2.0) }, 2, 4, 2.5, 1, -1, 2.9, 0.9, 0.1));
    }

    [Fact]
    public void AnAngledCarWithACornerInThePaintIsInTheBay()
    {
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 15.5, 2.2, 1, 1, 2.4, 0.9, 0.1));
        Assert.True(BayKeepOut.Overlaps(new[] { (0.0, 2.0) }, 2, 2.8, 1.6, 1, -1, 2.9, 0.9, 0.1));
    }

    private sealed class Car
    {
        public Car(double along, double halfLength) { Along = along; HalfLength = halfLength; }
        public double Along { get; }
        public double HalfLength { get; }
    }

    [Fact]
    public void ACarFoundByOneZonesBoxButStandingInTheNextIsTaken()
    {
        var zones = new[] { (From: -14.5, To: 14.5), (From: -9.5, To: 19.5) };
        var car = new Car(18.5, 2.4);
        bool In((double From, double To) z, Car c) => BayKeepOut.Overlaps(new[] { z }, 2.35, c.Along, 0, 1, 0, c.HalfLength, 0.9, 0.1);
        Assert.False(In(zones[0], car));
        Assert.True(In(zones[1], car));
        Assert.Same(car, Assert.Single(BayKeepOut.CarsInZones(zones, _ => new[] { car }, In)));
        Assert.Same(car, Assert.Single(BayKeepOut.CarsInZones(zones.Reverse(), _ => new[] { car }, In)));
        var both = new Car(0, 2.4);
        var neither = new Car(30, 2.4);
        Assert.Equal(new[] { both, car }, BayKeepOut.CarsInZones(zones, _ => new[] { both, neither, car }, In));
    }

    [Fact]
    public void TheFacadeIsTheOutermostWallHitNearTheDoorsLine()
    {
        Assert.Equal(0.25, PropPlacement.FacadeOffset(new[] { 0.0, 0.25, 1.4 }, 0.8)!.Value, 9);
        Assert.Null(PropPlacement.FacadeOffset(new[] { 1.4 }, 0.8));
        Assert.Null(PropPlacement.FacadeOffset(Array.Empty<double>(), 0.8));
        Assert.Equal(-0.3, PropPlacement.FacadeOffset(new[] { -0.3 }, 0.8)!.Value, 9);
    }

    [Fact]
    public void PropsAreTriedOutwardsFromTheDoorWhileTheyEndWithinReach()
    {
        var offsets = PropPlacement.Offsets(2.0, 0.5, 4.0, 0.5).ToList();
        Assert.Equal(new[] { 2.5, 3.0, 3.5 }, offsets);
        Assert.Empty(PropPlacement.Offsets(2.0, 1.5, 4.0, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PropPlacement.Offsets(0, 1, 4, 0).ToList());
    }
}
