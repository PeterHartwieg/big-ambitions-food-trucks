#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace FoodTrucks.BuildEditor
{
    // Materials and renderer settings shared by every model the mod ships (art/README.md, "Surface look").
    // art/tools/game_look.py writes three maps per model next to its FBX: T_<Name>_BC (base colour, sRGB),
    // T_<Name>_M (HDRP mask: R metallic, G ambient occlusion, B unused, A smoothness) and T_<Name>_N
    // (tangent-space normal map, OpenGL/Y up). The game's own props use the same HDRP/Lit layout with all three
    // maps and the remaps at their defaults, so the values in the maps are the values the game renders.
    internal static class SurfaceLook
    {
        // HDRP rendering layers: bit 0 is the default light layer, bit 8 the default decal layer. The game's
        // vehicles (M_Van, M_VanBig, the taxi, the hand truck) render with 1, so the street's dirt and litter
        // decal projectors (decal layer 0) skip them; its props in buildings use 257. Read from the 1.0 prefabs
        // bundle (MeshRenderer.m_RenderingLayerMask).
        public const uint VehicleRenderingLayers = 1;

        // An HDRP/Lit material at path with the model's three maps from folder (T_<name>_BC/_M/_N.png).
        public static Material LitMaterial(string path, string folder, string name)
        {
            var bc = Texture($"{folder}/T_{name}_BC.png", TextureImporterType.Default, srgb: true);
            // sRGB, as the game imports its own mask maps (m_ColorSpace 1 on T_HotdogHeater_Mask Map, Van_Mask and
            // the rest): only the AO in G is affected, the values were measured and set as stored bytes.
            var mask = Texture($"{folder}/T_{name}_M.png", TextureImporterType.Default, srgb: true);
            var normal = Texture($"{folder}/T_{name}_N.png", TextureImporterType.NormalMap, srgb: false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit shader not found.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseColorMap", bc);
            material.SetTexture("_MainTex", bc);
            material.SetTexture("_MaskMap", mask);
            material.SetTexture("_NormalMap", normal);
            material.SetFloat("_NormalScale", 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_MetallicRemapMin", 0f);
            material.SetFloat("_MetallicRemapMax", 1f);
            material.SetFloat("_SmoothnessRemapMin", 0f);
            material.SetFloat("_SmoothnessRemapMax", 1f);
            material.SetFloat("_AORemapMin", 0f);
            material.SetFloat("_AORemapMax", 1f);
            material.SetFloat("_NormalMapSpace", 0f);
            material.EnableKeyword("_MASKMAP");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
            // No decals on the mod's models: the material refuses them, and the renderers leave the decal
            // layer (NoStreetDecals), as the game's vehicles do.
            material.SetFloat("_SupportDecals", 0f);
            material.EnableKeyword("_DISABLE_DECALS");
            EditorUtility.SetDirty(material);
            return material;
        }

        // Every renderer below root leaves HDRP's default decal layer, so street dirt never projects onto it.
        public static void NoStreetDecals(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.renderingLayerMask = VehicleRenderingLayers;
        }

        // Import the FBX's own normals (smoothed and weighted by game_look.py) and Mikk tangents, which the
        // normal maps were baked against.
        public static bool NormalsAsBaked(ModelImporter model)
        {
            if (model.importNormals == ModelImporterNormals.Import && model.importTangents == ModelImporterTangents.CalculateMikk) return false;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.CalculateMikk;
            return true;
        }

        private static Texture2D Texture(string path, TextureImporterType type, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path)
                           ?? throw new InvalidOperationException($"{path} missing; tools/build-mod.sh copies it from art/ (art/tools/game_look.py makes it).");
            if (importer.textureType != type || importer.sRGBTexture != srgb)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
