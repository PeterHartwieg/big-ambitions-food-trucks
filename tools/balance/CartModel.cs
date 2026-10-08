using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Balance;

public sealed record ItemPrice(double Price, double Wholesale, int Box);

// Game money the mod's data files don't hold (game-prices.json).
public sealed class GamePrices
{
    public Dictionary<string, ItemPrice> Items { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> PriceIndex { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Equipment { get; } = new(StringComparer.Ordinal);

    public static GamePrices Load(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        var g = new GamePrices();
        foreach (var p in (JObject)json["items"]!)
            g.Items[p.Key] = new ItemPrice((double)p.Value!["price"]!, (double)p.Value!["wholesale"]!, (int)p.Value!["box"]!);
        foreach (var p in (JObject)json["priceIndex"]!) g.PriceIndex[p.Key] = (double)p.Value!;
        foreach (var p in (JObject)json["equipment"]!) g.Equipment[p.Key] = (double)p.Value!;
        return g;
    }
}

public enum Pricing
{
    // CartDemand.PriceOf today: the item's default market price.
    Default,
    // The ceiling: the most every social class accepts, reference × min over classes of
    // max(class index, neighbourhood average index) = reference × the neighbourhood's index
    // (ItemHelper.CalculateMaxAcceptablePriceByNeighborhood; no monopoly bonus, the cart is no provider).
    // What a player sets by hand, and what a full-skill pricing manager suggests. Conditions.PriceFactor scales it.
    NeighbourhoodIndex,
    // A pricing manager below full skill: reference + (ceiling - reference) × capture
    // (PricingManagerHelper.ComputeSuggestion). Conditions.PriceFactor is the capture.
    Manager,
    // The most the upper class accepts: reference × max(1.7, the neighbourhood's index)
    // (CitizenHelper.MaxAcceptableRelativePrice). Working and middle class may walk away.
    UpperClass
}

public sealed class Conditions
{
    public DayOfWeek Day { get; init; } = DayOfWeek.Wednesday;
    public bool Rain { get; init; }
    public int Month { get; init; } = 5;
    // The game's demand for the item in the neighbourhood, 0 to 100. 100: nobody else sells it there.
    public double MarketDemand { get; init; } = 100;
    public Pricing Pricing { get; init; } = Pricing.Default;
    // Multiplies the NeighbourhoodIndex price: 0.9 is a custom price 10% under the manager's.
    public double PriceFactor { get; init; } = 1.0;

    public string Label => $"{(Day is DayOfWeek.Saturday or DayOfWeek.Sunday ? "weekend" : "weekday")} {(Rain ? "rain" : "dry")}";
}

public sealed class Spot
{
    public Spot(string name, AreaMix areas, string hood, string region, double x, double z)
    {
        Name = name; Areas = areas; Hood = hood; Region = region; X = x; Z = z;
    }

    public string Name { get; }
    public AreaMix Areas { get; }
    public string Hood { get; }
    public string Region { get; }
    public double X { get; }
    public double Z { get; }
    public double Distance(Spot other) => Math.Sqrt((X - other.X) * (X - other.X) + (Z - other.Z) * (Z - other.Z));
}

public sealed class HourPlan
{
    public static readonly HourPlan None = new() { Daypart = Daypart.Night };
    public Daypart Daypart { get; init; }
    public double Passers { get; init; }
    // The model's customers per hour, before price and the window.
    public double Demand { get; init; }
    public double Conversion { get; init; }
    public double Capacity { get; init; }
    public double Served { get; init; }
    public double DrinksPerFoodBuyer { get; init; }
    public IReadOnlyDictionary<string, double> BuyerShares { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, string> SaleItems { get; init; } = new Dictionary<string, string>();
    public string? AddOnItem { get; init; }
}

// One cart with a fixed menu. Plan() is CartDemand.Plan from unity/FoodTrucks/Scripts/CartDemand.cs with
// the game reads replaced by inputs: the Core calls (DemandModel, PriceCurve, ServiceRate, SalesRate) are the
// mod's own, unchanged.
public sealed class Cart
{
    public const string Kind = "cart";
    private readonly DemandTables tables;
    private readonly DemandModel model;
    private readonly GamePrices prices;

    public Cart(DemandTables tables, GamePrices prices, IReadOnlyList<string> menu, string name, int? stockUnits = null,
        int? staff = null)
    {
        this.tables = tables;
        this.prices = prices;
        model = new DemandModel(tables);
        Menu = menu;
        Name = name;
        var vehicle = tables.Vehicle(Kind) ?? throw new InvalidOperationException("economy.json has no cart.");
        Staff = staff ?? vehicle.Staff;
        StockUnits = stockUnits ?? vehicle.StockUnits;
    }

    public string Name { get; }
    // Use the same per-product load as Core's CargoRules.
    public double WeightOf(string item) => tables.Product(item)?.StockWeight ?? 1.0;
    // Stock measured against the unit limit.
    public double Load(IReadOnlyDictionary<string, double> stock) => stock.Sum(p => p.Value * WeightOf(p.Key));
    public IReadOnlyList<string> Menu { get; }
    public int Staff { get; }
    public int StockUnits { get; }
    public DemandTables Tables => tables;
    public GamePrices Prices => prices;

    public double PriceOf(string item, string hood, Conditions c) => prices.Items[item].Price * c.Pricing switch
    {
        Pricing.NeighbourhoodIndex => IndexOf(hood) * c.PriceFactor,
        Pricing.Manager => 1 + (IndexOf(hood) - 1) * c.PriceFactor,
        Pricing.UpperClass => Math.Max(1.7, IndexOf(hood)),
        _ => 1.0
    };

    public double IndexOf(string hood) => prices.PriceIndex.TryGetValue(hood, out var i) ? i : tables.PriceCurve.AssumedPriceIndex;

    // CartDemand.ConversionOf: the reference price is the item's default price (no cheaper shop nearby).
    public double ConversionOf(string item, string hood, Conditions c)
    {
        var curve = tables.PriceCurve;
        var reference = prices.Items[item].Price;
        return curve.Conversion(curve.NeighbourhoodRatio(PriceOf(item, hood, c), reference, IndexOf(hood)));
    }

    public HourPlan Plan(Spot spot, int minute, Conditions c, IReadOnlyDictionary<string, double> stock)
    {
        var sellable = Menu.Where(i => stock.TryGetValue(i, out var u) && u > 1e-6).OrderBy(i => i, StringComparer.Ordinal).ToList();
        if (sellable.Count == 0) return HourPlan.None;
        var day = minute >= 1440 ? (DayOfWeek)(((int)c.Day + 1) % 7) : c.Day;
        var estimate = model.Estimate(new DemandQuery
        {
            Areas = spot.Areas,
            Neighbourhood = spot.Hood,
            Menu = sellable,
            MinuteOfDay = minute % 1440,
            Day = day,
            Month = c.Month,
            Raining = c.Rain,
            MarketDemand = sellable.ToDictionary(i => i, _ => c.MarketDemand),
            Promotion = 1.0 // baseCustomerPromotionMultiplier 0.5 / 0.5 at the default difficulty
        });

        var menuStock = stock.Where(s => sellable.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value);
        var accepted = new Dictionary<string, double>(StringComparer.Ordinal);
        var saleItems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var share in estimate.CustomerShares)
        {
            if (share.Value <= 0) continue;
            var (item, best) = BestItem(menuStock, share.Key, spot.Hood, c);
            if (item == null || best <= 0) continue;
            accepted[share.Key] = share.Value * best;
            saleItems[share.Key] = item;
        }
        var conversion = accepted.Values.Sum();
        var buyerShares = conversion > 0 ? accepted.ToDictionary(p => p.Key, p => p.Value / conversion) : new Dictionary<string, double>();
        // Service time from the items actually on sale, as CartDemand.Plan does.
        var onSale = saleItems.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var capacity = ServiceRate.CustomersPerHour(Staff, ServiceRate.OrderMinutes(tables, onSale, buyerShares));
        var served = SalesRate.PerHour(estimate.CustomersPerHour, conversion, capacity);

        var (drink, drinkConversion) = BestAddOn(menuStock, spot.Hood, c);
        var foodShare = estimate.CustomerShares.Where(p => tables.Category(p.Key).AddOnFactor <= 0).Sum(p => p.Value);
        var addOns = drink == null || foodShare <= 0 ? 0 : estimate.AddOnsPerCustomer / foodShare * drinkConversion;
        return new HourPlan
        {
            Daypart = estimate.Daypart, Passers = estimate.Footfall, Demand = estimate.CustomersPerHour, Conversion = conversion,
            Capacity = capacity, Served = served, DrinksPerFoodBuyer = addOns, BuyerShares = buyerShares,
            SaleItems = saleItems, AddOnItem = drink
        };
    }

    // CartDemand.BestItem: highest price conversion, then more stock, then ordinal item name.
    private (string? Item, double Conversion) BestItem(IReadOnlyDictionary<string, double> stock, string category,
        string hood, Conditions c)
    {
        string? best = null;
        double bestConversion = 0;
        double bestUnits = 0;
        foreach (var entry in stock.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (entry.Value <= 1e-6 || tables.Product(entry.Key)?.Category != category) continue;
            var conversion = ConversionOf(entry.Key, hood, c);
            if (best == null || conversion > bestConversion || (conversion == bestConversion && entry.Value > bestUnits))
            {
                best = entry.Key;
                bestConversion = conversion;
                bestUnits = entry.Value;
            }
        }
        return (best, bestConversion);
    }

    // CartDemand.BestAddOn: best conversion across add-on categories; equal conversions keep the first category.
    private (string? Item, double Conversion) BestAddOn(IReadOnlyDictionary<string, double> stock, string hood, Conditions c)
    {
        (string? Item, double Conversion) best = (null, 0);
        foreach (var category in stock.Keys.Select(k => tables.Product(k)?.Category).Where(c => c != null).Distinct())
        {
            if (tables.Category(category!).AddOnFactor <= 0) continue;
            var candidate = BestItem(stock, category!, hood, c);
            if (candidate.Item != null && candidate.Conversion > best.Conversion) best = candidate;
        }
        return best;
    }

    public bool IsDrink(string item) => tables.Category(tables.Product(item)!.Category).AddOnFactor > 0;
}

public sealed class DaypartTotals
{
    public double Minutes;
    public double Passers;
    public double Demand;
    public double Served;
    public double Units;
    public double Revenue;
    public double Cost;
    public double Capacity;
    public double Profit => Revenue - Cost;
    public double PerHour(double v) => Minutes > 0 ? v * 60 / Minutes : 0;
}

public sealed class StopResult
{
    public Dictionary<string, double> Sold { get; } = new(StringComparer.Ordinal);
    public Dictionary<Daypart, DaypartTotals> ByDaypart { get; } = new();
    public double Revenue;
    public double Cost;
    public double Customers;
    public int? SoldOutAt;
    public double Profit => Revenue - Cost;
    public double Units => Sold.Values.Sum();

    public void Add(StopResult other)
    {
        foreach (var p in other.Sold) Sold[p.Key] = Sold.GetValueOrDefault(p.Key) + p.Value;
        foreach (var p in other.ByDaypart)
        {
            if (!ByDaypart.TryGetValue(p.Key, out var t)) ByDaypart[p.Key] = t = new DaypartTotals();
            t.Minutes += p.Value.Minutes; t.Passers += p.Value.Passers; t.Demand += p.Value.Demand; t.Served += p.Value.Served;
            t.Units += p.Value.Units; t.Revenue += p.Value.Revenue; t.Cost += p.Value.Cost; t.Capacity += p.Value.Capacity;
        }
        Revenue += other.Revenue; Cost += other.Cost; Customers += other.Customers;
    }
}

public static class Selling
{
    public const int StepMinutes = 5;

    // Sells from `stock` at the spot from minute `from` to `to` (minutes since 00:00 of the day, past 1440 is
    // after midnight). Each served customer takes the plan's best-accepted item for their category by the
    // buyer shares, and the plan's add-on drink at its rate, as CartSelling.ServeCustomer does. Stops when nothing on
    // the menu is left.
    public static StopResult Run(Cart cart, Spot spot, int from, int to, Conditions c, Dictionary<string, double> stock)
    {
        var r = new StopResult();
        for (var t = from; t < to; t += StepMinutes)
        {
            var step = Math.Min(StepMinutes, to - t);
            var plan = cart.Plan(spot, t, c, stock);
            if (plan == HourPlan.None) { r.SoldOutAt = t; break; }
            if (!r.ByDaypart.TryGetValue(plan.Daypart, out var d)) r.ByDaypart[plan.Daypart] = d = new DaypartTotals();
            d.Minutes += step;
            d.Passers += plan.Passers * step / 60;
            d.Demand += plan.Demand * step / 60;
            d.Capacity += plan.Capacity * step / 60;
            var served = plan.Served * step / 60;
            var foodBuyers = 0.0;
            var servedNow = 0.0;
            foreach (var share in plan.BuyerShares.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!plan.SaleItems.TryGetValue(share.Key, out var item) || stock.GetValueOrDefault(item) <= 1e-6) continue;
                var units = Math.Min(served * share.Value, stock[item]);
                Sell(cart, spot, c, stock, item, units, r, d);
                servedNow += units;
                if (!cart.IsDrink(item)) foodBuyers += units;
            }
            if (plan.DrinksPerFoodBuyer > 0 && foodBuyers > 0)
            {
                var drink = plan.AddOnItem;
                if (drink != null && stock.GetValueOrDefault(drink) > 1e-6)
                    Sell(cart, spot, c, stock, drink, Math.Min(foodBuyers * plan.DrinksPerFoodBuyer, stock[drink]), r, d);
            }
            d.Served += servedNow;
            r.Customers += servedNow;
        }
        return r;
    }

    private static void Sell(Cart cart, Spot spot, Conditions c, Dictionary<string, double> stock, string item, double units, StopResult r, DaypartTotals d)
    {
        if (units <= 0) return;
        stock[item] -= units;
        r.Sold[item] = r.Sold.GetValueOrDefault(item) + units;
        var revenue = units * cart.PriceOf(item, spot.Hood, c);
        var cost = units * cart.Prices.Items[item].Wholesale;
        r.Revenue += revenue; r.Cost += cost;
        d.Units += units; d.Revenue += revenue; d.Cost += cost;
    }

    // Splits the cart's unit limit across its menu by what the stop would sell with unlimited stock.
    public static Dictionary<string, double> Load(Cart cart, Spot spot, int from, int to, Conditions c)
    {
        var unlimited = cart.Menu.ToDictionary(i => i, _ => 1e9, StringComparer.Ordinal);
        var dry = Run(cart, spot, from, to, c, unlimited);
        var total = dry.Units;
        if (total <= 0) return cart.Menu.ToDictionary(i => i, i => cart.StockUnits / cart.Menu.Count / cart.WeightOf(i), StringComparer.Ordinal);
        // Fill the unit limit in the proportions the stop sells, each item counting at its stock weight.
        var weighted = cart.Menu.Sum(i => dry.Sold.GetValueOrDefault(i) * cart.WeightOf(i));
        return cart.Menu.ToDictionary(i => i, i => cart.StockUnits * dry.Sold.GetValueOrDefault(i) / weighted, StringComparer.Ordinal);
    }

    public static Dictionary<string, double> Unlimited(Cart cart) => cart.Menu.ToDictionary(i => i, _ => 1e9, StringComparer.Ordinal);
}
