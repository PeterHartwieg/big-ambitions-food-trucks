using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Demand
{
    // The validated contents of data/demand.json, data/economy.json and data/zones.json.
    // Parse throws DemandDataException naming the file and field of the first problem.
    public sealed class DemandTables
    {
        public const int DemandSchemaVersion = 1;
        public const int EconomySchemaVersion = 1;
        public const int ZonesSchemaVersion = 1;

        internal static readonly string[] DaypartKeys = { "early", "morning", "lunch", "afternoon", "evening", "late", "night" };
        // Indexed by System.DayOfWeek, which starts on Sunday.
        internal static readonly string[] DayKeys = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };
        internal static readonly string[] SeasonKeys = { "winter", "spring", "summer", "autumn" };
        internal static readonly string[] MonthKeys = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        private readonly int[] daypartStarts;
        private readonly Dictionary<string, AreaTable> areas;
        private readonly Dictionary<string, CategoryTable> categories;
        private readonly Dictionary<string, NeighbourhoodTable> neighbourhoods;
        private readonly MonthInfo[] months;
        private readonly Dictionary<string, double>[] seasonAppetite;
        private readonly Dictionary<string, double> rainAppetite;
        private readonly Dictionary<string, ProductInfo> products;
        private readonly Dictionary<string, EquipmentInfo> equipment;
        private readonly Dictionary<string, EquipmentInfo> equipmentByGameItem;
        private readonly Dictionary<string, VehicleInfo> vehicles;

        public double PassersPerHour { get; }
        public double FurtherCategoryWeight { get; }
        public int DaysPerMonth { get; }
        public int StartMonth { get; }
        public double RainFootfall { get; }
        public double MarketFloor { get; }
        public double MarketCeiling { get; }
        public ClassifierSettings Classifier { get; }
        public IReadOnlyList<Zone> Zones { get; }
        public PriceCurve PriceCurve { get; }

        public IReadOnlyCollection<string> AreaNames => areas.Keys;
        public IReadOnlyCollection<string> CategoryNames => categories.Keys;
        public IReadOnlyCollection<string> ProductNames => products.Keys;
        public IReadOnlyCollection<string> EquipmentIds => equipment.Keys;
        // For the adapter to log keys the game doesn't know at city load: a typo here only goes neutral.
        public IReadOnlyCollection<string> NeighbourhoodNames => neighbourhoods.Keys;

        private DemandTables(DataReader demandReader, JObject demand, DataReader economyReader, JObject economy, DataReader zonesReader, JObject zones)
        {
            var r = demandReader;
            r.OnlyKeys(demand, "schemaVersion", "passersPerHour", "dayparts", "areas", "categories", "menu", "neighbourhoods",
                "calendar", "months", "seasons", "rain", "market", "classifier");
            r.SchemaVersion(demand, DemandSchemaVersion);

            PassersPerHour = r.Number(demand, "passersPerHour", 0, 100000);
            daypartStarts = ReadDayparts(r, r.Object(demand, "dayparts"));

            areas = new Dictionary<string, AreaTable>(StringComparer.Ordinal);
            var areasObj = r.Object(demand, "areas");
            foreach (var entry in r.Entries(areasObj))
            {
                var obj = AsObject(r, entry);
                r.OnlyKeys(obj, "footfall", "days");
                areas.Add(entry.Name, new AreaTable(entry.Name,
                    FullTable(r, r.Object(obj, "footfall"), DaypartKeys, 0, 100),
                    FullTable(r, r.Object(obj, "days"), DayKeys, 0, 100)));
            }
            if (areas.Count == 0) throw r.Fail("areas", "must list at least one area");

            categories = new Dictionary<string, CategoryTable>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(r.Object(demand, "categories")))
            {
                var obj = AsObject(r, entry);
                r.OnlyKeys(obj, "appetite", "newCustomerWeight", "addOnFactor");
                var weight = obj["newCustomerWeight"] == null ? 1.0 : r.Number(obj, "newCustomerWeight", 0, 1);
                var addOn = obj["addOnFactor"] == null ? 0.0 : r.Number(obj, "addOnFactor", 0, 100);
                categories.Add(entry.Name, new CategoryTable(entry.Name, FullTable(r, r.Object(obj, "appetite"), DaypartKeys, 0, 1), weight, addOn));
            }
            if (categories.Count == 0) throw r.Fail("categories", "must list at least one category");

            var menu = r.Object(demand, "menu");
            r.OnlyKeys(menu, "furtherCategoryWeight");
            FurtherCategoryWeight = r.Number(menu, "furtherCategoryWeight", 0, 1);

            neighbourhoods = new Dictionary<string, NeighbourhoodTable>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(r.Object(demand, "neighbourhoods")))
                neighbourhoods.Add(entry.Name, ReadNeighbourhood(r, entry.Name, AsObject(r, entry)));

            var calendar = r.Object(demand, "calendar");
            r.OnlyKeys(calendar, "daysPerMonth", "startMonth");
            DaysPerMonth = r.Integer(calendar, "daysPerMonth", 1, 365);
            StartMonth = r.Integer(calendar, "startMonth", 1, 12);

            var monthsObj = r.Object(demand, "months");
            r.OnlyKeys(monthsObj, MonthKeys);
            months = new MonthInfo[12];
            for (var i = 0; i < 12; i++)
            {
                var obj = r.Object(monthsObj, MonthKeys[i]);
                r.OnlyKeys(obj, "season", "footfall");
                var seasonName = r.Text(obj, "season");
                var season = Array.IndexOf(SeasonKeys, seasonName);
                if (season < 0) throw r.Fail(DataReader.PathOf(obj, "season"), "must be one of: " + string.Join(", ", SeasonKeys));
                months[i] = new MonthInfo((Season)season, r.Number(obj, "footfall", 0, 100));
            }

            var seasonsObj = r.Object(demand, "seasons");
            r.OnlyKeys(seasonsObj, SeasonKeys);
            seasonAppetite = new Dictionary<string, double>[SeasonKeys.Length];
            for (var i = 0; i < SeasonKeys.Length; i++)
            {
                var obj = r.Object(seasonsObj, SeasonKeys[i]);
                r.OnlyKeys(obj, "appetite");
                seasonAppetite[i] = CategoryFactors(r, r.Object(obj, "appetite"));
            }

            var rain = r.Object(demand, "rain");
            r.OnlyKeys(rain, "footfall", "appetite");
            RainFootfall = r.Number(rain, "footfall", 0, 100);
            rainAppetite = CategoryFactors(r, r.Object(rain, "appetite"));

            var market = r.Object(demand, "market");
            r.OnlyKeys(market, "floor", "ceiling");
            MarketFloor = r.Number(market, "floor", 0, 10);
            MarketCeiling = r.Number(market, "ceiling", MarketFloor, 10);

            Classifier = ReadClassifier(r, r.Object(demand, "classifier"));

            ReadEconomy(economyReader, economy, out products, out equipment, out vehicles, out var curve);
            equipmentByGameItem = new Dictionary<string, EquipmentInfo>(StringComparer.Ordinal);
            foreach (var piece in equipment.Values)
                foreach (var item in piece.GameItems) equipmentByGameItem.Add(item, piece);
            PriceCurve = curve;
            Zones = ReadZones(zonesReader, zones);
        }

        public static DemandTables Parse(string? demandJson, string? economyJson, string? zonesJson)
        {
            var demandReader = new DataReader("demand.json");
            var economyReader = new DataReader("economy.json");
            var zonesReader = new DataReader("zones.json");
            return new DemandTables(demandReader, demandReader.Parse(demandJson), economyReader, economyReader.Parse(economyJson),
                zonesReader, zonesReader.Parse(zonesJson));
        }

        public Daypart DaypartAt(int minuteOfDay)
        {
            if (minuteOfDay < 0 || minuteOfDay >= 1440) throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
            var offset = MinutesSinceEarly(minuteOfDay);
            var result = Daypart.Early;
            for (var i = 1; i < daypartStarts.Length; i++)
                if (offset >= MinutesSinceEarly(daypartStarts[i])) result = (Daypart)i;
            return result;
        }

        // True from midnight until Early starts: those hours still belong to the previous day's night out.
        public bool IsBeforeEarly(int minuteOfDay) => minuteOfDay < daypartStarts[(int)Daypart.Early];

        // The mod's own month for an in-game day: day 1 is the first day of StartMonth.
        public int MonthOf(int gameDay)
        {
            var monthIndex = (long)Math.Floor((gameDay - 1) / (double)DaysPerMonth);
            var month = (StartMonth - 1 + monthIndex) % 12;
            if (month < 0) month += 12;
            return (int)month + 1;
        }

        public Season SeasonOf(int month) => Month(month).Season;

        public MonthInfo Month(int month)
        {
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month));
            return months[month - 1];
        }

        public bool HasArea(string name) => name != null && areas.ContainsKey(name);
        public AreaTable Area(string name) =>
            areas.TryGetValue(name, out var area) ? area : throw new ArgumentException($"Unknown area type '{name}'.", nameof(name));

        public CategoryTable Category(string name) =>
            categories.TryGetValue(name, out var category) ? category : throw new ArgumentException($"Unknown category '{name}'.", nameof(name));

        // Unknown neighbourhoods (mod-added ones, for example) are neutral.
        public NeighbourhoodTable Neighbourhood(string? name) =>
            name != null && neighbourhoods.TryGetValue(name, out var table) ? table : NeighbourhoodTable.Neutral;

        public double SeasonAppetite(Season season, string category) =>
            seasonAppetite[(int)season].TryGetValue(category, out var f) ? f : 1.0;

        public double RainAppetite(string category) => rainAppetite.TryGetValue(category, out var f) ? f : 1.0;

        public ProductInfo? Product(string? itemName) =>
            itemName != null && products.TryGetValue(itemName, out var product) ? product : null;

        public EquipmentInfo? Equipment(string? id) => id != null && equipment.TryGetValue(id, out var piece) ? piece : null;

        public EquipmentInfo? EquipmentForGameItem(string? itemName) =>
            itemName != null && equipmentByGameItem.TryGetValue(itemName, out var piece) ? piece : null;

        public VehicleInfo? Vehicle(string? type) => type != null && vehicles.TryGetValue(type, out var vehicle) ? vehicle : null;

        private int MinutesSinceEarly(int minute) => ((minute - daypartStarts[(int)Daypart.Early]) % 1440 + 1440) % 1440;

        private static int[] ReadDayparts(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, DaypartKeys);
            var starts = new int[DaypartKeys.Length];
            for (var i = 0; i < DaypartKeys.Length; i++)
            {
                var path = DataReader.PathOf(obj, DaypartKeys[i]);
                var text = r.Text(r.Required(obj, DaypartKeys[i]), path);
                var parts = text.Split(':');
                if (parts.Length != 2 || parts[0].Length != 2 || parts[1].Length != 2 ||
                    !int.TryParse(parts[0], out var hours) || !int.TryParse(parts[1], out var minutes) ||
                    hours < 0 || hours > 23 || minutes < 0 || minutes > 59)
                    throw r.Fail(path, $"must be a time as HH:MM, got '{text}'");
                starts[i] = hours * 60 + minutes;
            }
            // Each daypart must start after the one before it, counting round the clock from Early.
            for (var i = 1; i < starts.Length; i++)
            {
                var previous = ((starts[i - 1] - starts[0]) % 1440 + 1440) % 1440;
                var current = ((starts[i] - starts[0]) % 1440 + 1440) % 1440;
                if (current <= previous)
                    throw r.Fail(DataReader.PathOf(obj, DaypartKeys[i]), $"must start after {DaypartKeys[i - 1]} (dayparts run in the order {string.Join(", ", DaypartKeys)})");
            }
            return starts;
        }

        private NeighbourhoodTable ReadNeighbourhood(DataReader r, string name, JObject obj)
        {
            r.OnlyKeys(obj, "footfall", "dayparts", "days", "seasons", "appetite");
            var footfall = r.Number(obj, "footfall", 0, 100);
            var dayparts = PartialTable(r, r.OptionalObject(obj, "dayparts"), DaypartKeys);
            var days = PartialTable(r, r.OptionalObject(obj, "days"), DayKeys);
            var seasons = PartialTable(r, r.OptionalObject(obj, "seasons"), SeasonKeys);
            var rules = new List<AppetiteRule>();
            var appetite = r.OptionalArray(obj, "appetite");
            if (appetite != null)
            {
                foreach (var token in appetite)
                {
                    if (!(token is JObject rule)) throw r.Fail(token.Path, "must be an object");
                    r.OnlyKeys(rule, "categories", "dayparts", "days", "seasons", "factor");
                    var ruleCategories = NameList(r, rule, "categories", categories.Keys, required: true)!;
                    rules.Add(new AppetiteRule(
                        ruleCategories,
                        IndexSet(NameList(r, rule, "dayparts", DaypartKeys, required: false), DaypartKeys),
                        IndexSet(NameList(r, rule, "days", DayKeys, required: false), DayKeys),
                        IndexSet(NameList(r, rule, "seasons", SeasonKeys, required: false), SeasonKeys),
                        r.Number(rule, "factor", 0, 100)));
                }
            }
            return new NeighbourhoodTable(name, footfall, dayparts, days, seasons, rules);
        }

        private ClassifierSettings ReadClassifier(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "radiusMetres", "edgeWeight", "maxAreas", "fallbackArea", "buildingTypes", "businessTypes");
            var fallback = r.Text(obj, "fallbackArea");
            if (!areas.ContainsKey(fallback)) throw r.Fail(DataReader.PathOf(obj, "fallbackArea"), $"names unknown area '{fallback}'");
            return new ClassifierSettings(
                r.Number(obj, "radiusMetres", 1, 10000),
                r.Number(obj, "edgeWeight", 0, 1),
                r.Integer(obj, "maxAreas", 1, areas.Count),
                fallback,
                AreaRules(r, r.Object(obj, "buildingTypes")),
                AreaRules(r, r.Object(obj, "businessTypes")));
        }

        private Dictionary<string, IReadOnlyDictionary<string, double>> AreaRules(DataReader r, JObject obj)
        {
            var rules = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(obj))
                rules.Add(entry.Name, AreaWeights(r, AsObject(r, entry), requirePositive: true));
            return rules;
        }

        // Area name to weight; every name must be a known area.
        private Dictionary<string, double> AreaWeights(DataReader r, JObject obj, bool requirePositive)
        {
            var weights = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(obj))
            {
                var path = DataReader.PathOf(obj, entry.Name);
                if (!areas.ContainsKey(entry.Name)) throw r.Fail(path, "is not an area listed in areas");
                weights.Add(entry.Name, r.Number(entry.Value, path, 0, 100));
            }
            if (requirePositive && weights.Values.Sum() <= 0) throw r.Fail(obj.Path, "must give at least one area a weight above 0");
            return weights;
        }

        private Dictionary<string, double> CategoryFactors(DataReader r, JObject obj)
        {
            var factors = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(obj))
            {
                var path = DataReader.PathOf(obj, entry.Name);
                if (!categories.ContainsKey(entry.Name)) throw r.Fail(path, "is not a category listed in categories");
                factors.Add(entry.Name, r.Number(entry.Value, path, 0, 100));
            }
            return factors;
        }

        private void ReadEconomy(DataReader r, JObject root,
            out Dictionary<string, ProductInfo> productTable,
            out Dictionary<string, EquipmentInfo> equipmentTable,
            out Dictionary<string, VehicleInfo> vehicleTable,
            out PriceCurve curve)
        {
            r.OnlyKeys(root, "schemaVersion", "products", "equipment", "vehicles", "priceCurve");
            r.SchemaVersion(root, EconomySchemaVersion);

            productTable = new Dictionary<string, ProductInfo>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(r.Object(root, "products")))
            {
                var obj = AsObject(r, entry);
                r.OnlyKeys(obj, "category", "serviceMinutes");
                var category = r.Text(obj, "category");
                if (!categories.ContainsKey(category))
                    throw r.Fail(DataReader.PathOf(obj, "category"), $"names '{category}', which is not a category in demand.json");
                productTable.Add(entry.Name, new ProductInfo(entry.Name, category, r.Number(obj, "serviceMinutes", 0.1, 120)));
            }
            if (productTable.Count == 0) throw r.Fail("products", "must list at least one product");

            equipmentTable = new Dictionary<string, EquipmentInfo>(StringComparer.Ordinal);
            var gameItemOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(r.Object(root, "equipment")))
            {
                var obj = AsObject(r, entry);
                r.OnlyKeys(obj, "products", "gameItems");
                var list = r.List(obj, "products");
                if (list.Count == 0) throw r.Fail(list.Path, "must list at least one product");
                var items = new List<string>();
                foreach (var token in list)
                {
                    var item = r.Text(token, token.Path);
                    if (!productTable.ContainsKey(item)) throw r.Fail(token.Path, $"names '{item}', which is not listed in products");
                    if (items.Contains(item)) throw r.Fail(token.Path, $"lists '{item}' twice");
                    items.Add(item);
                }
                var gameItems = new List<string>();
                var gameList = r.OptionalArray(obj, "gameItems");
                if (gameList != null)
                {
                    foreach (var token in gameList)
                    {
                        var item = r.Text(token, token.Path);
                        if (string.IsNullOrWhiteSpace(item)) throw r.Fail(token.Path, "must not be blank");
                        if (gameItemOwners.TryGetValue(item, out var owner))
                            throw r.Fail(token.Path, $"lists '{item}' again; it already maps to equipment '{owner}'");
                        gameItemOwners.Add(item, entry.Name);
                        gameItems.Add(item);
                    }
                }
                equipmentTable.Add(entry.Name, new EquipmentInfo(entry.Name, items, gameItems));
            }

            vehicleTable = new Dictionary<string, VehicleInfo>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(r.Object(root, "vehicles")))
            {
                var obj = AsObject(r, entry);
                r.OnlyKeys(obj, "equipmentSlots", "staff", "stockUnits");
                vehicleTable.Add(entry.Name, new VehicleInfo(entry.Name, r.Integer(obj, "equipmentSlots", 1, 64), r.Integer(obj, "staff", 1, 64),
                    r.Integer(obj, "stockUnits", 1, 100000)));
            }

            var curveObj = r.Object(root, "priceCurve");
            r.OnlyKeys(curveObj, "assumedPriceIndex", "points");
            var assumedIndex = r.Number(curveObj, "assumedPriceIndex", PriceCurve.LowestPriceIndex, 10);
            var points = r.List(curveObj, "points");
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var token in points)
            {
                if (!(token is JArray pair) || pair.Count != 2) throw r.Fail(token.Path, "must be a pair [price ratio, share of buyers]");
                var x = r.Number(pair[0], pair[0].Path, 0, 100);
                var y = r.Number(pair[1], pair[1].Path, 0, 10);
                if (xs.Count > 0 && x <= xs[xs.Count - 1]) throw r.Fail(pair[0].Path, "must be above the previous point's price ratio");
                if (ys.Count > 0 && y > ys[ys.Count - 1]) throw r.Fail(pair[1].Path, "must not be above the previous point's share: a higher price never brings more buyers");
                xs.Add(x);
                ys.Add(y);
            }
            if (xs.Count < 2) throw r.Fail(points.Path, "must have at least two points");
            if (assumedIndex >= xs[xs.Count - 1])
                throw r.Fail(DataReader.PathOf(curveObj, "assumedPriceIndex"), "must be below the last point's price ratio");
            curve = new PriceCurve(xs, ys, assumedIndex);
        }

        private IReadOnlyList<Zone> ReadZones(DataReader r, JObject root)
        {
            r.OnlyKeys(root, "schemaVersion", "zones");
            r.SchemaVersion(root, ZonesSchemaVersion);
            var zones = new List<Zone>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in r.List(root, "zones"))
            {
                if (!(token is JObject obj)) throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(obj, "id", "mode", "share", "areas", "polygon");
                var id = r.Text(obj, "id");
                if (!ids.Add(id)) throw r.Fail(DataReader.PathOf(obj, "id"), $"repeats zone id '{id}'");
                var modeText = r.Text(obj, "mode");
                ZoneMode mode;
                if (modeText == "override") mode = ZoneMode.Override;
                else if (modeText == "add") mode = ZoneMode.Add;
                else throw r.Fail(DataReader.PathOf(obj, "mode"), $"must be override or add, got '{modeText}'");
                double share = 1;
                if (mode == ZoneMode.Add) share = r.Number(obj, "share", 0, 1);
                else if (obj["share"] != null) throw r.Fail(DataReader.PathOf(obj, "share"), "only applies to add zones");
                var weights = AreaWeights(r, r.Object(obj, "areas"), requirePositive: true);
                var polygonList = r.List(obj, "polygon");
                var polygon = new List<ZonePoint>();
                foreach (var pointToken in polygonList)
                {
                    if (!(pointToken is JArray pair) || pair.Count != 2) throw r.Fail(pointToken.Path, "must be a point [x, z]");
                    polygon.Add(new ZonePoint(r.Number(pair[0], pair[0].Path, -1e7, 1e7), r.Number(pair[1], pair[1].Path, -1e7, 1e7)));
                }
                // A closing point that repeats the first is allowed and dropped.
                if (polygon.Count > 1 && polygon[0].X == polygon[polygon.Count - 1].X && polygon[0].Z == polygon[polygon.Count - 1].Z)
                    polygon.RemoveAt(polygon.Count - 1);
                var problem = PolygonProblem(polygon);
                if (problem != null) throw r.Fail(polygonList.Path, problem);
                zones.Add(new Zone(id, mode, share, AreaMix.FromWeights(weights), polygon));
            }
            return zones;
        }

        // Null for a simple polygon with an area; otherwise what is wrong with it.
        private static string? PolygonProblem(IReadOnlyList<ZonePoint> polygon)
        {
            if (polygon.Count < 3) return "needs at least three distinct points";
            var area = 0.0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if (a.X == b.X && a.Z == b.Z) return $"repeats point {i + 1} straight after itself";
                area += a.X * b.Z - b.X * a.Z;
            }
            // Edges that don't share a corner must not touch: the zone has to be a simple outline.
            for (var i = 0; i < polygon.Count; i++)
            {
                for (var j = i + 1; j < polygon.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == polygon.Count - 1)) continue;
                    if (SegmentsTouch(polygon[i], polygon[(i + 1) % polygon.Count], polygon[j], polygon[(j + 1) % polygon.Count]))
                        return $"crosses itself (edges {i + 1} and {j + 1})";
                }
            }
            if (Math.Abs(area) < 1e-6) return "encloses no area (all points on one line)";
            return null;
        }

        private static bool SegmentsTouch(ZonePoint a, ZonePoint b, ZonePoint c, ZonePoint d)
        {
            var d1 = Cross(c, d, a);
            var d2 = Cross(c, d, b);
            var d3 = Cross(a, b, c);
            var d4 = Cross(a, b, d);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            return (d1 == 0 && OnSegment(c, d, a)) || (d2 == 0 && OnSegment(c, d, b)) ||
                   (d3 == 0 && OnSegment(a, b, c)) || (d4 == 0 && OnSegment(a, b, d));
        }

        private static double Cross(ZonePoint a, ZonePoint b, ZonePoint p) => (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X);

        private static bool OnSegment(ZonePoint a, ZonePoint b, ZonePoint p) =>
            p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Z >= Math.Min(a.Z, b.Z) && p.Z <= Math.Max(a.Z, b.Z);

        private static JObject AsObject(DataReader r, JProperty entry) =>
            entry.Value as JObject ?? throw r.Fail(entry.Value.Path, "must be an object");

        private static double[] FullTable(DataReader r, JObject obj, string[] keys, double min, double max)
        {
            r.OnlyKeys(obj, keys);
            var values = new double[keys.Length];
            for (var i = 0; i < keys.Length; i++) values[i] = r.Number(obj, keys[i], min, max);
            return values;
        }

        // Missing keys mean 1.0.
        private static double[] PartialTable(DataReader r, JObject? obj, string[] keys)
        {
            var values = Enumerable.Repeat(1.0, keys.Length).ToArray();
            if (obj == null) return values;
            r.OnlyKeys(obj, keys);
            for (var i = 0; i < keys.Length; i++)
                if (obj[keys[i]] != null) values[i] = r.Number(obj, keys[i], 0, 100);
            return values;
        }

        private static List<string>? NameList(DataReader r, JObject obj, string name, IEnumerable<string> allowed, bool required)
        {
            var list = required ? r.List(obj, name) : r.OptionalArray(obj, name);
            if (list == null) return null;
            if (list.Count == 0) throw r.Fail(list.Path, "must not be empty (leave it out to mean all)");
            var known = allowed.ToList();
            var names = new List<string>();
            foreach (var token in list)
            {
                var value = r.Text(token, token.Path);
                if (!known.Contains(value)) throw r.Fail(token.Path, $"'{value}' is not one of: {string.Join(", ", known)}");
                if (!names.Contains(value)) names.Add(value);
            }
            return names;
        }

        private static bool[]? IndexSet(List<string>? names, string[] keys) =>
            names == null ? null : keys.Select(names.Contains).ToArray();
    }

    public sealed class AreaTable
    {
        private readonly double[] footfall;
        private readonly double[] days;

        internal AreaTable(string name, double[] footfall, double[] days)
        {
            Name = name;
            this.footfall = footfall;
            this.days = days;
        }

        public string Name { get; }
        public double Footfall(Daypart daypart) => footfall[(int)daypart];
        public double DayFactor(DayOfWeek day) => days[(int)day];
    }

    public sealed class CategoryTable
    {
        private readonly double[] appetite;

        internal CategoryTable(string name, double[] appetite, double newCustomerWeight, double addOnFactor)
        {
            Name = name;
            this.appetite = appetite;
            NewCustomerWeight = newCustomerWeight;
            AddOnFactor = addOnFactor;
        }

        public string Name { get; }
        public double NewCustomerWeight { get; }
        public double AddOnFactor { get; }
        public double Appetite(Daypart daypart) => appetite[(int)daypart];
    }

    public sealed class AppetiteRule
    {
        private readonly bool[]? dayparts;
        private readonly bool[]? days;
        private readonly bool[]? seasons;

        internal AppetiteRule(IReadOnlyList<string> categories, bool[]? dayparts, bool[]? days, bool[]? seasons, double factor)
        {
            Categories = categories;
            this.dayparts = dayparts;
            this.days = days;
            this.seasons = seasons;
            Factor = factor;
        }

        public IReadOnlyList<string> Categories { get; }
        public double Factor { get; }

        public bool Applies(string category, Daypart daypart, DayOfWeek day, Season season)
        {
            if (!Categories.Contains(category)) return false;
            if (dayparts != null && !dayparts[(int)daypart]) return false;
            if (days != null && !days[(int)day]) return false;
            return seasons == null || seasons[(int)season];
        }
    }

    public sealed class NeighbourhoodTable
    {
        internal static readonly NeighbourhoodTable Neutral = new NeighbourhoodTable("", 1,
            Enumerable.Repeat(1.0, 7).ToArray(), Enumerable.Repeat(1.0, 7).ToArray(), Enumerable.Repeat(1.0, 4).ToArray(),
            Array.Empty<AppetiteRule>());

        private readonly double[] dayparts;
        private readonly double[] days;
        private readonly double[] seasons;

        internal NeighbourhoodTable(string name, double footfall, double[] dayparts, double[] days, double[] seasons, IReadOnlyList<AppetiteRule> appetite)
        {
            Name = name;
            Footfall = footfall;
            this.dayparts = dayparts;
            this.days = days;
            this.seasons = seasons;
            Appetite = appetite;
        }

        public string Name { get; }
        public double Footfall { get; }
        public IReadOnlyList<AppetiteRule> Appetite { get; }
        public double DaypartFactor(Daypart daypart) => dayparts[(int)daypart];
        public double DayFactor(DayOfWeek day) => days[(int)day];
        public double SeasonFactor(Season season) => seasons[(int)season];

        public double AppetiteFactor(string category, Daypart daypart, DayOfWeek day, Season season)
        {
            var factor = 1.0;
            foreach (var rule in Appetite)
                if (rule.Applies(category, daypart, day, season)) factor *= rule.Factor;
            return factor;
        }
    }

    public sealed class MonthInfo
    {
        internal MonthInfo(Season season, double footfall)
        {
            Season = season;
            Footfall = footfall;
        }

        public Season Season { get; }
        public double Footfall { get; }
    }

    public sealed class ClassifierSettings
    {
        internal ClassifierSettings(double radiusMetres, double edgeWeight, int maxAreas, string fallbackArea,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> buildingTypes,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> businessTypes)
        {
            RadiusMetres = radiusMetres;
            EdgeWeight = edgeWeight;
            MaxAreas = maxAreas;
            FallbackArea = fallbackArea;
            BuildingTypes = buildingTypes;
            BusinessTypes = businessTypes;
        }

        public double RadiusMetres { get; }
        public double EdgeWeight { get; }
        public int MaxAreas { get; }
        public string FallbackArea { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> BuildingTypes { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> BusinessTypes { get; }
    }

    public sealed class ProductInfo
    {
        internal ProductInfo(string itemName, string category, double serviceMinutes)
        {
            ItemName = itemName;
            Category = category;
            ServiceMinutes = serviceMinutes;
        }

        public string ItemName { get; }
        public string Category { get; }
        public double ServiceMinutes { get; }
    }

    public sealed class EquipmentInfo
    {
        internal EquipmentInfo(string id, IReadOnlyList<string> products, IReadOnlyList<string> gameItems)
        {
            Id = id;
            Products = products;
            GameItems = gameItems;
        }

        public string Id { get; }
        public IReadOnlyList<string> Products { get; }
        // Vanilla item names that fit this piece of equipment. Missing gameItems means none.
        public IReadOnlyList<string> GameItems { get; }
    }

    public sealed class VehicleInfo
    {
        internal VehicleInfo(string type, int equipmentSlots, int staff, int stockUnits)
        {
            StockUnits = stockUnits;
            Type = type;
            EquipmentSlots = equipmentSlots;
            Staff = staff;
        }

        public string Type { get; }
        public int EquipmentSlots { get; }
        // Food the vehicle holds, in units, whatever the game's box slots allow: one wholesale box
        // can hold hundreds of units.
        public int StockUnits { get; }
        public int Staff { get; }
    }
}
