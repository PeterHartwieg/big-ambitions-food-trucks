#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodTrucks
{
    // What the game does to buildings for its own camera, undone for one frame of the bridge's camera and put back
    // exactly before the game's camera renders (docs/test-bridge.md, "shoot"; research note of 13 October 2026).
    //
    // 1. The view-blocking fade. ViewBlockingObjectManager, on the game's main camera, puts every ViewBlockingEntity
    //    between that camera and the player (buildings, trees, bridges) into camera-block mode: its renderersToFade get
    //    _Reveal towards 0 through a property block (a pale see-through copy), renderersToHide go shadows-only, signs
    //    switch off, and a building's bottom plane shows (the flat white-edged cut). That is renderer state, so the
    //    bridge's camera saw it too: the ghost facades of live sessions 3 to 6, which came and went with where the player
    //    stood relative to the game's camera [code, 1.0: ViewBlockingObjectManager, ViewBlockingEntity,
    //    CityBuildingController.SetCameraBlockMode]. Undone at renderer level only (review round 10, Astra and Sol:
    //    the game's own toggles flip the logical flag first and SetActive objects, which can't be undone exactly):
    //    _Reveal is set to 1 in a copy of each faded renderer's property block; a blocked entity's hidden renderers
    //    (renderersToHide, its signs) are shown again (shadows-only back to on, or a disabled renderer enabled), and its
    //    bottom plane's renderers are disabled. Every renderer's exact property block, enabled flag and shadow mode is
    //    saved before anything changes and written back, each on its own. The building's logical block state, its
    //    layers and object activation are never touched, so objectsToDisable (and an attraction's riders) stay hidden.
    // 2. The hover and map-filter outline. A building under the mouse in the game's view, or highlighted by a map
    //    filter, has its objects on the BuildingsOutlined layer (CityBuildingController.SetOutlineLayer); items under the
    //    mouse are on InteractiveItemsOutlined. Moved to their plain layers for the frame and back to the exact layer
    //    each had.
    //
    // Everything runs on the main thread inside one Update, round one Camera.Render, so nothing reaches the game's own
    // view. Every step is undone in reverse order in a finally, each one on its own, so one failure doesn't leave the
    // others changed.
    internal sealed class TestBridgeUncover : IDisposable
    {
        private const float Reach = 250f;

        private static readonly FieldInfo? ActiveEntities = AccessTools.Field(typeof(ViewBlockingObjectManager), "ActiveEntities");
        private static readonly FieldInfo? RevealValue = AccessTools.Field(typeof(ViewBlockingEntity), "_currentRevealValue");
        private static readonly FieldInfo? Signs = AccessTools.Field(typeof(CityBuildingController), "buildingSigns");
        private static readonly FieldInfo? KeepGroundPlane = AccessTools.Field(typeof(CityBuildingController), "keepGroundPlaneAlways");
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");

        // A renderer as it was: its property block (null when it had none), enabled flag and shadow mode.
        private sealed class RendererState
        {
            public Renderer Renderer = null!;
            public MaterialPropertyBlock? Block;
            public bool Enabled;
            public ShadowCastingMode Shadows;
        }

        private readonly List<RendererState> renderers = new List<RendererState>();
        private readonly HashSet<Renderer> saved = new HashSet<Renderer>();
        private readonly List<(GameObject Thing, int Layer)> outlined = new List<(GameObject, int)>();

        // Names for the shot's reply: what this frame undid.
        internal readonly List<string> Uncovered = new List<string>();
        internal readonly List<string> Outlined = new List<string>();

        private TestBridgeUncover() { }

        // Undoes both round a camera at `from`; Dispose puts everything back. With `apply` false nothing changes, but the
        // lists still say what would have been undone (the as-is picture of `compare`).
        internal static TestBridgeUncover Begin(Vector3 from, bool apply)
        {
            var scope = new TestBridgeUncover();
            try
            {
                scope.Unfade(apply);
                scope.Unoutline(from, apply);
            }
            catch
            {
                scope.Dispose();
                throw;
            }
            return scope;
        }

        private static IEnumerable<ViewBlockingEntity> Entities()
        {
            if (ActiveEntities?.GetValue(null) is List<ViewBlockingEntity> list) return list.ToList();
            // Without the game's list: every entity in block mode (a scene-wide search; only if the field is gone).
            return UnityEngine.Object.FindObjectsByType<ViewBlockingEntity>(FindObjectsSortMode.None).Where(e => e != null && e.IsInCameraBlockMode);
        }

        // For state: what the game fades for its own camera now, and where that camera is (to stand the player where a
        // building comes between them, for a check of the shots).
        internal static Newtonsoft.Json.Linq.JArray ViewBlocked()
        {
            var list = new Newtonsoft.Json.Linq.JArray();
            foreach (var entity in Entities())
            {
                if (entity == null) continue;
                var reveal = RevealValue?.GetValue(entity) is float rv ? rv : 1f;
                if (!entity.IsInCameraBlockMode && reveal >= 0.999f) continue;
                list.Add($"{entity.name} ({entity.GetType().Name}, reveal {reveal:F2}{(entity.IsInCameraBlockMode ? ", blocking the game's camera" : "")})");
            }
            return list;
        }

        // Saves a renderer's state once, before its first change.
        private bool Save(Renderer? r)
        {
            if (r == null) return false;
            if (saved.Contains(r)) return true;
            var state = new RendererState { Renderer = r, Enabled = r.enabled, Shadows = r.shadowCastingMode };
            if (r.HasPropertyBlock())
            {
                state.Block = new MaterialPropertyBlock();
                r.GetPropertyBlock(state.Block);
            }
            renderers.Add(state);
            saved.Add(r);
            return true;
        }

        // A renderer the game hid for block mode, shown again: shadows-only back to casting, or a disabled renderer (the
        // game disables those whose shadow mode is off) enabled.
        private void Show(Renderer? r)
        {
            if (r == null || r.CompareTag("BuildingLOD")) return;
            if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
            {
                if (Save(r)) r.shadowCastingMode = ShadowCastingMode.On;
            }
            else if (r.shadowCastingMode == ShadowCastingMode.Off && !r.enabled)
            {
                if (Save(r)) r.enabled = true;
            }
        }

        private void Unfade(bool apply)
        {
            foreach (var entity in Entities())
            {
                if (entity == null) continue;
                var reveal = RevealValue?.GetValue(entity) is float rv ? rv : 1f;
                var blocked = entity.IsInCameraBlockMode;
                if (!blocked && reveal >= 0.999f) continue;
                Uncovered.Add($"{entity.name} ({entity.GetType().Name}, reveal {reveal:F2}{(blocked ? ", blocking the game's camera" : "")})");
                if (!apply) continue;
                try
                {
                    foreach (var r in entity.renderersToFade ?? new List<Renderer>())
                    {
                        if (!Save(r)) continue;
                        var block = new MaterialPropertyBlock();
                        r.GetPropertyBlock(block);
                        block.SetFloat(RevealId, 1f);
                        r.SetPropertyBlock(block);
                    }
                    if (!blocked) continue;
                    foreach (var r in entity.renderersToHide ?? new List<Renderer>()) Show(r);
                    if (entity is CityBuildingController building)
                    {
                        if (Signs?.GetValue(building) is List<BuildingSignController> signs)
                            foreach (var sign in signs)
                                if (sign != null)
                                {
                                    Show(sign.signMeshRenderer);
                                    Show(sign.lightMeshRenderer);
                                }
                        var keep = KeepGroundPlane?.GetValue(building) is bool k && k;
                        if (!keep && building.bottomPlane != null)
                            foreach (var r in building.bottomPlane.GetComponentsInChildren<Renderer>())
                                if (r.enabled && Save(r)) r.enabled = false;
                    }
                }
                catch (Exception ex) { Log.Error($"Test bridge, uncovering {entity.name}: {ex.Message}"); throw; }
            }
        }

        private void Unoutline(Vector3 from, bool apply)
        {
            var buildingLayer = LayerHelper.BuildingOutlinedLayerIndex;
            var itemLayer = LayerHelper.InteractiveItemsOutlinedLayerIndex;
            foreach (var (controller, _) in World.AllBuildings())
            {
                if (controller == null || (controller.transform.position - from).sqrMagnitude > Reach * Reach) continue;
                var before = outlined.Count;
                foreach (var t in controller.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer == buildingLayer) Swap(t.gameObject, LayerHelper.BuildingLayerIndex, apply);
                if (outlined.Count > before || (!apply && Has(controller.transform, buildingLayer))) Outlined.Add(controller.name);
            }
            var roots = new HashSet<Transform>();
            // From each collider up to its item (or its body), not the scene root: an item's root can be the game's
            // container of every owned item and vehicle (review round 10).
            foreach (var c in Physics.OverlapSphere(from, Reach, 1 << itemLayer, QueryTriggerInteraction.Collide))
            {
                if (c == null) continue;
                var item = c.GetComponentInParent<ItemController>();
                roots.Add(item != null ? item.transform : c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform);
            }
            foreach (var root in roots)
            {
                var before = outlined.Count;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer == itemLayer) Swap(t.gameObject, LayerHelper.InteractiveItemsLayerIndex, apply);
                if (outlined.Count > before || (!apply && Has(root, itemLayer))) Outlined.Add(root.name);
            }
        }

        private static bool Has(Transform root, int layer) => root.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.layer == layer);

        private void Swap(GameObject thing, int layer, bool apply)
        {
            if (!apply) return;
            outlined.Add((thing, thing.layer));
            thing.layer = layer;
        }

        // Puts back the outline layers, then every renderer, each on its own.
        public void Dispose()
        {
            for (var i = outlined.Count - 1; i >= 0; i--)
            {
                var (thing, layer) = outlined[i];
                try { if (thing != null) thing.layer = layer; }
                catch (Exception ex) { Log.Error("Test bridge, putting an outline back: " + ex.Message); }
            }
            outlined.Clear();
            for (var i = renderers.Count - 1; i >= 0; i--)
            {
                var state = renderers[i];
                if (state.Renderer == null) continue;
                try { state.Renderer.SetPropertyBlock(state.Block); }
                catch (Exception ex) { Log.Error($"Test bridge, putting {state.Renderer.name}'s property block back: {ex.Message}"); }
                try { state.Renderer.shadowCastingMode = state.Shadows; }
                catch (Exception ex) { Log.Error($"Test bridge, putting {state.Renderer.name}'s shadow mode back: {ex.Message}"); }
                try { state.Renderer.enabled = state.Enabled; }
                catch (Exception ex) { Log.Error($"Test bridge, putting {state.Renderer.name}'s enabled flag back: {ex.Message}"); }
            }
            renderers.Clear();
            saved.Clear();
        }
    }
}
