using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class BaySnapTests
{
    // The game's wheel test as a point test: a 0.1 m box round the wheel touches the lane, so a wheel passes up to
    // 0.1 m outside the lane's edge (its box overlaps). Lanes here are axis-aligned rectangles.
    private const double Box = 0.1;

    private static Func<double, double, double, double, bool> Lane(double x0, double x1, double z0, double z1) =>
        (x, z, _, _) => x >= x0 - Box && x <= x1 + Box && z >= z0 - Box && z <= z1 + Box;

    // Hell's Kitchen as the logs have it: the door at x 404.5, z 228.4 across a one-way street, and the only parking
    // lane on the far kerb, 2.4 m wide round x 393.1, from z 200 to z 225.
    private static readonly Func<double, double, double, double, bool> HellsKitchen = Lane(391.9, 394.3, 200, 225);

    private static IEnumerable<(double X, double Z)> BothAxes(double x, double z) => new[] { (0.0, 1.0), (1.0, 0.0) };

    // The real truck's wheels (±1.80 along, ±0.87 across) pass the game's test with the truck's middle here.
    private static bool RealTruckLegal(Func<double, double, double, double, bool> legal, BaySnapResult r) =>
        BaySnap.Fits(r.X, r.Z, (r.AlongX, r.AlongZ), WheelFootprint.Truck, legal);

    [Fact]
    public void TheBaySnapsAcrossTheStreetToTheMiddleOfTheNearestLegalPlace()
    {
        var found = BaySnap.Nearest(404.5, 228.4, HellsKitchen, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.Equal(393.1, found!.X, 1);
        // Far enough from the lane's end for the rear wheels and their clearance: z at most 225 + 0.1 - 2.0.
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
        // Review round 4: on a lane from x -1.2 to 1.2, 0.9.9's ±0.85 m probes accepted a middle at x 0.44, where the
        // real right wheels (0.87 m out, their boxes from 1.21 m) miss the lane. Every point tried here must give a
        // place the real wheels pass, with the clearance to spare.
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
        // Half width 0.88: the probes at 0.97 m pass only within 0.01 m of the middle, which the 0.5 m grid misses.
        var legal = Lane(-0.88, 0.88, -20, 20);
        var found = BaySnap.Nearest(0.3, 4, legal, BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.InRange(found!.X, -0.03, 0.03);
    }

    [Fact]
    public void ALaneNarrowerThanTheTruckWithItsClearanceHasNoPlace()
    {
        // Half width 0.85: the real wheels would just pass (0.87 within 0.85 + 0.1), the clearance doesn't.
        Assert.Null(BaySnap.Nearest(0, 0, Lane(-0.85, 0.85, -20, 20), BothAxes, WheelFootprint.Truck));
    }

    [Fact]
    public void ALaneTooShortForTheWheelbaseAndClearanceHasNoPlace()
    {
        // 4.0 m long: the probes reach 2.0 m either way, so the lane's 0.1 m box tolerance is all that's left.
        Assert.NotNull(BaySnap.Nearest(0, 0, Lane(-1.2, 1.2, -1.9, 1.9), BothAxes, WheelFootprint.Truck));
        Assert.Null(BaySnap.Nearest(0, 0, Lane(-1.2, 1.2, -1.85, 1.85), BothAxes, WheelFootprint.Truck));
    }

    [Fact]
    public void ARotatedLaneIsSearchedAlongItsOwnAxes()
    {
        // A lane at 30 degrees, 2.4 m wide and 20 m long, through (5, 5); the probe gets the lane's direction.
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
        // Two lanes: one 4 m off behind a building (rejected), one 9 m off on the door's street.
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
        // Review round 5: from (0, 0), a lane from x 30 to 32.4 admits the grid cell (30, 0); centred it would be
        // (31.2, 0), past the 30 m limit. Nothing is found there; a place found is always within the limit.
        Assert.Null(BaySnap.Nearest(0, 0, Lane(30, 32.4, -20, 20), BothAxes, WheelFootprint.Truck));
        var found = BaySnap.Nearest(0, 0, Lane(27.0, 29.4, -20, 20), BothAxes, WheelFootprint.Truck);
        Assert.NotNull(found);
        Assert.True(found!.Distance <= BaySnap.MaxMetres);
        Assert.Equal(28.2, found.X, 1);
    }

    [Fact]
    public void ASavedPosePlacesTheWheelsWhereALiveTruckHasThem()
    {
        // A live truck's wheel transforms are its local offsets turned by its yaw (Unity: x' = x cos + z sin,
        // z' = -x sin + z cos for a yaw about +y). Place must give the same points for any heading.
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
        // Review round 6: the middle of a truck parked across a 2.4 m lane is on parking; its wheels aren't. The
        // saved-pose test (Place) and the live test (wheel transforms at the same pose) agree.
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
        // Review round 7: the street behind a warehouse, 10 m away in a straight line but an 80 m walk round the block.
        var behind = WalkRule.Classify(true, true, complete: true, pathMetres: 80, straightMetres: 10);
        Assert.Equal(WalkVerdict.TooLong, behind);
        Assert.False(WalkRule.Accepts(behind));
        // The door's own street: a short walk.
        Assert.True(WalkRule.Accepts(WalkRule.Classify(true, true, true, 14, 10)));
        // The limit itself: 1.6 x 10 + 15 = 31 m.
        Assert.True(WalkRule.Accepts(WalkRule.Classify(true, true, true, 31, 10)));
        Assert.False(WalkRule.Accepts(WalkRule.Classify(true, true, true, 31.01, 10)));
        // No route between two ends on the NavMesh: rejected. An end off the NavMesh: unknown, accepted.
        Assert.Equal(WalkVerdict.NoRoute, WalkRule.Classify(true, true, false, 0, 10));
        Assert.False(WalkRule.Accepts(WalkVerdict.NoRoute));
        Assert.Equal(WalkVerdict.Unknown, WalkRule.Classify(false, true, false, 0, 10));
        Assert.True(WalkRule.Accepts(WalkVerdict.Unknown));
    }

    // A unit quaternion for a rotation of `degrees` about the unit axis (x, y, z).
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

    // The rotation matrix of a unit quaternion applied to v, written out independently of PoseMath.
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
        // Review round 7: the live test probes each wheel transform at its world position less 0.1 m along its up; a
        // truck pitched or rolled on a slope or kerb has its wheels there in 3D, not at a flat footprint plus the hub's height.
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
            // The flat placement plus the hub's height (0.9.9 before round 7) is well off a 15 degree pitch at the rear wheel.
            var saved = PoseMath.WheelProbe(position, q, (0.87, 0.544, -1.80));
            Assert.True(Math.Abs(saved.Y - (0.4 + 0.544 - 0.1)) > 0.3);
        }
    }

    [Fact]
    public void AWaitingCommissaryDoesNotHoldUpTheNext()
    {
        // Review round 6: the first commissary is unresolved but its retry isn't due; the second can be resolved.
        var pending = new[] { true, true, false };
        var due = new[] { false, true, true };
        Assert.Equal(1, ResolveOrder.Next(3, 0, i => pending[i] && due[i]));
        // It goes round from where it left off, wrapping.
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
        // Review round 1 (Astra): a long lane from x 29 to 31.4. Nearest takes the uncentred cell at x 30 (centring
        // would carry it past 30 m); the 80 m candidates centre it at x 30.2, beyond the limit.
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
    public void TheAutomaticPickSkipsAPlaceTheWalkRejects()
    {
        // Two lanes: one 11 m off behind the building (the walk rejects it), one 20 m off on the street.
        var behind = Lane(391.9, 394.3, 200, 240);
        var street = Lane(415.8, 418.2, 200, 240);
        bool Legal(double x, double z, double ax, double az) => behind(x, z, ax, az) || street(x, z, ax, az);
        bool Reach(double x, double z) => x > 400;
        var result = BaySnap.CandidatesWithPick(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, Reach);
        Assert.NotNull(result.Automatic);
        Assert.Contains(result.Automatic!, result.Places);
        Assert.Equal(417.0, result.Automatic!.X, 1);
        // The nearer place is still listed, ahead of the pick.
        Assert.True(result.Places[0].X < 400);
        Assert.Null(BaySnap.CandidatesWithPick(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, Reach, automatic: false).Automatic);
        Assert.Null(BaySnap.CandidatesWithPick(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80, (_, _) => false).Automatic);
    }

    [Fact]
    public void CandidatesListEveryStretchNearestFirstBeyondTheAutomaticLimit()
    {
        // Two lanes: the Hell's Kitchen one 11 m from the door, and another 50 m off, beyond the automatic 30 m.
        var near = Lane(391.9, 394.3, 200, 225);
        var far = Lane(452.8, 455.2, 210, 240);
        bool Legal(double x, double z, double ax, double az) => near(x, z, ax, az) || far(x, z, ax, az);
        var list = BaySnap.Candidates(404.5, 228.4, Legal, BothAxes, WheelFootprint.Truck, 80);
        Assert.NotEmpty(list);
        Assert.True(list.Zip(list.Skip(1), (a, b) => a.Distance <= b.Distance + 1e-9).All(x => x));
        Assert.Contains(list, c => Math.Abs(c.X - 393.1) < 0.1);
        Assert.Contains(list, c => Math.Abs(c.X - 454.0) < 0.1 && c.Distance > BaySnap.MaxMetres);
        // The nearest candidate is the automatic search's own pick.
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
        // A bay found is kept and never searched again on a load.
        CommissaryBays.Store(state, "murray-hill", Origins, new[] { new BaySnapResult(393.1, 222.9, 0, 1, 12) });
        Assert.False(CommissaryBays.SearchDue(CommissaryBays.Current(state, "murray-hill", Origins), searchedThisLoad: false));
    }

    [Fact]
    public void InTheBayNeedsLegalParkingAndTheRadius()
    {
        var points = new[] { new CommissaryBayPoint { X = 393.1, Z = 217.6, AlongX = 0, AlongZ = 1 } };
        // On the bay's lane, 10 m along: in.
        Assert.True(CommissaryBays.InBay(points, 393.1, 227.6, 15, legallyParked: true));
        // The garage's own kerb across the street, 7 m from the bay point but no parking there (review round 4): out.
        Assert.False(CommissaryBays.InBay(points, 400.0, 220.0, 15, legallyParked: false));
        // Legal parking past the radius: out.
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
