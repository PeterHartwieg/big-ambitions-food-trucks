#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    // Whether a truck standing at a position and rotation has its hatch (on its right, art/README.md) towards
    // the pavement. This side only probes; Core's HatchSide decides. Each point of the probe grid is read for
    // the things the game knows there: the traffic system's driving lanes (the reliable road signal), the Roads
    // layer, parking lanes, crosswalks and the walkable NavMesh. The NavMesh alone can't tell pavement from
    // road: people walk across streets, so the street is walkable too.
    internal static class HatchCheck
    {
        // The lanes are gathered from waypoints within this distance; a lane passing the truck has waypoints
        // a few metres apart, so its nearest ones are well within it.
        internal const float LaneSearchMetres = 25f;

        public sealed class Result
        {
            public Result(HatchVerdict verdict, SideReading hatch, SideReading opposite, string lanes, string kerb, bool lanesKnown)
            {
                Verdict = verdict;
                LanesKnown = lanesKnown;
                Hatch = hatch;
                Opposite = opposite;
                Lanes = lanes;
                Kerb = kerb;
            }

            public HatchVerdict Verdict { get; }
            public SideReading Hatch { get; }
            public SideReading Opposite { get; }
            public string Lanes { get; }
            public string Kerb { get; }
            // False when the reading had no traffic data (the manager not ready, or no lane near) and fell back to
            // the NavMesh alone; such a reading isn't kept (HireRoutes.Hatch).
            public bool LanesKnown { get; }
            public bool Opens => HatchSide.Opens(Verdict);

            // Null when the truck may open, else the message saying why not.
            public string? RefusalKey => Verdict switch
            {
                HatchVerdict.WrongWay => "foodtrucks:legality_truck_hatch",
                HatchVerdict.NoPavement => "foodtrucks:legality_truck_no_pavement",
                _ => null
            };

            public override string ToString() =>
                $"{Verdict}: hatch side {Hatch}; other side {Opposite}; {Lanes}; {Kerb}";
        }

        // nearby: lanes already read for a wider area (FreeParking reads them once for every space it tries); the
        // traffic system's search scans every waypoint in the city, so one read per call is enough.
        public static Result Probe(Vector3 position, Quaternion rotation, TrafficLanes? nearby = null)
        {
            var forward = Flat(rotation * Vector3.forward);
            var right = Flat(rotation * Vector3.right);
            var lanes = nearby != null ? nearby.Within(position, LaneSearchMetres) : TrafficLanes.Near(position, LaneSearchMetres);
            var hatch = Side(position, forward, right, 1, lanes, out var hatchKerb);
            var opposite = Side(position, forward, right, -1, lanes, out var oppositeKerb);
            var verdict = HatchSide.Decide(hatch, opposite);
            // The pavement's height over the truck's wheels, for seeing in the log whether kerbs are raised.
            var kerb = $"first pavement {Height(hatchKerb)} on the hatch side, {Height(oppositeKerb)} on the other";
            return new Result(verdict, hatch, opposite, lanes.Describe(), kerb, lanes.Known);
        }

        public static Result Probe(Transform truck) => Probe(truck.position, truck.rotation);

        private static SideReading Side(Vector3 position, Vector3 forward, Vector3 right, int side, TrafficLanes lanes, out float? kerbHeight)
        {
            var filter = QueuePerson.Filter;
            var rows = new List<(double, IReadOnlyList<Ground>)>();
            kerbHeight = null;
            foreach (var offset in HatchSide.Offsets)
            {
                var points = new List<Ground>();
                foreach (var along in HatchSide.Along)
                {
                    var wanted = position + right * (side * (float)offset) + forward * (float)along;
                    var walkable = NavMesh.SamplePosition(wanted + Vector3.up * 0.3f, out var hit, 0.6f, filter) && FlatDistance(hit.position, wanted) <= 0.45f;
                    var at = walkable ? hit.position : wanted;
                    var ground = HatchSide.Classify(lanes.Distance(at), Legality.RoadBelow(at), Legality.InParkingLane(at),
                        Legality.NearCrosswalk(at, 0.5f), walkable);
                    if (ground == Ground.Pavement && kerbHeight == null) kerbHeight = hit.position.y - position.y;
                    points.Add(ground);
                }
                rows.Add((offset, points));
            }
            return HatchSide.Read(rows);
        }

        private static string Height(float? metres) => metres is { } m ? $"{m:+0.00;-0.00} m" : "none";

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    // The traffic system's driving lanes near a point, as segments from each waypoint to the next. Cars drive
    // along these, so they are road wherever the NavMesh says people may walk. Unknown (no segments) when the
    // traffic system isn't running, which leaves the NavMesh alone to decide, as before.
    internal sealed class TrafficLanes
    {
        // Waypoints on a bridge or under one don't count for the street below.
        private const float HeightMetres = 3f;

        private readonly List<(Vector3 A, Vector3 B)> segments;
        private readonly string? problem;

        private TrafficLanes(List<(Vector3, Vector3)> segments, string? problem)
        {
            this.segments = segments;
            this.problem = problem;
        }

        public static readonly TrafficLanes None = new TrafficLanes(new List<(Vector3, Vector3)>(), "traffic system not running");

        public static TrafficLanes Near(Vector3 centre, float radius)
        {
            try
            {
                // Instance makes a new manager when there is none, so HasInstance first.
                if (!TrafficManager.HasInstance || !TrafficManager.IsInitialized) return None;
                var manager = TrafficManager.Instance;
                var near = new List<Waypoint>();
                // The game's search calls the filter for every waypoint within the radius; it collects them all.
                manager.GetClosestWaypoint(centre, radius, w =>
                {
                    if (w != null && Mathf.Abs(w.position.y - centre.y) <= HeightMetres) near.Add(w);
                    return false;
                });
                var found = new List<(Vector3, Vector3)>();
                foreach (var waypoint in near)
                {
                    foreach (var index in (waypoint.neighbors ?? new List<int>()).Concat(waypoint.prev ?? new List<int>()))
                    {
                        var other = manager.GetWaypoint(index);
                        if (other != null) found.Add((waypoint.position, other.position));
                    }
                    // A waypoint on its own still marks a lane.
                    found.Add((waypoint.position, waypoint.position));
                }
                return new TrafficLanes(found, found.Count == 0 ? $"no traffic lanes within {radius:F0} m" : null);
            }
            catch (Exception ex)
            {
                return new TrafficLanes(new List<(Vector3, Vector3)>(), "traffic lanes unreadable: " + ex.Message);
            }
        }

        // Flat distance from the point to the nearest lane's centre line, or null without lanes.
        public float? Distance(Vector3 point)
        {
            if (segments.Count == 0) return null;
            var best = float.MaxValue;
            var p = new Vector2(point.x, point.z);
            foreach (var (a, b) in segments)
            {
                var a2 = new Vector2(a.x, a.z);
                var ab = new Vector2(b.x, b.z) - a2;
                var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a2, ab) / ab.sqrMagnitude) : 0f;
                var d = (a2 + ab * t - p).magnitude;
                if (d < best) best = d;
            }
            return best;
        }

        public bool Known => segments.Count > 0;

        // The segments of these lanes within radius of a point (either end), as Near(centre, radius) would find them,
        // when these were read for a wider area that covers it. Unknown lanes stay unknown.
        public TrafficLanes Within(Vector3 centre, float radius)
        {
            if (segments.Count == 0) return this;
            var c = new Vector2(centre.x, centre.z);
            var kept = segments.Where(s => (new Vector2(s.A.x, s.A.z) - c).magnitude <= radius || (new Vector2(s.B.x, s.B.z) - c).magnitude <= radius).ToList();
            return new TrafficLanes(kept, kept.Count == 0 ? $"no traffic lanes within {radius:F0} m" : null);
        }

        public string Describe() => problem ?? $"{segments.Count} lane segments near";
    }
}
