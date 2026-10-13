using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class BaySideTests
{
    private static List<((double X, double Z) A, (double X, double Z) B)> Polyline(params (double X, double Z)[] points) =>
        points.Zip(points.Skip(1), (a, b) => (a, b)).ToList();

    // A two-way street running along z at x 0: one lane each way, centres 1.75 m either side, waypoints every 10 m.
    private static List<((double X, double Z) A, (double X, double Z) B)> Street(double fromZ = -100, double toZ = 100)
    {
        var east = Enumerable.Range(0, (int)((toZ - fromZ) / 10) + 1).Select(i => (1.75, fromZ + i * 10.0)).ToArray();
        var west = east.Select(p => (-1.75, p.Item2)).Reverse().ToArray();
        return Polyline(east).Concat(Polyline(west)).ToList();
    }

    [Fact]
    public void ABayAtTheDoorsKerbIsOnItsSide()
    {
        // The door 3 m behind the east kerb (x 5); the truck's middle in the east parking lane at x 4.0, 3 m along.
        Assert.True(BaySide.SameSide(8, 0, 4.0, 12, Street()));
    }

    [Fact]
    public void ABayOnTheFarKerbIsAcrossTheRoad()
    {
        var crossing = BaySide.FirstCrossing(8, 0, -4.0, 12, Street());
        Assert.NotNull(crossing);
        // The nearer lane (x 1.75) is met first.
        Assert.Equal(1.75, crossing!.Value.X, 6);
        Assert.False(BaySide.SameSide(8, 0, -4.0, 12, Street()));
    }

    [Fact]
    public void HellsKitchenAsLoggedIsAcrossItsOneWayStreet()
    {
        // The door at x 404.5, z 228.4, the old measured bay at x 393.2, z 217.6 on the far kerb, one lane between them
        // running along z (its waypoints read about x 398.5).
        var lane = Polyline((398.5, 180), (398.5, 200), (398.5, 220), (398.5, 240), (398.5, 260));
        Assert.False(BaySide.SameSide(404.5, 228.4, 393.2, 217.6, lane));
        // The door's own kerb would pass.
        Assert.True(BaySide.SameSide(404.5, 228.4, 401.8, 215.0, lane));
    }

    [Fact]
    public void AroundTheCornerOnTheDoorsBlockIsTheSameSide()
    {
        // Main street along z at x 0 (lanes at ±1.75) and a side street along x at z 0 (lanes at z ±1.75), meeting in a
        // junction whose turning lanes run round inside it. The door is on the corner block's main-street face, at x 8,
        // z 10; the block lies at x > 5, z > 5.
        var lanes = new List<((double X, double Z) A, (double X, double Z) B)>();
        lanes.AddRange(Street());
        lanes.AddRange(Polyline((-100, 1.75), (-5, 1.75)).Concat(Polyline((5, 1.75), (100, 1.75))));
        lanes.AddRange(Polyline((100, -1.75), (5, -1.75)).Concat(Polyline((-5, -1.75), (-100, -1.75))));
        // A right turn from the main street's northbound lane into the side street, hugging the corner.
        lanes.AddRange(Polyline((1.75, -5), (2.6, -2.6), (5, -1.75)));
        lanes.AddRange(Polyline((1.75, 5), (2.6, 2.6), (5, 1.75)));
        // Round the corner on the side street's near kerb (z 4.0), 12 m along: same side.
        Assert.True(BaySide.SameSide(8, 10, 17, 4.0, lanes));
        // On the main street's near kerb past the side street (x 4.0, z -12): the walk crosses the side street.
        Assert.False(BaySide.SameSide(8, 10, 4.0, -12, lanes));
        // Across the main street.
        Assert.False(BaySide.SameSide(8, 10, -4.0, 14, lanes));
        // Across the side street, round the corner.
        Assert.False(BaySide.SameSide(8, 10, 17, -4.0, lanes));
    }

    [Fact]
    public void ACurvingStreetIsFollowedByItsWaypoints()
    {
        // A street bending round (0, 0) at radius 60 m (lanes at 58.25 and 61.75), waypoints every 5 degrees; the door on
        // the outside at radius 67, the near parking lane at radius 64.
        IEnumerable<(double X, double Z)> Arc(double r) =>
            Enumerable.Range(0, 37).Select(i => i * 5 * Math.PI / 180).Select(a => (r * Math.Cos(a), r * Math.Sin(a)));
        var lanes = Polyline(Arc(58.25).ToArray()).Concat(Polyline(Arc(61.75).ToArray())).ToList();
        (double X, double Z) At(double r, double degrees) => (r * Math.Cos(degrees * Math.PI / 180), r * Math.Sin(degrees * Math.PI / 180));
        var door = At(67, 45);
        // 25 m along the outer kerb: same side, though the walk cuts towards the road.
        var near = At(64, 45 + 25 / 64.0 * 180 / Math.PI);
        Assert.True(BaySide.SameSide(door.X, door.Z, near.X, near.Z, lanes));
        // The inner kerb opposite: across.
        var far = At(56, 50);
        Assert.False(BaySide.SameSide(door.X, door.Z, far.X, far.Z, lanes));
    }

    [Fact]
    public void TouchingALaneAtAWaypointOrRunningAlongOneCountsAsCrossing()
    {
        var lane = Polyline((0, -10), (0, 0), (0, 10));
        // Through the shared waypoint (0, 0) exactly.
        Assert.Equal((0.0, 0.0), BaySide.FirstCrossing(-5, 0, 5, 0, lane));
        // Ending on the line.
        Assert.False(BaySide.SameSide(-5, 3, 0, 3, lane));
        // Along the line itself: met where the walk starts on it.
        var along = BaySide.FirstCrossing(0, -20, 0, 5, lane);
        Assert.NotNull(along);
        Assert.Equal(-10, along!.Value.Z, 6);
        // Parallel beside it: not met.
        Assert.True(BaySide.SameSide(0.5, -20, 0.5, 20, lane));
    }

    [Fact]
    public void ALaneThatEndsShortOfTheWalkIsNotCrossed()
    {
        var lane = Polyline((0, 10), (0, 30));
        Assert.True(BaySide.SameSide(-5, 0, 5, 0, lane));
        Assert.True(BaySide.SameSide(-5, 9.99, 5, 9.99, lane));
        Assert.False(BaySide.SameSide(-5, 10, 5, 10, lane));
    }

    [Fact]
    public void LoneWaypointsNoLanesAndAZeroWalkCrossNothing()
    {
        var lone = new List<((double X, double Z) A, (double X, double Z) B)> { ((0, 0), (0, 0)) };
        Assert.True(BaySide.SameSide(-5, 0, 5, 0, lone));
        Assert.True(BaySide.SameSide(-5, 0, 5, 0, new List<((double X, double Z) A, (double X, double Z) B)>()));
        Assert.True(BaySide.SameSide(3, 3, 3, 3, Street()));
        Assert.Throws<ArgumentNullException>(() => BaySide.FirstCrossing(0, 0, 1, 1, null!));
    }

    [Fact]
    public void TheFirstCrossingIsTheOneNearestTheDoor()
    {
        var lanes = Polyline((5, -10), (5, 10)).Concat(Polyline((2, -10), (2, 10))).ToList();
        Assert.Equal((2.0, 0.0), BaySide.FirstCrossing(0, 0, 10, 0, lanes));
    }

    [Fact]
    public void TheBaySearchPassesOverTheFarKerbForTheDoorsOwn()
    {
        // Hell's Kitchen's street with parking on both kerbs: the far kerb's lane (x 391.9 to 394.3) nearer the door than
        // the near kerb's (x 402.7 to 405.1, from z 170 to 195). The search with the side rule as its reach
        // takes the near kerb; without the rule, the far one.
        const double Box = 0.1;
        bool Legal(double x, double z, double _, double __) =>
            (x >= 391.9 - Box && x <= 394.3 + Box && z >= 200 - Box && z <= 225 + Box) ||
            (x >= 402.7 - Box && x <= 405.1 + Box && z >= 170 - Box && z <= 195 + Box);
        IEnumerable<(double X, double Z)> Axes(double x, double z) => new[] { (0.0, 1.0) };
        var lane = Polyline((398.5, 140), (398.5, 180), (398.5, 220), (398.5, 260));
        var without = BaySnap.Nearest(408.0, 228.4, Legal, Axes, WheelFootprint.Truck);
        Assert.NotNull(without);
        Assert.Equal(393.1, without!.X, 1);
        var with = BaySnap.Nearest(408.0, 228.4, Legal, Axes, WheelFootprint.Truck, 40, (x, z) => BaySide.SameSide(408.0, 228.4, x, z, lane));
        Assert.NotNull(with);
        Assert.Equal(403.9, with!.X, 1);
        Assert.True(BaySide.SameSide(408.0, 228.4, with.X, with.Z, lane));
    }

    // Review round 1 (Astra and Sol): the door at (8, 0), the bay found at (4, 0) on its kerb, driving lanes at x ±1.75
    // between the kerbs. A truck on the opposite kerb at (-4, 0) is 8 m from the bay point, inside the 15 m radius.
    private static CommissaryBayPoint OppositeKerbsBay() =>
        new CommissaryBayPoint { X = 4, Z = 0, AlongX = 0, AlongZ = 1, DoorX = 8, DoorZ = 0 };

    [Fact]
    public void AVehicleOnTheOppositeKerbIsNotInTheBay()
    {
        var points = new[] { OppositeKerbsBay() };
        IReadOnlyList<((double X, double Z) A, (double X, double Z) B)> Lanes(CommissaryBayPoint _) => Street();
        Assert.True(CommissaryBays.WithinRadius(points, -4, 0, 15));
        Assert.Equal(false, CommissaryBays.OnDoorSide(points, -4, 0, 15, Lanes));
        Assert.Equal(true, CommissaryBays.OnDoorSide(points, 4, 9, 15, Lanes));
        // Outside the radius on the door's side: not in the bay either.
        Assert.Equal(false, CommissaryBays.OnDoorSide(points, 4, 20, 15, Lanes));
        Assert.Equal(false, CommissaryBays.OnDoorSide(null, 4, 0, 15, Lanes));
    }

    [Fact]
    public void AnUncheckableSideIsUnknownNeverIn()
    {
        var points = new[] { OppositeKerbsBay() };
        // Lanes unread, or a read that found none (an entrance far above the street): unknown, even on the door's kerb.
        Assert.Null(CommissaryBays.OnDoorSide(points, 4, 9, 15, _ => null));
        Assert.Null(CommissaryBays.OnDoorSide(points, 4, 9, 15, _ => new List<((double X, double Z) A, (double X, double Z) B)>()));
        // A point stored without its entrance can't be checked either.
        var old = new[] { new CommissaryBayPoint { X = 4, Z = 0, AlongX = 0, AlongZ = 1 } };
        Assert.Null(CommissaryBays.OnDoorSide(old, 4, 9, 15, _ => Street()));
        // One point that says yes is enough; an unknown one beside it doesn't change that.
        Assert.Equal(true, CommissaryBays.OnDoorSide(old.Concat(points), 4, 9, 15, _ => Street()));
    }

    [Fact]
    public void StoredBaysKeepTheEntranceTheirSideWasCheckedFrom()
    {
        var state = new CommissaryState();
        var set = CommissaryBays.Store(state, "a", new[] { (8.0, 0.0) }, new[] { (new BaySnapResult(4, 0, 0, 1, 4), ((double X, double Z)?)(8.0, 0.0)) });
        Assert.Equal(8, set.Points[0].DoorX);
        Assert.Equal(0, set.Points[0].DoorZ);
        Assert.True(CommissaryBays.WellFormed(set));
        set.Points[0].DoorZ = null;
        Assert.False(CommissaryBays.WellFormed(set));
        set.Points[0].DoorX = null;
        Assert.True(CommissaryBays.WellFormed(set));
        set.Points[0].DoorX = double.NaN;
        set.Points[0].DoorZ = 0;
        Assert.False(CommissaryBays.WellFormed(set));
    }

    [Fact]
    public void BaysStoredUnderTheOldRuleAreSearchedAgain()
    {
        Assert.Equal(4, BaySnap.RuleVersion);
    }
}
