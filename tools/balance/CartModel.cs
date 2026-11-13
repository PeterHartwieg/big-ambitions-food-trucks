using System.Collections.Concurrent;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
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
    public Dictionary<string, Dictionary<string, double>> Market { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<string>> MarketLimits { get; } = new(StringComparer.Ordinal);

    public double MarketFor(string item, string hood, double fallback)
    {
        if (MarketLimits.TryGetValue(item, out var limit) && !limit.Contains(hood)) return 100;
        return Market.TryGetValue(item, out var hoods) && hoods.TryGetValue(hood, out var v) ? v : fallback;
    }

    public static GamePrices Load(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        var g = new GamePrices();
        foreach (var p in (JObject)json["items"]!)
            g.Items[p.Key] = new ItemPrice((double)p.Value!["price"]!, (double)p.Value!["wholesale"]!, (int)p.Value!["box"]!);
        foreach (var p in (JObject)json["priceIndex"]!) g.PriceIndex[p.Key] = (double)p.Value!;
        foreach (var p in (JObject)json["equipment"]!) g.Equipment[p.Key] = (double)p.Value!;
        if (json["marketLimits"] is JObject limits)
            foreach (var p in limits)
                if (!p.Key.StartsWith("_")) g.MarketLimits[p.Key] = new HashSet<string>(p.Value!.Values<string>()!, StringComparer.Ordinal);
        g.MergeMarket(json);
        return g;
    }

    public void MergeMarket(JObject json)
    {
        if (json["market"] is JObject market)
            foreach (var p in market)
            {
                if (p.Key.StartsWith("_") || p.Value is not JObject hoods) continue;
                Market[p.Key] = hoods.Properties().Where(h => !h.Name.StartsWith("_")).ToDictionary(h => h.Name, h => (double)h.Value, StringComparer.Ordinal);
            }
    }

    public void Merge(JObject game)
    {
        if (game["items"] is JObject items)
            foreach (var p in items)
            {
                if (p.Key.StartsWith("_")) continue;
                Items[p.Key] = new ItemPrice((double)p.Value!["defaultMarketPrice"]!, (double)p.Value!["wholesalePrice"]!, (int)p.Value!["boxSize"]!);
            }
        MergeMarket(game);
        if (game["equipment"] is JObject equipment)
            foreach (var p in equipment)
                if (!p.Key.StartsWith("_")) Equipment[p.Key] = (double)p.Value!;
    }
}

public enum Pricing
{
    Default,
    NeighbourhoodIndex,
    Manager,
    UpperClass
}

public sealed class Conditions
{
    public DayOfWeek Day { get; init; } = DayOfWeek.Wednesday;
    public bool Rain { get; init; }
    public int Month { get; init; } = 5;
    public double MarketDemand { get; init; } = 100;
    public Pricing Pricing { get; init; } = Pricing.Default;
    public double PriceFactor { get; init; } = 1.0;
    public FoodTrucks.Core.Pitches.PitchBonus Pitch { get; init; } = FoodTrucks.Core.Pitches.PitchBonus.None;
    public int PitchFrom { get; init; }
    public int PitchTo { get; init; } = 2880;

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
    public double Demand { get; init; }
    public double Conversion { get; init; }
    public double Capacity { get; init; }
    public double Served { get; init; }
    public double DrinksPerFoodBuyer { get; init; }
    public IReadOnlyDictionary<string, double> BuyerShares { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, string> SaleItems { get; init; } = new Dictionary<string, string>();
    public string? AddOnItem { get; init; }
}

public sealed class Cart
{
    public const string Kind = "cart";
    private readonly DemandTables tables;
    private readonly DemandModel model;
    private readonly GamePrices prices;

    public Cart(DemandTables tables, GamePrices prices, IReadOnlyList<string> menu, string name, bool storage = false,
        int? staff = null, string kind = Kind, int? servers = null, double speed = 1.0)
    {
        VehicleKind = kind;
        this.tables = tables;
        this.prices = prices;
        model = new DemandModel(tables);
        Menu = menu;
        Name = name;
        Storage = storage;
        var vehicle = tables.Vehicle(kind) ?? throw new InvalidOperationException($"economy.json has no {kind}.");
        Staff = staff ?? vehicle.Staff;
        Servers = Math.Max(1, Math.Min(Staff, servers ?? Staff));
        Speed = speed;
        var factor = storage ? tables.EquipmentIds.Select(tables.Equipment).Where(e => e!.IsStorage).Max(e => e!.StorageFactor) : 1.0;
        PerPiece = FoodTrucks.Core.Selling.Storage.Capacity(vehicle.UnitsPerPiece, 1, factor);
        PieceOf = menu.ToDictionary(i => i, i => tables.EquipmentForProduct(i)?.Id ?? throw new InvalidOperationException($"No equipment serves {i}."),
            StringComparer.Ordinal);
        StockUnits = PieceOf.Values.Distinct().Count() * PerPiece;
    }

    public bool Storage { get; }
    public Cart WithServers(int servers) => new Cart(tables, prices, Menu, Name, Storage, Staff, VehicleKind, servers, Speed);
    public int PerPiece { get; }
    public IReadOnlyDictionary<string, string> PieceOf { get; }

    public string Name { get; }
    public string VehicleKind { get; }
    public double WeightOf(string item) => tables.Product(item)?.StockWeight ?? 1.0;
    public double Load(IReadOnlyDictionary<string, double> stock) => stock.Sum(p => p.Value * WeightOf(p.Key));
    public Dictionary<string, double> PieceLoads(IReadOnlyDictionary<string, double> stock) =>
        PieceOf.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Sum(p => stock.GetValueOrDefault(p.Key) * WeightOf(p.Key)), StringComparer.Ordinal);
    public bool HasSharedPiece => PieceOf.GroupBy(p => p.Value).Any(g => g.Count() > 1);
    public IReadOnlyList<string> Menu { get; }
    public int Staff { get; }
    public int Servers { get; }
    public double Speed { get; }
    public double WindowCapacity(HourPlan plan) => Staff > 0 ? plan.Capacity / Staff * Servers * Speed : 0;
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
            PitchAreas = PitchTime.PitchAt(c, minute).Areas,
            PitchFootfall = PitchTime.PitchAt(c, minute).Footfall,
            Neighbourhood = spot.Hood,
            Menu = sellable,
            MinuteOfDay = minute % 1440,
            Day = day,
            Month = c.Month,
            Raining = c.Rain,
            MarketDemand = sellable.ToDictionary(i => i, i => prices.MarketFor(i, spot.Hood, c.MarketDemand)),
            Promotion = 1.0
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
    public int? FirstOutAt;
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

    public static QueueThroughput? Queue { get; set; }

    public static StopResult Run(Cart cart, Spot spot, int from, int to, Conditions c, Dictionary<string, double> stock,
        bool untilOneOut = false)
    {
        var r = new StopResult();
        var stocked = cart.Menu.Where(i => stock.GetValueOrDefault(i) > 1e-6).ToList();
        for (var t = from; t < to; t += StepMinutes)
        {
            var step = Math.Min(StepMinutes, to - t);
            if (r.FirstOutAt == null && stocked.Any(i => stock.GetValueOrDefault(i) <= 1e-6))
            {
                r.FirstOutAt = t;
                if (untilOneOut) { r.SoldOutAt = t; break; }
            }
            var plan = cart.Plan(spot, t, c, stock);
            if (plan == HourPlan.None) { r.SoldOutAt = t; break; }
            if (!r.ByDaypart.TryGetValue(plan.Daypart, out var d)) r.ByDaypart[plan.Daypart] = d = new DaypartTotals();
            d.Minutes += step;
            d.Passers += plan.Passers * step / 60;
            d.Demand += plan.Demand * step / 60;
            var capacity = cart.WindowCapacity(plan);
            d.Capacity += capacity * step / 60;
            var served = SalesRate.PerHour(plan.Demand, plan.Conversion, capacity) * step / 60;
            if (Queue != null && capacity > 0)
                served *= Queue.Factor(plan.Demand * plan.Conversion, capacity, cart.Servers, c.Rain, cart.VehicleKind);
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

    public static Dictionary<string, double> Load(Cart cart, Spot spot, int from, int to, Conditions c)
    {
        var unlimited = cart.Menu.ToDictionary(i => i, _ => 1e9, StringComparer.Ordinal);
        var dry = Run(cart, spot, from, to, c, unlimited);
        var load = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var piece in cart.Menu.GroupBy(i => cart.PieceOf[i]))
        {
            var items = piece.ToList();
            var weighted = items.Sum(i => dry.Sold.GetValueOrDefault(i) * cart.WeightOf(i));
            foreach (var i in items)
                load[i] = weighted <= 0
                    ? cart.PerPiece / (double)items.Count / cart.WeightOf(i)
                    : cart.PerPiece * dry.Sold.GetValueOrDefault(i) / weighted;
        }
        return load;
    }

    public static Dictionary<string, double> TopUp(Cart cart, Spot spot, int from, int to, Conditions c, IReadOnlyDictionary<string, double> stock)
    {
        var ideal = Load(cart, spot, from, to, c);
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var i in cart.Menu) result[i] = Math.Max(0, stock.GetValueOrDefault(i));
        foreach (var piece in cart.Menu.GroupBy(i => cart.PieceOf[i]))
        {
            var items = piece.ToList();
            var room = cart.PerPiece - items.Sum(i => result[i] * cart.WeightOf(i));
            if (room <= 1e-9) continue;
            var shortfall = items.ToDictionary(i => i, i => Math.Max(0, (ideal[i] - result[i]) * cart.WeightOf(i)));
            var total = shortfall.Values.Sum();
            if (total <= 1e-9) continue;
            foreach (var i in items) result[i] += shortfall[i] * room / total / cart.WeightOf(i);
        }
        return result;
    }

    public static Dictionary<string, double> Unlimited(Cart cart) => cart.Menu.ToDictionary(i => i, _ => 1e9, StringComparer.Ordinal);
}

public sealed class QueueThroughput
{
    private const string StandIn = "ba:itemname_hotdog";
    private const double GridStep = 0.05;
    private const double GridMax = 3.0;
    private const int Hours = 300;

    private readonly DemandTables tables;
    private readonly RuleTables rules;
    private readonly ConcurrentDictionary<(int OrderTenths, int Staff, bool Rain, int Limit), double[]> cache = new();

    public QueueThroughput(DemandTables tables, RuleTables rules)
    {
        this.tables = tables;
        this.rules = rules;
    }

    public double Factor(double buyersPerHour, double capacityPerHour, int staff, bool rain, string kind)
    {
        if (buyersPerHour <= 0 || capacityPerHour <= 0 || staff <= 0) return 1.0;
        var load = buyersPerHour / capacityPerHour;
        if (load >= GridMax) return 1.0;
        var orderMinutes = staff * 60.0 / capacityPerHour;
        var limit = rules.Service.QueueLimit(kind, rules.Queue.MaxQueue);
        var key = ((int)Math.Round(orderMinutes * 10), staff, rain, limit);
        var table = cache.GetOrAdd(key, k => Measure(k.OrderTenths / 10.0, k.Staff, k.Rain, k.Limit));
        var x = load / GridStep;
        var i = Math.Min((int)x, table.Length - 2);
        return table[i] + (table[i + 1] - table[i]) * (x - i);
    }

    private double[] Measure(double orderMinutes, int staff, bool rain, int limit)
    {
        var points = (int)Math.Round(GridMax / GridStep) + 1;
        var table = new double[points];
        var speed = tables.Product(StandIn)!.ServiceMinutes / Math.Max(0.1, orderMinutes);
        var capacity = staff * 60.0 / orderMinutes;
        Parallel.For(0, points, i =>
        {
            if (i == 0) { table[i] = 1.0; return; }
            var simulator = new SalesSimulator(tables, rules.Queue);
            var state = SalesSimulator.Open(1000 + i);
            var input = new SalesInput
            {
                Menu = new[] { StandIn },
                Stock = new Dictionary<string, int> { [StandIn] = int.MaxValue / 2 },
                CustomersPerHour = i * GridStep * capacity,
                CustomerShares = new Dictionary<string, double> { ["fastfood"] = 1.0 },
                Servers = staff,
                Raining = rain,
                ServiceSpeed = speed,
                QueueLimit = limit
            };
            long served = 0, joined = 0, walkedOn = 0;
            for (var h = 0; h < Hours; h++)
            {
                var r = simulator.Advance(state, input, 60);
                served += r.Count(CustomerExit.Served);
                joined += r.Joined.Count;
                walkedOn += r.Count(CustomerExit.QueueFull);
            }
            var fluid = Math.Min(joined + walkedOn, capacity * Hours);
            table[i] = fluid > 0 ? Math.Min(1.0, served / fluid) : 1.0;
        });
        return table;
    }
}

internal static class PitchTime
{
    public static FoodTrucks.Core.Pitches.PitchBonus PitchAt(Conditions c, int minute) =>
        minute % 1440 >= c.PitchFrom && minute % 1440 < c.PitchTo ? c.Pitch : FoodTrucks.Core.Pitches.PitchBonus.None;
}
