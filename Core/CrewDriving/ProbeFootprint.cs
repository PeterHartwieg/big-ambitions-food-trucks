using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.CrewDriving
{
    /// <summary>A bounded convex projection used to certify that internal wheel shapes stay behind the visible body.</summary>
    public sealed class ProbeFootprint
    {
        private readonly ProbePoint[] hull;
        private ProbeFootprint(ProbePoint[] hull) { this.hull = hull; }

        public static ProbeFootprint Create(IReadOnlyList<ProbePoint> vertices)
        {
            if (vertices == null || vertices.Count < 3 || vertices.Count > 4096)
                throw new ArgumentException("Body projection needs 3..4096 finite vertices.");
            var points = new List<ProbePoint>(vertices.Count);
            for (var i = 0; i < vertices.Count; i++)
            {
                if (!Finite(vertices[i].X) || !Finite(vertices[i].Z)) throw new ArgumentException("Non-finite body vertex.");
                points.Add(vertices[i]);
            }
            points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            var boundary = new List<ProbePoint>(points.Count * 2);
            foreach (var p in points)
            {
                while (boundary.Count >= 2 && Cross(boundary[boundary.Count - 2], boundary[boundary.Count - 1], p) <= 0)
                    boundary.RemoveAt(boundary.Count - 1);
                boundary.Add(p);
            }
            var lower = boundary.Count;
            for (var i = points.Count - 2; i >= 0; i--)
            {
                var p = points[i];
                while (boundary.Count > lower && Cross(boundary[boundary.Count - 2], boundary[boundary.Count - 1], p) <= 0)
                    boundary.RemoveAt(boundary.Count - 1);
                boundary.Add(p);
            }
            boundary.RemoveAt(boundary.Count - 1);
            if (boundary.Count < 3 || boundary.Count > 256) throw new ArgumentException("Degenerate or unbounded body hull.");
            var area = 0d;
            for (var i = 0; i < boundary.Count; i++)
            {
                var a = boundary[i]; var b = boundary[(i + 1) % boundary.Count];
                area += a.X * b.Z - a.Z * b.X;
            }
            if (!Finite(area) || area < .01) throw new ArgumentException("Degenerate body hull.");
            return new ProbeFootprint(boundary.ToArray());
        }

        /// <summary>Certifies every rotation of an axis-aligned local box in [-yaw, yaw], with an inward margin.</summary>
        public bool ContainsYawSweep(ProbePoint pivot, ProbePoint min, ProbePoint max, double yawRadians, double inset)
        {
            if (!Finite(pivot.X) || !Finite(pivot.Z) || !Finite(min.X) || !Finite(min.Z) ||
                !Finite(max.X) || !Finite(max.Z) || min.X > max.X || min.Z > max.Z ||
                !Finite(yawRadians) || yawRadians < 0 || yawRadians > Math.PI || !Finite(inset) || inset < 0) return false;
            for (var i = 0; i < hull.Length; i++)
            {
                var a = hull[i]; var b = hull[(i + 1) % hull.Length];
                // Outward normal of a counterclockwise hull edge.
                var nx = b.Z - a.Z; var nz = a.X - b.X;
                var limit = nx * (a.X - pivot.X) + nz * (a.Z - pivot.Z) - inset * Math.Sqrt(nx * nx + nz * nz);
                for (var corner = 0; corner < 4; corner++)
                {
                    var x = (corner & 1) == 0 ? min.X : max.X;
                    var z = (corner & 2) == 0 ? min.Z : max.Z;
                    // Unity yaw: (x cos + z sin, -x sin + z cos).
                    if (Maximum(nx * x + nz * z, nx * z - nz * x, yawRadians) > limit) return false;
                }
            }
            return true;
        }

        private static double Maximum(double cosine, double sine, double angle)
        {
            var result = cosine * Math.Cos(angle) + Math.Abs(sine * Math.Sin(angle));
            var peak = Math.Atan2(sine, cosine);
            if (Math.Abs(peak) <= angle) result = Math.Max(result, Math.Sqrt(cosine * cosine + sine * sine));
            return result;
        }
        private static double Cross(ProbePoint a, ProbePoint b, ProbePoint c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
}
