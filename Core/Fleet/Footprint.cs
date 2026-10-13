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
    }
}
