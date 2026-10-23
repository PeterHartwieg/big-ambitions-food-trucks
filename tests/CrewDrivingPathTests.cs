using System;
using FoodTrucks.Core.CrewDriving;
using Xunit;

namespace FoodTrucks.Core.Tests;

public class CrewDrivingPathTests
{
    [Fact]
    public void ProjectionIsClippedToWindowIncludingDistanceAtBothEnds()
    {
        var path = CrewDrivingTests.Straight();
        Assert.True(path.TryProject(2, 30, 10, 20, out var p));
        Assert.Equal(20, p.Along);
        Assert.Equal(Math.Sqrt(104), p.Distance, 8);
        Assert.Equal(2, p.Lateral, 8);
        Assert.Equal(0, p.Heading);
        Assert.True(path.TryProject(0, 1, 10, 20, out p));
        Assert.Equal(10, p.Along);
        Assert.Equal(9, p.Distance);
        Assert.False(path.TryProject(0, 0, -1, 5, out _));
        Assert.False(path.TryProject(0, 0, 20, 10, out _));
        Assert.False(path.TryProject(double.NaN, 0, 0, 5, out _));
        var longPath = new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 100), new ProbePoint(0, 200) });
        Assert.False(longPath.TryProject(0, 0, 0, 101, out _));
    }

    [Fact]
    public void SamplingIsClampedAndInputArrayIsCopied()
    {
        var input = new[] { new ProbePoint(0, 0), new ProbePoint(0, 100) };
        var path = new ProbePath(input);
        input[1] = new ProbePoint(100, 0);
        Assert.Equal(100, path.Length);
        Assert.Equal(100, path.Sample(200).Z);
        Assert.Equal(0, path.Sample(-1).Z);
        Assert.Throws<ArgumentOutOfRangeException>(() => path.Sample(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => path.HeadingAt(double.NaN));
    }

    [Fact]
    public void RejectsBadDegenerateHugeAndKinkedPaths()
    {
        Assert.Throws<ArgumentException>(() => new ProbePath(Array.Empty<ProbePoint>()));
        Assert.Throws<ArgumentException>(() => new ProbePath(new ProbePoint[2049]));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 0) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, .01) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(double.NaN, 0), new ProbePoint(0, 1) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, double.PositiveInfinity) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(100001, 0) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 101) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 20), new ProbePoint(20, 20) }));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 1), new ProbePoint(0, 0) }));
        var huge = new ProbePoint[12];
        for (var i = 0; i < huge.Length; i++) huge[i] = new ProbePoint(0, i * 100);
        Assert.Throws<ArgumentException>(() => new ProbePath(huge));
    }

    [Fact]
    public void SampledCurveMustRespectTruckSteeringAndProvidesExplicitSweptEnvelope()
    {
        var geometry = CrewDrivingTests.Truck();
        var path = CrewDrivingTests.Arc(20);
        path.ValidateGeometry(geometry);
        Assert.InRange(path.MaxCurvature, .049, .051);
        Assert.InRange(path.ChordErrorBound, 0, .04);
        var radius = path.RequiredSweptRadius(geometry);
        Assert.True(radius >= Math.Sqrt(geometry.Front * geometry.Front + geometry.Width * geometry.Width / 4) + ProbeController.CorridorTolerance);
        Assert.Throws<ArgumentException>(() => CrewDrivingTests.Arc(5).ValidateGeometry(geometry));
        Assert.Throws<ArgumentException>(() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 5) }).ValidateGeometry(geometry));
    }

    [Theory]
    [InlineData(0, 4, 1, 2.5, 30, 8)]
    [InlineData(3, 2, 1, 2.5, 30, 8)]
    [InlineData(3, 4, -1, 2.5, 30, 8)]
    [InlineData(3, 4, 1, 0, 30, 8)]
    [InlineData(3, 4, 1, 2.5, 90, 8)]
    [InlineData(3, 4, 1, 2.5, 30, 100)]
    [InlineData(double.NaN, 4, 1, 2.5, 30, 8)]
    public void RejectsInvalidMeasuredGeometry(double wheelbase, double front, double rear, double width, double steer, double speed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeGeometry(wheelbase, front, rear, width, steer, speed));
}
