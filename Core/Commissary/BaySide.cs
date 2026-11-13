using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.Commissary
{
    public static class BaySide
    {
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

        private static double? Meets(double px, double pz, double rx, double rz, double qx, double qz, double sx, double sz)
        {
            var denom = Cross(rx, rz, sx, sz);
            var qpx = qx - px;
            var qpz = qz - pz;
            var scale = Math.Sqrt((rx * rx + rz * rz) * (sx * sx + sz * sz));
            if (Math.Abs(denom) <= Eps * scale)
            {
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
