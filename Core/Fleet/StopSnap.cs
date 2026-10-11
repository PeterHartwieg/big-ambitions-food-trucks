using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;

namespace FoodTrucks.Core.Fleet
{
    // Which way round a truck stands on a kerbside place.
    public enum HeadingChoice
    {
        // Neither way round has the hatch to the pavement: the place is no good for a stop.
        None,
        // Facing the way the parking lane's axis points.
        Along,
        // Turned round.
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
        // The parking lane's axis at the place.
        public double AlongX { get; }
        public double AlongZ { get; }
        // Degrees, as Unity's yaw: the way the vehicle faces at the stop.
        public double Heading { get; }
        // From the search's start (the building's entrance).
        public double Distance { get; }
    }

    // Where a stop picked on the city map goes (Peter, 11 October 2026): the player picks a building and the stop
    // snaps to the nearest kerbside place within MaxMetres of its entrance where a truck parks legally by the game's
    // wheel test (BaySnap's grid, centring and footprint) and can stand with its hatch to the pavement (HatchSide).
    // A cart goes on the pavement just past the kerb beside such a place. The game side probes; this decides.
    public static class StopSnap
    {
        public const double MaxMetres = 40;
        // A place the caller turned down rules out its neighbours this close, as BaySnap's reach does.
        public const double RejectedMetres = BaySnap.RejectedMetres;
        // A cart stands this far past the first pavement row the hatch probe found, and tries a little further in.
        public static readonly double[] CartPastKerbMetres = { 0.75, 1.25, 1.75 };

        // The truck's hatch is on its right. Pavement by the hatch is best, the lane's own way round first; a place
        // that reads unclear either way (no traffic data, parked cars) opens anyway, as HatchSide.Opens has it, so it
        // is the next best; road by the hatch both ways round rules the place out.
        public static HeadingChoice ChooseHeading(HatchVerdict along, HatchVerdict reverse)
        {
            if (along == HatchVerdict.Pavement) return HeadingChoice.Along;
            if (reverse == HatchVerdict.Pavement) return HeadingChoice.Reverse;
            if (along == HatchVerdict.Unclear) return HeadingChoice.Along;
            if (reverse == HatchVerdict.Unclear) return HeadingChoice.Reverse;
            return HeadingChoice.None;
        }

        // Unity's yaw in degrees (0 to 360) for a vehicle facing (x, z) on the ground: forward is (sin h, cos h).
        public static double HeadingDegrees(double x, double z)
        {
            if (Math.Abs(x) < 1e-12 && Math.Abs(z) < 1e-12) throw new ArgumentException("A direction needs a length.");
            var degrees = Math.Atan2(x, z) * 180 / Math.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        // The nearest place within maxMetres of (x, z) where the vehicle's footprint passes the game's wheel test,
        // lined up with the parking lane, and `heading` accepts it. legal and axes are BaySnap's; heading(x, z,
        // alongX, alongZ) gives the yaw to stand there at, or null to turn the place down (the hatch would face the
        // road either way round, say), which rules out its neighbours within RejectedMetres too.
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

        // The side of a kerbside place the pavement is on, from the hatch probe's two readings: 1 for the truck's
        // right (the hatch side), -1 for its left, 0 when neither side reads pavement before road. With pavement on
        // both sides (a median strip, a plaza), the nearer one.
        public static int PavementSide(SideReading right, SideReading left)
        {
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right.Clear && left.Clear) return left.Pavement < right.Pavement ? -1 : 1;
            if (right.Clear) return 1;
            if (left.Clear) return -1;
            return 0;
        }

        // How far out from the place's middle, across to the pavement side, a cart may stand: just past the first
        // pavement row the probe found, then a little further in, so it stands on the pavement by the kerb.
        public static IEnumerable<double> CartOffsets(double pavementMetres) => CartPastKerbMetres.Select(m => pavementMetres + m);
    }
}
