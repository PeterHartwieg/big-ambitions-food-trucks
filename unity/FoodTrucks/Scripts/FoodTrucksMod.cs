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
    // Loads once at game start: vehicle types and Harmony patches.
    [ModEntryOnInitializationLoad]
    public sealed class FoodTrucksMod : IModBigAmbitions
    {
        public const string Version = "0.8.0";
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

        public Task OnLoadAsync(ModContext context)
        {
            Log.Init(context.Logger);
            Log.Info($"Version {Version} loading.");
            DemandData.Load(context.ModRootPath);

            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
            Bundle = bundle;
            if (bundle == null)
            {
                Log.Error($"Asset bundle '{BundleKey}' not found; no vehicles registered.");
            }
            else
            {
                foreach (var (typeName, assetPath) in VehicleAssets)
                    RegisterVehicle(bundle, typeName, assetPath);
            }

            Patches.Apply();
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
            Patches.Remove();
            foreach (var typeName in registered)
                ModdingAPI.UnregisterModVehicleType(typeName);
            registered.Clear();
            RegisteredVehicleTypes.Clear();
            Bundle = null;
            return Task.CompletedTask;
        }
    }

    internal static class Log
    {
        private static IModLogger? logger;

        public static void Init(IModLogger modLogger) => logger = modLogger;

        // The game's mod logger adds its own "[Mod:Food Trucks]" prefix.
        public static void Info(string message)
        {
            if (logger != null) logger.Info(message);
            else Debug.Log("[Food Trucks] " + message);
        }

        public static void Error(string message)
        {
            if (logger != null) logger.Error(message);
            else Debug.LogError("[Food Trucks] " + message);
        }

        public static void Error(Exception exception)
        {
            Error(exception.ToString());
        }
    }
}
