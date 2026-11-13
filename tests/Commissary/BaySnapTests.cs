using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class BaySnapTests
{
    private const double Box = 0.1;

    private static Func<double, double, double, double, bool> Lane(double x0, double x1, double z0, double z1) =>
        (x, z, _, _) => x >= x0 - Box && x <= x1 + Box && z >= z0 - Box && z <= z1 + Box;

    private static readonly Func<double, double, double, double, bool> HellsKitchen = Lane(391.9, 394.3, 200, 225);

    private static IEnumerable<(double X, double Z)> BothAxes(double x, double z) => new[] { (0.0, 1.0), (1.0, 0.0) };

    private static bool RealTruckLegal(Func<double, double, double, double, bool> legal, BaySnapResult r) =>
        BaySnap.Fits(r.X, r.Z, (r.AlongX, r.AlongZ), WheelFootprint.Truck, legal);

    [Fact]
    public void TheBaySnapsAcrossTheStreetToTheMiddleOfTheNearestLegalPlace()
    {
        var found = BaySnap.Nearest(404.5, 228.4, HellsKitchen, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.Equal(393.1, found!.X, 1);
        Assert.InRange(found.Z, 222.0, 223.1);
        Assert.Equal(0, found.AlongX, 9);
        Assert.Equal(1, found.AlongZ, 9);
        Assert.True(RealTruckLegal(HellsKitchen, found));
    }

    [Fact]
    public void APointAlreadyOnLegalParkingStaysThereCentredAcrossTheLane()
    {
        var found = BaySnap.Nearest(393.2, 217.6, HellsKitchen, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.Equal(393.1, found!.X, 1);
        Assert.Equal(217.6, found.Z, 6);
        Assert.InRange(found.Distance, 0, 0.11);
    }

    [Fact]
    public void ThePlaceFoundPassesTheRealWheelsEvenAtTheLanesEdge()
    {
        var legal = Lane(-1.2, 1.2, -20, 20);
        foreach (var start in new[] { 0.44, -0.44, 0.3, 1.1, -1.25, 3.0 })
        {
            var found = BaySnap.Nearest(start, 0, legal, BothAxes, WheelFootprint.Truck);
            Assert.NotNull(found);
            Assert.True(RealTruckLegal(legal, found!));
            Assert.True(BaySnap.Fits(found.X, found.Z, (found.AlongX, found.AlongZ),
                WheelFootprint.Truck.WithClearance(BaySnap.ClearanceAlongMetres, BaySnap.ClearanceAcrossMetres), legal));
            Assert.InRange(found.X, -0.06, 0.06);
        }
    }

    [Fact]
    public void ALaneJustWideEnoughIsFoundOnlyByCentring()
    {
        var legal = Lane(-0.88, 0.88, -20, 20);
        var found = BaySnap.Nearest(0.3, 4, legal, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.InRange(found!.X, -0.03, 0.03);
    }

    [Fact]
    public void ALaneNarrowerThanTheTruckWithItsClearanceHasNoPlace()
    {
        Assert.Null(BaySnap.Nearest(0, 0, Lane(-0.85, 0.85, -20, 20), BothAxes, WheelFootprint.Truck));
    }

    [Fact]
    public void ALaneTooShortForTheWheelbaseAndClearanceHasNoPlace()
    {
        Assert.NotNull(BaySnap.Nearest(0, 0, Lane(-1.2, 1.2, -1.9, 1.9), BothAxes, WheelFootprint.Truck));
        Assert.Null(BaySnap.Nearest(0, 0, Lane(-1.2, 1.2, -1.85, 1.85), BothAxes, WheelFootprint.Truck));
    }

    [Fact]
    public void ARotatedLaneIsSearchedAlongItsOwnAxes()
    {
        var angle = 30 * Math.PI / 180;
        var along = (X: Math.Sin(angle), Z: Math.Cos(angle));
        var across = (X: -along.Z, Z: along.X);
        var oriented = new List<(double, double)>();
        bool Legal(double x, double z, double ax, double az)
        {
            oriented.Add((ax, az));
            var dx = x - 5;
            var dz = z - 5;
            var a = dx * along.X + dz * along.Z;
            var c = dx * across.X + dz * across.Z;
            return Math.Abs(a) <= 10 + Box && Math.Abs(c) <= 1.2 + Box;
        }
        IEnumerable<(double, double)> Axes(double x, double z) => new[] { (along.X, along.Z), (across.X, across.Z) };
        var found = BaySnap.Nearest(0, 0, Legal, Axes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.Equal(along.X, found!.AlongX, 9);
        Assert.Equal(along.Z, found.AlongZ, 9);
        var off = (found.X - 5) * across.X + (found.Z - 5) * across.Z;
        Assert.InRange(off, -0.06, 0.06);
        Assert.All(oriented, o => Assert.True(Math.Abs(o.Item1 - along.X) < 1e-9 || Math.Abs(o.Item1 - across.X) < 1e-9));
    }

    [Fact]
    public void AScrapOfParkingTooSmallForATruckIsPassedOver()
    {
        var scrap = Lane(0, 2, 0, 2);
        var lane = Lane(11, 13.4, -20, 20);
        bool Legal(double x, double z, double ax, double az) => scrap(x, z, ax, az) || lane(x, z, ax, az);
        var found = BaySnap.Nearest(1, 1, Legal, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.Equal(12.2, found!.X, 1);
    }

    [Fact]
    public void APlaceTheCallerRejectsIsSkippedAndTheSearchGoesOn()
    {
        var behind = Lane(3, 5.4, -20, 20);
        var front = Lane(-10.4, -8, -20, 20);
        bool Legal(double x, double z, double ax, double az) => behind(x, z, ax, az) || front(x, z, ax, az);
        var found = BaySnap.Nearest(0, 0, Legal, BothAxes, WheelFootprint.Truck, reach: (x, z) => x < 0);
        Assert.NotNull(found);
        Assert.Equal(-9.2, found!.X, 1);
    }

    [Fact]
    public void CentringNeverMovesThePlacePastTheLimit()
    {
        Assert.Null(BaySnap.Nearest(0, 0, Lane(30, 32.4, -20, 20), BothAxes, WheelFootprint.Truck));
        var found = BaySnap.Nearest(0, 0, Lane(27.0, 29.4, -20, 20), BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.True(found!.Distance <= BaySnap.MaxMetres);
        Assert.Equal(28.2, found.X, 1);
    }

    [Fact]
    public void ASavedPosePlacesTheWheelsWhereALiveTruckHasThem()
    {
        foreach (var yaw in new[] { 0.0, 90.0, 37.0, 181.0, -120.0 })
        {
            var r = yaw * Math.PI / 180;
            var forward = (X: Math.Sin(r), Z: Math.Cos(r));
            var placed = WheelFootprint.Truck.Place(10, 20, forward.X, forward.Z).ToList();
            var live = WheelFootprint.Truck.Wheels.Select(w =>
            {
                var lx = w.Across;
                var lz = w.Along;
                return (X: 10 + lx * Math.Cos(r) + lz * Math.Sin(r), Z: 20 - lx * Math.Sin(r) + lz * Math.Cos(r));
            }).ToList();
            for (var i = 0; i < live.Count; i++)
            {
                Assert.Equal(live[i].X, placed[i].X, 9);
                Assert.Equal(live[i].Z, placed[i].Z, 9);
            }
        }
    }

    [Fact]
    public void ATruckCrosswiseOnALaneFailsWithOrWithoutItsController()
    {
        var legal = Lane(-1.2, 1.2, -20, 20);
        foreach (var (fx, fz, expected) in new[] { (1.0, 0.0, false), (0.0, 1.0, true), (0.0, -1.0, true) })
        {
            var saved = WheelFootprint.Truck.Place(0, 0, fx, fz).All(p => legal(p.X, p.Z, fx, fz));
            var r = Math.Atan2(fx, fz);
            var live = WheelFootprint.Truck.Wheels.All(w =>
                legal(w.Across * Math.Cos(r) + w.Along * Math.Sin(r), -w.Across * Math.Sin(r) + w.Along * Math.Cos(r), fx, fz));
            Assert.Equal(expected, saved);
            Assert.Equal(live, saved);
            Assert.True(legal(0, 0, fx, fz));
        }
    }

    [Fact]
    public void ACompleteButTooLongWalkStaysRejected()
    {
        var behind = WalkRule.Classify(true, true, complete: true, pathMetres: 80, straightMetres: 10);
        Assert.Equal(WalkVerdict.TooLong, behind);
        Assert.False(WalkRule.Accepts(behind));
        Assert.True(WalkRule.Accepts(WalkRule.Classify(true, true, true, 14, 10)));
        Assert.True(WalkRule.Accepts(WalkRule.Classify(true, true, true, 31, 10)));
        Assert.False(WalkRule.Accepts(WalkRule.Classify(true, true, true, 31.01, 10)));
        Assert.Equal(WalkVerdict.NoRoute, WalkRule.Classify(true, true, false, 0, 10));
        Assert.False(WalkRule.Accepts(WalkVerdict.NoRoute));
        Assert.Equal(WalkVerdict.Unknown, WalkRule.Classify(false, true, false, 0, 10));
        Assert.True(WalkRule.Accepts(WalkVerdict.Unknown));
    }

    private static (double, double, double, double) AxisAngle(double x, double y, double z, double degrees)
    {
        var h = degrees * Math.PI / 360;
        return (x * Math.Sin(h), y * Math.Sin(h), z * Math.Sin(h), Math.Cos(h));
    }

    private static (double, double, double, double) Multiply((double X, double Y, double Z, double W) a, (double X, double Y, double Z, double W) b) =>
        (a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
         a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
         a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
         a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    private static (double, double, double) ByMatrix((double X, double Y, double Z, double W) q, (double X, double Y, double Z) v)
    {
        var (x, y, z, w) = q;
        return ((1 - 2 * (y * y + z * z)) * v.X + 2 * (x * y - z * w) * v.Y + 2 * (x * z + y * w) * v.Z,
                2 * (x * y + z * w) * v.X + (1 - 2 * (x * x + z * z)) * v.Y + 2 * (y * z - x * w) * v.Z,
                2 * (x * z - y * w) * v.X + 2 * (y * z + x * w) * v.Y + (1 - 2 * (x * x + y * y)) * v.Z);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(15, 0, 0)]
    [InlineData(0, 0, 5)]
    [InlineData(-8, 37, 4)]
    [InlineData(6, 181, -12)]
    public void ASavedPoseProbesTheWheelsWhereTheLiveTruckDoes(double pitch, double yaw, double roll)
    {
        var q = Multiply(Multiply(AxisAngle(0, 1, 0, yaw), AxisAngle(1, 0, 0, pitch)), AxisAngle(0, 0, 1, roll));
        var position = (12.0, 0.4, -7.0);
        foreach (var hub in new[] { (0.87, 0.544, 1.80), (-0.87, 0.544, -1.80), (0.87, 0.544, -1.80) })
        {
            var world = ByMatrix(q, hub);
            var up = ByMatrix(q, (0, 1, 0));
            var live = (position.Item1 + world.Item1 - 0.1 * up.Item1, position.Item2 + world.Item2 - 0.1 * up.Item2, position.Item3 + world.Item3 - 0.1 * up.Item3);
            var saved = PoseMath.WheelProbe(position, q, hub);
            Assert.Equal(live.Item1, saved.X, 9);
            Assert.Equal(live.Item2, saved.Y, 9);
            Assert.Equal(live.Item3, saved.Z, 9);
        }
        if (pitch == 15)
        {
            var saved = PoseMath.WheelProbe(position, q, (0.87, 0.544, -1.80));
            Assert.True(Math.Abs(saved.Y - (0.4 + 0.544 - 0.1)) > 0.3);
        }
    }

    [Fact]
    public void AWaitingCommissaryDoesNotHoldUpTheNext()
    {
        var pending = new[] { true, true, false };
        var due = new[] { false, true, true };
        Assert.Equal(1, ResolveOrder.Next(3, 0, i => pending[i] && due[i]));
        Assert.Equal(1, ResolveOrder.Next(3, 2, i => pending[i] && due[i]));
        due[0] = true;
        Assert.Equal(0, ResolveOrder.Next(3, 2, i => pending[i] && due[i]));
        Assert.Equal(-1, ResolveOrder.Next(3, 0, _ => false));
        Assert.Equal(-1, ResolveOrder.Next(0, 0, _ => true));
    }

    [Fact]
    public void ATruckNeedsLegalParkingACartDoesNot()
    {
        Assert.True(CommissaryBays.RequiresLegalParking("truck"));
        Assert.False(CommissaryBays.RequiresLegalParking("cart"));
    }

    [Fact]
    public void NoLegalParkingWithinReachFindsNothing()
    {
        Assert.Null(BaySnap.Nearest(0, 0, Lane(40, 42.4, -20, 20), BothAxes, WheelFootprint.Truck));
    }

    [Fact]
    public void TheGridRunsNearestFirstAndIsBuiltOnce()
    {
        var grid = BaySnap.Grid(3);
        Assert.Equal((0.0, 0.0), grid[0]);
        var distances = grid.Select(c => c.X * c.X + c.Z * c.Z).ToList();
        Assert.Equal(distances.OrderBy(d => d), distances);
        Assert.All(grid, c => Assert.True(c.X * c.X + c.Z * c.Z <= 9 + 1e-9));
        Assert.Same(grid, BaySnap.Grid(3));
    }

    [Fact]
    public void ClearanceMovesEveryWheelOutwards()
    {
        var wide = WheelFootprint.Truck.WithClearance(0.2, 0.1);
        Assert.Contains((2.0, 0.97), wide.Wheels.Select(w => (Math.Round(w.Along, 6), Math.Round(w.Across, 6))));
        Assert.Contains((-2.0, -0.97), wide.Wheels.Select(w => (Math.Round(w.Along, 6), Math.Round(w.Across, 6))));
    }

    [Fact]
    public void TheAutomaticPickAtTheLimitIsInTheListAndFlagged()
    {
        var legal = Lane(29, 31.4, -40, 40);
        var auto = BaySnap.Nearest(0, 0, legal, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(auto);
        Assert.Equal(30, auto!.X, 6);
        var result = BaySnap.CandidatesWithPick(0, 0, legal, BothAxes, WheelFootprint.Truck, 80, reach: null);
        Assert.NotNull(result.Automatic);
        Assert.Contains(result.Automatic!, result.Places);
        Assert.Equal(30, result.Automatic!.X, 6);
        Assert.Equal(0, result.Automatic.Z, 6);
        Assert.True(result.Places.Zip(result.Places.Skip(1), (a, b) => a.Distance <= b.Distance).All(x => x));
    }

    [Fact]
    public void TheExactAutomaticPickIsKeptOverACandidateCentimetresAway()
    {
        var legal = Lane(29, 31.05, -40, 40);
        var auto = BaySnap.Nearest(0, 0, legal, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(auto);
        var result = BaySnap.CandidatesWithPick(0, 0, legal, BothAxes, WheelFootprint.Truck, 80, reach: null);
        Assert.Same(result.Automatic, result.Places.Single(p => Math.Abs(p.Z) < 2 && Math.Abs(p.X - 30) < 1));
        Assert.Equal(auto!.X, result.Automatic!.X, 9);
        Assert.Equal(auto.Z, result.Automatic.Z, 9);
        Assert.True(result.Automatic.Distance <= BaySnap.MaxMetres + 1e-9);
    }

    [Fact]
    public void TheAutomaticPickSkipsAPlaceTheWalkRejects()
    {
        var behind = Lane(391.9, 394.3, 200, 240);
        var street = Lane(415.8, 418.2, 200, 240);
        bool Legal(double x, double z, double ax, double az) => behind(x, z, ax, az) || street(x, z, ax, az);
        bool Reach(double x, double z) => x > 400;
        var result = BaySnap.CandidatesWithPick(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, Reach);
        Assert.NotNull(result.Automatic);
        Assert.Contains(result.Automatic!, result.Places);
        Assert.Equal(417.0, result.Automatic!.X, 1);
        Assert.True(result.Places[0].X < 400);
        Assert.Null(BaySnap.CandidatesWithPick(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, Reach, automatic: false).Automatic);
        Assert.Null(BaySnap.CandidatesWithPick(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, (_, _) => false).Automatic);
    }

    [Fact]
    public void CandidatesListEveryStretchNearestFirstBeyondTheAutomaticLimit()
    {
        var near = Lane(391.9, 394.3, 200, 225);
        var far = Lane(452.8, 455.2, 210, 240);
        bool Legal(double x, double z, double ax, double az) => near(x, z, ax, az) || far(x, z, ax, az);
        var list = BaySnap.Candidates(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80);
        Assert.NotEmpty(list);
        Assert.True(list.Zip(list.Skip(1), (a, b) => a.Distance <= b.Distance + 1e-9).All(x => x));
        Assert.Contains(list, c => Math.Abs(c.X - 393.1) < 0.1);
        Assert.Contains(list, c => Math.Abs(c.X - 454.0) < 0.1 && c.Distance > BaySnap.MaxMetres);
        var auto = BaySnap.Nearest(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck);
        Assert.Equal(auto!.X, list[0].X, 6);
        Assert.Equal(auto.Z, list[0].Z, 6);
        foreach (var c in list)
        {
            Assert.True(RealTruckLegal(Legal, c));
            Assert.All(list.Where(o => !ReferenceEquals(o, c)), o => Assert.True(BaySnap.Distance(o.X, o.Z, c.X, c.Z) >= BaySnap.CandidateSpacingMetres - 1e-9));
        }
        Assert.Empty(BaySnap.Candidates(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 5));
        Assert.Equal(2, BaySnap.Candidates(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, limit: 2).Count);
    }
}

public class CommissaryBaysTests
{
    private static readonly (double X, double Z)[] Origins = { (404.5, 228.4) };

    [Fact]
    public void StoredBaysAreUsedOnlyForTheSameOriginsAndRule()
    {
        var state = new CommissaryState();
        CommissaryBays.Store(state, "hells-kitchen", Origins, new[] { new BaySnapResult(393.1, 222.9, 0, 1, 12) });
        Assert.NotNull(CommissaryBays.Current(state, "hells-kitchen", Origins));
        Assert.Null(CommissaryBays.Current(state, "hells-kitchen", new[] { (393.2, 217.6) }));
        Assert.Null(CommissaryBays.Current(state, "murray-hill", Origins));
        state.Bays["hells-kitchen"].Rule = BaySnap.RuleVersion - 1;
        Assert.Null(CommissaryBays.Current(state, "hells-kitchen", Origins));
    }

    [Fact]
    public void ASearchThatFoundNothingIsStoredAsNoBay()
    {
        var state = new CommissaryState();
        CommissaryBays.Store(state, "murray-hill", Origins, Array.Empty<BaySnapResult>());
        var set = CommissaryBays.Current(state, "murray-hill", Origins);
        Assert.NotNull(set);
        Assert.Empty(set!.Points);
        Assert.False(CommissaryBays.InBay(set.Points, 404.5, 228.4, 15, legallyParked: true));
    }

    [Fact]
    public void AFailedSearchIsRepeatedOncePerLoad()
    {
        var state = new CommissaryState();
        Assert.True(CommissaryBays.SearchDue(CommissaryBays.Current(state, "murray-hill", Origins), searchedThisLoad: false));
        CommissaryBays.Store(state, "murray-hill", Origins, Array.Empty<BaySnapResult>());
        var none = CommissaryBays.Current(state, "murray-hill", Origins);
        Assert.True(CommissaryBays.SearchDue(none, searchedThisLoad: false));
        Assert.False(CommissaryBays.SearchDue(none, searchedThisLoad: true));
        CommissaryBays.Store(state, "murray-hill", Origins, new[] { new BaySnapResult(393.1, 222.9, 0, 1, 12) });
        Assert.False(CommissaryBays.SearchDue(CommissaryBays.Current(state, "murray-hill", Origins), searchedThisLoad: false));
    }

    [Fact]
    public void InTheBayNeedsLegalParkingAndTheRadius()
    {
        var points = new[] { new CommissaryBayPoint { X = 393.1, Z = 217.6, AlongX = 0, AlongZ = 1 } };
        Assert.True(CommissaryBays.InBay(points, 393.1, 227.6, 15, legallyParked: true));
        Assert.False(CommissaryBays.InBay(points, 400.0, 220.0, 15, legallyParked: false));
        Assert.False(CommissaryBays.InBay(points, 393.1, 233.0, 15, legallyParked: true));
    }

    [Fact]
    public void AMalformedStoredBayIsDroppedOnLoad()
    {
        var state = new CommissaryState();
        CommissaryBays.Store(state, "a", Origins, new[] { new BaySnapResult(1, 2, 0, 1, 0) });
        CommissaryBays.Store(state, "b", Origins, new[] { new BaySnapResult(double.NaN, 2, 0, 1, 0) });
        state.Bays["c"] = new CommissaryBaySet { Rule = BaySnap.RuleVersion, Origins = new List<CommissaryPoint>(), Points = new List<CommissaryBayPoint>() };
        CommissaryValidation.Validate(state);
        Assert.Equal(new[] { "a" }, state.Bays.Keys.ToArray());
    }
}
