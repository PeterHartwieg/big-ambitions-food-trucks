#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    internal static class BayProps
    {
        private const string Folder = "Assets/Mods/FoodTrucks/Commissary/BayArt/";
        private const float LetteringLength = 8.04f;
        private const float LetteringMaxCap = 0.70f;
        private const float LetteringMinCap = 0.30f;
        private const float PoleBehindKerb = 0.40f;
        private const float WalkwayMetres = 1.5f;
        private const float DoorClearMetres = 2.0f;
        private const float RoadBandMetres = 3.25f;
        private const float PropStepMetres = 0.5f;
        private const float MaxAlongMetres = 9f;
        private const float FacadeShiftMetres = 0.8f;
        private const float FacadeGapMetres = 0.12f;

        private static readonly Dictionary<string, GameObject?> prefabs = new Dictionary<string, GameObject?>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Material?> materials = new Dictionary<string, Material?>(StringComparer.Ordinal);

        internal static GameObject? Model(string name)
        {
            if (!prefabs.TryGetValue(name, out var prefab))
            {
                prefab = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(Folder + name + "_Display.prefab");
                prefabs[name] = prefab;
                if (prefab == null) Log.Error($"Commissary bay: {name}_Display.prefab missing from the mod's bundle.");
            }
            return prefab;
        }

        internal static Material? Material(string name)
        {
            if (!materials.TryGetValue(name, out var material))
            {
                material = FoodTrucksMod.Bundle?.LoadAsset<Material>(Folder + name + ".mat");
                materials[name] = material;
            }
            return material;
        }

        public static string Lettering(Transform root, (float From, float To) longest, float width, Func<Vector3, float> groundAt)
        {
            var model = Model("BayLettering");
            if (model == null) return "no lettering (model missing)";
            var cap = Mathf.Min(LetteringMaxCap, (longest.To - longest.From - 1.5f) / LetteringLength, width - 0.7f);
            if (cap < LetteringMinCap) return $"no lettering (the longest stretch, {longest.To - longest.From:F1} m, is too short)";
            var middle = (longest.From + longest.To) / 2f;
            var half = cap * LetteringLength / 2f;
            var y = new[] { (middle - half, -cap / 2), (middle - half, cap / 2), (middle, 0f), (middle + half, -cap / 2), (middle + half, cap / 2) }
                .Max(p => groundAt(root.TransformPoint(new Vector3(p.Item1, 0f, p.Item2))));
            var letters = Object.Instantiate(model, root, false);
            letters.name = "LoadingZone";
            letters.transform.localPosition = new Vector3(middle, 0f, 0f);
            letters.transform.position = new Vector3(letters.transform.position.x, y + 0.027f, letters.transform.position.z);
            letters.transform.localRotation = Quaternion.identity;
            letters.transform.localScale = new Vector3(cap, 1f, cap);
            return $"LOADING ZONE {cap:F2} m high, {cap * LetteringLength:F1} m long";
        }

        public static string Sign(Transform root, string siteId, (float From, float To) longest, float width, Func<Vector3, float> groundAt, float laneY)
        {
            var model = Model("BaySign");
            if (model == null) return "no sign (model missing)";
            var middle = (longest.From + longest.To) / 2f;
            var tried = new List<string>();
            foreach (var shift in new[] { 0f, 1.5f, -1.5f, 3f, -3f })
            {
                var x = middle + shift;
                if (x < longest.From || x > longest.To) continue;
                var edgeY = groundAt(root.TransformPoint(new Vector3(x, 0f, -width / 2f)));
                float? kerb = null;
                var found = "";
                for (var z = -width / 2f; z >= -width / 2f - 3f; z -= 0.1f)
                {
                    var ground = groundAt(root.TransformPoint(new Vector3(x, 0f, z)));
                    if (ground - edgeY >= 0.08f) { kerb = z; found = "a kerb"; break; }
                }
                if (kerb == null)
                    for (var z = -width / 2f - 0.2f; z >= -width / 2f - 3f; z -= 0.1f)
                    {
                        var p = root.TransformPoint(new Vector3(x, 0f, z));
                        p.y = groundAt(p);
                        if (!Legality.InParkingLane(p) && !Legality.RoadBelow(p) && NavMesh.SamplePosition(p, out _, 0.3f, NavMesh.AllAreas))
                        {
                            kerb = z;
                            found = "the pavement's edge (no raised kerb)";
                            break;
                        }
                    }
                if (kerb == null)
                {
                    tried.Add($"{shift:+0.0;-0.0} m: no kerb and no pavement within 3 m of the paint's edge");
                    continue;
                }
                var local = new Vector3(x, 0f, kerb.Value - PoleBehindKerb);
                var at = root.TransformPoint(local);
                at.y = groundAt(at);
                var blocker = Physics.OverlapCapsule(at + Vector3.up * 0.3f, at + Vector3.up * 2.0f, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(c => c != null && !c.transform.IsChildOf(root) && !NotWall(c) && c.GetComponentInParent<ThirdPersonCharacter>() == null &&
                                         ((1 << c.gameObject.layer) & (int)LayerHelper.humanLayerMask) == 0);
                if (blocker != null)
                {
                    tried.Add($"{shift:+0.0;-0.0} m: the pole at x {at.x:F1}, z {at.z:F1} (behind {found}) would stand in {Describe(blocker)}");
                    continue;
                }
                var sign = Object.Instantiate(model, root, false);
                sign.name = "LoadingZoneSign";
                sign.transform.position = at;
                sign.transform.localRotation = Quaternion.identity;
                var plate = Material("M_BaySign_" + siteId);
                if (plate != null)
                    foreach (var renderer in sign.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.EndsWith("Plate", StringComparison.Ordinal)) renderer.sharedMaterial = plate;
                return $"sign at x {at.x:F1}, z {at.z:F1}, {PoleBehindKerb:F2} m behind {found} {-kerb.Value - width / 2f:F1} m past the paint{(plate == null ? " (no named plate for " + siteId + ")" : "")}";
            }
            return "no sign (" + (tried.Count == 0 ? "the longest stretch is too short for a spot" : string.Join("; ", tried)) + ")";
        }

        public static string SignAtHead(Transform root, string siteId, (float From, float To) longest, int head, Func<Vector3, float> groundAt)
        {
            var model = Model("BaySign");
            if (model == null) return "no sign (model missing)";
            var end = head > 0 ? longest.To : longest.From;
            var tried = new List<string>();
            foreach (var across in new[] { 0f, 0.6f, -0.6f })
            {
                var endY = groundAt(root.TransformPoint(new Vector3(end, 0f, across)));
                float? kerb = null;
                var found = "";
                for (var d = 0f; d <= 3f; d += 0.1f)
                {
                    var ground = groundAt(root.TransformPoint(new Vector3(end + head * d, 0f, across)));
                    if (ground - endY >= 0.08f) { kerb = d; found = "a kerb"; break; }
                }
                if (kerb == null)
                    for (var d = 0.2f; d <= 3f; d += 0.1f)
                    {
                        var p = root.TransformPoint(new Vector3(end + head * d, 0f, across));
                        p.y = groundAt(p);
                        if (!Legality.InParkingLane(p) && !Legality.RoadBelow(p) && NavMesh.SamplePosition(p, out _, 0.3f, NavMesh.AllAreas))
                        {
                            kerb = d;
                            found = "the pavement's edge (no raised kerb)";
                            break;
                        }
                    }
                if (kerb == null)
                {
                    tried.Add($"{across:+0.0;-0.0} m across: no kerb and no pavement within 3 m of the stall's head");
                    continue;
                }
                var at = root.TransformPoint(new Vector3(end + head * (kerb.Value + PoleBehindKerb), 0f, across));
                at.y = groundAt(at);
                var blocker = Physics.OverlapCapsule(at + Vector3.up * 0.3f, at + Vector3.up * 2.0f, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(c => c != null && !c.transform.IsChildOf(root) && !NotWall(c) && c.GetComponentInParent<ThirdPersonCharacter>() == null &&
                                         ((1 << c.gameObject.layer) & (int)LayerHelper.humanLayerMask) == 0);
                if (blocker != null)
                {
                    tried.Add($"{across:+0.0;-0.0} m across: the pole at x {at.x:F1}, z {at.z:F1} (behind {found}) would stand in {Describe(blocker)}");
                    continue;
                }
                var sign = Object.Instantiate(model, root, false);
                sign.name = "LoadingZoneSign";
                sign.transform.position = at;
                sign.transform.localRotation = Quaternion.Euler(0f, head > 0 ? -90f : 90f, 0f);
                var plate = Material("M_BaySign_" + siteId);
                if (plate != null)
                    foreach (var renderer in sign.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.EndsWith("Plate", StringComparison.Ordinal)) renderer.sharedMaterial = plate;
                return $"sign at x {at.x:F1}, z {at.z:F1}, at the stall's head, {PoleBehindKerb:F2} m behind {found} {kerb.Value:F1} m past the paint{(plate == null ? " (no named plate for " + siteId + ")" : "")}";
            }
            return "no sign (" + string.Join("; ", tried) + ")";
        }

        private sealed class Prop
        {
            public Prop(string name, Func<Transform, GameObject?> make) { Name = name; Make = make; }
            public string Name { get; }
            public Func<Transform, GameObject?> Make { get; }
        }

        public readonly struct Outcome
        {
            public Outcome(string line, bool tryAgain) { Line = line; TryAgain = tryAgain; }
            public string Line { get; }
            public bool TryAgain { get; }
        }

        private readonly struct Refusal
        {
            public Refusal(string why, bool transient, string? kind = null) { Why = why; Transient = transient; Kind = kind ?? why; }
            public string Why { get; }
            public bool Transient { get; }
            public string Kind { get; }
        }

        public static Outcome ByTheDoor(Transform parent, Vector3 door, TrafficLanes lanes, CityBuildingController? own = null)
        {
            var nearest = lanes.Nearest(door);
            if (nearest == null) return new Outcome("no props (no driving lane near the door to tell the street from the building)", false);
            var outward = nearest.Value.Point - door;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) return new Outcome("no props (the door stands on a driving lane)", false);
            outward.Normalize();
            // The facade: back along the outward line from the pavement, at waist height. Only its position across the
            // pavement and its facing count; the props' heights come from the ground under each (Fits).
            // The player, people and vehicles are never the wall (live session 4: a player teleported to the door was
            // read as the facade, and every place came out 1.5 m too far out, by the driving lane).
            // The ray starts at the driving lane, not 3 m out from the door (live session 5: Lower Manhattan's door
            // point stands more than that inside its building, so a ray from 3 m out started inside the walls, which
            // rays don't hit from within, and every place had "no wall behind it"). It runs back past the door, and
            // the facade is the first surface of the Buildings layer met from the street; failing that, the first
            // solid surface between 1.5 m behind the door and 3 m in front of it, as before.
            // First the wall the door is in: the nearest building surface within WallNearDoorMetres of the door point,
            // facing away from it. Lower Manhattan's door is in a side face, on an alley off the street (live session 6:
            // seen in game, the door point stands 0.5 m off Factory9's north face, and a ray from the street lane
            // passed along that face without touching it). Only colliders whose nearest point Unity can tell count (box,
            // sphere, capsule, convex mesh), so a building's concave mesh falls through to the ray from the street.
            Collider? doorWall = null;
            var facade = door;
            var near = NearestWall(door, own);
            if (near != null)
            {
                doorWall = near.Value.Collider;
                facade = new Vector3(near.Value.Point.x, door.y, near.Value.Point.z);
                outward = near.Value.Outward;
            }
            else
            {
                var laneMetres = Mathf.Max(3f, Flat(nearest.Value.Point - door));
                var from = door + outward * laneMetres + Vector3.up * 1.0f;
                var rayMetres = laneMetres + FacadeBehindDoorMetres;
                var hits = Physics.RaycastAll(from, -outward, rayMetres, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider != null && !NotWall(h.collider)).OrderBy(h => h.distance).ToList();
                var rayAlong = new Vector3(outward.z, 0f, -outward.x);
                var hit = hits.FirstOrDefault(h => IsBuilding(h.collider) && h.distance >= laneMetres - 6f && WidthAlong(h.collider, rayAlong) >= KioskMetres);
                if (hit.collider == null)
                    hit = hits.FirstOrDefault(h => h.distance >= laneMetres - 3f && h.distance <= laneMetres + 1.5f && WidthAlong(h.collider, rayAlong) >= KioskMetres);
                if (hit.collider == null)
                    return new Outcome($"no props by the door at x {door.x:F1}, z {door.z:F1}: no building wall within {WallNearDoorMetres:F0} m of the door point, and none on a ray at waist height from the driving lane at " +
                                       $"x {from.x:F1}, z {from.z:F1} back {rayMetres:F0} m, {FacadeBehindDoorMetres:F0} m past the door ({(hits.Count == 0 ? "nothing solid hit" : "hit only " + string.Join(", ", hits.Take(3).Select(h => Describe(h.collider))))})", hits.Count == 0);
                doorWall = hit.collider;
                facade = new Vector3(hit.point.x, door.y, hit.point.z);
                var normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude > 0.5f && Vector3.Angle(normal, outward) < 35f) outward = normal.normalized;
            }
            var along = new Vector3(outward.z, 0f, -outward.x);
            var entrances = World.DoorPositions().Where(d => Flat(d - door) < 20f).ToList();
            var props = new[]
            {
                new Prop("pallet", GamePallet),
                new Prop("roll cage", p => Copy(Model("BayRollCage"), p)),
                new Prop("hand truck", GameHandTruck),
                new Prop("dumpster", p => Copy(Model("BayDumpster"), p)),
            };
            var placed = new List<string>();
            var skipped = new List<string>();
            var tryAgain = false;
            var offNavMesh = 0;
            var reach = new Dictionary<int, float> { [1] = DoorClearMetres, [-1] = DoorClearMetres };
            foreach (var prop in props)
            {
                GameObject? made = null;
                try
                {
                    made = prop.Make(parent);
                    if (made == null) { skipped.Add(prop.Name + " (no model)"); continue; }
                    made.SetActive(false);
                    var box = made.GetComponent<BoxCollider>();
                    var size = box != null ? box.size : new Vector3(1f, 1f, 1f);
                    var centre = box != null ? box.center : new Vector3(0f, 0.5f, 0f);
                    var turn = size.z > size.x ? Quaternion.AngleAxis(90f, Vector3.up) : Quaternion.identity;
                    var rotation = Quaternion.LookRotation(outward, Vector3.up) * turn;
                    var alongSize = Mathf.Max(size.x, size.z);
                    var depth = Mathf.Min(size.x, size.z);
                    var refusals = new Dictionary<string, (int Count, string Example)>(StringComparer.Ordinal);
                    var transient = false;
                    var ok = false;
                    var tried = 0;
                    foreach (var side in new[] { 1, -1 })
                    {
                        foreach (var offset in PropPlacement.Offsets(reach[side], alongSize / 2f, MaxAlongMetres, PropStepMetres))
                        {
                            var s = side * (float)offset;
                            tried++;
                            var wallAt = FacadeAt(facade, outward, along, s, alongSize, parent);
                            var wall = wallAt ?? 0f;
                            var at = facade + outward * (wall + depth / 2f + FacadeGapMetres) + along * s;
                            var ground = 0f;
                            var why = wallAt == null
                                ? new Refusal($"no wall behind it at x {at.x:F1}, z {at.z:F1}", false, "no wall")
                                : Fits(at, rotation, centre, size, outward, depth, lanes, entrances, parent, out ground);
                            if (why != null)
                            {
                                transient |= why.Value.Transient;
                                refusals[why.Value.Kind] = refusals.TryGetValue(why.Value.Kind, out var r) ? (r.Count + 1, r.Example) : (1, why.Value.Why);
                                continue;
                            }
                            at.y = ground;
                            made.transform.SetPositionAndRotation(at - rotation * new Vector3(centre.x, 0f, centre.z), rotation);
                            reach[side] = (float)offset + alongSize / 2f + 0.35f;
                            ok = true;
                            placed.Add($"{prop.Name} {s:+0.0;-0.0} m along the facade{(Mathf.Abs(wall) > 0.01f ? $", {wall:+0.00;-0.00} m off the door's line" : "")}");
                            break;
                        }
                        if (ok) break;
                    }
                    if (!ok)
                    {
                        var reasons = refusals.Values.OrderByDescending(r => r.Count).Take(4).Select(r => r.Count > 1 ? $"{r.Count}x {r.Example}" : r.Example);
                        skipped.Add($"{prop.Name} ({tried} places within {MaxAlongMetres:F0} m: {string.Join("; ", reasons)})");
                        tryAgain |= transient;
                        if (refusals.Count == 1 && refusals.ContainsKey(NotOnNavMesh)) offNavMesh++;
                        Object.Destroy(made);
                        continue;
                    }
                    made.SetActive(true);
                }
                catch (Exception ex)
                {
                    if (made != null) Object.Destroy(made);
                    skipped.Add($"{prop.Name} ({ex.GetType().Name}: {ex.Message})");
                    tryAgain = true;
                }
            }
            if (placed.Count == 0 && skipped.Count > 0 && offNavMesh == skipped.Count) tryAgain = true;
            return new Outcome($"props by the door at x {door.x:F1}, z {door.z:F1} (facade facing {Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg:F0} degrees" +
                               $", {Describe(doorWall!)}, {(near != null ? "the wall at the door" : "met from the street")}, {Vector3.Dot(facade - door, outward):+0.0;-0.0} m from the door point): {(placed.Count == 0 ? "none" : string.Join(", ", placed))}" +
                               (skipped.Count > 0 ? "; left out: " + string.Join(", ", skipped) : ""), tryAgain);
        }

        private const string NotOnNavMesh = "not on the pavement's NavMesh";

        private static float? FacadeAt(Vector3 facade, Vector3 outward, Vector3 along, float s, float length, Transform ownProps)
        {
            var hits = new List<double>();
            foreach (var a in new[] { s - length / 2f + 0.05f, s, s + length / 2f - 0.05f })
                foreach (var h in new[] { 0.2f, 0.6f, 1.0f })
                {
                    var from = facade + along * a + outward * 3f + Vector3.up * h;
                    foreach (var hit in Physics.RaycastAll(from, -outward, 3f + FacadeShiftMetres + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        var c = hit.collider;
                        if (c == null || c.transform.IsChildOf(ownProps) || NotWall(c)) continue;
                        hits.Add(Vector3.Dot(hit.point - facade, outward));
                    }
                }
            var offset = PropPlacement.FacadeOffset(hits, FacadeShiftMetres);
            return offset == null ? (float?)null : (float)offset.Value;
        }

        private static bool Moving(Collider c) =>
            c.GetComponentInParent<ThirdPersonCharacter>() != null || c.GetComponentInParent<VehicleController>() != null ||
            ((1 << c.gameObject.layer) & (int)LayerHelper.humanLayerMask) != 0 || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic);

        private static bool NotWall(Collider c)
        {
            var layer = c.gameObject.layer;
            return Moving(c) || layer == PlayerLayer || c.transform.root.gameObject.layer == PlayerLayer ||
                   layer == LayerHelper.ParkedVehiclesLayerIndex || layer == LayerHelper.AiVehiclesLayerIndex || layer == LayerHelper.PlayerVehiclesLayerIndex;
        }

        private static readonly int PlayerLayer = LayerHelper.PlayerLayerIndex;

        private static bool IsBuilding(Collider c) =>
            ((1 << c.gameObject.layer) & (int)LayerHelper.buildingAndOutlinesLayerMask) != 0 ||
            (c.gameObject.layer == LayerHelper.DefaultLayerIndex && c.GetComponentInParent<CityBuildingController>() != null);

        private static (Collider Collider, Vector3 Point, Vector3 Outward)? NearestWall(Vector3 door, CityBuildingController? own)
        {
            if (own != null)
            {
                var ownWall = NearestWall(door, c => c.GetComponentInParent<CityBuildingController>() == own);
                if (ownWall != null) return ownWall;
            }
            return NearestWall(door, _ => true);
        }

        private static (Collider Collider, Vector3 Point, Vector3 Outward)? NearestWall(Vector3 door, Func<Collider, bool> accept)
        {
            (Collider, Vector3, Vector3)? best = null;
            var bestDistance = float.MaxValue;
            var probe = door + Vector3.up * 1.0f;
            foreach (var c in Physics.OverlapSphere(probe, WallNearDoorMetres, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (c == null || !IsBuilding(c) || NotWall(c) || c.bounds.size.y < 1.5f || !accept(c)) continue;
                if (Mathf.Max(c.bounds.size.x, c.bounds.size.z) < KioskMetres) continue;
                if (c is MeshCollider mesh && !mesh.convex) continue;
                var point = c.ClosestPoint(probe);
                var away = probe - point;
                away.y = 0f;
                if (away.sqrMagnitude < 0.05f * 0.05f) continue;
                var distance = away.magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (c, point, away / distance);
                }
            }
            return best;
        }

        private const float WallNearDoorMetres = 2f;

        private static float WidthAlong(Collider c, Vector3 along)
        {
            var e = c.bounds.extents;
            return 2f * (Mathf.Abs(along.x) * e.x + Mathf.Abs(along.z) * e.z);
        }

        private const float KioskMetres = 4f;
        private const float FacadeBehindDoorMetres = 8f;

        internal static string Describe(Collider c)
        {
            var root = c.transform.root;
            var layer = LayerMask.LayerToName(c.gameObject.layer);
            return $"{c.name}{(root != null && root != c.transform ? " on " + root.name : "")}, layer {(string.IsNullOrEmpty(layer) ? c.gameObject.layer.ToString() : layer)}";
        }

        private static Refusal? Fits(Vector3 at, Quaternion rotation, Vector3 centre, Vector3 size, Vector3 outward, float depth, TrafficLanes lanes,
            List<Vector3> entrances, Transform ownProps, out float ground)
        {
            ground = GroundUnder(at, ownProps);
            at.y = ground;
            if (!NavMesh.SamplePosition(at, out var nav, 0.6f, NavMesh.AllAreas)) return new Refusal($"{NotOnNavMesh} at x {at.x:F1}, z {at.z:F1}", false, NotOnNavMesh);
            ground = nav.position.y;
            at.y = ground;
            var road = lanes.Distance(at);
            if (road != null && road.Value < RoadBandMetres + depth / 2f) return new Refusal($"{road.Value:F1} m from a driving lane at x {at.x:F1}, z {at.z:F1}", false, "road");
            var footprint = new Vector2(Mathf.Max(size.x, size.z) / 2f, depth / 2f);
            foreach (var entrance in entrances)
            {
                var local = Quaternion.Inverse(Quaternion.LookRotation(outward, Vector3.up)) * (entrance - at);
                var dx = Mathf.Max(0f, Mathf.Abs(local.x) - footprint.x);
                var dz = local.z < 0f ? 0f : Mathf.Max(0f, Mathf.Abs(local.z) - footprint.y);
                if (Mathf.Abs(local.y) < 3f && new Vector2(dx, dz).magnitude < DoorClearMetres - 0.05f)
                    return new Refusal($"next to the entrance at x {entrance.x:F1}, z {entrance.z:F1}", false, "entrance");
            }
            var front = at + outward * (depth / 2f + WalkwayMetres);
            front.y = GroundUnder(front, ownProps);
            if (!NavMesh.SamplePosition(front, out var walk, 0.4f, NavMesh.AllAreas) || Mathf.Abs(walk.position.y - ground) > 0.3f)
                return new Refusal($"no walkway left in front at x {at.x:F1}, z {at.z:F1}", false, "walkway");
            var halves = size / 2f - new Vector3(0.03f, 0f, 0.03f);
            halves.y = Mathf.Max(0.05f, halves.y - 0.1f);
            var middle = new Vector3(at.x, ground + 0.15f + halves.y, at.z);
            var transient = false;
            foreach (var other in Physics.OverlapBox(middle, halves, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (other == null) continue;
                var person = other.GetComponentInParent<ThirdPersonCharacter>() != null || ((1 << other.gameObject.layer) & (int)LayerHelper.humanLayerMask) != 0 ||
                             other.gameObject.layer == PlayerLayer;
                if (person && !IsPlayer(other)) continue;
                if (person || other.GetComponentInParent<VehicleController>() != null || (other.attachedRigidbody != null && !other.attachedRigidbody.isKinematic))
                {
                    transient = true;
                    continue;
                }
                return new Refusal("something stands there: " + Describe(other), false);
            }
            return transient ? new Refusal("a vehicle or the player stands there", true, "moving") : (Refusal?)null;
        }

        private static bool IsPlayer(Collider collider) =>
            Flat(collider.transform.position - PlayerHelper.GetPosition()) < 1.5f;

        private static float GroundUnder(Vector3 point, Transform ownProps)
        {
            var best = float.MinValue;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 0.5f, Vector3.down, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var c = hit.collider;
                if (c == null || c.transform.IsChildOf(ownProps)) continue;
                if (NotWall(c)) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            return best > float.MinValue ? best : point.y;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private static GameObject? GamePallet(Transform parent)
        {
            var copy = CopyGame("DeliveryPallet", parent, t => !t.name.StartsWith("SM_CardboardBox", StringComparison.Ordinal) || t.localPosition.y < 0.7f);
            return copy ?? Copy(Model("BayPalletStack"), parent);
        }

        private static GameObject? GameHandTruck(Transform parent)
        {
            var copy = CopyGame("Vehicles/PlayerVehicles/HandTruck", parent, _ => true);
            return copy ?? Copy(Model("BayHandTruck"), parent);
        }

        private static GameObject? Copy(GameObject? model, Transform parent)
        {
            if (model == null) return null;
            var made = Object.Instantiate(model, parent, false);
            made.name = model.name;
            return made;
        }

        private static GameObject? CopyGame(string prefabName, Transform parent, Func<Transform, bool> keep)
        {
            GameObject? prefab;
            try { prefab = PrefabHelper.LoadPrefabAssetByName(prefabName); }
            catch (Exception ex)
            {
                Log.Info($"Commissary bay: the game's {prefabName} prefab not loaded ({ex.Message}); the mod's model stands in.");
                return null;
            }
            if (prefab == null) return null;
            var root = prefab.transform;
            var parts = new List<(Mesh Mesh, Material[] Materials, Matrix4x4 Matrix)>();
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = renderer.transform;
                if (!Active(t, root) || !keep(t)) continue;
                var name = t.name;
                if (name.Contains("Indicator") || name.Contains("ForSale") || name.Contains("NavMeshTarget")) continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                parts.Add((mesh, renderer.sharedMaterials, root.worldToLocalMatrix * t.localToWorldMatrix));
            }
            if (parts.Count == 0)
            {
                Log.Info($"Commissary bay: the game's {prefabName} has no plain meshes to copy; the mod's model stands in.");
                return null;
            }
            var made = new GameObject("Game" + prefabName.Split('/').Last());
            made.SetActive(false);
            made.transform.SetParent(parent, false);
            var bounds = new Bounds();
            var first = true;
            for (var i = 0; i < parts.Count; i++)
            {
                var (mesh, mats, m) = parts[i];
                var part = new GameObject(mesh.name);
                part.transform.SetParent(made.transform, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var partRenderer = part.AddComponent<MeshRenderer>();
                partRenderer.sharedMaterials = mats;
                partRenderer.renderingLayerMask = BayMarkers.NoDecalsRenderingLayers;
                var b = mesh.bounds;
                var corners = new List<Vector3>();
                for (var c = 0; c < 8; c++)
                    corners.Add(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                foreach (var corner in corners)
                {
                    if (first) bounds = new Bounds(corner, Vector3.zero);
                    else bounds.Encapsulate(corner);
                    first = false;
                }
            }
            foreach (Transform child in made.transform) child.localPosition -= new Vector3(0f, bounds.min.y, 0f);
            bounds.center -= new Vector3(0f, bounds.min.y, 0f);
            var box = made.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            var obstacle = made.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = bounds.center;
            obstacle.size = bounds.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            return made;
        }

        private static bool Active(Transform t, Transform root)
        {
            for (var x = t; x != null; x = x.parent)
            {
                if (!x.gameObject.activeSelf) return false;
                if (x == root) break;
            }
            return true;
        }
    }
}
