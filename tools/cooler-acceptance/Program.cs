using FoodTrucks.Balance;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Selling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
var root = Path.GetFullPath(args[0]);
var tables = DemandTables.Parse(File.ReadAllText(root + "/data/demand.json"), File.ReadAllText(root + "/data/economy.json"), File.ReadAllText(root + "/data/zones.json"));
var rules = RuleTables.Parse(File.ReadAllText(root + "/data/rules.json"));
var prices = GamePrices.Load(root + "/tools/balance/game-prices.json");
const string food = "ba:itemname_hotdog", soda = "ba:itemname_sodacan";
var rows = new List<object>();
foreach (var kind in new[] { "cart", "truck" })
foreach (var scenario in new[] { ("quiet", .15), ("busy", 1.0), ("saturated", 6.0) })
foreach (var cooler in new[] { false, true }) {
 long foods = 0, addons = 0, standalone = 0, restocks = 0, refused = 0;
 double revenue = 0, cost = 0;
 for (var seed = 1; seed <= 30; seed++) {
  var menu = cooler ? new[] { food, soda } : new[] { food };
  var cart = new Cart(tables, prices, menu, "matched", kind: kind);
  var spot = new Spot("synthetic office", AreaMix.Single("office"), "hells-kitchen", "", 0, 0);
  var conditions = new Conditions { Pricing = Pricing.Default };
  var capacity = menu.ToDictionary(i => i, i => (int)(cart.PerPiece / cart.WeightOf(i)));
  var stock = new Dictionary<string, int>(capacity);
  var sim = new SalesSimulator(tables, rules.Queue);
  var state = SalesSimulator.Open(seed);
  var drawn = menu.ToDictionary(i => i, _ => 0);
  var loaded = new Dictionary<string, int>(stock);
  for (var minute = 600; minute < 1080; minute++) {
   // Explicit hourly instantaneous top-up policy, identical in every pair. No travel or wage claim.
   if (minute > 600 && minute % 60 == 0 && stock.Any(p => p.Value < capacity[p.Key])) {
    restocks++;
    foreach (var i in menu) { loaded[i] += capacity[i] - stock[i]; stock[i] = capacity[i]; }
   }
   var plan = cart.Plan(spot, minute, conditions, stock.ToDictionary(p => p.Key, p => (double)p.Value));
   var result = sim.Advance(state, new SalesInput {
    Menu = menu, Stock = stock, CustomersPerHour = plan.Demand * scenario.Item2,
    Conversion = plan.Conversion, CustomerShares = plan.BuyerShares, CategoryItems = plan.SaleItems,
    AddOnItem = plan.AddOnItem, AddOnsPerCustomer = plan.DrinksPerFoodBuyer,
    Servers = cart.Servers, ServiceSpeed = 1, QueueLimit = rules.Service.QueueLimit(kind, rules.Queue.MaxQueue)
   }, 1);
   foreach (var d in result.Draws) { stock[d.Key] -= d.Value; drawn[d.Key] += d.Value; }
   foreach (var sale in result.Sales) {
    if (sale.Item == food) foods++; else if (sale.AddOn) addons++; else standalone++;
    revenue += cart.PriceOf(sale.Item, spot.Hood, conditions); cost += prices.Items[sale.Item].Wholesale;
   }
   refused += result.Count(CustomerExit.QueueFull);
   if (stock.Values.Any(v => v < 0)) throw new Exception("Negative stock");
  }
  foreach (var i in menu) if (loaded[i] != stock[i] + drawn[i]) throw new Exception("Stock conservation");
 }
 rows.Add(new { kind, pitch = scenario.Item1, demandMultiplier = scenario.Item2, cooler, seeds = 30,
  foodOrders = foods / 30.0, sodaAddons = addons / 30.0, sodaStandalone = standalone / 30.0,
  revenue = revenue / 30, soldStockCost = cost / 30, hourlyRestocks = restocks / 30.0, queueFull = refused / 30.0 });
}
var inputs = new[] { "data/demand.json", "data/economy.json", "data/rules.json", "tools/balance/game-prices.json", "tools/cooler-acceptance/Program.cs", "tools/balance/CartModel.cs", "Core/Selling/SalesSimulator.cs" }
 .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, p)))).ToLowerInvariant());
Console.WriteLine(JsonConvert.SerializeObject(new { assumptions = "Offline synthetic matched office pitches; shipped demand/queue/service rules; 30 paired seeds; 10:00–18:00 dry May Wednesday, default prices and skill; quiet/busy/saturated use 0.15/1/6 demand multipliers. Hourly instantaneous top-ups; no travel, payroll, rent or profit claim. Same food equipment and capacity with/without one dedicated fridge. No tuning changes. Native visual/economic validation remains separate.", inputs, rows }, Formatting.Indented));
