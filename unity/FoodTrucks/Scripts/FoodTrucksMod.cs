#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(FoodTrucks.FoodTrucksMod))]
[assembly: RegisterModClass(typeof(FoodTrucks.FoodTrucksCity))]

namespace FoodTrucks
{
    // Loads once at game start: vehicle types, the mod's own items and Harmony patches.
    [ModEntryOnInitializationLoad]
    public sealed class FoodTrucksMod : IModBigAmbitions
    {
        public const string Version = "0.10.10";
        internal const string BundleKey = "AssetBundles/foodtrucks.unity3d";
        internal const string TruckTypeName = "foodtrucks:vehicletype_foodtruck";
        internal const string CartTypeName = "foodtrucks:vehicletype_foodcart";
        internal const string CartPrefabPath = "Assets/Mods/FoodTrucks/Vehicles/FoodCart.prefab";
        internal const string TruckPrefabPath = "Assets/Mods/FoodTrucks/Vehicles/FoodTruck.prefab";
        // Every vehicle type this mod has ever registered starts with this, spike types included.
        internal const string VehicleTypePrefix = "foodtrucks:vehicletype_";
        private const string HandVehicleTag = "ba:vehicletag_ishandvehicle";

        private static readonly (string TypeName, string AssetPath)[] VehicleAssets =
        {
            (TruckTypeName, "Assets/Mods/FoodTrucks/Vehicles/FoodTruck.asset"),
            (CartTypeName, "Assets/Mods/FoodTrucks/Vehicles/FoodCart.asset"),
        };

        internal static readonly string[] SoldAtDealer = { CartTypeName, TruckTypeName };

        // The mod's asset bundle, for the prefab fixes applied at city load.
        internal static AssetBundle? Bundle { get; private set; }

        internal static bool IsModVehicle(string? typeName) =>
            typeName != null && typeName.StartsWith(VehicleTypePrefix, StringComparison.Ordinal);

        // "cart" or "truck", the vehicle kinds in economy.json and rules.json; null for any other vehicle.
        internal static string? KindOf(string? typeName) =>
            typeName == CartTypeName ? "cart" : typeName == TruckTypeName ? "truck" : null;

        private readonly List<string> registered = new List<string>();

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        internal static readonly List<string> RegisteredVehicleTypes = new List<string>();

        // The game's id for this mod (its folder for a local mod), for the late attach's log line.
        internal static string? ModId { get; private set; }

        public Task OnLoadAsync(ModContext context)
        {
            Log.Init(context.Logger);
            ModId = context.ModId;
            Log.Info($"Version {Version} loading.");
            DemandData.Load(context.ModRootPath);

            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
            Bundle = bundle;
            if (bundle == null)
            {
                Log.Error($"Asset bundle '{BundleKey}' not found; no vehicles or items registered.");
            }
            else
            {
                foreach (var (typeName, assetPath) in VehicleAssets)
                    RegisterVehicle(bundle, typeName, assetPath);
                ModItems.Register(bundle);
            }

            Patches.Apply();
            BigAmbitions.ModsInternal.ModDiscoveryRegistry.OnDiscoveryUpdated -= SaveGuard.DiscoveryUpdated;
            BigAmbitions.ModsInternal.ModDiscoveryRegistry.OnDiscoveryUpdated += SaveGuard.DiscoveryUpdated;
            // Only with -foodtrucks-test on the game's command line (docs/test-bridge.md); started here so it answers at
            // the main menu.
            try { TestBridge.Start(); }
            catch (Exception ex) { Log.Error("Test bridge: " + ex); }
            return Task.CompletedTask;
        }

        private void RegisterVehicle(AssetBundle bundle, string typeName, string assetPath)
        {
            var type = bundle.LoadAsset<VehicleType>(assetPath);
            if (type == null)
            {
                Log.Error($"Vehicle type asset '{assetPath}' missing from the bundle.");
                return;
            }
            if (type.vehicleTypeName != typeName)
            {
                Log.Error($"Vehicle type asset '{assetPath}' is named '{type.vehicleTypeName}', expected '{typeName}'.");
                return;
            }
            // The serialized tag list has no database; AddTag resolves the vanilla tag index at runtime.
            if (typeName == CartTypeName) type.AddTag(HandVehicleTag);

            if (ModdingAPI.RegisterModVehicleType(type))
            {
                registered.Add(typeName);
                RegisteredVehicleTypes.Add(typeName);
                // The game checks the tag by index, so log the index lookup rather than the string list.
                var handVehicle = type.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.ishandvehicle);
                Log.Info($"Vehicle registered: {typeName} (hand vehicle tag: {handVehicle}, price {type.price}).");
            }
            else
            {
                Log.Error($"Vehicle registration refused: {typeName}.");
            }
        }

        public Task OnUnloadAsync()
        {
            try { TestBridge.Stop(); }
            catch (Exception ex) { Log.Error("Test bridge: " + ex); }
            try { Patches.Remove(); }
            catch (Exception ex) { Log.Error("Unpatch: " + ex); }
            BigAmbitions.ModsInternal.ModDiscoveryRegistry.OnDiscoveryUpdated -= SaveGuard.DiscoveryUpdated;
            FoodSafetyCourse.Unregister();
            foreach (var typeName in registered)
                ModdingAPI.UnregisterModVehicleType(typeName);
            registered.Clear();
            RegisteredVehicleTypes.Clear();
            ModItems.Unregister();
            Bundle = null;
            return Task.CompletedTask;
        }
    }

    internal static class Log
    {
        private static IModLogger? logger;

        // Gets every line this class writes, with its kind ("log", "error", "notice", "phone"): the test bridge's events.
        internal static Action<string, string>? Listener;

        public static void Init(IModLogger modLogger) => logger = modLogger;

        // The game's mod logger adds its own "[Mod:Food Trucks]" prefix.
        public static void Info(string message) => Event("log", message);

        // An info line of another kind for the listener (Notices: "notice", "phone").
        public static void Event(string kind, string message)
        {
            if (logger != null) logger.Info(message);
            else Debug.Log("[Food Trucks] " + message);
            Tell(kind, message);
        }

        // Every error line starts with this marker, once, for log checks.
        internal const string ErrorMarker = "ERROR: ";

        public static void Error(string message)
        {
            if (!message.StartsWith(ErrorMarker, StringComparison.Ordinal)) message = ErrorMarker + message;
            writing = true;
            try
            {
                if (logger != null) logger.Error(message);
                else Debug.LogError("[Food Trucks] " + message);
            }
            finally { writing = false; }
            Tell("error", message);
        }

        // True while this class hands an error line to Unity, so the test bridge doesn't count it twice.
        [ThreadStatic] private static bool writing;
        internal static bool Writing => writing;

        private static void Tell(string kind, string message)
        {
            try { Listener?.Invoke(kind, message); }
            catch (Exception) { /* A listener never stops the mod's logging. */ }
        }

        public static void Error(Exception exception)
        {
            Error(exception.ToString());
        }
    }
}
