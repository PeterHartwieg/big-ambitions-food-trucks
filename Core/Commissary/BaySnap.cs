using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Commissary
{
    public sealed class WheelFootprint
    {
        public WheelFootprint(IEnumerable<(double Along, double Across)> wheels)
        {
            if (wheels == null) throw new ArgumentNullException(nameof(wheels));
            Wheels = wheels.ToList();
            if (Wheels.Count == 0) throw new ArgumentException("A footprint needs at least one wheel.", nameof(wheels));
        }

        public IReadOnlyList<(double Along, double Across)> Wheels { get; }

        // The food truck's wheel hubs (unity/BuildEditor/TruckAssets.cs, Wheels): 1.80 m either way along, 0.87 m across.
        // The adapter reads them from the truck prefab when it can; these are the fallback.
        public static WheelFootprint Truck { get; } = new WheelFootprint(new[] { (1.80, 0.87), (1.80, -0.87), (-1.80, 0.87), (-1.80, -0.87) });

        public IEnumerable<(double X, double Z)> Place(double x, double z, double alongX, double alongZ)
        {
            var length = Math.Sqrt(alongX * alongX + alongZ * alongZ);
            if (length < 1e-9) throw new ArgumentException("A vehicle's direction needs a length.");
            var f = (X: alongX / length, Z: alongZ / length);
            var right = (X: f.Z, Z: -f.X);
            return Wheels.Select(w => (x + f.X * w.Along + right.X * w.Across, z + f.Z * w.Along + right.Z * w.Across)).ToList();
        }

        public WheelFootprint WithClearance(double along, double across) =>
            new WheelFootprint(Wheels.Select(w => (w.Along + Math.Sign(w.Along) * along, w.Across + Math.Sign(w.Across) * across)));
    }

    public static class BaySnap
    {
        public const int RuleVersion = 4;
        public const double MaxMetres = 30;
        public const double StepMetres = 0.5;
        public const double ClearanceAlongMetres = 0.20;
        public const double ClearanceAcrossMetres = 0.10;
        public const double CentreProbeMetres = 3.5;
        public const double CentreStepMetres = 0.05;
        public const double RejectedMetres = 3.0;

        private static readonly Dictionary<(double, double), IReadOnlyList<(double X, double Z)>> grids = new Dictionary<(double, double), IReadOnlyList<(double X, double Z)>>();

        public static BaySnapResult? Nearest(double x, double z, Func<double, double, double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, WheelFootprint footprint,
            double maxMetres = MaxMetres, Func<double, double, bool>? reach = null)
        {
            if (legal == null) throw new ArgumentNullException(nameof(legal));
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            var probes = footprint.WithClearance(ClearanceAlongMetres, ClearanceAcrossMetres);
            var rejected = new List<(double X, double Z)>();
            foreach (var (dx, dz) in Grid(maxMetres))
            {
                var px = x + dx;
                var pz = z + dz;
                if (rejected.Any(r => Distance(r.X, r.Z, px, pz) < RejectedMetres)) continue;
                foreach (var (ax, az) in axes(px, pz))
                {
                    var length = Math.Sqrt(ax * ax + az * az);
                    if (length < 1e-9) continue;
                    var along = (X: ax / length, Z: az / length);
                    var place = PlaceAt(x, z, px, pz, along, probes, legal, maxMetres);
                    if (place == null) continue;
                    if (reach != null && !reach(place.Value.X, place.Value.Z))
                    {
                        rejected.Add((px, pz));
                        break;
                    }
                    return new BaySnapResult(place.Value.X, place.Value.Z, along.X, along.Z, Distance(x, z, place.Value.X, place.Value.Z));
                }
            }
            return null;
        }

        private static (double X, double Z)? PlaceAt(double x, double z, double px, double pz, (double X, double Z) along, WheelFootprint probes,
            Func<double, double, double, double, bool> legal, double maxMetres)
        {
            if (!legal(px, pz, along.X, along.Z)) return null;
            var place = Centred(px, pz, along, legal) ?? (px, pz);
            if (Distance(x, z, place.X, place.Z) > maxMetres + 1e-9 || !Fits(place.X, place.Z, along, probes, legal))
            {
                if (place.X == px && place.Z == pz) return null;
                place = (px, pz);
                if (!Fits(px, pz, along, probes, legal)) return null;
            }
            return place;
        }

        public static IReadOnlyList<BaySnapResult> Candidates(double x, double z, Func<double, double, double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, WheelFootprint footprint, double maxMetres,
            double spacingMetres = CandidateSpacingMetres, int limit = CandidateLimit)
        {
            if (legal == null) throw new ArgumentNullException(nameof(legal));
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            var probes = footprint.WithClearance(ClearanceAlongMetres, ClearanceAcrossMetres);
            var found = new List<BaySnapResult>();
            foreach (var (dx, dz) in Grid(maxMetres))
            {
                if (found.Count >= limit) break;
                var px = x + dx;
                var pz = z + dz;
                if (found.Any(f => Distance(f.X, f.Z, px, pz) < spacingMetres)) continue;
                foreach (var (ax, az) in axes(px, pz))
                {
                    var length = Math.Sqrt(ax * ax + az * az);
                    if (length < 1e-9) continue;
                    var along = (X: ax / length, Z: az / length);
                    var place = PlaceAt(x, z, px, pz, along, probes, legal, maxMetres);
                    if (place == null) continue;
                    if (!found.Any(f => Distance(f.X, f.Z, place.Value.X, place.Value.Z) < spacingMetres))
                        found.Add(new BaySnapResult(place.Value.X, place.Value.Z, along.X, along.Z, Distance(x, z, place.Value.X, place.Value.Z)));
                    break;
                }
            }
            return found.OrderBy(f => f.Distance).ToList();
        }

        public static BayCandidates CandidatesWithPick(double x, double z, Func<double, double, double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, WheelFootprint footprint, double radius,
            Func<double, double, bool>? reach, bool automatic = true, double? heading = null)
        {
            var listAxes = heading == null ? axes : FixedAxis(axes, HeadingAxis(heading.Value));
            var list = Candidates(x, z, legal, listAxes, footprint, radius).ToList();
            BaySnapResult? pick = automatic ? Measured(x, z, heading, legal, axes, footprint, MaxMetres, reach) : null;
            if (pick != null)
            {
                list.RemoveAll(c => Distance(c.X, c.Z, pick.X, pick.Z) < SamePickMetres);
                list.Add(pick);
            }
            return new BayCandidates(list.OrderBy(c => c.Distance).ToList(), pick);
        }

        public const double SamePickMetres = 0.05;

        public static (double X, double Z) HeadingAxis(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return (Math.Sin(radians), Math.Cos(radians));
        }

        public static Func<double, double, IEnumerable<(double X, double Z)>> FixedAxis(Func<double, double, IEnumerable<(double X, double Z)>> axes, (double X, double Z) along)
        {
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            var only = new[] { along };
            return (x, z) => axes(x, z).Any() ? only : Array.Empty<(double X, double Z)>();
        }

        public static BaySnapResult? Measured(double x, double z, double? heading, Func<double, double, double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, WheelFootprint footprint, double maxMetres = MaxMetres,
            Func<double, double, bool>? reach = null)
        {
            if (legal == null) throw new ArgumentNullException(nameof(legal));
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (heading == null) return Nearest(x, z, legal, axes, footprint, maxMetres, reach);
            var along = HeadingAxis(heading.Value);
            if (Fits(x, z, along, footprint.WithClearance(ClearanceAlongMetres, ClearanceAcrossMetres), legal) && (reach == null || reach(x, z)))
                return new BaySnapResult(x, z, along.X, along.Z, 0);
            return Nearest(x, z, legal, FixedAxis(axes, along), footprint, maxMetres, reach);
        }

        public const double CandidateSpacingMetres = 4;
        public const int CandidateLimit = 60;

        public static (double X, double Z)? Centred(double x, double z, (double X, double Z) along, Func<double, double, double, double, bool> legal)
        {
            var across = (X: -along.Z, Z: along.X);
            bool At(double s) => legal(x + across.X * s, z + across.Z * s, along.X, along.Z);
            if (!At(0)) return null;
            var steps = (int)Math.Round(CentreProbeMetres / CentreStepMetres);
            var lo = 0;
            while (lo > -steps && At((lo - 1) * CentreStepMetres)) lo--;
            var hi = 0;
            while (hi < steps && At((hi + 1) * CentreStepMetres)) hi++;
            var mid = (lo + hi) * CentreStepMetres / 2;
            return (x + across.X * mid, z + across.Z * mid);
        }

        public static bool Fits(double x, double z, (double X, double Z) along, WheelFootprint probes, Func<double, double, double, double, bool> legal)
        {
            var length = Math.Sqrt(along.X * along.X + along.Z * along.Z);
            if (length < 1e-9) return false;
            var f = (X: along.X / length, Z: along.Z / length);
            var wheels = probes.Wheels;
            for (var i = 0; i < wheels.Count; i++)
            {
                var (a, c) = wheels[i];
                if (!legal(x + f.X * a + f.Z * c, z + f.Z * a - f.X * c, along.X, along.Z)) return false;
            }
            return true;
        }

        public static IReadOnlyList<(double X, double Z)> Grid(double maxMetres)
        {
            lock (grids)
            {
                if (grids.TryGetValue((maxMetres, StepMetres), out var cached)) return cached;
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
                var result = cells.Select(c => (c.X, c.Z)).ToList().AsReadOnly();
                grids[(maxMetres, StepMetres)] = result;
                return result;
            }
        }

        public static double Distance(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
    }

    public sealed class BayCandidates
    {
        public BayCandidates(IReadOnlyList<BaySnapResult> places, BaySnapResult? automatic)
        {
            Places = places;
            Automatic = automatic;
        }

        public IReadOnlyList<BaySnapResult> Places { get; }
        public BaySnapResult? Automatic { get; }
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
        public double AlongX { get; }
        public double AlongZ { get; }
        public double Distance { get; }
    }

    public enum WalkVerdict
    {
        Short,
        TooLong,
        NoRoute,
        Unknown,
    }

    public static class WalkRule
    {
        public const double Factor = 1.6;
        public const double ExtraMetres = 15;

        public static WalkVerdict Classify(bool fromOnNavMesh, bool toOnNavMesh, bool complete, double pathMetres, double straightMetres)
        {
            if (!fromOnNavMesh || !toOnNavMesh) return WalkVerdict.Unknown;
            if (!complete) return WalkVerdict.NoRoute;
            return pathMetres <= straightMetres * Factor + ExtraMetres ? WalkVerdict.Short : WalkVerdict.TooLong;
        }

        public static bool Accepts(WalkVerdict verdict) => verdict == WalkVerdict.Short || verdict == WalkVerdict.Unknown;
    }

    public static class PoseMath
    {
        public static (double X, double Y, double Z) Rotate((double X, double Y, double Z, double W) q, (double X, double Y, double Z) v)
        {
            var (ux, uy, uz, w) = q;
            var cx = uy * v.Z - uz * v.Y;
            var cy = uz * v.X - ux * v.Z;
            var cz = ux * v.Y - uy * v.X;
            var ccx = uy * cz - uz * cy;
            var ccy = uz * cx - ux * cz;
            var ccz = ux * cy - uy * cx;
            return (v.X + 2 * (w * cx + ccx), v.Y + 2 * (w * cy + ccy), v.Z + 2 * (w * cz + ccz));
        }

        public static (double X, double Y, double Z) WheelProbe((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation,
            (double X, double Y, double Z) hub)
        {
            var h = Rotate(rotation, hub);
            var up = Rotate(rotation, (0, 1, 0));
            return (position.X + h.X - 0.1 * up.X, position.Y + h.Y - 0.1 * up.Y, position.Z + h.Z - 0.1 * up.Z);
        }
    }

    public static class ResolveOrder
    {
        public static int Next(int count, int start, Func<int, bool> pendingAndDue)
        {
            if (pendingAndDue == null) throw new ArgumentNullException(nameof(pendingAndDue));
            if (count <= 0) return -1;
            start = ((start % count) + count) % count;
            for (var k = 0; k < count; k++)
            {
                var i = (start + k) % count;
                if (pendingAndDue(i)) return i;
            }
            return -1;
        }
    }

    public static class CommissaryBays
    {
        public const double SameOriginMetres = 0.05;

        // Whether a commissary's bay is searched now: never searched for these origins and rule, or searched and found
        // nothing and not yet searched again since this load. A failed search is stored (so the 04:00 check and the
        // paint decide from it) but repeated once on every load, as the street round a door can have loaded late or
        // the rule have changed since (live session 2, 13 October 2026: three commissaries kept "no bay" for good).
        public static bool SearchDue(CommissaryBaySet? current, bool searchedThisLoad) =>
            current == null || ((current.Points == null || current.Points.Count == 0) && !searchedThisLoad);

        public static CommissaryBaySet? Current(CommissaryState state, string siteId, IReadOnlyList<(double X, double Z)> origins)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (origins == null) throw new ArgumentNullException(nameof(origins));
            if (state.Bays == null || !state.Bays.TryGetValue(siteId, out var set) || set == null) return null;
            if (set.Rule != BaySnap.RuleVersion || set.Origins.Count != origins.Count) return null;
            for (var i = 0; i < origins.Count; i++)
                if (BaySnap.Distance(set.Origins[i].X, set.Origins[i].Z, origins[i].X, origins[i].Z) > SameOriginMetres) return null;
            return set;
        }

        public static CommissaryBaySet Store(CommissaryState state, string siteId, IReadOnlyList<(double X, double Z)> origins, IEnumerable<BaySnapResult> found) =>
            Store(state, siteId, origins, found.Select(b => (b, ((double X, double Z)?)null)));

        public static CommissaryBaySet Store(CommissaryState state, string siteId, IReadOnlyList<(double X, double Z)> origins,
            IEnumerable<(BaySnapResult Bay, (double X, double Z)? Door)> found)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(siteId)) throw new ArgumentException("A bay needs its commissary.", nameof(siteId));
            var set = new CommissaryBaySet
            {
                Rule = BaySnap.RuleVersion,
                Origins = origins.Select(o => new CommissaryPoint { X = o.X, Z = o.Z }).ToList(),
                Points = found.Select(f => new CommissaryBayPoint
                {
                    X = f.Bay.X, Z = f.Bay.Z, AlongX = f.Bay.AlongX, AlongZ = f.Bay.AlongZ, DoorX = f.Door?.X, DoorZ = f.Door?.Z
                }).ToList()
            };
            state.Bays ??= new Dictionary<string, CommissaryBaySet>(StringComparer.Ordinal);
            state.Bays[siteId] = set;
            return set;
        }

        public static bool WellFormed(CommissaryBaySet? set)
        {
            if (set == null || set.Origins == null || set.Points == null || set.Origins.Count == 0) return false;
            if (set.Origins.Any(o => o == null || !Finite(o.X) || !Finite(o.Z))) return false;
            return set.Points.All(p => p != null && Finite(p.X) && Finite(p.Z) && Finite(p.AlongX) && Finite(p.AlongZ) &&
                                       (p.DoorX == null) == (p.DoorZ == null) && (p.DoorX == null || (Finite(p.DoorX.Value) && Finite(p.DoorZ!.Value))) &&
                                       Math.Abs(Math.Sqrt(p.AlongX * p.AlongX + p.AlongZ * p.AlongZ) - 1) < 0.01);
        }

        public static bool RequiresLegalParking(string? kind) => kind != "cart";

        public static bool InBay(IEnumerable<CommissaryBayPoint>? points, double x, double z, double radius, bool legallyParked) =>
            legallyParked && WithinRadius(points, x, z, radius);

        public static bool? OnDoorSide(IEnumerable<CommissaryBayPoint>? points, double x, double z, double radius,
            Func<CommissaryBayPoint, IReadOnlyList<((double X, double Z) A, (double X, double Z) B)>?> lanes)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            if (points == null) return false;
            var unknown = false;
            foreach (var p in points)
            {
                if (p == null || BaySnap.Distance(p.X, p.Z, x, z) > radius) continue;
                if (p.DoorX is not { } doorX || p.DoorZ is not { } doorZ)
                {
                    unknown = true;
                    continue;
                }
                var read = lanes(p);
                if (read == null || read.Count == 0)
                {
                    unknown = true;
                    continue;
                }
                if (BaySide.SameSide(doorX, doorZ, x, z, read)) return true;
            }
            return unknown ? (bool?)null : false;
        }

        public static bool WithinRadius(IEnumerable<CommissaryBayPoint>? points, double x, double z, double radius) =>
            points != null && points.Any(p => BaySnap.Distance(p.X, p.Z, x, z) <= radius);

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
