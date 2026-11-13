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
        public Dictionary<string, int> Stock { get; }
        public DemandEstimate? Estimate { get; }
        public double Conversion { get; }
        public double Capacity { get; }
        public int Staff { get; }
        public double CapacityPerServer => Capacity / Staff;
        public double SalesPerHour { get; }
        public IReadOnlyDictionary<string, double> BuyerShares { get; }
        public IReadOnlyDictionary<string, string> SaleItems { get; }
        public string? AddOnItem { get; }
        public double AddOnsPerFoodBuyer { get; }
        public int StockUnits => Stock.Values.Sum();
        public bool Usable => Menu.Problem == MenuProblem.None && Menu.Menu.Items.Count > 0;
    }

    internal static class CartDemand
    {
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
            var marketing = DemandData.Rules == null ? 1.0 : MarketingFees.Factor(vehicle, DemandData.Rules.Marketing, (int)(minute / 1440) + 1);
            var estimate = new DemandModel(tables).Estimate(World.Query(tables, spot, sellable, reputation.Factor, shares, at, pitch, marketing));

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
            var onSale = saleItems.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var capacity = ServiceRate.CustomersPerHour(staff, ServiceRate.OrderMinutes(tables, onSale, buyerShares));
            var perHour = SalesRate.PerHour(estimate.CustomersPerHour, conversion, capacity);

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

        public static (ReputationParts Parts, IReadOnlyDictionary<string, double>? Shares) Reputation(DemandTables tables, VehicleInstance cart, Spot spot,
            VehicleState? vehicle, IReadOnlyCollection<string> sellable)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var reputationBook = Book();
            if (state == null || rules == null || reputationBook == null) return (new ReputationParts(1, 1, 1), null);
            var health = reputationBook.HealthFactor(vehicle);
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

        public static double ConversionOf(DemandTables tables, Spot spot, VehicleState? vehicle, string itemName)
        {
            var price = CartPricing.PriceCents(vehicle, itemName, spot.Neighbourhood) / 100.0;
            if (price <= 0) return tables.PriceCurve.Conversion(1.0);
            if (string.IsNullOrEmpty(spot.Neighbourhood) || !CitizenHelper.averagePriceIndicesInNeighborhoods.ContainsKey(spot.Neighbourhood!))
            {
                var standard = CartPricing.DefaultCents(itemName) / 100.0;
                return standard > 0
                    ? tables.PriceCurve.Conversion(tables.PriceCurve.NeighbourhoodRatio(price, standard, tables.PriceCurve.AssumedPriceIndex))
                    : tables.PriceCurve.Conversion(1.0);
            }
            var reference = ItemHelper.GetMarketReferencePrice(itemName, spot.Neighbourhood);
            if (reference <= 0) return tables.PriceCurve.Conversion(1.0);
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
