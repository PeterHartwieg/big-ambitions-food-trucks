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
        // Door to +z: the marker faces -z, and a Unity transform facing -z has its right axis at -x.
        var (x, z) = BayLayout.PaintAxis(0, 1);
        Assert.Equal(-1, x, 9);
        Assert.Equal(0, z, 9);
        // Door to +x: facing -x, right is +z.
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
        // Parking only on one side of the door, from 0 to 8 m along the world direction `kerb` (either way along the
        // street, whichever the lane's direction gave). The paint at local x must land on the same world points.
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
        // 0.9.8 assumed a whole bay here and painted it on a no-parking kerb.
        var found = BayLayout.Stretches(9.6, _ => false);
        Assert.Equal(BayStretchSource.NoneUsable, found.Source);
        Assert.Empty(found.Spans);
    }

    [Fact]
    public void ParkingTooShortEverywhereDrawsNothing()
    {
        // A 1 m fragment near a corner: no full-length bay is made up.
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
        // A 5.8 m van with its middle 2.5 m past the stretch's end (beyond 0.9.9's 2 m rule) stands 0.4 m in the paint.
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 17.0, 0, 1, 0, 2.9, 0.9, 0));
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 16.4, 0, 1, 0, 2.4, 0.9, 0));
        // Wholly beyond the end, past the margin.
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 17.1, 0, 1, 0, 2.4, 0.9, 0.1));
    }

    [Fact]
    public void ACarAlongsideInTheDrivingLaneIsNotInTheBay()
    {
        // 3.1 m across (the driving lane's middle), 0.9 m half width: its side 2.2 m out, the paint's edge at 1.175 m.
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 0, 3.1, 1, 0, 2.4, 0.9, 0.1));
        // Half over the paint's edge.
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 0, 1.9, 1, 0, 2.4, 0.9, 0.1));
    }

    [Fact]
    public void ACarTurnedAcrossTheLaneReachesFurtherAcross()
    {
        // Side by side parking: turned 90 degrees, its length runs across the lane.
        Assert.True(BayKeepOut.Overlaps(Stretch, 2.35, 0, 3.4, 0, 1, 2.4, 0.9, 0));
        Assert.False(BayKeepOut.Overlaps(Stretch, 2.35, 0, 3.4, 1, 0, 2.4, 0.9, 0));
        // A car longer than a short stretch covering it wholly still counts.
        Assert.True(BayKeepOut.Overlaps(new[] { (0.0, 2.0) }, 2.35, 1, 0, 1, 0, 2.4, 0.9, 0));
    }

    [Fact]
    public void TheFacadeIsTheOutermostWallHitNearTheDoorsLine()
    {
        // A recessed door (0), pilasters 0.25 m out, a hydrant 1.4 m out: the pilasters are the wall.
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
