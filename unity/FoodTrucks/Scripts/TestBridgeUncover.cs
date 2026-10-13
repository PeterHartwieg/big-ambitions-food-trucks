#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using UnityEngine;

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
    //    CityBuildingController.SetCameraBlockMode]. Undone with the game's own toggles (SetCameraBlockMode(false),
    //    SetFadeSate(1)) and put back with them (SetCameraBlockMode(true), SetFadeSate(the saved reveal)).
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

        private readonly List<(ViewBlockingEntity Entity, bool Unblocked, float Reveal)> faded = new List<(ViewBlockingEntity, bool, float)>();
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

        private void Unfade(bool apply)
        {
            foreach (var entity in Entities())
            {
                if (entity == null) continue;
                var reveal = RevealValue?.GetValue(entity) is float r ? r : 1f;
                var blocked = entity.IsInCameraBlockMode;
                if (!blocked && reveal >= 0.999f) continue;
                Uncovered.Add($"{entity.name} ({entity.GetType().Name}, reveal {reveal:F2}{(blocked ? ", blocking the game's camera" : "")})");
                if (!apply) continue;
                var unblocked = false;
                try
                {
                    // False while temporarilyDisableCameraBlock is set; then the entity is left as it is, and only its
                    // fade is lifted.
                    if (blocked) unblocked = entity.SetCameraBlockMode(false);
                    faded.Add((entity, unblocked, reveal));
                    entity.SetFadeSate(1f);
                }
                catch (Exception ex)
                {
                    if (!faded.Any(f => f.Entity == entity)) faded.Add((entity, unblocked, reveal));
                    Log.Error($"Test bridge, uncovering {entity.name}: {ex.Message}");
                }
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
            foreach (var c in Physics.OverlapSphere(from, Reach, 1 << itemLayer, QueryTriggerInteraction.Collide))
                if (c != null) roots.Add(c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform.root);
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

        // Puts back the outline layers, then the fades, each on its own.
        public void Dispose()
        {
            for (var i = outlined.Count - 1; i >= 0; i--)
            {
                var (thing, layer) = outlined[i];
                try { if (thing != null) thing.layer = layer; }
                catch (Exception ex) { Log.Error("Test bridge, putting an outline back: " + ex.Message); }
            }
            outlined.Clear();
            for (var i = faded.Count - 1; i >= 0; i--)
            {
                var (entity, unblocked, reveal) = faded[i];
                if (entity == null) continue;
                try
                {
                    if (unblocked) entity.SetCameraBlockMode(true);
                    entity.SetFadeSate(reveal);
                }
                catch (Exception ex) { Log.Error($"Test bridge, putting {entity.name}'s fade back: {ex.Message}"); }
            }
            faded.Clear();
        }
    }
}
