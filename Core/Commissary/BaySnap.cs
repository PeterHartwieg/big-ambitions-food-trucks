using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.Commissary
{
    // Where a commissary's bay point goes: the nearest place to the door (or to the point set in data) where a truck
    // stands legally parked, by the game's own rule. The game calls a vehicle legally parked when every wheel stands on
    // a parking lane that isn't a disabled bay (CarController.UpdateParkingZone), so a place counts when all four wheels
    // of a truck there, lined up with one of the lane's axes, are on legal parking. The game side (Commissary) supplies
    // the probes; this is the search.
    public static class BaySnap
    {
        public const double MaxMetres = 30;
        public const double StepMetres = 0.5;
        // The food truck's wheels, measured from its middle: about 3.8 m between the axles and 1.7 m between the
        // wheels of an axle, for a 5.8 m truck. A little generous, so a place found fits the truck with room.
        public const double HalfWheelbaseMetres = 1.9;
        public const double HalfTrackMetres = 0.85;

        // The nearest legal place within maxMetres of (x, z), searched on a StepMetres grid by distance, or null. legal
        // is the game's test at one point; axes gives the directions to try a truck along at a legal point (the parking
        // lane's own axes there).
        public static BaySnapResult? Nearest(double x, double z, Func<double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, double maxMetres = MaxMetres)
        {
            if (legal == null) throw new ArgumentNullException(nameof(legal));
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            foreach (var (dx, dz) in Grid(maxMetres))
            {
                var px = x + dx;
                var pz = z + dz;
                if (!legal(px, pz)) continue;
                foreach (var (ax, az) in axes(px, pz))
                {
                    var length = Math.Sqrt(ax * ax + az * az);
                    if (length < 1e-9) continue;
                    var along = (X: ax / length, Z: az / length);
                    if (Fits(px, pz, along, legal))
                        return new BaySnapResult(px, pz, along.X, along.Z, Math.Sqrt(dx * dx + dz * dz));
                }
            }
            return null;
        }

        // A truck with its middle at (x, z) and its length along `along` has all four wheels on legal parking.
        public static bool Fits(double x, double z, (double X, double Z) along, Func<double, double, bool> legal)
        {
            var across = (X: -along.Z, Z: along.X);
            foreach (var a in new[] { -HalfWheelbaseMetres, HalfWheelbaseMetres })
                foreach (var c in new[] { -HalfTrackMetres, HalfTrackMetres })
                    if (!legal(x + along.X * a + across.X * c, z + along.Z * a + across.Z * c)) return false;
            return true;
        }

        // The grid's offsets within maxMetres, nearest first (ties in a fixed order, so the search is repeatable).
        public static IReadOnlyList<(double X, double Z)> Grid(double maxMetres)
        {
            var n = (int)Math.Floor(maxMetres / StepMetres + 1e-9);
            var cells = new List<(double X, double Z, int I, int J)>();
            for (var i = -n; i <= n; i++)
                for (var j = -n; j <= n; j++)
                    if (i * i + j * j <= n * n) cells.Add((i * StepMetres, j * StepMetres, i, j));
            cells.Sort((a, b) =>
            {
                var c = (a.I * a.I + a.J * a.J).CompareTo(b.I * b.I + b.J * b.J);
                if (c != 0) return c;
                c = a.I.CompareTo(b.I);
                return c != 0 ? c : a.J.CompareTo(b.J);
            });
            var result = new List<(double X, double Z)>(cells.Count);
            foreach (var cell in cells) result.Add((cell.X, cell.Z));
            return result;
        }
    }

    public sealed class BaySnapResult
    {
        public BaySnapResult(double x, double z, double alongX, double alongZ, double distance)
        {
            X = x;
            Z = z;
            AlongX = alongX;
            AlongZ = alongZ;
            Distance = distance;
        }

        public double X { get; }
        public double Z { get; }
        // The way the parking lane runs at the bay (a truck parked there lines up with it).
        public double AlongX { get; }
        public double AlongZ { get; }
        // From the search's start.
        public double Distance { get; }
    }
}
