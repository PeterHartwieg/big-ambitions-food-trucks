#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
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
            public string Body { get; }
            public bool Turns { get; }
            public string[] Spots { get; }
            public string[] Labels { get; }
        }

        private static readonly Layout Cart = new Layout("cart", "Cart_Body", turns: true,
            new[] { "EquipmentFront", "EquipmentRear", EquipmentLayout.CoolerPlace }, new[] { "front", "rear", "customer cooler" });
        private static readonly Layout Truck = new Layout("truck", "CarHolder/Body/CheapSaloon", turns: false,
            new[] { "EquipmentLine2", "EquipmentLine3", "EquipmentLine1", "EquipmentLine4", EquipmentLayout.CoolerPlace },
            new[] { "line 2", "line 3", "line 1", "line 4", "customer cooler" });

        private readonly Dictionary<int, Shown> shown = new Dictionary<int, Shown>();
        private readonly Dictionary<string, GameObject?> models = new Dictionary<string, GameObject?>();
        private readonly HashSet<string> logged = new HashSet<string>();
        private float nextCheck;
        private static VehicleDisplays? instance;

        private void OnEnable() => instance = this;

        internal static void QueueChanged()
        {
            if (instance == null) return;
            try { instance.Check(); }
            catch (Exception ex) { Log.Error("Vehicle displays: " + ex); }
        }

        private sealed class Shown
        {
            public VehicleController? Controller;
            public Transform? Body;
            public int Layer = -1;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public bool Visible = true;
            public Layout Layout = Cart;
            public Transform?[] Spots = Array.Empty<Transform?>();
            public Transform? Register;
            public string?[] Pieces = Array.Empty<string?>();
            public GameObject?[] Objects = Array.Empty<GameObject?>();
            public int Side;
            public int WindowSide = 1;
            public int Umbrella = -1;
        }

        private void OnDisable()
        {
            if (instance == this) instance = null;
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
                        Register = layout == Cart ? controller.transform.Find("Cash_Register") : null,
                        Spots = layout.Spots.Select(name => controller.transform.Find(name)).ToArray(),
                        Pieces = new string?[count], Objects = new GameObject?[count],
                    };
                    shown[key] = entry;
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
                entry.WindowSide = CartStance.DisplaySide(open, CartQueueView.QueueSide(vehicle.id), entry.WindowSide);
                var side = layout.Turns ? entry.WindowSide : 0;
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
                    foreach (var go in entry.Objects)
                        if (go != null) go.transform.localRotation = Quaternion.Euler(0f,
                            layout == Cart ? (float)CartStance.EquipmentYaw(side, go.name.Substring("Display_".Length)) : 0f, 0f);
                    if (entry.Register != null)
                    {
                        var register = CartStance.Register(side);
                        entry.Register.localPosition = new Vector3((float)register.X, (float)register.Y, (float)register.Z);
                        entry.Register.localRotation = Quaternion.Euler(0f, (float)register.Yaw, 0f);
                        if (logged.Add($"register:{vehicle.id}:{CartStance.Side(side)}"))
                            Log.Info($"Cart register: cart {vehicle.id} register on the {(CartStance.Side(side) > 0 ? "right" : "left")} front corner, facing the customers.");
                    }
                    if (layout == Cart)
                        entry.Spots[entry.Spots.Length - 1]!.localPosition = new Vector3(-side * 0.443f, 0.525f, -0.32f);
                }
            }
            foreach (var key in shown.Keys.Where(k => !seen.Contains(k)).ToList()) shown.Remove(key);
        }

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
