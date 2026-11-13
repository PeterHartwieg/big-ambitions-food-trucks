using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.Fleet
{
    public readonly struct LocalBox
    {
        public LocalBox((double X, double Y, double Z) centre, (double X, double Y, double Z) size)
        {
            Centre = centre;
            Size = size;
        }

        public (double X, double Y, double Z) Centre { get; }
        public (double X, double Y, double Z) Size { get; }
    }

    public static class Footprint
    {
        public static (double X, double Y, double Z) Transform(double[] m, (double X, double Y, double Z) p)
        {
            if (m == null || m.Length != 12) throw new ArgumentException("A part's matrix has 12 entries (3 rows of 4).", nameof(m));
            return (m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3],
                    m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7],
                    m[8] * p.X + m[9] * p.Y + m[10] * p.Z + m[11]);
        }

        public static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max)? Box(IEnumerable<(double[] Matrix, LocalBox Box)> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            (double X, double Y, double Z)? min = null, max = null;
            foreach (var (matrix, box) in parts)
            {
                for (var i = 0; i < 8; i++)
                {
                    var corner = (box.Centre.X + ((i & 1) == 0 ? -0.5 : 0.5) * box.Size.X,
                                  box.Centre.Y + ((i & 2) == 0 ? -0.5 : 0.5) * box.Size.Y,
                                  box.Centre.Z + ((i & 4) == 0 ? -0.5 : 0.5) * box.Size.Z);
                    var p = Transform(matrix, corner);
                    min = min == null ? p : (Math.Min(min.Value.X, p.X), Math.Min(min.Value.Y, p.Y), Math.Min(min.Value.Z, p.Z));
                    max = max == null ? p : (Math.Max(max.Value.X, p.X), Math.Max(max.Value.Y, p.Y), Math.Max(max.Value.Z, p.Z));
                }
            }
            if (min == null || max == null) return null;
            return (min.Value, max.Value);
        }

        public static bool Overlap(FlatBox a, FlatBox b, double margin = 0)
        {
            foreach (var axis in new[] { a.Forward, a.Right, b.Forward, b.Right })
            {
                var gap = Math.Abs((b.X - a.X) * axis.X + (b.Z - a.Z) * axis.Z);
                if (gap > a.Reach(axis) + b.Reach(axis) + 2 * margin) return false;
            }
            return true;
        }

        public static bool Overlap(FlatBox a, (double Bottom, double Top) aSpan, FlatBox b, (double Bottom, double Top) bSpan, double margin = 0)
        {
            if (aSpan.Bottom >= bSpan.Top || bSpan.Bottom >= aSpan.Top) return false;
            return Overlap(a, b, margin);
        }
    }

    public readonly struct FlatBox
    {
        public FlatBox(double x, double z, double forwardX, double forwardZ, double halfWidth, double halfLength)
        {
            var n = Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
            if (n < 1e-9) throw new ArgumentException("A footprint needs a direction.", nameof(forwardX));
            X = x;
            Z = z;
            Forward = (forwardX / n, forwardZ / n);
            HalfWidth = Math.Abs(halfWidth);
            HalfLength = Math.Abs(halfLength);
        }

        public double X { get; }
        public double Z { get; }
        public (double X, double Z) Forward { get; }
        public (double X, double Z) Right => (Forward.Z, -Forward.X);
        public double HalfWidth { get; }
        public double HalfLength { get; }

        public double Reach((double X, double Z) axis) =>
            HalfLength * Math.Abs(Forward.X * axis.X + Forward.Z * axis.Z) + HalfWidth * Math.Abs(Right.X * axis.X + Right.Z * axis.Z);
    }
}
