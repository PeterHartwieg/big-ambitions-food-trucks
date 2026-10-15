#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UI;
using UnityEngine;

namespace FoodTrucks
{
    // A cart's or truck's prices, for the Prices section of its BizMan page (BizManVehiclePage): one line per menu item
    // with its price now, where that comes from and the most customers accept where the vehicle stands; "Change" asks
    // for a price with the game's text prompt, and an empty answer goes back to the automatic price (pricing manager
    // or default).
    internal static class PriceDialog
    {
        internal readonly struct Line
        {
            public Line(string item, string name, string text)
            {
                Item = item;
                Name = name;
                Text = text;
            }

            public string Item { get; }
            public string Name { get; }
            public string Text { get; }
        }

        // The vehicle's menu as lines; empty when it has none (or isn't a food vehicle in this save).
        internal static List<Line> Lines(string vehicleId)
        {
            var lines = new List<Line>();
            var tables = DemandData.Tables;
            var state = FoodTrucksSession.CurrentState;
            var cart = Instance(vehicleId);
            if (tables == null || state == null || cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return lines;
            var neighbourhood = World.ReadSpot(tables, PositionOf(cart)).Neighbourhood;
            var vehicle = StreetSales.Find(state, vehicleId);
            foreach (var item in CartDemand.MenuOf(tables, cart).Menu.Items)
            {
                var name = LocalizorManager.GetLocalization(item, null);
                var price = CartSelling.Money(CartPricing.PriceCents(vehicle, item, neighbourhood));
                var source = Text(SourceKey(vehicle, item, neighbourhood));
                var ceiling = CartPricing.CeilingCents(item, neighbourhood);
                var text = ceiling > 0
                    ? string.Format(Text("foodtrucks:prices_option"), name, price, source, CartSelling.Money(ceiling))
                    : string.Format(Text("foodtrucks:prices_option_no_ceiling"), name, price, source);
                lines.Add(new Line(item, name, text));
            }
            return lines;
        }

        // The game's text prompt for one item's price. `done` runs once the prompt closes, answered or not.
        internal static void AskPrice(string vehicleId, string item, string name, Action? done = null)
        {
            var input = InstanceBehavior<UIs>.Instance?.textInputUi;
            if (input == null)
            {
                Log.Error("Prices: the game's text prompt isn't available in this scene.");
                done?.Invoke();
                return;
            }
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                done?.Invoke();
                return;
            }
            var current = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId)?.CustomPriceCents;
            var initial = current != null && current.TryGetValue(item, out var c) ? (c / 100.0).ToString("0.00", CultureInfo.InvariantCulture) : "";
            input.Show("foodtrucks:prices_input", "foodtrucks:prices_set", text =>
                {
                    try { SetPrice(vehicleId, item, name, text); }
                    catch (Exception ex) { Log.Error("Prices: " + ex); }
                    done?.Invoke();
                }, onClose: () => done?.Invoke(),
                initialText: initial, allowEmpty: true, characterLimit: 9);
        }

        private static void SetPrice(string vehicleId, string item, string name, string text)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Log.Info($"Price: {item} on {vehicleId} not changed: the mod's data is read-only.");
                return;
            }
            var vehicle = StreetSales.GetOrAdd(state, vehicleId);
            if (string.IsNullOrEmpty(vehicle.VehicleType)) vehicle.VehicleType = FoodTrucksMod.KindOf(Instance(vehicleId)?.vehicleTypeName) ?? "cart";
            vehicle.CustomPriceCents ??= new Dictionary<string, long>();
            if (string.IsNullOrWhiteSpace(text))
            {
                vehicle.CustomPriceCents.Remove(item);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Price: {item} on {vehicleId} back to automatic.");
                CartSelling.Notify("foodtrucks:notification_price_automatic", ("item", name));
                return;
            }
            // The same parse and range the save accepts, so a typed price can never make the save unloadable.
            if (!PriceChoice.TryParseCents(text, out var cents))
            {
                Log.Info($"Price: {item} on {vehicleId} not changed: '{text}' isn't a price.");
                CartSelling.Notify("foodtrucks:notification_price_invalid");
                return;
            }
            vehicle.CustomPriceCents[item] = cents;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Price: {item} on {vehicleId} set to {CartSelling.Money(cents)}.");
            CartSelling.Notify("foodtrucks:notification_price_set", ("item", name), ("price", CartSelling.Money(cents)));
        }

        private static string SourceKey(VehicleState? vehicle, string item, string? neighbourhood)
        {
            if (vehicle?.CustomPriceCents != null && vehicle.CustomPriceCents.ContainsKey(item)) return "foodtrucks:prices_source_custom";
            return CartPricing.ManagerCents(item, neighbourhood) != null ? "foodtrucks:prices_source_manager" : "foodtrucks:prices_source_default";
        }

        // In the city, or with a private driver (the vehicle page lists both).
        private static VehicleInstance? Instance(string id)
        {
            var game = SaveGameManager.Current;
            return game?.VehicleInstances.FirstOrDefault(v => v.id == id) ?? game?.privateDriverVehicleInstances?.FirstOrDefault(v => v.id == id);
        }

        // Where the vehicle stands: its live position, else the one saved for it.
        private static Vector3 PositionOf(VehicleInstance cart)
        {
            var controller = VehicleHelper.GetVehicleController(cart);
            return controller != null ? controller.transform.position : (Vector3)cart.position;
        }

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);
    }
}
