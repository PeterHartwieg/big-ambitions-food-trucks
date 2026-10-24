#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The game's logo generator supplies the same artwork as shop signs. A unique private key
    // isolates its cache/files from shops; only SetUpSettings receives the player's display text.
    internal sealed class TruckLogos : MonoBehaviour
    {
        private static readonly Dictionary<string, string> generationNames = new Dictionary<string, string>();
        internal static void GeneratorPrefix(ref string businessName)
        { if (generationNames.TryGetValue(businessName, out var name)) businessName = name; }
        internal static bool Ready
        {
            get
            {
                var method = AccessTools.Method(typeof(BusinessLogoGenerator), "SetUpSettings");
                return method != null && Harmony.GetPatchInfo(method)?.Prefixes.Any(p => p.PatchMethod.DeclaringType == typeof(TruckLogos)) == true;
            }
        }
        private sealed class Display
        {
            internal VehicleController Controller = null!;
            internal Transform? Body;
            internal string Design = "";
            internal GameObject[] Signs = Array.Empty<GameObject>();
            internal Material? Material;
            internal Material? BackingMaterial;
        }
        private readonly Dictionary<int, Display> shown = new Dictionary<int, Display>();
        private readonly Dictionary<string, Texture2D?> textures = new Dictionary<string, Texture2D?>();
        private float nextCheck;
        private bool disposed;

        private void Update()
        {
            foreach (var display in shown.Values)
                foreach (var sign in display.Signs)
                    if (sign != null)
                    {
                        sign.SetActive(display.Controller != null && display.Controller.visible);
                        if (display.Body != null && sign.layer != display.Body.gameObject.layer)
                            foreach (var t in sign.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = display.Body.gameObject.layer;
                    }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + .5f;
            try { Refresh(); }
            catch (Exception ex) { Log.Error("Truck logos: " + ex); }
        }

        private void Refresh()
        {
            var seen = new HashSet<int>();
            foreach (var controller in VehicleHelper.AllPlayerVehicles)
            {
                var instance = controller == null ? null : controller.vehicleInstance;
                if (instance?.vehicleTypeName != FoodTrucksMod.TruckTypeName) continue;
                var key = controller!.GetInstanceID(); seen.Add(key);
                var logo = VehicleLogo.Read(StreetSales.Find(FoodTrucksSession.CurrentState, instance.id)?.Logo);
                var design = logo?.Save().ToString() ?? "";
                if (!shown.TryGetValue(key, out var display))
                    shown[key] = display = new Display { Controller = controller };
                if (display.Design == design && (design == "" || display.Signs.Length == 2 && display.Signs.All(s => s != null))) continue;
                if (logo == null) { Clear(display); display.Design = ""; continue; }
                var body = controller.transform.Find("CarHolder/Body/CheapSaloon");
                if (body == null) continue;
                var texture = Texture(design, logo);
                if (texture == null) continue;
                var template = FoodTrucksMod.Bundle?.LoadAsset<Material>("Assets/Mods/FoodTrucks/Vehicles/M_TruckLogo.mat");
                if (template == null) continue;
                Clear(display); display.Body = body;
                var material = new Material(template) { name = "FoodTrucks.TruckLogo" };
                material.SetTexture("_LogoTexture", texture);
                // Match the native-camera-tested illuminated sign tint; SetColor handles its colour-space conversion.
                material.SetColor("_LogoColour", Color.white * 10f);
                display.Material = material;
                display.BackingMaterial = new Material(material);
                display.BackingMaterial.SetTexture("_LogoTexture", Texture2D.whiteTexture);
                display.BackingMaterial.SetColor("_LogoColour", TruckLogoEditor.Colour(logo.Background) * 10f);
                var signs = new List<GameObject>();
                try
                {
                    foreach (var side in new[] { 1, -1 }) signs.Add(Sign(body.parent, body.gameObject.layer, side, material, display.BackingMaterial, texture));
                    display.Signs = signs.ToArray(); display.Design = design;
                    foreach (var sign in display.Signs) sign.SetActive(controller.visible);
                    Log.Info($"Truck logo: two body signs for {instance.id}.");
                }
                catch { foreach (var sign in signs) if (sign != null) Destroy(sign); Clear(display); throw; }
            }
            foreach (var key in shown.Keys.Where(k => !seen.Contains(k)).ToList())
            { Clear(shown[key]); shown.Remove(key); }
            // Release old designs after replacement/removal. A fleet sharing one design shares its texture.
            var used = new HashSet<string>(shown.Values.Select(d => d.Design));
            foreach (var key in textures.Where(p => p.Value != null && !used.Contains(p.Key)).Select(p => p.Key).ToList())
            { Destroy(textures[key]); textures.Remove(key); }
        }

        private Texture2D? Texture(string design, VehicleLogo logo)
        {
            if (textures.TryGetValue(design, out var texture)) return texture;
            if (!Ready || BusinessLogoGenerator.Instance == null) return null;
            var key = "FoodTrucksLogo-" + Guid.NewGuid().ToString("N");
            var folder = Path.Combine(Application.temporaryCachePath, "FoodTrucksLogos", key);
            textures[design] = null; generationNames[key] = logo.Name;
            try
            {
                BusinessLogoGenerator.Create(key, TruckLogoEditor.Settings(logo), folder, false, () =>
                {
                    try
                    {
                        if (!disposed)
                        {
                            // The high-resolution square includes both icon and name; the native wide sign is text-only.
                            var data = File.ReadAllBytes(Path.Combine(folder, "Billboard.jpg"));
                            var image = new Texture2D(2, 2) { name = "FoodTrucks.Logo", wrapMode = TextureWrapMode.Clamp };
                            if (ImageConversion.LoadImage(image, data))
                            { textures[design] = image; Log.Info($"Truck logo: generated {image.width}x{image.height} sign, corner {ColorUtility.ToHtmlStringRGBA(image.GetPixel(1, 1))}."); }
                            else { Destroy(image); Log.Error("Truck logo: generated image could not be read."); }
                        }
                    }
                    catch (Exception ex) { Log.Error("Truck logo texture: " + ex); }
                    finally { generationNames.Remove(key); DeleteTemporary(folder); }
                });
            }
            catch (Exception ex) { generationNames.Remove(key); DeleteTemporary(folder); Log.Error("Truck logo generation: " + ex); }
            return null;
        }
        private static void DeleteTemporary(string folder)
        { try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (Exception ex) { Log.Error("Truck logo cache cleanup: " + ex.Message); } }

        private static GameObject Sign(Transform body, int layer, int side, Material material, Material backingMaterial, Texture2D texture)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Quad);
            root.name = "FoodTrucks.TruckLogo"; root.layer = layer; root.SetActive(false);
            var collider = root.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; Destroy(collider); }
            root.transform.SetParent(body, false);
            var aspect = texture.width / (float)texture.height;
            var height = Mathf.Min(.55f, .55f / aspect);
            root.transform.localScale = new Vector3(height * aspect, height, 1);
            // The driver-side sign sits above the generator hatch; the customer-side sign below the serving window.
            const float depth = .004f;
            root.transform.localPosition = new Vector3(side * (1.10f + depth), side < 0 ? 2.65f : 1.32f, -.85f);
            root.transform.localRotation = Quaternion.Euler(0, -side * 90, 0);
            var renderer = root.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Backing"; backing.layer = layer; backing.transform.SetParent(root.transform, false);
            backing.transform.localPosition = new Vector3(0, 0, depth / 2 + .001f);
            backing.transform.localScale = new Vector3(1, 1, depth);
            var box = backing.GetComponent<Collider>(); box.enabled = false; Destroy(box);
            var backingRenderer = backing.GetComponent<MeshRenderer>();
            backingRenderer.sharedMaterial = backingMaterial;
            backingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }
        private static void Clear(Display display)
        {
            foreach (var sign in display.Signs) if (sign != null) Destroy(sign);
            display.Signs = Array.Empty<GameObject>();
            if (display.Material != null) Destroy(display.Material);
            if (display.BackingMaterial != null) Destroy(display.BackingMaterial);
            display.Material = null;
            display.BackingMaterial = null;
        }
        private void OnDestroy()
        {
            disposed = true;
            foreach (var display in shown.Values) Clear(display); shown.Clear();
            foreach (var texture in textures.Values) if (texture != null) Destroy(texture); textures.Clear();
        }
    }
}
