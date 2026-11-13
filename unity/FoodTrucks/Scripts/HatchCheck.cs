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
    internal static class HatchCheck
    {
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
            public bool LanesKnown { get; }
            public bool Opens => HatchSide.Opens(Verdict);

            public string? RefusalKey => Verdict switch
            {
                HatchVerdict.WrongWay => "foodtrucks:legality_truck_hatch",
                HatchVerdict.NoPavement => "foodtrucks:legality_truck_no_pavement",
                _ => null
            };

            public override string ToString() =>
                $"{Verdict}: hatch side {Hatch}; other side {Opposite}; {Lanes}; {Kerb}";
        }

        public static Result Probe(Vector3 position, Quaternion rotation, TrafficLanes? nearby = null)
        {
            var forward = Flat(rotation * Vector3.forward);
            var right = Flat(rotation * Vector3.right);
            var lanes = nearby != null ? nearby.Within(position, LaneSearchMetres) : TrafficLanes.Near(position, LaneSearchMetres);
            var hatch = Side(position, forward, right, 1, lanes, out var hatchKerb);
            var opposite = Side(position, forward, right, -1, lanes, out var oppositeKerb);
            var verdict = HatchSide.Decide(hatch, opposite);
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

    internal sealed class TrafficLanes
    {
        private const float HeightMetres = (float)LaneReach.HeightMetres;

        private readonly List<(Vector3 Point, List<(Vector3 A, Vector3 B)> Segments)> waypoints;
        private readonly List<(Vector3 A, Vector3 B)> segments;
        private readonly string? problem;

        private TrafficLanes(List<(Vector3, List<(Vector3, Vector3)>)> waypoints, string? problem, bool unavailable = false)
        {
            Unavailable = unavailable;
            this.waypoints = waypoints;
            segments = waypoints.SelectMany(w => w.Item2).ToList();
            this.problem = problem;
        }

        public static readonly TrafficLanes None = new TrafficLanes(new List<(Vector3, List<(Vector3, Vector3)>)>(), "traffic system not running", unavailable: true);

        public bool Unavailable { get; }

        public static TrafficLanes Near(Vector3 centre, float radius) => Read(centre, radius).Within(centre, radius);

        public static TrafficLanes Read(Vector3 centre, float radius)
        {
            try
            {
                if (!TrafficManager.HasInstance || !TrafficManager.IsInitialized) return None;
                var manager = TrafficManager.Instance;
                var near = new List<Waypoint>();
                manager.GetClosestWaypoint(centre, radius, w =>
                {
                    if (w != null) near.Add(w);
                    return false;
                });
                var found = new List<(Vector3, List<(Vector3, Vector3)>)>();
                foreach (var waypoint in near)
                {
                    var own = new List<(Vector3, Vector3)>();
                    foreach (var index in (waypoint.neighbors ?? new List<int>()).Concat(waypoint.prev ?? new List<int>()))
                    {
                        var other = manager.GetWaypoint(index);
                        if (other != null) own.Add((waypoint.position, other.position));
                    }
                    own.Add((waypoint.position, waypoint.position));
                    found.Add((waypoint.position, own));
                }
                return new TrafficLanes(found, null);
            }
            catch (Exception ex)
            {
                return new TrafficLanes(new List<(Vector3, List<(Vector3, Vector3)>)>(), "traffic lanes unreadable: " + ex.Message, unavailable: true);
            }
        }

        public TrafficLanes Within(Vector3 centre, float radius)
        {
            if (problem != null) return this;
            var limit = radius * radius;
            var kept = waypoints.Where(w => Vector3.SqrMagnitude(centre - w.Point) < limit && Mathf.Abs(w.Point.y - centre.y) <= HeightMetres).ToList();
            return new TrafficLanes(kept, kept.Count == 0 ? $"no traffic lanes within {radius:F0} m" : null);
        }

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

        public (Vector3 Point, Vector3 Direction)? Nearest(Vector3 point)
        {
            (Vector3, Vector3)? found = null;
            var best = float.MaxValue;
            var p = new Vector2(point.x, point.z);
            foreach (var (a, b) in segments)
            {
                var a2 = new Vector2(a.x, a.z);
                var ab = new Vector2(b.x, b.z) - a2;
                if (ab.sqrMagnitude < 0.25f) continue;
                var t = Mathf.Clamp01(Vector2.Dot(p - a2, ab) / ab.sqrMagnitude);
                var d = (a2 + ab * t - p).magnitude;
                if (d >= best) continue;
                best = d;
                found = (Vector3.Lerp(a, b, t), new Vector3(ab.x, 0f, ab.y).normalized);
            }
            return found;
        }

        public List<((double X, double Z) A, (double X, double Z) B)> FlatSegments() =>
            segments.Select(s => (((double)s.A.x, (double)s.A.z), ((double)s.B.x, (double)s.B.z))).ToList();

        public bool Known => segments.Count > 0;

        public string Describe() => problem ?? $"{segments.Count} lane segments near";
    }
}
