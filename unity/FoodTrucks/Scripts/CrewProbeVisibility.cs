#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using GleyTrafficSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    // One-off main-thread diagnostic. Only reads existing components, masks and physics settings.
    // Deliberately independent of probe ownership, geometry/admission and engine adapters.
    internal static class CrewProbeVisibility
    {
        private const int MaxColliders = 128, MaxPool = 1024, MaxTriggers = 16;
        private const int MaxNodes = 4096, MaxDepth = 64, MaxWheels = 64;

        internal static JObject Inspect(CarController car)
        {
            if (car == null) throw new ArgumentNullException(nameof(car));
            var errors = new JArray();
            var report = new JObject {
                ["id"] = car.vehicleInstance?.id, ["controller_instance"] = car.GetInstanceID(),
                ["root_name"] = Short(car.name), ["root_layer"] = car.gameObject.layer,
                ["root_layer_name"] = LayerMask.LayerToName(car.gameObject.layer),
                ["root_world_position"] = WorldPosition(car.transform),
                ["read_only"] = true, ["errors"] = errors,
                ["limits"] = new JObject { ["colliders"] = MaxColliders, ["pool"] = MaxPool,
                    ["front_colliders_per_model"] = MaxTriggers, ["hierarchy_nodes"] = MaxNodes,
                    ["hierarchy_depth"] = MaxDepth, ["wheels"] = MaxWheels },
                ["rule_lookup"] = "Each collider's rule_index selects gley.groups[].rules. " +
                    "Each front collider's matrix_index selects gley.collision_matrix[], whose matrix_allows uses the same rule_index. " +
                    "Rules are layer precedence only: own-layer > building > obstacle > player > none. " +
                    "own-layer dereferences attachedRigidbody; see own_layer_rigidbody_check. " +
                    "Delivery also requires an enabled active trigger and collider. " +
                    "Native ignores target triggers unless tagged AiVehicleHalt. Layer overrides and Rigidbody state are observations only; " +
                    "matrix_allows reports the global layer matrix, without resolving per-body/collider overrides. " +
                    "No spatial overlap, effective collision delivery, visibility, admission or actual traffic stopping is inferred." };

            var colliders = Collect(car.transform, MaxColliders, out var truncated, out var nodes);
            report["colliders_truncated"] = truncated;
            report["hierarchy_nodes_visited"] = nodes.Count;
            var parking = new HashSet<Collider>();
            foreach (var node in nodes)
                foreach (var helper in node.GetComponents<VehicleParkingHelper>())
                    if (helper.parallelParkingCollider != null) parking.Add(helper.parallelParkingCollider);
            var wheelRoots = new List<Transform>();
            var wheelMeshes = new Dictionary<Collider, string>();
            var wheelsKnown = false;
            try
            {
                var wheels = car.vehicleController?.powertrain?.wheels;
                if (wheels == null) throw new InvalidOperationException("Native wheel list unavailable.");
                var count = 0;
                foreach (var wheel in wheels)
                {
                    if (++count > MaxWheels) throw new InvalidOperationException("Native wheel list exceeds limit.");
                    if (wheel?.wheelUAPI != null) wheelRoots.Add(wheel.wheelUAPI.transform);
                    if (wheel?.wheelUAPI is NWH.WheelController3D.WheelController controller && controller.wheel != null)
                    {
                        if (controller.wheel.topMeshCollider != null) wheelMeshes[controller.wheel.topMeshCollider] = $"wheel[{count - 1}].topMeshCollider";
                        if (controller.wheel.bottomMeshCollider != null) wheelMeshes[controller.wheel.bottomMeshCollider] = $"wheel[{count - 1}].bottomMeshCollider";
                    }
                }
                wheelsKnown = true;
            }
            catch (Exception ex) { Error(errors, "wheels", ex); }

            var layers = new List<int>();
            var inventory = new JArray();
            foreach (var c in colliders)
            {
                var layer = c.gameObject.layer;
                if (!layers.Contains(layer)) layers.Add(layer);
                var item = new JObject {
                    ["path"] = Path(c.transform, car.transform, out var pathTruncated),
                    ["path_truncated"] = pathTruncated, ["type"] = c.GetType().Name,
                    ["instance_id"] = c.GetInstanceID(), ["layer"] = layer,
                    ["layer_name"] = LayerMask.LayerToName(layer), ["rule_index"] = layers.IndexOf(layer),
                    ["enabled"] = c.enabled, ["active_self"] = c.gameObject.activeSelf,
                    ["active_in_hierarchy"] = c.gameObject.activeInHierarchy, ["is_trigger"] = c.isTrigger,
                    ["native_ignores_target_trigger"] = c.isTrigger && !c.CompareTag("AiVehicleHalt"),
                    ["attached_rigidbody"] = c.attachedRigidbody != null,
                    ["rigidbody_instance_id"] = c.attachedRigidbody == null ? (int?)null : c.attachedRigidbody.GetInstanceID(),
                    ["own_layer_rigidbody_check"] = c.attachedRigidbody == null ? "missing: native own-layer dereference would fail" : "present",
                    ["vehicle_collider"] = c == car.vehicleCollider,
                    ["additional_collider"] = c == car.additionalCollider,
                    ["parallel_parking_collider"] = parking.Contains(c),
                    ["under_native_wheel"] = wheelsKnown ? (bool?)UnderWheel(c.transform, wheelRoots) : null,
                    ["native_wheel_mesh_reference"] = wheelMeshes.TryGetValue(c, out var meshRole) ? meshRole : null };
                item["collider_layer_overrides"] = LayerOverrides(c);
                item["rigidbody_state"] = RigidbodyState(c);
                item["world_bounds"] = WorldBounds(c);
                try { item["bounds"] = LocalBounds(c, car.transform); }
                catch (Exception ex) { item["bounds"] = new JObject { ["known"] = false, ["reason"] = Short(ex.Message) }; }
                if (c is MeshCollider mesh)
                {
                    item["mesh_name"] = mesh.sharedMesh == null ? null : Short(mesh.sharedMesh.name);
                    item["mesh_convex"] = mesh.convex;
                    item["mesh_vertex_count"] = mesh.sharedMesh == null ? (int?)null : mesh.sharedMesh.vertexCount;
                    item["mesh_readable"] = mesh.sharedMesh == null ? (bool?)null : mesh.sharedMesh.isReadable;
                }
                inventory.Add(item);
            }
            report["wheel_mesh_references"] = new JArray(wheelMeshes.Select(pair => new JObject {
                ["role"] = pair.Value, ["instance_id"] = pair.Key.GetInstanceID(), ["in_inventory"] = colliders.Contains(pair.Key) }));
            report["colliders"] = inventory;
            report["collider_count"] = inventory.Count;
            report["collider_layers"] = new JArray(layers);
            // Missing/out-of-tree role references must not masquerade as a complete body inventory.
            report["native_role_references"] = new JObject {
                ["vehicle_collider"] = Reference(car.vehicleCollider, colliders),
                ["additional_collider"] = Reference(car.additionalCollider, colliders) };
            var parkingRefs = new JArray();
            foreach (var c in parking) parkingRefs.Add(Reference(c, colliders));
            report["parking_collider_references"] = parkingRefs;
            report["gley"] = Gley(layers);
            return report;
        }

        private static JObject Reference(Collider? c, List<Collider> inventory) => new JObject {
            ["instance_id"] = c == null ? (int?)null : c.GetInstanceID(),
            ["in_inventory"] = c != null && inventory.Contains(c) };

        // Bounded depth-first traversal includes inactive objects and every Collider component.
        // Does not allocate the entire truck/pool hierarchy via GetComponentsInChildren.
        private static List<Collider> Collect(Transform root, int limit, out bool truncated, out List<Transform> visited)
        {
            var result = new List<Collider>();
            visited = new List<Transform>();
            truncated = false;
            var stack = new Stack<(Transform Node, int Depth, int NextChild)>();
            stack.Push((root, 0, -1));
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                if (frame.NextChild == -1)
                {
                    if (visited.Count >= MaxNodes) { truncated = true; break; }
                    visited.Add(frame.Node);
                    foreach (var c in frame.Node.GetComponents<Collider>())
                    {
                        if (result.Count == limit) { truncated = true; return result; }
                        result.Add(c);
                    }
                    frame.NextChild = 0;
                }
                if (frame.NextChild >= frame.Node.childCount) continue;
                if (frame.Depth >= MaxDepth) { truncated = true; continue; }
                var child = frame.Node.GetChild(frame.NextChild++);
                stack.Push(frame);
                stack.Push((child, frame.Depth + 1, -1));
            }
            return result;
        }

        private static bool UnderWheel(Transform node, List<Transform> roots)
        {
            for (var depth = 0; node != null && depth <= MaxDepth; depth++, node = node.parent)
                if (roots.Contains(node)) return true;
            return false;
        }

        private static string Path(Transform node, Transform root, out bool truncated)
        {
            var parts = new List<string>();
            truncated = false;
            var length = 0;
            for (var depth = 0; node != root; depth++, node = node.parent)
            {
                if (node == null || depth >= MaxDepth || length >= 2048) { truncated = true; break; }
                var part = Short(node.name) + "[" + node.GetSiblingIndex() + "]";
                parts.Add(part); length += part.Length + 1;
            }
            parts.Reverse();
            return (truncated ? "…/" : "./") + string.Join("/", parts);
        }

        private static JObject LocalBounds(Collider c, Transform root)
        {
            if (!CrewProbeShape.TryLocalBounds(c, out var shape) || !CrewProbeShape.TryBounds(c, root, out var min, out var max))
                return new JObject { ["known"] = false,
                    ["reason"] = "Unsupported shape, missing mesh, invalid dimensions or non-finite/degenerate transformed bounds; no world-AABB fallback." };
            return new JObject { ["known"] = true, ["source"] = "shape-local bounds corners transformed to car-local AABB (enclosure)",
                ["shape_local_center"] = Vec(shape.center), ["shape_local_size"] = Vec(shape.size),
                ["car_local_min"] = Vec(min), ["car_local_max"] = Vec(max) };
        }

        private static JObject LayerOverrides(Collider c)
        {
            try
            {
                return new JObject { ["known"] = true, ["include_layers"] = c.includeLayers.value,
                    ["exclude_layers"] = c.excludeLayers.value, ["layer_override_priority"] = c.layerOverridePriority };
            }
            catch (Exception ex) { return Unknown(ex.Message); }
        }

        private static JObject RigidbodyState(Collider c)
        {
            bool? present = null;
            try
            {
                var rb = c.attachedRigidbody;
                present = rb != null;
                if (rb == null) return new JObject { ["present"] = false, ["known"] = false, ["reason"] = "No attached Rigidbody." };
                return new JObject { ["present"] = true, ["known"] = true,
                    ["include_layers"] = rb.includeLayers.value, ["exclude_layers"] = rb.excludeLayers.value,
                    ["detect_collisions"] = rb.detectCollisions, ["is_kinematic"] = rb.isKinematic };
            }
            catch (Exception ex)
            {
                var result = Unknown(ex.Message); result["present"] = present; return result;
            }
        }

        private static JObject WorldPosition(Transform? root)
        {
            if (root == null) return Unknown("Model root absent.");
            try
            {
                var position = root.position;
                return Finite(position) ? new JObject { ["known"] = true, ["position"] = Vec(position) } : Unknown("Non-finite root world position.");
            }
            catch (Exception ex) { return Unknown(ex.Message); }
        }

        private static JObject WorldBounds(Collider c)
        {
            try
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy)
                    return Unknown("Collider disabled or inactive; world bounds not read.");
                var bounds = c.bounds;
                if (!Finite(bounds.center) || !Finite(bounds.size) || !Finite(bounds.min) || !Finite(bounds.max) ||
                    bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                    return Unknown("Non-finite or non-positive world bounds.");
                return new JObject { ["known"] = true, ["center"] = Vec(bounds.center), ["size"] = Vec(bounds.size),
                    ["min"] = Vec(bounds.min), ["max"] = Vec(bounds.max) };
            }
            catch (Exception ex) { return Unknown(ex.Message); }
        }

        private static JObject Unknown(string reason) => new JObject { ["known"] = false, ["reason"] = Short(reason) };

        private static JObject Gley(List<int> layers)
        {
            var errors = new JArray();
            var groups = new JArray();
            var models = new JArray();
            var matrix = new JArray();
            var matrixByLayer = new Dictionary<int, int>();
            var result = new JObject { ["complete"] = false, ["errors"] = errors, ["groups"] = groups,
                ["collision_matrix"] = matrix, ["models"] = models };
            try
            {
                if (!TrafficManager.HasInstance || !TrafficManager.IsInitialized)
                    throw new InvalidOperationException("Existing Gley pool is not initialized; no instance was requested.");
                var manager = TrafficManager.Instance;
                var pool = (Read(manager, "trafficVehicles") as TrafficVehicles)?.GetVehicleList();
                if (pool == null) throw new InvalidOperationException("Existing Gley pool unavailable.");
                result["pool_count"] = pool.Count;
                if (pool.Count == 0 || pool.Count > MaxPool) throw new InvalidOperationException("Gley pool outside 1..1024; inventory refused.");
                var byKey = new Dictionary<string, JObject>();
                var failures = 0;
                for (var i = 0; i < pool.Count; i++)
                {
                    try
                    {
                        var model = pool[i];
                        var modelReport = new JObject { ["pool_index"] = i,
                            ["instance_id"] = model == null ? (int?)null : model.GetInstanceID(),
                            ["root_world_position"] = WorldPosition(model == null ? null : model.transform), ["group_id"] = null };
                        models.Add(modelReport);
                        if (model == null || model.frontTrigger == null) throw new InvalidOperationException("Uninitialized model/front trigger.");
                        var triggers = Collect(model.frontTrigger, MaxTriggers, out var truncated, out _);
                        if (truncated || triggers.Count == 0) throw new InvalidOperationException("Front collider inventory absent or exceeds hierarchy/16-collider limit.");
                        var obstacles = ((LayerMask)Read(model, "obstacleLayers")).value;
                        var players = ((LayerMask)Read(model, "playerLayers")).value;
                        var buildings = ((LayerMask)Read(model, "buildingLayers")).value;
                        var rules = new JArray();
                        foreach (var layer in layers) rules.Add(Rule(layer, model.gameObject.layer, buildings, obstacles, players));
                        var triggerGroups = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
                        var triggerCount = 0;
                        var instances = new JArray(); modelReport["trigger_instances"] = instances;
                        foreach (var t in triggers)
                        {
                            if (t.isTrigger) triggerCount++;
                            var triggerLayer = t.gameObject.layer;
                            if (!matrixByLayer.TryGetValue(triggerLayer, out var matrixIndex))
                            {
                                var allows = new JArray();
                                foreach (var layer in layers) allows.Add(!Physics.GetIgnoreLayerCollision(triggerLayer, layer));
                                matrixIndex = matrix.Count;
                                matrixByLayer.Add(triggerLayer, matrixIndex);
                                matrix.Add(new JObject { ["trigger_layer"] = triggerLayer, ["matrix_allows"] = allows });
                            }
                            var rotation = t.transform.rotation;
                            var up = rotation * new Vector3(0, 1, 0);
                            instances.Add(new JObject { ["collider_id"] = t.GetInstanceID(), ["collider_path"] = Path(t.transform, model.transform, out _),
                                ["enabled"] = t.enabled, ["active_in_hierarchy"] = t.gameObject.activeInHierarchy,
                                ["world_rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w),
                                ["world_up"] = Vec(up), ["world_tilt"] = Math.Atan2(Math.Sqrt(up.x * up.x + up.z * up.z), up.y) });
                            var entry = new JObject { ["layer"] = t.gameObject.layer,
                                ["layer_name"] = LayerMask.LayerToName(t.gameObject.layer), ["is_trigger"] = t.isTrigger,
                                ["enabled"] = t.enabled, ["active_in_hierarchy"] = t.gameObject.activeInHierarchy,
                                ["matrix_index"] = matrixIndex, ["model_local_bounds"] = LocalBounds(t, model.transform),
                                ["collider_layer_overrides"] = LayerOverrides(t), ["rigidbody_state"] = RigidbodyState(t) };
                            var key = entry.ToString(Formatting.None);
                            if (!triggerGroups.TryGetValue(key, out var existing)) { entry["count"] = 1; triggerGroups.Add(key, entry); }
                            else existing["count"] = (int)existing["count"]! + 1;
                        }
                        if (triggerCount == 0) throw new InvalidOperationException("Front hierarchy has no trigger collider.");
                        var group = new JObject { ["root_layer"] = model.gameObject.layer,
                            ["root_layer_name"] = LayerMask.LayerToName(model.gameObject.layer),
                            ["obstacle_mask"] = obstacles, ["player_mask"] = players, ["building_mask"] = buildings,
                            ["rules"] = rules, ["triggers"] = new JArray(triggerGroups.Values) };
                        var groupKey = group.ToString(Formatting.None);
                        if (byKey.TryGetValue(groupKey, out var found))
                        {
                            found["model_count"] = (int)found["model_count"]! + 1;
                            modelReport["group_id"] = (int)found["group_id"]!;
                        }
                        else
                        {
                            byKey.Add(groupKey, group);
                            group["group_id"] = groups.Count; group["model_count"] = 1;
                            group["sample_pool_index"] = i; group["sample_model_name"] = Short(model.name);
                            group["sample_instance_id"] = model.GetInstanceID();
                            groups.Add(group);
                            modelReport["group_id"] = (int)group["group_id"]!;
                        }
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        if (errors.Count < 32) errors.Add(new JObject { ["pool_index"] = i, ["message"] = Short(ex.Message) });
                    }
                }
                result["failed_models"] = failures;
                result["errors_truncated"] = failures > errors.Count;
                result["complete"] = failures == 0;
            }
            catch (Exception ex) { Error(errors, "pool", ex); }
            return result;
        }

        private static object Read(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target) ?? throw new InvalidOperationException(name + " is null.");
            }
            throw new InvalidOperationException("Read-only field binding missing: " + name);
        }

        internal static string Rule(int layer, int root, int buildings, int obstacles, int players) => layer == root ? "own-layer" :
            (buildings & (1 << layer)) != 0 ? "building" : (obstacles & (1 << layer)) != 0 ? "obstacle" :
            (players & (1 << layer)) != 0 ? "player" : "none";
        private static string Short(string value) => value.Length <= 160 ? value : value.Substring(0, 160) + "…";
        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) ||
            float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));
        private static JObject Vec(Vector3 v) => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };
        private static void Error(JArray errors, string section, Exception ex) => errors.Add(new JObject { ["section"] = section, ["message"] = Short(ex.Message) });
    }
}
