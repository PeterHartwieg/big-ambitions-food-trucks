#nullable enable
using UnityEngine;

namespace FoodTrucks
{
    // The truck's serving hatch and the cart's steam-well lid: open while the vehicle is open for business,
    // shut otherwise. The truck's hatch hinges along its local Z and opens at +80 degrees, the cart's lid
    // along its local X at -100 (art/README.md). Called on every sales tick, so it remembers each vehicle's
    // part and only moves it on a change.
    internal static class TruckHatch
    {
        private const string HatchName = "Truck_Hatch";
        private const string LidName = "Cart_Lid";

        // By the controller's instance ID: a vehicle spawned again (out of range and back) has a new one.
        private static readonly System.Collections.Generic.Dictionary<int, Transform?> hatches =
            new System.Collections.Generic.Dictionary<int, Transform?>();

        public static void Set(VehicleController? controller, bool open)
        {
            var typeName = controller?.vehicleInstance?.vehicleTypeName;
            var truck = typeName == FoodTrucksMod.TruckTypeName;
            if (controller == null || (!truck && typeName != FoodTrucksMod.CartTypeName)) return;
            var name = truck ? HatchName : LidName;
            var key = controller.GetInstanceID();
            // A miss is remembered too (logged once), so a vehicle without the part isn't searched on every tick.
            if (!hatches.TryGetValue(key, out var hatch))
            {
                hatch = Find(controller.transform, name);
                hatches[key] = hatch;
                if (hatch == null) Log.Error($"{(truck ? "Truck" : "Cart")} {controller.vehicleInstance!.id} has no {name}.");
            }
            if (hatch == null) return;
            var target = truck ? new Vector3(0f, 0f, open ? 80f : 0f) : new Vector3(open ? -100f : 0f, 0f, 0f);
            if (Quaternion.Angle(hatch.localRotation, Quaternion.Euler(target)) < 0.5f) return;
            hatch.localEulerAngles = target;
        }

        public static void Forget() => hatches.Clear();

        private static Transform? Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
