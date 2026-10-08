#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AI.Citizens;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
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
            IReadOnlyDictionary<string, double> buyerShares, double addOnsPerFoodBuyer,
            IReadOnlyDictionary<string, string>? saleItems = null, string? addOnItem = null)
        {
            SaleItems = saleItems ?? new Dictionary<string, string>();
            AddOnItem = addOnItem;
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
        // Per category, the item its customers buy now: the stocked item they accept best at its price.
        public IReadOnlyDictionary<string, string> SaleItems { get; }
        // The cold drink food buyers add, chosen the same way; null when none sells.
        public string? AddOnItem { get; }
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
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
            if (menu.Problem != MenuProblem.None || menu.Menu.Items.Count == 0 || stock.Count == 0)
                return new CartPlan(menu, stock, null, 0, 0, 0, NoShares, 0);

            var sellable = stock.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var estimate = new DemandModel(tables).Estimate(World.Query(tables, spot, sellable));

            // Accepted demand per category: share × the conversion of the item customers accept best in it,
            // so one overpriced item doesn't block a well-priced one in the same category.
            var accepted = new Dictionary<string, double>();
            var saleItems = new Dictionary<string, string>();
            foreach (var share in estimate.CustomerShares)
            {
                if (share.Value <= 0) continue;
                var (item, best) = BestItem(tables, spot, vehicle, stock, share.Key);
                if (item == null || best <= 0) continue;
                accepted[share.Key] = share.Value * best;
                saleItems[share.Key] = item;
            }
            var conversion = accepted.Values.Sum();
            var buyerShares = conversion > 0
                ? accepted.ToDictionary(p => p.Key, p => p.Value / conversion)
                : new Dictionary<string, double>();

            var staff = tables.Vehicle(Kind)?.Staff ?? 1;
            // Service time from the items actually on sale, not every stocked one.
            var onSale = saleItems.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var capacity = ServiceRate.CustomersPerHour(staff, ServiceRate.OrderMinutes(tables, onSale, buyerShares));
            var perHour = SalesRate.PerHour(estimate.CustomersPerHour, conversion, capacity);

            // Core averages the add-ons over all customers, food buyers or not (its main share is before
            // prices). Undo that to get drinks per food buyer, then apply the drink's own conversion.
            var (drink, drinkConversion) = BestAddOn(tables, spot, vehicle, stock);
            var foodShare = estimate.CustomerShares.Where(p => tables.Category(p.Key).AddOnFactor <= 0).Sum(p => p.Value);
            var addOns = drink == null || foodShare <= 0 ? 0 : estimate.AddOnsPerCustomer / foodShare * drinkConversion;
            return new CartPlan(menu, stock, estimate, conversion, capacity, perHour, buyerShares, addOns, saleItems, drink);
        }

        // The stocked item of a category customers accept best at its price; ties go to more stock.
        private static (string? Item, double Conversion) BestItem(DemandTables tables, Spot spot, VehicleState? vehicle,
            Dictionary<string, int> stock, string category)
        {
            string? best = null;
            double bestConversion = 0;
            var bestUnits = 0;
            foreach (var entry in stock.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                if (entry.Value <= 0 || tables.Product(entry.Key)?.Category != category) continue;
                var conversion = ConversionOf(tables, spot, vehicle, entry.Key);
                if (best == null || conversion > bestConversion || (conversion == bestConversion && entry.Value > bestUnits))
                {
                    best = entry.Key;
                    bestConversion = conversion;
                    bestUnits = entry.Value;
                }
            }
            return (best, bestConversion);
        }

        // The cold drink food buyers add: of the add-on categories' stocked items, the one accepted best.
        private static (string? Item, double Conversion) BestAddOn(DemandTables tables, Spot spot, VehicleState? vehicle,
            Dictionary<string, int> stock)
        {
            (string? Item, double Conversion) best = (null, 0);
            foreach (var category in stock.Keys.Select(k => tables.Product(k)?.Category).Where(c => c != null).Distinct())
            {
                if (tables.Category(category!).AddOnFactor <= 0) continue;
                var candidate = BestItem(tables, spot, vehicle, stock, category!);
                if (candidate.Item != null && candidate.Conversion > best.Conversion) best = candidate;
            }
            return best;
        }

        // Customers compare the cart's price (CartPricing: your price, the district's pricing manager,
        // or the default) with the neighbourhood's reference price through the mod's price curve.
        public static double ConversionOf(DemandTables tables, Spot spot, VehicleState? vehicle, string itemName)
        {
            var price = CartPricing.PriceCents(vehicle, itemName, spot.Neighbourhood) / 100.0;
            if (price <= 0) return tables.PriceCurve.Conversion(1.0);
            // A district the game has no price index for (unknown or mod-added): the game's reference price
            // would index it unguarded and throw, so compare with the item's default price instead.
            if (string.IsNullOrEmpty(spot.Neighbourhood) || !CitizenHelper.averagePriceIndicesInNeighborhoods.ContainsKey(spot.Neighbourhood!))
            {
                var standard = CartPricing.DefaultCents(itemName) / 100.0;
                return standard > 0
                    ? tables.PriceCurve.Conversion(tables.PriceCurve.NeighbourhoodRatio(price, standard, tables.PriceCurve.AssumedPriceIndex))
                    : tables.PriceCurve.Conversion(1.0);
            }
            var reference = ItemHelper.GetMarketReferencePrice(itemName, spot.Neighbourhood);
            if (reference <= 0) return tables.PriceCurve.Conversion(1.0);
            // The game's own ceiling for the district (class indices and a player monopoly's bonus included)
            // is where every customer still buys; the curve is placed on it.
            var ceiling = CartPricing.CeilingCents(itemName, spot.Neighbourhood) / 100.0;
            var index = ceiling > 0 ? ceiling / reference
                : CitizenHelper.averagePriceIndicesInNeighborhoods.TryGetValue(spot.Neighbourhood!, out var i) && i > 0
                    ? i : tables.PriceCurve.AssumedPriceIndex;
            return tables.PriceCurve.Conversion(tables.PriceCurve.NeighbourhoodRatio(price, reference, index));
        }

        public static string Describe(CartPlan plan)
        {
            if (plan.Menu.Problem != MenuProblem.None) return $"menu problem {plan.Menu.Problem} ({plan.Menu.Detail})";
            if (plan.Menu.Menu.Items.Count == 0) return "no equipment fitted";
            var e = plan.Estimate;
            var menu = string.Join(", ", plan.Menu.Menu.Items.Select(World.Short));
            if (e == null) return $"menu {menu}";
            var shares = string.Join(", ", plan.BuyerShares.Where(s => s.Value > 0).Select(s =>
                $"{s.Key} {s.Value:P0}{(plan.SaleItems.TryGetValue(s.Key, out var item) ? " " + World.Short(item) : "")}"));
            return $"menu {menu}; {e.Daypart}, {e.Season}; {e.Footfall:F0} passers/h, appetite {e.Appetite:P1}, market {e.Market:F2}; " +
                   $"{e.CustomersPerHour:F1} customers/h, buy {plan.Conversion:P0} ({shares}), window {plan.Capacity:F0}/h " +
                   $"-> {plan.SalesPerHour:F1} sales/h, {plan.AddOnsPerFoodBuyer:F2} drinks ({World.Short(plan.AddOnItem)}) per food buyer; stock {plan.StockUnits}.";
        }
    }
}
