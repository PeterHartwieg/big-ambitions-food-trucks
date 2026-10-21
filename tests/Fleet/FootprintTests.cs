using System;
using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class FootprintTests
{
    // Row-major 3 x 4: a turn about the vertical axis by `degrees` (clockwise seen from above, as Unity's yaw), a scale,
    // then a move.
    private static double[] Pose(double degrees, (double X, double Y, double Z) move, double scale = 1)
    {
        var r = degrees * Math.PI / 180;
        var (c, s) = (Math.Cos(r) * scale, Math.Sin(r) * scale);
        return new[] { c, 0, s, move.X, 0, scale, 0, move.Y, -s, 0, c, move.Z };
    }

    // a then b: the matrix of applying b first, then a.
    private static double[] Then(double[] a, double[] b)
    {
        var m = new double[12];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                var sum = col == 3 ? a[row * 4 + 3] : 0;
                for (var k = 0; k < 3; k++) sum += a[row * 4 + k] * b[k * 4 + col];
                m[row * 4 + col] = sum;
            }
        }
        return m;
    }

    // The inverse of a turn-and-move pose (no scale).
    private static double[] Inverse(double degrees, (double X, double Y, double Z) move)
    {
        var undoTurn = Pose(-degrees, (0, 0, 0));
        var undoMove = Pose(0, (-move.X, -move.Y, -move.Z));
        return Then(undoTurn, undoMove);
    }

    private static readonly LocalBox Truck = new((0, 1.2, 0), (2.2, 2.4, 6.0));

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(-30)]
    public void TheFootprintIsTheSameWhicheverWayTheVehicleFaces(double heading)
    {
        var where = (412.5, 0.1, -37.0);
        var collider = Pose(heading, where);
        var vehicleFromWorld = Inverse(heading, where);
        var box = Footprint.Box(new List<(double[], LocalBox)> { (Then(vehicleFromWorld, collider), Truck) })!.Value;
        Assert.Equal(-1.1, box.Min.X, 6);
        Assert.Equal(1.1, box.Max.X, 6);
        Assert.Equal(-3.0, box.Min.Z, 6);
        Assert.Equal(3.0, box.Max.Z, 6);
        Assert.Equal(0.0, box.Min.Y, 6);
        Assert.Equal(2.4, box.Max.Y, 6);
    }

    [Fact]
    public void APartTurnedAndScaledInsideTheVehicleCountsAsItStands()
    {
        // A 1 x 1 x 4 box turned 90 degrees and scaled by 0.5, 2 m forward: 2 m across, 0.5 m along.
        var box = Footprint.Box(new List<(double[], LocalBox)> { (Pose(90, (0, 0, 2), 0.5), new LocalBox((0, 0, 0), (1, 1, 4))) })!.Value;
        Assert.Equal(-1.0, box.Min.X, 6);
        Assert.Equal(1.0, box.Max.X, 6);
        Assert.Equal(1.75, box.Min.Z, 6);
        Assert.Equal(2.25, box.Max.Z, 6);
    }

    [Fact]
    public void TwoPartsMakeOneBoxAndNoPartsNone()
    {
        var parts = new List<(double[], LocalBox)>
        {
            (Pose(0, (0, 0, 0)), new LocalBox((0, 1, 1), (2, 2, 4))),
            (Pose(0, (0, 0, 0)), new LocalBox((0, 0.5, -2.5), (1, 1, 1))),
        };
        var box = Footprint.Box(parts)!.Value;
        Assert.Equal(-3.0, box.Min.Z, 6);
        Assert.Equal(3.0, box.Max.Z, 6);
        Assert.Null(Footprint.Box(new List<(double[], LocalBox)>()));
        Assert.Throws<ArgumentException>(() => Footprint.Transform(new double[3], (0, 0, 0)));
    }

    private static readonly double[] Identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };

    [Fact]
    public void APushedCartIsMeasuredByItsSwitchedOffCollider()
    {
        // The cart's hull, ±0.48 across, ±0.70 along (art/README.md), as a collider the game switched off while pushing.
        var hull = new[] { (Identity, new LocalBox((0, 0.475, 0), (0.96, 0.95, 1.40))) };
        var none = new (double[] Matrix, LocalBox Box)[0];
        var box = Footprint.Box(none, hull)!.Value;
        Assert.Equal((-0.48, 0.70), (box.Min.X, box.Max.Z), new TupleComparer());
        // An enabled collider wins; with none at all the caller falls back.
        var enabled = new[] { (Identity, new LocalBox((0, 1, 0), (2, 2, 6))) };
        Assert.Equal(3, Footprint.Box(enabled, hull)!.Value.Max.Z, 6);
        Assert.Null(Footprint.Box(none, none));
    }

    [Fact]
    public void TheBayGapIsMeasuredBetweenTheWholeFootprints()
    {
        // A 5.8 m truck at z 0 and a 1.5 m cart behind it along the same lane: 2.9 + 0.75 + 0.5 = 4.15 m apart keeps the
        // 0.5 m gap, a centimetre less doesn't (review round 1: shrinking the footprints first left about 0.4 m).
        var truck = new FlatBox(0, 0, 0, 1, 1.25, 2.9);
        Assert.True(Footprint.KeepsGap(new FlatBox(0, -4.15, 0, 1, 0.5, 0.75), truck, 0.5));
        Assert.False(Footprint.KeepsGap(new FlatBox(0, -4.14, 0, 1, 0.5, 0.75), truck, 0.5));
        Assert.False(Footprint.KeepsGap(new FlatBox(0, -3.75, 0, 1, 0.5, 0.75), truck, 0.5));
        // Beside the truck, 3 m across: no part of either within the other's lane, so the gap along doesn't matter.
        Assert.True(Footprint.KeepsGap(new FlatBox(3, 0, 0, 1, 0.5, 0.75), truck, 0.5));
    }

    private sealed class TupleComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) a, (double, double) b) => Math.Abs(a.Item1 - b.Item1) < 1e-9 && Math.Abs(a.Item2 - b.Item2) < 1e-9;
        public int GetHashCode((double, double) t) => 0;
    }
}
