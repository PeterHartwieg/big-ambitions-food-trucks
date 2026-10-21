using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.Fleet
{
    // A box in its own frame: its centre and size (a box collider's center and size, or a mesh's local bounds).
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

    // A vehicle's footprint in its own frame (x across, y up, z along): the axis-aligned box round its parts' boxes, each
    // carried into the vehicle's frame by the matrix from the part's frame to the vehicle's (rotation and scale, then
    // translation; row-major 3 x 4). The matrix is the vehicle's world-to-local times the part's local-to-world, so the
    // result doesn't depend on where the vehicle stands or which way it faces; a world-aligned box read back into the
    // vehicle's frame would grow as the vehicle turns (a 2 x 6 m truck at 45 degrees reads 5.7 x 5.7 m).
    public static class Footprint
    {
        public static (double X, double Y, double Z) Transform(double[] m, (double X, double Y, double Z) p)
        {
            if (m == null || m.Length != 12) throw new ArgumentException("A part's matrix has 12 entries (3 rows of 4).", nameof(m));
            return (m[0] * p.X + m[1] * p.Y + m[2] * p.Z + m[3],
                    m[4] * p.X + m[5] * p.Y + m[6] * p.Z + m[7],
                    m[8] * p.X + m[9] * p.Y + m[10] * p.Z + m[11]);
        }

        // Min and max corners in the vehicle's frame; null without parts.
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

        // A vehicle's own box from its solid colliders: the enabled ones; when none is enabled (a pushed cart: the game's
        // HandTruck.EnterVehicle switches its collider off) the same colliders whatever their state; null when it has none
        // at all, and the caller falls back to its known hull (review round 1: a pushed cart measured as the 6 m default).
        public static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max)? Box(IEnumerable<(double[] Matrix, LocalBox Box)> enabled,
            IEnumerable<(double[] Matrix, LocalBox Box)> disabled) => Box(enabled) ?? Box(disabled);

        // The footprint made longer by `metres` at its nose and at its tail.
        public static FlatBox Lengthened(FlatBox box, double metres) =>
            new FlatBox(box.X, box.Z, box.Forward.X, box.Forward.Z, box.HalfWidth, box.HalfLength + metres);

        // Whether `a`, placed nose to tail beside `b`, keeps at least `gap` metres clear of it along its own length (the
        // footprints themselves, not shrunk). Exactly `gap` apart keeps it.
        public static bool KeepsGap(FlatBox a, FlatBox b, double gap) => !Overlap(Lengthened(a, gap), b, -1e-6);

        // Whether two footprints on the ground overlap (separating axes), each grown by `margin` metres on every side.
        // Height isn't looked at: the caller has already picked what stands at the vehicle's level.
        public static bool Overlap(FlatBox a, FlatBox b, double margin = 0)
        {
            foreach (var axis in new[] { a.Forward, a.Right, b.Forward, b.Right })
            {
                var gap = Math.Abs((b.X - a.X) * axis.X + (b.Z - a.Z) * axis.Z);
                if (gap > a.Reach(axis) + b.Reach(axis) + 2 * margin) return false;
            }
            return true;
        }

        // The same for two things that also have a height span (bottom and top, metres): they overlap only where both
        // their spans and their footprints do, so a car on another parking level doesn't count (review round 6).
        public static bool Overlap(FlatBox a, (double Bottom, double Top) aSpan, FlatBox b, (double Bottom, double Top) bSpan, double margin = 0)
        {
            if (aSpan.Bottom >= bSpan.Top || bSpan.Bottom >= aSpan.Top) return false;
            return Overlap(a, b, margin);
        }
    }

    // A footprint on the ground: its middle (x, z), the way it faces (a unit vector in x, z) and its half sizes.
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
        // Forward turned a quarter clockwise seen from above (Unity's right for a yaw).
        public (double X, double Z) Right => (Forward.Z, -Forward.X);
        public double HalfWidth { get; }
        public double HalfLength { get; }

        // Half the footprint's extent along a unit axis.
        public double Reach((double X, double Z) axis) =>
            HalfLength * Math.Abs(Forward.X * axis.X + Forward.Z * axis.Z) + HalfWidth * Math.Abs(Right.X * axis.X + Right.Z * axis.Z);
    }
}
