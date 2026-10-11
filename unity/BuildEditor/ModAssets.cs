#nullable enable
using System;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FoodTrucks.BuildEditor
{
    // Generates the mod's Unity assets from code so they can be rebuilt and reviewed as text.
    // Entry point for `Unity -batchmode -executeMethod FoodTrucks.BuildEditor.ModAssets.Generate`.
    public static class ModAssets
    {
        public const string ModFolder = "Assets/Mods/FoodTrucks";
        private const string VehiclesFolder = ModFolder + "/Vehicles";

        public const string TruckTypeName = "foodtrucks:vehicletype_foodtruck";
        public const string CartTypeName = "foodtrucks:vehicletype_foodcart";

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
                TruckAssets.GenerateTruck(VehiclesFolder, TruckTypeName);
                EquipmentAssets.Generate(VehiclesFolder);
                // The machines' prefabs use the equipment models and materials made just before.
                ItemAssets.Generate();
                CartAssets.GenerateCart(VehiclesFolder, CartTypeName);
                CommissaryAssets.Generate();
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

        private static string RuntimeVersion()
        {
            var source = File.ReadAllText(ModFolder + "/Scripts/FoodTrucksMod.cs");
            var match = System.Text.RegularExpressions.Regex.Match(source, "const string Version = \"([^\"]+)\"");
            if (!match.Success) throw new InvalidOperationException("No Version constant in FoodTrucksMod.cs.");
            return match.Groups[1].Value;
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
            // The runtime's Version constant is the one source; this assembly can't reference the mod's.
            manifest.Version = RuntimeVersion();
            manifest.AssetBundleName = "foodtrucks.unity3d";
            manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(ModFolder + "/FoodTrucks.asmdef");
            manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ModFolder + "/Locales");
            // Dependencies/ is found by convention; the folder is recreated by the build script.
            manifest.DependenciesFolder = null!;
            manifest.TargetPlatforms = ModTargetPlatforms.Windows;
            if (manifest.ModAssembly == null) throw new InvalidOperationException("FoodTrucks.asmdef not found.");
            EditorUtility.SetDirty(manifest);
        }

        internal static void EnsureFolder(string path)
        {
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
