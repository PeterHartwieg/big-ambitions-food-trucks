using System;
using System.Collections.Generic;
using FoodTrucks.Core.CrewDriving;
using Xunit;

namespace FoodTrucks.Core.Tests;

public class CrewDrivingRoadPathTests
{
    // SYNTHETIC native-like fixtures. These are not captured Gley routes or measured native results.
    private static ProbePoint[] Turn(double radius = 20, double stepAngle = .2)
    {
        var p = new List<ProbePoint> { new(0, 0), new(0, .25), new(0, 10), new(0, 20) };
        var steps = (int)Math.Ceiling(Math.PI / 2 / stepAngle);
        for (var i = 1; i <= steps; i++)
        {
            var a = i * Math.PI / 2 / steps;
            p.Add(new ProbePoint(radius * (1 - Math.Cos(a)), 20 + radius * Math.Sin(a)));
        }
        p.Add(new ProbePoint(radius + 10, 20 + radius));
        p.Add(new ProbePoint(radius + 40, 20 + radius));
        return p.ToArray();
    }

    [Theory]
    [InlineData(3, 4, 1.2, 2.5, 30)]
    [InlineData(4.8, 6.4, 1.8, 2.6, 25)]
    [InlineData(6, 8, 2, 3, 20)]
    public void CoarseSmoothTurnIsPreparedForDifferentSuppliedTruckGeometries(double wheelbase, double front, double rear, double width, double steer)
    {
        var geometry = new ProbeGeometry(wheelbase, front, rear, width, steer, 8);
        var native = Circle(30, Math.PI / 2, 8);
        Assert.Throws<ArgumentException>(() => new ProbePath(native));
        var prepared = ProbeRoadPath.Prepare(native, geometry);
        prepared.Path.ValidateGeometry(geometry);
        Assert.InRange(prepared.CurvatureBound, 1 / 30.0, geometry.MaxCurvature * .65);
        Assert.InRange(prepared.Path.MaxCurvature, .025, geometry.MaxCurvature * .65);
        Assert.InRange(prepared.Path.Count, native.Length + 1, 250);
        Assert.Equal(native[0], prepared.Path.Sample(0));
        Assert.Equal(native[^1], prepared.Path.Sample(prepared.Path.Length));
        AssertSpacingAndMarkers(prepared, native);
    }

    [Fact]
    public void EveryNativeMarkerAndItsHeightParameterAreExactAndArraysAreDetached()
    {
        var source = Turn();
        var original = (ProbePoint[])source.Clone();
        var p = ProbeRoadPath.Prepare(source, CrewDrivingTests.Truck());
        AssertSpacingAndMarkers(p, original);
        var points = p.Points; var indices = p.SourcePointIndices; var parameters = p.SourceParameters;
        source[0] = new ProbePoint(999, 999); points[0] = source[0]; indices[0] = 99; parameters[0] = 99;
        Assert.Equal(original[0], p.Points[0]); Assert.Equal(original[0], p.Path.Sample(0));
        Assert.Equal(0, p.SourcePointIndices[0]); Assert.Equal(0, p.SourceParameters[0]);
        var again = ProbeRoadPath.Prepare(original, CrewDrivingTests.Truck());
        Assert.Equal(p.Points, again.Points); Assert.Equal(p.SourceParameters, again.SourceParameters);
        Assert.Equal(p.SourcePointIndices, again.SourcePointIndices);
    }

    [Fact]
    public void EntireReturnedChordsStayInTheirOriginalDirectedHalfMetreCorridor()
    {
        var source = Turn();
        var p = ProbeRoadPath.Prepare(source, CrewDrivingTests.Truck());
        var points = p.Points; var markers = p.SourcePointIndices;
        var maxDeviation = 0.0;
        for (var i = 0; i < source.Length - 1; i++)
        {
            var previousProjection = -1.0;
            for (var j = markers[i]; j < markers[i + 1]; j++)
                for (var k = 0; k <= 20; k++)
                {
                    var f = k / 20.0;
                    var q = new ProbePoint(points[j].X + (points[j + 1].X - points[j].X) * f,
                        points[j].Z + (points[j + 1].Z - points[j].Z) * f);
                    var distance = Distance(q, source[i], source[i + 1], out var projection);
                    Assert.InRange(distance, 0, ProbeRoadPath.CorridorMetres);
                    Assert.True(projection >= previousProjection - 1e-12);
                    previousProjection = projection;
                    maxDeviation = Math.Max(maxDeviation, distance);
                }
        }
        Assert.True(maxDeviation > .03); // actually exercises smoothing, rather than a straight polyline
    }

    [Theory]
    [InlineData(.02)]
    [InlineData(.05)]
    [InlineData(.1)]
    public void PreparedTurnTracksAndStopsWithPedalDrivenBicycle(double dt)
    {
        var path = ProbeRoadPath.Prepare(Turn(), CrewDrivingTests.Truck()).Path;
        var rig = new CrewDrivingTests.Rig(path: path);
        ProbeCommand command = default;
        var maxMiss = 0.0;
        for (var i = 0; i < 20000 && rig.Controller.State != ProbeState.Finished; i++)
        {
            command = rig.Tick(dt);
            Assert.True(command.State != ProbeState.Cancelled, command.Reason.ToString());
            Assert.InRange(command.BoundaryViolationMetres, 0, 0);
            Assert.True(path.TryProject(rig.X, rig.Z, Math.Max(0, command.RearAlong - 1),
                Math.Min(path.Length, command.RearAlong + 1), out var projection));
            maxMiss = Math.Max(maxMiss, projection.Distance);
            Assert.InRange(projection.Distance, 0, ProbeController.CorridorTolerance);
            Assert.InRange(Math.Abs(command.SteerDegrees), 0, rig.Geometry.MaxSteerDegrees);
        }
        Assert.Equal(ProbeState.Finished, command.State);
        Assert.InRange(rig.Heading, 1.5, 1.65);
        Assert.True(rig.MaxSpeed > 2);
        Assert.InRange(maxMiss, .01, ProbeController.CorridorTolerance);
        Assert.InRange(command.FrontBoundaryDistance, 1, 1.6);
    }

    [Fact]
    public void Float32DirectedCollinearityNoiseAndLongStraightSpansDoNotExplodeSampleCount()
    {
        var distances = new[] { 0.0, .25, 10, 40, 70, 100, 130, 160, 190, 220 };
        var source = new ProbePoint[distances.Length];
        for (var i = 0; i < source.Length; i++)
            source[i] = new ProbePoint((float)(312.5 + .12345 * distances[i]), (float)(-227.5 + .98765 * distances[i]));
        var p = ProbeRoadPath.Prepare(source, CrewDrivingTests.Truck());
        Assert.InRange(p.Points.Length, 220, 260);
        Assert.InRange(p.CurvatureBound, 0, .002);
        AssertSpacingAndMarkers(p, source);
        var straight = ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0), new ProbePoint(0, 999) }, CrewDrivingTests.Truck());
        Assert.InRange(straight.Points.Length, 1000, 1100);
        Assert.Equal(0, straight.Path.MaxCurvature);
    }

    [Fact]
    public void SamplingRefinesForHeadingAsWellAsDistanceWithoutTinyMarkerFragments()
    {
        var native = Circle(10, Math.PI / 2, 6);
        var p = ProbeRoadPath.Prepare(native, CrewDrivingTests.Truck());
        var points = p.Points;
        AssertSpacingAndMarkers(p, native);
        Assert.InRange(p.CurvatureBound, .1, CrewDrivingTests.Truck().MaxCurvature * .65);
        for (var i = 2; i < points.Length; i++)
        {
            var a = Math.Atan2(points[i - 1].X - points[i - 2].X, points[i - 1].Z - points[i - 2].Z);
            var b = Math.Atan2(points[i].X - points[i - 1].X, points[i].Z - points[i - 1].Z);
            Assert.InRange(Math.Abs(Math.Atan2(Math.Sin(b - a), Math.Cos(b - a))), 0, .12);
            var spacing = Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Z - points[i - 1].Z, 2));
            Assert.True(spacing < .8); // one-metre-only subdivision would leave wider turning spans
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothTurnDirectionsAndWorldCoordinateOffsetsAreSupported(bool mirror)
    {
        var source = Turn();
        for (var i = 0; i < source.Length; i++)
            source[i] = new ProbePoint((mirror ? -source[i].X : source[i].X) + 1400, source[i].Z - 800);
        AssertSpacingAndMarkers(ProbeRoadPath.Prepare(source, CrewDrivingTests.Truck()), source);
    }

    [Fact]
    public void SharpCornerAndShortTightCurveAreRefusedRatherThanFilleted()
    {
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0),
            new ProbePoint(0, 20), new ProbePoint(20, 20) }, CrewDrivingTests.Truck()));
        var tight = Circle(2, Math.PI / 2, 16);
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(tight, CrewDrivingTests.Truck()));
    }

    [Fact]
    public void FeasibleRadiusOutsideNarrowNativeCorridorIsRefused()
    {
        // Radius 30 is feasible, but these 14.8 m native chords need >0.5 m of lateral smoothing.
        var wide = Circle(30, 1.5, 3);
        var error = Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(wide, CrewDrivingTests.Truck()));
        Assert.Contains("certified", error.Message);
    }

    [Fact]
    public void SmoothClosedLoopCrossingAndRepeatAreRefused()
    {
        var closed = Circle(20, Math.PI * 2, 48);
        closed[^1] = closed[0];
        Assert.Contains("crosses", Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(closed, CrewDrivingTests.Truck())).Message);
        var crossing = Circle(20, Math.PI * 2.2, 53);
        Assert.Contains("crosses", Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(crossing, CrewDrivingTests.Truck())).Message);
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0),
            new ProbePoint(0, 20), new ProbePoint(0, 20), new ProbePoint(0, 40) }, CrewDrivingTests.Truck()));
    }

    [Fact]
    public void InvalidAndExcessiveInputsAreBoundedAndRefused()
    {
        var geometry = CrewDrivingTests.Truck();
        Assert.Throws<ArgumentNullException>(() => ProbeRoadPath.Prepare(Turn(), null!));
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(null!, geometry));
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(Array.Empty<ProbePoint>(), geometry));
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new ProbePoint[2049], geometry));
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 100001 })
        {
            Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(bad, 0), new ProbePoint(0, 20) }, geometry));
            Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0), new ProbePoint(0, bad) }, geometry));
        }
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0), new ProbePoint(0, .09) }, geometry));
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0), new ProbePoint(0, 1001) }, geometry));
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(new[] { new ProbePoint(0, 0), new ProbePoint(0, 5) }, geometry));
        var tooManySamples = new ProbePoint[2048];
        for (var i = 0; i < tooManySamples.Length; i++) tooManySamples[i] = new ProbePoint(0, i * .48);
        Assert.Equal(2048, ProbeRoadPath.Prepare(tooManySamples, geometry).Path.Count);
        // Each 0.96 m native span needs two <=0.95 m samples, exceeding the global sample cap.
        var exceedsSamples = new ProbePoint[1030];
        for (var i = 0; i < exceedsSamples.Length; i++) exceedsSamples[i] = new ProbePoint(0, i * .96);
        Assert.Throws<ArgumentException>(() => ProbeRoadPath.Prepare(exceedsSamples, geometry));
    }

    private static ProbePoint[] Circle(double radius, double angle, int steps)
    {
        var p = new ProbePoint[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var a = angle * i / steps;
            p[i] = new ProbePoint(radius * (1 - Math.Cos(a)), radius * Math.Sin(a));
        }
        return p;
    }

    private static void AssertSpacingAndMarkers(ProbeRoadPath prepared, ProbePoint[] source)
    {
        var points = prepared.Points; var markers = prepared.SourcePointIndices; var parameters = prepared.SourceParameters;
        Assert.Equal(source.Length, markers.Length); Assert.Equal(points.Length, parameters.Length);
        Assert.InRange(points.Length, 2, ProbePath.MaxPoints);
        Assert.InRange(prepared.Path.Length, 0, ProbePath.MaxLength);
        for (var i = 0; i < source.Length; i++)
        {
            Assert.Equal(source[i], points[markers[i]]); Assert.Equal(i, parameters[markers[i]]);
            if (i > 0) Assert.True(markers[i] > markers[i - 1]);
        }
        var along = 0.0;
        for (var j = 1; j < points.Length; j++)
        {
            var span = Math.Sqrt(Math.Pow(points[j].X - points[j - 1].X, 2) + Math.Pow(points[j].Z - points[j - 1].Z, 2));
            Assert.InRange(span, ProbeRoadPath.MinSampleSpacing, ProbeRoadPath.MaxSampleSpacing);
            along += span;
            Assert.True(parameters[j] > parameters[j - 1]);
            var sampled = prepared.Path.Sample(along);
            Assert.Equal(points[j].X, sampled.X, 8); Assert.Equal(points[j].Z, sampled.Z, 8);
        }
    }

    private static double Distance(ProbePoint p, ProbePoint a, ProbePoint b, out double projection)
    {
        var x = b.X - a.X; var z = b.Z - a.Z;
        projection = Math.Clamp(((p.X - a.X) * x + (p.Z - a.Z) * z) / (x * x + z * z), 0, 1);
        return Math.Sqrt(Math.Pow(p.X - a.X - projection * x, 2) + Math.Pow(p.Z - a.Z - projection * z, 2));
    }
}
