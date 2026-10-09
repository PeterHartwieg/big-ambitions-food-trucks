#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.SaveSystem;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // The cart model's moving parts and its place in the parking lanes:
    // - the wheels turn with the distance the pushed cart rolls;
    // - the body collider at CarHolder/Colliders/BodyCollider (a trigger the lanes read, see CartAssets) is on
    //   only while the cart stands, like the root collider the game switches off while pushing. The game's
    //   onExitVehicle comes before VehicleController.ExitVehicle regenerates the lanes, so they see it on;
    // - parked cars the lanes generate on a cart are cleared (DestroyBlockingPrefix).
    internal sealed class CartParts : MonoBehaviour
    {
        private const string BodyColliderPath = "CarHolder/Colliders/BodyCollider";
        private const float WheelRadius = 0.25f;
        private static readonly string[] WheelNames = { "Cart_Wheel_L", "Cart_Wheel_R" };

        private HandTruck? pushed;
        private Transform[] wheels = Array.Empty<Transform>();
        private Vector3 lastPosition;
        private float wheelDegrees;

        private void OnEnable()
        {
            GlobalEvents.onEnterVehicle += OnEnterVehicle;
            GlobalEvents.onExitVehicle += OnExitVehicle;
        }

        private void OnDisable()
        {
            GlobalEvents.onEnterVehicle -= OnEnterVehicle;
            GlobalEvents.onExitVehicle -= OnExitVehicle;
        }

        // A save made while pushing loads with the cart in hand, possibly before this component existed.
        private void Start()
        {
            try
            {
                var active = SaveGameManager.Current?.ActiveVehicleId;
                var current = active == null ? null : VehicleHelper.AllPlayerVehicles.FirstOrDefault(v => v != null && v.vehicleInstance?.id == active);
                if (current != null) OnEnterVehicle(current);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void OnEnterVehicle(VehicleController controller)
        {
            try
            {
                if (!IsCart(controller)) return;
                SetBody(controller, false);
                pushed = controller as HandTruck;
                wheels = pushed == null ? Array.Empty<Transform>() : FindWheels(pushed.transform);
                lastPosition = controller.transform.position;
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void OnExitVehicle(VehicleController controller)
        {
            try
            {
                if (!IsCart(controller)) return;
                SetBody(controller, true);
                if (controller == pushed) pushed = null;
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void LateUpdate()
        {
            if (pushed == null || wheels.Length == 0) return;
            var position = pushed.transform.position;
            var moved = position - lastPosition;
            lastPosition = position;
            // A jump (the grab moving the player, a teleport) isn't rolling.
            if (moved.sqrMagnitude > 4f) return;
            var distance = Vector3.Dot(moved, pushed.transform.forward);
            if (Mathf.Abs(distance) < 0.0001f) return;
            // Rolling forward (+z) turns the top of the wheel forward: positive about local X.
            wheelDegrees = Mathf.Repeat(wheelDegrees + distance / WheelRadius * Mathf.Rad2Deg, 360f);
            var rotation = Quaternion.Euler(wheelDegrees, 0f, 0f);
            foreach (var wheel in wheels)
                if (wheel != null) wheel.localRotation = rotation;
        }

        private static Transform[] FindWheels(Transform root) =>
            WheelNames.Select(n => root.Find(n)).Where(t => t != null).ToArray();

        private static bool IsCart(VehicleController? controller) =>
            controller != null && controller.vehicleInstance?.vehicleTypeName == FoodTrucksMod.CartTypeName;

        private static void SetBody(VehicleController controller, bool on)
        {
            var body = controller.transform.Find(BodyColliderPath)?.GetComponent<Collider>();
            if (body != null) body.enabled = on;
        }

        // One line per cart at load: whether the parking lanes will keep its spot free.
        public static void LogLanes()
        {
            var carts = SaveGameManager.Current?.VehicleInstances?.Where(v => v != null && v.vehicleTypeName == FoodTrucksMod.CartTypeName).ToList()
                        ?? new List<VehicleInstance>();
            foreach (var cart in carts)
            {
                var controller = VehicleHelper.AllPlayerVehicles.FirstOrDefault(v => v != null && v.vehicleInstance?.id == cart.id);
                string verdict;
                if (controller == null) verdict = "not seen: not in the game's player vehicle list (not spawned)";
                else if (!VehicleHelper.TryGetBodyColliderBounds(controller.transform, out var bounds, out var collider))
                    verdict = $"not seen: no collider at {BodyColliderPath} (a cart from an older build)";
                else if (controller.controlledByPlayer)
                    verdict = $"in hand now (lanes skip it while pushed); body collider {bounds.size.x:F2} x {bounds.size.z:F2} m, on when set down";
                else if (!collider.enabled)
                    verdict = "not seen: body collider switched off";
                else
                    verdict = $"seen; body collider {bounds.size.x:F2} x {bounds.size.z:F2} m at {collider.bounds.center:F1}";
                Log.Info($"Parking lanes: cart {cart.id} {verdict}.");
            }
            if (carts.Count == 0) Log.Info("Parking lanes: no carts in this save.");
        }

        // VehicleHelper.DestroyBlockingVehicles, which clears parked and traffic cars from where a player vehicle
        // stands, reads a hand vehicle's root BoxCollider.bounds (world space) and then places the check box with
        // TransformPoint as if it were local, so for a cart the box lands far away [code, 1.0]. For the mod's carts
        // this runs the same check with the collider's local box and skips the game's.
        public static bool DestroyBlockingPrefix(GameObject prefab, Vehicles.VehicleTypes.VehicleType vehicleType, Transform placedTransform, bool onlyParkedVehicles)
        {
            try
            {
                if (vehicleType == null || vehicleType.vehicleTypeName != FoodTrucksMod.CartTypeName || prefab == null || placedTransform == null) return true;
                var box = prefab.GetComponent<BoxCollider>();
                if (box == null) return true;
                Physics.SyncTransforms();
                var count = Physics.OverlapBoxNonAlloc(placedTransform.TransformPoint(box.center), box.size * 0.6f, Hits,
                    placedTransform.rotation, LayerHelper.vehicleConflictMask);
                var parked = 0;
                var traffic = 0;
                Cleared.Clear();
                for (var i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (hit == null || hit.transform.IsChildOf(placedTransform) || !Cleared.Add(hit.gameObject)) continue;
                    if (hit.gameObject.layer == LayerHelper.ParkedVehiclesLayerIndex)
                    {
                        ParkingSimulator.ReleaseParkedVehicle(hit.gameObject);
                        parked++;
                    }
                    else if (hit.gameObject.layer == LayerHelper.AiVehiclesLayerIndex && !onlyParkedVehicles)
                    {
                        var vehicle = hit.GetComponentInParent<GleyTrafficSystem.VehicleComponent>();
                        if (vehicle != null)
                        {
                            GleyTrafficSystem.Manager.RemoveVehicle(vehicle.gameObject);
                            traffic++;
                        }
                    }
                }
                if (parked + traffic > 0)
                {
                    var id = prefab.GetComponent<VehicleController>()?.vehicleInstance?.id ?? "(new)";
                    Log.Info($"Parking lanes: cleared {parked} parked and {traffic} traffic cars from cart {id}.");
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Parking lanes: " + ex);
                return true;
            }
        }

        private static readonly Collider[] Hits = new Collider[32];
        private static readonly HashSet<GameObject> Cleared = new HashSet<GameObject>();
    }
}
