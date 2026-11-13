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
            public long PriceCents { get; }
            public bool Custom { get; }
            public long AutomaticCents { get; }
            public long MarketCents { get; }
            public long YellowCents { get; }
            public long RedCents { get; }
        }

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
                var yellow = RivalSells(item, neighbourhood) && market > 0 ? Math.Min(defaultCents, market) : defaultCents;
                lines.Add(new Line(item, LocalizorManager.GetLocalization(item, null), CartPricing.PriceCents(vehicle, item, neighbourhood),
                    custom, automatic, market, yellow, defaultCents * 2));
            }
            return lines;
        }

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

        private static VehicleInstance? Instance(string id)
        {
            var game = SaveGameManager.Current;
            return game?.VehicleInstances.FirstOrDefault(v => v.id == id) ?? game?.privateDriverVehicleInstances?.FirstOrDefault(v => v.id == id);
        }

        private static Vector3 PositionOf(VehicleInstance cart)
        {
            var controller = VehicleHelper.GetVehicleController(cart);
            return controller != null ? controller.transform.position : (Vector3)cart.position;
        }

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);
    }
}
