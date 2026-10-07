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
        public const string Version = "0.0.1-spike";
        internal const string BundleKey = "AssetBundles/foodtrucks.unity3d";
        internal const string TruckTypeName = "foodtrucks:vehicletype_spiketruck";
        internal const string CartTypeName = "foodtrucks:vehicletype_spikecart";
        private const string HandVehicleTag = "ba:vehicletag_ishandvehicle";

        private static readonly (string TypeName, string AssetPath)[] VehicleAssets =
        {
            (TruckTypeName, "Assets/Mods/FoodTrucks/Vehicles/SpikeTruck.asset"),
            (CartTypeName, "Assets/Mods/FoodTrucks/Vehicles/SpikeCart.asset"),
        };

        private readonly List<string> registered = new List<string>();

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        internal static readonly List<string> RegisteredVehicleTypes = new List<string>();

        public Task OnLoadAsync(ModContext context)
        {
            Log.Init(context.Logger);
            Log.Info($"Version {Version} loading.");

            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
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
                Log.Info($"Vehicle registered: {typeName} (hand vehicle tag: {type.HasTag(HandVehicleTag)}, price {type.price}).");
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
            return Task.CompletedTask;
        }
    }

    internal static class Log
    {
        private static IModLogger? logger;

        public static void Init(IModLogger modLogger) => logger = modLogger;

        // Every line carries the same prefix so it can be found in Player.log.
        public static void Info(string message)
        {
            if (logger != null) logger.Info("[Food Trucks] " + message);
            else Debug.Log("[Food Trucks] " + message);
        }

        public static void Error(string message)
        {
            if (logger != null) logger.Error("[Food Trucks] " + message);
            else Debug.LogError("[Food Trucks] " + message);
        }

        public static void Error(Exception exception)
        {
            Error(exception.ToString());
        }
    }
}
