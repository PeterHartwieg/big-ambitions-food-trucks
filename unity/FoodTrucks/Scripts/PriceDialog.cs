#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BigAmbitions.Items;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UI;
using UnityEngine;

namespace FoodTrucks
{
    // A cart's or truck's prices, for the Menu box of its BizMan page (BizManVehiclePage), the way the game's own
    // inventory pricing shows a shop's (InventoryProductCellView): the price now, the market price where the vehicle
    // stands, and the thresholds that colour a price yellow (above the market) and red (twice the item's default price).
    // A price the player sets replaces the automatic one (pricing manager or default); an empty one goes back to it.
    internal static class PriceDialog
    {
        internal readonly struct Line
        {
            public Line(string item, string name, long priceCents, bool custom, long automaticCents, long marketCents, long yellowCents, long redCents)
            {
                Item = item;
                Name = name;
                PriceCents = priceCents;
                Custom = custom;
                AutomaticCents = automaticCents;
                MarketCents = marketCents;
                YellowCents = yellowCents;
                RedCents = redCents;
            }

            public string Item { get; }
            public string Name { get; }
            // What the vehicle charges now, and whether that is the player's own price.
            public long PriceCents { get; }
            public bool Custom { get; }
            // The price without the player's: pricing manager or default.
            public long AutomaticCents { get; }
            public long MarketCents { get; }
            // As the game's product row: a price above YellowCents shows yellow, at RedCents or above red (0: no colour).
            public long YellowCents { get; }
            public long RedCents { get; }
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
                var custom = vehicle?.CustomPriceCents != null && vehicle.CustomPriceCents.ContainsKey(item);
                var automatic = PriceChoice.Choose(null, CartPricing.ManagerCents(item, neighbourhood), CartPricing.DefaultCents(item));
                var market = MarketCents(item, neighbourhood);
                var game = ItemsGetter.GetByName(item, suppressError: true);
                var defaultCents = game == null ? 0 : (long)Math.Round(game.DefaultMarketPrice * 100.0);
                // InventoryProductCellView: yellow above the item's default price, or above the market where a rival's shop
                // in the district sells it and is cheaper; red at twice the default price.
                var yellow = RivalSells(item, neighbourhood) && market > 0 ? Math.Min(defaultCents, market) : defaultCents;
                lines.Add(new Line(item, LocalizorManager.GetLocalization(item, null), CartPricing.PriceCents(vehicle, item, neighbourhood),
                    custom, automatic, market, yellow, defaultCents * 2));
            }
            return lines;
        }

        // The game's market price (ItemHelper.GetLowestMarketPrice: the cheapest shop in the district selling it, else its
        // own estimate). For the mod's own foods, which no shop sells, and outside a known district: the default price.
        private static long MarketCents(string item, string? neighbourhood)
        {
            if (string.IsNullOrEmpty(neighbourhood) || ModItems.Foods.Contains(item) ||
                !AI.Citizens.CitizenHelper.averagePriceIndicesInNeighborhoods.ContainsKey(neighbourhood!))
                return CartPricing.DefaultCents(item);
            try
            {
                var price = ItemHelper.GetLowestMarketPrice(item, neighbourhood!);
                return price > 0f ? (long)Math.Round(price * 100.0) : CartPricing.DefaultCents(item);
            }
            catch (Exception ex)
            {
                Log.Error($"Market price for {item} in {neighbourhood}: {ex.Message}");
                return CartPricing.DefaultCents(item);
            }
        }

        private static bool RivalSells(string item, string? neighbourhood)
        {
            if (string.IsNullOrEmpty(neighbourhood)) return false;
            foreach (var building in SaveGameManager.Current?.BuildingRegistrations ?? Enumerable.Empty<BuildingRegistration>())
                if (!string.IsNullOrEmpty(building.businessOwnerRivalId) && !building.RentedByPlayer && building.Neighborhood == neighbourhood &&
                    building.cachedAvailableProducts != null && building.cachedAvailableProducts.Any(p => p == item))
                    return true;
            return false;
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

        internal static void SetPrice(string vehicleId, string item, string name, string text)
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
