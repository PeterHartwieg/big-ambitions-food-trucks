using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;

namespace FoodTrucks.Core.Fleet
{
    public enum HeadingChoice
    {
        None,
        Along,
        Reverse
    }

    public sealed class StopSnapResult
    {
        public StopSnapResult(double x, double z, double alongX, double alongZ, double heading, double distance)
        {
            X = x;
            Z = z;
            AlongX = alongX;
            AlongZ = alongZ;
            Heading = heading;
            Distance = distance;
        }

        public double X { get; }
        public double Z { get; }
        public double AlongX { get; }
        public double AlongZ { get; }
        public double Heading { get; }
        public double Distance { get; }
    }

    public static class StopSnap
    {
        public const double MaxMetres = 40;
        public const double RejectedMetres = BaySnap.RejectedMetres;
        public static readonly double[] CartPastKerbMetres = { 0.75, 1.25, 1.75 };

        public static HeadingChoice ChooseHeading(HatchVerdict along, HatchVerdict reverse)
        {
            if (along == HatchVerdict.Pavement) return HeadingChoice.Along;
            if (reverse == HatchVerdict.Pavement) return HeadingChoice.Reverse;
            if (along == HatchVerdict.Unclear) return HeadingChoice.Along;
            if (reverse == HatchVerdict.Unclear) return HeadingChoice.Reverse;
            return HeadingChoice.None;
        }

        public static double HeadingDegrees(double x, double z)
        {
            if (Math.Abs(x) < 1e-12 && Math.Abs(z) < 1e-12) throw new ArgumentException("A direction needs a length.");
            var degrees = Math.Atan2(x, z) * 180 / Math.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        public static StopSnapResult? Nearest(double x, double z, Func<double, double, double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, WheelFootprint footprint,
            Func<double, double, double, double, double?> heading, double maxMetres = MaxMetres)
        {
            if (legal == null) throw new ArgumentNullException(nameof(legal));
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (heading == null) throw new ArgumentNullException(nameof(heading));
            var probes = footprint.WithClearance(BaySnap.ClearanceAlongMetres, BaySnap.ClearanceAcrossMetres);
            var rejected = new List<(double X, double Z)>();
            foreach (var (dx, dz) in BaySnap.Grid(maxMetres))
            {
                var px = x + dx;
                var pz = z + dz;
                if (rejected.Any(r => BaySnap.Distance(r.X, r.Z, px, pz) < RejectedMetres)) continue;
                foreach (var (ax, az) in axes(px, pz))
                {
                    var length = Math.Sqrt(ax * ax + az * az);
                    if (length < 1e-9) continue;
                    var along = (X: ax / length, Z: az / length);
                    if (!legal(px, pz, along.X, along.Z)) continue;
                    var place = BaySnap.Centred(px, pz, along, legal) ?? (px, pz);
                    if (BaySnap.Distance(x, z, place.X, place.Z) > maxMetres + 1e-9 || !BaySnap.Fits(place.X, place.Z, along, probes, legal))
                    {
                        if (place.X == px && place.Z == pz) continue;
                        place = (px, pz);
                        if (!BaySnap.Fits(px, pz, along, probes, legal)) continue;
                    }
                    var yaw = heading(place.X, place.Z, along.X, along.Z);
                    if (yaw == null)
                    {
                        rejected.Add((px, pz));
                        rejected.Add(place);
                        break;
                    }
                    return new StopSnapResult(place.X, place.Z, along.X, along.Z, yaw.Value, BaySnap.Distance(x, z, place.X, place.Z));
                }
            }
            return null;
        }

        public static int PavementSide(SideReading right, SideReading left)
        {
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right.Clear && left.Clear) return left.Pavement < right.Pavement ? -1 : 1;
            if (right.Clear) return 1;
            if (left.Clear) return -1;
            return 0;
        }

        public static IEnumerable<double> CartOffsets(double pavementMetres) => CartPastKerbMetres.Select(m => pavementMetres + m);
    }
}
