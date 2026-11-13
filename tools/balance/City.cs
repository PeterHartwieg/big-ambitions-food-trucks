using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Balance;

public sealed class Building
{
    public string Key = "";
    public string Region = "";
    public string Hood = "";
    public string BuildingType = "";
    public string? BusinessType;
    public string Size = "";
    public int Traffic;
    public double X, Z;
    public double[] Box = new double[4];

    public double DistanceFrom(double x, double z)
    {
        var dx = Math.Max(Math.Max(Box[0] - x, 0), x - Box[2]);
        var dz = Math.Max(Math.Max(Box[1] - z, 0), z - Box[3]);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public string Address
    {
        get
        {
            var parts = Key.Replace("ba:street_", "").Split('#');
            return $"{parts[1]} {parts[0]}";
        }
    }
}

public sealed class City
{
    public List<Building> Buildings { get; } = new();

    public static City Load(string path)
    {
        var city = new City();
        foreach (var b in (JArray)JObject.Parse(File.ReadAllText(path))["buildings"]!)
        {
            city.Buildings.Add(new Building
            {
                Key = (string)b["key"]!, Region = (string)b["region"]!, Hood = (string)b["hood"]!,
                BuildingType = (string)b["buildingType"]!, BusinessType = (string?)b["businessType"], Size = (string)b["size"]!,
                Traffic = (int)b["traffic"]!, X = (double)b["x"]!, Z = (double)b["z"]!,
                Box = ((JArray)b["box"]!).Select(v => (double)v).ToArray()
            });
        }
        return city;
    }

    public IEnumerable<Building> Wholesalers => Buildings.Where(b => b.BusinessType == "ba:businesstype_wholesalestore");

    public List<Building> Commissaries(string path)
    {
        var keys = ((JArray)JObject.Parse(File.ReadAllText(path))["commissaries"]!).Select(c => (string)c["address"]!).ToList();
        return Buildings.Where(b => keys.Contains(b.Key)).ToList();
    }

    public List<Spot> Spots(DemandTables tables)
    {
        var classifier = new AreaClassifier(tables);
        var radius = tables.Classifier.RadiusMetres;
        var spots = new List<Spot>();
        foreach (var b in Buildings)
        {
            var places = Buildings.Where(o => o.Region == b.Region)
                .Select(o => (o, d: o == b ? 0 : o.DistanceFrom(b.X, b.Z)))
                .Where(p => p.d <= radius)
                .Select(p => new NearbyPlace(p.o.BuildingType, p.o.BusinessType, p.d));
            spots.Add(new Spot(b.Address, classifier.Classify(places, b.X, b.Z), b.Hood, b.Region, b.X, b.Z));
        }
        return spots;
    }
}

public sealed class Travel
{
    public double MetresPerMinute { get; init; } = 4.0;
    public double Detour { get; init; } = 1.3;
    public int ShopMinutes { get; init; } = 10;

    public int Minutes(double metres) => (int)Math.Ceiling(metres * Detour / MetresPerMinute);

    public (int Minutes, Building? Wholesaler) Restock(City city, Spot from, Spot to, IEnumerable<Building>? depots = null)
    {
        var best = (Minutes: int.MaxValue, Wholesaler: (Building?)null);
        foreach (var w in (depots ?? city.Wholesalers).Where(w => w.Region == from.Region && w.Region == to.Region))
        {
            var d = Math.Sqrt((w.X - from.X) * (w.X - from.X) + (w.Z - from.Z) * (w.Z - from.Z)) +
                    Math.Sqrt((w.X - to.X) * (w.X - to.X) + (w.Z - to.Z) * (w.Z - to.Z));
            var m = Minutes(d) + ShopMinutes;
            if (m < best.Minutes) best = (m, w);
        }
        return best;
    }
}
