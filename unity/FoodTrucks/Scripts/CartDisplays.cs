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
    // Fitted equipment on a cart's counter: the first fitted piece on the front spot, the second on the rear
    // (CartAssets' EquipmentFront and EquipmentRear). economy.json's "display" names each piece's model in the
    // bundle; a piece without one leaves its spot empty. Every half second it compares each cart's fitted pieces
    // with what it shows, so fitting, removing and loading need no hooks. While the cart is open the pieces face
    // the side its queue forms on (CartQueueView's choice); closed, or with no line laid out, they face right.
    // The models have no colliders.
    internal sealed class CartDisplays : MonoBehaviour
    {
        private const float CheckSeconds = 0.5f;
        private const string ModelFolder = "Assets/Mods/FoodTrucks/Vehicles/EquipmentArt/";
        private static readonly string[] SpotNames = { "EquipmentFront", "EquipmentRear" };
        private static readonly string[] SpotLabels = { "front", "rear" };

        // By the controller's instance ID: a cart spawned again (out of range and back) has a new one.
        private readonly Dictionary<int, Shown> shown = new Dictionary<int, Shown>();
        private readonly Dictionary<string, GameObject?> models = new Dictionary<string, GameObject?>();
        // Cart, spot and piece already logged, so each fitted piece is logged once per session.
        private readonly HashSet<string> logged = new HashSet<string>();
        private float nextCheck;

        private sealed class Shown
        {
            public Transform?[] Spots = new Transform?[2];
            public string?[] Pieces = new string?[2];
            public GameObject?[] Objects = new GameObject?[2];
            public int Side;
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
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckSeconds;
            try { Check(); }
            catch (Exception ex) { Log.Error("Cart displays: " + ex); }
        }

        private void Check()
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var seen = new HashSet<int>();
            foreach (var controller in VehicleHelper.AllPlayerVehicles)
            {
                var cart = controller != null ? controller.vehicleInstance : null;
                if (cart == null || cart.vehicleTypeName != FoodTrucksMod.CartTypeName) continue;
                var key = controller!.GetInstanceID();
                seen.Add(key);
                if (!shown.TryGetValue(key, out var entry))
                {
                    entry = new Shown();
                    for (var i = 0; i < SpotNames.Length; i++) entry.Spots[i] = controller.transform.Find(SpotNames[i]);
                    shown[key] = entry;
                    if (entry.Spots.Any(s => s == null)) Log.Error($"Cart displays: cart {cart.id} has no equipment spots (a cart from an older build).");
                }
                if (entry.Spots.Any(s => s == null)) continue;

                var fitted = CargoRules.FittedEquipment(tables, CargoLimit.Lines(cart));
                var open = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id)?.IsOpen == true;
                var side = open ? CartQueueView.QueueSide(cart.id) ?? 1 : 1;
                for (var i = 0; i < SpotNames.Length; i++)
                {
                    var piece = i < fitted.Count ? fitted[i] : null;
                    if (piece != entry.Pieces[i] || (piece != null && entry.Objects[i] == null && Display(tables, piece) != null))
                    {
                        if (entry.Objects[i] != null) Destroy(entry.Objects[i]);
                        entry.Objects[i] = piece == null ? null : Place(cart.id, i, piece, Display(tables, piece), entry.Spots[i]!);
                        entry.Pieces[i] = piece;
                        entry.Side = 0;
                    }
                }
                if (entry.Side != side)
                {
                    entry.Side = side;
                    // The models face +Z; +90 degrees turns them to the cart's right (+X), -90 to its left.
                    var rotation = Quaternion.Euler(0f, side * 90f, 0f);
                    foreach (var go in entry.Objects)
                        if (go != null) go.transform.localRotation = rotation;
                }
            }
            // Carts despawned or sold: their pieces went with them; forget them.
            foreach (var key in shown.Keys.Where(k => !seen.Contains(k)).ToList()) shown.Remove(key);
        }

        private static string? Display(FoodTrucks.Core.Demand.DemandTables tables, string piece) => tables.Equipment(piece)?.Display;

        private GameObject? Place(string cartId, int spot, string piece, string? display, Transform parent)
        {
            var model = display == null ? null : Model(display);
            var note = $"{cartId}:{spot}:{piece}:{display}";
            if (logged.Add(note))
            {
                var what = display == null ? "no display model yet, the spot stays empty"
                    : model == null ? $"display {display} not found in the bundle, the spot stays empty"
                    : $"shows {display}";
                Log.Info($"Cart displays: cart {cartId} {SpotLabels[spot]} spot, {piece}: {what}.");
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
            var model = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(ModelFolder + display + ".prefab");
            models[display] = model;
            return model;
        }
    }
}
