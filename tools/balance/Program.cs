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
// Pitches the city's buildings can't produce, as archetype spots: a park (the design's waterfront and park
// area type, which only hand-drawn zones give) and, when the data has a "transit" area, a subway entrance.
// Each sits at the middle of its neighbourhood's buildings, a stand-in for a real location.
foreach (var g in city.Buildings.GroupBy(b => (b.Hood, b.Region)))
{
    var x = g.Average(b => b.X);
    var z = g.Average(b => b.Z);
    if (opts.Parks) spots.Add(new Spot("park (archetype)", AreaMix.Single("waterfront"), g.Key.Hood, g.Key.Region, x, z));
    if (opts.Parks && tables.HasArea("transit")) spots.Add(new Spot("subway entrance (archetype)", AreaMix.Single("transit"), g.Key.Hood, g.Key.Region, x, z + 40));
}
Conditions Cond(DayOfWeek day = DayOfWeek.Wednesday, bool rain = false, Pricing? pricing = null, double? factor = null) =>
    new() { Day = day, Rain = rain, Pricing = pricing ?? opts.Pricing, PriceFactor = factor ?? opts.PriceFactor, MarketDemand = opts.Market };
var conditions = new[] { Cond(), Cond(rain: true), Cond(DayOfWeek.Saturday), Cond(DayOfWeek.Saturday, true) };

const string HotDog = "ba:itemname_hotdog", Soda = "ba:itemname_sodacan", Coffee = "ba:itemname_cupofcoffee", Croissant = "ba:itemname_croissant";
Cart MakeCart(string[] menu, string name, int? units = null, int? staff = null) => new(tables, prices, menu, name, units, staff);
var carts = new Dictionary<string, (Cart Cart, string[] Equipment)>
{
    ["hotdog"] = (MakeCart(new[] { HotDog }, "hot dog grill"), new[] { "hotdoggrill" }),
    ["hotdog+cooler"] = (MakeCart(new[] { HotDog, Soda }, "hot dog grill + cooler"), new[] { "hotdoggrill", "cooler" }),
    ["hotdog+coffee"] = (MakeCart(new[] { HotDog, Coffee }, "hot dog grill + coffee machine"), new[] { "hotdoggrill", "coffeemachine" }),
    ["coffee+bakery"] = (MakeCart(new[] { Coffee, Croissant }, "coffee machine + display case (croissants)"), new[] { "coffeemachine", "displaycase" }),
    // Not a vehicle in the data yet: a truck stand-in with the truck's 160 units, the player and one hire at the
    // window, and a grill, coffee machine, display case and cooler (four slots).
    ["truck-like"] = (MakeCart(new[] { HotDog, Coffee, Croissant, Soda }, "truck stand-in: 2 at the window, 160 units, 4 items",
        tables.Vehicle("truck")?.StockUnits ?? 160, 2), new[] { "hotdoggrill", "coffeemachine", "displaycase", "cooler" }),
};
// Each cart's 16-hour day and the ways a route may split it (staying put is one of them).
var days = new Dictionary<string, (int From, int To, (string Name, (int, int)[] Windows)[] Splits)>
{
    ["hotdog+cooler"] = (540, 1500, new[]
    {
        ("change at 14:30", new[] { (540, 870), (870, 1500) }),
        ("change at 17:00", new[] { (540, 1020), (1020, 1500) }),
        ("change at 14:30 and 21:00", new[] { (540, 870), (870, 1260), (1260, 1500) }),
    }),
    ["truck-like"] = (420, 1380, new[]
    {
        ("change at 11:00", new[] { (420, 660), (660, 1380) }),
        ("change at 14:30", new[] { (420, 870), (870, 1380) }),
        ("change at 11:00 and 14:30", new[] { (420, 660), (660, 870), (870, 1380) }),
        ("change at 11:00 and 17:00", new[] { (420, 660), (660, 1020), (1020, 1380) }),
    }),
    ["coffee+bakery"] = (360, 1320, new[]
    {
        ("change at 11:00", new[] { (360, 660), (660, 1320) }),
        ("change at 14:30", new[] { (360, 870), (870, 1320) }),
        ("change at 11:00 and 14:30", new[] { (360, 660), (660, 870), (870, 1320) }),
    }),
};

var hoods = new[] { "ba:neighborhood_midtown", "ba:neighborhood_lowermanhattan", "ba:neighborhood_murrayhill", "ba:neighborhood_hellskitchen",
    "ba:neighborhood_garmentdistrict", "ba:neighborhood_industrycity", "ba:neighborhood_thehamptons" };
var areas = new[] { "office", "residential", "nightlife", "industrial", "retail", "waterfront", "transit" }.Where(tables.HasArea).ToArray();
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
    o.AppendLine("| Neighbourhood | Spots | Mostly office | Mostly residential | Mostly retail | Mostly industrial | Mostly nightlife | Nightlife 0.3 or more | Best office share |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|");
    foreach (var hood in hoods)
    {
        var hs = spots.Where(s => s.Hood == hood && !s.Name.Contains("archetype")).ToList();
        int Mostly(string a) => hs.Count(s => s.Areas.ShareOf(a) >= 0.5);
        o.AppendLine($"| {H(hood)} | {hs.Count} | {Mostly("office")} | {Mostly("residential")} | {Mostly("retail")} | {Mostly("industrial")} | {Mostly("nightlife")} | {hs.Count(s => s.Areas.ShareOf("nightlife") >= 0.3)} | {hs.Max(s => s.Areas.ShareOf("office")):0.00} |");
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
        o.AppendLine($"## Best single spot per neighbourhood, {cart.Name}, {Clock(days[key].From)} to {Clock(days[key].To)}");
        o.AppendLine();
        o.AppendLine("Profit = sales at the cart's price minus wholesale cost of what sold. Restocks walk to the nearest wholesaler and back.");
        o.AppendLine();
        o.AppendLine("| Neighbourhood | Spot (area mix) | " + string.Join(" | ", conditions.Select(c => c.Label)) + " | Units (weekday dry) | Restocks |");
        o.AppendLine("|---|---|" + string.Concat(conditions.Select(_ => "---|")) + "---|---|");
        foreach (var hood in hoods)
        {
            var hs = spots.Where(s => s.Hood == hood).ToList();
            var (dFrom, dTo, _) = days[key];
            var best = hs.Select(s => (s, d: planner.SingleSpot(s, dFrom, dTo, conditions[0]))).OrderByDescending(x => x.d.Total.Profit).First();
            var cells = conditions.Select(c => Money(planner.SingleSpot(best.s, dFrom, dTo, c).Total.Profit));
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

var ladder = new List<string>();
if (opts.Wants("compare"))
{
    // Moving against staying: the best single spot over the cart's 16-hour day against the best route that
    // splits the same hours into two or three pitches. The five best all-day spots join every shortlist,
    // so a route can always stay put.
    foreach (var key in opts.Carts)
    {
        var (cart, equipment) = carts[key];
        var planner = PlannerFor(cart);
        var (dFrom, dTo, splits) = days[key];
        var cityRows = new Dictionary<string, (DayResult Stay, DayResult? Move)>();
        foreach (var (label, c, perHood) in new[] { ("weekday dry", conditions[0], true), ("weekend dry", conditions[2], true),
                     ("weekday dry, default price", Cond(pricing: Pricing.Default, factor: 1), false) })
        {
            o.AppendLine($"## Staying against moving, {cart.Name}, {label}, {Clock(dFrom)} to {Clock(dTo)}");
            o.AppendLine();
            o.AppendLine("| Where | Best single spot | Profit | Best route | Profit | Staying ÷ route |");
            o.AppendLine("|---|---|---|---|---|---|");
            foreach (var hood in perHood ? hoods.Append("city") : new[] { "city" })
            {
                var pool = hood == "city" ? spots : spots.Where(s => s.Hood == hood).ToList();
                var stays = pool.Select(s => planner.SingleSpot(s, dFrom, dTo, c)).OrderByDescending(d => d.Total.Profit).ToList();
                var stay = stays[0];
                var stayers = stays.Take(5).Select(d => d.Stays[0].Spot).ToList();
                DayResult? move = null;
                string? moveName = null;
                foreach (var split in splits)
                {
                    var r = planner.BestRoute(pool, split.Windows, c, 20, stayers);
                    if (r != null && (move == null || r.Total.Profit > move.Total.Profit)) { move = r; moveName = split.Name; }
                }
                if (move != null && move.Total.Profit < stay.Total.Profit) { move = stay; moveName = "stay"; }
                if (hood == "city") cityRows[label] = (stay, move);
                var s0 = stay.Stays[0];
                o.AppendLine($"| {H(hood)} | {s0.Spot.Name} ({s0.Spot.Areas}), {s0.Restocks} refills | {Money(stay.Total.Profit)} | " +
                             (move == null ? "none | | |" : $"{moveName}: {string.Join(" → ", move.Stays.Select(x => $"{x.Spot.Name} ({H(x.Spot.Hood)}, {x.Spot.Areas}) {Clock(x.From)}"))}, {move.Restocks} refills | {Money(move.Total.Profit)} | {stay.Total.Profit / move.Total.Profit:P0} |"));
            }
            o.AppendLine();
        }
        var eq = equipment.Sum(e => prices.Equipment[e]);
        var load = Selling.Load(cart, new Spot("office", AreaMix.Single("office"), "ba:neighborhood_midtown", "", 0, 0), dFrom, dFrom + 300, conditions[0]);
        var start = opts.CartPrice + eq + load.Sum(p => p.Value * prices.Items[p.Key].Wholesale);
        string R(string l) => cityRows[l].Move == null ? "-" : Money(cityRows[l].Move!.Total.Profit);
        ladder.Add($"| {cart.Name} | {Money(start)} | {R("weekday dry")} | {R("weekday dry, default price")} | {R("weekend dry")} | " +
                   $"{cityRows["weekday dry"].Stay.Total.Profit / cityRows["weekday dry"].Move!.Total.Profit:P0} |");
    }
    o.AppendLine("## Ladder");
    o.AppendLine();
    o.AppendLine("| Cart | Cost to start | Best route, ceiling, weekday dry | Best route, default price | Best route, ceiling, weekend dry | Staying ÷ route, weekday |");
    o.AppendLine("|---|---|---|---|---|---|");
    foreach (var l in ladder) o.AppendLine(l);
    o.AppendLine();
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

// Merge native data fields before Core loads them. Core owns stock weights and area appetite.
static DemandTables LoadTables(Options opts)
{
    var demand = JObject.Parse(File.ReadAllText(Path.Combine(opts.Data, "demand.json")));
    var economy = JObject.Parse(File.ReadAllText(Path.Combine(opts.Data, "economy.json")));
    var zones = File.ReadAllText(Path.Combine(opts.Data, "zones.json"));
    if (opts.Patch != null)
    {
        var patch = JObject.Parse(File.ReadAllText(opts.Patch));
        if (patch["rules"] != null)
            throw new ArgumentException("The patch's legacy 'rules' section is no longer supported. Use economy.products.<item>.stockWeight and demand.areas.<area>.appetite instead.");
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
    public bool Parks = true;
    public string[] Carts = { "hotdog+cooler", "coffee+bakery", "truck-like" };
    public HashSet<string> Reports = new() { "archetypes", "spots", "single", "compare", "pricing", "costs" };

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
                case "--no-parks": case "--no-archetypes": o.Parks = false; break;
                case "--carts": o.Carts = Next().Split(','); break;
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
