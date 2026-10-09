#nullable enable
using UnityEngine;

namespace FoodTrucks
{
    // The truck's serving hatch: up while the truck is open for business, down otherwise.
    // The model's hatch hinges along its local Z and opens at +80 degrees (art/README.md).
    // Called on every sales tick, so it remembers each truck's hatch and only moves it on a change.
    internal static class TruckHatch
    {
        private const string HatchName = "Truck_Hatch";
        private const float OpenDegrees = 80f;

        // By the controller's instance ID: a truck spawned again (out of range and back) has a new one.
        private static readonly System.Collections.Generic.Dictionary<int, Transform?> hatches =
            new System.Collections.Generic.Dictionary<int, Transform?>();

        public static void Set(VehicleController? controller, bool open)
        {
            if (controller == null || controller.vehicleInstance?.vehicleTypeName != FoodTrucksMod.TruckTypeName) return;
            var key = controller.GetInstanceID();
            // A miss is remembered too (logged once), so a truck without a hatch isn't searched on every tick.
            if (!hatches.TryGetValue(key, out var hatch))
            {
                hatch = Find(controller.transform);
                hatches[key] = hatch;
                if (hatch == null) Log.Error($"Truck {controller.vehicleInstance.id} has no {HatchName}.");
            }
            if (hatch == null) return;
            var angle = open ? OpenDegrees : 0f;
            if (Mathf.Abs(Mathf.DeltaAngle(hatch.localEulerAngles.z, angle)) < 0.5f) return;
            hatch.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        public static void Forget() => hatches.Clear();

        private static Transform? Find(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == HatchName) return t;
            return null;
        }
    }
}
