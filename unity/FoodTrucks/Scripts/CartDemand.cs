#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AI.Citizens;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Marketing;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Rules;
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
            IReadOnlyDictionary<string, string>? saleItems = null, string? addOnItem = null, int staff = 1)
        {
            Staff = Math.Max(1, staff);
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
        // Customers the window can serve in an hour with its full crew (Staff workers).
        public double Capacity { get; }
        public int Staff { get; }
        // What one worker makes in an hour, the player alone at the window.
        public double CapacityPerServer => Capacity / Staff;
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
        // The vehicle's kind in the data files; a vehicle of no known kind is treated as a cart.
        public static string KindOf(VehicleInstance vehicle) => CargoLimit.VehicleKind(vehicle) ?? "cart";

        public static MenuBuild MenuOf(DemandTables tables, VehicleInstance cart) =>
            Menu.FromCargo(tables, KindOf(cart), CargoLimit.Lines(cart));

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
        // with stock. A grill with no hot dogs left brings no hot-dog customers. `reserved` are units already
        // sold to customers still on their way to the window (CartSelling's held sales); they don't count.
        // `at`: the absolute game minute to plan for (a catch-up step's start); null is now.
        public static CartPlan Plan(DemandTables tables, VehicleInstance cart, Spot spot, IReadOnlyDictionary<string, int>? reserved = null, double? at = null)
        {
            var menu = MenuOf(tables, cart);
            var stock = StockOf(cart, menu.Menu);
            if (reserved != null)
            {
                foreach (var pair in reserved)
                {
                    if (!stock.TryGetValue(pair.Key, out var units)) continue;
                    if (units - pair.Value > 0) stock[pair.Key] = units - pair.Value;
                    else stock.Remove(pair.Key);
                }
            }
            // A catering gig's units are held back from street sales until it ends.
            foreach (var pair in Catering.Reserved(cart.id, stock))
            {
                if (!stock.TryGetValue(pair.Key, out var units)) continue;
                if (units - pair.Value > 0) stock[pair.Key] = units - pair.Value;
                else stock.Remove(pair.Key);
            }
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
            if (menu.Problem != MenuProblem.None || menu.Menu.Items.Count == 0 || stock.Count == 0)
                return new CartPlan(menu, stock, null, 0, 0, 0, NoShares, 0);

            var sellable = stock.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var (reputation, shares) = Reputation(tables, cart, spot, vehicle, sellable);
            var minute = (long)Math.Floor(at ?? HireCrew.NowExact());
            var pitch = CityPitches.BonusFor(FoodTrucksSession.CurrentState, vehicle, spot, minute);
            // Only a level paid for the day planned for raises the passers (MarketingFees.Active).
            var marketing = DemandData.Rules == null ? 1.0 : MarketingFees.Factor(vehicle, DemandData.Rules.Marketing, (int)(minute / 1440) + 1);
            var estimate = new DemandModel(tables).Estimate(World.Query(tables, spot, sellable, reputation.Factor, shares, at, pitch, marketing));

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

            var staff = tables.Vehicle(KindOf(cart))?.Staff ?? 1;
            // Service time from the items actually on sale, not every stocked one.
            var onSale = saleItems.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var capacity = ServiceRate.CustomersPerHour(staff, ServiceRate.OrderMinutes(tables, onSale, buyerShares));
            var perHour = SalesRate.PerHour(estimate.CustomersPerHour, conversion, capacity);

            // Core averages the add-ons over all customers, food buyers or not (its main share is before
            // prices). Undo that to get drinks per food buyer, then apply the drink's own conversion.
            var (drink, drinkConversion) = BestAddOn(tables, spot, vehicle, stock);
            var foodShare = estimate.CustomerShares.Where(p => tables.Category(p.Key).AddOnFactor <= 0).Sum(p => p.Value);
            var addOns = drink == null || foodShare <= 0 ? 0 : estimate.AddOnsPerCustomer / foodShare * drinkConversion;
            return new CartPlan(menu, stock, estimate, conversion, capacity, perHour, buyerShares, addOns, saleItems, drink, staff);
        }

        internal readonly struct ReputationParts
        {
            public ReputationParts(double health, double regulars, double footfall)
            {
                Health = health;
                Regulars = regulars;
                Footfall = footfall;
            }

            public double Health { get; }
            public double Regulars { get; }
            public double Footfall { get; }
            public double Factor => ReputationFactor.Combine(Health, Regulars, Footfall);

            public override string ToString() => $"grade {Health:F2} x regulars {Regulars:F2} x neighbours {Footfall:F2} = {Factor:F2}";
        }

        private static RuleTables? bookRules;
        private static ReputationBook? book;

        public static ReputationBook? Book()
        {
            var rules = DemandData.Rules;
            if (rules == null) return null;
            if (!ReferenceEquals(rules, bookRules))
            {
                bookRules = rules;
                book = new ReputationBook(rules);
            }
            return book;
        }

        // The design's reputation (Health grades, Regulars, Competition): the grade on this vehicle's window,
        // the regulars at this pitch on this weekday, and the player's other open vehicles within a block,
        // which bring extra passers and split the customers for the same items.
        public static (ReputationParts Parts, IReadOnlyDictionary<string, double>? Shares) Reputation(DemandTables tables, VehicleInstance cart, Spot spot,
            VehicleState? vehicle, IReadOnlyCollection<string> sellable)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var reputationBook = Book();
            if (state == null || rules == null || reputationBook == null) return (new ReputationParts(1, 1, 1), null);
            var health = reputationBook.HealthFactor(vehicle);
            // Regulars by working day: after midnight it is still the evening's crowd.
            var pitch = CartSelling.PitchOf(cart.id) ?? World.PitchKey(state, spot.X, spot.Z);
            var regulars = reputationBook.RegularsFactor(state, pitch, CartSelling.ServiceDayNow());
            var fleet = new List<CompetitionVehicle> { new CompetitionVehicle { VehicleId = cart.id, X = spot.X, Z = spot.Z, Menu = sellable.ToList() } };
            foreach (var other in state.Vehicles)
            {
                if (other.VehicleId == cart.id || !other.IsOpen || other.Transit != null) continue;
                var instance = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v.id == other.VehicleId);
                if (instance == null) continue;
                var controller = VehicleHelper.GetVehicleController(instance);
                var position = controller != null ? controller.transform.position : (UnityEngine.Vector3)instance.position;
                var menu = MenuOf(tables, instance);
                fleet.Add(new CompetitionVehicle { VehicleId = other.VehicleId, X = position.x, Z = position.z, Menu = StockOf(instance, menu.Menu).Keys.ToList() });
            }
            var effect = new Competition(tables, rules.Competition).Evaluate(fleet)[cart.id];
            return (new ReputationParts(health, regulars, effect.FootfallFactor), effect.CategoryShares);
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
