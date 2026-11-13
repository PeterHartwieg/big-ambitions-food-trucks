#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FoodTrucks.BuildEditor
{
    internal static class EquipmentAssets
    {
        public static readonly string[] Models =
        {
            "CoffeeStation", "CornGrill", "CottonCandyMachine", "DrinksCooler", "DumplingSteamer",
            "Griddle", "KabobGrill", "MerchRack", "PopcornMachine", "SlushieMachine",
            "BurgerStation", "HotDogRoller", "IceCreamCabinet", "PastryCase", "PizzaOven",
            "StorageCabinet", "CartDrinksCooler", "TruckDrinksCooler",
        };

        public static void Generate(string vehiclesFolder)
        {
            var folder = vehiclesFolder + "/EquipmentArt";
            foreach (var name in Models)
            {
                var fbxPath = ModelPath(folder, name);
                var texturePath = $"{folder}/T_{name}_BC.png";
                if (!File.Exists(Path.Combine(ProjectRoot, fbxPath)) || !File.Exists(Path.Combine(ProjectRoot, texturePath)))
                    throw new InvalidOperationException($"{fbxPath} or its texture missing; tools/build-mod.sh copies them from art/equipment/{name}.");
                var model = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
                if (SurfaceLook.NormalsAsBaked(model) || model.isReadable || model.importAnimation || model.materialImportMode != ModelImporterMaterialImportMode.None || model.globalScale != 1f)
                {
                    model.isReadable = false;
                    model.importAnimation = false;
                    model.materialImportMode = ModelImporterMaterialImportMode.None;
                    model.globalScale = 1f;
                    model.SaveAndReimport();
                }
                var filter = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath).GetComponentsInChildren<MeshFilter>(true)
                                 .FirstOrDefault(f => f.name == name + "_Body")
                             ?? throw new InvalidOperationException($"{fbxPath} lacks {name}_Body.");
                if (filter.transform.localPosition.magnitude > 0.005f || Quaternion.Angle(filter.transform.localRotation, Quaternion.identity) > 0.5f)
                    throw new InvalidOperationException($"{name}_Body in {fbxPath} is not at the origin with rotation 0.");
                var mesh = filter.sharedMesh;
                var b = mesh.bounds;
                Debug.Log($"[FoodTrucksBuild] Equipment {name}: {mesh.triangles.Length / 3} tris, bounds {b.min:F3} to {b.max:F3}.");

                var material = SurfaceLook.LitMaterial($"{folder}/M_{name}.mat", folder, name);
                ItemAssets.IconImport($"{folder}/Icons/{IconName(name)}.png");
                var root = new GameObject(name);
                try
                {
                    root.layer = LayerMask.NameToLayer("Vehicles");
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    root.AddComponent<MeshRenderer>().sharedMaterial = material;
                    SurfaceLook.NoStreetDecals(root);
                    PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/{name}_Display.prefab");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        internal static string ModelPath(string folder, string name) => $"{folder}/{name}_Model.fbx";

        internal static string IconName(string name) => "Icon_" + name;

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;
    }
}
