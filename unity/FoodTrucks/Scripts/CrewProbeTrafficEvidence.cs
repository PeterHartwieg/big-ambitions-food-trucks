#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GleyTrafficSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    // On-demand, read-only evidence of native recognition. Compare successive snapshots to
    // observe stopping; membership alone is not proof of braking or a collision-free journey.
    internal static class CrewProbeTrafficEvidence
    {
        // No scene search, ID resolution, physics query or ownership call: all live reads use
        // the exact reference retained by the decisive overlap. IDs below are evidence only.
        internal static JObject InspectObstacle(CrewProbeTraffic? traffic)
        {
            var result = new JObject { ["read_only"] = true, ["complete"] = true,
                ["status"] = traffic == null ? "no_active_or_last_traffic" : "no_observation" };
            if (traffic == null) return result;
            try
            {
                result["native_traffic_id"] = traffic.NativeTrafficId;
                result["observer_disposed"] = traffic.ObserverDisposed;
                result["observation_sequence"] = traffic.ObservationSequence;
                result["physical_local_bounds_at_admission"] = new JObject {
                    ["min"] = Vec(traffic.PhysicalLocalMin), ["max"] = Vec(traffic.PhysicalLocalMax) };
                if (traffic.ObservationSequence == 0) return result;
                result["status"] = traffic.BlockerId.HasValue ? "blocker_captured" : "no_blocker_in_last_observation";
                if (traffic.BlockerId.HasValue)
                    result["observation_snapshot"] = new JObject {
                        ["timing"] = "captured_in_last_Observe; not current geometry",
                        ["collider_instance_id"] = traffic.BlockerId, ["collider_layer"] = traffic.BlockerLayer,
                        ["collider_bounds_center_world"] = Vec(traffic.BlockerPosition),
                        ["query"] = traffic.BlockerQuery, ["gap_m"] = traffic.BlockerGap,
                        ["center_world"] = Vec(traffic.BlockerQueryCenter),
                        ["half_extents"] = Vec(traffic.BlockerQueryHalfExtents),
                        ["rotation_xyzw"] = Quat(traffic.BlockerQueryRotation),
                        ["layer_mask"] = -1, ["query_trigger_interaction"] = "Ignore",
                        ["route_ground_y"] = traffic.BlockerQueryGround, ["support_y"] = traffic.BlockerQuerySupport,
                        ["ground_top_limit_y"] = traffic.BlockerQueryTopLimit,
                        ["ignore_behind_route_m"] = traffic.BlockerQueryIgnoreBehind };

                var live = new JObject { ["timing"] = "read_on_demand; may differ from observation_snapshot" };
                result["live_read"] = live;
                try
                {
                    var car = traffic.EvidenceCar;
                    live["truck"] = car == null ? new JObject { ["status"] = "unavailable_or_destroyed" } :
                        new JObject { ["status"] = "available", ["instance_id"] = car.GetInstanceID(), ["transform"] = Pose(car.transform) };
                    if (car == null) result["complete"] = false;
                }
                catch (Exception ex) { Incomplete(result, live, "truck", ex); }
                if (!traffic.BlockerId.HasValue) return result;
                try
                {
                    var c = traffic.BlockerCollider;
                    var collider = new JObject { ["cached_instance_id"] = traffic.BlockerId,
                        ["status"] = c == null ? "unavailable_or_destroyed" : "available",
                        ["stale_reference"] = c == null };
                    live["collider"] = collider;
                    if (c == null) { result["complete"] = false; return result; }
                    collider["instance_id"] = c.GetInstanceID(); collider["type"] = Text(c.GetType().FullName ?? c.GetType().Name);
                    collider["name"] = Text(c.name); collider["layer"] = c.gameObject.layer;
                    collider["layer_name"] = Text(LayerMask.LayerToName(c.gameObject.layer));
                    collider["enabled"] = c.enabled; collider["is_trigger"] = c.isTrigger;
                    collider["active_in_hierarchy"] = c.gameObject.activeInHierarchy;
                    collider["hierarchy"] = Hierarchy(c.transform, out var truncated);
                    collider["hierarchy_truncated"] = truncated;
                    if (truncated) result["complete"] = false;
                    collider["world_bounds"] = Box(c.bounds); collider["transform"] = Pose(c.transform);
                    var rb = c.attachedRigidbody;
                    collider["attached_rigidbody"] = rb == null ? new JObject { ["status"] = "none" } :
                        new JObject { ["status"] = "available", ["instance_id"] = rb.GetInstanceID(),
                            ["name"] = Text(rb.name), ["is_kinematic"] = rb.isKinematic, ["transform"] = Pose(rb.transform) };
                    collider["authored_shape_local"] = Shape(c);
                }
                catch (Exception ex) { Incomplete(result, live, "collider", ex); }
            }
            catch (Exception ex) { result["complete"] = false; result["status"] = "error"; result["error"] = Text(ex.GetType().Name + ": " + ex.Message); }
            return result;
        }

        private static void Incomplete(JObject result, JObject live, string section, Exception ex)
        {
            result["complete"] = false;
            // Preserve any fields already read, including the cached identity.
            var details = live[section] as JObject;
            if (details == null) { details = new JObject(); live[section] = details; }
            details["status"] = "error"; details["error"] = Text(ex.GetType().Name + ": " + ex.Message);
        }
        private static string Text(string value) => value.Length <= 1024 ? value : value.Substring(0, 1024);
        private static JArray Vec(Vector3 v) => new JArray(v.x, v.y, v.z);
        private static JArray Quat(Quaternion q) => new JArray(q.x, q.y, q.z, q.w);
        private static JObject Box(Bounds b) => new JObject { ["center"] = Vec(b.center), ["size"] = Vec(b.size) };
        private static JObject Pose(Transform t) => new JObject {
            ["position_world"] = Vec(t.position), ["rotation_world_xyzw"] = Quat(t.rotation),
            ["lossy_scale"] = Vec(t.lossyScale), ["position_local"] = Vec(t.localPosition),
            ["rotation_local_xyzw"] = Quat(t.localRotation), ["scale_local"] = Vec(t.localScale) };

        private static string Hierarchy(Transform t, out bool truncated)
        {
            var path = ""; var count = 0; truncated = false;
            for (var p = t; p != null; p = p.parent)
            {
                if (count++ >= 64) { truncated = true; break; }
                var available = 1024 - path.Length - (path.Length == 0 ? 0 : 1);
                var name = p.name;
                if (name.Length > available) { truncated = true; break; }
                path = path.Length == 0 ? name : name + "/" + path;
            }
            return path;
        }

        private static JObject Shape(Collider c)
        {
            if (c is BoxCollider box) return new JObject { ["kind"] = "box", ["center"] = Vec(box.center), ["size"] = Vec(box.size) };
            if (c is SphereCollider sphere) return new JObject { ["kind"] = "sphere", ["center"] = Vec(sphere.center), ["radius"] = sphere.radius };
            if (c is CapsuleCollider capsule) return new JObject { ["kind"] = "capsule", ["center"] = Vec(capsule.center),
                ["radius"] = capsule.radius, ["height"] = capsule.height, ["direction_axis"] = capsule.direction };
            if (c is MeshCollider mesh)
            {
                var asset = mesh.sharedMesh;
                return new JObject { ["kind"] = "mesh", ["convex"] = mesh.convex,
                    ["mesh_status"] = asset == null ? "unavailable" : "available",
                    ["mesh_name"] = asset == null ? null : Text(asset.name),
                    ["mesh_instance_id"] = asset == null ? (int?)null : asset.GetInstanceID(),
                    ["mesh_bounds_local"] = asset == null ? null : Box(asset.bounds) };
            }
            return new JObject { ["kind"] = "unsupported", ["type"] = Text(c.GetType().Name) };
        }

        internal static JObject Inspect(CarController car)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var result = new JObject { ["read_only"] = true, ["id"] = car.vehicleInstance.id };
            var rows = new JArray(); result["nearby"] = rows;
            try
            {
                if (!TrafficManager.HasInstance || !TrafficManager.IsInitialized) throw new InvalidOperationException("Gley unavailable.");
                var field = typeof(TrafficManager).GetField("trafficVehicles", flags);
                var obstacles = typeof(VehicleComponent).GetField("obstacleList", flags);
                var pool = (field?.GetValue(TrafficManager.Instance) as TrafficVehicles)?.GetVehicleList();
                if (pool == null || pool.Count > 1024 || obstacles == null) throw new InvalidOperationException("Bounded native bindings unavailable.");
                foreach (var v in pool)
                {
                    if (v == null || !v.gameObject.activeInHierarchy) continue;
                    var distance = (v.transform.position - car.transform.position).magnitude;
                    if (distance > 80) continue;
                    if (rows.Count >= 128) throw new InvalidOperationException("Nearby traffic evidence limit reached.");
                    var list = obstacles.GetValue(v) as List<Collider>;
                    if (list == null || list.Count > 128 || v.rb == null) throw new InvalidOperationException("Native obstacle inventory unavailable/unbounded.");
                    var body = car.vehicleCollider != null && list.Contains(car.vehicleCollider);
                    var extra = car.additionalCollider != null && list.Contains(car.additionalCollider);
                    var p = v.transform.position; var forward = v.transform.forward;
                    rows.Add(new JObject {
                        ["instance_id"] = v.GetInstanceID(), ["native_index"] = v.GetIndex(),
                        ["position"] = new JArray(p.x, p.y, p.z), ["forward"] = new JArray(forward.x, forward.y, forward.z),
                        ["distance_m"] = distance, ["speed_ms"] = v.rb.velocity.magnitude,
                        ["recognizes_vehicle_collider"] = body, ["recognizes_additional_collider"] = extra,
                        ["native_obstacle_count"] = list.Count, ["native_action"] = v.GetCurrentAction().ToString() });
                }
                result["complete"] = true;
            }
            catch (Exception ex) { result["complete"] = false; result["error"] = ex.Message; }
            return result;
        }
    }
}
