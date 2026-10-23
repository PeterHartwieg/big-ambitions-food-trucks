#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // Fitted equipment on show. On a cart's counter: the first fitted piece on the front spot, the second on the
    // rear (CartAssets' EquipmentFront and EquipmentRear). In a truck: all four on the back counter opposite the
    // hatch, full size, the cooking line, filled from the middle out (TruckAssets' spots, which carry the turn
    // that has each piece face the cook and the hatch). A piece
    // keeps its spot while it stays fitted. economy.json's "display" names each piece's model in the bundle; a
    // piece without one leaves its spot empty. Every half second it compares each vehicle's fitted pieces with
    // what it shows, so fitting, removing and loading need no hooks. While a cart is open the pieces face the side
    // its queue forms on (CartQueueView's choice); closed, or with no line laid out, they face right. A truck's
    // always face the hatch. The models have no colliders. They follow the vehicle's visible flag every
    // frame: the game hides a vehicle (EntityController.Hide, e.g. on another floor of a multi-floor building) by
    // disabling only the renderers in its own list, which the pieces aren't in.
    internal sealed class VehicleDisplays : MonoBehaviour
    {
        private const float CheckSeconds = 0.5f;
        private const string ModelFolder = "Assets/Mods/FoodTrucks/Vehicles/EquipmentArt/";

        private sealed class Layout
        {
            public Layout(string kind, string body, bool turns, string[] spots, string[] labels)
            {
                Kind = kind;
                Body = body;
                Turns = turns;
                Spots = spots;
                Labels = labels;
            }

            public string Kind { get; }
            // The renderer whose layer the pieces follow.
            public string Body { get; }
            // Whether the pieces turn to the queue's side (the cart) or keep their spot's facing (the truck).
            public bool Turns { get; }
            public string[] Spots { get; }
            public string[] Labels { get; }
        }

        private static readonly Layout Cart = new Layout("cart", "Cart_Body", turns: true,
            new[] { "EquipmentFront", "EquipmentRear", EquipmentLayout.CoolerPlace }, new[] { "front", "rear", "customer cooler" });
        private static readonly Layout Truck = new Layout("truck", "CarHolder/Body/CheapSaloon", turns: false,
            new[] { "EquipmentLine2", "EquipmentLine3", "EquipmentLine1", "EquipmentLine4", EquipmentLayout.CoolerPlace },
            new[] { "line 2", "line 3", "line 1", "line 4", "customer cooler" });

        // By the controller's instance ID: a vehicle spawned again (out of range and back) has a new one.
        private readonly Dictionary<int, Shown> shown = new Dictionary<int, Shown>();
        private readonly Dictionary<string, GameObject?> models = new Dictionary<string, GameObject?>();
        // Vehicle, spot and piece already logged, so each fitted piece is logged once per session.
        private readonly HashSet<string> logged = new HashSet<string>();
        private float nextCheck;

        private sealed class Shown
        {
            public VehicleController? Controller;
            public Transform? Body;
            public int Layer = -1;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public bool Visible = true;
            public Layout Layout = Cart;
            public Transform?[] Spots = Array.Empty<Transform?>();
            public string?[] Pieces = Array.Empty<string?>();
            public GameObject?[] Objects = Array.Empty<GameObject?>();
            public int Side;
            // The cart's umbrella: 1 open, 0 furled, -1 not set yet, -2 the model has no canopies.
            public int Umbrella = -1;
        }

        private void OnDisable()
        {
            foreach (var entry in shown.Values)
                foreach (var go in entry.Objects)
                    if (go != null) Destroy(go);
            shown.Clear();
        }

        private void Update()
        {
            try { SyncVisibility(); }
            catch (Exception ex) { Log.Error("Vehicle displays: " + ex); }
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckSeconds;
            try { Check(); }
            catch (Exception ex) { Log.Error("Vehicle displays: " + ex); }
        }

        private void Check()
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var seen = new HashSet<int>();
            foreach (var controller in VehicleHelper.AllPlayerVehicles)
            {
                var vehicle = controller != null ? controller.vehicleInstance : null;
                var layout = vehicle?.vehicleTypeName == FoodTrucksMod.CartTypeName ? Cart
                    : vehicle?.vehicleTypeName == FoodTrucksMod.TruckTypeName ? Truck : null;
                if (vehicle == null || layout == null) continue;
                var key = controller!.GetInstanceID();
                seen.Add(key);
                if (!shown.TryGetValue(key, out var entry))
                {
                    var count = layout.Spots.Length;
                    entry = new Shown
                    {
                        Controller = controller, Body = controller.transform.Find(layout.Body), Layout = layout,
                        Spots = layout.Spots.Select(name => controller.transform.Find(name)).ToArray(),
                        Pieces = new string?[count], Objects = new GameObject?[count],
                    };
                    shown[key] = entry;
                    // A vehicle that left the game's list and came back must not get a second set of pieces.
                    foreach (var spot in entry.Spots)
                    {
                        if (spot == null) continue;
                        foreach (Transform child in spot)
                            if (child.name.StartsWith("Display_", StringComparison.Ordinal)) Destroy(child.gameObject);
                    }
                    if (entry.Spots.Any(s => s == null)) Log.Error($"Vehicle displays: {layout.Kind} {vehicle.id} lacks equipment spots (a {layout.Kind} from an older build).");
                }
                if (entry.Spots.Any(s => s == null)) continue;

                var fitted = CargoRules.FittedEquipment(tables, CargoLimit.Lines(vehicle));
                var open = layout.Turns && StreetSales.Find(FoodTrucksSession.CurrentState, vehicle.id)?.IsOpen == true;
                var side = !layout.Turns ? 0 : open ? CartQueueView.QueueSide(vehicle.id) ?? 1 : 1;
                if (layout == Cart) Umbrella(entry, vehicle.id, open);
                var wanted = new string?[entry.Spots.Length];
                var info = tables.Vehicle(layout.Kind)!;
                foreach (var placement in EquipmentLayout.Place(fitted, layout.Spots.Length - 1, info.CoolerSlots,
                    p => p, entry.Pieces)) wanted[placement.Slot] = placement.Piece;
                for (var i = 0; i < wanted.Length; i++)
                {
                    var piece = wanted[i];
                    var display = i == layout.Spots.Length - 1 && piece != null
                        ? (layout == Cart ? "CartDrinksCooler" : "TruckDrinksCooler") : piece == null ? null : Display(tables, piece);
                    if (piece != entry.Pieces[i] || (piece != null && entry.Objects[i] == null && display != null))
                    {
                        if (entry.Objects[i] != null) Destroy(entry.Objects[i]);
                        entry.Objects[i] = piece == null ? null : Place(vehicle.id, layout, i, piece, display, entry.Spots[i]!);
                        entry.Pieces[i] = piece;
                        entry.Side = int.MinValue;
                        entry.Layer = -1;
                        Collect(entry);
                    }
                }
                // The game moves a pushed cart's renderers to the player vehicles layer while it is pushed and back after.
                var layer = entry.Body != null ? entry.Body.gameObject.layer : controller.gameObject.layer;
                if (entry.Layer != layer)
                {
                    entry.Layer = layer;
                    foreach (var renderer in entry.Renderers)
                        if (renderer != null) renderer.gameObject.layer = layer;
                }
                if (entry.Side != side)
                {
                    entry.Side = side;
                    // The models face +Z; +90 degrees turns them to the cart's right (+X), -90 to its left. A truck's
                    // spots are turned already (side 0).
                    var rotation = Quaternion.Euler(0f, side * 90f, 0f);
                    foreach (var go in entry.Objects)
                        if (go != null) go.transform.localRotation = rotation;
                    // The cart's cabinet front follows the customer side as well as facing it.
                    // General worktop pieces turn in place; the cooler stays below the counter.
                    if (layout == Cart)
                        entry.Spots[entry.Spots.Length - 1]!.localPosition = new Vector3(side * 0.443f, 0.525f, -0.32f);
                }
            }
            // Vehicles despawned or sold: their pieces went with them; forget them.
            foreach (var key in shown.Keys.Where(k => !seen.Contains(k)).ToList()) shown.Remove(key);
        }

        // The cart's umbrella is open only while it sells; closed, pushed, parked or loaded it is furled round the pole
        // (CartAssets' Cart_Canopy and Cart_Canopy_Furled). One log line per cart and state per session.
        private void Umbrella(Shown entry, string vehicleId, bool open)
        {
            var state = open ? 1 : 0;
            if (entry.Umbrella == state || entry.Umbrella == -2 || entry.Controller == null) return;
            var canopy = entry.Controller.transform.Find("Cart_Canopy");
            var furled = entry.Controller.transform.Find("Cart_Canopy_Furled");
            if (canopy == null || furled == null)
            {
                entry.Umbrella = -2;
                Log.Error($"Cart umbrella: cart {vehicleId} has no Cart_Canopy / Cart_Canopy_Furled (a cart from an older build).");
                return;
            }
            canopy.gameObject.SetActive(open);
            furled.gameObject.SetActive(!open);
            entry.Umbrella = state;
            if (logged.Add($"umbrella:{vehicleId}:{state}"))
                Log.Info($"Cart umbrella: cart {vehicleId} {(open ? "open, selling" : "furled, not selling")}.");
        }

        private void SyncVisibility()
        {
            foreach (var entry in shown.Values)
            {
                var visible = entry.Controller == null || entry.Controller.visible;
                if (visible == entry.Visible) continue;
                entry.Visible = visible;
                foreach (var renderer in entry.Renderers)
                    if (renderer != null) renderer.enabled = visible;
            }
        }

        // The pieces' renderers, set to the vehicle's current visibility (a new piece on a hidden one starts hidden).
        private static void Collect(Shown entry)
        {
            entry.Renderers = entry.Objects.Where(go => go != null).SelectMany(go => go!.GetComponentsInChildren<Renderer>(true)).ToArray();
            entry.Visible = entry.Controller == null || entry.Controller.visible;
            foreach (var renderer in entry.Renderers) renderer.enabled = entry.Visible;
        }

        private static string? Display(FoodTrucks.Core.Demand.DemandTables tables, string piece) => tables.Equipment(piece)?.Display;

        private GameObject? Place(string vehicleId, Layout layout, int spot, string piece, string? display, Transform parent)
        {
            var model = display == null ? null : Model(display);
            var note = $"{vehicleId}:{spot}:{piece}:{display}";
            if (logged.Add(note))
            {
                var what = display == null ? "no display model yet, the spot stays empty"
                    : model == null ? $"display {display} not found in the bundle, the spot stays empty"
                    : $"shows {display}";
                Log.Info($"Vehicle displays: {layout.Kind} {vehicleId} {layout.Labels[spot]} spot, {piece}: {what}.");
            }
            if (model == null) return null;
            var go = Instantiate(model, parent, false);
            go.name = "Display_" + display;
            go.transform.localPosition = Vector3.zero;
            // Looks only: nothing that blocks, triggers or catches the mouse.
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            return go;
        }

        private GameObject? Model(string display)
        {
            if (models.TryGetValue(display, out var cached)) return cached;
            var model = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(ModelFolder + display + "_Display.prefab");
            models[display] = model;
            return model;
        }
    }
}
