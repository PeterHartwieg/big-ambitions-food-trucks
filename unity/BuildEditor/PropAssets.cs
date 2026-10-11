#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FoodTrucks.BuildEditor
{
    // Fixed props the vehicle prefabs carry, from art/props (tools/build-mod.sh copies them into Vehicles/PropArt):
    // the cash register on the cart's counter and the truck's serving counter. Looks only: no collider.
    internal static class PropAssets
    {
        private const string Folder = ModAssets.ModFolder + "/Vehicles/PropArt";
        private const string Register = "CashRegister";

        // A "Cash_Register" child of parent with the register's mesh and material, on layer; returns its renderer.
        public static MeshRenderer AddRegister(Transform parent, Vector3 position, Quaternion rotation, int layer)
        {
            var (mesh, material) = Load(Register);
            var go = new GameObject("Cash_Register");
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderingLayerMask = SurfaceLook.VehicleRenderingLayers;
            var b = mesh.bounds;
            Debug.Log($"[FoodTrucksBuild] Prop {Register} under {parent.name} at {position:F3}, turned {rotation.eulerAngles.y:F0} degrees: {mesh.triangles.Length / 3} tris, bounds {b.min:F3} to {b.max:F3}.");
            return renderer;
        }

        private static (Mesh Mesh, Material Material) Load(string name)
        {
            var fbxPath = $"{Folder}/{name}_Model.fbx";
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, fbxPath)))
                throw new InvalidOperationException($"{fbxPath} missing; tools/build-mod.sh copies it from art/props/{name}.");
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
            var material = SurfaceLook.LitMaterial($"{Folder}/M_{name}.mat", Folder, name);
            return (filter.sharedMesh, material);
        }
    }
}
