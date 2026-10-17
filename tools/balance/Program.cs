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
var rules = RuleTables.Parse(File.ReadAllText(Path.Combine(opts.Data, "rules.json")));
Selling.Queue = opts.Queue ? new QueueThroughput(tables, rules) : null;
var prices = GamePrices.Load(opts.Prices);
if (opts.Patch != null && JObject.Parse(File.ReadAllText(opts.Patch))["game"] is JObject gamePatch) prices.Merge(gamePatch);
var city = City.Load(opts.City);
var travel = new Travel { MetresPerMinute = opts.MetresPerSecond };
// The player drives a truck: a real second is a game minute, so metres a second are metres a game minute. Buying and
// carrying boxes for four pieces takes longer than for a cart.
var drive = new Travel { MetresPerMinute = opts.DriveSpeed, ShopMinutes = 15 };
var spots = city.Spots(tables);
// Pitches the city's buildings can't produce, as archetype spots: a park (the game's park areas and zones give it;
// city.json has no parks), the waterfront (zones only) and, when the data has a "transit" area, a subway entrance.
// Each sits at the middle of its neighbourhood's buildings, a stand-in for a real location.
// A park pitch's presence is what the game's park area gives a spot inside it (classifier parkarea at 0 m), not 1.
var parkMix = tables.HasArea("park")
    ? new AreaClassifier(tables).FromPlaces(new[] { new NearbyPlace("parkarea", null, 0) }) is { Presences.Count: > 0 } inside ? inside : AreaMix.Single("park")
    : AreaMix.Single("waterfront");
foreach (var g in city.Buildings.GroupBy(b => (b.Hood, b.Region)))
{
    var x = g.Average(b => b.X);
    var z = g.Average(b => b.Z);
    if (opts.Parks) spots.Add(new Spot("park (archetype)", parkMix, g.Key.Hood, g.Key.Region, x, z));
    if (opts.Parks && tables.HasArea("park")) spots.Add(new Spot("waterfront (archetype)", AreaMix.Single("waterfront"), g.Key.Hood, g.Key.Region, x - 40, z));
    if (opts.Parks && tables.HasArea("transit")) spots.Add(new Spot("subway entrance (archetype)", AreaMix.Single("transit"), g.Key.Hood, g.Key.Region, x, z + 40));
}
Conditions Cond(DayOfWeek day = DayOfWeek.Wednesday, bool rain = false, Pricing? pricing = null, double? factor = null) =>
    new() { Day = day, Rain = rain, Pricing = pricing ?? opts.Pricing, PriceFactor = factor ?? opts.PriceFactor, MarketDemand = opts.Market };
var conditions = new[] { Cond(), Cond(rain: true), Cond(DayOfWeek.Saturday), Cond(DayOfWeek.Saturday, true) };

const string HotDog = "ba:itemname_hotdog", Soda = "ba:itemname_sodacan", Coffee = "ba:itemname_cupofcoffee", Croissant = "ba:itemname_croissant";
Cart MakeCart(string[] menu, string name, bool storage = false, int? staff = null, string kind = Cart.Kind, int? servers = null, double speed = 1.0) =>
    new(tables, prices, menu, name, storage, staff, kind, servers, speed);
var carts = new Dictionary<string, (Cart Cart, string[] Equipment)>
{
    ["hotdog"] = (MakeCart(new[] { HotDog }, "hot dog grill"), new[] { "hotdoggrill" }),
    ["hotdog+cooler"] = (MakeCart(new[] { HotDog, Soda }, "hot dog grill + cooler"), new[] { "hotdoggrill", "cooler" }),
    ["hotdog+coffee"] = (MakeCart(new[] { HotDog, Coffee }, "hot dog grill + coffee machine"), new[] { "hotdoggrill", "coffeemachine" }),
    ["coffee+bakery"] = (MakeCart(new[] { Coffee, Croissant }, "coffee machine + display case (croissants)"), new[] { "coffeemachine", "displaycase" }),
    // A truck stand-in: the truck's storage per piece, its staff (economy.json, 2) at the window, which is the player
    // and one hire, and a grill, coffee machine, display case and cooler (four slots).
    ["truck-like"] = (MakeCart(new[] { HotDog, Coffee, Croissant, Soda }, "truck stand-in: 2 at the window, 4 pieces",
        false, null, "truck"), new[] { "hotdoggrill", "coffeemachine", "displaycase", "cooler" }),
};
// The new street foods (new-foods-proposal.json), and the vanilla ice cream counter for comparison. A cart is
// added only when the data and prices know all its items, so runs without the patch are unchanged.
const string Dumplings = "foodtrucks:itemname_dumplings", Kabob = "ba:itemname_kabob", Tacos = "foodtrucks:itemname_tacos",
    ChoppedCheese = "foodtrucks:itemname_choppedcheese", Corn = "foodtrucks:itemname_grilledcorn", Popcorn = "ba:itemname_popcorn",
    CottonCandy = "ba:itemname_cottoncandy", Slushie = "ba:itemname_slushi", IceCream = "ba:itemname_icecream", Burger = "ba:itemname_burger";
foreach (var (key, menu, name, equipment, truck) in new (string, string[], string, string[], bool)[]
         {
             ("icecream", new[] { IceCream }, "ice cream counter", new[] { "icecreamcounter" }, false),
             ("dumplings", new[] { Dumplings }, "dumpling steamer", new[] { "dumplingsteamer" }, false),
             ("dumplings+cooler", new[] { Dumplings, Soda }, "dumpling steamer + cooler", new[] { "dumplingsteamer", "cooler" }, false),
             ("kabob", new[] { Kabob }, "grill, kabobs only", new[] { "grill" }, false),
             ("kabob+cooler", new[] { Kabob, Soda }, "grill (kabobs) + cooler", new[] { "grill", "cooler" }, false),
             ("burger+kabob", new[] { Burger, Kabob }, "grill (burgers, kabobs)", new[] { "grill" }, false),
             ("burger+kabob+cooler", new[] { Burger, Kabob, Soda }, "grill (burgers, kabobs) + cooler", new[] { "grill", "cooler" }, false),
             ("hotdog+kabob", new[] { HotDog, Kabob }, "hot dog grill + grill (kabobs)", new[] { "hotdoggrill", "grill" }, false),
             ("griddle", new[] { Tacos, ChoppedCheese }, "griddle (tacos, chopped cheese)", new[] { "griddle" }, false),
             ("griddle+cooler", new[] { Tacos, ChoppedCheese, Soda }, "griddle + cooler", new[] { "griddle", "cooler" }, false),
             ("griddle+slushie", new[] { Tacos, ChoppedCheese, Slushie }, "griddle + slushie machine", new[] { "griddle", "slushiemachine" }, false),
             ("corn", new[] { Corn }, "corn grill", new[] { "corngrill" }, false),
             ("concessions", new[] { Popcorn, CottonCandy }, "concessions case (popcorn, cotton candy)", new[] { "concessions" }, false),
             ("popcorn", new[] { Popcorn }, "concessions case, popcorn only", new[] { "concessions" }, false),
             ("slushie", new[] { Slushie }, "slushie machine", new[] { "slushiemachine" }, false),
             ("concessions+slushie", new[] { Popcorn, CottonCandy, Slushie }, "concessions case + slushie machine", new[] { "concessions", "slushiemachine" }, false),
             ("corn+slushie", new[] { Corn, Slushie }, "corn grill + slushie machine", new[] { "corngrill", "slushiemachine" }, false),
             ("icecream+slushie", new[] { IceCream, Slushie }, "ice cream counter + slushie machine", new[] { "icecreamcounter", "slushiemachine" }, false),
             ("truck-street", new[] { Kabob, Tacos, ChoppedCheese, Slushie, Soda }, "truck stand-in: grill (kabobs), griddle, slushie machine, cooler",
                 new[] { "grill", "griddle", "slushiemachine", "cooler" }, true),
         })
{
    if (!menu.All(i => tables.Product(i) != null && prices.Items.ContainsKey(i)) || !equipment.All(prices.Equipment.ContainsKey)) continue;
    carts[key] = truck
        ? (MakeCart(menu, name, false, null, "truck"), equipment)
        : (MakeCart(menu, name), equipment);
}
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

// Carts without their own day take the hot dog cart's (09:00 to 01:00), trucks the truck stand-in's.
(int From, int To, (string Name, (int, int)[] Windows)[] Splits) DayOf(string key) =>
    days.TryGetValue(key, out var d) ? d : carts[key].Cart.VehicleKind == "truck" ? days["truck-like"] : days["hotdog+cooler"];

var hoods = new[] { "ba:neighborhood_midtown", "ba:neighborhood_lowermanhattan", "ba:neighborhood_murrayhill", "ba:neighborhood_hellskitchen",
    "ba:neighborhood_garmentdistrict", "ba:neighborhood_industrycity", "ba:neighborhood_thehamptons" };
var areas = new[] { "office", "residential", "nightlife", "industrial", "retail", "park", "waterfront", "transit" }.Where(tables.HasArea).ToArray();
var dayparts = new[] { Daypart.Early, Daypart.Morning, Daypart.Lunch, Daypart.Afternoon, Daypart.Evening, Daypart.Late };
string H(string hood) => hood.Replace("ba:neighborhood_", "");
string Clock(int m) => $"{m / 60 % 24:00}:{m % 60:00}";
string Money(double v) => v.ToString("#,0", CultureInfo.InvariantCulture);

var o = new StringBuilder();
o.AppendLine($"# Cart balance run");
o.AppendLine();
o.AppendLine($"Data: {opts.Data}{(opts.Patch != null ? $" patched with {opts.Patch}" : "")}. Pricing: {opts.Pricing}{(opts.PriceFactor != 1 ? $" × {opts.PriceFactor}" : "")}. Market demand: {opts.Market}. " +
             $"Restock: {opts.Restock}. Pushing speed: {opts.MetresPerSecond} m/s ({opts.MetresPerSecond * 60:0} m per in-game hour); trucks drive at {opts.DriveSpeed} m/s. " +
             $"Cart: {tables.Vehicle("cart")!.UnitsPerPiece} units a piece, {tables.Vehicle("cart")!.Staff} staff; truck {tables.Vehicle("truck")!.UnitsPerPiece} a piece; " +
             $"Extra Storage doubles them. passersPerHour {tables.PassersPerHour}. Month: May.");
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

Planner PlannerFor(Cart cart) => new(cart, city, cart.VehicleKind == "truck" ? drive : travel) { Mode = opts.Restock };

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

// Premium spots and events (data/pitches.json): what the bonus adds at each place, against an ordinary day there,
// for the hot dog cart and the truck stand-in. Events are worth their fee when the gain over their hours beats it.
if (opts.Wants("pitches"))
{
    var pitchTables = FoodTrucks.Core.Pitches.PitchTables.Parse(File.ReadAllText(Path.Combine(opts.Data, "pitches.json")), tables);
    // city.json names a spot "2 fifthstreet" for the key ba:street_fifthstreet#2.
    Spot? At(string address)
    {
        var parts = address.Replace("ba:street_", "").Split('#');
        return spots.FirstOrDefault(s => s.Name == $"{parts[1]} {parts[0]}");
    }
    o.AppendLine("## Premium spots: a day with the spot held against the same pitch without it");
    o.AppendLine();
    o.AppendLine("| Spot | Hot dog weekday | with spot | Hot dog Saturday | with spot | Truck weekday | with spot |");
    o.AppendLine("|---|---|---|---|---|---|---|");
    var hot = PlannerFor(carts["hotdog+cooler"].Cart);
    var truck = PlannerFor(carts["truck-like"].Cart);
    var (hFrom, hTo, _) = days["hotdog+cooler"];
    var (tFrom, tTo, _) = days["truck-like"];
    foreach (var premium in pitchTables.Spots)
    {
        var spot = At(premium.Address);
        if (spot == null) { o.AppendLine($"| {premium.Id} | not in city.json | | | | | |"); continue; }
        var bonus = FoodTrucks.Core.Pitches.PitchBonus.Of(premium);
        Conditions With(Conditions c) => new() { Day = c.Day, Rain = c.Rain, Pricing = c.Pricing, PriceFactor = c.PriceFactor, MarketDemand = c.MarketDemand, Pitch = bonus };
        double P(Planner p, int f, int t, Conditions c) => p.SingleSpot(spot, f, t, c).Total.Profit;
        o.AppendLine($"| {premium.Id} | {Money(P(hot, hFrom, hTo, conditions[0]))} | {Money(P(hot, hFrom, hTo, With(conditions[0])))} | " +
                     $"{Money(P(hot, hFrom, hTo, conditions[2]))} | {Money(P(hot, hFrom, hTo, With(conditions[2])))} | " +
                     $"{Money(P(truck, tFrom, tTo, conditions[0]))} | {Money(P(truck, tFrom, tTo, With(conditions[0])))} |");
    }
    o.AppendLine();
    o.AppendLine("## Events: profit over the event's hours with a booked place against the same hours without the event");
    o.AppendLine();
    o.AppendLine("| Place | Event | Fee | Hot dog without | with | gain less fee | Truck without | with | gain less fee |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|");
    foreach (var place in pitchTables.Places)
    {
        var spot = At(place.Address);
        if (spot == null) { o.AppendLine($"| {place.Id} | not in city.json | | | | | | | |"); continue; }
        foreach (var kindId in place.Kinds)
        {
            var kind = pitchTables.Kind(kindId)!;
            var day = kind.Days.Contains(DayOfWeek.Saturday) ? DayOfWeek.Saturday : kind.Days[0];
            var plain = Cond(day);
            var with = new Conditions { Day = day, Pricing = plain.Pricing, PriceFactor = plain.PriceFactor, MarketDemand = plain.MarketDemand,
                Pitch = FoodTrucks.Core.Pitches.PitchBonus.Of(kind), PitchFrom = kind.Start, PitchTo = kind.End };
            var fee = kind.FeeCents / 100.0;
            var h0 = hot.SingleSpot(spot, kind.Start, kind.End, plain).Total.Profit;
            var h1 = hot.SingleSpot(spot, kind.Start, kind.End, with).Total.Profit;
            var t0 = truck.SingleSpot(spot, kind.Start, kind.End, plain).Total.Profit;
            var t1 = truck.SingleSpot(spot, kind.Start, kind.End, with).Total.Profit;
            o.AppendLine($"| {place.Id} | {kind.Id} | {Money(fee)} | {Money(h0)} | {Money(h1)} | {Money(h1 - h0 - fee)} | {Money(t0)} | {Money(t1)} | {Money(t1 - t0 - fee)} |");
        }
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
        var (dFrom, dTo, splits) = DayOf(key);
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

// The new foods against the accepted carts: each cart's best route city-wide (the compare report's search,
// city pool only) under several conditions, its cost to start, and its best park day in summer.
if (opts.Wants("foods"))
{
    var keys = (opts.FoodCarts ?? carts.Keys.ToArray()).Where(carts.ContainsKey).ToArray();
    Conditions At(DayOfWeek day, int month, Pricing? pricing = null) => new()
        { Day = day, Month = month, Pricing = pricing ?? opts.Pricing, PriceFactor = pricing == null ? opts.PriceFactor : 1, MarketDemand = opts.Market };
    var cases = new (string Label, Conditions C)[]
    {
        ("Weekday May", At(DayOfWeek.Wednesday, 5)), ("Saturday May", At(DayOfWeek.Saturday, 5)),
        ("Weekday July", At(DayOfWeek.Wednesday, 7)), ("Saturday July", At(DayOfWeek.Saturday, 7)),
        ("Weekday January", At(DayOfWeek.Wednesday, 1)), ("Weekday May, default price", At(DayOfWeek.Wednesday, 5, Pricing.Default)),
    };
    var parks = spots.Where(s => s.Name.StartsWith("park")).ToList();
    var ParkArea = tables.HasArea("park") ? "park" : "waterfront";
    var rows = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
    var routeRows = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
    var rateRows = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
    Parallel.ForEach(keys, key =>
    {
        var (cart, equipment) = carts[key];
        var planner = PlannerFor(cart);
        var (dFrom, dTo, splits) = DayOf(key);
        DayResult Best(Conditions c)
        {
            var stays = spots.Select(s => planner.SingleSpot(s, dFrom, dTo, c)).OrderByDescending(d => d.Total.Profit).ToList();
            var best = stays[0];
            var stayers = stays.Take(5).Select(d => d.Stays[0].Spot).ToList();
            foreach (var split in splits)
            {
                var r = planner.BestRoute(spots, split.Windows, c, 20, stayers);
                if (r != null && r.Total.Profit > best.Total.Profit) best = r;
            }
            return best;
        }
        var results = cases.Select(x => Best(x.C)).ToList();
        var park = parks.Count == 0 ? null : parks.Select(s => planner.SingleSpot(s, dFrom, dTo, cases[3].C)).OrderByDescending(d => d.Total.Profit).First();
        var eq = equipment.Sum(e => prices.Equipment[e]);
        var load = Selling.Load(cart, new Spot("office", AreaMix.Single("office"), "ba:neighborhood_midtown", "", 0, 0), dFrom, dFrom + 300, cases[0].C);
        var start = (cart.VehicleKind == "truck" ? 0 : opts.CartPrice) + eq + load.Sum(p => p.Value * prices.Items[p.Key].Wholesale);
        rows[key] = $"| {cart.Name} | {Money(start)} | {string.Join(" | ", results.Select(r => Money(r.Total.Profit)))} | " +
                    $"{(park == null ? "-" : $"{Money(park.Total.Profit)} ({H(park.Stays[0].Spot.Hood)})")} |";
        string Rate(string area, string hood, int from, int to, Conditions c)
        {
            var r = Selling.Run(cart, new Spot(area, AreaMix.Single(area), hood, "", 0, 0), from, to, c, Selling.Unlimited(cart));
            var t = r.ByDaypart.Values.First();
            return $"{t.PerHour(t.Served):0} / {Money(t.PerHour(t.Profit))}";
        }
        rateRows[key] = $"| {cart.Name} | {Rate("office", "ba:neighborhood_midtown", 660, 870, cases[0].C)} | {Rate("residential", "ba:neighborhood_hellskitchen", 1020, 1260, cases[0].C)} | " +
                        $"{Rate("nightlife", "ba:neighborhood_hellskitchen", 1260, 1500, cases[1].C)} | {Rate(ParkArea, "ba:neighborhood_midtown", 870, 1020, cases[0].C)} | " +
                        $"{Rate(ParkArea, "ba:neighborhood_midtown", 870, 1020, cases[3].C)} | {Rate(ParkArea, "ba:neighborhood_midtown", 870, 1020, cases[4].C)} |";
        var w = results[0];
        routeRows[key] = $"| {cart.Name} | {string.Join(" → ", w.Stays.Select(x => $"{x.Spot.Name} ({H(x.Spot.Hood)}, {x.Spot.Areas}) {Clock(x.From)}"))} | " +
                      $"{w.Restocks} | {w.Total.Units:0} | {w.Total.Customers:0} | {string.Join(", ", w.Total.Sold.OrderByDescending(p => p.Value).Select(p => $"{p.Key.Split('_').Last()} {p.Value:0}"))} |";
    });
    o.AppendLine("## New foods: best day city-wide, ceiling price unless noted, dry");
    o.AppendLine();
    o.AppendLine("Best of staying at one spot and the route splits of the compare report, over the whole city. Cost to start: cart (not for the truck stand-in) + equipment + the first load at wholesale.");
    o.AppendLine();
    o.AppendLine("| Cart | Cost to start | " + string.Join(" | ", cases.Select(x => x.Label)) + " | Best park all day, Saturday July |");
    o.AppendLine("|---|---|" + string.Concat(cases.Select(_ => "---|")) + "---|");
    foreach (var key in keys) o.AppendLine(rows[key]);
    o.AppendLine();
    o.AppendLine("## New foods: the weekday May best day");
    o.AppendLine();
    o.AppendLine("| Cart | Pitches | Restocks | Units | Customers | Sold |");
    o.AppendLine("|---|---|---|---|---|---|");
    foreach (var key in keys) o.AppendLine(routeRows[key]);
    o.AppendLine();
    o.AppendLine("## New foods: pure spots, served per hour / profit per hour, unlimited stock");
    o.AppendLine();
    o.AppendLine("| Cart | Midtown office lunch, weekday May | Hell's Kitchen residential evening, weekday May | Hell's Kitchen nightlife late, Saturday May | Midtown park afternoon, weekday May | same, Saturday July | same, weekday January |");
    o.AppendLine("|---|---|---|---|---|---|---|");
    foreach (var key in keys) o.AppendLine(rateRows[key]);
    o.AppendLine();
}

// Storage per piece (Peter, 10 October 2026): what a piece's storage does to a day. Each vehicle at its best spot
// city-wide on a dry weekday in May, from the morning full: when the first item runs out, when everything has,
// how many restocks the day takes, and the day's profit with restock trips and with restocking in place.
if (opts.Wants("storage"))
{
    var cases = new (string Label, string[] Menu, string[] Equipment, string Kind, bool Storage)[]
    {
        ("hot dog grill", new[] { HotDog }, new[] { "hotdoggrill" }, "cart", false),
        ("hot dog grill + Extra Storage", new[] { HotDog }, new[] { "hotdoggrill", "storage" }, "cart", true),
        ("hot dog grill + cooler", new[] { HotDog, Soda }, new[] { "hotdoggrill", "cooler" }, "cart", false),
        ("coffee machine + display case", new[] { Coffee, Croissant }, new[] { "coffeemachine", "displaycase" }, "cart", false),
        ("coffee machine + Extra Storage", new[] { Coffee }, new[] { "coffeemachine", "storage" }, "cart", true),
        ("grill (burgers, kabobs)", new[] { Burger, Kabob }, new[] { "grill" }, "cart", false),
        ("grill (burgers, kabobs) + Extra Storage", new[] { Burger, Kabob }, new[] { "grill", "storage" }, "cart", true),
        ("truck: hot dog, coffee, display case, cooler", new[] { HotDog, Coffee, Croissant, Soda }, new[] { "hotdoggrill", "coffeemachine", "displaycase", "cooler" }, "truck", false),
        ("truck: hot dog, coffee, display case + Extra Storage", new[] { HotDog, Coffee, Croissant }, new[] { "hotdoggrill", "coffeemachine", "displaycase", "storage" }, "truck", true),
    };
    var c = Cond();
    o.AppendLine("## Storage per piece: a day at the best spot (dry weekday, May)");
    o.AppendLine();
    o.AppendLine($"Cart {tables.Vehicle("cart")!.UnitsPerPiece} units a piece, truck {tables.Vehicle("truck")!.UnitsPerPiece}; Extra Storage doubles every piece. " +
                 "Full at the day's start. First out: the first item to run out; all out: nothing left. Restocks: trips the day takes " +
                 $"({(opts.Restock == RestockMode.Wholesaler ? "to the nearest wholesaler and back, a stand-in for the commissary" : "in place")}). " +
                 "Profit in place: the same day if a restock took no walk.");
    o.AppendLine();
    o.AppendLine("| Vehicle | Holds | Best spot | Units a day | First out | All out | Restocks | Profit | Profit in place |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|");
    var results = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
    var profits = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
    Parallel.For(0, cases.Length, n =>
    {
        var (label, menu, equipment, kind, storage) = cases[n];
        if (!menu.All(i => tables.Product(i) != null && prices.Items.ContainsKey(i))) return;
        var cart = MakeCart(menu, label, storage, null, kind);
        // The day of the cart it is most like: coffee carts open at 06:00, others at 09:00; trucks 07:00 to 23:00.
        var (dFrom, dTo, _) = kind == "truck" ? days["truck-like"] : menu.Contains(Coffee) ? days["coffee+bakery"] : days["hotdog+cooler"];
        var planner = PlannerFor(cart);
        var best = spots.Select(s => (s, d: planner.SingleSpot(s, dFrom, dTo, c))).OrderByDescending(x => x.d.Total.Profit).First();
        var first = Selling.Run(cart, best.s, dFrom, dTo, c, Selling.Load(cart, best.s, dFrom, dTo, c));
        var inPlace = new Planner(cart, city, kind == "truck" ? drive : travel) { Mode = RestockMode.BoxInHand }.SingleSpot(best.s, dFrom, dTo, c);
        string At(int? m) => m == null ? "never" : $"{Clock(m.Value)} ({(m.Value - dFrom) / 60.0:0.0} h)";
        var holds = string.Join(", ", menu.GroupBy(i => cart.PieceOf[i]).Select(g => $"{cart.PerPiece} {g.Key}"));
        results[n] = $"| {label} | {holds} | {best.s.Name} ({H(best.s.Hood)}) | {best.d.Total.Units:0} | {At(first.FirstOutAt)} | {At(first.SoldOutAt)} | " +
                     $"{best.d.Restocks} | {Money(best.d.Total.Profit)} | {Money(inPlace.Total.Profit)} |";
        profits[label] = best.d.Total.Profit;
    });
    foreach (var row in results.OrderBy(r => r.Key)) o.AppendLine(row.Value);
    o.AppendLine();
    var price = prices.Equipment.TryGetValue("storage", out var p) ? p : 900;
    o.AppendLine($"Extra Storage at ${Money(price)}: days to pay back from the extra profit a day at the best spot.");
    o.AppendLine();
    foreach (var (with, without) in new[]
             {
                 ("hot dog grill + Extra Storage", "hot dog grill"), ("hot dog grill + Extra Storage", "hot dog grill + cooler"),
                 ("coffee machine + Extra Storage", "coffee machine + display case"),
                 ("grill (burgers, kabobs) + Extra Storage", "grill (burgers, kabobs)"),
                 ("truck: hot dog, coffee, display case + Extra Storage", "truck: hot dog, coffee, display case, cooler"),
             })
    {
        if (!profits.TryGetValue(with, out var a) || !profits.TryGetValue(without, out var b)) continue;
        var gain = a - b;
        o.AppendLine($"- {with} against {without}: {(gain >= 0 ? "+" : "")}{Money(gain)} a day" + (gain > 0 ? $", paid back in {price / gain:0.0} days." : "."));
    }
    o.AppendLine();
}

// The balance pass of 14 October 2026 (docs/balance/balance-pass-2026-10-14.md): what carts and trucks earn a day and a
// week, played and crewed, at typical, good, best and premium pitches; whether storage caps the day; how far the
// commissary's weekly phone limit goes; the delivery fee against hauling; crew wages against takings; parks.
if (opts.Wants("pass"))
{
    var cr = rules.Commissary;
    double RentDay(string kind) => cr.DailyRentCents(kind) / 100.0;
    // EmployeeHelper.CalculateHourlyWageForSkill: customer service's base $16 × (skill^1.05 / 100 + 1) × the wage
    // multiplier (1.0 at the default difficulty) [code, BC data].
    double Wage(double skill) => 16 * (Math.Pow(skill, 1.05) / 100 + 1);
    var crewSpeed = rules.Service.Speed(FoodTrucks.Core.Rules.ServiceRules.EffectiveSkill(opts.CrewSkill, opts.CrewSatisfaction));
    var wage = Wage(opts.CrewSkill);
    // The delivery fee: one flat fee per delivery at a commissary (rules.json deliveryFeeCents), whatever it brings and
    // for however many vehicles. A vehicle's share a day: the fee × deliveries a week ÷ 7 ÷ the vehicles sharing them.
    // One delivery a week covers a vehicle's week (10 boxes hold 5,000 units; a busy truck sells under 3,000 of all
    // its foods), so the default is one a week for one vehicle.
    var feeDay = cr.DeliveryFeeCents / 100.0 * opts.DeliveriesPerWeek / 7 / opts.FeeSharing;
    var commissaries = city.Commissaries(Path.Combine(opts.Data, "commissaries.json"));
    var truckMenu = new[] { HotDog, Coffee, Croissant, Soda };
    int CeilHalf(int minute) => (minute + 29) / 30 * 30;

    // What the game pays a hire for (Core CrewPay). A shift hour: every whole hour of the hire's shifts on the week plan
    // (HireCrew.CountHour, WeekPlan.OnShift), worked or idle; a crew's shifts are Core's auto schedule for its route
    // (Route below). An open hour of a window the player opened: the hire serves at some minute of its first half
    // (CrewPay.CloseHours); during a restock trip the window is shut.
    int OpenHours(Stay stay)
    {
        var hours = 0;
        for (var h = stay.From / 60; h * 60 < stay.To; h++)
        {
            var first = Math.Max(stay.From, h * 60);
            var last = Math.Min(stay.To, h * 60 + 30);
            var served = false;
            for (var m = first; m < last && !served; m++)
                served = !stay.Trips.Any(t => m >= t.From && m < t.To);
            if (served) hours++;
        }
        return hours;
    }

    // One day of a setup at a pitch: gross (sales less goods at wholesale), goods, hire-hours paid, units sold, and for
    // the storage question when the first piece ran out and the share of unlimited-stock sales. Null: not feasible
    // (a crew needs a commissary in the pitch's region to load at).

    (int From, int To) cartDay = (540, 1500), coffeeDay = (360, 1320), truckDay = (420, 1380);
    var setups = new List<(string Label, Cart Cart, int Hires, bool Commissary, Func<Spot, Conditions, DayEval?> Day)>();

    Func<Spot, Conditions, DayEval?> Played(Cart cart, Travel tr, (int From, int To) day, int hires) => (spot, c) =>
    {
        var planner = new Planner(cart, city, tr);
        var d = planner.SingleSpot(spot, day.From, day.To, c);
        var first = Selling.Run(cart, spot, day.From, day.To, c, Selling.Load(cart, spot, day.From, day.To, c));
        var unlimited = Selling.Run(cart, spot, day.From, day.To, c, Selling.Unlimited(cart));
        return new DayEval(d.Total.Profit, d.Total.Cost, hires * d.Stays.Sum(OpenHours), new Dictionary<string, double>(d.Total.Sold),
            first.FirstOutAt, unlimited.Units > 0 ? d.Total.Units / unlimited.Units : 1, $"{d.Restocks} restocks");
    };

    // A crew's route, built and checked by Core (HireSchedules.Validate, PlanDay), as the game runs it. The vehicle
    // restocks only in the bay of the commissary where it rents (Core CrewRestock). Since 16 October 2026 every route day
    // ends with the drive back to that bay (PlanDay's home leg, HireSchedules.HomeIndex), unless its last stop already is
    // the bay; the drive back is paid like the other drives. The forms: a bay stop in the evening after the pitch, or one
    // in the morning before it (the night is then spent in the bay too, after the drive back). With `breaks` the evening form also gets a midday bay stop. A crew that sells out stays sold out
    // until a bay stop comes; it may drive there with nothing on board (HireRunner.Step, VehicleWhere.BayStops). Each
    // form that validates is simulated on a steady-state day (planned from where the day before ended, no earlier than
    // its last leg) and the best net is kept; the note says which, and which forms Core refused. Sales at bay stops
    // are left out. The crew's shifts are Core's WeekPlan.AutoSchedule for the route (at most 12 hours a shift, one a day,
    // the vehicle's staff at most: a cart's crew is 1), the plan follows them (PlanDay with their coverage: no drive or
    // opening without someone on shift), the window serves with the crew on shift each hour, and each hire is paid their
    // shift hours on Wednesday (Tuesday's night past midnight included). The stops run on Tuesday and Wednesday (the week
    // plan's stops are per weekday); Wednesday is the day measured.
    var hireSchedules = new FoodTrucks.Core.Fleet.HireSchedules(rules.Fleet);
    int FloorHalf(int minute) => minute / 30 * 30;
    Func<Spot, Conditions, DayEval?> Route(Cart cart, (int From, int To) day, int hires, FoodTrucks.Core.Fleet.TravelMode mode, int[]? breaks = null) => (spot, c) =>
    {
        var bay = commissaries.Where(b => b.Region == spot.Region)
            .OrderBy(b => Math.Sqrt((b.X - spot.X) * (b.X - spot.X) + (b.Z - spot.Z) * (b.Z - spot.Z))).FirstOrDefault();
        if (bay == null) return null;
        var metres = Math.Sqrt((bay.X - spot.X) * (bay.X - spot.X) + (bay.Z - spot.Z) * (bay.Z - spot.Z));
        var t = FoodTrucks.Core.Fleet.Travel.Minutes(rules.Fleet, metres, mode);
        FoodTrucks.Core.Saves.ScheduleStop P(string key, int open, int close) => new() { Day = DayOfWeek.Tuesday, SpotKey = key, X = spot.X, Z = spot.Z, OpenAt = open, CloseAt = close };
        FoodTrucks.Core.Saves.ScheduleStop B(string key, int open) => new() { Day = DayOfWeek.Tuesday, SpotKey = key, X = bay.X, Z = bay.Z, OpenAt = open, CloseAt = open + 30 };
        var forms = new List<(string Name, List<FoodTrucks.Core.Saves.ScheduleStop> Stops, bool NightAtPitch)>();
        var close = Math.Min(day.To, FloorHalf(1410 - t));
        var shortened = close < day.To ? $", pitch shortened to {Clock(close)}" : "";
        forms.Add(($"pitch, bay{shortened}", new() { P("pitch", day.From, close), B("bay", CeilHalf(close + t)) }, false));
        var morningBay = FloorHalf(day.From - t) - 30;
        forms.Add(("bay, pitch, back to the bay", new() { B("bay", morningBay), P("pitch", day.From, day.To) }, false));
        foreach (var b in breaks ?? Array.Empty<int>())
        {
            var bayOpen = CeilHalf(b + t);
            var back = CeilHalf(bayOpen + 30 + t);
            forms.Add(($"pitch, bay {Clock(bayOpen)}, pitch from {Clock(back)}, bay{shortened}",
                new() { P("pitch", day.From, b), B("bay-midday", bayOpen), P("pitch-again", back, close), B("bay", CeilHalf(close + t)) }, false));
        }
        var unlimited = Selling.Run(cart, spot, day.From, day.To, c, Selling.Unlimited(cart));
        var refused = new List<string>();
        (double Net, DayEval Eval)? best = null;
        foreach (var (name, day1, _) in forms)
        {
            var schedule = new FoodTrucks.Core.Saves.HireSchedule { Stops = FoodTrucks.Core.Fleet.WeekPlan.Ordered(
                day1.Concat(day1.Select(s => FoodTrucks.Core.Fleet.WeekPlan.CopyStop(s, DayOfWeek.Wednesday)))) };
            var stops = schedule.Stops;
            var bayStops = stops.Select((s, i) => (s, i)).Where(x => x.s.SpotKey.StartsWith("bay")).Select(x => x.i).ToList();
            var crewIds = Enumerable.Range(1, Math.Max(1, hires)).Select(i => $"h{i}").ToList();
            var staff = Math.Min(crewIds.Count, tables.Vehicle(cart.VehicleKind)!.Staff);
            crewIds = crewIds.Take(staff).ToList();
            var routeDays = FoodTrucks.Core.Fleet.WeekPlan.RouteDays(hireSchedules, schedule, mode, home: (bay.X, bay.Z), bayStops: bayStops);
            schedule.CrewShifts = FoodTrucks.Core.Fleet.WeekPlan.AutoSchedule(rules.Fleet, routeDays,
                crewIds.Select(id => new FoodTrucks.Core.Fleet.CrewWish(id)).ToList(), staff);
            var coverage = FoodTrucks.Core.Fleet.WeekPlan.CoverageOf(schedule, crewIds, FoodTrucks.Core.Fleet.HireSchedules.Absolute(1, 0),
                FoodTrucks.Core.Fleet.HireSchedules.Absolute(5, 0));
            var onShift = FoodTrucks.Core.Fleet.WeekPlan.WeekCount(schedule, crewIds);
            var problem = hireSchedules.Validate(schedule);
            if (problem != FoodTrucks.Core.Fleet.ScheduleProblem.None) { refused.Add($"{name}: {problem}"); continue; }
            // Each day ends back in the bay (the home leg), or at the bay stop that ends it.
            var (endX, endZ) = (bay.X, bay.Z);
            var first = hireSchedules.PlanDay(schedule, 2, DayOfWeek.Tuesday, endX, endZ, FoodTrucks.Core.Fleet.HireSchedules.Absolute(2, 0) - 1440, mode,
                home: (bay.X, bay.Z), bayStops: bayStops, coverage: coverage);
            if (first.Legs.Count == 0) { refused.Add($"{name}: no plan"); continue; }
            var plan = hireSchedules.PlanDay(schedule, 3, DayOfWeek.Wednesday, endX, endZ, first.Legs[^1].End, mode,
                home: (bay.X, bay.Z), bayStops: bayStops, coverage: coverage);
            if (plan.Legs.Count == 0 || plan.Skipped.Count > 0) { refused.Add($"{name}: stops skipped"); continue; }
            var origin = FoodTrucks.Core.Fleet.HireSchedules.Absolute(3, 0);
            var stock = Selling.Load(cart, spot, day.From, day.To, c);
            var result = new StopResult();
            int? firstOut = null;
            foreach (var leg in plan.Legs.Where(l => l.Kind == FoodTrucks.Core.Fleet.LegKind.Open))
            {
                var from = (int)(leg.Start - origin);
                var to = (int)(leg.End - origin);
                if (stops[leg.StopIndex].SpotKey.StartsWith("bay"))
                {
                    // The crew restocks in the bay before it leaves: each piece filled up again.
                    stock = Selling.TopUp(cart, spot, to, day.To, c, stock);
                    continue;
                }
                // Hour by hour, with the crew on shift then (Wednesday's hours of the week; day 3 is a Wednesday).
                for (var a = from; a < to; a = Math.Min(to, (a / 60 + 1) * 60))
                {
                    var b = Math.Min(to, (a / 60 + 1) * 60);
                    var servers = onShift[(2 * 24 + a / 60) % 168];
                    if (servers <= 0) continue;
                    var r = Selling.Run(cart.WithServers(servers), spot, a, b, c, stock);
                    firstOut ??= r.FirstOutAt;
                    result.Add(r);
                }
            }
            var hours = schedule.CrewShifts.Where(x => x.Day == DayOfWeek.Wednesday).Sum(x => x.End - x.Start);
            // The 04:00 check (Commissary's night check) on the planned day: no fine on the way somewhere, open at a stop
            // (a late stop still serving) or in the bay; standing anywhere else, the fine.
            var check = FoodTrucks.Core.Fleet.HireSchedules.Absolute(3, cr.NightCheckMinute);
            var legs = first.Legs.Concat(plan.Legs).ToList();
            var during = legs.FirstOrDefault(l => l.Start <= check && check < l.End);
            bool InBayStop(int stopIndex) => stopIndex == FoodTrucks.Core.Fleet.HireSchedules.HomeIndex || stops[stopIndex].SpotKey.StartsWith("bay");
            bool fined;
            if (during.End > during.Start) fined = false; // on its way, or open at a stop
            else
            {
                var before = legs.Where(l => l.End <= check).OrderBy(l => l.End).LastOrDefault();
                // Before its first leg ever the vehicle is where the day before left it: back in the bay.
                var stopIndex = before.End > before.Start ? before.StopIndex : FoodTrucks.Core.Fleet.HireSchedules.HomeIndex;
                fined = !InBayStop(stopIndex);
            }
            var fine = fined ? cr.OvernightFineCents / 100.0 : 0;
            var eval = new DayEval(result.Profit, result.Cost, hours, new Dictionary<string, double>(result.Sold), firstOut,
                unlimited.Units > 0 ? result.Units / unlimited.Units : 1, $"{name}; bay {t} min away{(fined ? "; fined at 04:00" : "")}", fine);
            var net = eval.Gross - hours * wage - feeDay - fine;
            if (best == null || net > best.Value.Net) best = (net, eval);
        }
        if (best == null) return null;
        var e = best.Value.Eval;
        return e with { Note = e.Note + (refused.Count > 0 ? $"; Core refused {string.Join(", ", refused)}" : "") };
    };

    var byDrive = FoodTrucks.Core.Fleet.TravelMode.Drive;
    var byPush = FoodTrucks.Core.Fleet.TravelMode.Push;
    setups.Add(("Cart: hot dog grill + cooler, player", MakeCart(new[] { HotDog, Soda }, "hd+cooler"), 0, false, Played(MakeCart(new[] { HotDog, Soda }, "hd+cooler"), travel, cartDay, 0)));
    setups.Add(("Cart: hot dog grill + Extra Storage, player", MakeCart(new[] { HotDog }, "hd+storage", true), 0, false, Played(MakeCart(new[] { HotDog }, "hd+storage", true), travel, cartDay, 0)));
    setups.Add(("Cart: coffee machine + display case, player", MakeCart(new[] { Coffee, Croissant }, "coffee+bakery"), 0, false, Played(MakeCart(new[] { Coffee, Croissant }, "coffee+bakery"), travel, coffeeDay, 0)));
    var alone = MakeCart(truckMenu, "truck", false, null, "truck", 1);
    setups.Add(("Truck (4 pieces), player alone", alone, 0, false, Played(alone, drive, truckDay, 0)));
    var withHire = MakeCart(truckMenu, "truck", false, null, "truck", 2, (1.0 + crewSpeed) / 2);
    setups.Add(("Truck (4 pieces), player + 1 hire", withHire, 1, false, Played(withHire, drive, truckDay, 1)));
    var crew1 = MakeCart(truckMenu, "truck", false, null, "truck", 1, crewSpeed);
    setups.Add(("Truck (4 pieces), crew of 1", crew1, 1, true, Route(crew1, truckDay, 1, byDrive)));
    var crew2 = MakeCart(truckMenu, "truck", false, null, "truck", 2, crewSpeed);
    setups.Add(("Truck (4 pieces), crew of 2", crew2, 2, true, Route(crew2, truckDay, 2, byDrive)));
    setups.Add(("Truck (4 pieces), crew of 2, 07:00 to 15:00", crew2, 2, true, Route(crew2, (420, 900), 2, byDrive)));
    setups.Add(("Truck (4 pieces), crew of 2, midday bay stop", crew2, 2, true, Route(crew2, truckDay, 2, byDrive, new[] { 660, 810, 960 })));
    var aloneStorage = MakeCart(new[] { HotDog, Coffee, Croissant }, "truck+storage", true, null, "truck", 1);
    setups.Add(("Truck (3 pieces + Extra Storage), player alone", aloneStorage, 0, false, Played(aloneStorage, drive, truckDay, 0)));
    var crewStorage = MakeCart(new[] { HotDog, Coffee, Croissant }, "truck+storage", true, null, "truck", 2, crewSpeed);
    setups.Add(("Truck (3 pieces + Extra Storage), crew of 2", crewStorage, 2, true, Route(crewStorage, truckDay, 2, byDrive)));
    var crewStorage1 = MakeCart(new[] { HotDog, Coffee, Croissant }, "truck+storage", true, null, "truck", 1, crewSpeed);
    setups.Add(("Truck (3 pieces + Extra Storage), crew of 1", crewStorage1, 1, true, Route(crewStorage1, truckDay, 1, byDrive)));
    var crewCart = MakeCart(new[] { HotDog }, "hd+storage crew", true, null, "cart", 1, crewSpeed);
    setups.Add(("Cart: hot dog grill + Extra Storage, 1 hire", crewCart, 1, true, Route(crewCart, cartDay, 1, byPush)));

    var real = spots.Where(s => !s.Name.Contains("archetype")).ToList();
    var pitchTables = FoodTrucks.Core.Pitches.PitchTables.Parse(File.ReadAllText(Path.Combine(opts.Data, "pitches.json")), tables);
    var premiumSpots = pitchTables.Spots.Select(p =>
    {
        var parts = p.Address.Replace("ba:street_", "").Split('#');
        return (Premium: p, Spot: spots.FirstOrDefault(s => s.Name == $"{parts[1]} {parts[0]}"));
    }).Where(p => p.Spot != null).ToList();
    var week = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    Conditions On(DayOfWeek day, FoodTrucks.Core.Pitches.PitchBonus? bonus = null, int month = 5, bool rain = false) => new()
        { Day = day, Month = month, Rain = rain, Pricing = opts.Pricing, PriceFactor = opts.PriceFactor, MarketDemand = opts.Market, Pitch = bonus ?? FoodTrucks.Core.Pitches.PitchBonus.None };
    double DayNet(DayEval e, bool commissary, string kind) => e.Gross - e.HireHours * wage - (commissary ? feeDay : 0) - e.Fine - RentDay(kind);
    // A setup's week at a pitch: the seven days, and the week's gross, paid hire-hours, wages, fee and fines, and net.
    (List<DayEval> Days, double Gross, int Hours, double Wages, double Fee, double Fines, double Net) WeekOf(
        (string Label, Cart Cart, int Hires, bool Commissary, Func<Spot, Conditions, DayEval?> Day) setup, Spot spot, FoodTrucks.Core.Pitches.PitchBonus? bonus)
    {
        var days = week.AsParallel().AsOrdered().Select(d => setup.Day(spot, On(d, bonus))!).ToList();
        var gross = days.Sum(d => d.Gross);
        var hours = days.Sum(d => d.HireHours);
        var fee = setup.Commissary ? 7 * feeDay : 0;
        var fines = days.Sum(d => d.Fine);
        return (days, gross, hours, hours * wage, fee, fines, gross - 7 * RentDay(setup.Cart.VehicleKind) - hours * wage - fee - fines);
    }
    var tiersOf = new Dictionary<string, List<(string Tier, Spot Spot, FoodTrucks.Core.Pitches.PitchBonus? Bonus, string Name)>>();

    o.AppendLine("## Balance pass: a day and a week per vehicle and pitch (May, dry, ceiling price)");
    o.AppendLine();
    o.AppendLine($"Pitches: every real pitch ranked by its weekday net (gross less wages, fee and rent); poor is the 25th percentile, typical the median, good the 90th percentile, best the top; premium is the median and the best of the {premiumSpots.Count} premium spots, held all day. " +
                 "Crewed setups rank only pitches in a region with a commissary, since a crew loads only in its commissary's bay. " +
                 $"Gross: sales less goods at wholesale. Rent: the commissary space, ${RentDay("cart"):0.00} a day for a cart, ${RentDay("truck"):0.00} for a truck. " +
                 $"Hires: customer service {opts.CrewSkill:0} at satisfaction {opts.CrewSatisfaction:0}, ${wage:0.00} an hour, team speed {crewSpeed:0.00}, paid as the game pays them: a crew every hour of its shifts (Core's auto schedule for the route: at most 12 hours a shift, a cart's crew of 1), sold out or not, serving only while on shift; a window the player opened, every hour the hire serves in its first half. " +
                 $"Fee: ${cr.DeliveryFeeCents / 100.0:0} per delivery at a commissary, {opts.DeliveriesPerWeek:0.#} a week shared by {opts.FeeSharing:0.#} vehicles (${feeDay:0.00} a vehicle a day), when a crew's stock comes by phone (a logistics plan has none); players haul from the wholesaler. " +
                 $"The player restocks when a piece runs out: a cart is pushed at {opts.MetresPerSecond} m/s, a truck driven at {opts.DriveSpeed} m/s, to the nearest wholesaler. " +
                 "A crew's route is built and checked by Core's HireSchedules: pitch then a 30-minute bay stop before midnight (the pitch closes early enough; the night in the bay), or a morning bay stop then the pitch (the night at the pitch, fined $250 at 04:00), whichever Core accepts and nets more; the crew loads in the bay and sells until everything is out or the stop closes, and a sold-out crew waits for the next bay stop. The midday bay stop row adds a bay stop at 11:00, 13:30 or 16:00, whichever nets most. The note names the route and the forms Core refused. " +
                 "First out: hours from opening, full, until the first piece is empty, weekday. Of demand: units sold against the same weekday with unlimited stock and no trips.");
    o.AppendLine();
    o.AppendLine("| Setup | Pitch | Spot | Weekday | Saturday | Sunday | Week gross | Rent | Wages (hours) | Fee and fines | Week net | Wages ÷ gross | First out | Of demand | Note |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
    var weekUnits = new List<(string Setup, string Tier, Dictionary<string, double> Units)>();
    var goodSpots = new Dictionary<string, Spot>();
    foreach (var setup in setups)
    {
        var (label, cart, hires, commissary, dayOf) = setup;
        var from = cart.VehicleKind == "truck" ? truckDay.From : label.Contains("coffee") ? coffeeDay.From : cartDay.From;
        var ranked = real.AsParallel().Select(s => (s, e: dayOf(s, On(DayOfWeek.Wednesday)))).Where(x => x.e != null)
            .Select(x => (x.s, p: DayNet(x.e!, commissary, cart.VehicleKind))).OrderBy(x => x.p).ThenBy(x => x.s.Name, StringComparer.Ordinal).ToList();
        var premium = premiumSpots.AsParallel().Select(p => (p, e: dayOf(p.Spot!, On(DayOfWeek.Wednesday, FoodTrucks.Core.Pitches.PitchBonus.Of(p.Premium)))))
            .Where(x => x.e != null).Select(x => (x.p, d: DayNet(x.e!, commissary, cart.VehicleKind)))
            .OrderBy(x => x.d).ThenBy(x => x.p.Premium.Id, StringComparer.Ordinal).ToList();
        var tiers = new List<(string Tier, Spot Spot, FoodTrucks.Core.Pitches.PitchBonus? Bonus, string Name)>
        {
            ("poor", ranked[ranked.Count / 4].s, null, ranked[ranked.Count / 4].s.Name),
            ("typical", ranked[ranked.Count / 2].s, null, ranked[ranked.Count / 2].s.Name),
            ("good", ranked[(int)(ranked.Count * 0.9)].s, null, ranked[(int)(ranked.Count * 0.9)].s.Name),
            ("best", ranked[^1].s, null, ranked[^1].s.Name),
        };
        if (premium.Count > 0)
        {
            tiers.Add(("premium, median", premium[premium.Count / 2].p.Spot!, FoodTrucks.Core.Pitches.PitchBonus.Of(premium[premium.Count / 2].p.Premium), premium[premium.Count / 2].p.Premium.Id));
            tiers.Add(("premium, best", premium[^1].p.Spot!, FoodTrucks.Core.Pitches.PitchBonus.Of(premium[^1].p.Premium), premium[^1].p.Premium.Id));
        }
        goodSpots[label] = tiers[2].Spot;
        tiersOf[label] = tiers;
        foreach (var (tier, spot, bonus, name) in tiers)
        {
            var (days7, gross, hireHours, wages, fee, fines, net) = WeekOf(setup, spot, bonus);
            var rent = 7 * RentDay(cart.VehicleKind);
            fee += fines;
            var weekday = days7[2];
            o.AppendLine($"| {label} | {tier} | {name} ({H(spot.Hood)}) | {Money(weekday.Gross)} | {Money(days7[5].Gross)} | {Money(days7[6].Gross)} | " +
                         $"{Money(gross)} | {Money(rent)} | {Money(wages)} ({hireHours}) | {Money(fee)} | **{Money(net)}** | {(gross > 0 ? wages / gross : 0):P0} | " +
                         $"{(weekday.FirstOut == null ? "never" : $"{(weekday.FirstOut.Value - from) / 60.0:0.0} h")} | {weekday.OfDemand:P0} | {weekday.Note} |");
            var units = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var d in days7) foreach (var p in d.Sold) units[p.Key] = units.GetValueOrDefault(p.Key) + p.Value;
            weekUnits.Add((label, tier, units));
        }
    }
    o.AppendLine();

    // Extra Storage (and the midday bay stop) at the same pitch: the second setup at the first one's pitches.
    o.AppendLine("## The same pitch with and without Extra Storage (and the midday bay stop)");
    o.AppendLine();
    o.AppendLine("Each pair at the first setup's pitches (Extra Storage takes a slot, so the truck drops its cooler). Payback: days for the week's extra net to pay the cabinet's price.");
    o.AppendLine();
    o.AppendLine("| Without | With | Pitch | Spot | Week net without | with | Difference | Payback at $900 | at $1,500 |");
    o.AppendLine("|---|---|---|---|---|---|---|---|---|");
    foreach (var (without, with) in new[]
             {
                 ("Cart: hot dog grill + cooler, player", "Cart: hot dog grill + Extra Storage, player"),
                 ("Truck (4 pieces), player alone", "Truck (3 pieces + Extra Storage), player alone"),
                 ("Truck (4 pieces), crew of 1", "Truck (3 pieces + Extra Storage), crew of 1"),
                 ("Truck (4 pieces), crew of 2", "Truck (3 pieces + Extra Storage), crew of 2"),
                 ("Truck (4 pieces), crew of 2", "Truck (4 pieces), crew of 2, midday bay stop"),
             })
    {
        var a = setups.First(x => x.Label == without);
        var b = setups.First(x => x.Label == with);
        foreach (var (tier, spot, bonus, name) in tiersOf[without])
        {
            var na = WeekOf(a, spot, bonus).Net;
            var nb = WeekOf(b, spot, bonus).Net;
            var gain = nb - na;
            string Pay(double price) => !with.Contains("Extra Storage") ? "-" : gain > 0 ? $"{price / (gain / 7):0.0} days" : "never";
            o.AppendLine($"| {without} | {with} | {tier} | {name} ({H(spot.Hood)}) | {Money(na)} | {Money(nb)} | {(gain >= 0 ? "+" : "")}{Money(gain)} | {Pay(900)} | {Pay(1500)} |");
        }
    }
    o.AppendLine();

    // The phone limit: boxes of each food a week (rules.json commissary.foodBoxesPerWeek), one limit for the whole fleet.
    o.AppendLine($"## The weekly phone limit: {cr.FoodBoxesPerWeek} boxes of each food a week for the whole fleet");
    o.AppendLine();
    o.AppendLine("Each food's units sold in the week above, and how many such vehicles the fleet's limit supplies (limit ÷ one vehicle's week). A box holds the item's box size (500 for every food here).");
    o.AppendLine();
    o.AppendLine("| Setup | Pitch | Units a week (vehicles the fleet's limit supplies) |");
    o.AppendLine("|---|---|---|");
    foreach (var (setup, tier, units) in weekUnits.Where(w => w.Tier is "typical" or "good" or "best" or "premium, best"))
        o.AppendLine($"| {setup} | {tier} | {string.Join(", ", units.OrderByDescending(p => p.Value).Select(p => $"{p.Key.Split('_').Last()} {p.Value:0} ({cr.FoodBoxLimit(p.Key) * prices.Items[p.Key].Box / Math.Max(1, p.Value):0.0})"))} |");
    o.AppendLine();

    // The delivery fee against hauling: the player's day at the good pitch restocking from the wholesaler (no fee),
    // from the nearest commissary (the fee on all goods, the Restock button) and in place (no trip at all).
    var played = new (string Label, Cart Cart, Travel Tr, (int From, int To) Day)[]
    {
        ("Cart: hot dog grill + cooler, player", setups[0].Cart, travel, cartDay), ("Cart: hot dog grill + Extra Storage, player", setups[1].Cart, travel, cartDay),
        ("Cart: coffee machine + display case, player", setups[2].Cart, travel, coffeeDay), ("Truck (4 pieces), player alone", alone, drive, truckDay),
        ("Truck (4 pieces), player + 1 hire", withHire, drive, truckDay),
    };
    o.AppendLine("## The delivery fee against hauling: the player's weekday at the good pitch");
    o.AppendLine();
    o.AppendLine("Wholesaler: all stock hauled from the nearest wholesaler, goods at wholesale. Commissary: all stock ordered by phone (the vehicle's day share of the flat delivery fee, as in the pass table) and restocks at the nearest commissary's bay with its Restock button. In place: no trip (the upper bound).");
    o.AppendLine();
    o.AppendLine("| Setup | Spot | Wholesaler: profit, restocks, minutes away | Commissary: profit less fee, restocks, minutes away | Fee | In place |");
    o.AppendLine("|---|---|---|---|---|---|");
    foreach (var (label, cart, tr, (from, to)) in played)
    {
        var spot = goodSpots[label];
        var c = On(DayOfWeek.Wednesday);
        var w = new Planner(cart, city, tr).SingleSpot(spot, from, to, c);
        var com = new Planner(cart, city, new Travel { MetresPerMinute = tr.MetresPerMinute, Detour = tr.Detour, ShopMinutes = 5 }) { Depots = commissaries }.SingleSpot(spot, from, to, c);
        var hand = new Planner(cart, city, tr) { Mode = RestockMode.BoxInHand }.SingleSpot(spot, from, to, c);
        var fee = feeDay;
        o.AppendLine($"| {label} | {spot.Name} ({H(spot.Hood)}) | {Money(w.Total.Profit)}, {w.Restocks}, {w.Stays.Sum(x => x.RestockMinutes)} | " +
                     $"{Money(com.Total.Profit - fee)}, {com.Restocks}, {com.Stays.Sum(x => x.RestockMinutes)} | {Money(fee)} | {Money(hand.Total.Profit)} |");
    }
    o.AppendLine();

    // Parks: the best park pitch (archetype, one per district) against the setup's good real pitch.
    var parkSpots = spots.Where(s => s.Name.StartsWith("park (")).ToList();
    if (parkSpots.Count > 0)
    {
        o.AppendLine($"## Parks: the best park pitch (one archetype per district, {parkMix}) against the good real pitch");
        o.AppendLine();
        var parkSetups = played.Where(p => p.Label != "Truck (4 pieces), player + 1 hire").ToList();
        if (carts.TryGetValue("icecream", out var ic)) parkSetups.Add(("Cart: ice cream counter, player", ic.Cart, travel, cartDay));
        var parkCases = new (string Label, Conditions C)[]
        {
            ("Weekday May", On(DayOfWeek.Wednesday)), ("Saturday May", On(DayOfWeek.Saturday)), ("Saturday July", On(DayOfWeek.Saturday, month: 7)),
            ("Saturday July, rain", On(DayOfWeek.Saturday, month: 7, rain: true)), ("Saturday January", On(DayOfWeek.Saturday, month: 1)),
        };
        o.AppendLine("| Setup | Good real pitch, weekday May | " + string.Join(" | ", parkCases.Select(x => $"Park, {x.Label}")) + " |");
        o.AppendLine("|---|---|" + string.Concat(parkCases.Select(_ => "---|")));
        foreach (var (label, cart, tr, (from, to)) in parkSetups)
        {
            var planner = new Planner(cart, city, tr);
            var good = goodSpots.TryGetValue(label, out var g) ? g
                : real.AsParallel().Select(s => (s, p: planner.SingleSpot(s, from, to, On(DayOfWeek.Wednesday)).Total.Profit)).OrderBy(x => x.p).ThenBy(x => x.s.Name, StringComparer.Ordinal).ElementAt((int)(real.Count * 0.9)).s;
            var cells = parkCases.Select(pc =>
            {
                var best = parkSpots.Select(s => planner.SingleSpot(s, from, to, pc.C)).OrderByDescending(d => d.Total.Profit).First();
                return $"{Money(best.Total.Profit)} ({H(best.Stays[0].Spot.Hood)})";
            });
            o.AppendLine($"| {label} | {Money(planner.SingleSpot(good, from, to, On(DayOfWeek.Wednesday)).Total.Profit)} | {string.Join(" | ", cells)} |");
        }
        o.AppendLine();
    }
}

// Regression checks for the calculator itself; a failure throws. Route (review 0.9.7, round 2): a middle stop that
// starts without a refill and restocks inside its stay must hand its end stock to the next stop, so a refill is
// counted only after a stay that really ended with a piece below half.
if (opts.Wants("checks"))
{
    var cart = carts["hotdog+cooler"].Cart;
    var planner = PlannerFor(cart);
    var c = Cond();
    var windows = new[] { (9 * 60, 10 * 60), (10 * 60, 13 * 60 + 50), (13 * 60 + 50, 14 * 60 + 50) };
    bool BelowHalf(Dictionary<string, double> stock) => cart.PieceLoads(stock).Values.Any(l => l < cart.PerPiece / 2.0);
    // The first pitch, in name order, where the case happens: the first stop leaves every piece above half (no refill),
    // the middle stop restocks inside its stay and ends above half too, so the third stop needs no refill.
    var found = spots.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Hood, StringComparer.Ordinal).Select(s =>
    {
        var first = planner.SellFull(s, windows[0].Item1, windows[0].Item2, c);
        if (BelowHalf(first.Left)) return null;
        var middle = planner.SellAt(s, windows[1].Item1, windows[1].Item2, c, new Dictionary<string, double>(first.Left));
        return middle.Restocks > 0 && !BelowHalf(middle.Left) ? (s, middle) : ((Spot, Stay)?)null;
    }).FirstOrDefault(x => x != null) ?? throw new InvalidOperationException("Check route: no pitch restocks inside the middle stop; pick other windows.");
    var (spot, mid) = found;
    var day = planner.Route(new[] { spot, spot, spot }, windows, c);
    o.AppendLine("## Calculator checks");
    o.AppendLine();
    o.AppendLine($"- Route, three stops at {spot.Name} ({H(spot.Hood)}), 09:00, 10:00 and 13:50: the middle stop restocks {mid.Restocks} times and ends with " +
                 string.Join(", ", mid.Left.Select(p => $"{p.Value:0.##} {p.Key}")) + $"; route refills {(day == null ? "none (no route)" : day.Refills.ToString())}, expected 0.");
    if (day == null || day.Refills != 0) throw new InvalidOperationException("Check route: the route refilled after a stop that ended above half.");
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

// The AI playthrough's plan (tools/playthrough writes it from the game's routes): each vehicle's foods and its week of
// stops at their hours, priced as its crew sells them. The crew load in the bay before the first stop and carry that
// stock through the day (RestockMode.None, as Core's CrewRestock loads only in the bay). Each stop is the city's
// building nearest the stop's position (city.json positions are approximate). Revenue and units per weekday, for the
// runner's ±25% comparison; written as JSON with --json-out.
if (opts.Wants("playthrough"))
{
    if (opts.Plan == null) throw new ArgumentException("--reports playthrough needs --plan FILE (tools/playthrough report writes balance-plan.json).");
    var plan = JObject.Parse(File.ReadAllText(opts.Plan));
    var proposal = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(opts.City))!, "new-foods-proposal.json");
    var priced = new JArray();
    var buildingSpots = spots.Where(sp => !sp.Name.Contains("(archetype)")).ToList();
    Spot Nearest(double x, double z) => buildingSpots.OrderBy(sp => (sp.X - x) * (sp.X - x) + (sp.Z - z) * (sp.Z - z)).First();
    o.AppendLine("## Playthrough plan");
    o.AppendLine();
    o.AppendLine("| Vehicle | Menu | Servers | Week revenue | Week units |");
    o.AppendLine("|---|---|---|---|---|");
    foreach (var v in plan["vehicles"] ?? new JArray())
    {
        var id = (string?)v["id"] ?? "?";
        var kind = (string?)v["kind"] ?? "cart";
        var menu = (v["menu"] ?? new JArray()).Select(t => (string)t!).Where(i => tables.Product(i) != null).ToArray();
        // The new foods' prices are only in the proposal's game section (game-prices.json predates them).
        if (menu.Any(i => !prices.Items.ContainsKey(i)) && File.Exists(proposal) && JObject.Parse(File.ReadAllText(proposal))["game"] is JObject newPrices)
            prices.Merge(newPrices);
        menu = menu.Where(prices.Items.ContainsKey).ToArray();
        if (menu.Length == 0)
        {
            o.AppendLine($"| {id} | none priced | | | |");
            continue;
        }
        var servers = (int?)v["servers"];
        var cart = MakeCart(menu, $"{kind} {id}", false, null, kind, servers is > 0 ? servers : null);
        var planner = new Planner(cart, city, kind == "truck" ? drive : travel) { Mode = RestockMode.None };
        var byDay = new JObject();
        double weekRevenue = 0, weekUnits = 0;
        foreach (var d in v["week"] ?? new JArray())
        {
            var name = (string)d["day"]!;
            var stops = (d["stops"] ?? new JArray()).ToList();
            double revenue = 0, cost = 0, units = 0;
            if (stops.Count > 0)
            {
                var c = Cond(Enum.Parse<DayOfWeek>(name, true));
                var first = (int)stops[0]["open"]!;
                var last = (int)stops[^1]["close"]!;
                Dictionary<string, double>? stock = null;
                foreach (var stop in stops)
                {
                    var spot = Nearest((double)stop["x"]!, (double)stop["z"]!);
                    stock ??= Selling.Load(cart, spot, first, last, c);
                    var stay = planner.SellAt(spot, (int)stop["open"]!, (int)stop["close"]!, c, stock);
                    revenue += stay.Result.Revenue; cost += stay.Result.Cost; units += stay.Result.Units;
                    stock = stay.Left;
                }
            }
            byDay[name] = new JObject { ["revenue"] = Math.Round(revenue, 2), ["stock_cost"] = Math.Round(cost, 2), ["units"] = Math.Round(units, 1) };
            weekRevenue += revenue; weekUnits += units;
        }
        priced.Add(new JObject { ["id"] = id, ["kind"] = kind, ["menu"] = new JArray(menu), ["servers"] = cart.Servers, ["days"] = byDay });
        o.AppendLine($"| {id} ({kind}) | {string.Join(", ", menu)} | {cart.Servers} | {Money(weekRevenue)} | {weekUnits:0} |");
    }
    o.AppendLine();
    if (opts.JsonOut != null) File.WriteAllText(opts.JsonOut, new JObject { ["vehicles"] = priced }.ToString());
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
        var settings = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };
        if (patch["demand"] is JObject d) demand.Merge(d, settings);
        if (patch["economy"] is JObject e) economy.Merge(e, settings);
        // A null in the patch removes the key (economy.equipment.burgerstation in new-foods-proposal.json).
        foreach (var gone in demand.Descendants().Concat(economy.Descendants()).OfType<JProperty>().Where(p => p.Value.Type == JTokenType.Null).ToList()) gone.Remove();
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
    // The player's truck in town, metres a second (a game minute a real second): about 29 km/h with turns and lights.
    public double DriveSpeed = 8.0;
    // A typical hire for the pass report: customer service skill and satisfaction.
    public double CrewSkill = 60;
    public double CrewSatisfaction = 85;
    // Phone deliveries a week at the vehicle's commissary, and how many vehicles share each (one flat fee per delivery).
    public double DeliveriesPerWeek = 1;
    public double FeeSharing = 1;
    public double CartPrice = 1200;
    public bool Parks = true;
    // The playthrough report's plan and its JSON output (tools/playthrough).
    public string? Plan;
    public string? JsonOut;
    // --service queue (default): the game's queue since 0.4.0. --service fluid: the old min(buyers, capacity).
    public bool Queue = true;
    public string[] Carts = { "hotdog+cooler", "coffee+bakery", "truck-like" };
    // Carts for the foods report: --carts when given, otherwise every cart the data knows.
    public string[]? FoodCarts;
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
                case "--plan": o.Plan = Next(); break;
                case "--json-out": o.JsonOut = Next(); break;
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
                case "--drive-speed": o.DriveSpeed = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--crew-skill": o.CrewSkill = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--crew-satisfaction": o.CrewSatisfaction = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--deliveries-per-week": o.DeliveriesPerWeek = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--fee-sharing": o.FeeSharing = Math.Max(1, double.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--no-parks": case "--no-archetypes": o.Parks = false; break;
                case "--carts": o.Carts = Next().Split(','); o.FoodCarts = o.Carts; break;
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
