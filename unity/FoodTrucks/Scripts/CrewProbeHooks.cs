#nullable enable
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // Only installed by ApplyTestBridge. Every required patch must succeed before a probe may start.
    internal static class CrewProbeHooks
    {
        internal static void Install(Harmony harmony)
        {
            var ready = true;
            void Add(Type type, string name, Type[]? args, string handler, bool postfix = false)
            {
                var label = $"{type.Name}.{name}, crew probe";
                try
                {
                    var original = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.Name, name);
                    var patch = new HarmonyMethod(typeof(CrewProbeHooks), handler) { priority = Priority.First };
                    harmony.Patch(original, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
                    Log.Info($"Patch {label}: Active.");
                }
                catch (Exception ex) { ready = false; Log.Error($"Patch {label}: Not active, {ex.Message}."); }
            }
            Add(typeof(NWH.WheelController3D.WheelController), "Step", Type.EmptyTypes, nameof(WheelStep));
            Add(typeof(CarController), "OnVehicleStationaryAfterLoad", Type.EmptyTypes, nameof(Ready), true);
            Add(typeof(CarController), "EnterVehicle", Type.EmptyTypes, nameof(Enter));
            Add(typeof(CarController), "OnDestroy", Type.EmptyTypes, nameof(Destroying));
            Add(typeof(CarController), "FuelEmptyNotification", new[] { typeof(bool) }, nameof(FuelNotice));
            Add(typeof(NWH.VehiclePhysics2.Damage.DamageHandler), "OnCollisionEnter", new[] { typeof(Collision) }, nameof(Damage), true);
            Add(typeof(VehicleHelper), "Delete", new[] { typeof(VehicleInstance), typeof(VehicleController) }, nameof(Delete));
            Add(typeof(VehicleHelper), "TeleportVehicleToGround", new[] { typeof(VehicleController), typeof(Vector3), typeof(Quaternion) }, nameof(Teleport));
            Add(typeof(SaveGameManager), "Save", null, nameof(Save));
            Add(typeof(GameSpeedController), "Set", new[] { typeof(GameSpeed) }, nameof(Speed));
            Add(typeof(GameManager), "SetMinutesMultiplier", new[] { typeof(float) }, nameof(Minutes));
            // Patch both overloads without depending on Timestamp's assembly/name.
            var skips = typeof(Timemachine.TimeMachine).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "StartTimeMachine").ToArray();
            if (skips.Length != 2)
            {
                ready = false;
                Log.Error("Patch TimeMachine.StartTimeMachine inventory, crew probe: Not active, expected two overloads.");
            }
            foreach (var method in skips)
                Add(typeof(Timemachine.TimeMachine), method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray(), nameof(Skip));
            Add(typeof(UI.ItemPanel.ItemPanelUI), "ClickAutoPark", Type.EmptyTypes, nameof(PlayerTeleport));
            Add(typeof(Vehicles.SkipBridgeHelper), "SkipBridge", Type.EmptyTypes, nameof(PlayerTeleport));
            CrewDriveProbe.HooksReady = ready;
        }

        private static bool WheelStep(NWH.WheelController3D.WheelController __instance) => CrewDriveProbe.BeforeWheelStep(__instance);
        private static void Ready(CarController __instance) => CrewDriveProbe.StationaryReady(__instance);
        private static bool Enter(CarController __instance) => CrewDriveProbe.StopVehicle(__instance, "Player takeover before native entry");
        private static void Destroying(CarController __instance)
        {
            CrewDriveProbe.StopVehicle(__instance, "Native destruction");
            CrewDriveProbe.FlushObservedDamage(__instance);
            CrewProbeTraffic.NativeRemoving(__instance);
        }
        private static void Damage(NWH.VehiclePhysics2.Damage.DamageHandler __instance) =>
            CrewDriveProbe.FlushObservedDamage(__instance.GetComponentInParent<CarController>());
        private static bool FuelNotice(CarController __instance) => !CrewDriveProbe.Owns(__instance);
        private static bool Delete(VehicleInstance __0, VehicleController? __1) =>
            !CrewDriveProbe.Owns(__0?.id) || CrewDriveProbe.StopAll("Native sale or deletion");
        private static bool Teleport(VehicleController __0) => !CrewDriveProbe.Owns(__0);
        private static bool PlayerTeleport() => !CrewDriveProbe.Owns(SaveGameManager.Current?.ActiveVehicleId);
        private static bool Save(ref bool __result)
        {
            if (CrewDriveProbe.StopAll("Before native save")) return true;
            __result = false; // Before Save sets its in-progress bookkeeping; retry remains possible.
            return false;
        }
        private static bool Speed(GameSpeed __0) => (!__0.paused && (int)__0.speed == 1) || CrewDriveProbe.StopAll("Pause or native acceleration");
        private static bool Minutes() => CrewDriveProbe.StopAll("Game minute-rate change");
        private static bool Skip() => CrewDriveProbe.StopAll("Time-machine skip");
    }
}
