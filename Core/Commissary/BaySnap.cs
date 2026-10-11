using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Commissary
{
    // A vehicle's wheels, as offsets from its middle: along its length and across it (metres).
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

        // Each wheel moved outwards by the clearances (away from the middle on each axis), so a place where these
        // probes pass leaves the real wheels that much room on every side.
        public WheelFootprint WithClearance(double along, double across) =>
            new WheelFootprint(Wheels.Select(w => (w.Along + Math.Sign(w.Along) * along, w.Across + Math.Sign(w.Across) * across)));
    }

    // Where a commissary's bay point goes: the nearest place to the door (or to the point set in data) where a truck
    // stands legally parked by the game's own rule. The game calls a vehicle legally parked when a small box round each
    // wheel, turned with the vehicle, touches a parking lane that isn't a disabled bay (CarController.UpdateParkingZone).
    // So a place counts when the truck's wheels there, lined up with one of the parking lane's axes and with a margin
    // (Clearance), all pass that test. A legal place is first moved to the middle of the lane across it, so the truck
    // stands centred. The game side (Commissary) supplies the probes; this is the search.
    public static class BaySnap
    {
        // Bumped when the rule changes, so bays stored in saves are searched again.
        public const int RuleVersion = 2;
        public const double MaxMetres = 30;
        public const double StepMetres = 0.5;
        // Margins beyond the real wheel hubs (Sol and Astra, review round 4: 0.9.9's ±0.85 m was inside the truck's
        // real ±0.87 m, so a snapped place could fail the game's test).
        public const double ClearanceAlongMetres = 0.20;
        public const double ClearanceAcrossMetres = 0.10;
        // How far either way across a legal point the lane's edges are looked for, and in what steps.
        public const double CentreProbeMetres = 3.5;
        public const double CentreStepMetres = 0.05;
        // Candidates within this of one the caller rejected (reach) are skipped: one rejection covers its lane nearby.
        public const double RejectedMetres = 3.0;

        private static readonly Dictionary<(double, double), IReadOnlyList<(double X, double Z)>> grids = new Dictionary<(double, double), IReadOnlyList<(double X, double Z)>>();

        // The nearest legal place within maxMetres of (x, z), or null. legal(x, z, alongX, alongZ) is the game's wheel
        // test at one point for a vehicle lined up with (alongX, alongZ); axes(x, z) gives the parking lane's axes at a
        // point (none off parking, so it also skips most cells cheaply). reach, if given, can reject a place found
        // (behind a building from the door); the search then goes on.
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
                    if (!legal(px, pz, along.X, along.Z)) continue;
                    var place = Centred(px, pz, along, legal) ?? (px, pz);
                    if (!Fits(place.X, place.Z, along, probes, legal))
                    {
                        if (place.X == px && place.Z == pz) continue;
                        place = (px, pz);
                        if (!Fits(px, pz, along, probes, legal)) continue;
                    }
                    if (reach != null && !reach(place.X, place.Z))
                    {
                        rejected.Add((px, pz));
                        break;
                    }
                    return new BaySnapResult(place.X, place.Z, along.X, along.Z, Distance(x, z, place.X, place.Z));
                }
            }
            return null;
        }

        // The middle of the legal run across the lane through (x, z), or null when (x, z) isn't legal itself.
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

        // Every probe of the footprint, for a vehicle with its middle at (x, z) lined up with `along`, passes.
        public static bool Fits(double x, double z, (double X, double Z) along, WheelFootprint probes, Func<double, double, double, double, bool> legal)
        {
            var across = (X: -along.Z, Z: along.X);
            foreach (var (a, c) in probes.Wheels)
                if (!legal(x + along.X * a + across.X * c, z + along.Z * a + across.Z * c, along.X, along.Z)) return false;
            return true;
        }

        // The grid's offsets within maxMetres, nearest first (ties in a fixed order, so the search is repeatable).
        // Built once per size.
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

    // The bays a save keeps, and what "in the bay" means (the coordinator's decision, review round 4): a vehicle is in a
    // commissary's bay when the game's own wheel test calls it legally parked and its middle is within the bay radius of
    // one of the commissary's resolved bay points. The paint, Ctrl+F3, the panel's Commissary button, the visit unlock
    // and the 04:00 check all use this.
    public static class CommissaryBays
    {
        // Origins closer than this to the stored ones are the same search.
        public const double SameOriginMetres = 0.05;

        // The stored bays for a commissary, if they were searched from these origins under the current rule.
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

        public static CommissaryBaySet Store(CommissaryState state, string siteId, IReadOnlyList<(double X, double Z)> origins, IEnumerable<BaySnapResult> found)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(siteId)) throw new ArgumentException("A bay needs its commissary.", nameof(siteId));
            var set = new CommissaryBaySet
            {
                Rule = BaySnap.RuleVersion,
                Origins = origins.Select(o => new CommissaryPoint { X = o.X, Z = o.Z }).ToList(),
                Points = found.Select(b => new CommissaryBayPoint { X = b.X, Z = b.Z, AlongX = b.AlongX, AlongZ = b.AlongZ }).ToList()
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
                                       Math.Abs(Math.Sqrt(p.AlongX * p.AlongX + p.AlongZ * p.AlongZ) - 1) < 0.01);
        }

        // In the bay: legally parked by the game's wheel test, and the middle within the radius of a bay point.
        public static bool InBay(IEnumerable<CommissaryBayPoint>? points, double x, double z, double radius, bool legallyParked) =>
            legallyParked && WithinRadius(points, x, z, radius);

        public static bool WithinRadius(IEnumerable<CommissaryBayPoint>? points, double x, double z, double radius) =>
            points != null && points.Any(p => BaySnap.Distance(p.X, p.Z, x, z) <= radius);

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
