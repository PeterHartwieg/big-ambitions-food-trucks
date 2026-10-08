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
using UI.InteriorDesigner;

namespace FoodTrucks
{
    // Setting the cart's prices, with the game's own list picker and text prompt: pick a menu item,
    // type a price, or leave it empty to go back to the automatic price (pricing manager or default).
    internal static class PriceDialog
    {
        public static void Show()
        {
            var tables = DemandData.Tables;
            var state = FoodTrucksSession.CurrentState;
            var cart = VehicleHelper.GetCurrentVehicle();
            if (tables == null || state == null || cart == null || cart.vehicleTypeName != FoodTrucksMod.CartTypeName) return;
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            var menu = CartDemand.MenuOf(tables, cart).Menu.Items;
            if (menu.Count == 0)
            {
                CartSelling.Notify("foodtrucks:notification_prices_no_menu");
                return;
            }
            var controller = VehicleHelper.GetCurrentVehicleBase();
            var neighbourhood = controller == null ? null : World.ReadSpot(tables, controller.transform.position).Neighbourhood;
            var vehicle = StreetSales.Find(state, cart.id);

            // One line per menu item: its price now, where that comes from, and the most customers accept here.
            var options = new Dictionary<string, string>();
            foreach (var item in menu)
            {
                var name = LocalizorManager.GetLocalization(item, null);
                var ceiling = CartPricing.CeilingCents(item, neighbourhood);
                var line = ceiling > 0
                    ? string.Format(Text("foodtrucks:prices_option"), name, CartSelling.Money(CartPricing.PriceCents(vehicle, item, neighbourhood)),
                        Text(SourceKey(vehicle, item, neighbourhood)), CartSelling.Money(ceiling))
                    : string.Format(Text("foodtrucks:prices_option_no_ceiling"), name, CartSelling.Money(CartPricing.PriceCents(vehicle, item, neighbourhood)),
                        Text(SourceKey(vehicle, item, neighbourhood)));
                options[line] = item;
            }
            // The game's list picker belongs to the interior designer and registers itself when that UI starts.
            if (DropdownSelector.onShow == null)
            {
                Log.Error("Prices: the game's list picker isn't available in this scene.");
                CartSelling.Notify("foodtrucks:notification_prices_unavailable");
                return;
            }
            DropdownSelector.Show(options.Keys.ToList(), chosen =>
            {
                if (options.TryGetValue(chosen, out var item)) AskPrice(cart.id, item, LocalizorManager.GetLocalization(item, null));
            }, "foodtrucks:prices_header", "foodtrucks:prices_body");
        }

        private static void AskPrice(string cartId, string item, string name)
        {
            var input = InstanceBehavior<UIs>.Instance?.textInputUi;
            if (input == null) return;
            var current = StreetSales.Find(FoodTrucksSession.CurrentState, cartId)?.CustomPriceCents;
            var initial = current != null && current.TryGetValue(item, out var c) ? (c / 100.0).ToString("0.00", CultureInfo.InvariantCulture) : "";
            input.Show("foodtrucks:prices_input", "foodtrucks:prices_set", text => SetPrice(cartId, item, name, text),
                initialText: initial, allowEmpty: true, characterLimit: 9);
        }

        private static void SetPrice(string cartId, string item, string name, string text)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            var vehicle = StreetSales.GetOrAdd(state, cartId);
            vehicle.CustomPriceCents ??= new Dictionary<string, long>();
            if (string.IsNullOrWhiteSpace(text))
            {
                vehicle.CustomPriceCents.Remove(item);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Price: {item} on cart {cartId} back to automatic.");
                CartSelling.Notify("foodtrucks:notification_price_automatic", ("item", name));
                return;
            }
            // The same parse and range the save accepts, so a typed price can never make the save unloadable.
            if (!PriceChoice.TryParseCents(text, out var cents))
            {
                CartSelling.Notify("foodtrucks:notification_price_invalid");
                return;
            }
            vehicle.CustomPriceCents[item] = cents;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Price: {item} on cart {cartId} set to {CartSelling.Money(cents)}.");
            CartSelling.Notify("foodtrucks:notification_price_set", ("item", name), ("price", CartSelling.Money(cents)));
        }

        private static string SourceKey(VehicleState? vehicle, string item, string? neighbourhood)
        {
            if (vehicle?.CustomPriceCents != null && vehicle.CustomPriceCents.ContainsKey(item)) return "foodtrucks:prices_source_custom";
            return CartPricing.ManagerCents(item, neighbourhood) != null ? "foodtrucks:prices_source_manager" : "foodtrucks:prices_source_default";
        }

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);
    }
}
