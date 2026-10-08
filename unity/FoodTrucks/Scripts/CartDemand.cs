#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AI.Citizens;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using Helpers;

namespace FoodTrucks
{
    // One moment's sales plan for a cart: what it sells, how many customers come and buy, and how
    // many the window can serve. Core decides; this only gathers the game's inputs.
    internal sealed class CartPlan
    {
        public CartPlan(MenuBuild menu, Dictionary<string, int> stock, DemandEstimate? estimate,
            double conversion, double capacity, double salesPerHour,
            IReadOnlyDictionary<string, double> buyerShares, double addOnsPerFoodBuyer)
        {
            Menu = menu;
            Stock = stock;
            Estimate = estimate;
            Conversion = conversion;
            Capacity = capacity;
            SalesPerHour = salesPerHour;
            BuyerShares = buyerShares;
            AddOnsPerFoodBuyer = addOnsPerFoodBuyer;
        }

        public MenuBuild Menu { get; }
        // Units in stock of menu items only: paid, unsealed, on the fitted menu.
        public Dictionary<string, int> Stock { get; }
        public DemandEstimate? Estimate { get; }
        // Share of interested passers who buy at the cart's prices.
        public double Conversion { get; }
        // Customers the window can serve in an hour.
        public double Capacity { get; }
        public double SalesPerHour { get; }
        // Each category's part of the customers who buy: the model's shares times that category's
        // price conversion, normalised. Customers pick their main item by these.
        public IReadOnlyDictionary<string, double> BuyerShares { get; }
        // Cold drinks per customer who buys food, after the drinks' own price conversion.
        public double AddOnsPerFoodBuyer { get; }
        public int StockUnits => Stock.Values.Sum();
        public bool Usable => Menu.Problem == MenuProblem.None && Menu.Menu.Items.Count > 0;
    }

    internal static class CartDemand
    {
        public const string Kind = "cart";

        public static MenuBuild MenuOf(DemandTables tables, VehicleInstance cart) =>
            Menu.FromCargo(tables, Kind, CargoLimit.Lines(cart));

        public static Dictionary<string, int> StockOf(VehicleInstance cart, Menu menu)
        {
            var stock = new Dictionary<string, int>();
            foreach (var entry in cart.cargoInstances)
            {
                if (entry == null || !entry.paid || entry.IsSealed || entry.amount <= 0 || !menu.IsOnMenu(entry.itemName)) continue;
                if (entry.nestedCargoInstances != null && entry.nestedCargoInstances.Count > 0) continue;
                stock.TryGetValue(entry.itemName, out var units);
                stock[entry.itemName] = units + entry.amount;
            }
            return stock;
        }

        private static readonly IReadOnlyDictionary<string, double> NoShares = new Dictionary<string, double>();

        // Demand, price and service are all worked out on what the cart can sell right now: menu items
        // with stock. A grill with no hot dogs left brings no hot-dog customers.
        public static CartPlan Plan(DemandTables tables, VehicleInstance cart, Spot spot)
        {
            var menu = MenuOf(tables, cart);
            var stock = StockOf(cart, menu.Menu);
            if (menu.Problem != MenuProblem.None || menu.Menu.Items.Count == 0 || stock.Count == 0)
                return new CartPlan(menu, stock, null, 0, 0, 0, NoShares, 0);

            var sellable = stock.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var estimate = new DemandModel(tables).Estimate(World.Query(tables, spot, sellable));

            // Accepted demand per category: share × the conversion of the item that category sells now.
            var accepted = new Dictionary<string, double>();
            foreach (var share in estimate.CustomerShares)
            {
                if (share.Value <= 0) continue;
                var item = ItemFor(tables, stock, share.Key);
                if (item == null) continue;
                var weight = share.Value * ConversionOf(tables, spot, item);
                if (weight > 0) accepted[share.Key] = weight;
            }
            var conversion = accepted.Values.Sum();
            var buyerShares = conversion > 0
                ? accepted.ToDictionary(p => p.Key, p => p.Value / conversion)
                : new Dictionary<string, double>();

            var staff = tables.Vehicle(Kind)?.Staff ?? 1;
            var capacity = ServiceRate.CustomersPerHour(staff, ServiceRate.OrderMinutes(tables, sellable, buyerShares));
            var perHour = SalesRate.PerHour(estimate.CustomersPerHour, conversion, capacity);

            // Core averages the add-ons over all customers, food buyers or not (its main share is before
            // prices). Undo that to get drinks per food buyer, then apply the drink's own conversion.
            var drink = CustomerPick.AddOnItem(tables, stock);
            var foodShare = estimate.CustomerShares.Where(p => tables.Category(p.Key).AddOnFactor <= 0).Sum(p => p.Value);
            var addOns = drink == null || foodShare <= 0 ? 0
                : estimate.AddOnsPerCustomer / foodShare * ConversionOf(tables, spot, drink);
            return new CartPlan(menu, stock, estimate, conversion, capacity, perHour, buyerShares, addOns);
        }

        // The item a category sells now: its menu item with the most stock.
        private static string? ItemFor(DemandTables tables, Dictionary<string, int> stock, string category) =>
            stock.Where(s => s.Value > 0 && tables.Product(s.Key)?.Category == category)
                .OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal)
                .Select(s => s.Key).FirstOrDefault();

        // The cart charges the game's default price; customers compare it with the neighbourhood's
        // reference price through the mod's price curve.
        public static double ConversionOf(DemandTables tables, Spot spot, string itemName)
        {
            var price = PriceOf(itemName);
            if (price <= 0 || string.IsNullOrEmpty(spot.Neighbourhood)) return tables.PriceCurve.Conversion(1.0);
            // Without a shop selling the item, the game's reference price reads this neighbourhood's index
            // unguarded; a neighbourhood it doesn't know would throw inside the sales tick.
            if (!CitizenHelper.averagePriceIndicesInNeighborhoods.ContainsKey(spot.Neighbourhood!)) return tables.PriceCurve.Conversion(1.0);
            var reference = ItemHelper.GetMarketReferencePrice(itemName, spot.Neighbourhood);
            if (reference <= 0) return tables.PriceCurve.Conversion(1.0);
            var index = CitizenHelper.averagePriceIndicesInNeighborhoods.TryGetValue(spot.Neighbourhood!, out var i) && i > 0
                ? i : tables.PriceCurve.AssumedPriceIndex;
            return tables.PriceCurve.Conversion(tables.PriceCurve.NeighbourhoodRatio(price, reference, index));
        }

        // The cart's price for an item: the game's default market price (no price setting yet).
        public static float PriceOf(string itemName)
        {
            var item = ItemsGetter.GetByName(itemName, suppressError: true);
            return item == null ? 0f : item.DefaultMarketPrice * (float)CartTuning.PriceMultiplier;
        }

        public static string Describe(CartPlan plan)
        {
            if (plan.Menu.Problem != MenuProblem.None) return $"menu problem {plan.Menu.Problem} ({plan.Menu.Detail})";
            if (plan.Menu.Menu.Items.Count == 0) return "no equipment fitted";
            var e = plan.Estimate;
            var menu = string.Join(", ", plan.Menu.Menu.Items.Select(World.Short));
            if (e == null) return $"menu {menu}";
            var shares = string.Join(", ", plan.BuyerShares.Where(s => s.Value > 0).Select(s => $"{s.Key} {s.Value:P0}"));
            return $"menu {menu}; {e.Daypart}, {e.Season}; {e.Footfall:F0} passers/h, appetite {e.Appetite:P1}, market {e.Market:F2}; " +
                   $"{e.CustomersPerHour:F1} customers/h, buy {plan.Conversion:P0} ({shares}), window {plan.Capacity:F0}/h " +
                   $"-> {plan.SalesPerHour:F1} sales/h, {plan.AddOnsPerFoodBuyer:F2} drinks per food buyer; stock {plan.StockUnits}.";
        }
    }
}
