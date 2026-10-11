#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        // road (and readable from the pavement). The kerb is found by walking out from the paint's pavement edge until
        // the ground steps up; with no kerb within 3 m there is no sign (a pole in the road would be worse).
        public static string Sign(Transform root, string siteId, (float From, float To) longest, float width, Func<Vector3, float> groundAt, float laneY)
        {
            var model = Model("BaySign");
            if (model == null) return "no sign (model missing)";
            var middle = (longest.From + longest.To) / 2f;
            foreach (var shift in new[] { 0f, 1.5f, -1.5f, 3f, -3f })
            {
                var x = middle + shift;
                if (x < longest.From || x > longest.To) continue;
                float? kerb = null;
                for (var z = -width / 2f; z >= -width / 2f - 3f; z -= 0.1f)
                {
                    var ground = groundAt(root.TransformPoint(new Vector3(x, 0f, z)));
                    if (ground - laneY >= 0.08f) { kerb = z; break; }
                }
                if (kerb == null) continue;
                var local = new Vector3(x, 0f, kerb.Value - PoleBehindKerb);
                var at = root.TransformPoint(local);
                at.y = groundAt(at);
                // Room for the pole: nothing solid within 0.25 m of it up to head height.
                if (Physics.CheckCapsule(at + Vector3.up * 0.3f, at + Vector3.up * 2.0f, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                var sign = Object.Instantiate(model, root, false);
                sign.name = "LoadingZoneSign";
                sign.transform.position = at;
                sign.transform.localRotation = Quaternion.identity;
                var plate = Material("M_BaySign_" + siteId);
                if (plate != null)
                    foreach (var renderer in sign.GetComponentsInChildren<MeshRenderer>(true))
                        if (renderer.name.EndsWith("Plate", StringComparison.Ordinal)) renderer.sharedMaterial = plate;
                return $"sign at x {at.x:F1}, z {at.z:F1}, {PoleBehindKerb:F2} m behind a kerb {-kerb.Value - width / 2f:F1} m past the paint{(plate == null ? " (no named plate for " + siteId + ")" : "")}";
            }
            return "no sign (no kerb within 3 m of the paint's pavement edge, or no room for the pole)";
        }

        private sealed class Prop
        {
            public Prop(string name, Func<Transform, GameObject?> make) { Name = name; Make = make; }
            public string Name { get; }
            public Func<Transform, GameObject?> Make { get; }
        }

        // The props by the door, against the facade on either side of it. Each is placed only where it fits: on the
        // NavMesh, touching nothing, clear of the door, not on the road, and with WalkwayMetres of walkable pavement left
        // in front of it. Those that don't fit are left out; the log says which and why.
        public static string ByTheDoor(Transform parent, Vector3 door, TrafficLanes lanes)
        {
            var nearest = lanes.Nearest(door);
            if (nearest == null) return "no props (no driving lane near the door to tell the street from the building)";
            var outward = nearest.Value.Point - door;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) return "no props (the door stands on a driving lane)";
            outward.Normalize();
            // The facade: back along the outward line from the pavement, at waist height.
            var from = door + outward * 3f + Vector3.up * 1.0f;
            var facade = Physics.Raycast(from, -outward, out var hit, 4.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point : door;
            if (hit.collider != null)
            {
                var normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude > 0.5f && Vector3.Angle(normal, outward) < 35f) outward = normal.normalized;
            }
            var along = new Vector3(outward.z, 0f, -outward.x);
            var props = new[]
            {
                new Prop("pallet", GamePallet),
                new Prop("roll cage", p => Copy(Model("BayRollCage"), p)),
                new Prop("hand truck", GameHandTruck),
                new Prop("dumpster", p => Copy(Model("BayDumpster"), p)),
            };
            var placed = new List<string>();
            var skipped = new List<string>();
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
                    string? why = null;
                    var ok = false;
                    foreach (var side in new[] { 1, -1 })
                    {
                        var s = side * (reach[side] + alongSize / 2f);
                        var at = facade + outward * (depth / 2f + 0.12f) + along * s;
                        why = Fits(at, rotation, centre, size, outward, depth, lanes, out var ground);
                        if (why != null) continue;
                        at.y = ground;
                        made.transform.SetPositionAndRotation(at - rotation * new Vector3(centre.x, 0f, centre.z), rotation);
                        reach[side] += alongSize + 0.35f;
                        ok = true;
                        placed.Add($"{prop.Name} {s:+0.0;-0.0} m along the facade");
                        break;
                    }
                    if (!ok)
                    {
                        skipped.Add($"{prop.Name} ({why})");
                        Object.Destroy(made);
                        continue;
                    }
                    made.SetActive(true);
                }
                catch (Exception ex)
                {
                    if (made != null) Object.Destroy(made);
                    skipped.Add($"{prop.Name} ({ex.GetType().Name}: {ex.Message})");
                }
            }
            return $"props by the door at x {door.x:F1}, z {door.z:F1}: {(placed.Count == 0 ? "none" : string.Join(", ", placed))}" +
                   (skipped.Count > 0 ? "; left out: " + string.Join(", ", skipped) : "");
        }

        // Null when the prop fits there, else why not. `ground` is the pavement's height under it.
        private static string? Fits(Vector3 at, Quaternion rotation, Vector3 centre, Vector3 size, Vector3 outward, float depth, TrafficLanes lanes, out float ground)
        {
            ground = at.y;
            if (!NavMesh.SamplePosition(at, out var nav, 0.6f, NavMesh.AllAreas)) return "not on the pavement's NavMesh";
            ground = nav.position.y;
            var road = lanes.Distance(at);
            if (road != null && road.Value < RoadBandMetres + depth / 2f) return $"{road.Value:F1} m from a driving lane";
            var front = at + outward * (depth / 2f + WalkwayMetres);
            if (!NavMesh.SamplePosition(front, out var walk, 0.4f, NavMesh.AllAreas) || Mathf.Abs(walk.position.y - ground) > 0.3f)
                return "no walkway left in front";
            var halves = size / 2f - new Vector3(0.03f, 0f, 0.03f);
            halves.y = Mathf.Max(0.05f, halves.y - 0.1f);
            var middle = new Vector3(at.x, ground + 0.15f + halves.y, at.z);
            if (Physics.CheckBox(middle, halves, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return "something stands there";
            return null;
        }

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
            for (var i = 0; i < parts.Count; i++)
            {
                var (mesh, mats, m) = parts[i];
                var part = new GameObject(mesh.name);
                part.transform.SetParent(made.transform, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = mats;
                var b = mesh.bounds;
                var corners = new List<Vector3>();
                for (var c = 0; c < 8; c++)
                    corners.Add(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                foreach (var corner in corners)
                {
                    if (i == 0 && corner == corners[0]) bounds = new Bounds(corner, Vector3.zero);
                    else bounds.Encapsulate(corner);
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
