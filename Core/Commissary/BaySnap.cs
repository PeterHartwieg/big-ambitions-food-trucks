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

        // Where each wheel stands (flat) for a vehicle with its middle at (x, z) facing (alongX, alongZ): Along is the
        // vehicle's forward (local +z), Across its right (local +x; Unity's right of a forward f is (f.z, -f.x)). The same
        // as a live vehicle's wheel transforms at that pose, so a saved pose is tested exactly as a live truck is.
        public IEnumerable<(double X, double Z)> Place(double x, double z, double alongX, double alongZ)
        {
            var length = Math.Sqrt(alongX * alongX + alongZ * alongZ);
            if (length < 1e-9) throw new ArgumentException("A vehicle's direction needs a length.");
            var f = (X: alongX / length, Z: alongZ / length);
            var right = (X: f.Z, Z: -f.X);
            return Wheels.Select(w => (x + f.X * w.Along + right.X * w.Across, z + f.Z * w.Along + right.Z * w.Across)).ToList();
        }

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
        public const int RuleVersion = 4;
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

        // The place a grid cell gives for a vehicle lined up with `along`, or null: the cell must be legal, and the
        // place is the legal run's middle across the lane when that fits within maxMetres, else the cell itself.
        private static (double X, double Z)? PlaceAt(double x, double z, double px, double pz, (double X, double Z) along, WheelFootprint probes,
            Func<double, double, double, double, bool> legal, double maxMetres)
        {
            if (!legal(px, pz, along.X, along.Z)) return null;
            var place = Centred(px, pz, along, legal) ?? (px, pz);
            // Centring can move a place past the search's limit (review round 5): the place itself must be within
            // maxMetres, else the grid cell is tried as it is.
            if (Distance(x, z, place.X, place.Z) > maxMetres + 1e-9 || !Fits(place.X, place.Z, along, probes, legal))
            {
                if (place.X == px && place.Z == pz) return null;
                place = (px, pz);
                if (!Fits(px, pz, along, probes, legal)) return null;
            }
            return place;
        }

        // Every place within maxMetres of (x, z) where a truck stands legally parked, nearest first, at most `limit`, each
        // at least spacingMetres from the ones before it (one per stretch of lane, not one per grid cell). The same
        // places Nearest would weigh, with no walk check: the test bridge lists them so a commissary whose automatic
        // search found nothing can be given a measured bay point (bay_candidates, docs/test-bridge.md).
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
            // Centring moves a place across its lane, so grid order isn't distance order (review round 1, Opus).
            return found.OrderBy(f => f.Distance).ToList();
        }

        // The candidates within radius plus the automatic search's own pick: Nearest within MaxMetres with the same
        // reach (walk) rule, run as the bay search runs it, always listed in place of any candidate within
        // SamePickMetres of it (it can differ: Nearest skips cells near a rejected one and falls back to an uncentred
        // cell at its limit; review round 1, Sol, Astra and Opus). `automatic` false: the search takes nothing from
        // this origin (a door cut off from the street). Nearest first.
        public static BayCandidates CandidatesWithPick(double x, double z, Func<double, double, double, double, bool> legal,
            Func<double, double, IEnumerable<(double X, double Z)>> axes, WheelFootprint footprint, double radius,
            Func<double, double, bool>? reach, bool automatic = true, double? heading = null)
        {
            // A measured nose-in bay lists and picks only places facing its own way (Measured).
            var listAxes = heading == null ? axes : FixedAxis(axes, HeadingAxis(heading.Value));
            var list = Candidates(x, z, legal, listAxes, footprint, radius).ToList();
            BaySnapResult? pick = automatic ? Measured(x, z, heading, legal, axes, footprint, MaxMetres, reach) : null;
            if (pick != null)
            {
                // The pick itself is listed, exactly as Nearest found it: a candidate within SamePickMetres is the same
                // place and gives way to it (review round 2, Astra: a centred candidate 2.5 cm off stood beyond 30 m).
                list.RemoveAll(c => Distance(c.X, c.Z, pick.X, pick.Z) < SamePickMetres);
                list.Add(pick);
            }
            return new BayCandidates(list.OrderBy(c => c.Distance).ToList(), pick);
        }

        public const double SamePickMetres = 0.05;

        // A heading in degrees clockwise from +z (as the log writes them) as a unit direction on the ground.
        public static (double X, double Z) HeadingAxis(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return (Math.Sin(radians), Math.Cos(radians));
        }

        // The parking lane's axes replaced by one fixed direction wherever there is parking: a measured nose-in bay in a
        // lot is searched only facing its own way (the stalls' row reads as one lane running across them).
        public static Func<double, double, IEnumerable<(double X, double Z)>> FixedAxis(Func<double, double, IEnumerable<(double X, double Z)>> axes, (double X, double Z) along)
        {
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            var only = new[] { along };
            return (x, z) => axes(x, z).Any() ? only : Array.Empty<(double X, double Z)>();
        }

        // The place a measured bay gives. With a heading (nose-in in a lot): the point itself, facing that way, when the
        // truck's wheels with their clearance pass the game's test there and reach accepts it, uncentred, so the truck
        // stands in the stall chosen by hand; else the nearest legal place facing that way (Nearest with FixedAxis).
        // Without a heading: Nearest as for an entrance. Null when nothing is legal within maxMetres.
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
            // One wheel at a time, without a list (review round 7): the search calls this for many places.
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

    // A candidate list and the automatic search's pick, which is one of the list's own entries (or null).
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
        // The way the parking lane runs at the bay (a truck parked there lines up with it).
        public double AlongX { get; }
        public double AlongZ { get; }
        // From the search's start.
        public double Distance { get; }
    }

    // The walk from a garage's door to a place the bay search found (review rounds 4 to 7). A complete NavMesh path at
    // most 1.6 times the straight line plus 15 m is a short walk; a complete path longer than that is the street
    // behind the building, rejected for good; no complete path between two ends that are both on the NavMesh is no
    // route, rejected too. With either end off the NavMesh nothing is known, and the place is accepted.
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

    // A rigid pose's rotation applied to a vector, as Unity's Quaternion * Vector3 (x, y, z, w; unit quaternion).
    // The saved-pose wheel test places each wheel with it (review round 7).
    public static class PoseMath
    {
        public static (double X, double Y, double Z) Rotate((double X, double Y, double Z, double W) q, (double X, double Y, double Z) v)
        {
            // v' = v + 2w(u x v) + 2(u x (u x v)), u = (q.x, q.y, q.z).
            var (ux, uy, uz, w) = q;
            var cx = uy * v.Z - uz * v.Y;
            var cy = uz * v.X - ux * v.Z;
            var cz = ux * v.Y - uy * v.X;
            var ccx = uy * cz - uz * cy;
            var ccy = uz * cx - ux * cz;
            var ccz = ux * cy - uy * cx;
            return (v.X + 2 * (w * cx + ccx), v.Y + 2 * (w * cy + ccy), v.Z + 2 * (w * cz + ccz));
        }

        // Where the game probes a wheel of a vehicle at this pose: the wheel's world position (position + rotation * hub)
        // less 0.1 m along the vehicle's up (CarController.UpdateParkingZone takes the wheel transform's up, which is
        // the vehicle's for the food truck's wheel controllers).
        public static (double X, double Y, double Z) WheelProbe((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation,
            (double X, double Y, double Z) hub)
        {
            var h = Rotate(rotation, hub);
            var up = Rotate(rotation, (0, 1, 0));
            return (position.X + h.X - 0.1 * up.X, position.Y + h.Y - 0.1 * up.Y, position.Z + h.Z - 0.1 * up.Z);
        }
    }

    // Picks the next commissary to resolve: the first pending one at or after `start`, wrapping round, or -1. A pending
    // commissary whose retry isn't due is passed over, so one that waits doesn't hold up the others (review round 6).
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

    // The bays a save keeps, and what "in the bay" means (the coordinator's decision, review round 4): a vehicle is in a
    // commissary's bay when the game's own wheel test calls it legally parked and its middle is within the bay radius of
    // one of the commissary's resolved bay points. The paint, Ctrl+F3, the panel's Commissary stock button, the visit unlock
    // and the 04:00 check all use this.
    public static class CommissaryBays
    {
        // Origins closer than this to the stored ones are the same search.
        public const double SameOriginMetres = 0.05;

        // Whether a commissary's bay is searched now: never searched for these origins and rule, or searched and found
        // nothing and not yet searched again since this load. A failed search is stored (so the 04:00 check and the
        // paint decide from it) but repeated once on every load, as the street round a door can have loaded late or
        // the rule have changed since (live session 2, 13 October 2026: three commissaries kept "no bay" for good).
        public static bool SearchDue(CommissaryBaySet? current, bool searchedThisLoad) =>
            current == null || ((current.Points == null || current.Points.Count == 0) && !searchedThisLoad);

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

        public static CommissaryBaySet Store(CommissaryState state, string siteId, IReadOnlyList<(double X, double Z)> origins, IEnumerable<BaySnapResult> found) =>
            Store(state, siteId, origins, found.Select(b => (b, ((double X, double Z)?)null)));

        // Each bay with the entrance its side was checked from, kept for "in the bay" (OnDoorSide).
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

        // Whether a vehicle of this kind has to be legally parked to be in a bay. A truck does; a cart (pushed by hand,
        // never parked in a lane by the game, which has no parking test for it) counts by its middle alone, on the
        // pavement or in the lane (Peter, 11 October 2026, review round 5).
        public static bool RequiresLegalParking(string? kind) => kind != "cart";

        // In the bay: legally parked by the game's wheel test, and the middle within the radius of a bay point.
        public static bool InBay(IEnumerable<CommissaryBayPoint>? points, double x, double z, double radius, bool legallyParked) =>
            legallyParked && WithinRadius(points, x, z, radius);

        // Whether a vehicle at (x, z) stands on the door's side of the street of a bay point it is within the radius of
        // (Peter, 13 October 2026; review round 1, Astra and Sol: the opposite kerb is within 15 m too): the straight
        // walk from the entrance that point was checked from to the vehicle's middle crosses no driving lane (BaySide).
        // lanes gives the driving lanes round a point's entrance, null when they can't be read. True when some point
        // within the radius says so; null (unknown) when none does and a point's side couldn't be checked (no entrance
        // stored, the lanes unread, or no lane found at all: every street has lanes, so finding none means the read
        // missed them, review round 1, Opus); false otherwise, including no point within the radius. Never true by
        // default.
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
