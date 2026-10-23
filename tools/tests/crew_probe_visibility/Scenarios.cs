#nullable enable
using System;
using System.Linq;
using FoodTrucks;
using GleyTrafficSystem;
using Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class Scenarios
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GameObject Child(Transform parent, string name, int layer = 12)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.parent = parent; parent.Children.Add(go.transform); return go;
    }
    private static CarController Truck()
    {
        var go = new GameObject("TruckRoot") { layer = 0 };
        var car = go.Add<CarController>();
        var rb = go.Add<Rigidbody>();
        var body = Child(go.transform, "BodyCollider").Add<MeshCollider>();
        body.sharedMesh = new Mesh { name = "truck-body", bounds = new Bounds(new Vector3(0, 1, 0), new Vector3(2, 2, 6)) };
        body.convex = true; body.attachedRigidbody = rb; car.vehicleCollider = body;
        var extra = Child(go.transform, "Additional").Add<BoxCollider>();
        extra.attachedRigidbody = rb; car.additionalCollider = extra;
        var wheel = Child(go.transform, "Front_WheelController", 0);
        var controller = wheel.Add<NWH.WheelController3D.WheelController>();
        car.vehicleController!.powertrain!.wheels!.Add(new Wheel { wheelUAPI = controller });
        var generated = Child(wheel.transform, "GeneratedWheel", 2).Add<MeshCollider>();
        generated.sharedMesh = new Mesh { name = "invisible-wheel", bounds = new Bounds(new Vector3(), Vector3.one) };
        generated.attachedRigidbody = rb;
        controller.wheel.topMeshCollider = generated;
        var parking = Child(go.transform, "ParkingProxy").Add<BoxCollider>();
        parking.enabled = false; parking.isTrigger = true;
        go.Add<VehicleParkingHelper>().parallelParkingCollider = parking;
        var disabled = Child(go.transform, "Inactive", 6);
        disabled.activeSelf = false;
        disabled.Add<SphereCollider>().radius = .5f;
        disabled.Add<BoxCollider>().enabled = false; // Two components on one path must survive.
        var trigger = Child(go.transform, "Trigger", 9).Add<BoxCollider>();
        trigger.isTrigger = true;
        SaveGameManager.Current.VehicleInstances.Add(car.vehicleInstance!); VehicleHelper.Car = car;
        return car;
    }
    private static VehicleComponent Model(int root = 9, int triggerLayer = 8)
    {
        var go = new GameObject("TrafficModel") { layer = root };
        var model = go.Add<VehicleComponent>();
        var front = Child(go.transform, "Front", triggerLayer);
        var trigger = front.Add<BoxCollider>(); trigger.isTrigger = true;
        model.frontTrigger = front.transform;
        model.Masks((1 << 12) | (1 << 6), (1 << 12) | (1 << 6), 1 << 6);
        TrafficManager.Instance.Pool.Models.Add(model); return model;
    }
    private static JObject Inspect(CarController car)
    {
        Writes.Count = 0;
        var result = CrewProbeVisibility.Inspect(car);
        Check(Writes.Count == 0, "Inspector changed native state"); return result;
    }
    private static JObject Named(JObject report, string name) => report["colliders"]!.OfType<JObject>().Single(c => ((string)c["path"]!).Contains(name));
    private static void Near(float expected, JToken actual, string reason) => Check(Math.Abs(expected - (float)actual) < .0001, reason + ": " + actual);
    private static void Refused(JObject args)
    {
        try { TestBridgeCommands.Invoke(args); throw new Exception("Command should refuse"); }
        catch (BridgeError) { }
    }
    public static void Main(string[] args)
    {
        var car = Truck(); var model = Model();
        switch (args[0])
        {
            case "inventory":
            {
                var r = Inspect(car);
                Check((string)r["id"]! == "owned" && (string)r["root_name"]! == "TruckRoot" && (int)r["root_layer"]! == 0, "Root/native identity");
                Check((int)r["collider_count"]! == 7 && !(bool)r["colliders_truncated"]!, "All seven colliders");
                var body = Named(r, "BodyCollider");
                Check((bool)body["vehicle_collider"]! && !(bool)body["additional_collider"]!, "Body exact role");
                Check((string)body["mesh_name"]! == "truck-body" && (bool)body["mesh_convex"]!, "Mesh metadata");
                Check((bool)Named(r, "Additional")["additional_collider"]!, "Additional role");
                Check(!(bool)Named(r, "GeneratedWheel")["vehicle_collider"]! && (bool)Named(r, "GeneratedWheel")["under_native_wheel"]!, "Invisible generated wheel identity");
                Check((string)Named(r, "GeneratedWheel")["native_wheel_mesh_reference"]! == "wheel[0].topMeshCollider", "Exact NWH mesh reference, separate from ancestry");
                Check((string?)Named(r, "BodyCollider")["native_wheel_mesh_reference"] == null, "Body is not a native wheel mesh");
                var park = Named(r, "ParkingProxy");
                Check((bool)park["parallel_parking_collider"]! && !(bool)park["enabled"]! && (bool)park["is_trigger"]!, "Parking disabled trigger");
                Check(r["colliders"]!.Count(c => ((string)c["path"]!).Contains("Inactive")) == 2, "All disabled/inactive components");
                Check(r["colliders"]!.Select(c => (int)c["instance_id"]!).Distinct().Count() == 7, "Unique component IDs");
                Check((bool)Named(r, "Trigger")["native_ignores_target_trigger"]!, "Native target-trigger filter");
                break;
            }
            case "rules":
            {
                Model(); // Identical pooled models collapse into one group.
                var other = Model(12, 10);
                other.Masks(1 << 12, 1 << 12, 1 << 12); // own-layer beats building.
                Physics.Ignored.Add((8, 12));
                var r = Inspect(car);
                var groups = (JArray)r["gley"]!["groups"]!;
                Check((bool)groups[0]["triggers"]![0]!["model_local_bounds"]!["known"]!, "Front trigger shape bounds reported relative to model root");
                Check(groups.Count == 2 && (int)groups[0]["model_count"]! == 2, "Group masks/root/trigger settings");
                var models = (JArray)r["gley"]!["models"]!;
                var exact = models.SelectMany(m => (JArray)m["trigger_instances"]!).ToArray();
                Check(exact.Length == 3 && exact.Select(t => (int)t["collider_id"]!).Distinct().Count() == 3, "Exact trigger identities survive grouping");
                Check(exact.All(t => t["world_rotation"] is JArray q && q.Count == 4 && t["world_up"]?["y"] != null && t["world_tilt"] != null), "Exact trigger world orientation");
                var bodyIndex = (int)Named(r, "BodyCollider")["rule_index"]!;
                var inactiveIndex = (int)r["colliders"]!.First(c => ((string)c["path"]!).Contains("Inactive"))["rule_index"]!;
                var wheelIndex = (int)Named(r, "GeneratedWheel")["rule_index"]!;
                Check((string)groups[0]["rules"]![bodyIndex]! == "obstacle", "Obstacle before player");
                Check((string)groups[0]["rules"]![inactiveIndex]! == "building", "Building before obstacle/player");
                Check((string)groups[0]["rules"]![wheelIndex]! == "none", "Invisible layer");
                var matrices = r["gley"]!["collision_matrix"]!;
                var firstMatrix = (int)groups[0]["triggers"]![0]!["matrix_index"]!;
                var secondMatrix = (int)groups[1]["triggers"]![0]!["matrix_index"]!;
                Check(!(bool)matrices[firstMatrix]!["matrix_allows"]![bodyIndex]!, "Collision matrix overrides mask recognition");
                Check((string)groups[1]["rules"]![bodyIndex]! == "own-layer", "Own-layer precedence");
                Check((bool)matrices[secondMatrix]!["matrix_allows"]![bodyIndex]!, "Independent trigger layer");
                Check(CrewProbeVisibility.Rule(12, 9, 0, 0, 1 << 12) == "player", "Player-only rule");
                var missing = Child(car.transform, "NoRigidbody", 9).Add<BoxCollider>();
                var noRb = Named(Inspect(car), "NoRigidbody");
                Check(((string)noRb["own_layer_rigidbody_check"]!).StartsWith("missing"), "Same-layer missing rigidbody explicit");
                break;
            }
            case "bounds":
            {
                // Root world rotation must cancel, rather than inflate the reported local box.
                car.transform.yaw = .7f; car.transform.position = new Vector3(100, 20, -30);
                var box = Child(car.transform, "RotatedBox").Add<BoxCollider>();
                box.size = new Vector3(2, 4, 6); box.transform.yaw = MathF.PI / 2;
                box.transform.localPosition = new Vector3(3, 0, 4); box.transform.scale = new Vector3(2, 1, 1);
                var capsule = Child(car.transform, "Capsule").Add<CapsuleCollider>();
                capsule.radius = 2; capsule.height = 8; capsule.direction = 2;
                Child(car.transform, "Unsupported").Add<Collider>();
                Child(car.transform, "MissingMesh").Add<MeshCollider>();
                var r = Inspect(car);
                var body = Named(r, "BodyCollider")["bounds"]!;
                Near(-1, body["car_local_min"]!["x"]!, "Body local min without world inflation");
                Near(3, body["car_local_max"]!["z"]!, "Body local front");
                var b = Named(r, "RotatedBox")["bounds"]!;
                Near(0, b["car_local_min"]!["x"]!, "Rotated scaled shape min x");
                Near(6, b["car_local_max"]!["x"]!, "Rotated scaled shape max x");
                Near(2, b["car_local_min"]!["z"]!, "Rotated scaled shape min z");
                Near(6, b["car_local_max"]!["z"]!, "Rotated scaled shape max z");
                Near(4, Named(r, "Capsule")["bounds"]!["car_local_max"]!["z"]!, "Capsule axial bounds");
                Check(!(bool)Named(r, "Unsupported")["bounds"]!["known"]! && !(bool)Named(r, "MissingMesh")["bounds"]!["known"]!, "Unknown is explicit");
                break;
            }
            case "overrides":
            {
                car.transform.position = new Vector3(100, 20, -30);
                model.transform.position = new Vector3(110, 21, -40);
                var body = car.vehicleCollider!;
                body.includeLayers = 1 << 8; body.excludeLayers = 1 << 9; body.layerOverridePriority = 3;
                var rb = body.attachedRigidbody!;
                rb.includeLayers = 1 << 10; rb.excludeLayers = 1 << 11;
                rb.detectCollisions = false; rb.isKinematic = true;
                var trigger = model.frontTrigger!.GetComponents<Collider>().Single();
                trigger.includeLayers = 1 << 12; trigger.excludeLayers = 1 << 2; trigger.layerOverridePriority = 7;
                trigger.attachedRigidbody = model.gameObject.Add<Rigidbody>();
                trigger.attachedRigidbody.includeLayers = 1 << 6;
                trigger.attachedRigidbody.excludeLayers = 1 << 7;
                trigger.attachedRigidbody.detectCollisions = true; trigger.attachedRigidbody.isKinematic = true;
                // Same configuration at a different position still groups, while every root position is retained.
                var twin = Model(); twin.transform.position = new Vector3(120, 22, -50);
                var twinTrigger = twin.frontTrigger!.GetComponents<Collider>().Single();
                twinTrigger.includeLayers = trigger.includeLayers; twinTrigger.excludeLayers = trigger.excludeLayers;
                twinTrigger.layerOverridePriority = trigger.layerOverridePriority;
                twinTrigger.attachedRigidbody = trigger.attachedRigidbody;
                Collider.WorldBoundsReads = 0;
                var r = Inspect(car);
                Check(Collider.WorldBoundsReads == 4, "Only enabled active colliders have world bounds read");
                Near(100, r["root_world_position"]!["position"]!["x"]!, "Car root world position");
                var b = Named(r, "BodyCollider"); var o = b["collider_layer_overrides"]!;
                Check((bool)o["known"]! && (int)o["include_layers"]! == 1 << 8 && (int)o["exclude_layers"]! == 1 << 9 &&
                    (int)o["layer_override_priority"]! == 3, "Collider overrides exact");
                var state = b["rigidbody_state"]!;
                Check((bool)state["present"]! && (bool)state["known"]! && (int)state["include_layers"]! == 1 << 10 &&
                    (int)state["exclude_layers"]! == 1 << 11 && !(bool)state["detect_collisions"]! && (bool)state["is_kinematic"]!, "Rigidbody state exact");
                var world = b["world_bounds"]!;
                Check((bool)world["known"]!, "Enabled collider world bounds");
                Near(9, world["min"]!["x"]!, "Actual world bounds, independent of shape-local bounds");
                Near(33, world["max"]!["z"]!, "World bounds max");
                var park = Named(r, "ParkingProxy");
                Check(!(bool)park["world_bounds"]!["known"]! && (bool)park["bounds"]!["known"]!, "Disabled shape known but world bounds unread");
                Check(!(bool)park["rigidbody_state"]!["present"]! && !(bool)park["rigidbody_state"]!["known"]!, "Absent Rigidbody explicit");
                var gley = r["gley"]!;
                Check(gley["models"]!.Count() == 2 && gley["groups"]!.Count() == 1, "Positions do not fragment settings groups");
                Near(110, gley["models"]![0]!["root_world_position"]!["position"]!["x"]!, "First model world position");
                Near(120, gley["models"]![1]!["root_world_position"]!["position"]!["x"]!, "Second model world position");
                Check((int)gley["models"]![1]!["group_id"]! == 0, "Model links to settings group");
                var t = gley["groups"]![0]!["triggers"]![0]!;
                Check((int)t["collider_layer_overrides"]!["include_layers"]! == 1 << 12 &&
                    (int)t["collider_layer_overrides"]!["exclude_layers"]! == 1 << 2 &&
                    (int)t["collider_layer_overrides"]!["layer_override_priority"]! == 7, "Trigger collider overrides");
                Check((bool)t["rigidbody_state"]!["known"]! && (int)t["rigidbody_state"]!["include_layers"]! == 1 << 6 &&
                    (int)t["rigidbody_state"]!["exclude_layers"]! == 1 << 7 &&
                    (bool)t["rigidbody_state"]!["detect_collisions"]! && (bool)t["rigidbody_state"]!["is_kinematic"]!, "Attached trigger Rigidbody state");
                // Different override or Rigidbody settings must not collapse into the same group.
                twinTrigger.excludeLayers = 0;
                Check(Inspect(car)["gley"]!["groups"]!.Count() == 2, "Override differences retain distinct groups");
                twinTrigger.excludeLayers = trigger.excludeLayers;
                twinTrigger.attachedRigidbody = null;
                Check(Inspect(car)["gley"]!["groups"]!.Count() == 2, "Rigidbody differences retain distinct groups");
                break;
            }
            case "unknowns":
            {
                var body = car.vehicleCollider!;
                body.ThrowOverrides = true; body.ThrowWorldBounds = true;
                body.attachedRigidbody!.ThrowState = true;
                var trigger = model.frontTrigger!.GetComponents<Collider>().Single();
                trigger.ThrowOverrides = true;
                trigger.attachedRigidbody = model.gameObject.Add<Rigidbody>();
                trigger.attachedRigidbody.ThrowState = true;
                car.transform.position = new Vector3(float.NaN, 0, 0);
                model.transform.position = new Vector3(float.PositiveInfinity, 0, 0);
                TrafficManager.Instance.Pool.Models.Add(null);
                var r = Inspect(car);
                Check(!(bool)r["root_world_position"]!["known"]!, "Non-finite root position unknown");
                var b = Named(r, "BodyCollider");
                Check(!(bool)b["collider_layer_overrides"]!["known"]! && b["collider_layer_overrides"]!["reason"] != null, "Override getter failure explicit");
                Check(!(bool)b["rigidbody_state"]!["known"]! && (bool)b["rigidbody_state"]!["present"]!, "Rigidbody getter failure explicit");
                Check(!(bool)b["world_bounds"]!["known"]!, "World bounds getter failure explicit");
                Check((int)r["collider_count"]! == 7, "Unavailable fields preserve inventory");
                var unknownTrigger = r["gley"]!["groups"]![0]!["triggers"]![0]!;
                Check(!(bool)unknownTrigger["collider_layer_overrides"]!["known"]! &&
                    !(bool)unknownTrigger["rigidbody_state"]!["known"]!, "Trigger getter failures explicit without losing model group");
                var models = r["gley"]!["models"]!;
                Check(!(bool)models[0]!["root_world_position"]!["known"]! &&
                    !(bool)models[1]!["root_world_position"]!["known"]! && models[1]!["group_id"]!.Type == JTokenType.Null,
                    "Non-finite and missing model roots explicit");
                car.transform.position = new Vector3();
                body.ThrowWorldBounds = false;
                body.ReportedWorldBounds = new Bounds(new Vector3(), new Vector3(float.NaN, 1, 1));
                Check(!(bool)Named(Inspect(car), "BodyCollider")["world_bounds"]!["known"]!, "Non-finite world bounds explicit");
                body.ReportedWorldBounds = new Bounds(new Vector3(), new Vector3(0, 1, 1));
                Check(!(bool)Named(Inspect(car), "BodyCollider")["world_bounds"]!["known"]!, "Empty world bounds explicit");
                break;
            }
            case "limits":
            {
                for (var i = 7; i < 128; i++) Child(car.transform, "Extra" + i).Add<BoxCollider>();
                var exact = Inspect(car);
                Check((int)exact["collider_count"]! == 128 && !(bool)exact["colliders_truncated"]!, "Exact collider limit complete");
                Child(car.transform, "Overflow").Add<BoxCollider>();
                var over = Inspect(car);
                Check((int)over["collider_count"]! == 128 && (bool)over["colliders_truncated"]!, "Collider overflow explicit and bounded");
                var models = TrafficManager.Instance.Pool.Models;
                while (models.Count < 1024) models.Add(model);
                Check((int)Inspect(car)["gley"]!["groups"]![0]!["model_count"]! == 1024, "1024 identical models compact");
                models.Add(model);
                Check(!(bool)Inspect(car)["gley"]!["complete"]!, "Pool overflow refused");
                models.RemoveRange(1, models.Count - 1);
                for (var i = 1; i < 16; i++) Child(model.frontTrigger!, "T" + i).Add<BoxCollider>().isTrigger = true;
                Check((bool)Inspect(car)["gley"]!["complete"]!, "Exact front collider limit");
                Child(model.frontTrigger!, "Overflow").Add<BoxCollider>();
                var limited = Inspect(car);
                Check(!(bool)limited["gley"]!["complete"]! && (int)limited["gley"]!["failed_models"]! == 1, "Trigger overflow explicit");
                Check((int)limited["collider_count"]! == 128, "Trigger failure preserves truck list");
                break;
            }
            case "failures":
            {
                car.vehicleController = null;
                TrafficManager.IsInitialized = false;
                var reads = TrafficManager.InstanceReads;
                var r = Inspect(car);
                Check(TrafficManager.InstanceReads == reads, "Uninitialized pool must not request singleton");
                Check((int)r["collider_count"]! == 7 && r["errors"]!.Any(), "Wheel reporting failure separate from inventory");
                Check(Named(r, "GeneratedWheel")["under_native_wheel"]!.Type == JTokenType.Null, "Unknown wheel flag explicit");
                Check(!(bool)r["gley"]!["complete"]! && r["gley"]!["errors"]!.Any(), "Uninitialized pool error separate");
                TrafficManager.IsInitialized = true;
                TrafficManager.Instance.Pool.Models.Add(null);
                var partial = Inspect(car);
                Check((int)partial["gley"]!["failed_models"]! == 1 && partial["gley"]!["groups"]!.Count() == 1, "Bad model preserves valid groups");
                // Depth budget stays bounded even for pathological hierarchies.
                var parent = car.transform;
                for (var i = 0; i < 70; i++) parent = Child(parent, "Deep").transform;
                parent.gameObject.Add<BoxCollider>();
                Check((bool)Inspect(car)["colliders_truncated"]!, "Depth exhaustion explicit");
                break;
            }
            case "command":
            {
                var request = new JObject { ["op"] = "visibility", ["id"] = "owned" };
                Writes.Count = 0;
                var r = (JObject)TestBridgeCommands.Invoke(request);
                Check((int)r["collider_count"]! == 7 && Writes.Count == 0, "Read-only command returns object from actual car");
                Refused(new JObject { ["op"] = "visibility", ["id"] = "not-owned" });
                Refused(new JObject { ["op"] = "visibility" });
                car.vehicleInstance!.vehicleTypeName = "cart"; Refused(request);
                car.vehicleInstance.vehicleTypeName = "foodtruck"; VehicleHelper.Car = null; Refused(request);
                break;
            }
            default: throw new Exception("Unknown scenario");
        }
        Console.WriteLine("PASS: " + args[0]);
    }
}
