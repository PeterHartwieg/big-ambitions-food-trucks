#nullable enable
using System;
using UnityEngine;

namespace FoodTrucks
{
    // Peter, 13 October 2026, driving the 1,600 kg truck: "when aggressively steering I hear a short crashing sound
    // around 70% of the time". NWH's CrashComponent plays a clip on every collision its vehicle reports, except with the
    // Ground, GroundUnplacable and Roads layers (DamageHandler.IsCollisionValid) [code, 1.0]. With the test bridge on, each
    // such collision of a food vehicle is logged with what it hit, so a drive shows what the truck touches.
    internal static partial class TestBridgeCommands
    {
        private static float lastCollisionLog = -1f;
        private static int groundLayer = -2, groundUnplacableLayer, roadsLayer;

        // Postfix on NWH.VehiclePhysics2.VehicleController.OnCollisionEnter(Collision).
        public static void VehicleCollisionPostfix(Component __instance, Collision other)
        {
            try
            {
                if (other == null || other.gameObject == null) return;
                var game = __instance.GetComponent<VehicleController>();
                var type = game?.vehicleInstance?.vehicleTypeName;
                if (!FoodTrucksMod.IsModVehicle(type)) return;
                if (groundLayer == -2)
                {
                    groundLayer = LayerMask.NameToLayer("Ground");
                    groundUnplacableLayer = LayerMask.NameToLayer("GroundUnplacable");
                    roadsLayer = LayerMask.NameToLayer("Roads");
                }
                var layer = other.gameObject.layer;
                if (layer == groundLayer || layer == groundUnplacableLayer || layer == roadsLayer) return;
                // The crash clip waits 80 ms between plays; a quarter second keeps a scrape to a few lines.
                if (UnityEngine.Time.time - lastCollisionLog < 0.25f) return;
                lastCollisionLog = UnityEngine.Time.time;
                var where = "no contact point";
                var ours = "?";
                if (other.contactCount > 0)
                {
                    var contact = other.GetContact(0);
                    var local = __instance.transform.InverseTransformPoint(contact.point);
                    where = $"at x {local.x:F2}, y {local.y:F2}, z {local.z:F2} on the vehicle (x right, z forward)";
                    ours = contact.thisCollider != null ? contact.thisCollider.name : "?";
                }
                var body = __instance.GetComponent<Rigidbody>();
                Log.Info($"Collision (test bridge): {FoodTrucksMod.KindOf(type)} {game!.vehicleInstance.id} hit {other.collider?.name ?? "?"} on " +
                         $"{other.transform.root.name} (layer {LayerMask.LayerToName(layer)}) {where}, our {ours}; relative speed " +
                         $"{other.relativeVelocity.magnitude:F1} m/s, vehicle {(body != null ? body.velocity.magnitude * 3.6f : 0f):F0} km/h; the crash clip plays.");
            }
            catch (Exception ex)
            {
                Log.Error("Collision log (test bridge): " + ex.Message);
            }
        }
    }
}
