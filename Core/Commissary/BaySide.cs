using System;
using System.Collections.Generic;

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
}
