using System;
using System.Collections.Generic;
using FoodTrucks.Core.Fleet;

namespace FoodTrucks.Core.Commissary
{
    // A commissary's bay is on the same side of the street as its garage door (Peter, 13 October 2026): a place counts
    // only when the straight walk from the entrance to it crosses no driving lane's centre line. The lanes are the
    // traffic system's waypoint links (the game side reads them as for the hatch check), which follow a curving street
    // and run on through junctions, so the test needs no idea of "the street": a bay round the corner on the door's own
    // block crosses no lane, one across either street crosses that street's lanes. Asking which side of the nearest lane
    // the door and the bay are on fails at a corner, where the door's nearest lane isn't the bay's.
    //
    // A truck legally parked at the door's kerb has its middle in the parking lane, about 2.5 to 3.5 m short of the
    // nearest driving lane's centre (HatchSide.RoadsideMetres), so the walk to it never reaches that line. A walk along
    // the outside of a bend cuts towards the road: with the door 3 m behind the kerb and the lane's centre 3 m beyond it,
    // a 30 m walk (the search's reach) only reaches the lane on a bend of under about 30 m radius, tighter than the
    // city's streets.
    public static class BaySide
    {
        // The point where the straight walk from (fromX, fromZ) to (toX, toZ) first meets a lane's centre line, nearest
        // the start, or null when it meets none. A walk that touches a line at a waypoint, or runs along one, meets it.
        // A lane segment with no length (a lone waypoint) can't be crossed and is skipped.
        public static (double X, double Z)? FirstCrossing(double fromX, double fromZ, double toX, double toZ,
            IEnumerable<((double X, double Z) A, (double X, double Z) B)> lanes)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            var rx = toX - fromX;
            var rz = toZ - fromZ;
            if (rx * rx + rz * rz < 1e-12) return null;
            double? best = null;
            foreach (var (a, b) in lanes)
            {
                var sx = b.X - a.X;
                var sz = b.Z - a.Z;
                if (sx * sx + sz * sz < 1e-12) continue;
                var t = Meets(fromX, fromZ, rx, rz, a.X, a.Z, sx, sz);
                if (t != null && (best == null || t < best)) best = t;
            }
            return best is { } at ? (fromX + rx * at, fromZ + rz * at) : ((double X, double Z)?)null;
        }

        public static bool SameSide(double fromX, double fromZ, double toX, double toZ,
            IEnumerable<((double X, double Z) A, (double X, double Z) B)> lanes) =>
            FirstCrossing(fromX, fromZ, toX, toZ, lanes) == null;

        private const double Eps = 1e-9;

        // Where along the walk p + t r (t in 0..1) the segment q + u s (u in 0..1) meets it, or null.
        private static double? Meets(double px, double pz, double rx, double rz, double qx, double qz, double sx, double sz)
        {
            var denom = Cross(rx, rz, sx, sz);
            var qpx = qx - px;
            var qpz = qz - pz;
            var scale = Math.Sqrt((rx * rx + rz * rz) * (sx * sx + sz * sz));
            if (Math.Abs(denom) <= Eps * scale)
            {
                // Parallel: they meet only when on one line and overlapping along it.
                if (Math.Abs(Cross(qpx, qpz, rx, rz)) > Eps * Math.Sqrt(rx * rx + rz * rz) * Math.Max(1, Math.Sqrt(qpx * qpx + qpz * qpz))) return null;
                var rr = rx * rx + rz * rz;
                var t0 = (qpx * rx + qpz * rz) / rr;
                var t1 = t0 + (sx * rx + sz * rz) / rr;
                var lo = Math.Max(0, Math.Min(t0, t1));
                var hi = Math.Min(1, Math.Max(t0, t1));
                return lo <= hi + Eps ? lo : (double?)null;
            }
            var t = Cross(qpx, qpz, sx, sz) / denom;
            var u = Cross(qpx, qpz, rx, rz) / denom;
            if (t < -Eps || t > 1 + Eps || u < -Eps || u > 1 + Eps) return null;
            return Math.Max(0, Math.Min(1, t));
        }

        private static double Cross(double ax, double az, double bx, double bz) => ax * bz - az * bx;
    }

    // A vehicle's footprint clear of the driving lanes: no part of it within half a lane's width (HatchSide's
    // LaneHalfWidthMetres) of a driving lane's centre line. A cart counts in its bay by its middle (CommissaryBays), and a
    // cart pushed up beside a truck already in the parking lane stands with its middle still within the bay's radius but
    // half in the road (playtest a14, 21 October 2026: Murray Hill's lane middle is 3.1 m from the driving lane's centre,
    // and a cart beside the truck at 1.8 m across read "in the bay"). The bay rule keeps counting it (Peter: that is the
    // player's parking); the test bridge uses this to park carts behind one another and to say when one sticks out.
    public static class LaneClearance
    {
        // The flat distance from the footprint to the nearest lane's centre line: 0 when a line crosses or touches it,
        // null without lanes (nothing known).
        public static double? Distance(FlatBox box, IEnumerable<((double X, double Z) A, (double X, double Z) B)> lanes)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            var corners = Corners(box);
            double? best = null;
            foreach (var (a, b) in lanes)
            {
                double d;
                if (Inside(box, a.X, a.Z) || Inside(box, b.X, b.Z)) d = 0;
                else
                {
                    d = double.MaxValue;
                    for (var i = 0; i < 4; i++)
                        d = Math.Min(d, SegmentDistance(corners[i], corners[(i + 1) % 4], a, b));
                }
                if (best == null || d < best) best = d;
            }
            return best;
        }

        // Clear when every part of the footprint is more than halfWidth from every lane's centre line. Null when no lane
        // was read: the caller decides what an unknown means.
        public static bool? Clear(FlatBox box, IEnumerable<((double X, double Z) A, (double X, double Z) B)> lanes,
            double halfWidth = HatchSide.LaneHalfWidthMetres) =>
            Distance(box, lanes) is { } d ? d > halfWidth : (bool?)null;

        private static (double X, double Z)[] Corners(FlatBox box)
        {
            var f = box.Forward;
            var r = box.Right;
            (double, double) At(double along, double across) =>
                (box.X + f.X * along * box.HalfLength + r.X * across * box.HalfWidth, box.Z + f.Z * along * box.HalfLength + r.Z * across * box.HalfWidth);
            return new[] { At(1, 1), At(1, -1), At(-1, -1), At(-1, 1) };
        }

        private static bool Inside(FlatBox box, double x, double z)
        {
            var dx = x - box.X;
            var dz = z - box.Z;
            const double eps = 1e-9;
            return Math.Abs(dx * box.Forward.X + dz * box.Forward.Z) <= box.HalfLength + eps &&
                   Math.Abs(dx * box.Right.X + dz * box.Right.Z) <= box.HalfWidth + eps;
        }

        // The shortest distance between two segments (0 when they cross).
        private static double SegmentDistance((double X, double Z) p, (double X, double Z) q, (double X, double Z) a, (double X, double Z) b)
        {
            if (BaySide.FirstCrossing(p.X, p.Z, q.X, q.Z, new[] { (a, b) }) != null) return 0;
            return Math.Min(Math.Min(PointDistance(p, a, b), PointDistance(q, a, b)), Math.Min(PointDistance(a, p, q), PointDistance(b, p, q)));
        }

        private static double PointDistance((double X, double Z) p, (double X, double Z) a, (double X, double Z) b)
        {
            var abx = b.X - a.X;
            var abz = b.Z - a.Z;
            var length = abx * abx + abz * abz;
            var t = length < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * abx + (p.Z - a.Z) * abz) / length));
            var dx = a.X + abx * t - p.X;
            var dz = a.Z + abz * t - p.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
