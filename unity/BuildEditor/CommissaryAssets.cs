#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks.BuildEditor
{
    // The commissary bay's street furniture (art/commissary, art/README.md "Commissary bay"): the road paint material,
    // the LOADING ZONE lettering, the kerb sign with one plate material per commissary, and the props by the door.
    // tools/build-mod.sh copies each FBX (as <Name>_Model.fbx) and texture into Commissary/BayArt; this makes the
    // HDRP/Lit materials and one <Name>_Display.prefab per model there. BayMarkers and BayProps load them by path.
    // The lettering and the sign have no collider; each prop has a box collider and a carving NavMeshObstacle of its
    // footprint, so people walk round it.
    internal static class CommissaryAssets
    {
        public const string Folder = ModAssets.ModFolder + "/Commissary/BayArt";
        public static readonly string[] Props = { "BayPalletStack", "BayHandTruck", "BayRollCage", "BayDumpster" };

        public static void Generate()
        {
            ModAssets.EnsureFolder(Folder);
            // Road paint takes the street's dirt decals like the asphalt round it, so it isn't cleaner than the road.
            var paint = Lit($"{Folder}/M_BayPaint.mat", $"{Folder}/T_BayPaint_BaseColor.png", smoothness: 0.2f, decals: true);
            Display("BayLettering", new Dictionary<string, Material> { ["M_BayPaint"] = paint }, collider: false);

            var pole = Lit($"{Folder}/M_BaySignPole.mat", $"{Folder}/T_BaySignPole_BaseColor.png", smoothness: 0.45f, metallic: 0.6f);
            var plate = Lit($"{Folder}/M_BaySign.mat", $"{Folder}/T_BaySign_BaseColor.png", smoothness: 0.4f);
            Display("BaySign", new Dictionary<string, Material> { ["M_BaySignPole"] = pole, ["M_BaySign"] = plate }, collider: false);
            // One plate per commissary, named on it; BayProps swaps it in by the commissary's id.
            foreach (var texture in Directory.GetFiles(Path.Combine(ProjectRoot, Folder), "T_BaySign_*_BaseColor.png"))
            {
                var id = Path.GetFileName(texture).Substring("T_BaySign_".Length).Replace("_BaseColor.png", "");
                Lit($"{Folder}/M_BaySign_{id}.mat", $"{Folder}/T_BaySign_{id}_BaseColor.png", smoothness: 0.4f);
            }

            var materials = new Dictionary<string, Material>();
            foreach (var name in new[] { "Wood", "Crate", "Produce", "Greens", "Carton", "Steel", "Dumpster", "DumpsterLid" })
                materials["M_Bay" + name] = Lit($"{Folder}/M_Bay{name}.mat", $"{Folder}/T_Bay{name}_BaseColor.png",
                    smoothness: name == "Steel" ? 0.4f : 0.15f, metallic: name == "Steel" ? 0.5f : 0f);
            foreach (var name in Props) Display(name, materials, collider: true);
        }

        // <name>_Display.prefab from <name>_Model.fbx: every mesh of the model with the FBX's materials remapped to ours
        // (by the Blender material names), at the model's own transforms.
        private static void Display(string name, Dictionary<string, Material> remap, bool collider)
        {
            var fbx = $"{Folder}/{name}_Model.fbx";
            if (!File.Exists(Path.Combine(ProjectRoot, fbx)))
                throw new InvalidOperationException($"{fbx} missing; tools/build-mod.sh copies it from art/commissary.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            importer.isReadable = false;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var pair in remap)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject(name);
            try
            {
                var bounds = new Bounds();
                var first = true;
                foreach (var source in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = source.GetComponent<MeshFilter>();
                    var used = source.sharedMaterials;
                    var missing = used.Where(m => m == null || !remap.ContainsValue(m)).Select(m => m == null ? "(none)" : m.name).ToList();
                    if (missing.Count > 0)
                        throw new InvalidOperationException($"{fbx}: {source.name} has materials not remapped: {string.Join(", ", missing)}.");
                    var part = new GameObject(source.name);
                    part.transform.SetParent(root.transform, false);
                    part.transform.localPosition = source.transform.localPosition;
                    part.transform.localRotation = source.transform.localRotation;
                    part.transform.localScale = source.transform.localScale;
                    part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var renderer = part.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = used;
                    var b = filter.sharedMesh.bounds;
                    var world = new Bounds(part.transform.TransformPoint(b.center), Vector3.Scale(b.size, part.transform.lossyScale));
                    if (first) bounds = world; else bounds.Encapsulate(world);
                    first = false;
                    Debug.Log($"[FoodTrucksBuild] Commissary {name}/{source.name}: {filter.sharedMesh.triangles.Length / 3} tris, {used.Length} materials, bounds {world.min:F2} to {world.max:F2}.");
                }
                if (first) throw new InvalidOperationException($"{fbx} has no meshes.");
                if (collider)
                {
                    var box = root.AddComponent<BoxCollider>();
                    box.center = bounds.center;
                    box.size = bounds.size;
                    var obstacle = root.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.center = bounds.center;
                    obstacle.size = bounds.size;
                    obstacle.carving = true;
                    obstacle.carveOnlyStationary = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{Folder}/{name}_Display.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // An HDRP/Lit material with the texture as its base colour map (sRGB), opaque.
        private static Material Lit(string path, string texturePath, float smoothness, float metallic = 0f, bool decals = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath)
                           ?? throw new InvalidOperationException($"{texturePath} missing; tools/build-mod.sh copies it from art/commissary.");
            if (importer.textureType != TextureImporterType.Default || !importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit shader not found."));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_SupportDecals", decals ? 1f : 0f);
            if (decals) material.DisableKeyword("_DISABLE_DECALS");
            else material.EnableKeyword("_DISABLE_DECALS");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;
    }
}
