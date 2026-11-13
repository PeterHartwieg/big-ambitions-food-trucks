using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class BayLotTests
{
    private const double Box = 0.1;

    private static Func<double, double, double, double, bool> Row(double depth) =>
        (x, z, _, _) => x >= -20 - Box && x <= 20 + Box && z >= -depth / 2 - Box && z <= depth / 2 + Box;

    private static IEnumerable<(double X, double Z)> RowAxes(double x, double z) =>
        x >= -20 && x <= 20 && Math.Abs(z) <= 4 ? new[] { (1.0, 0.0), (0.0, 1.0) } : Array.Empty<(double, double)>();

    [Fact]
    public void ALegalNoseInPoseIsTakenAsMeasured()
    {
        var bay = BaySnap.Measured(1.3, 0, 180, Row(5.0), RowAxes, WheelFootprint.Truck);
        Assert.NotNull(bay);
        Assert.Equal((1.3, 0.0), (bay!.X, bay.Z));
        Assert.Equal(0, bay.AlongX, 9);
        Assert.Equal(-1, bay.AlongZ, 9);
        var plain = BaySnap.Measured(1.3, 0, null, Row(5.0), RowAxes, WheelFootprint.Truck);
        Assert.Equal(1, Math.Abs(plain!.AlongX), 9);
    }

    [Fact]
    public void ANoseInPoseOffTheStallsSnapsFacingTheSameWay()
    {
        var bay = BaySnap.Measured(1.3, 1.0, 180, Row(5.0), RowAxes, WheelFootprint.Truck);
        Assert.NotNull(bay);
        Assert.Equal(-1, bay!.AlongZ, 9);
        Assert.InRange(bay.Z, -0.6, 0.6);
        Assert.True(BaySnap.Fits(bay.X, bay.Z, (bay.AlongX, bay.AlongZ), WheelFootprint.Truck, Row(5.0)));
    }

    [Fact]
    public void StallsTooShallowForTheTruckNoseInGiveNoBayRatherThanTurningIt()
    {
        Assert.Null(BaySnap.Measured(1.3, 0, 180, Row(3.0), RowAxes, WheelFootprint.Truck));
        Assert.NotNull(BaySnap.Measured(1.3, 0, null, Row(3.0), RowAxes, WheelFootprint.Truck));
    }

    [Fact]
    public void TheSideRuleStillDecidesAMeasuredNoseInPose()
    {
        Assert.Null(BaySnap.Measured(1.3, 0, 180, Row(5.0), RowAxes, WheelFootprint.Truck, reach: (_, _) => false));
    }

    [Fact]
    public void TheBridgeListsAndPicksOnlyTheMeasuredWay()
    {
        var listed = BaySnap.CandidatesWithPick(1.3, 0, Row(5.0), RowAxes, WheelFootprint.Truck, 20, null, heading: 180);
        Assert.All(listed.Places, p => Assert.Equal(-1, p.AlongZ, 9));
        Assert.Equal((1.3, 0.0), (listed.Automatic!.X, listed.Automatic.Z));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(90, 1, 0)]
    [InlineData(180, 0, -1)]
    [InlineData(-90, -1, 0)]
    public void HeadingsAreDegreesClockwiseFromZ(double degrees, double x, double z)
    {
        var (ax, az) = BaySnap.HeadingAxis(degrees);
        Assert.Equal(x, ax, 9);
        Assert.Equal(z, az, 9);
    }
}
