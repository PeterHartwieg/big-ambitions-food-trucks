using System.Globalization;
using System.Text;
using FoodTrucks.Balance;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using Newtonsoft.Json.Linq;

// The cart balance calculator. See README.md.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var opts = Options.Parse(args);
var tables = LoadTables(opts);
Selling.Queue = opts.Queue ? new QueueThroughput(tables, RuleTables.Parse(File.ReadAllText(Path.Combine(opts.Data, "rules.json")))) : null;
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
Cart MakeCart(string[] menu, string name, int? units = null, int? staff = null, string kind = Cart.Kind) => new(tables, prices, menu, name, units, staff, kind);
var carts = new Dictionary<string, (Cart Cart, string[] Equipment)>
{
    ["hotdog"] = (MakeCart(new[] { HotDog }, "hot dog grill"), new[] { "hotdoggrill" }),
    ["hotdog+cooler"] = (MakeCart(new[] { HotDog, Soda }, "hot dog grill + cooler"), new[] { "hotdoggrill", "cooler" }),
    ["hotdog+coffee"] = (MakeCart(new[] { HotDog, Coffee }, "hot dog grill + coffee machine"), new[] { "hotdoggrill", "coffeemachine" }),
    ["coffee+bakery"] = (MakeCart(new[] { Coffee, Croissant }, "coffee machine + display case (croissants)"), new[] { "coffeemachine", "displaycase" }),
    // Not a vehicle in the data yet: a truck stand-in with the truck's 160 units, the player and one hire at the
    // window, and a grill, coffee machine, display case and cooler (four slots).
    ["truck-like"] = (MakeCart(new[] { HotDog, Coffee, Croissant, Soda }, "truck stand-in: 2 at the window, 160 units, 4 items",
        tables.Vehicle("truck")?.StockUnits ?? 160, 2, "truck"), new[] { "hotdoggrill", "coffeemachine", "displaycase", "cooler" }),
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
    o.AppendLine("| Neighbourhood | Spots | Office presence 0.5+ | Residential 0.5+ | Retail 0.5+ | Industrial 0.5+ | Nightlife 0.5+ | Nightlife 0.3+ | Highest office presence |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|");
    foreach (var hood in hoods)
    {
        var hs = spots.Where(s => s.Hood == hood && !s.Name.Contains("archetype")).ToList();
        int Mostly(string a) => hs.Count(s => s.Areas.PresenceOf(a) >= 0.5);
        o.AppendLine($"| {H(hood)} | {hs.Count} | {Mostly("office")} | {Mostly("residential")} | {Mostly("retail")} | {Mostly("industrial")} | {Mostly("nightlife")} | {hs.Count(s => s.Areas.PresenceOf("nightlife") >= 0.3)} | {hs.Max(s => s.Areas.PresenceOf("office")):0.00} |");
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

// Candidates for the premium spots: each district's best pitches for a hot dog cart and a coffee cart, on a dry
// weekday and a dry Saturday, so the picks can cover lunch, mornings, nights and weekends.
if (opts.Wants("premium"))
{
    o.AppendLine("## Premium spot candidates: the best pitches per district");
    o.AppendLine();
    o.AppendLine("| Neighbourhood | Spot | Area mix | Hot dog weekday | Hot dog Saturday | Coffee weekday | Coffee Saturday |");
    o.AppendLine("|---|---|---|---|---|---|---|");
    var hotdog = PlannerFor(carts["hotdog+cooler"].Cart);
    var coffee = PlannerFor(carts["coffee+bakery"].Cart);
    var (hFrom, hTo, _) = days["hotdog+cooler"];
    var (cFrom, cTo, _) = days["coffee+bakery"];
    foreach (var hood in hoods)
    {
        var rows = spots.Where(s => s.Hood == hood && !s.Name.Contains("archetype")).Select(s => (s,
            hw: hotdog.SingleSpot(s, hFrom, hTo, conditions[0]).Total.Profit,
            hs: hotdog.SingleSpot(s, hFrom, hTo, conditions[2]).Total.Profit,
            cw: coffee.SingleSpot(s, cFrom, cTo, conditions[0]).Total.Profit,
            cs: coffee.SingleSpot(s, cFrom, cTo, conditions[2]).Total.Profit)).ToList();
        var picked = rows.OrderByDescending(r => r.hw).Take(4)
            .Concat(rows.OrderByDescending(r => r.hs).Take(4))
            .Concat(rows.OrderByDescending(r => r.cw).Take(4))
            .Concat(rows.OrderByDescending(r => r.cs).Take(4))
            .GroupBy(r => r.s.Name).Select(g => g.First());
        foreach (var r in picked.OrderByDescending(r => Math.Max(Math.Max(r.hw, r.hs), Math.Max(r.cw, r.cs))))
            o.AppendLine($"| {H(hood)} | {r.s.Name} | {r.s.Areas} | {Money(r.hw)} | {Money(r.hs)} | {Money(r.cw)} | {Money(r.cs)} |");
    }
    o.AppendLine();
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

if (opts.Wants("ingame"))
{
    // The case found in game on 8 October: two offices 20 and 40 m away and a nightclub 30 m away. "Shares" is the
    // 0.3.0 model (votes normalised, the two biggest areas kept, no street baseline), rebuilt here from the same
    // votes; "presences" is the additive model in Core now.
    var places = new[]
    {
        new NearbyPlace("ba:buildingtype_office", null, 20), new NearbyPlace("ba:buildingtype_office", null, 40),
        new NearbyPlace("ba:buildingtype_retail", "ba:businesstype_nightclub", 30)
    };
    var settings = tables.Classifier;
    var votes = new Dictionary<string, double>(StringComparer.Ordinal);
    foreach (var pl in places)
    {
        IReadOnlyDictionary<string, double>? rule = null;
        if (pl.BusinessType != null) settings.BusinessTypes.TryGetValue(pl.BusinessType, out rule);
        if (rule == null) settings.BuildingTypes.TryGetValue(pl.BuildingType, out rule);
        if (rule == null) continue;
        var f = 1.0 - (1.0 - settings.EdgeWeight) * pl.DistanceMetres / settings.RadiusMetres;
        foreach (var r in rule) votes[r.Key] = votes.GetValueOrDefault(r.Key) + r.Value * f;
    }
    var top = votes.OrderByDescending(v => v.Value).Take(2).ToList();
    var shares = AreaMix.FromPresences(top.Select(v => new KeyValuePair<string, double>(v.Key, v.Value / top.Sum(t => t.Value))));
    var presences = new AreaClassifier(tables).FromPlaces(places);
    var oldTables = LoadTables(opts, zeroBaseline: true);
    var oldCart = new Cart(oldTables, prices, new[] { HotDog, Soda }, "hot dog grill + cooler");
    var newCart = carts["hotdog+cooler"].Cart;
    o.AppendLine("## The in-game case: two offices at 20 and 40 m, a nightclub at 30 m, hot dog grill + cooler, weekday, ceiling price");
    o.AppendLine();
    o.AppendLine($"Votes: {string.Join(", ", votes.Select(v => $"{v.Key} {v.Value:0.00}"))}. Shares (old model on the current data): {shares}. Presences (now): {presences}.");
    o.AppendLine();
    o.AppendLine("| District | Hour | Model | Passers/h | Customers/h | Served/h | Profit/h |");
    o.AppendLine("|---|---|---|---|---|---|---|");
    foreach (var hood in new[] { "ba:neighborhood_midtown", "ba:neighborhood_murrayhill" })
    foreach (var (label, from) in new[] { ("lunch 12:00", 720), ("evening 19:00", 1140), ("late 22:00", 1320) })
    foreach (var (model, cart, mix) in new[] { ("shares (old model, current data)", oldCart, shares), ("presences, now", newCart, presences) })
    {
        var spot = new Spot("in-game case", mix, hood, "", 0, 0);
        var r = Selling.Run(cart, spot, from, from + 60, conditions[0], Selling.Unlimited(cart));
        var t = r.ByDaypart.Values.First();
        o.AppendLine($"| {H(hood)} | {label} | {model} | {t.PerHour(t.Passers):0} | {t.PerHour(t.Demand):0.0} | {t.PerHour(t.Served):0.0} | ${Money(t.PerHour(t.Profit))} |");
    }
    o.AppendLine();
    // How a cart does at an ordinary spot: hot dog grill + cooler customers per hour at weekday lunch (12:00) and evening
    // (19:00) over every real pitch, by district: the median and the quarter mark.
    o.AppendLine("## Typical spots: hot dog grill + cooler, weekday, ceiling price, customers per hour (served)");
    o.AppendLine();
    o.AppendLine("| District | Lunch 12:00: p25 / median / p90 | Evening 19:00: p25 / median / p90 | Lunch passers/h, median |");
    o.AppendLine("|---|---|---|---|");
    foreach (var hood in hoods.Append("city"))
    {
        var pool = spots.Where(s => !s.Name.Contains("archetype") && (hood == "city" || s.Hood == hood)).ToList();
        (double P25, double Med, double P90, double Passers) At(int minute)
        {
            var served = new List<double>();
            var passers = new List<double>();
            foreach (var sp in pool)
            {
                var r = Selling.Run(newCart, sp, minute, minute + 60, conditions[0], Selling.Unlimited(newCart));
                var t = r.ByDaypart.Values.First();
                served.Add(t.PerHour(t.Served));
                passers.Add(t.PerHour(t.Passers));
            }
            served.Sort(); passers.Sort();
            double Q(List<double> v, double q) => v[(int)Math.Min(v.Count - 1, Math.Floor(q * v.Count))];
            return (Q(served, .25), Q(served, .5), Q(served, .9), Q(passers, .5));
        }
        var l = At(720);
        var e = At(1140);
        o.AppendLine($"| {H(hood)} | {l.P25:0.0} / {l.Med:0.0} / {l.P90:0.0} | {e.P25:0.0} / {e.Med:0.0} / {e.P90:0.0} | {l.Passers:0} |");
    }
    o.AppendLine();
    // A pure office block (eight office buildings around the pitch) for comparison with the in-game case.
    var block = new AreaClassifier(tables).FromPlaces(Enumerable.Range(0, 8).Select(i => new NearbyPlace("ba:buildingtype_office", null, 5 + i * 6)));
    foreach (var (label, from) in new[] { ("lunch 12:00", 720), ("evening 19:00", 1140) })
    {
        var spot = new Spot("office block", block, "ba:neighborhood_midtown", "", 0, 0);
        var r = Selling.Run(newCart, spot, from, from + 60, conditions[0], Selling.Unlimited(newCart));
        var t = r.ByDaypart.Values.First();
        o.AppendLine($"Pure office block (eight offices within 50 m, {block}), Midtown {label}: {t.PerHour(t.Passers):0} passers/h, {t.PerHour(t.Demand):0.0} customers/h, {t.PerHour(t.Served):0.0} served/h.");
        o.AppendLine();
    }
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
static DemandTables LoadTables(Options opts, bool zeroBaseline = false)
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
    // The share model had no street baseline: zero it to rebuild the old footfall.
    if (zeroBaseline && demand["streetBaseline"] is JObject baseline)
        foreach (var p in baseline.Properties().ToList()) p.Value = 1e-9; // the data must stay above 0
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
    // --service queue (default): the game's queue since 0.4.0. --service fluid: the old min(buyers, capacity).
    public bool Queue = true;
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
                case "--service": o.Queue = Next() != "fluid"; break;
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
