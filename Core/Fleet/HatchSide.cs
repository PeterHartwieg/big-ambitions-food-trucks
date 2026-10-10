using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Fleet
{
    // What one probe point beside a truck stands on.
    public enum Ground
    {
        // Not walkable, in a parking lane, on a crosswalk or blocked: says nothing either way.
        Unknown,
        Pavement,
        Road
    }

    public enum HatchVerdict
    {
        // Pavement beside the hatch before any road: open.
        Pavement,
        // Road beside the hatch before any pavement, and pavement on the other side: turned the wrong way.
        WrongWay,
        // Road beside the hatch before any pavement, and no pavement on the other side either.
        NoPavement,
        // Neither pavement nor road found beside the hatch (parked cars, angled spaces, no traffic data): open,
        // since the rule only refuses a truck that clearly faces the road.
        Unclear
    }

    // The nearest pavement and road found on one side, in metres out from the truck's centre line.
    public sealed class SideReading
    {
        public SideReading(double? pavement, double? road)
        {
            Pavement = pavement;
            Road = road;
        }

        public double? Pavement { get; }
        public double? Road { get; }

        // Pavement comes before any road: people can stand there without crossing traffic.
        public bool Clear => Pavement != null && (Road == null || Pavement < Road);

        // Road comes before any pavement.
        public bool RoadFirst => Road != null && (Pavement == null || Road <= Pavement);

        public override string ToString() =>
            $"pavement {(Pavement is { } p ? $"{p:F1} m" : "none")}, road {(Road is { } r ? $"{r:F1} m" : "none")}";
    }

    // Whether a truck's serving hatch faces the pavement. The game side probes a grid of points on each side of
    // the truck: rows at Offsets metres out from its centre line, each row a few points along the hatch's
    // length, and classifies every point (Classify). A row counts as pavement or road when most of its points
    // agree, so one odd point (a lamp post's hole in the NavMesh, a lane line bending round a corner) changes
    // nothing. Only a truck whose hatch side meets road before pavement is refused; anything unclear opens.
    public static class HatchSide
    {
        // Rows from just outside the truck's side (half its width is 1.10 m) out to 5 m. A truck legally parked
        // in a lane at the kerb has the pavement within about 3 m of its centre line; 5 m leaves room for one
        // parked well away from the kerb or at a slant. A driving lane beside a parked truck has its centre
        // about 3 to 5 m out, so the road side reads road within the rows.
        public static readonly double[] Offsets = { 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 };

        // Points along the hatch, metres forward of the truck's middle: its two ends and middle, as the
        // hatch's opening runs z -2.00 to -0.20 in the model (art/README.md).
        public static readonly double[] Along = { -1.9, -1.1, -0.3 };

        // A point this close to a driving lane's centre line (the traffic system's waypoints) is road: half of a
        // 3.5 m lane, so an ordinary lane reads road across its whole width.
        public const double LaneHalfWidthMetres = 1.75;

        // A walkable point closer than this to a driving lane's centre, and not road by the rule above, says nothing
        // either way: it may be the outer part of a wide lane (4 to 5 m), a parking lane the game's parking colliders
        // miss, or waypoints running off the lane's middle, and the NavMesh covers all of those as it covers the
        // pavement. On the road side of a parked truck everything between the truck and the lane's centre is within
        // about 2.8 m of it (a 5 m lane beside a 3 m parking strip, the truck at the kerb's 1.2 m); legal kerbside
        // pavement is at least 3.5 m from the nearest lane's centre (a 3 m lane beside a 2 m strip).
        public const double RoadsideMetres = 3.25;

        // A point's ground, from what the game knows there. Road first: the driving lanes (or the game's road
        // layer, which is known only on bridges) are road even where the NavMesh lets people walk, and the
        // NavMesh covers the street as well as the pavement. A parking lane, a crosswalk or the strip beside a
        // driving lane is neither. Without lanes (no traffic data) every walkable point is pavement, as before 0.9.6.
        public static Ground Classify(double? laneMetres, bool roadLayer, bool parkingLane, bool crosswalk, bool walkable)
        {
            if (roadLayer || (laneMetres is { } lane && lane <= LaneHalfWidthMetres)) return Ground.Road;
            if (parkingLane || crosswalk || !walkable) return Ground.Unknown;
            if (laneMetres is { } near && near <= RoadsideMetres) return Ground.Unknown;
            return Ground.Pavement;
        }

        // One side's reading from its rows (offset, the ground of each point along). A row is pavement or road
        // when more than half its points are.
        public static SideReading Read(IEnumerable<(double Offset, IReadOnlyList<Ground> Points)> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            double? pavement = null, road = null;
            foreach (var (offset, points) in rows.OrderBy(r => r.Offset))
            {
                if (points == null || points.Count == 0) continue;
                if (pavement == null && points.Count(p => p == Ground.Pavement) * 2 > points.Count) pavement = offset;
                if (road == null && points.Count(p => p == Ground.Road) * 2 > points.Count) road = offset;
            }
            return new SideReading(pavement, road);
        }

        public static HatchVerdict Decide(SideReading hatch, SideReading opposite)
        {
            if (hatch == null) throw new ArgumentNullException(nameof(hatch));
            if (opposite == null) throw new ArgumentNullException(nameof(opposite));
            if (hatch.Clear) return HatchVerdict.Pavement;
            if (hatch.RoadFirst) return opposite.Clear ? HatchVerdict.WrongWay : HatchVerdict.NoPavement;
            return HatchVerdict.Unclear;
        }

        public static bool Opens(HatchVerdict verdict) => verdict == HatchVerdict.Pavement || verdict == HatchVerdict.Unclear;
    }
}
