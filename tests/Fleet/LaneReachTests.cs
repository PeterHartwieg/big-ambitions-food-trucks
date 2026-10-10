using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class LaneReachTests
{
    private readonly record struct Point(double X, double Y, double Z)
    {
        public double DistanceTo(Point o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y) + (Z - o.Z) * (Z - o.Z));
    }

    private static IEnumerable<int> Counted(IEnumerable<(int Id, Point P)> waypoints, Point probe, double radius) =>
        waypoints.Where(w => LaneReach.Counts(w.P.X - probe.X, w.P.Y - probe.Y, w.P.Z - probe.Z, radius)).Select(w => w.Id).OrderBy(i => i);

    // FreeParking reads the lanes once round the stop, at any height, and each space narrows them. A space on a slope,
    // a ramp or another road level must count exactly the waypoints its own read would.
    [Theory]
    [InlineData(60.0)]
    [InlineData(200.0)]
    public void OneWideReadGivesEachSpaceItsOwnReading(double searchMetres)
    {
        var random = new Random(7);
        var target = new Point(0, 0, 0);
        var waypoints = Enumerable.Range(0, 4000)
            .Select(i => (i, new Point(random.NextDouble() * 600 - 300, random.NextDouble() * 24 - 12, random.NextDouble() * 600 - 300)))
            .ToList();
        var cover = LaneReach.CoverRadius(searchMetres, 25);
        // The wide read: the game's search with no height filter.
        var read = waypoints.Where(w => w.Item2.DistanceTo(target) < cover).ToList();
        for (var s = 0; s < 300; s++)
        {
            // Spaces up to searchMetres away, up to 10 m above or below the stop.
            var space = new Point(random.NextDouble() * 2 * searchMetres - searchMetres, random.NextDouble() * 20 - 10, random.NextDouble() * 2 * searchMetres - searchMetres);
            if (space.DistanceTo(target) > searchMetres) continue;
            Assert.Equal(Counted(waypoints, space, 25), Counted(read, space, 25));
        }
    }

    [Fact]
    public void HeightAndDistanceAreTheProbesOwn()
    {
        Assert.True(LaneReach.Counts(10, 2.9, 0, 25));
        Assert.False(LaneReach.Counts(10, 3.1, 0, 25));
        Assert.False(LaneReach.Counts(25, 0, 0, 25));
        Assert.False(LaneReach.Counts(24, 0, 7.1, 25));
    }
}
