using System.Globalization;
using System.Text;
using FoodTrucks.Balance;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

// The cart balance calculator. See README.md.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var opts = Options.Parse(args);
var tables = LoadTables(opts);
var prices = GamePrices.Load(opts.Prices);
var city = City.Load(opts.City);
var travel = new Travel { MetresPerMinute = opts.MetresPerSecond };
var spots = city.Spots(tables);
Conditions Cond(DayOfWeek day = DayOfWeek.Wednesday, bool rain = false, Pricing? pricing = null, double? factor = null) =>
    new() { Day = day, Rain = rain, Pricing = pricing ?? opts.Pricing, PriceFactor = factor ?? opts.PriceFactor, MarketDemand = opts.Market };
var conditions = new[] { Cond(), Cond(rain: true), Cond(DayOfWeek.Saturday), Cond(DayOfWeek.Saturday, true) };

const string HotDog = "ba:itemname_hotdog", Soda = "ba:itemname_sodacan", Coffee = "ba:itemname_cupofcoffee", Croissant = "ba:itemname_croissant";
var carts = new Dictionary<string, (Cart Cart, string[] Equipment)>
{
    ["hotdog"] = (new Cart(tables, prices, new[] { HotDog }, "hot dog grill"), new[] { "hotdoggrill" }),
    ["hotdog+cooler"] = (new Cart(tables, prices, new[] { HotDog, Soda }, "hot dog grill + cooler"), new[] { "hotdoggrill", "cooler" }),
    ["hotdog+coffee"] = (new Cart(tables, prices, new[] { HotDog, Coffee }, "hot dog grill + coffee machine"), new[] { "hotdoggrill", "coffeemachine" }),
    ["coffee+bakery"] = (new Cart(tables, prices, new[] { Coffee, Croissant }, "coffee machine + display case (croissants)"), new[] { "coffeemachine", "displaycase" }),
};

var hoods = new[] { "ba:neighborhood_midtown", "ba:neighborhood_lowermanhattan", "ba:neighborhood_murrayhill", "ba:neighborhood_hellskitchen",
    "ba:neighborhood_garmentdistrict", "ba:neighborhood_industrycity", "ba:neighborhood_thehamptons" };
var areas = new[] { "office", "residential", "nightlife", "industrial", "retail" };
var dayparts = new[] { Daypart.Early, Daypart.Morning, Daypart.Lunch, Daypart.Afternoon, Daypart.Evening, Daypart.Late };
string H(string hood) => hood.Replace("ba:neighborhood_", "");
string Clock(int m) => $"{m / 60 % 24:00}:{m % 60:00}";
string Money(double v) => v.ToString("#,0", CultureInfo.InvariantCulture);

var o = new StringBuilder();
o.AppendLine($"# Cart balance run");
o.AppendLine();
o.AppendLine($"Data: {opts.Data}{(opts.Patch != null ? $" patched with {opts.Patch}" : "")}. Pricing: {opts.Pricing}{(opts.PriceFactor != 1 ? $" × {opts.PriceFactor}" : "")}. Market demand: {opts.Market}. " +
             $"Restock: {opts.Restock}. Pushing speed: {opts.MetresPerSecond} m/s ({opts.MetresPerSecond * 60:0} m per in-game hour). " +
             $"Cart: {tables.Vehicle("cart")!.StockUnits} units, {tables.Vehicle("cart")!.Staff} staff. passersPerHour {tables.PassersPerHour}. Month: May.");
o.AppendLine();

if (opts.Wants("archetypes"))
{
    foreach (var key in new[] { "hotdog+cooler", "coffee+bakery" })
    {
        var cart = carts[key].Cart;
        foreach (var c in new[] { conditions[0], conditions[2] })
        {
            o.AppendLine($"## Pure area spots, {cart.Name}, {c.Label}: customers wanting to buy per hour / profit per hour");
            o.AppendLine();
            o.AppendLine("Unlimited stock. Customers: the model's customers per hour before price and the window. Profit: sales at the window's limit, price minus wholesale.");
            o.AppendLine();
            o.AppendLine("| Neighbourhood | Area | " + string.Join(" | ", dayparts) + " | Best 3.5 h |");
            o.AppendLine("|---|---|" + string.Concat(dayparts.Select(_ => "---|")) + "---|");
            foreach (var hood in hoods)
            foreach (var area in areas)
            {
                var spot = new Spot(area, AreaMix.Single(area), hood, "", 0, 0);
                var r = Selling.Run(cart, spot, 300, 1560, c, Selling.Unlimited(cart));
                var cells = dayparts.Select(dp => r.ByDaypart.TryGetValue(dp, out var t)
                    ? $"{t.PerHour(t.Demand):0} / {Money(t.PerHour(t.Profit))}" : "-");
                var best = dayparts.Max(dp => r.ByDaypart.TryGetValue(dp, out var t) ? t.PerHour(t.Profit) : 0) * 3.5;
                o.AppendLine($"| {H(hood)} | {area} | {string.Join(" | ", cells)} | {Money(best)} |");
            }
            o.AppendLine();
        }
    }

    // How the day and the weather move one strong pitch.
    var hd = carts["hotdog+cooler"].Cart;
    o.AppendLine("## One pitch under each condition: Midtown office lunch, Hell's Kitchen nightlife late, hot dog grill + cooler");
    o.AppendLine();
    o.AppendLine("| Conditions | Midtown office lunch: customers/h, served/h, profit/h | Hell's Kitchen nightlife late: customers/h, served/h, profit/h |");
    o.AppendLine("|---|---|---|");
    foreach (var c in conditions)
    {
        string Cell(string area, string hood, Daypart dp)
        {
            var r = Selling.Run(hd, new Spot(area, AreaMix.Single(area), hood, "", 0, 0), 300, 1560, c, Selling.Unlimited(hd));
            var t = r.ByDaypart[dp];
            return $"{t.PerHour(t.Demand):0}, {t.PerHour(t.Served):0.0}, ${Money(t.PerHour(t.Profit))}";
        }
        o.AppendLine($"| {c.Label} | {Cell("office", "ba:neighborhood_midtown", Daypart.Lunch)} | {Cell("nightlife", "ba:neighborhood_hellskitchen", Daypart.Late)} |");
    }
    o.AppendLine();
}

if (opts.Wants("spots"))
{
    o.AppendLine("## Real spots: what the classifier makes of the city");
    o.AppendLine();
    o.AppendLine("One pitch in front of each building; buildings whose outline is within the classifier radius vote.");
    o.AppendLine();
    o.AppendLine("| Neighbourhood | Spots | Mostly office | Mostly residential | Mostly retail | Mostly industrial | Mostly nightlife | Best office share |");
    o.AppendLine("|---|---|---|---|---|---|---|---|");
    foreach (var hood in hoods)
    {
        var hs = spots.Where(s => s.Hood == hood).ToList();
        int Mostly(string a) => hs.Count(s => s.Areas.ShareOf(a) >= 0.5);
        o.AppendLine($"| {H(hood)} | {hs.Count} | {Mostly("office")} | {Mostly("residential")} | {Mostly("retail")} | {Mostly("industrial")} | {Mostly("nightlife")} | {hs.Max(s => s.Areas.ShareOf("office")):0.00} |");
    }
    o.AppendLine();
}

Planner PlannerFor(Cart cart) => new(cart, city, travel) { Mode = opts.Restock };

string Describe(DayResult d, Conditions c, Cart cart) =>
    string.Join("; ", d.Stays.Select(s => $"{s.Spot.Name} ({H(s.Spot.Hood)}: {s.Spot.Areas}) {Clock(s.From)}-{Clock(s.To)}, " +
                                            $"{s.Result.Units:0} units, {s.Restocks} restock{(s.Restocks == 1 ? "" : "s")}"));

if (opts.Wants("single"))
{
    foreach (var key in new[] { "hotdog+cooler", "coffee+bakery" })
    {
        var cart = carts[key].Cart;
        var planner = PlannerFor(cart);
        o.AppendLine($"## Best single spot per neighbourhood, {cart.Name}, 09:00 to 01:00");
        o.AppendLine();
        o.AppendLine("Profit = sales at the cart's price minus wholesale cost of what sold. Restocks walk to the nearest wholesaler and back.");
        o.AppendLine();
        o.AppendLine("| Neighbourhood | Spot (area mix) | " + string.Join(" | ", conditions.Select(c => c.Label)) + " | Units (weekday dry) | Restocks |");
        o.AppendLine("|---|---|" + string.Concat(conditions.Select(_ => "---|")) + "---|---|");
        foreach (var hood in hoods)
        {
            var hs = spots.Where(s => s.Hood == hood).ToList();
            var best = hs.Select(s => (s, d: planner.SingleSpot(s, 540, 1500, conditions[0]))).OrderByDescending(x => x.d.Total.Profit).First();
            var cells = conditions.Select(c => Money(planner.SingleSpot(best.s, 540, 1500, c).Total.Profit));
            o.AppendLine($"| {H(hood)} | {best.s.Name} ({best.s.Areas}) | {string.Join(" | ", cells)} | {best.d.Total.Units:0} | {best.d.Restocks} |");
        }
        o.AppendLine();
    }
}

var routes = new List<(string Name, string Cart, (int, int)[] Windows)>
{
    ("lunch + evening", "hotdog+cooler", new[] { (660, 870), (1020, 1380) }),
    ("lunch + evening + late", "hotdog+cooler", new[] { (660, 870), (1020, 1260), (1260, 1500) }),
    ("breakfast + lunch + evening", "hotdog+coffee", new[] { (360, 630), (660, 870), (1020, 1380) }),
    ("breakfast + afternoon", "coffee+bakery", new[] { (360, 660), (870, 1020) }),
};

if (opts.Wants("routes"))
{
    o.AppendLine("## Best routes city-wide");
    o.AppendLine();
    o.AppendLine("Each pitch's best 20 spots on its own, then every combination within one map region. The cart starts full; between pitches it goes through the nearest wholesaler.");
    o.AppendLine();
    o.AppendLine("| Route | Cart | Conditions | Profit | Units | Customers | Hours (first open to close) | Walking (min) | Pitches |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|");
    foreach (var route in routes)
    {
        var cart = carts[route.Cart].Cart;
        var planner = PlannerFor(cart);
        foreach (var c in conditions)
        {
            var best = planner.BestRoute(spots, route.Windows, c);
            if (best == null) continue;
            var walking = best.MoveMinutes + best.Stays.Sum(s => s.RestockMinutes);
            o.AppendLine($"| {route.Name} | {cart.Name} | {c.Label} | {Money(best.Total.Profit)} | {best.Total.Units:0} | {best.Total.Customers:0} | {best.Hours:0.0} | {walking} | {Describe(best, c, cart)} |");
        }
    }
    o.AppendLine();

    o.AppendLine("## Best lunch + evening route per neighbourhood (hot dog grill + cooler, weekday dry)");
    o.AppendLine();
    o.AppendLine("| Neighbourhood | Profit | Units | Walking (min) | Pitches |");
    o.AppendLine("|---|---|---|---|---|");
    var hdc = carts["hotdog+cooler"].Cart;
    var hdp = PlannerFor(hdc);
    foreach (var hood in hoods)
    {
        var best = hdp.BestRoute(spots.Where(s => s.Hood == hood).ToList(), routes[0].Windows, conditions[0]);
        if (best == null) { o.AppendLine($"| {H(hood)} | no route | | | |"); continue; }
        o.AppendLine($"| {H(hood)} | {Money(best.Total.Profit)} | {best.Total.Units:0} | {best.MoveMinutes + best.Stays.Sum(s => s.RestockMinutes)} | {Describe(best, conditions[0], hdc)} |");
    }
    o.AppendLine();
}

if (opts.Wants("compare"))
{
    // Moving against staying: the best single spot all day (09:00 to 01:00, the same 16 hours a route may use) against the best route
    // (lunch + evening, or lunch + evening + late), hot dog grill + cooler.
    var cart = carts["hotdog+cooler"].Cart;
    var planner = PlannerFor(cart);
    // Routes that split the same 16 hours into two or three pitches; staying put is one of them.
    var hotdogRoutes = new List<(string Name, (int, int)[] Windows)>
    {
        ("2 pitches, change at 14:30", new[] { (540, 870), (870, 1500) }),
        ("2 pitches, change at 17:00", new[] { (540, 1020), (1020, 1500) }),
        ("3 pitches, change at 14:30 and 21:00", new[] { (540, 870), (870, 1260), (1260, 1500) }),
    };
    foreach (var c in new[] { conditions[0], conditions[2] })
    {
        o.AppendLine($"## Staying against moving, {cart.Name}, {c.Label}");
        o.AppendLine();
        o.AppendLine("| Where | Best single spot 09:00-01:00 | Profit | Best route | Profit | Staying ÷ route |");
        o.AppendLine("|---|---|---|---|---|---|");
        foreach (var hood in hoods.Append("city"))
        {
            var pool = hood == "city" ? spots : spots.Where(s => s.Hood == hood).ToList();
            var stays = pool.Select(s => planner.SingleSpot(s, 540, 1500, c)).OrderByDescending(d => d.Total.Profit).ToList();
            var stay = stays[0];
            // The best all-day spots join every pitch's shortlist, so a route can always stay put.
            var stayers = stays.Take(5).Select(d => d.Stays[0].Spot).ToList();
            DayResult? move = null;
            string? moveName = null;
            foreach (var route in hotdogRoutes)
            {
                var r = planner.BestRoute(pool, route.Windows, c, 20, stayers);
                if (r != null && (move == null || r.Total.Profit > move.Total.Profit)) { move = r; moveName = route.Name; }
            }
            var s0 = stay.Stays[0];
            o.AppendLine($"| {H(hood)} | {s0.Spot.Name} ({s0.Spot.Areas}), {s0.Restocks} restocks | {Money(stay.Total.Profit)} | " +
                         (move == null ? "none | | |" : $"{moveName}: {string.Join(" → ", move.Stays.Select(x => x.Spot.Name))}, {move.Restocks} restocks | {Money(move.Total.Profit)} | {stay.Total.Profit / move.Total.Profit:P0} |"));
        }
        o.AppendLine();
    }
}

if (opts.Wants("pricing"))
{
    var cart = carts["hotdog+cooler"].Cart;
    var planner = PlannerFor(cart);
    o.AppendLine($"## Price sensitivity: best lunch + evening route city-wide, {cart.Name}, weekday dry");
    o.AppendLine();
    o.AppendLine("| Price | Profit | Units | Pitches |");
    o.AppendLine("|---|---|---|---|");
    foreach (var (label, c) in new (string, Conditions)[]
             {
                 ("default price (today)", Cond(pricing: Pricing.Default, factor: 1)),
                 ("pricing manager, capture 0.5", Cond(pricing: Pricing.Manager, factor: 0.5)),
                 ("pricing manager, capture 0.75", Cond(pricing: Pricing.Manager, factor: 0.75)),
                 ("ceiling × 0.9", Cond(pricing: Pricing.NeighbourhoodIndex, factor: 0.9)),
                 ("ceiling: the most every class accepts (default × neighbourhood index)", Cond(pricing: Pricing.NeighbourhoodIndex, factor: 1)),
                 ("ceiling × 1.1", Cond(pricing: Pricing.NeighbourhoodIndex, factor: 1.1)),
                 ("upper-class maximum: default × max(1.7, index)", Cond(pricing: Pricing.UpperClass, factor: 1)),
             })
    {
        var best = planner.BestRoute(spots, routes[0].Windows, c);
        if (best == null) continue;
        o.AppendLine($"| {label} | {Money(best.Total.Profit)} | {best.Total.Units:0} | {Describe(best, c, cart)} |");
    }
    o.AppendLine();
    o.AppendLine("Midtown office lunch, one hour, by price:");
    o.AppendLine();
    o.AppendLine("| Price | Hot dog price | Bought / customers | Served/h | Profit/h |");
    o.AppendLine("|---|---|---|---|---|");
    var office = new Spot("office", AreaMix.Single("office"), "ba:neighborhood_midtown", "", 0, 0);
    foreach (var (label, c) in new (string, Conditions)[]
             {
                 ("default", Cond(pricing: Pricing.Default, factor: 1)),
                 ("manager, capture 0.5", Cond(pricing: Pricing.Manager, factor: 0.5)), ("manager, capture 0.75", Cond(pricing: Pricing.Manager, factor: 0.75)),
                 ("ceiling × 0.9", Cond(pricing: Pricing.NeighbourhoodIndex, factor: 0.9)),
                 ("ceiling", Cond(pricing: Pricing.NeighbourhoodIndex, factor: 1)), ("ceiling × 1.1", Cond(pricing: Pricing.NeighbourhoodIndex, factor: 1.1)),
                 ("upper-class maximum", Cond(pricing: Pricing.UpperClass, factor: 1)),
             })
    {
        var r = Selling.Run(cart, office, 660, 870, c, Selling.Unlimited(cart));
        var t = r.ByDaypart[Daypart.Lunch];
        var plan = cart.Plan(office, 700, c, Selling.Unlimited(cart));
        o.AppendLine($"| {label} | ${cart.PriceOf(HotDog, office.Hood, c):0.00} | {plan.Conversion:P0} | {t.PerHour(t.Served):0.0} | ${Money(t.PerHour(t.Profit))} |");
    }
    o.AppendLine();
}

if (opts.Wants("costs"))
{
    o.AppendLine($"## Cost to start (cart ${Money(opts.CartPrice)})");
    o.AppendLine();
    o.AppendLine("| Cart | Equipment | First load (the cart's units at wholesale, split by a Midtown lunch) | Total |");
    o.AppendLine("|---|---|---|---|");
    foreach (var (key, (cart, equipment)) in carts)
    {
        var eq = equipment.Sum(e => prices.Equipment[e]);
        var load = Selling.Load(cart, new Spot("office", AreaMix.Single("office"), "ba:neighborhood_midtown", "", 0, 0), 660, 870, conditions[0]);
        var stock = load.Sum(p => p.Value * prices.Items[p.Key].Wholesale);
        o.AppendLine($"| {cart.Name} | {Money(eq)} | {Money(stock)} | {Money(opts.CartPrice + eq + stock)} |");
    }
    o.AppendLine();
}

if (opts.Out != null) File.WriteAllText(opts.Out, o.ToString());
Console.Write(o.ToString());

static DemandTables LoadTables(Options opts)
{
    var demand = JObject.Parse(File.ReadAllText(Path.Combine(opts.Data, "demand.json")));
    var economy = JObject.Parse(File.ReadAllText(Path.Combine(opts.Data, "economy.json")));
    var zones = File.ReadAllText(Path.Combine(opts.Data, "zones.json"));
    if (opts.Patch != null)
    {
        var patch = JObject.Parse(File.ReadAllText(opts.Patch));
        var settings = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace };
        if (patch["demand"] is JObject d) demand.Merge(d, settings);
        if (patch["economy"] is JObject e) economy.Merge(e, settings);
    }
    return DemandTables.Parse(demand.ToString(), economy.ToString(), zones);
}

sealed class Options
{
    public string Data = "";
    public string City = "";
    public string Prices = "";
    public string? Patch;
    public string? Out;
    public Pricing Pricing = Pricing.NeighbourhoodIndex;
    public double PriceFactor = 1.0;
    public double Market = 100;
    public RestockMode Restock = RestockMode.Wholesaler;
    public double MetresPerSecond = 4.0;
    public double CartPrice = 1200;
    public HashSet<string> Reports = new() { "archetypes", "spots", "single", "routes", "compare", "pricing", "costs" };

    public bool Wants(string report) => Reports.Contains(report);

    public static Options Parse(string[] args)
    {
        var here = AppContext.BaseDirectory;
        var repo = FindRepo(here) ?? FindRepo(Directory.GetCurrentDirectory())
                   ?? throw new InvalidOperationException("Run inside the repository or pass --data, --city and --prices.");
        var o = new Options
        {
            Data = Path.Combine(repo, "data"),
            City = Path.Combine(repo, "tools", "balance", "city.json"),
            Prices = Path.Combine(repo, "tools", "balance", "game-prices.json")
        };
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--data": o.Data = Next(); break;
                case "--city": o.City = Next(); break;
                case "--prices": o.Prices = Next(); break;
                case "--patch": o.Patch = Next(); break;
                case "--out": o.Out = Next(); break;
                case "--pricing":
                    o.Pricing = Next() switch
                    {
                        "ceiling" or "index" => Pricing.NeighbourhoodIndex, "manager" => Pricing.Manager, "upper" => Pricing.UpperClass, _ => Pricing.Default
                    };
                    break;
                case "--price-factor": o.PriceFactor = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--market": o.Market = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--restock": o.Restock = Next() == "hand" ? RestockMode.BoxInHand : RestockMode.Wholesaler; break;
                case "--speed": o.MetresPerSecond = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--cart-price": o.CartPrice = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--reports": o.Reports = new HashSet<string>(Next().Split(',')); break;
                default: throw new ArgumentException($"Unknown option {args[i]}. See tools/balance/README.md.");
            }
        }
        return o;
    }

    private static string? FindRepo(string start)
    {
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "data", "demand.json")) && Directory.Exists(Path.Combine(d.FullName, "Core"))) return d.FullName;
        return null;
    }
}
