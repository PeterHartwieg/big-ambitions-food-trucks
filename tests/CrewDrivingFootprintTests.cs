using System;
using FoodTrucks.Core.CrewDriving;
using Xunit;

namespace FoodTrucks.Core.Tests;

public class CrewDrivingFootprintTests
{
    private static ProbeFootprint Rectangle() => ProbeFootprint.Create(new[] {
        new ProbePoint(-1.231, -3.15), new ProbePoint(1.260, -3.15),
        new ProbePoint(1.260, 2.65), new ProbePoint(-1.231, 2.65) });

    [Fact]
    public void FullSteeringSweepFitsCapturedWheelButLargerSweepDoesNot()
    {
        var body = Rectangle();
        foreach (var x in new[] { -.87, .87 })
            foreach (var z in new[] { -1.8, 1.8 })
                Assert.True(body.ContainsYawSweep(new ProbePoint(x, z), new ProbePoint(-.134, -.418),
                    new ProbePoint(.134, .418), 32 * Math.PI / 180, .005));
        Assert.False(body.ContainsYawSweep(new ProbePoint(-.87, 1.8), new ProbePoint(-.134, -.418),
            new ProbePoint(.134, .418), 45 * Math.PI / 180, .005));
    }

    [Fact]
    public void InteriorRotationPeakIsCheckedNotOnlyEndpoints()
    {
        var body = ProbeFootprint.Create(new[] { new ProbePoint(-1, -1), new ProbePoint(1, -1),
            new ProbePoint(1, 1), new ProbePoint(-1, 1) });
        Assert.True(body.ContainsYawSweep(new ProbePoint(0, 0), new ProbePoint(-.8, -.8), new ProbePoint(.8, .8), 0, 0));
        Assert.False(body.ContainsYawSweep(new ProbePoint(0, 0), new ProbePoint(-.8, -.8), new ProbePoint(.8, .8), Math.PI / 2, 0));
    }

    [Fact]
    public void ActualOutlineRejectsWheelInsideOnlyBoundingRectangle()
    {
        var body = ProbeFootprint.Create(new[] { new ProbePoint(-1, 0), new ProbePoint(0, -1),
            new ProbePoint(1, 0), new ProbePoint(0, 1), new ProbePoint(0, 0), new ProbePoint(1, 0) });
        Assert.False(body.ContainsYawSweep(new ProbePoint(.7, .7), new ProbePoint(-.1, -.1), new ProbePoint(.1, .1), 0, 0));
        Assert.True(body.ContainsYawSweep(new ProbePoint(0, 0), new ProbePoint(-.1, -.1), new ProbePoint(.1, .1), Math.PI, .01));
    }

    [Fact]
    public void InvalidAndDegenerateInputsRefuse()
    {
        Assert.Throws<ArgumentException>(() => ProbeFootprint.Create(new[] { new ProbePoint(0, 0), new ProbePoint(1, 1), new ProbePoint(2, 2) }));
        Assert.Throws<ArgumentException>(() => ProbeFootprint.Create(new[] { new ProbePoint(0, 0), new ProbePoint(double.NaN, 1), new ProbePoint(2, 2) }));
        Assert.False(Rectangle().ContainsYawSweep(new ProbePoint(0, 0), new ProbePoint(-1, -1), new ProbePoint(1, 1), double.NaN, 0));
        Assert.False(Rectangle().ContainsYawSweep(new ProbePoint(0, 0), new ProbePoint(-1, -1), new ProbePoint(1, 1), 0, -.01));
    }
}
