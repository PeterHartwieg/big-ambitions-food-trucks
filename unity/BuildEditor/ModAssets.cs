#nullable enable
using System;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace FoodTrucks.BuildEditor
{
    // Generates the mod's Unity assets from code so they can be rebuilt and reviewed as text.
    // Entry point for `Unity -batchmode -executeMethod FoodTrucks.BuildEditor.ModAssets.Generate`.
    public static class ModAssets
    {
        public const string ModFolder = "Assets/Mods/FoodTrucks";
        private const string VehiclesFolder = ModFolder + "/Vehicles";
        private const string ExampleVehicleType = "Assets/Mods/Example-Vehicle/TurboHonza.asset";
        private const string ExampleVehiclePrefab = "Assets/Mods/Example-Vehicle/TurboHonza.prefab";

        public const string TruckTypeName = "foodtrucks:vehicletype_spiketruck";
        public const string CartTypeName = "foodtrucks:vehicletype_spikecart";

        [MenuItem("Big Ambitions/Food Trucks/Generate assets")]
        public static void GenerateFromMenu() => Run(exitWhenDone: false);

        public static void Generate() => Run(exitWhenDone: true);

        private static void Run(bool exitWhenDone)
        {
            var code = 0;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                EnsureFolder(VehiclesFolder);
                EnsureManifest();
                GenerateTruck();
                CartAssets.GenerateCart(VehiclesFolder, CartTypeName);
                AssetDatabase.SaveAssets();
                Debug.Log("[FoodTrucksBuild] OK: generated assets.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[FoodTrucksBuild] FAILED: " + ex);
                code = 1;
            }
            if (exitWhenDone) EditorApplication.Exit(code);
        }

        private static void EnsureManifest()
        {
            var path = ModFolder + "/ModManifest.asset";
            var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(path);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<BAModManifest>();
                AssetDatabase.CreateAsset(manifest, path);
            }
            manifest.ModId = "FoodTrucks";
            manifest.DisplayName = "Food Trucks";
            manifest.Author = "Peter Hartwieg";
            manifest.Version = "0.0.1";
            manifest.AssetBundleName = "foodtrucks.unity3d";
            manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(ModFolder + "/FoodTrucks.asmdef");
            manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ModFolder + "/Locales");
            // Dependencies/ is found by convention; the folder is recreated by the build script.
            manifest.DependenciesFolder = null!;
            manifest.TargetPlatforms = ModTargetPlatforms.Windows;
            if (manifest.ModAssembly == null) throw new InvalidOperationException("FoodTrucks.asmdef not found.");
            EditorUtility.SetDirty(manifest);
        }

        // Spike 1: the SDK's example car under a new type name. Placeholder until the real truck exists.
        private static void GenerateTruck()
        {
            var typePath = VehiclesFolder + "/SpikeTruck.asset";
            var prefabPath = VehiclesFolder + "/SpikeTruck.prefab";
            CopyFresh(ExampleVehicleType, typePath);
            CopyFresh(ExampleVehiclePrefab, prefabPath);

            var type = AssetDatabase.LoadAssetAtPath<VehicleType>(typePath);
            type.vehicleTypeName = TruckTypeName;
            type.price = 1f;
            type.maxCargoCapacity = 20;
            type.taxDeductible = true;
            EditorUtility.SetDirty(type);

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var controller = root.GetComponent<VehicleController>();
                if (controller == null) throw new InvalidOperationException("Example prefab has no VehicleController.");
                controller.vehicleType = type;
                root.name = "SpikeTruck";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Overwrites the file but keeps an existing .meta, so the asset GUID stays stable across regenerations.
        private static void CopyFresh(string from, string to)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath)!;
            File.Copy(Path.Combine(projectRoot, from), Path.Combine(projectRoot, to), overwrite: true);
            AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
        }

        internal static void EnsureFolder(string path)
        {
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
