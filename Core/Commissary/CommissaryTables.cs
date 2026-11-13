using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Pitches;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Commissary
{
    public sealed class CommissarySite
    {
        public CommissarySite(string id, string name, string address, (double X, double Z)? bay, double? bayHeading = null)
        {
            if (bayHeading != null && bay == null) throw new ArgumentException("A bay heading needs a bay point.", nameof(bayHeading));
            Id = id;
            Name = name;
            Address = address;
            Bay = bay;
            BayHeading = bayHeading;
        }

        public string Id { get; }
        public string Name { get; }
        public string Address { get; }
        public (double X, double Z)? Bay { get; }
        public double? BayHeading { get; }

        public bool InLot => BayHeading != null;
    }

    public sealed class CommissaryTables
    {
        public const int SchemaVersion = 1;

        public CommissaryTables(IReadOnlyList<CommissarySite> sites)
        {
            Sites = sites ?? throw new ArgumentNullException(nameof(sites));
        }

        public IReadOnlyList<CommissarySite> Sites { get; }

        public CommissarySite? Site(string? id) => id == null ? null : Sites.FirstOrDefault(s => s.Id == id);

        public CommissarySite? At(Func<CommissarySite, IEnumerable<(double X, double Z)>?> bays, double x, double z, double radiusMetres)
        {
            if (bays == null) throw new ArgumentNullException(nameof(bays));
            CommissarySite? best = null;
            var bestDistance = double.MaxValue;
            foreach (var site in Sites)
            {
                var distance = Distance(bays(site), x, z);
                if (distance <= radiusMetres && distance < bestDistance)
                {
                    best = site;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static double Distance(IEnumerable<(double X, double Z)>? points, double x, double z)
        {
            if (points == null) return double.MaxValue;
            var best = double.MaxValue;
            foreach (var (px, pz) in points)
                best = Math.Min(best, Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z)));
            return best;
        }

        public static CommissaryTables Parse(string? json)
        {
            var r = new DataReader("commissaries.json");
            var root = r.Parse(json);
            r.OnlyKeys(root, "schemaVersion", "commissaries");
            r.SchemaVersion(root, SchemaVersion);
            var sites = new List<CommissarySite>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in r.List(root, "commissaries"))
            {
                var site = token as JObject ?? throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(site, "id", "name", "address", "bay");
                var id = r.Text(site, "id");
                if (!id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-'))
                    throw r.Fail(DataReader.PathOf(site, "id"), "must be lower-case letters, digits and dashes");
                if (!ids.Add(id)) throw r.Fail(DataReader.PathOf(site, "id"), "must be unique");
                var address = r.Text(site, "address");
                var hash = address.LastIndexOf('#');
                if (hash <= 0 || !int.TryParse(address.Substring(hash + 1), out var number) || number < 0)
                    throw r.Fail(DataReader.PathOf(site, "address"), "must be street#number, as in ba:street_fifthstreet#2");
                if (!addresses.Add(address)) throw r.Fail(DataReader.PathOf(site, "address"), "must be unique");
                (double, double)? bay = null;
                double? heading = null;
                var point = r.OptionalObject(site, "bay");
                if (point != null)
                {
                    r.OnlyKeys(point, "x", "z", "heading");
                    bay = (r.Number(point, "x", -100_000, 100_000), r.Number(point, "z", -100_000, 100_000));
                    if (point["heading"] != null) heading = r.Number(point, "heading", -360, 360);
                }
                sites.Add(new CommissarySite(id, r.Text(site, "name"), address, bay, heading));
            }
            if (sites.Count == 0) throw r.Fail("commissaries", "must not be empty");
            return new CommissaryTables(sites);
        }

        public static (string Street, int Number) SplitAddress(string address) => PitchTables.SplitAddress(address);
    }
}
