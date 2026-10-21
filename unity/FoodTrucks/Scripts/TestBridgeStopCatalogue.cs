#nullable enable
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    internal static partial class TestBridgeCommands
    {
        // Owner-only map export. Uses exactly the native route picker's physics,
        // wheel footprint, hatch direction and pavement rules. Small batches keep
        // the main thread responsive; this does not change vehicles or schedules.
        private static object StopCatalogue(JObject args)
        {
            if (!Legality.ParkingSceneReady()) throw new BridgeError("City parking lanes are not loaded.");
            var start = args.Value<int?>("start") ?? 0;
            var count = args.Value<int?>("count") ?? 4;
            if (start < 0 || count < 1 || count > 8) throw new BridgeError("Use start >= 0 and count 1–8.");
            var entrances = World.AllBuildings()
                .Where(b => b.Controller.building.Address != null)
                .SelectMany(b => b.Doors.Select((door, index) => new {
                    key = World.AddressKey(b.Controller.building.Address.streetName, b.Controller.building.Address.streetNumber),
                    door, index
                })).OrderBy(b => b.key, StringComparer.Ordinal).ThenBy(b => b.index).ToArray();
            var rows = new JArray();
            foreach (var entrance in entrances.Skip(start).Take(count))
                foreach (var kind in new[] { "cart", "truck" })
                {
                    var stop = StopPicker.Snap(kind, entrance.door, out var refusal, out _);
                    var row = new JObject { ["key"] = entrance.key, ["entrance"] = entrance.index, ["kind"] = kind };
                    if (stop == null) row["refusal"] = refusal;
                    else if (CatalogueObstructed(kind, stop.Value.Position, stop.Value.Heading)) row["refusal"] = "static_obstacle";
                    else {
                        row["x"] = stop.Value.Position.x; row["y"] = stop.Value.Position.y;
                        row["z"] = stop.Value.Position.z; row["heading"] = stop.Value.Heading;
                    }
                    rows.Add(row);
                }
            return new JObject { ["total"] = entrances.Length, ["start"] = start,
                ["next"] = Math.Min(entrances.Length, start + count), ["rows"] = rows };
        }

        // A legal pavement sample can still put the cart's body through a
        // mailbox. Exclude permanent obstructions using the actual prefab hull.
        // Parked cars and people are transient: this is not a vacancy forecast.
        private static bool CatalogueObstructed(string kind, Vector3 ground, float heading)
        {
            var prefab = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(kind == "truck" ? FoodTrucksMod.TruckPrefabPath : FoodTrucksMod.CartPrefabPath);
            var controller = prefab?.GetComponent<VehicleController>();
            if (controller == null) throw new BridgeError("Vehicle prefab is unavailable for stopping-place export.");
            var local = LocalBounds(controller);
            var bottom = Mathf.Min(local.min.y, 0f) + 0.3f;
            var top = Mathf.Max(local.max.y, bottom + 0.5f);
            var centre = new Vector3(local.center.x, (bottom + top) / 2f, local.center.z);
            var half = new Vector3(Mathf.Max(.05f, local.extents.x - .1f), (top - bottom) / 2f, Mathf.Max(.05f, local.extents.z - .1f));
            var rotation = Quaternion.Euler(0, heading, 0);
            return Physics.OverlapBox(ground + rotation * centre, half, rotation, ~0, QueryTriggerInteraction.Collide)
                .Any(c => c != null && !Ignored(c) && c.gameObject.layer != LayerHelper.ParkedVehiclesLayerIndex
                    && c.gameObject.layer != LayerHelper.AiVehiclesLayerIndex && c.attachedRigidbody == null
                    && c.GetComponentInParent<VehicleController>() == null && c.GetComponentInParent<ThirdPersonCharacter>() == null
                    && c.GetComponentInParent<PlayerController>() == null
                    && c.GetComponentInParent<Vehicles.DeliveryDriverJob.DeliveryJobStartController>() == null);
        }
    }
}
