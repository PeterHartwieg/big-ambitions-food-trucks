using System.Security.Cryptography;
using System.Text;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TimerTravel = FoodTrucks.Core.Fleet.Travel;

namespace FoodTrucks.Balance;

// Offline proposed dispatch policy. It neither calls the native rig nor changes the runtime.
internal sealed record CrewAssumptions
{
    public int SchemaVersion { get; init; }
    public string AssumptionStatus { get; init; } = "";
    public string Units { get; init; } = "";
    public double RoadFactor { get; init; }
    public double NormalSpeedMetresPerSecond { get; init; }
    public double HeavySpeedMetresPerSecond { get; init; }
    public int NormalWaitGameMinutesPerLeg { get; init; }
    public int HeavyWaitGameMinutesPerLeg { get; init; }
    public double TankLitres { get; init; }
    public double ReserveLitres { get; init; }
    public double StressStartingLitres { get; init; }
    public double LitresPerKilometre { get; init; }
    public double IdleLitresPerGameMinute { get; init; }
    public double FuelDollarsPerLitre { get; init; }
    public double StationOneWayStraightMetres { get; init; }
    public int StationServiceGameMinutes { get; init; }
    public int LoadingGameMinutes { get; init; }
    public string[] Districts { get; init; } = Array.Empty<string>();
    public double[] DemandQuantiles { get; init; } = Array.Empty<double>();
    public double[] SpeedSensitivityMetresPerSecond { get; init; } = Array.Empty<double>();
    public double[] FuelSensitivityLitresPerKilometre { get; init; } = Array.Empty<double>();
    public int[] WaitSensitivityGameMinutesPerLeg { get; init; } = Array.Empty<int>();
    public double[] ShortRouteTotalStraightMetresBand { get; init; } = Array.Empty<double>();
    public double[] LongRouteTotalStraightMetresBand { get; init; } = Array.Empty<double>();

    public static CrewAssumptions Load(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        // Require every field, reject misspellings, and reject fractional integer durations.
        var expected = JObject.FromObject(new CrewAssumptions()).Properties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (json.Properties().Any(p => !expected.Contains(p.Name)) || expected.Any(k => json.GetValue(k, StringComparison.OrdinalIgnoreCase) == null))
            throw new ArgumentException("crew-economics config must contain exactly the versioned fields in crew-economics.json.");
        foreach (var key in new[] { "schemaVersion", "normalWaitGameMinutesPerLeg", "heavyWaitGameMinutesPerLeg", "stationServiceGameMinutes", "loadingGameMinutes" })
            if (json[key]?.Type != JTokenType.Integer) throw new ArgumentException($"{key} must be an integer.");
        if (json["waitSensitivityGameMinutesPerLeg"] is not JArray waits || waits.Any(t => t.Type != JTokenType.Integer))
            throw new ArgumentException("waitSensitivityGameMinutesPerLeg must contain integer game minutes.");
        var c = json.ToObject<CrewAssumptions>() ?? throw new ArgumentException("Empty crew-economics config.");
        c.Validate();
        return c;
    }

    public void Validate()
    {
        void Range(string name, double n, double min, double max)
        {
            if (!double.IsFinite(n) || n < min || n > max) throw new ArgumentException($"{name} must be in [{min}, {max}].");
        }
        if (SchemaVersion != 2 || string.IsNullOrWhiteSpace(AssumptionStatus) ||
            Units != "coordinates/straight distance: metres; physical speed: metres/real second; time: game minutes; 1 real second = 1 game minute; fuel: litres; currency: game dollars")
            throw new ArgumentException("Unsupported crew-economics schema, missing assumption status, or unsupported units.");
        Range(nameof(RoadFactor), RoadFactor, 1, 5);
        Range(nameof(NormalSpeedMetresPerSecond), NormalSpeedMetresPerSecond, .01, 100);
        Range(nameof(HeavySpeedMetresPerSecond), HeavySpeedMetresPerSecond, .01, 100);
        Range(nameof(NormalWaitGameMinutesPerLeg), NormalWaitGameMinutesPerLeg, 0, 1440);
        Range(nameof(HeavyWaitGameMinutesPerLeg), HeavyWaitGameMinutesPerLeg, 0, 1440);
        Range(nameof(TankLitres), TankLitres, .01, 1000);
        Range(nameof(ReserveLitres), ReserveLitres, 0, TankLitres - .001);
        Range(nameof(StressStartingLitres), StressStartingLitres, 0, TankLitres);
        Range(nameof(LitresPerKilometre), LitresPerKilometre, .001, 100);
        Range(nameof(IdleLitresPerGameMinute), IdleLitresPerGameMinute, 0, 10);
        Range(nameof(FuelDollarsPerLitre), FuelDollarsPerLitre, .001, 1000);
        Range(nameof(StationOneWayStraightMetres), StationOneWayStraightMetres, 1, 100000);
        Range(nameof(StationServiceGameMinutes), StationServiceGameMinutes, 1, 720);
        Range(nameof(LoadingGameMinutes), LoadingGameMinutes, 1, 60);
        if (Districts == null || Districts.Length is < 1 or > 4 || Districts.Any(string.IsNullOrWhiteSpace) || Districts.Distinct().Count() != Districts.Length)
            throw new ArgumentException("districts must contain 1–4 distinct district ids with a commissary.");
        void Grid(string name, double[]? values, double min, double max, int limit)
        {
            if (values == null || values.Length == 0 || values.Length > limit || values.Distinct().Count() != values.Length)
                throw new ArgumentException($"{name} must contain 1–{limit} distinct values.");
            foreach (var v in values) Range(name, v, min, max);
        }
        Grid(nameof(DemandQuantiles), DemandQuantiles, 0, 1, 5);
        Grid(nameof(SpeedSensitivityMetresPerSecond), SpeedSensitivityMetresPerSecond, .01, 100, 12);
        Grid(nameof(FuelSensitivityLitresPerKilometre), FuelSensitivityLitresPerKilometre, .001, 100, 10);
        if (WaitSensitivityGameMinutesPerLeg == null) throw new ArgumentException("Missing wait sensitivity grid.");
        Grid(nameof(WaitSensitivityGameMinutesPerLeg), WaitSensitivityGameMinutesPerLeg.Select(v => (double)v).ToArray(), 0, 1440, 8);
        void Band(string name, double[] band)
        {
            if (band == null || band.Length != 2) throw new ArgumentException(name + " must be [inclusive minimum, exclusive maximum].");
            Range(name, band[0], 0, 100000); Range(name, band[1], .001, 100000);
            if (band[0] >= band[1]) throw new ArgumentException(name + " must increase.");
        }
        Band(nameof(ShortRouteTotalStraightMetresBand), ShortRouteTotalStraightMetresBand);
        Band(nameof(LongRouteTotalStraightMetresBand), LongRouteTotalStraightMetresBand);
        if (ShortRouteTotalStraightMetresBand[1] > LongRouteTotalStraightMetresBand[0]) throw new ArgumentException("Short/long route bands must not overlap.");
    }
}

internal sealed record CrewScenario(string Id, bool Legacy, double SpeedMetresPerSecond, int WaitGameMinutesPerLeg,
    double LitresPerKilometre, double StartingLitres, bool Stress = false, bool FuelEnabled = true);
internal sealed record CrewStop(Spot Pitch, int Open, int Close);
internal sealed record CrewRoute(string Id, string District, double DemandQuantile, double AnchorDemandScore,
    string LengthClass, Spot Depot, string DepotCoordinateSource, List<CrewStop> Stops, double[] TotalStraightMetresBand, double DownstreamWindowContributionScore);
internal sealed record CrewLeg(double StraightMetres, double AssumedRoadMetres, int Minutes, int WaitMinutes, double FuelLitres);
internal sealed record CrewTrip(string Purpose, string From, string To, int Depart, int Arrive, CrewLeg Leg);
internal sealed record CrewRefuel(int Arrive, int Depart, double FuelOnArrival, double PurchasedLitres, double FuelAfterFill);
internal sealed class CrewVisit
{
    public string Pitch = "";
    public int WindowOpen, WindowClose;
    public int? Arrive, ServiceFrom, ServiceTo;
    public string Outcome = "";
    public double PotentialMissedDemand, LunchPotentialMissedDemand;
    public Dictionary<string, double> StockBefore = new(), StockAfter = new();
    public StopResult Sales = new();
}
internal sealed class CrewDay
{
    public string Weekday = "", Date = "";
    public string Outcome = "CompletedHome";
    public bool CompletedHomeReturn;
    public bool ScheduledRouteFeasible = true;
    public int? HomeMinute;
    public double Revenue, StockCogs, Wages, Rent, DeliveryAllocation, FuelConsumedLitres, FuelConsumedCost, FuelPurchasedLitres, FuelPurchasedCash;
    public double WageHours = 24, ExtraWages = 0, RealizedOperatingBalance;
    public double? OperatingProfit, ProfitDeltaVsLegacy;
    public int? OpenMinutesDeltaVsLegacy, SalesMinutesDeltaVsLegacy, LunchClosedMinutesDeltaVsLegacy;
    public double StartingFuelLitres, FinalFuelLitres, ForecastDayFuelLitres;
    public int OpenMinutes, SalesMinutes, ClosedMinutes, LunchClosedMinutes, DelayedStops, SkippedStops;
    public int TravelMinutes, TravelWaitMinutes, LoadingMinutes, RefuelServiceMinutes, RefuelLoopMinutes, RefuelLoopWindowOverlapMinutes;
    public double StraightMetres, AssumedRoadMetres;
    public Dictionary<string, double> StartingStock = new(), LoadedStock = new(), EndingStock = new();
    public List<string> SoldOutItems = new();
    public List<CrewVisit> Stops = new();
    public List<CrewTrip> Trips = new();
    public List<CrewRefuel> Refuels = new();
}
internal sealed class CrewWeek
{
    public string Route = "", Scenario = "", Alternative = "", Outcome = "";
    public string SinglePitch = "";
    public List<CrewDay> Days = new();
    public double Revenue, StockCogs, Wages, Rent, DeliveryAllocation, FuelConsumedLitres, FuelConsumedCost, FuelPurchasedLitres, FuelPurchasedCash;
    public double WageHours, ExtraWages, RealizedOperatingBalance;
    public double? OperatingProfit, DailyMeanOperatingProfit, ProfitDeltaVsLegacy, OpenMinutesDeltaVsLegacy, SalesMinutesDeltaVsLegacy, LunchClosedMinutesDeltaVsLegacy;
    public double StartingFuelLitres, FinalFuelLitres;
    public int OpenMinutes, SalesMinutes, ClosedMinutes, LunchClosedMinutes, DelayedStops, SkippedStops, CompletedHomeReturns;
    public int TravelMinutes, TravelWaitMinutes, LoadingMinutes, RefuelServiceMinutes, RefuelLoopMinutes, RefuelLoopWindowOverlapMinutes;
    public bool ScheduledRouteFeasible;
    public double StraightMetres, AssumedRoadMetres, PotentialMissedDemand, LunchPotentialMissedDemand;
    public Dictionary<string, int> SoldOutItemDays = new();
    public Dictionary<string, double> TargetStock = new(), FinalStock = new(), UnitsSold = new();
}

internal sealed class CrewEconomics
{
    private const string BaselineLabel = "Legacy travel timing under the proposed fixed-shift/return-reserve policy";
    private readonly List<string> selectionOmissions = new();
    private const int ShiftOpen = 420, ShiftClose = 1140, LunchOpen = 690, LunchClose = 870;
    private readonly Options opts;
    private readonly RuleTables rules;
    private readonly City city;
    private readonly Cart cart;
    private readonly CrewAssumptions config;
    private readonly Dictionary<string, double> target;
    private readonly double wage, rent, fee;
    private readonly List<Spot> spots;
    private static readonly DayOfWeek[] Weekdays = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    private static readonly string[] Menu = { "ba:itemname_hotdog", "ba:itemname_cupofcoffee", "ba:itemname_croissant", "ba:itemname_sodacan" };
    internal CrewEconomics(Options opts, DemandTables tables, RuleTables rules, GamePrices prices, City city)
    {
        this.opts = opts; this.rules = rules; this.city = city;
        config = CrewAssumptions.Load(opts.CrewEconomicsConfig);
        if (!double.IsFinite(opts.CrewSkill) || opts.CrewSkill is < 0 or > 100 || !double.IsFinite(opts.CrewSatisfaction) || opts.CrewSatisfaction is < 0 or > 100 ||
            !double.IsFinite(opts.Market) || opts.Market is < 0 or > 100 || !double.IsFinite(opts.PriceFactor) || opts.PriceFactor <= 0 ||
            !double.IsFinite(opts.DeliveriesPerWeek) || opts.DeliveriesPerWeek < 0 || !double.IsFinite(opts.FeeSharing) || opts.FeeSharing < 1)
            throw new ArgumentException("Crew skill/satisfaction and market must be 0–100; price factor positive; deliveries nonnegative; fee sharing >=1; all finite.");
        var speed = rules.Service.Speed(ServiceRules.EffectiveSkill(opts.CrewSkill, opts.CrewSatisfaction));
        cart = new Cart(tables, prices, Menu, "crew truck: hot dog/coffee/croissant/soda", kind: "truck", servers: 2, speed: speed);
        if (cart.Staff < 2 || cart.Servers != 2 || cart.PieceOf.Values.Distinct().Count() != 4)
            throw new ArgumentException("Crew economics requires a two-server truck and four separate serving pieces.");
        // One item per piece: fixed full-piece target, never forecast loading by scenario or pitch.
        target = Menu.ToDictionary(i => i, i => cart.PerPiece / cart.WeightOf(i), StringComparer.Ordinal);
        wage = 16 * (1 + Math.Pow(opts.CrewSkill, 1.05) / 100);
        rent = rules.Commissary.DailyRentCents("truck") / 100.0;
        fee = rules.Commissary.DeliveryFeeCents / 100.0 * opts.DeliveriesPerWeek / 7 / opts.FeeSharing;
        spots = city.Spots(tables); // Only actual city building anchors; never archetypes.
    }
    private Conditions On(DayOfWeek day) => new() { Day = day, Month = 5, Rain = false, MarketDemand = opts.Market, Pricing = opts.Pricing, PriceFactor = opts.PriceFactor };
    private CrewScenario Legacy => new("legacy", true, 0, 0, 0, 0, FuelEnabled: false);
    private double LegacyEquivalentSpeed => config.RoadFactor / rules.Fleet.DriveMinutesPerMetre / rules.Fleet.RoadFactor;
    private CrewScenario EquivalentControl => new("legacy-equivalent-control", false, LegacyEquivalentSpeed, 0, 0, 0, FuelEnabled: false);
    private static bool UsesFuel(CrewScenario s) => !s.Legacy && s.FuelEnabled;
    private CrewScenario Normal => new("normal", false, config.NormalSpeedMetresPerSecond, config.NormalWaitGameMinutesPerLeg, config.LitresPerKilometre, config.TankLitres);
    private CrewScenario Heavy => new("heavy", false, config.HeavySpeedMetresPerSecond, config.HeavyWaitGameMinutesPerLeg, config.LitresPerKilometre, config.TankLitres);
    private CrewLeg Leg(double straight, CrewScenario s)
    {
        if (straight <= 1e-9) return new(0, 0, 0, 0, 0);
        if (s.Legacy) return new(straight, straight * rules.Fleet.RoadFactor,
            TimerTravel.Minutes(rules.Fleet, straight, TravelMode.Drive), 0, 0);
        var road = straight * config.RoadFactor;
        // 1 real second == 1 game minute, so NO multiplication/division by 60.
        return new(straight, road, checked((int)Math.Ceiling(road / s.SpeedMetresPerSecond - 1e-9)) + s.WaitGameMinutesPerLeg,
            s.WaitGameMinutesPerLeg, UsesFuel(s) ? road / 1000 * s.LitresPerKilometre + s.WaitGameMinutesPerLeg * config.IdleLitresPerGameMinute : 0);
    }
    private CrewLeg Leg(Spot from, Spot to, CrewScenario s) => Leg(from.Distance(to), s);
    private static int Overlap(int a, int b, int x, int y) => Math.Max(0, Math.Min(b, y) - Math.Max(a, x));
    private double Demand(Spot pitch, int from, int to, Conditions c)
    {
        // Full menu reference demand: opportunity lost to closure, not a promise of stock/capacity-constrained sales.
        var stock = Selling.Unlimited(cart);
        double demand = 0;
        for (var t = from; t < to; t += 5) demand += cart.Plan(pitch, t, c, stock).Demand * Math.Min(5, to - t) / 60;
        return demand;
    }
    private List<CrewRoute> SelectRoutes()
    {
        var commissaries = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(opts.Data, "commissaries.json")))["commissaries"]!;
        var routes = new List<CrewRoute>();
        foreach (var hood in config.Districts)
        {
            var ranked = spots.Where(s => s.Hood == hood).Select(s => (Spot: s, Score: Demand(s, 480, 1080, On(DayOfWeek.Wednesday))))
                .OrderBy(p => p.Score).ThenBy(p => p.Spot.Name, StringComparer.Ordinal).ToList();
            if (ranked.Count < 4) throw new ArgumentException($"Too few actual city spots in {hood}.");
            var depotEntry = commissaries.FirstOrDefault(c => city.Buildings.Any(b => b.Key == (string?)c["address"] && b.Hood == hood))
                ?? throw new ArgumentException($"No commissary in {hood}.");
            var b = city.Buildings.Single(b => b.Key == (string)depotEntry["address"]!);
            // city.json uses scaled regional map frames (make_city.py), NOT native world coordinates.
            // Never mix its anchors with commissaries.json's measured native bay x/z.
            var depot = new Spot(b.Address + " commissary", AreaMix.Single("industrial"), b.Hood, b.Region, b.X, b.Z);
            var source = "city.json regional map building anchor; native bay coordinates excluded (different frame), bay offset unknown";
            var contribution = ranked.Select(p => p.Spot).SelectMany(p => new[] { (From: 690, To: 870), (From: 870, To: 1080) }
                .Select(window => (Key: (p, window.From, window.To), Value: Selling.Run(cart, p, window.From, window.To, On(DayOfWeek.Wednesday), new(target)).Profit)))
                .ToDictionary(p => p.Key, p => p.Value);
            foreach (var q in config.DemandQuantiles)
            {
                var anchor = ranked[(int)Math.Floor(q * (ranked.Count - 1))];
                foreach (var length in new[] { "local", "long" })
                {
                    var band = length == "local" ? config.ShortRouteTotalStraightMetresBand : config.LongRouteTotalStraightMetresBand;
                    var pool = ranked.Select(p => p.Spot).Where(s => s.Region == anchor.Spot.Region && s != anchor.Spot).ToList();
                    // Purposeful bounded heuristic: baseline nominal-window contribution per pitch, scored with
                    // a common fresh target. Actual route simulation still carries finite stock across all stops.
                    var candidates = (from second in pool from third in pool where third != second
                        let distance = depot.Distance(anchor.Spot) + anchor.Spot.Distance(second) + second.Distance(third) + third.Distance(depot)
                        where distance >= band[0] && distance < band[1]
                        select new { Second = second, Third = third, Distance = distance,
                            Score = contribution[(second, 690, 870)] + contribution[(third, 870, 1080)] })
                        .OrderByDescending(p => p.Score).ThenBy(p => p.Distance)
                        .ThenBy(p => p.Second.Name, StringComparer.Ordinal).ThenBy(p => p.Third.Name, StringComparer.Ordinal).FirstOrDefault();
                    if (candidates == null)
                    {
                        selectionOmissions.Add($"{hood} q{q:R} {length}: no distinct three-pitch candidate in total straight-distance band [{band[0]}, {band[1]}) m.");
                        continue;
                    }
                    routes.Add(new CrewRoute($"{hood.Replace("ba:neighborhood_", "")}-q{q:R}-{length}", hood, q, anchor.Score, length, depot, source,
                        new() { new(anchor.Spot, 480, 690), new(candidates.Second, 690, 870), new(candidates.Third, 870, 1080) }, band, candidates.Score));
                }
            }
        }
        if (routes.Count == 0) throw new ArgumentException("No routes fit the configured distance bands: " + string.Join("; ", selectionOmissions));
        return routes;
    }
    private static double RouteDistance(CrewRoute route) => route.Depot.Distance(route.Stops[0].Pitch) +
        route.Stops.Zip(route.Stops.Skip(1), (a, b) => a.Pitch.Distance(b.Pitch)).Sum() + route.Stops[^1].Pitch.Distance(route.Depot);
    private static CrewRoute Stationary(CrewRoute route, Spot pitch) => route with { Stops = new() { new(pitch, 480, 1080) } };
    private Spot SelectSingle(CrewRoute route) => route.Stops.Select(st => st.Pitch).Distinct()
        .Select(s => (Spot: s, Week: Simulate(Stationary(route, s), Legacy, "single", s.Name)))
        .OrderByDescending(p => p.Week.OperatingProfit ?? double.NegativeInfinity).ThenBy(p => p.Spot.Name, StringComparer.Ordinal).First().Spot;

    private CrewWeek Simulate(CrewRoute route, CrewScenario scenario, string alternative, string single = "")
    {
        var w = new CrewWeek { Route = route.Id, Scenario = scenario.Id, Alternative = alternative, SinglePitch = single,
            TargetStock = new(target), StartingFuelLitres = scenario.StartingLitres };
        var stock = Menu.ToDictionary(i => i, _ => 0.0, StringComparer.Ordinal); // Empty on Monday, no daily reset.
        var fuel = scenario.StartingLitres;
        var position = route.Depot;
        foreach (var weekday in Weekdays)
        {
            var c = On(weekday);
            var d = new CrewDay { Date = new DateOnly(2026, 5, 4).AddDays(w.Days.Count).ToString("yyyy-MM-dd"), Weekday = weekday.ToString(), StartingFuelLitres = fuel, StartingStock = new(stock),
                Wages = wage * 24, Rent = rent, DeliveryAllocation = fee };
            w.Days.Add(d);
            var time = ShiftOpen;
            bool failed = false;
            void Fail(string reason) { d.Outcome = reason; d.ScheduledRouteFeasible = false; failed = true; }
            bool Move(Spot to, string purpose)
            {
                var leg = Leg(position, to, scenario);
                if (time + leg.Minutes > ShiftClose || fuel + 1e-8 < leg.FuelLitres)
                { Fail("InfeasibleTravelOrFuel"); return false; }
                d.Trips.Add(new(purpose, position.Name, to.Name, time, time + leg.Minutes, leg));
                time += leg.Minutes; fuel -= leg.FuelLitres;
                d.FuelConsumedLitres += leg.FuelLitres; d.TravelMinutes += leg.Minutes; d.TravelWaitMinutes += leg.WaitMinutes;
                d.StraightMetres += leg.StraightMetres; d.AssumedRoadMetres += leg.AssumedRoadMetres;
                position = to;
                return true;
            }
            // No recovery teleport: a failure to return on an earlier day prevents bay loading/departure.
            if (position.Distance(route.Depot) > 1e-6) Fail("InfeasibleNotAtDepot");
            if (!failed)
            {
                foreach (var item in Menu)
                {
                    var load = Math.Max(0, target[item] - stock[item]);
                    stock[item] += load; d.LoadedStock[item] = load;
                }
                d.LoadingMinutes = config.LoadingGameMinutes;
                time += d.LoadingMinutes;
                var forecastPosition = route.Depot;
                foreach (var stop in route.Stops)
                {
                    d.ForecastDayFuelLitres += Leg(forecastPosition, stop.Pitch, scenario).FuelLitres;
                    forecastPosition = stop.Pitch;
                }
                d.ForecastDayFuelLitres += Leg(forecastPosition, route.Depot, scenario).FuelLitres;
                // At most one declared station loop per morning. Engines off while loading, selling and filling.
                if (UsesFuel(scenario) && fuel + 1e-8 < d.ForecastDayFuelLitres + config.ReserveLitres)
                {
                    var stationLeg = Leg(config.StationOneWayStraightMetres, scenario);
                    if (stationLeg.FuelLitres > fuel + 1e-8) Fail("InfeasibleStationUnreachable");
                    else if (d.ForecastDayFuelLitres + config.ReserveLitres + stationLeg.FuelLitres > config.TankLitres + 1e-8)
                        Fail("InfeasibleTankForForecastAndReserve");
                    else if (time + stationLeg.Minutes * 2 + config.StationServiceGameMinutes > ShiftClose)
                        Fail("InfeasibleStationShift");
                    else
                    {
                        // Synthetic station displaced exactly the declared straight distance; no real access claim.
                        var station = new Spot("synthetic station loop", route.Depot.Areas, route.Depot.Hood, route.Depot.Region,
                            route.Depot.X + config.StationOneWayStraightMetres, route.Depot.Z);
                        var loopStart = time;
                        if (Move(station, "station-out"))
                        {
                            var arrival = fuel;
                            var purchased = config.TankLitres - fuel;
                            d.Refuels.Add(new(time, time + config.StationServiceGameMinutes, arrival, purchased, config.TankLitres));
                            fuel += purchased; d.FuelPurchasedLitres += purchased;
                            time += config.StationServiceGameMinutes; d.RefuelServiceMinutes += config.StationServiceGameMinutes;
                            Move(route.Depot, "station-back");
                            d.RefuelLoopMinutes += time - loopStart;
                            d.RefuelLoopWindowOverlapMinutes += Overlap(loopStart, time, 480, 1080);
                        }
                    }
                }
            }
            foreach (var stop in route.Stops)
            {
                var visit = new CrewVisit { Pitch = stop.Pitch.Name, WindowOpen = stop.Open, WindowClose = stop.Close, StockBefore = new(stock) };
                d.Stops.Add(visit);
                var leg = Leg(position, stop.Pitch, scenario);
                var home = Leg(stop.Pitch, route.Depot, scenario);
                var serviceFrom = Math.Max(time + leg.Minutes, stop.Open);
                var serviceTo = Math.Min(stop.Close, ShiftClose - home.Minutes);
                if (failed || serviceFrom >= serviceTo || fuel + 1e-8 < leg.FuelLitres + home.FuelLitres + (UsesFuel(scenario) ? config.ReserveLitres : 0))
                {
                    visit.Outcome = failed ? d.Outcome : serviceFrom >= serviceTo ? "SkippedWindowOrReturnShift" : "SkippedReturnFuelReserve";
                    d.SkippedStops++; d.ScheduledRouteFeasible = false;
                }
                else if (Move(stop.Pitch, "pitch"))
                {
                    visit.Arrive = time; visit.ServiceFrom = serviceFrom; visit.ServiceTo = serviceTo;
                    time = serviceFrom;
                    visit.Outcome = serviceFrom > stop.Open || serviceTo < stop.Close ? "Trimmed" : "OnTime";
                    if (visit.Outcome == "Trimmed") d.DelayedStops++;
                    d.OpenMinutes += serviceTo - serviceFrom;
                    visit.Sales = Selling.Run(cart, stop.Pitch, serviceFrom, serviceTo, c, stock);
                    d.SalesMinutes += (int)visit.Sales.ByDaypart.Values.Sum(p => p.Minutes);
                    d.Revenue += visit.Sales.Revenue; d.StockCogs += visit.Sales.Cost;
                    time = serviceTo; // Stay to fixed close even if sold out; explicit policy, no opportunistic replan.
                }
                visit.StockAfter = new(stock);
                var open = visit.ServiceFrom ?? stop.Close;
                var close = visit.ServiceTo ?? stop.Close;
                d.ClosedMinutes += stop.Close - stop.Open - (close - open);
                d.LunchClosedMinutes += Overlap(stop.Open, stop.Close, LunchOpen, LunchClose) - Overlap(open, close, LunchOpen, LunchClose);
                visit.PotentialMissedDemand = Demand(stop.Pitch, stop.Open, open, c) + Demand(stop.Pitch, close, stop.Close, c);
                visit.LunchPotentialMissedDemand = Demand(stop.Pitch, Math.Max(stop.Open, LunchOpen), Math.Min(open, LunchClose), c) +
                    Demand(stop.Pitch, Math.Max(close, LunchOpen), Math.Min(stop.Close, LunchClose), c);
            }
            if (!failed) Move(route.Depot, "home");
            d.CompletedHomeReturn = position.Distance(route.Depot) < 1e-6 && time <= ShiftClose && !failed;
            d.HomeMinute = d.CompletedHomeReturn ? time : null;
            if (!failed && d.OpenMinutes == 0) Fail("InfeasibleNoServiceWithinShift");
            if (!failed && d.SkippedStops > 0) d.Outcome = "CompletedHomeWithSkippedStops";
            d.FinalFuelLitres = fuel; d.EndingStock = new(stock);
            d.SoldOutItems = FindSoldOutItems(d);
            d.FuelConsumedCost = d.FuelConsumedLitres * config.FuelDollarsPerLitre;
            d.FuelPurchasedCash = d.FuelPurchasedLitres * config.FuelDollarsPerLitre;
            // Opening fuel is inventory valued at the same reference price; purchase cash is not charged twice.
            d.RealizedOperatingBalance = d.Revenue - d.StockCogs - d.Wages - d.Rent - d.DeliveryAllocation - d.FuelConsumedCost;
            if (!failed && d.CompletedHomeReturn) d.OperatingProfit = d.RealizedOperatingBalance;
        }
        w.FinalStock = new(stock); w.FinalFuelLitres = fuel;
        w.RealizedOperatingBalance = w.Days.Sum(d => d.RealizedOperatingBalance);
        w.RefuelLoopMinutes = w.Days.Sum(d => d.RefuelLoopMinutes);
        w.RefuelLoopWindowOverlapMinutes = w.Days.Sum(d => d.RefuelLoopWindowOverlapMinutes);
        w.Revenue = w.Days.Sum(d => d.Revenue); w.StockCogs = w.Days.Sum(d => d.StockCogs);
        w.Wages = w.Days.Sum(d => d.Wages); w.WageHours = w.Days.Sum(d => d.WageHours); w.ExtraWages = 0;
        w.Rent = w.Days.Sum(d => d.Rent); w.DeliveryAllocation = w.Days.Sum(d => d.DeliveryAllocation);
        w.FuelConsumedLitres = w.Days.Sum(d => d.FuelConsumedLitres); w.FuelConsumedCost = w.Days.Sum(d => d.FuelConsumedCost);
        w.FuelPurchasedLitres = w.Days.Sum(d => d.FuelPurchasedLitres); w.FuelPurchasedCash = w.Days.Sum(d => d.FuelPurchasedCash);
        w.OpenMinutes = w.Days.Sum(d => d.OpenMinutes); w.SalesMinutes = w.Days.Sum(d => d.SalesMinutes);
        w.ClosedMinutes = w.Days.Sum(d => d.ClosedMinutes); w.LunchClosedMinutes = w.Days.Sum(d => d.LunchClosedMinutes);
        w.DelayedStops = w.Days.Sum(d => d.DelayedStops); w.SkippedStops = w.Days.Sum(d => d.SkippedStops);
        w.TravelMinutes = w.Days.Sum(d => d.TravelMinutes); w.TravelWaitMinutes = w.Days.Sum(d => d.TravelWaitMinutes);
        w.LoadingMinutes = w.Days.Sum(d => d.LoadingMinutes); w.RefuelServiceMinutes = w.Days.Sum(d => d.RefuelServiceMinutes);
        w.CompletedHomeReturns = w.Days.Count(d => d.CompletedHomeReturn);
        w.ScheduledRouteFeasible = w.Days.All(d => d.ScheduledRouteFeasible);
        w.StraightMetres = w.Days.Sum(d => d.StraightMetres); w.AssumedRoadMetres = w.Days.Sum(d => d.AssumedRoadMetres);
        w.PotentialMissedDemand = w.Days.Sum(d => d.Stops.Sum(v => v.PotentialMissedDemand));
        w.LunchPotentialMissedDemand = w.Days.Sum(d => d.Stops.Sum(v => v.LunchPotentialMissedDemand));
        foreach (var item in Menu)
        {
            w.UnitsSold[item] = w.Days.Sum(d => d.Stops.Sum(v => v.Sales.Sold.GetValueOrDefault(item)));
            w.SoldOutItemDays[item] = w.Days.Count(d => d.SoldOutItems.Contains(item));
        }
        w.Outcome = w.Days.Any(d => d.OperatingProfit == null) ? "Infeasible (see daily outcomes; no ordinary weekly profit)"
            : w.ScheduledRouteFeasible ? "CompletedHome" : "CompletedHomeWithSkippedStops (reduced operation; schedule infeasible)";
        if (w.Days.All(d => d.OperatingProfit.HasValue))
        { w.OperatingProfit = w.Days.Sum(d => d.OperatingProfit!.Value); w.DailyMeanOperatingProfit = w.OperatingProfit / 7; }
        return w;
    }

    private static void Deltas(CrewWeek w, CrewWeek baseline)
    {
        w.ProfitDeltaVsLegacy = w.OperatingProfit - baseline.OperatingProfit;
        w.OpenMinutesDeltaVsLegacy = w.OpenMinutes - baseline.OpenMinutes;
        w.SalesMinutesDeltaVsLegacy = w.SalesMinutes - baseline.SalesMinutes;
        w.LunchClosedMinutesDeltaVsLegacy = w.LunchClosedMinutes - baseline.LunchClosedMinutes;
        for (var day = 0; day < w.Days.Count; day++)
        {
            var d = w.Days[day]; var b = baseline.Days[day];
            d.ProfitDeltaVsLegacy = d.OperatingProfit - b.OperatingProfit;
            d.OpenMinutesDeltaVsLegacy = d.OpenMinutes - b.OpenMinutes;
            d.SalesMinutesDeltaVsLegacy = d.SalesMinutes - b.SalesMinutes;
            d.LunchClosedMinutesDeltaVsLegacy = d.LunchClosedMinutes - b.LunchClosedMinutes;
        }
    }

    internal (string Markdown, JObject Json) Report(bool study, bool checks)
    {
        var routes = SelectRoutes();
        var singles = routes.ToDictionary(r => r.Id, SelectSingle, StringComparer.Ordinal);
        var checkResults = checks ? Checks(routes, singles) : new List<string>();
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() });
        JToken Token(object value) => JToken.FromObject(value, serializer);
        JObject ScenarioJson(CrewScenario scenario)
        {
            var item = (JObject)Token(scenario);
            item["gameMinutesPerStraightKilometreExcludingWait"] = scenario.Legacy ? 1000 * rules.Fleet.RoadFactor * rules.Fleet.DriveMinutesPerMetre : 1000 * config.RoadFactor / scenario.SpeedMetresPerSecond;
            item["interpretation"] = scenario.Legacy ? BaselineLabel : scenario.SpeedMetresPerSecond > 13.9 ? "Mathematical time-compression control, not an attainable truck setting" : scenario.SpeedMetresPerSecond <= 2 ? "Diagnostic probe-test speed limit, not an economically acceptable production target" : "Illustrative uncalibrated physical-speed assumption";
            return item;
        }
        var json = new JObject
        {
            ["schemaVersion"] = 2,
            ["baselineLabel"] = BaselineLabel,
            ["assumptions"] = Token(config),
            ["inputs"] = new JObject
            {
                ["pricing"] = opts.Pricing.ToString(), ["priceFactor"] = opts.PriceFactor, ["marketDemandFallback"] = opts.Market,
                ["crewSkill"] = opts.CrewSkill, ["crewSatisfaction"] = opts.CrewSatisfaction, ["crewServiceSpeed"] = cart.Speed,
                ["hourlyWagePerEmployee"] = wage, ["service"] = opts.Queue ? "queue expected throughput" : "fluid expected throughput",
                ["deliveriesPerWeek"] = opts.DeliveriesPerWeek, ["feeSharing"] = opts.FeeSharing,
                ["menu"] = new JArray(Menu), ["targetStock"] = Token(target), ["pieceOf"] = Token(cart.PieceOf), ["perPieceWeightedCapacity"] = cart.PerPiece,
                ["legacyFleetRules"] = Token(rules.Fleet), ["dailyRent"] = rent, ["dailyDeliveryAllocation"] = fee,
                ["inputSha256"] = Hashes()
            },
            ["policy"] = new JObject
            {
                ["calendar"] = "2026-05-04 Monday through 2026-05-10 Sunday, illustrative dry May week, same route each day; default market fallback 100, CLI override and game-price per-item market supported",
                ["shift"] = "Two employees each 07:00–19:00, fixed 12 hours/day each, 168 employee-hours/week, extra wages zero. Wage formula as pass report but fixed shifts differ from Core AutoSchedule/pay in pass; do not directly compare those profits. Not a payroll forecast for other modpacks.",
                ["dispatch"] = "Load at depot 07:00. Depart each pitch only at its fixed close (even sold out); wait if early; late arrival trims service. Reserve direct home travel time before service, and direct home fuel plus reserve before dispatch. Skip a stop if no positive service fits or return fuel fails. Return after last window. No roadside food restock, no overtime, teleport or daily state reset.",
                ["fuel"] = "Pre-departure full planned day forecast plus reserve triggers at most one synthetic station loop per morning. Must reach station on existing fuel; fill actual empty capacity there, engine off during service, then return to depot. Full tank must cover station-back, full day forecast and reserve, else explicit infeasible. Engine idles only during per-leg lights/parking waits, off during selling/loading/filling. Fuel inventory carried across week; consumption expensed at reference price incl initial tank; purchases reported as cash separately. Legacy uses no fuel.",
                ["geometry"] = "Actual city building and commissary address map anchors, all in the same regional map frame. Native bay coordinates are excluded because they use a different frame; bay offset is unknown. Straight-line × assumed road factor is not a measured road route. Synthetic station has no claimed real access/reservation. Cross-region travel never selected. Prior map-scale study suggests distances may be off 15–35%; this is not a confidence interval or native route calibration.",
                ["selection"] = "Rank actual district spots by Wednesday 08:00–18:00 full-menu unconstrained demand integral; floor(q*(N-1)), tie ordinal address. That fixes the first pitch only. For each configured short/local or long total straight-distance band [minimum, maximum), enumerate distinct second/third pitches in the same district/region. Select maximum summed Wednesday nominal-window stock contribution (revenue minus sold COGS) at 11:30–14:30 and 14:30–18:00, each scored separately from the common full stock target; ties shorter total distance, then ordinal second/third address. This purposeful bounded heuristic favours downstream contributions but ignores stock carryover/travel in its selection score; actual economics models both. Bands include depot outward and final return. Missing candidate bands are reported, never silently replaced. Fixed adjacent windows 08:00–11:30, 11:30–14:30, 14:30–18:00 have zero transfer slack, so every move consumes the next window (negative baseline transition slack emitted). No traffic-adaptive retiming. Matched stay: greatest legacy-policy weekly operating profit among the three pitches at continuous 08:00–18:00; tie ordinal address. All selection is baseline only and frozen before physical results. Illustrative cases with selection bias, not representative player routes or global optima.",
                ["supply"] = "Start empty at depot on Monday; morning top-up to common full per-piece target, retain leftovers. Sufficient commissary stock before 07:00 assumed (carryover/logistics), no daily order-arrival simulation. COGS only on sold units. No spoilage, regulars, health, competition, pitch fees or sales tax. Inventory valued consistently at supplied wholesale prices.",
                ["profitScope"] = "Pre-tax operating profit after sold stock COGS, fixed wages, commissary rent, allocated delivery and consumed fuel; excludes capex/depreciation, financing, licensing/permits, maintenance, damage, fines and personal labour. Realized operating balance always retains actual revenue and incurred costs, including wages on zero-sales/infeasible days; it is not a completed-route profitability claim. Infeasible home returns/no-service days give null ordinary profit; skipped-stop home completions explicitly denote reduced operation with infeasible original schedule.",
                ["metrics"] = "Open minutes are permitted service interval even if stock sells out; sales minutes are Selling.Run stocked simulation minutes (5-minute expected-rate steps), not a count of busy cashier time. Closed minutes are scheduled window minus open time. Lunch loss uses 11:30–14:30. Missed demand is unconstrained full-menu demand in closed intervals at their actual clock times, not guaranteed lost sales. SoldOutItems counts only items with positive opening/loaded stock, positive actual sales and ending stock <= 1e-6; an empty unserved item is not exhaustion. SoldOutItemDays counts such days by item over the week. Deltas compare the same alternative to the stated legacy-policy baseline. Opportunity loss is explanatory and is never expensed again. Refuel loop overlap is raw overlap with scheduled 08:00–18:00, not a causal estimate of additional lost sales."
            },
            ["caveats"] = "No representative timing/fuel calibration. 1 real second = 1 game minute is an assumed code-based scale, not verified here against the native clock. Excludes actual road graph, parking/access failures, stochastic traffic/jams, regulars/reputation and health/competition feedback, and service after 18:00 including nightlife. Mild 3/10-minute per-leg waits and 15-minute station service are hypothetical (3/10/15 real seconds under the assumed scale), not typical traffic/filling estimates. Fixed windows and no adaptive retiming can overstate losses versus a player who adapts; only the matched stay models adaptation. Selection score can bias results and is not an optimized finite-stock route.",
            ["selectionOmissions"] = new JArray(selectionOmissions),
            ["routes"] = new JArray(routes.Select(r => new JObject
            {
                ["id"] = r.Id, ["district"] = r.District, ["region"] = r.Depot.Region, ["demandQuantile"] = r.DemandQuantile,
                ["anchorDemandScoreWednesday"] = r.AnchorDemandScore, ["lengthClass"] = r.LengthClass,
                ["totalStraightMetresBand"] = Token(r.TotalStraightMetresBand), ["totalStraightMetres"] = RouteDistance(r),
                ["downstreamNominalWindowContributionScore"] = r.DownstreamWindowContributionScore,
                ["baselineTransitionSlackMinutes"] = new JArray(r.Stops.Zip(r.Stops.Skip(1), (a, b) => b.Open - a.Close - Leg(a.Pitch, b.Pitch, Legacy).Minutes)),
                ["depot"] = Point(r.Depot), ["depotCoordinateSource"] = r.DepotCoordinateSource,
                ["stops"] = new JArray(r.Stops.Select(s => new JObject { ["pitch"] = Point(s.Pitch), ["openMinute"] = s.Open, ["closeMinute"] = s.Close })),
                ["movingLegs"] = LegsJson(r), ["matchedSinglePitch"] = Point(singles[r.Id]),
                ["singleLegs"] = LegsJson(Stationary(r, singles[r.Id]))
            })),
            ["checks"] = new JArray(checkResults)
        };
        var o = new StringBuilder("# Crew-route economics sensitivity study\n\n");
        o.AppendLine($"Schema 2. Pricing **{opts.Pricing}**, factor {opts.PriceFactor}; market fallback {opts.Market}; crew skill/satisfaction {opts.CrewSkill}/{opts.CrewSatisfaction}, service speed {cart.Speed:0.000}, hourly wage ${wage:0.00} per employee. 2026-05-04 Monday through 2026-05-10 Sunday, illustrative dry May week. Results are deterministic expected sales, not native traffic observations.");
        o.AppendLine();
        o.AppendLine(config.AssumptionStatus);
        o.AppendLine();
        o.AppendLine($"Baseline: **{BaselineLabel}**. Only the travel timer is current Core behavior; these are not current shipping business outcomes. All scenarios use the same proposed fixed shifts, loading and home-reserve policy. Fixed paid hours differ from AutoSchedule/pass-report hours. Loading defaults to {config.LoadingGameMinutes} game minutes (current Core restock setting {rules.Fleet.RestockMinutes}).");
        o.AppendLine();
        o.AppendLine((string)json["caveats"]!);
        o.AppendLine();
        o.AppendLine($"Physical normal {config.NormalSpeedMetresPerSecond} m/s + {config.NormalWaitGameMinutesPerLeg} game min/leg; heavy {config.HeavySpeedMetresPerSecond} m/s + {config.HeavyWaitGameMinutesPerLeg} min/leg; road factor {config.RoadFactor}. **1 real second = 1 game minute**; travel = ceil(assumed road metres / m/s) + wait, without a factor of 60. Roads are straight lines × an assumption, not measured routes. Legacy calls Core.Fleet.Travel.Minutes with current shipped Fleet rules exactly, with no fuel.");
        o.AppendLine();
        o.AppendLine($"Tank {config.TankLitres} L, reserve {config.ReserveLitres} L, {config.LitresPerKilometre} L/km, idle {config.IdleLitresPerGameMinute} L/game minute during leg waits only. Fuel reference ${config.FuelDollarsPerLitre:0.00}/L is assumed, **not a verified current modpack price**. Ordinary cases start full; separately labelled stress cases start at {config.StressStartingLitres} L. Fuel/stock/position carry through the week. Loading takes {config.LoadingGameMinutes} game minutes each morning; Monday starts empty at the depot. Sufficient stock there before 07:00 is assumed. Common target: {string.Join(", ", target.Select(p => $"{p.Value:0.##} {p.Key}"))}, {cart.PerPiece} weighted units per serving piece; no Extra Storage or roadside restock.");
        o.AppendLine();
        o.AppendLine($"Proposed policy: two fixed 07:00–19:00 shifts (24 employee-hours/day, 168/week); wages equal across scenarios, extra wages **$0**. Windows close at fixed times, 18:00 latest. Reserve home time before serving and home fuel plus reserve before dispatch; skip stops that cannot fit. Stay until fixed close even if sold out, then move. No overtime or recovery teleport. Forecast the full day plus reserve each morning; if short, take one synthetic station loop ({config.StationOneWayStraightMetres} straight metres each way, road factor and scenario traffic/fuel applied, {config.StationServiceGameMinutes} min filling with engine off). Reach it on existing fuel, buy only actual empty capacity, then return; inadequate reachability/tank/shift is explicit infeasible. No speculative mid-day station search.");
        o.AppendLine();
        o.AppendLine($"Profit is **pre-tax operating profit**, excluding capex/depreciation, financing, permits/licences, maintenance, damage and fines. Sold-stock COGS is charged once. Consumed fuel includes use of the valued initial tank; cash purchases are reported separately and are not expensed again. Daily rent ${rent:0.00}, delivery allocation ${fee:0.00} ({opts.DeliveriesPerWeek} deliveries/week shared by {opts.FeeSharing}). No spoilage, health/regulars/competition feedback, premium pitch fees or order funding/arrival constraints. Fixed demand inputs and approximate city anchors limit the conclusion.");
        o.AppendLine();
        o.AppendLine("## Frozen route selection");
        o.AppendLine();
        o.AppendLine((string)json["policy"]!["selection"]!);
        o.AppendLine();
        o.AppendLine("Coordinates below are approximate regional map metres (x,z), as generated by make_city.py, not native world positions. Depot and pitches use the same map frame; explicit native bay coordinates are excluded and bay offsets are unknown; pitch anchors do not certify legal parking. Prior map-scale study suggests 15–35% distance uncertainty (not a confidence interval). Each distance includes morning outward travel and final return. Matched one-pitch choices come only from that moving route, never globally optimal.");
        o.AppendLine();
        o.AppendLine("| Route | Demand score | Depot and coordinates/source | 08–11:30 → 11:30–14:30 → 14:30–18 pitches (x,z) | Legs straight m (depot → stops → depot) | Assumed road m | Matched single 08–18; round trip straight m |");
        o.AppendLine("|---|---:|---|---|---|---:|---|");
        foreach (var r in routes)
        {
            var points = new[] { r.Depot }.Concat(r.Stops.Select(s => s.Pitch)).Append(r.Depot).ToArray();
            var lengths = points.Zip(points.Skip(1), (a, b) => a.Distance(b)).ToArray();
            o.AppendLine($"| {r.Id} | {r.AnchorDemandScore:0.00} | {Label(r.Depot)}; {r.DepotCoordinateSource} | {string.Join(" → ", r.Stops.Select(s => Label(s.Pitch)))} | {string.Join(" / ", lengths.Select(n => n.ToString("0.00")))} | {lengths.Sum() * config.RoadFactor:0.00} | {Label(singles[r.Id])}; {2 * r.Depot.Distance(singles[r.Id]):0.00} |");
        }
        o.AppendLine();
        o.AppendLine("| Route | Total straight-distance band m [min,max) | Actual straight m | Downstream nominal-window contribution score | Legacy transition slack min |");
        o.AppendLine("|---|---|---:|---:|---|");
        foreach (var r in routes) o.AppendLine($"| {r.Id} | [{r.TotalStraightMetresBand[0]}, {r.TotalStraightMetresBand[1]}) | {RouteDistance(r):0.00} | {r.DownstreamWindowContributionScore:0.00} | {string.Join(", ", r.Stops.Zip(r.Stops.Skip(1), (a, b) => b.Open - a.Close - Leg(a.Pitch, b.Pitch, Legacy).Minutes))} |");
        o.AppendLine();
        o.AppendLine(selectionOmissions.Count == 0 ? "Candidate omissions: none." : "Candidate omissions: " + string.Join("; ", selectionOmissions));
        o.AppendLine();
        o.AppendLine($"Exact legacy-equivalent physical control: {LegacyEquivalentSpeed:R} m/s = physical road factor / (legacy drive minutes/metre × legacy road factor), **zero waits and fuel disabled** (including idling/reserve/refuelling). This is a mathematical time-compression control, not an attainable truck setting. It must reproduce every legacy trip time and financial result. Legacy is {1000 * rules.Fleet.RoadFactor * rules.Fleet.DriveMinutesPerMetre:0.###} game minutes/straight km excluding ceiling. Normal/heavy are {1000 * config.RoadFactor / Normal.SpeedMetresPerSecond:0.###}/{1000 * config.RoadFactor / Heavy.SpeedMetresPerSecond:0.###}, plus per-leg waits.");
        o.AppendLine();
        var weeks = new List<CrewWeek>();
        var sensitivity = new List<CrewWeek>();
        if (study)
        {
            var scenarios = new[] { Legacy, EquivalentControl, Normal, Heavy,
                Normal with { Id = "stress-normal", StartingLitres = config.StressStartingLitres, Stress = true },
                Heavy with { Id = "stress-heavy", StartingLitres = config.StressStartingLitres, Stress = true } };
            foreach (var r in routes)
            {
                foreach (var s in scenarios)
                {
                    weeks.Add(Simulate(r, s, "moving"));
                    weeks.Add(Simulate(Stationary(r, singles[r.Id]), s, "single", singles[r.Id].Name));
                }
            }
            foreach (var w in weeks) Deltas(w, weeks.Single(b => b.Route == w.Route && b.Scenario == "legacy" && b.Alternative == w.Alternative));
            json["scenarios"] = new JArray(scenarios.Select(ScenarioJson));
            json["results"] = Token(weeks);
            o.AppendLine("## Weekly economics (daily mean in parentheses)"); o.AppendLine();
            o.AppendLine("All dollars are game dollars; costs and litres below are weekly. M = moving, S = matched single. Rows with skipped stops describe reduced operation; inspect status/JSON. An unfinished home return or wholly unserviceable day gives INFEASIBLE, not ordinary profit. Realized balance retains incurred losses even when ordinary profit is unavailable. JSON contains every actual weekday, stop service interval, stock boundary, trip and fill."); o.AppendLine();
            o.AppendLine("| Route | Case | M/S | Revenue | Stock COGS | Wages | Rent | Delivery | Fuel consumed L / cost | Fuel purchased L / cash | Weekly operating profit (daily mean) | Δ vs legacy | Realized balance incl. failures | Outcome |");
            o.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (var w in weeks) o.AppendLine($"| {w.Route} | {w.Scenario} | {Abbr(w)} | {Money(w.Revenue)} | {Money(w.StockCogs)} | {Money(w.Wages)} | {Money(w.Rent)} | {Money(w.DeliveryAllocation)} | {w.FuelConsumedLitres:0.00} / {Money(w.FuelConsumedCost)} | {w.FuelPurchasedLitres:0.00} / {Money(w.FuelPurchasedCash)} | {Money(w.OperatingProfit)} ({Money(w.DailyMeanOperatingProfit)}) | {Money(w.ProfitDeltaVsLegacy)} | {Money(w.RealizedOperatingBalance)} | {w.Outcome} |");
            o.AppendLine(); o.AppendLine("## Selling time, peaks and returns"); o.AppendLine();
            o.AppendLine("Weekly game minutes. Open is service permitted by schedule/arrival/home reservation, even if stock sold out. Sales is Selling.Run stocked simulation time, not busy-server minutes. Δ compares same alternative with legacy. Lunch closed is missed 11:30–14:30 time; potential demand is full-menu unconstrained customers during that closure, evaluated at actual clock time, not guaranteed lost sales. Travel includes lights/parking wait; loading and fill service are separate. Food-service time is open time. Sold-out item-days count only initially stocked/loaded items actually sold to zero (<=1e-6), not empty unserved items. Refuel loop total/08–18 overlap includes both station legs and filling, and is opportunity time, not an additional monetary expense or guaranteed lost sales. Daily demand follows actual weekday."); o.AppendLine();
            o.AppendLine("| Route | Case | M/S | Open / Δ | Sales / Δ | Closed | Lunch closed / Δ; potential demand missed | Delayed / skipped stops | Sold-out item-days | Travel (wait) | Load / fill service; loop total / 08–18 overlap | Home returns / 7 | Final fuel L |");
            o.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|");
            foreach (var w in weeks) o.AppendLine($"| {w.Route} | {w.Scenario} | {Abbr(w)} | {w.OpenMinutes} / {w.OpenMinutesDeltaVsLegacy:0} | {w.SalesMinutes} / {w.SalesMinutesDeltaVsLegacy:0} | {w.ClosedMinutes} | {w.LunchClosedMinutes} / {w.LunchClosedMinutesDeltaVsLegacy:0}; {w.LunchPotentialMissedDemand:0.0} | {w.DelayedStops} / {w.SkippedStops} | {ItemDays(w.SoldOutItemDays)} | {w.TravelMinutes} ({w.TravelWaitMinutes}) | {w.LoadingMinutes} / {w.RefuelServiceMinutes}; {w.RefuelLoopMinutes} / {w.RefuelLoopWindowOverlapMinutes} | {w.CompletedHomeReturns} | {w.FinalFuelLitres:0.00} |");
            o.AppendLine(); o.AppendLine("## Moving versus matched one-pitch operation"); o.AppendLine();
            o.AppendLine("Positive delta favours moving; negative delta supports fewer moves for this pair only. The one-pitch alternative has the same shifts, stock targets and full 08–18 opportunity; moving loses its inter-pitch travel from those same hours."); o.AppendLine();
            var legacyMoving = weeks.Where(w => w.Scenario == "legacy" && w.Alternative == "moving").ToArray();
            var legacySingle = weeks.Where(w => w.Scenario == "legacy" && w.Alternative == "single").ToArray();
            var legacyPairs = legacyMoving.Select(m => (M: m, S: legacySingle.Single(s => s.Route == m.Route))).ToArray();
            var movingWins = legacyPairs.Count(p => p.M.OperatingProfit > p.S.OperatingProfit);
            var stayWins = legacyPairs.Count(p => p.S.OperatingProfit > p.M.OperatingProfit);
            var ties = legacyPairs.Count(p => p.M.OperatingProfit.HasValue && p.M.OperatingProfit == p.S.OperatingProfit);
            var infeasiblePairs = legacyPairs.Count(p => !p.M.OperatingProfit.HasValue || !p.S.OperatingProfit.HasValue);
            Dictionary<string, int> ItemDayTotals(IEnumerable<CrewWeek> results) => Menu.ToDictionary(item => item, item => results.Sum(w => w.SoldOutItemDays[item]));
            var movingItemDays = ItemDayTotals(legacyMoving); var singleItemDays = ItemDayTotals(legacySingle);
            json["legacyComparisonSummary"] = new JObject { ["pairs"] = legacyPairs.Length, ["movingWins"] = movingWins, ["singleWins"] = stayWins,
                ["ties"] = ties, ["infeasiblePairs"] = infeasiblePairs, ["routeDaysPerAlternative"] = 7 * legacyPairs.Length,
                ["movingSoldOutItemDays"] = Token(movingItemDays), ["singleSoldOutItemDays"] = Token(singleItemDays) };
            o.AppendLine($"This comparison is **stock-constrained with no midday restock**. Staying already wins **{stayWins}/{legacyPairs.Length} legacy pairs**, before physical traffic ({movingWins} moving wins, {ties} ties, {infeasiblePairs} infeasible pairs). Across {7 * legacyPairs.Length} legacy route-days per alternative, sold-out item-days are M: {ItemDays(movingItemDays)}; S: {ItemDays(singleItemDays)}. The stay advantage cannot all be attributed to traffic. Without an unlimited-stock counterfactual, these counts do not quantify how much advantage is caused by stock.");
            o.AppendLine();
            o.AppendLine("| Route | Case | Moving weekly profit | Matched single weekly profit | Moving − single | Calculated comparison |"); o.AppendLine("|---|---|---:|---:|---:|---|");
            foreach (var m in weeks.Where(w => w.Alternative == "moving"))
            {
                var s = weeks.Single(w => w.Route == m.Route && w.Scenario == m.Scenario && w.Alternative == "single");
                o.AppendLine($"| {m.Route} | {m.Scenario} | {Money(m.OperatingProfit)} | {Money(s.OperatingProfit)} | {Money(m.OperatingProfit - s.OperatingProfit)} | {Comparison(m, s)} |");
            }
            // Two long routes nearest the median rank: bounded, declared subset, full discrete grid (no monotonic search).
            var subset = routes.Where(r => r.LengthClass == "long").GroupBy(r => r.District)
                .Select(g => g.OrderBy(r => Math.Abs(r.DemandQuantile - .5)).ThenBy(r => r.Id, StringComparer.Ordinal).First()).ToArray();
            o.AppendLine(); o.AppendLine("## Discrete speed × fuel sensitivity"); o.AppendLine();
            o.AppendLine($"Subset: {string.Join(", ", subset.Select(r => r.Id))}. All start full, normal wait {config.NormalWaitGameMinutesPerLeg} min/leg, road factor {config.RoadFactor}. Crossed speed ({string.Join(", ", config.SpeedSensitivityMetresPerSecond)} m/s) and fuel ({string.Join(", ", config.FuelSensitivityLitresPerKilometre)} L/km) grid. This isolates speed from heavy's extra wait. 1/2 m/s are diagnostic probe-test limits, not production targets or evidence of economic acceptability; no probe fuel calibration is used. Grid speeds above 13.9 m/s (16/25) are mathematical time-compression controls, not attainable truck settings. Minutes/straight km excludes per-leg wait and ceiling. No binary search or monotonic break-even claim: demand timing, stock and skipped stops can change results nonmonotonically. Negative profit marks a sampled loss; moving−single <0 marks a sampled preference for staying. Only these discrete points are supported."); o.AppendLine();
            o.AppendLine("| Route | m/s | Game min/straight km, no wait | L/km | Moving weekly profit | Single weekly profit | Moving − single | Sample outcome |"); o.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var r in subset)
            foreach (var speed in config.SpeedSensitivityMetresPerSecond)
            foreach (var lpk in config.FuelSensitivityLitresPerKilometre)
            {
                var scenario = Normal with { Id = $"speed-{speed:R}-fuel-{lpk:R}", SpeedMetresPerSecond = speed, LitresPerKilometre = lpk };
                var m = Simulate(r, scenario, "moving");
                var s = Simulate(Stationary(r, singles[r.Id]), scenario, "single", singles[r.Id].Name);
                Deltas(m, weeks.Single(w => w.Route == r.Id && w.Scenario == "legacy" && w.Alternative == "moving"));
                Deltas(s, weeks.Single(w => w.Route == r.Id && w.Scenario == "legacy" && w.Alternative == "single"));
                sensitivity.Add(m); sensitivity.Add(s);
                o.AppendLine($"| {r.Id} | {speed} | {1000 * config.RoadFactor / speed:0.###} | {lpk} | {Money(m.OperatingProfit)} | {Money(s.OperatingProfit)} | {Money(m.OperatingProfit - s.OperatingProfit)} | {(m.OperatingProfit < 0 ? "moving loss; " : "")}{Comparison(m, s)} |");
            }
            json["sensitivity"] = new JObject { ["routeSubset"] = new JArray(subset.Select(r => r.Id)), ["normalWaitGameMinutesPerLeg"] = config.NormalWaitGameMinutesPerLeg,
                ["roadFactor"] = config.RoadFactor, ["speedGrid"] = Token(config.SpeedSensitivityMetresPerSecond),
                ["speedPoints"] = new JArray(config.SpeedSensitivityMetresPerSecond.Select(speed => ScenarioJson(Normal with { Id = $"speed-{speed:R}", SpeedMetresPerSecond = speed }))), ["fuelGrid"] = Token(config.FuelSensitivityLitresPerKilometre), ["results"] = Token(sensitivity) };
            var waitResults = new List<CrewWeek>();
            var waitScenarios = config.WaitSensitivityGameMinutesPerLeg.Select(wait => Normal with { Id = $"wait-{wait}", WaitGameMinutesPerLeg = wait }).ToArray();
            o.AppendLine(); o.AppendLine("## Hypothetical wait sensitivity"); o.AppendLine();
            o.AppendLine($"Same declared subset, normal {Normal.SpeedMetresPerSecond} m/s, {Normal.LitresPerKilometre} L/km, full initial tank. Wait grid {string.Join(", ", config.WaitSensitivityGameMinutesPerLeg)} game min/leg equals that many real seconds under the unverified 1:1 clock assumption. Wait applies to every nonzero leg, station legs too; idle fuel scales with it. No real traffic-light distribution is claimed. Realized balance shows incurred losses even for infeasible no-service cases."); o.AppendLine();
            o.AppendLine("| Route | Wait game min/leg | Moving profit | Single profit | Moving − single | Moving / single realized balance | Outcomes |");
            o.AppendLine("|---|---:|---:|---:|---:|---|---|");
            foreach (var r in subset)
            foreach (var scenario in waitScenarios)
            {
                var m = Simulate(r, scenario, "moving"); var single = Simulate(Stationary(r, singles[r.Id]), scenario, "single", singles[r.Id].Name);
                foreach (var w in new[] { m, single }) Deltas(w, weeks.Single(b => b.Route == w.Route && b.Alternative == w.Alternative && b.Scenario == "legacy"));
                waitResults.Add(m); waitResults.Add(single);
                o.AppendLine($"| {r.Id} | {scenario.WaitGameMinutesPerLeg} | {Money(m.OperatingProfit)} | {Money(single.OperatingProfit)} | {Money(m.OperatingProfit - single.OperatingProfit)} | {Money(m.RealizedOperatingBalance)} / {Money(single.RealizedOperatingBalance)} | M: {ScheduleStatus(m)}; S: {ScheduleStatus(single)} |");
            }
            json["waitSensitivity"] = new JObject { ["routeSubset"] = new JArray(subset.Select(r => r.Id)), ["scenarios"] = new JArray(waitScenarios.Select(ScenarioJson)), ["results"] = Token(waitResults) };
            o.AppendLine(); o.AppendLine("Discrete threshold summary: for each route/fuel row, sets of sampled speeds are listed directly; these are not continuous break-even thresholds. Loss/preference sets can include reduced operation: consult the M/S reduced-operation sets; infeasible points are separate, and neither is an ordinary successful full schedule."); o.AppendLine();
            o.AppendLine("| Route | L/km | Sampled m/s with moving loss | Sampled m/s where moving loses to matched stay | Reduced-operation sampled m/s (M / S) | Infeasible sampled m/s (either) |"); o.AppendLine("|---|---:|---|---|---|---|");
            foreach (var r in subset)
            foreach (var lpk in config.FuelSensitivityLitresPerKilometre)
            {
                var grid = config.SpeedSensitivityMetresPerSecond.Select(speed =>
                {
                    var id = $"speed-{speed:R}-fuel-{lpk:R}";
                    return (Speed: speed, M: sensitivity.Single(w => w.Route == r.Id && w.Scenario == id && w.Alternative == "moving"),
                        S: sensitivity.Single(w => w.Route == r.Id && w.Scenario == id && w.Alternative == "single"));
                }).ToArray();
                string Speeds(IEnumerable<double> values) => string.Join(", ", values.Select(v => v.ToString("0.###"))) is { Length: > 0 } text ? text : "none";
                o.AppendLine($"| {r.Id} | {lpk} | {Speeds(grid.Where(g => g.M.OperatingProfit < 0).Select(g => g.Speed))} | {Speeds(grid.Where(g => g.M.OperatingProfit - g.S.OperatingProfit < 0).Select(g => g.Speed))} | {Speeds(grid.Where(g => IsReduced(g.M)).Select(g => g.Speed))} / {Speeds(grid.Where(g => IsReduced(g.S)).Select(g => g.Speed))} | {Speeds(grid.Where(g => !g.M.OperatingProfit.HasValue || !g.S.OperatingProfit.HasValue).Select(g => g.Speed))} |");
            }
        }
        if (checks) { o.AppendLine(); o.AppendLine("## Regression checks"); o.AppendLine(); foreach (var check in checkResults) o.AppendLine($"- PASS: {check}"); }
        o.AppendLine(); o.AppendLine("The JSON includes numeric daily/weekly metrics, assumptions, route definitions and SHA256 provenance of current inputs and calculator/Core sources. No native traffic/fuel calibration, game installation or runtime/data writes were used.");
        return (o.ToString(), json);
    }
    private JObject LegsJson(CrewRoute r)
    {
        var points = new[] { r.Depot }.Concat(r.Stops.Select(s => s.Pitch)).Append(r.Depot).ToArray();
        return new JObject { ["legs"] = new JArray(points.Zip(points.Skip(1), (a, b) => new JObject
        {
            ["from"] = a.Name, ["to"] = b.Name, ["straightMetres"] = a.Distance(b),
            ["assumedPhysicalRoadMetres"] = a.Distance(b) * config.RoadFactor,
            ["legacyAssumedRoadMetres"] = a.Distance(b) * rules.Fleet.RoadFactor,
            ["legacyTimerMinutes"] = Leg(a, b, Legacy).Minutes,
            ["normalMinutes"] = Leg(a, b, Normal).Minutes, ["heavyMinutes"] = Leg(a, b, Heavy).Minutes
        })) };
    }
    private static JObject Point(Spot s) => new() { ["address"] = s.Name, ["neighbourhood"] = s.Hood, ["region"] = s.Region, ["x"] = s.X, ["z"] = s.Z };
    private static string Label(Spot s) => $"{s.Name} ({s.X:R},{s.Z:R})";
    private static string Money(double? n) => n?.ToString("0.00") ?? "INFEASIBLE";
    private static string Abbr(CrewWeek w) => w.Alternative == "moving" ? "M" : "S";
    private static List<string> FindSoldOutItems(CrewDay day) => day.StartingStock.Keys.Concat(day.LoadedStock.Keys).Distinct(StringComparer.Ordinal)
        .Where(item => day.StartingStock.GetValueOrDefault(item) + day.LoadedStock.GetValueOrDefault(item) > 1e-6 &&
            day.Stops.Sum(v => v.Sales.Sold.GetValueOrDefault(item)) > 1e-6 && day.EndingStock.GetValueOrDefault(item) <= 1e-6)
        .OrderBy(item => item, StringComparer.Ordinal).ToList();
    private static string ItemDays(IReadOnlyDictionary<string, int> counts) => string.Join(", ", counts.Select(p => $"{p.Key.Replace("ba:itemname_", "")} {p.Value}"));
    private static bool IsReduced(CrewWeek w) => w.OperatingProfit.HasValue && (w.SkippedStops > 0 || !w.ScheduledRouteFeasible);
    private static string ScheduleStatus(CrewWeek w) => !w.OperatingProfit.HasValue ? $"infeasible, {w.SkippedStops} skipped"
        : IsReduced(w) ? $"reduced operation, {w.SkippedStops} skipped" : "full schedule completed";
    private static string Comparison(CrewWeek m, CrewWeek s)
    {
        var comparison = !m.OperatingProfit.HasValue || !s.OperatingProfit.HasValue ? "infeasible pair; no completed-schedule comparison"
            : m.OperatingProfit > s.OperatingProfit ? "moving earns more" : m.OperatingProfit < s.OperatingProfit ? "stay earns more; fewer moves supported for this pair" : "tie";
        return $"{comparison}; M: {ScheduleStatus(m)}; S: {ScheduleStatus(s)}";
    }
    private JObject Hashes()
    {
        string? repo = null;
        // Resolve repository independently of a custom --data directory or launch working directory.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "tools", "balance", "CrewEconomics.cs"))) { repo = d.FullName; break; }
            if (repo != null) break;
        }
        if (repo == null) throw new InvalidOperationException("Cannot locate calculator source for input hashes.");
        var files = new[] { "demand.json", "economy.json", "rules.json", "zones.json", "commissaries.json" }.Select(n => Path.Combine(opts.Data, n))
            .Concat(new[] { opts.City, opts.Prices, opts.CrewEconomicsConfig })
            .Concat(Directory.GetFiles(Path.Combine(repo, "Core"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains("/obj/") && !p.Contains("/.artifacts/")))
            .Concat(Directory.GetFiles(Path.Combine(repo, "tools", "balance"), "*.cs"))
            .Concat(new[] { Path.Combine(repo, "Core", "FoodTrucks.Core.csproj"), Path.Combine(repo, "tools", "balance", "Balance.csproj"), Path.Combine(repo, "tools", "balance", "make_city.py") });
        if (opts.Patch != null) files = files.Append(opts.Patch);
        var hashes = new JObject();
        foreach (var path in files.Select(Path.GetFullPath).Distinct().OrderBy(p => p, StringComparer.Ordinal))
            hashes[Path.GetRelativePath(repo, path)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        return hashes;
    }

    private List<string> Checks(List<CrewRoute> routes, Dictionary<string, Spot> singles)
    {
        var passed = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Crew economics check failed: " + message);
            passed.Add(message);
        }
        bool Near(double a, double b) => Math.Abs(a - b) <= 1e-7 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        bool SameStock(Dictionary<string, double> a, Dictionary<string, double> b) => Menu.All(i => Near(a.GetValueOrDefault(i), b.GetValueOrDefault(i)));
        Check(routes.All(route => city.Buildings.Any(b => b.Address + " commissary" == route.Depot.Name && b.Region == route.Depot.Region &&
            Near(b.X, route.Depot.X) && Near(b.Z, route.Depot.Z))),
            "Depots and pitches use the same city.json regional map frame; native bay world coordinates never enter distance calculations.");
        var exhaustionFixture = new CrewDay
        {
            StartingStock = new() { ["carried"] = 2, ["empty-unserved"] = 0, ["remaining"] = 2, ["not-sold"] = 2 },
            LoadedStock = new() { ["loaded"] = 2 },
            EndingStock = new() { ["carried"] = 0, ["empty-unserved"] = 0, ["remaining"] = 1, ["not-sold"] = 0, ["loaded"] = 1e-7 },
            Stops = new() { new() { Sales = new() } }
        };
        exhaustionFixture.Stops[0].Sales.Sold["carried"] = 2;
        exhaustionFixture.Stops[0].Sales.Sold["loaded"] = 2 - 1e-7;
        exhaustionFixture.Stops[0].Sales.Sold["remaining"] = 1;
        Check(FindSoldOutItems(exhaustionFixture).SequenceEqual(new[] { "carried", "loaded" }) && FindSoldOutItems(new CrewDay()).Count == 0,
            "Sold-out counting includes carried/loaded stock actually sold to zero, excluding empty unserved, unsold and remaining items.");
        var fullFixture = new CrewWeek { OperatingProfit = 1, ScheduledRouteFeasible = true };
        var reducedFixture = new CrewWeek { OperatingProfit = 2, ScheduledRouteFeasible = false, SkippedStops = 3 };
        var infeasibleFixture = new CrewWeek { OperatingProfit = null, ScheduledRouteFeasible = false, SkippedStops = 5 };
        Check(Comparison(reducedFixture, fullFixture).Contains("M: reduced operation, 3 skipped; S: full schedule completed") &&
            Comparison(infeasibleFixture, reducedFixture).Contains("M: infeasible, 5 skipped; S: reduced operation, 3 skipped") &&
            IsReduced(reducedFixture) && !IsReduced(infeasibleFixture) && !IsReduced(fullFixture),
            "Comparison/sensitivity status text and reduced-speed classification distinguish skipped-stop reduced operation, full completion and infeasibility.");
        var r = routes.Where(r => r.LengthClass == "long").OrderByDescending(r => r.Stops.Zip(r.Stops.Skip(1), (a, b) => a.Pitch.Distance(b.Pitch)).Sum()).First();
        Check(new[] { 0.0, .001, 31, 100, 1000, 10000 }.All(d => Leg(d, Legacy).Minutes == TimerTravel.Minutes(rules.Fleet, d, TravelMode.Drive)) &&
            routes.All(route => route.Stops.All(s => Leg(route.Depot, s.Pitch, Legacy).FuelLitres == 0)), "Legacy uses exact Core timer and no fuel (zero, fractional and long distances).");
        Check(routes.All(route => RouteDistance(route) >= route.TotalStraightMetresBand[0] && RouteDistance(route) < route.TotalStraightMetresBand[1] &&
            Near(route.DownstreamWindowContributionScore, Selling.Run(cart, route.Stops[1].Pitch, 690, 870, On(DayOfWeek.Wednesday), new(target)).Profit +
                Selling.Run(cart, route.Stops[2].Pitch, 870, 1080, On(DayOfWeek.Wednesday), new(target)).Profit)),
            "Purposeful downstream selection uses baseline nominal-window contribution and actual total distance within its declared band.");
        bool SameControl(CrewWeek legacy, CrewWeek control)
        {
            JObject Normalize(CrewWeek week)
            {
                var token = JObject.FromObject(week);
                foreach (var property in token.Descendants().OfType<JProperty>().Where(p => p.Name is "Scenario" or "AssumedRoadMetres").ToArray()) property.Remove();
                return token;
            }
            return JToken.DeepEquals(Normalize(legacy), Normalize(control));
        }
        Check(new[] { 0.0, .001, 31, 100, 1000, 10000, 25 * 13.0 }.All(d => Leg(d, EquivalentControl).Minutes == Leg(d, Legacy).Minutes) &&
            routes.All(route => SameControl(Simulate(route, Legacy, "moving"), Simulate(route, EquivalentControl, "moving")) &&
                SameControl(Simulate(Stationary(route, singles[route.Id]), Legacy, "single"), Simulate(Stationary(route, singles[route.Id]), EquivalentControl, "single"))),
            "Zero-wait fuel-disabled physical control exactly matches legacy trips, economics, stock and daily intervals (only assumed road-length metadata differs).");
        var physical = Normal with { SpeedMetresPerSecond = 8, WaitGameMinutesPerLeg = 3 };
        Check(Leg(800 / config.RoadFactor, physical).Minutes == 103 && Near(Leg(800 / config.RoadFactor, physical).AssumedRoadMetres, 800),
            "800 assumed road metres at 8 m/s takes 100 game minutes + 3 wait, no factor 60.");
        var a = Simulate(r, Normal, "moving");
        var b = Simulate(r, Normal, "moving");
        Check(JToken.DeepEquals(JToken.FromObject(a), JToken.FromObject(b)), "Identical scenarios produce identical full weekly economics and traces.");
        var samples = new[] { Simulate(r, Legacy, "moving"), a, Simulate(r, Heavy, "moving"),
            Simulate(r, Normal with { Id = "stress-check", StartingLitres = config.StressStartingLitres }, "moving") };
        Check(samples.All(w => Near(w.WageHours, 168) && Near(w.Wages, wage * 168) && w.ExtraWages == 0 && w.Days.All(d => d.WageHours == 24)),
            "All cases pay the same two 12-hour shifts for seven days; extra wages zero.");
        Check(samples.All(w => SameStock(w.TargetStock, target) && w.Days[0].StartingStock.Values.All(v => v == 0) && w.Days.All(d =>
            Menu.All(i => Near(d.StartingStock[i] + d.LoadedStock.GetValueOrDefault(i), target[i])))),
            "Monday starts empty; all days/cases load to identical full per-piece targets at the depot, never scenario-demand targets.");
        Check(samples.All(w => w.Days.Zip(w.Days.Skip(1), (left, right) => SameStock(left.EndingStock, right.StartingStock) && Near(left.FinalFuelLitres, right.StartingFuelLitres)).All(x => x)),
            "Next day starts with prior day's exact stock and fuel, no daily reset.");
        Check(samples.All(w => w.Days.All(d =>
        {
            var remaining = Menu.ToDictionary(i => i, i => d.StartingStock[i] + d.LoadedStock.GetValueOrDefault(i));
            foreach (var visit in d.Stops)
            {
                if (!SameStock(remaining, visit.StockBefore)) return false;
                foreach (var item in Menu) remaining[item] -= visit.Sales.Sold.GetValueOrDefault(item);
                if (remaining.Values.Any(n => n < -1e-7) || !SameStock(remaining, visit.StockAfter)) return false;
            }
            return SameStock(remaining, d.EndingStock) && cart.PieceLoads(d.EndingStock).Values.All(n => n <= cart.PerPiece + 1e-7);
        })), "Finite stock is consumed across stops, no roadside refill, no piece over capacity or negative stock.");
        Check(samples.All(w => w.Days.All(d => d.Stops.All(v => !v.ServiceFrom.HasValue ||
            v.ServiceFrom >= Math.Max(ShiftOpen + config.LoadingGameMinutes, v.WindowOpen) && v.ServiceTo <= v.WindowClose && v.ServiceTo > v.ServiceFrom && v.Arrive <= v.ServiceFrom &&
            !d.Trips.Any(t => Overlap(v.ServiceFrom.Value, v.ServiceTo!.Value, t.Depart, t.Arrive) > 0)))),
            "No service before loading/arrival, during travel or outside fixed pitch windows.");
        Check(samples.All(w => Near(w.StartingFuelLitres + w.FuelPurchasedLitres - w.FuelConsumedLitres, w.FinalFuelLitres) &&
            w.Days.All(d => Near(d.StartingFuelLitres + d.FuelPurchasedLitres - d.FuelConsumedLitres, d.FinalFuelLitres) && d.FinalFuelLitres >= -1e-7 && d.FinalFuelLitres <= config.TankLitres + 1e-7)),
            "Daily and weekly fuel conservation: start + purchased − consumed = end, bounded by finite tank.");
        Check(samples.All(w => w.Days.All(d => Near(d.StockCogs, d.Stops.Sum(v => v.Sales.Cost)) &&
            Near(d.FuelConsumedCost, d.FuelConsumedLitres * config.FuelDollarsPerLitre) &&
            (!d.OperatingProfit.HasValue || Near(d.OperatingProfit.Value, d.Revenue - d.StockCogs - d.Wages - d.Rent - d.DeliveryAllocation - d.FuelConsumedCost)))),
            "COGS is sold units only; profit expenses consumed fuel including initial inventory, never purchase cash twice.");
        Check(samples.All(w => w.Days.All(d => d.LoadingMinutes == config.LoadingGameMinutes && d.Trips.All(t => t.Depart >= ShiftOpen + config.LoadingGameMinutes))),
            "All cases use the configured morning loading duration before any departure (default 30 game minutes, matching Core restock).");
        var wait0 = Leg(1000, Normal with { WaitGameMinutesPerLeg = 0 });
        var wait180 = Leg(1000, Normal with { WaitGameMinutesPerLeg = 180 });
        Check(wait180.Minutes - wait0.Minutes == 180 && Near(wait180.FuelLitres - wait0.FuelLitres, 180 * config.IdleLitresPerGameMinute) &&
            Leg(1000 / config.RoadFactor, Normal with { SpeedMetresPerSecond = 1, WaitGameMinutesPerLeg = 0 }).Minutes == 1000 &&
            Leg(1000 / config.RoadFactor, Normal with { SpeedMetresPerSecond = 2, WaitGameMinutesPerLeg = 0 }).Minutes == 500 &&
            new[] { 0, 30, 90, 180 }.All(wait => Leg(1000, Normal with { WaitGameMinutesPerLeg = wait }).Minutes - wait0.Minutes == wait),
            "Diagnostic 1/2 m/s and 0/30/90/180 wait fixtures use game minutes and idle fuel with no factor 60.");
        var stress = samples[^1];
        var fills = stress.Days.SelectMany(d => d.Refuels).ToList();
        Check(fills.Count > 0 && fills.All(f => Near(f.PurchasedLitres, config.TankLitres - f.FuelOnArrival) && f.PurchasedLitres > 0 &&
            f.Depart - f.Arrive == config.StationServiceGameMinutes && Near(f.FuelAfterFill, config.TankLitres)) &&
            stress.Days.All(d => d.RefuelServiceMinutes == d.Refuels.Count * config.StationServiceGameMinutes && d.Refuels.Count <= 1 &&
                d.Trips.Count(t => t.Purpose.StartsWith("station-")) == d.Refuels.Count * 2),
            "Low-tank stress exercises bounded station loop, actual empty-capacity fill and full declared service/travel time.");
        var stranded = Simulate(r, Normal with { Id = "empty-tank-check", StartingLitres = 0 }, "moving");
        Check(stranded.OperatingProfit == null && stranded.Days.All(d => d.Outcome == "InfeasibleStationUnreachable" && d.Revenue == 0 && d.FuelPurchasedLitres == 0 && d.Trips.Count == 0) && stranded.FinalFuelLitres == 0,
            "Unreachable station at zero fuel is explicit infeasible; no free recovery, fill, sales or ordinary profit.");
        // A very slow fuel-free physical fixture isolates shift/window dispatch from finite-tank rejection.
        var slow = Simulate(r, Normal with { Id = "extreme-delay-check", SpeedMetresPerSecond = .0001, LitresPerKilometre = 0, WaitGameMinutesPerLeg = 0 }, "moving");
        Check(slow.OperatingProfit == null && slow.Days.All(d => d.SkippedStops == r.Stops.Count && d.OpenMinutes == 0 && d.Revenue == 0 &&
            d.Stops.All(v => v.Outcome == "SkippedWindowOrReturnShift" && !v.ServiceFrom.HasValue)) && slow.FinalFuelLitres == slow.StartingFuelLitres,
            "Extreme physical delay skips all missed windows, never sells afterwards; shift-infeasible/no-service outcome is explicit.");
        Check(slow.Days.All(d => Near(d.RealizedOperatingBalance, -d.Wages - d.Rent - d.DeliveryAllocation)) &&
            Near(slow.RealizedOperatingBalance, slow.Days.Sum(d => d.RealizedOperatingBalance)) && slow.RealizedOperatingBalance < 0,
            "Infeasible zero-sales operation retains actual fixed-cost losses in realized balance while ordinary profit remains null.");
        var thirsty = Simulate(r, Normal with { Id = "tank-check", LitresPerKilometre = 1000000, StartingLitres = config.TankLitres }, "moving");
        Check(thirsty.OperatingProfit == null && thirsty.FuelPurchasedLitres == 0 && thirsty.Days.All(d => d.Revenue == 0 && d.Outcome.StartsWith("Infeasible")),
            "Impossible day/station tank requirement is explicit infeasible and cannot mint fuel.");
        Check(samples.All(w => w.Days.Where(d => d.OperatingProfit.HasValue).All(d => d.CompletedHomeReturn && d.HomeMinute <= ShiftClose &&
            d.Stops.Where(v => v.ServiceTo.HasValue).All(v => v.ServiceTo + Leg(r.Stops.Single(s => s.Pitch.Name == v.Pitch).Pitch, r.Depot,
                w.Scenario == "legacy" ? Legacy : w.Scenario == "heavy" ? Heavy : Normal).Minutes <= ShiftClose))),
            "Homeward travel is reserved before serving; ordinary operating results complete home within paid shift.");
        // Real input demand must vary with the clock; a delayed interval must be priced at its actual time.
        var timing = spots.Select(s => (Spot: s, Difference: Math.Abs(Demand(s, 660, 690, On(DayOfWeek.Wednesday)) - Demand(s, 870, 900, On(DayOfWeek.Wednesday)))))
            .OrderByDescending(p => p.Difference).ThenBy(p => p.Spot.Name, StringComparer.Ordinal).First();
        var initial = new Dictionary<string, double>(target);
        var early = Selling.Run(cart, timing.Spot, 660, 690, On(DayOfWeek.Wednesday), initial);
        initial = new(target);
        var late = Selling.Run(cart, timing.Spot, 870, 900, On(DayOfWeek.Wednesday), initial);
        Check(timing.Difference > 1e-5 && !Near(early.Revenue, late.Revenue) &&
            !Near(cart.Plan(timing.Spot, 660, On(DayOfWeek.Wednesday), target).Demand, cart.Plan(timing.Spot, 870, On(DayOfWeek.Wednesday), target).Demand),
            "Real city demand/sales differ at 11:00 versus 14:30; Selling evaluates delayed demand at actual clock time.");
        // Controlled delayed arrival at a real pitch, crossing morning into lunch, while home still fits.
        // The synthetic depot is a check fixture only and is never part of study route definitions.
        var clockPitch = spots.OrderByDescending(s => Math.Abs(Demand(s, 480, 510, On(DayOfWeek.Wednesday)) - Demand(s, 690, 720, On(DayOfWeek.Wednesday))))
            .ThenBy(s => s.Name, StringComparer.Ordinal).First();
        var offsetDepot = new Spot("check-only offset depot", clockPitch.Areas, clockPitch.Hood, clockPitch.Region, clockPitch.X + 100, clockPitch.Z);
        var clockRoute = r with { Depot = offsetDepot, Stops = new() { new(clockPitch, 480, 900) } };
        var clockScenario = Normal with { Id = "clock-check", SpeedMetresPerSecond = 100 * config.RoadFactor / (690 - ShiftOpen - config.LoadingGameMinutes),
            WaitGameMinutesPerLeg = 0, LitresPerKilometre = 0 };
        var clockWeek = Simulate(clockRoute, clockScenario, "moving");
        var clockVisit = clockWeek.Days[2].Stops[0]; // Actual Wednesday, no weekday approximation.
        var expectedClock = Selling.Run(cart, clockPitch, 690, clockVisit.ServiceTo ?? 690, On(DayOfWeek.Wednesday), new(target));
        var backdatedClock = Selling.Run(cart, clockPitch, 480, 480 + (clockVisit.ServiceTo ?? 690) - 690, On(DayOfWeek.Wednesday), new(target));
        Check(clockVisit.ServiceFrom == 690 && Near(clockVisit.Sales.Revenue, expectedClock.Revenue) && !Near(expectedClock.Revenue, backdatedClock.Revenue),
            "A controlled arrival delayed from 08:00 to 11:30 sells at actual lunch demand and differs from backdated morning sales.");
        // Verify an actually trimmed study visit against Selling at actual time, and detect a backdated alternative.
        var trimmed = samples.SelectMany(w => w.Days.SelectMany(d => d.Stops.Where(v => v.ServiceFrom > v.WindowOpen).Select(v => (Day: d, Visit: v))))
            .Select(p =>
            {
                var stop = r.Stops.Single(s => s.Pitch.Name == p.Visit.Pitch);
                var actual = Selling.Run(cart, stop.Pitch, p.Visit.ServiceFrom!.Value, p.Visit.ServiceTo!.Value, On(Enum.Parse<DayOfWeek>(p.Day.Weekday)), new(p.Visit.StockBefore));
                return Near(actual.Revenue, p.Visit.Sales.Revenue) && Near(actual.Cost, p.Visit.Sales.Cost);
            }).ToList();
        Check(trimmed.Count > 0 && trimmed.All(x => x), "Actually late study visits match actual-time Selling intervals, never nominal scheduled-time sales.");
        Check(routes.All(route => route.Stops.Any(s => ReferenceEquals(s.Pitch, singles[route.Id])) &&
            SelectSingle(route) == singles[route.Id]), "Matched single is selected from moving pitches under legacy only and remains frozen for all scenarios.");
        var saturday = On(DayOfWeek.Saturday);
        Check(spots.Any(s => !Near(Demand(s, 480, 1080, On(DayOfWeek.Wednesday)), Demand(s, 480, 1080, saturday))) &&
            samples.All(w => w.Days.Select(d => d.Weekday).SequenceEqual(Weekdays.Select(d => d.ToString())) &&
                w.Days.All(d => DateOnly.Parse(d.Date).DayOfWeek == Enum.Parse<DayOfWeek>(d.Weekday))),
            "Seven actual weekdays are used, with demand varying between weekday and weekend.");
        bool Reject(CrewAssumptions candidate)
        {
            try { candidate.Validate(); return false; } catch (ArgumentException) { return true; }
        }
        Check(Reject(config with { NormalSpeedMetresPerSecond = 0 }) && Reject(config with { LitresPerKilometre = double.NaN }) &&
            Reject(config with { StressStartingLitres = config.TankLitres + 1 }) && Reject(config with { SchemaVersion = 3 }) &&
            Reject(config with { Units = "km/h" }) && Reject(config with { DemandQuantiles = new[] { 1.1 } }),
            "Configuration rejects invalid speed, nonfinite fuel, overfull start, unsupported schema/units and out-of-range ranks.");
        return passed;
    }
}
