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
    [ModEntryOnInitializationLoad]
    public sealed class FoodTrucksMod : IModBigAmbitions
    {
        public const string Version = "0.10.12";
        internal const string BundleKey = "AssetBundles/foodtrucks.unity3d";
        internal const string TruckTypeName = "foodtrucks:vehicletype_foodtruck";
        internal const string CartTypeName = "foodtrucks:vehicletype_foodcart";
        internal const string CartPrefabPath = "Assets/Mods/FoodTrucks/Vehicles/FoodCart.prefab";
        internal const string TruckPrefabPath = "Assets/Mods/FoodTrucks/Vehicles/FoodTruck.prefab";
        internal const string VehicleTypePrefix = "foodtrucks:vehicletype_";
        private const string HandVehicleTag = "ba:vehicletag_ishandvehicle";

        private static readonly (string TypeName, string AssetPath)[] VehicleAssets =
        {
            (TruckTypeName, "Assets/Mods/FoodTrucks/Vehicles/FoodTruck.asset"),
            (CartTypeName, "Assets/Mods/FoodTrucks/Vehicles/FoodCart.asset"),
        };

        internal static readonly string[] SoldAtDealer = { CartTypeName, TruckTypeName };

        internal static AssetBundle? Bundle { get; private set; }

        internal static bool IsModVehicle(string? typeName) =>
            typeName != null && typeName.StartsWith(VehicleTypePrefix, StringComparison.Ordinal);

        internal static string? KindOf(string? typeName) =>
            typeName == CartTypeName ? "cart" : typeName == TruckTypeName ? "truck" : null;

        private readonly List<string> registered = new List<string>();

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        internal static readonly List<string> RegisteredVehicleTypes = new List<string>();

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
            if (typeName == CartTypeName) type.AddTag(HandVehicleTag);

            if (ModdingAPI.RegisterModVehicleType(type))
            {
                registered.Add(typeName);
                RegisteredVehicleTypes.Add(typeName);
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

        internal static Action<string, string>? Listener;

        public static void Init(IModLogger modLogger) => logger = modLogger;

        public static void Info(string message) => Event("log", message);

        public static void Event(string kind, string message)
        {
            if (logger != null) logger.Info(message);
            else Debug.Log("[Food Trucks] " + message);
            Tell(kind, message);
        }

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

        [ThreadStatic] private static bool writing;
        internal static bool Writing => writing;

        private static void Tell(string kind, string message)
        {
            try { Listener?.Invoke(kind, message); }
            catch (Exception) {  }
        }

        public static void Error(Exception exception)
        {
            Error(exception.ToString());
        }
    }
}
