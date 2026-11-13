#nullable enable
using UnityEngine;

namespace FoodTrucks
{
    internal static class TruckHatch
    {
        private const string HatchName = "Truck_Hatch";
        private const float OpenDegrees = 80f;

        private static readonly System.Collections.Generic.Dictionary<int, Transform?> hatches =
            new System.Collections.Generic.Dictionary<int, Transform?>();

        public static void Set(VehicleController? controller, bool open)
        {
            if (open && CrewDriveProbe.Owns(controller)) return;
            if (controller == null || controller.vehicleInstance?.vehicleTypeName != FoodTrucksMod.TruckTypeName) return;
            var key = controller.GetInstanceID();
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
