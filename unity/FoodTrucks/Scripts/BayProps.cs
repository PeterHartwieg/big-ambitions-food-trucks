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
    // The commissary bay's street furniture beyond the paint (Peter, 11 October 2026: "the purple box breaks immersion"):
    // the LOADING ZONE lettering in the bay, a NYC-style loading zone sign at the kerb naming the commissary, and a few
    // props on the pavement by the commissary's door. Models come from the mod's bundle (art/commissary, built by
    // CommissaryAssets); the pallet of boxes and the hand truck are the game's own meshes where it has them (the
    // DeliveryPallet and HandTruck prefabs, loaded by name and copied as plain meshes so none of their game code runs),
    // with the mod's models as the fallback. The game has no dumpster or roll cage, so those are always the mod's.
    // Everything here is a child of the bay's marker, shown, hidden and destroyed with it (BayMarkers). The lettering
    // and the sign have no collider; each prop has a box collider and a carving NavMeshObstacle of its footprint, and
    // stands against the facade beside the door, out of the door's way, off the road and with a walkway left in front.
    internal static class BayProps
    {
        private const string Folder = "Assets/Mods/FoodTrucks/Commissary/BayArt/";
        // The lettering model's size at scale 1 (art/commissary/build_commissary.py): 1 m cap height, 8.04 m long.
        private const float LetteringLength = 8.04f;
        private const float LetteringMaxCap = 0.70f;
        private const float LetteringMinCap = 0.30f;
        // A pole stands this far behind the kerb's face, on the pavement.
        private const float PoleBehindKerb = 0.40f;
        // Pavement left free in front of a prop, and the gap kept to each side of the door's middle.
        private const float WalkwayMetres = 1.5f;
        private const float DoorClearMetres = 2.0f;
        // A point closer than this to a driving lane's middle is road or a parking lane (HatchSide's 3.25 m band).
        private const float RoadBandMetres = 3.25f;
        // Props are tried along the facade every PropStepMetres, out to MaxAlongMetres from the door's middle, and stand
        // against the wall read where each goes: the outermost wall within FacadeShiftMetres of the door's own line
        // (Core PropPlacement; live sessions 1 to 3: Hell's Kitchen's recessed door between pilasters put every prop
        // into a pilaster, one place per side, "something stands there").
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

        // LOADING ZONE in the middle of the longest stretch, scaled to fit it and the paint's width, flat at the road's
        // highest point under it. `local` is the paint root's frame: +x along the street, +z to the road. Returns a line
        // for the log.
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

        // The sign: on the pavement just behind the kerb, by the middle of the longest stretch, its plate facing the
        // road for drivers. Local -z is the pavement side (the paint's root faces the road). The
        // kerb is the first step up of 8 cm or more from the paint's own edge (0.9.10 measured from the driving lane's
        // waypoint height, which needn't be the road's); without one (a flush plaza, as across from the Hell's Kitchen
        // door), the pavement's edge: the first point off the parking lane and the road that is on the NavMesh. The pole
        // stands PoleBehindKerb past either, where nothing solid stands; a person or vehicle there doesn't count.
        // Without a place the log says, for each spot tried, what was missing (live session 4: 0.9.10's line didn't
        // say which of its two reasons held).
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
                // Room for the pole: nothing solid within 0.25 m of it up to head height.
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
                // As the model is built: the plate on the road side of the pole (the root's +z), its face and double
                // arrow towards the road. Seen in game (live session 7): the plate stood 4 cm road-side of the pole this
                // way. 0b461b7 turned it half round from a picture taken from the pavement, where the back's mirrored
                // copy of the face reads as well, and so put the plate on the pavement side.
                sign.transform.localRotation = Quaternion.identity;
                var plate = Material("M_BaySign_" + siteId);
                if (plate != null)
                    foreach (var renderer in sign.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.EndsWith("Plate", StringComparison.Ordinal)) renderer.sharedMaterial = plate;
                return $"sign at x {at.x:F1}, z {at.z:F1}, {PoleBehindKerb:F2} m behind {found} {-kerb.Value - width / 2f:F1} m past the paint{(plate == null ? " (no named plate for " + siteId + ")" : "")}";
            }
            return "no sign (" + (tried.Count == 0 ? "the longest stretch is too short for a spot" : string.Join("; ", tried)) + ")";
        }

        // The sign of a nose-in bay in a lot (CommissarySite.InLot): past the stall's head, the end the truck's nose
        // points to (head +1 is the paint's local +x), on the first step up of 8 cm or more (a kerb) or the first point
        // off the parking and the road that is on the NavMesh (the pavement's edge), within 3 m of the paint's end,
        // PoleBehindKerb further on, its plate facing back along the stall to the aisle. Without either there is no sign:
        // a lot has no kerb beside the paint to put one on.
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
                // The model's plate faces the root's +z; turned a quarter so it faces back along the stall (-head on x).
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

        // What placing the props came to: the log line, and whether to try again later (the street not ready, or a
        // vehicle or the player standing where a prop would go) rather than settle for what fitted.
        public readonly struct Outcome
        {
            public Outcome(string line, bool tryAgain) { Line = line; TryAgain = tryAgain; }
            public string Line { get; }
            public bool TryAgain { get; }
        }

        // Why a prop doesn't fit somewhere; Transient when something that moves (a vehicle, the player) is in the way.
        // Kind groups the refusals of one prop's places for the log (the first place's Why is the example shown); a
        // place blocked by a named collider is its own kind, so each obstacle is named.
        private readonly struct Refusal
        {
            public Refusal(string why, bool transient, string? kind = null) { Why = why; Transient = transient; Kind = kind ?? why; }
            public string Why { get; }
            public bool Transient { get; }
            public string Kind { get; }
        }

        // The props by the door, against the facade on either side of it, each at the first place outwards from the
        // door where it fits: on the NavMesh, touching nothing, clear of every entrance (the commissary's and its
        // neighbours'), not on the road, and with WalkwayMetres of walkable pavement left in front of it. Those that
        // don't fit anywhere within MaxAlongMetres are left out; the log says why, naming what stood there.
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
                // A building surface more than 6 m in front of the door point is something else on the pavement (a kiosk).
                // A building surface narrower than KioskMetres along the street is a newsstand or shelter on the pavement,
                // not the facade (review round 7, Opus). The Buildings layer and its hover twin both count (review round 7,
                // Sol: a building the cursor is over has its facade colliders moved to BuildingsOutlined).
                var rayAlong = new Vector3(outward.z, 0f, -outward.x);
                var hit = hits.FirstOrDefault(h => IsBuilding(h.collider) && h.distance >= laneMetres - 6f && WidthAlong(h.collider, rayAlong) >= KioskMetres);
                // The fallback near the door point never takes a kiosk either: nothing narrower than KioskMetres along
                // the street is a facade (review round 9, Astra: a kiosk the first test refused came back here).
                if (hit.collider == null)
                    hit = hits.FirstOrDefault(h => h.distance >= laneMetres - 3f && h.distance <= laneMetres + 1.5f && WidthAlong(h.collider, rayAlong) >= KioskMetres);
                if (hit.collider == null)
                    // Nothing solid at all: the buildings may still be streaming in, so it is tried again (MaxPropTries
                    // bounds it; review round 7, Opus). Only other things hit: settled.
                    return new Outcome($"no props by the door at x {door.x:F1}, z {door.z:F1}: no building wall within {WallNearDoorMetres:F0} m of the door point, and none on a ray at waist height from the driving lane at " +
                                       $"x {from.x:F1}, z {from.z:F1} back {rayMetres:F0} m, {FacadeBehindDoorMetres:F0} m past the door ({(hits.Count == 0 ? "nothing solid hit" : "hit only " + string.Join(", ", hits.Take(3).Select(h => Describe(h.collider))))})", hits.Count == 0);
                doorWall = hit.collider;
                facade = new Vector3(hit.point.x, door.y, hit.point.z);
                var normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude > 0.5f && Vector3.Angle(normal, outward) < 35f) outward = normal.normalized;
            }
            var along = new Vector3(outward.z, 0f, -outward.x);
            // Every entrance near enough to matter: the commissary's other doors and its neighbours'.
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
            // Props refused only for being off the NavMesh everywhere tried.
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
                    // Long side along the facade.
                    var turn = size.z > size.x ? Quaternion.AngleAxis(90f, Vector3.up) : Quaternion.identity;
                    var rotation = Quaternion.LookRotation(outward, Vector3.up) * turn;
                    var alongSize = Mathf.Max(size.x, size.z);
                    var depth = Mathf.Min(size.x, size.z);
                    // Why each place was refused, by kind: how many, and the first one's words, for the log when the prop
                    // fits nowhere.
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
                            // No wall behind it (past the building's corner, an alley mouth): never a free-standing prop
                            // (review round 1, Opus).
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
            // None on the NavMesh at all: the street's navigation may still be loading.
            if (placed.Count == 0 && skipped.Count > 0 && offNavMesh == skipped.Count) tryAgain = true;
            return new Outcome($"props by the door at x {door.x:F1}, z {door.z:F1} (facade facing {Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg:F0} degrees" +
                               $", {Describe(doorWall!)}, {(near != null ? "the wall at the door" : "met from the street")}, {Vector3.Dot(facade - door, outward):+0.0;-0.0} m from the door point): {(placed.Count == 0 ? "none" : string.Join(", ", placed))}" +
                               (skipped.Count > 0 ? "; left out: " + string.Join(", ", skipped) : ""), tryAgain);
        }

        private const string NotOnNavMesh = "not on the pavement's NavMesh";

        // The wall where a prop goes, in metres out from the door's facade line (+ to the street): rays back towards
        // the building at both ends and the middle of the prop's length, from just above the overlap box's floor
        // (0.15 m, Fits) to waist height; the outermost hit within FacadeShiftMetres (Core PropPlacement.FacadeOffset).
        // Null when nothing usable is hit: no wall there.
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

        // A person, a vehicle or anything pushed by physics: never the street's own furniture.
        private static bool Moving(Collider c) =>
            c.GetComponentInParent<ThirdPersonCharacter>() != null || c.GetComponentInParent<VehicleController>() != null ||
            ((1 << c.gameObject.layer) & (int)LayerHelper.humanLayerMask) != 0 || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic);

        // Never a wall: anything that moves, and the player's own colliders (layer Player; the player's root collider
        // has no ThirdPersonCharacter above it).
        // Parked and traffic cars too, by their layers (review round 7, Opus: on a narrow pavement a parked car could be
        // the first solid surface and read as the facade).
        private static bool NotWall(Collider c)
        {
            var layer = c.gameObject.layer;
            return Moving(c) || layer == PlayerLayer || c.transform.root.gameObject.layer == PlayerLayer ||
                   layer == LayerHelper.ParkedVehiclesLayerIndex || layer == LayerHelper.AiVehiclesLayerIndex || layer == LayerHelper.PlayerVehiclesLayerIndex;
        }

        private static readonly int PlayerLayer = LayerHelper.PlayerLayerIndex;

        // A building's collider: on the Buildings layer or its hover twin, or a part of a city building on the Default
        // layer, where the game's camera-block mode moves the parts between the camera and the player (review round 9,
        // Astra).
        private static bool IsBuilding(Collider c) =>
            ((1 << c.gameObject.layer) & (int)LayerHelper.buildingAndOutlinesLayerMask) != 0 ||
            (c.gameObject.layer == LayerHelper.DefaultLayerIndex && c.GetComponentInParent<CityBuildingController>() != null);

        // A collider's extent along a flat direction, from its world bounds.
        // The nearest building surface within WallNearDoorMetres of a door point that stands outside it, and the flat
        // direction from that surface to the door (the wall's outside). The commissary's own building first, when its
        // colliders can be told apart (under its CityBuildingController), else any building (review round 11, Opus: a
        // neighbour's wall nearer the door point could be taken). Null when none can be told.
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
                // A kiosk or shelter next to the door isn't the wall the door is in.
                if (Mathf.Max(c.bounds.size.x, c.bounds.size.z) < KioskMetres) continue;
                if (c is MeshCollider mesh && !mesh.convex) continue;
                var point = c.ClosestPoint(probe);
                var away = probe - point;
                away.y = 0f;
                // Inside the collider (ClosestPoint gives the point itself) tells nothing.
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
        // How far behind the door point the facade ray looks for the wall.
        private const float FacadeBehindDoorMetres = 8f;

        // What a collider is, for the log: its name, its root object's name and its layer.
        internal static string Describe(Collider c)
        {
            var root = c.transform.root;
            var layer = LayerMask.LayerToName(c.gameObject.layer);
            return $"{c.name}{(root != null && root != c.transform ? " on " + root.name : "")}, layer {(string.IsNullOrEmpty(layer) ? c.gameObject.layer.ToString() : layer)}";
        }

        // Null when the prop fits there, else why not. `ground` is the pavement's height under it, found from the door's
        // height (the facade ray's hit is a metre up) before both NavMesh probes.
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
            // Clear of every entrance: none within DoorClearMetres of the footprint (the facade's own door is already
            // that far away along it).
            var footprint = new Vector2(Mathf.Max(size.x, size.z) / 2f, depth / 2f);
            foreach (var entrance in entrances)
            {
                var local = Quaternion.Inverse(Quaternion.LookRotation(outward, Vector3.up)) * (entrance - at);
                var dx = Mathf.Max(0f, Mathf.Abs(local.x) - footprint.x);
                // An entrance behind the prop (a door point inside its building, as at Lower Manhattan) counts by its
                // distance along the facade alone, so a prop never stands in front of a recessed door.
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
            // Pedestrians walk on and the carving obstacle steers them round; a vehicle or the player standing there is
            // waited for (tried again later); anything else is in the way for good.
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

        // The pavement's height under a point near the door's height: the highest surface within half a metre above it,
        // or below, that isn't a person, a vehicle or one of the props already placed.
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

        // The game's DeliveryPallet with the bottom two layers of its boxes (the prefab stacks four), as plain meshes.
        private static GameObject? GamePallet(Transform parent)
        {
            var copy = CopyGame("DeliveryPallet", parent, t => !t.name.StartsWith("SM_CardboardBox", StringComparison.Ordinal) || t.localPosition.y < 0.7f);
            return copy ?? Copy(Model("BayPalletStack"), parent);
        }

        // The game's hand truck (the player's), standing, as plain meshes.
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

        // A game prefab's visible meshes copied into a new object with a box collider and a carving NavMeshObstacle round
        // them, so no game component (item, vehicle, animator) comes along. Skips the game's placement indicators and
        // anything inactive in the prefab. Null when the prefab or its meshes aren't there.
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
                // Street dirt decals skip it, as they skip every bay piece and the game's vehicles (SurfaceLook.NoStreetDecals).
                partRenderer.renderingLayerMask = BayMarkers.NoDecalsRenderingLayers;
                var b = mesh.bounds;
                var corners = new List<Vector3>();
                for (var c = 0; c < 8; c++)
                    corners.Add(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                foreach (var corner in corners)
                {
                    // The first corner of all starts the bounds; a flat mesh repeats corners, so never compare them.
                    if (first) bounds = new Bounds(corner, Vector3.zero);
                    else bounds.Encapsulate(corner);
                    first = false;
                }
            }
            // Stand it on the ground: its lowest point at the object's origin.
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
