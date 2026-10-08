#nullable enable
using System;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // Settings the mod's prefabs can only get from vanilla prefabs at runtime:
    // the hand truck's grip rotations for the cart, and a car's sleep setup for the truck.
    // Applied to the prefab assets (for later spawns) and to vehicles already in the world.
    internal static class PrefabFixes
    {
        private const string VanillaHandTruck = "Vehicles/PlayerVehicles/handtruck";
        private const string VanillaCar = "Vehicles/PlayerVehicles/vordv150";

        private static bool loggedVanilla;

        public static void Apply()
        {
            HandTruck? vanillaCart = null;
            VehicleController? vanillaCar = null;
            try
            {
                vanillaCart = PrefabHelper.LoadPrefabAssetByName(VanillaHandTruck)?.GetComponent<HandTruck>();
                vanillaCar = PrefabHelper.LoadPrefabAssetByName(VanillaCar)?.GetComponent<VehicleController>();
            }
            catch (Exception ex) { Log.Error("Prefab fixes: vanilla prefabs not loaded: " + ex.Message); }

            if (vanillaCart == null) Log.Error($"Prefab fixes: {VanillaHandTruck} not found; the cart keeps its own grip.");
            else if (!loggedVanilla) LogVanillaGrip(vanillaCart);
            if (vanillaCar == null) Log.Error($"Prefab fixes: {VanillaCar} not found; the truck has no sleep setup.");

            var bundle = FoodTrucksMod.Bundle;
            if (bundle != null)
            {
                FixCart(bundle.LoadAsset<GameObject>(FoodTrucksMod.CartPrefabPath)?.GetComponent<HandTruck>(), vanillaCart);
                FixTruck(bundle.LoadAsset<GameObject>(FoodTrucksMod.TruckPrefabPath)?.GetComponent<VehicleController>(), vanillaCar);
            }
            foreach (var controller in VehicleHelper.AllPlayerVehicles)
                ApplyTo(controller, vanillaCart, vanillaCar);
            loggedVanilla = true;
        }

        // For a vehicle spawned after Apply ran but from a stale instance (e.g. the player's held cart).
        public static void ApplyTo(VehicleController? controller) =>
            ApplyTo(controller,
                PrefabHelper.LoadPrefabAssetByName(VanillaHandTruck)?.GetComponent<HandTruck>(),
                PrefabHelper.LoadPrefabAssetByName(VanillaCar)?.GetComponent<VehicleController>());

        private static void ApplyTo(VehicleController? controller, HandTruck? vanillaCart, VehicleController? vanillaCar)
        {
            if (controller == null || controller.vehicleInstance == null) return;
            var typeName = controller.vehicleInstance.vehicleTypeName;
            if (typeName == FoodTrucksMod.CartTypeName) FixCart(controller as HandTruck, vanillaCart);
            else if (typeName == FoodTrucksMod.TruckTypeName) FixTruck(controller, vanillaCar);
        }

        private static void FixCart(HandTruck? cart, HandTruck? vanilla)
        {
            if (cart == null || vanilla == null) return;
            CopyGripRotation(vanilla, vanilla.LHandIKAttachmentPoint, cart, cart.LHandIKAttachmentPoint);
            CopyGripRotation(vanilla, vanilla.RHandIKAttachmentPoint, cart, cart.RHandIKAttachmentPoint);
        }

        // The game copies the IK point's rotation onto the hand bone, so the rotation relative to the
        // vehicle must match the rig. Positions stay the cart's own: its handle is in a different place.
        private static void CopyGripRotation(Component vanillaRoot, Transform? vanillaPoint, Component cartRoot, Transform? cartPoint)
        {
            if (vanillaPoint == null || cartPoint == null) return;
            var relative = Quaternion.Inverse(vanillaRoot.transform.rotation) * vanillaPoint.rotation;
            cartPoint.rotation = cartRoot.transform.rotation * relative;
        }

        private static void FixTruck(VehicleController? truck, VehicleController? vanilla)
        {
            if (truck == null || vanilla == null) return;
            // The field holds a serializable wrapper around a config asset; sharing it is read-only use.
            truck.sleepEnvironment = vanilla.sleepEnvironment;
        }

        private static void LogVanillaGrip(HandTruck vanilla)
        {
            string Point(Transform? t)
            {
                if (t == null) return "none";
                var root = vanilla.transform;
                var position = root.InverseTransformPoint(t.position);
                var rotation = (Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles;
                return $"position {position:F3}, rotation {rotation:F1}";
            }
            Log.Info($"Vanilla hand truck: offset {vanilla.playerOffsetWhenActive:F2}, left hand {Point(vanilla.LHandIKAttachmentPoint)}, right hand {Point(vanilla.RHandIKAttachmentPoint)}.");
        }
    }
}
