#if FOODTRUCKS_CREW_DRIVING_DEV
#nullable enable
using System;
using System.Linq;
using GleyTrafficSystem;
using GleyUrbanAssets;
using Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    internal static partial class TestBridgeCommands
    {
        private static object CrewDrive(JObject args)
        {
            switch (Str(args, "op") ?? "status")
            {
                case "status": return CrewDriveProbe.Status();
                case "obstacle_evidence": return CrewProbeTrafficEvidence.InspectObstacle(CrewDriveProbe.ObstacleTraffic);
                case "waypoints": return new JObject { ["waypoints"] = CrewWaypoints(args) };
                case "trigger_coverage":
                case "body_contract":
                case "traffic_evidence":
                case "visibility":
                    var inspectId = Need(args, "id", "the owned food truck ID");
                    var instance = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == inspectId);
                    var car = instance == null ? null : VehicleHelper.GetVehicleController(instance) as CarController;
                    if (car == null || instance!.vehicleTypeName != FoodTrucksMod.TruckTypeName)
                        throw new BridgeError("Visibility inspection needs a spawned owned food truck.");
                    if (Str(args, "op") == "trigger_coverage") return JObject.FromObject(CrewProbeTraffic.InspectCoverage(car));
                    if (Str(args, "op") == "body_contract")
                    {
                        var contract = new CrewProbeBody(car);
                        var geometryValid = contract.Validate() == null;
                        var grounded = contract.TryGround(out var ground);
                        return new JObject { ["read_only"] = true, ["valid"] = geometryValid, ["geometry_valid"] = geometryValid,
                            ["traffic_certified"] = contract.TrafficCertified, ["traffic_certificate_requires"] = "trigger_coverage or traffic admission",
                            ["controllerEnabled"] = car.vehicleController.enabled,
                            ["engineIgnition"] = car.vehicleController.powertrain.engine.ignition,
                            ["engineRunningCached"] = car.vehicleController.powertrain.engine.IsRunning,
                            ["starterActive"] = car.vehicleController.powertrain.engine.starterActive,
                            ["bodyKinematic"] = car.vehicleController.vehicleRigidbody.isKinematic,
                            ["physical_min"] = new JArray(contract.Min.x, contract.Min.y, contract.Min.z),
                            ["physical_max"] = new JArray(contract.Max.x, contract.Max.y, contract.Max.z),
                            ["ground_known"] = grounded, ["ground_y"] = grounded ? (float?)ground : null };
                    }
                    return Str(args, "op") == "traffic_evidence" ? CrewProbeTrafficEvidence.Inspect(car) : CrewProbeVisibility.Inspect(car);
                case "stop":
                    if (args["generation"]?.Type != JTokenType.Integer) throw new BridgeError("stop requires the current integer generation.");
                    if (!Guid.TryParse(Str(args, "session"), out var session)) throw new BridgeError("stop requires the current probe session.");
                    return CrewDriveProbe.StopProbe(session, (long)args["generation"]!);
                case "start":
                    var id = Need(args, "id", "the owned food truck ID");
                    if (!(args["waypoints"] is JArray input) || input.Count < 2 || input.Count > 512)
                        throw new BridgeError("start requires 2–512 connected native waypoint IDs.");
                    var path = new int[input.Count];
                    for (var i = 0; i < path.Length; i++)
                    {
                        if (input[i].Type != JTokenType.Integer || (long)input[i] < 0 || (long)input[i] > int.MaxValue)
                            throw new BridgeError("Each waypoint must be a nonnegative native integer ID.");
                        path[i] = (int)input[i];
                    }
                    return CrewDriveProbe.StartProbe(id, path, (float)(Num(args, "max_speed_ms") ?? 2));
                default: throw new BridgeError("crew_drive_probe op is start, status, stop, waypoints, visibility, body_contract, trigger_coverage, traffic_evidence or obstacle_evidence.");
            }
        }

        // Developer route inspection only: no route generation, spawning, repositioning or clock changes.
        private static JArray CrewWaypoints(JObject args)
        {
            var scene = UnityEngine.Object.FindObjectOfType<CurrentSceneData>();
            if (scene?.allWaypoints == null) throw new BridgeError("Native road graph unavailable.");
            Waypoint[] points;
            if (args["ids"] is JArray ids)
            {
                if (ids.Count == 0 || ids.Count > 512) throw new BridgeError("Inspect 1–512 native waypoint IDs.");
                points = ids.Select(t => {
                    if (t.Type != JTokenType.Integer || (long)t < 0 || (long)t >= scene.allWaypoints.Length)
                        throw new BridgeError("Invalid native waypoint ID.");
                    return scene.allWaypoints[(int)t];
                }).ToArray();
            }
            else
            {
                var id = Need(args, "id", "the owned truck to inspect near");
                var instance = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == id);
                var car = instance == null ? null : VehicleHelper.GetVehicleController(instance);
                if (car == null) throw new BridgeError("Truck is not spawned.");
                var position = car.transform.position;
                points = scene.allWaypoints.Where(w => w != null && (w.position - position).sqrMagnitude < 10000)
                    .OrderBy(w => (w.position - position).sqrMagnitude).Take(64).ToArray();
            }
            return new JArray(points.Where(w => w != null).Select(w => new JObject {
                ["id"] = w.listIndex, ["x"] = w.position.x, ["y"] = w.position.y, ["z"] = w.position.z,
                ["next"] = new JArray(w.neighbors), ["enter"] = w.enter, ["exit"] = w.exit,
                ["stop"] = w.stop, ["give_way"] = w.giveWay, ["disabled"] = w.temporaryDisabled }));
        }
    }
}

#endif
