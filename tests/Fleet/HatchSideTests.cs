using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class HatchSideTests
{
    // A street across the truck, in metres along the truck's right (+) from its centre line: pavement beyond
    // the kerb, a parking lane between the kerb and the first driving lane, driving lanes 3.5 m wide.
    private sealed class Street
    {
        public double Kerb = 1.3;            // signed: + kerb on the truck's right (the hatch side)
        public double ParkingWidth = 2.4;
        public int Lanes = 2;
        public double LaneWidth = 3.5;
        public bool LanesKnown = true;       // false: the traffic system had no waypoints near
        public double SlantDegrees;          // the truck turned this much off the kerb's line
        public double[]? ExtraLanes;         // driving lane centres anywhere, e.g. a median's other side
        public bool ParkingSeen = true;      // false: the game's parking colliders miss the parking lane entirely

        private int Towards => Kerb > 0 ? -1 : 1; // from the kerb into the street

        private double[] LaneCentres()
        {
            var centres = Enumerable.Range(0, Lanes)
                .Select(i => Kerb + Towards * (ParkingWidth + LaneWidth / 2 + i * LaneWidth)).ToList();
            if (ExtraLanes != null) centres.AddRange(ExtraLanes);
            return centres.ToArray();
        }

        public Ground At(double sideways, double along)
        {
            var x = sideways + along * Math.Tan(SlantDegrees * Math.PI / 180);
            double? lane = LanesKnown ? LaneCentres().Select(c => Math.Abs(x - c)).DefaultIfEmpty(double.MaxValue).Min() : null;
            var beyondKerb = Kerb > 0 ? x >= Kerb : x <= Kerb;
            var parking = ParkingSeen && !beyondKerb && Math.Abs(x - Kerb) < ParkingWidth;
            return HatchSide.Classify(lane, roadLayer: false, parkingLane: parking, crosswalk: false, walkable: true);
        }

        public SideReading Side(int side) =>
            HatchSide.Read(HatchSide.Offsets.Select(o => (o, (IReadOnlyList<Ground>)HatchSide.Along.Select(a => At(side * o, a)).ToList())));

        public HatchVerdict Verdict() => HatchSide.Decide(Side(1), Side(-1));
    }

    [Theory]
    [InlineData(1.2)]  // flush against the kerb (half the truck's width is 1.10 m)
    [InlineData(1.6)]
    [InlineData(2.4)]  // over a metre off the kerb, still inside the parking lane
    [InlineData(3.4)]  // the far edge of a wide space
    public void KerbsideParkingWithTheHatchToThePavementOpens(double kerb)
    {
        var street = new Street { Kerb = kerb };
        Assert.Equal(HatchVerdict.Pavement, street.Verdict());
        Assert.True(HatchSide.Opens(street.Verdict()));
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    [InlineData(20)]
    public void ASlantedTruckStillOpens(double degrees)
    {
        Assert.Equal(HatchVerdict.Pavement, new Street { Kerb = 1.6, SlantDegrees = degrees }.Verdict());
        Assert.Equal(HatchVerdict.Pavement, new Street { Kerb = 2.4, SlantDegrees = degrees }.Verdict());
    }

    [Theory]
    [InlineData(-1.2)]
    [InlineData(-1.6)]
    [InlineData(-2.4)]
    public void TheHatchToTheRoadIsRefusedAsTurnedTheWrongWay(double kerb)
    {
        var street = new Street { Kerb = kerb };
        Assert.True(street.Side(1).RoadFirst);
        Assert.True(street.Side(-1).Clear);
        Assert.Equal(HatchVerdict.WrongWay, street.Verdict());
        Assert.False(HatchSide.Opens(street.Verdict()));
    }

    [Fact]
    public void ASlantedTruckWithTheHatchToTheRoadIsStillRefused()
    {
        Assert.Equal(HatchVerdict.WrongWay, new Street { Kerb = -1.6, SlantDegrees = 15 }.Verdict());
    }

    [Fact]
    public void WithoutAParkingLaneTheRoadSideStillReadsRoad()
    {
        // A space with the driving lane right beside it: the lane's centre is 1.75 m past the space's edge.
        Assert.Equal(HatchVerdict.WrongWay, new Street { Kerb = -1.3, ParkingWidth = 2.2, LaneWidth = 3.0 }.Verdict());
        Assert.Equal(HatchVerdict.Pavement, new Street { Kerb = 1.3, ParkingWidth = 2.2, LaneWidth = 3.0 }.Verdict());
    }

    [Fact]
    public void RoadOnBothSidesHasNoPavement()
    {
        // A space in a median: driving lanes on the hatch side and beyond the "kerb" on the other.
        var street = new Street { Kerb = -1.3, ExtraLanes = new[] { -2.8, -6.3 } };
        Assert.Equal(HatchVerdict.NoPavement, street.Verdict());
    }

    [Fact]
    public void WithoutTrafficDataEverythingWalkableIsPavement()
    {
        // The fallback when the traffic system has no lanes near: the NavMesh alone, as before 0.9.6.
        Assert.Equal(HatchVerdict.Pavement, new Street { Kerb = -1.6, LanesKnown = false }.Verdict());
    }

    [Fact]
    public void NothingKnownBesideTheHatchOpens()
    {
        var unknown = (IReadOnlyList<Ground>)new[] { Ground.Unknown, Ground.Unknown, Ground.Unknown };
        var hatch = HatchSide.Read(HatchSide.Offsets.Select(o => (o, unknown)));
        var opposite = new SideReading(1.5, null);
        Assert.Equal(HatchVerdict.Unclear, HatchSide.Decide(hatch, opposite));
        Assert.True(HatchSide.Opens(HatchVerdict.Unclear));
    }

    [Fact]
    public void PavementBeforeRoadOnTheHatchSideOpensEvenWithRoadFurtherOut()
    {
        // A narrow pavement with a street beyond it (a corner, an island): the line forms before the road.
        Assert.Equal(HatchVerdict.Pavement, HatchSide.Decide(new SideReading(2.0, 4.0), new SideReading(null, 2.0)));
        Assert.Equal(HatchVerdict.WrongWay, HatchSide.Decide(new SideReading(4.0, 2.0), new SideReading(1.5, null)));
    }

    [Fact]
    public void OneOddPointInARowChangesNothing()
    {
        var rows = new List<(double, IReadOnlyList<Ground>)>
        {
            (1.5, new[] { Ground.Pavement, Ground.Unknown, Ground.Unknown }),
            (2.0, new[] { Ground.Road, Ground.Unknown, Ground.Unknown }),
            (2.5, new[] { Ground.Pavement, Ground.Pavement, Ground.Road })
        };
        var reading = HatchSide.Read(rows);
        Assert.Equal(2.5, reading.Pavement);
        Assert.Null(reading.Road);
    }

    [Fact]
    public void RowsAreReadNearestFirstWhateverTheirOrder()
    {
        var road = (IReadOnlyList<Ground>)new[] { Ground.Road, Ground.Road, Ground.Road };
        var pavement = (IReadOnlyList<Ground>)new[] { Ground.Pavement, Ground.Pavement, Ground.Pavement };
        var reading = HatchSide.Read(new[] { (4.0, pavement), (2.0, road), (3.0, pavement) });
        Assert.Equal(3.0, reading.Pavement);
        Assert.Equal(2.0, reading.Road);
        Assert.True(reading.RoadFirst);
    }

    [Fact]
    public void ClassifyPutsRoadFirstAndParkingLanesNowhere()
    {
        Assert.Equal(Ground.Road, HatchSide.Classify(0.4, roadLayer: false, parkingLane: false, crosswalk: false, walkable: true));
        Assert.Equal(Ground.Road, HatchSide.Classify(HatchSide.LaneHalfWidthMetres, false, true, false, true));
        Assert.Equal(Ground.Road, HatchSide.Classify(null, roadLayer: true, parkingLane: false, crosswalk: false, walkable: true));
        Assert.Equal(Ground.Unknown, HatchSide.Classify(2.0, false, parkingLane: true, crosswalk: false, walkable: true));
        Assert.Equal(Ground.Unknown, HatchSide.Classify(null, false, false, crosswalk: true, walkable: true));
        Assert.Equal(Ground.Unknown, HatchSide.Classify(null, false, false, false, walkable: false));
        Assert.Equal(Ground.Road, HatchSide.Classify(1.7, false, false, false, true));
        Assert.Equal(Ground.Unknown, HatchSide.Classify(2.0, false, false, false, true));
        Assert.Equal(Ground.Unknown, HatchSide.Classify(HatchSide.RoadsideMetres, false, false, false, true));
        Assert.Equal(Ground.Pavement, HatchSide.Classify(3.5, false, false, false, true));
        Assert.Equal(Ground.Pavement, HatchSide.Classify(null, false, false, false, true));
    }

    public static IEnumerable<object[]> Streets()
    {
        foreach (var lane in new[] { 3.0, 3.5, 4.0, 4.5, 5.0 })
            foreach (var parking in new[] { 2.0, 2.2, 2.4, 2.6, 2.8, 3.0 })
                foreach (var seen in new[] { true, false })
                    yield return new object[] { lane, parking, seen };
    }

    // The strip between a parking lane and a wide driving lane's middle is walkable and in no parking lane the game
    // knows of; it read as pavement in the first 0.9.6 build, so a hatch to the road opened on wide streets.
    [Theory]
    [MemberData(nameof(Streets))]
    public void TheHatchToTheRoadIsRefusedOnEveryStreetWidth(double laneWidth, double parkingWidth, bool parkingSeen)
    {
        foreach (var kerb in new[] { -1.1, -1.2, -1.6, -2.0 })
            foreach (var slant in new[] { 0.0, 10.0, -20.0 })
            {
                var street = new Street { Kerb = kerb, LaneWidth = laneWidth, ParkingWidth = parkingWidth, ParkingSeen = parkingSeen, SlantDegrees = slant };
                Assert.True(street.Verdict() == HatchVerdict.WrongWay,
                    $"kerb {kerb}, slant {slant}: {street.Verdict()} (hatch side {street.Side(1)}, other side {street.Side(-1)})");
            }
    }

    [Theory]
    [MemberData(nameof(Streets))]
    public void KerbsideParkingOpensOnEveryStreetWidth(double laneWidth, double parkingWidth, bool parkingSeen)
    {
        // From flush against the kerb out to the far edge of the space: the truck's middle up to 1 m past the
        // parking strip's edge, its outer side then over the driving lane (WindowShape's reach of 3.4 m), or 0.6 m
        // past it at a 20° slant. Further out on a 3 m lane, the lane's middle runs under the truck's own side and
        // reads road on both sides: "no pavement".
        foreach (var kerb in new[] { 1.2, 1.6, 2.0, 2.4, 2.8, 3.0, 3.4 })
            foreach (var slant in new[] { 0.0, 10.0, -10.0, 20.0, -20.0 })
            {
                if (kerb > parkingWidth + (Math.Abs(slant) > 10 ? 0.6 : 1.0) + 1e-9) continue;
                var street = new Street { Kerb = kerb, LaneWidth = laneWidth, ParkingWidth = parkingWidth, ParkingSeen = parkingSeen, SlantDegrees = slant };
                Assert.True(street.Verdict() == HatchVerdict.Pavement,
                    $"kerb {kerb}, slant {slant}: {street.Verdict()} (hatch side {street.Side(1)}, other side {street.Side(-1)})");
            }
    }

    [Fact]
    public void BadInputIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => HatchSide.Read(null!));
        Assert.Throws<ArgumentNullException>(() => HatchSide.Decide(null!, new SideReading(null, null)));
        Assert.Throws<ArgumentNullException>(() => HatchSide.Decide(new SideReading(null, null), null!));
    }
}
