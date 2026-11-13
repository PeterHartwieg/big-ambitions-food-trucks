using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    public sealed class DemandQuery
    {
        public AreaMix Areas { get; set; } = null!;
        public string? Neighbourhood { get; set; }
        public IReadOnlyCollection<string> Menu { get; set; } = Array.Empty<string>();
        public int MinuteOfDay { get; set; }
        public DayOfWeek Day { get; set; }
        public int Month { get; set; } = 4;
        public bool Raining { get; set; }
        public IReadOnlyDictionary<string, double>? MarketDemand { get; set; }
        public double Reputation { get; set; } = 1.0;
        public IReadOnlyDictionary<string, double>? CategoryShares { get; set; }
        public IReadOnlyDictionary<string, double>? AreaLive { get; set; }
        public double Promotion { get; set; } = 1.0;
        public double PitchFootfall { get; set; } = 1.0;
        public double Marketing { get; set; } = 1.0;
        public AreaMix? PitchAreas { get; set; }
    }

    public sealed class DemandEstimate
    {
        internal DemandEstimate(Daypart daypart, Season season, double footfall, double appetite, double market,
            double customersPerHour, double addOnsPerCustomer, IReadOnlyDictionary<string, double> customerShares,
            double baseline, IReadOnlyDictionary<string, double> areaFootfall)
        {
            Daypart = daypart;
            Season = season;
            Footfall = footfall;
            Appetite = appetite;
            Market = market;
            CustomersPerHour = customersPerHour;
            AddOnsPerCustomer = addOnsPerCustomer;
            CustomerShares = customerShares;
            Baseline = baseline;
            AreaFootfall = areaFootfall;
        }

        public Daypart Daypart { get; }
        public Season Season { get; }
        public double Footfall { get; }
        public double Baseline { get; }
        public IReadOnlyDictionary<string, double> AreaFootfall { get; }
        public double Appetite { get; }
        public double Market { get; }
        public double CustomersPerHour { get; }
        public double AddOnsPerCustomer { get; }
        public IReadOnlyDictionary<string, double> CustomerShares { get; }
    }

    public sealed class DemandModel
    {
        private readonly DemandTables tables;

        public DemandModel(DemandTables tables) => this.tables = tables ?? throw new ArgumentNullException(nameof(tables));

        public DemandEstimate Estimate(DemandQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.Areas == null) throw new ArgumentException("Areas must be set.", nameof(query));
            if (query.Menu == null) throw new ArgumentException("Menu must be set.", nameof(query));
            if (query.MinuteOfDay < 0 || query.MinuteOfDay >= 1440)
                throw new ArgumentOutOfRangeException(nameof(query), "MinuteOfDay must be 0 to 1439.");
            if (query.Day < DayOfWeek.Sunday || query.Day > DayOfWeek.Saturday)
                throw new ArgumentOutOfRangeException(nameof(query), "Day must be a day of the week.");
            if (query.Month < 1 || query.Month > 12)
                throw new ArgumentOutOfRangeException(nameof(query), "Month must be 1 to 12.");
            RequireFactor(query.Reputation, nameof(query.Reputation));
            RequireFactor(query.Promotion, nameof(query.Promotion));
            RequireFactor(query.PitchFootfall, nameof(query.PitchFootfall));
            RequireFactor(query.Marketing, nameof(query.Marketing));
            foreach (var area in query.Areas.Presences.Keys.Concat(query.PitchAreas?.Presences.Keys ?? Enumerable.Empty<string>()))
                if (!tables.HasArea(area)) throw new ArgumentException($"Unknown area type '{area}'.", nameof(query));

            var daypart = tables.DaypartAt(query.MinuteOfDay);
            var day = tables.IsBeforeEarly(query.MinuteOfDay) ? (DayOfWeek)(((int)query.Day + 6) % 7) : query.Day;
            var month = tables.Month(query.Month);
            var season = month.Season;
            var neighbourhood = tables.Neighbourhood(query.Neighbourhood);

            var baseline = tables.StreetBaseline(daypart);
            var areaCrowds = AreaCrowds(query.Areas, daypart, day, season, query.Raining, query.AreaLive, query.PitchAreas);
            var crowdTotal = baseline + areaCrowds.Values.Sum();
            var footfallFactor = tables.PassersPerHour
                * neighbourhood.Footfall * neighbourhood.DaypartFactor(daypart) * neighbourhood.DayFactor(day) * neighbourhood.SeasonFactor(season)
                * month.Footfall * (query.Raining ? tables.RainFootfall : 1.0) * query.Promotion * query.PitchFootfall * query.Marketing;
            var footfall = crowdTotal * footfallFactor;

            var shares = new List<(string Category, double WithMarket, double WithoutMarket, double AddOns)>();
            foreach (var group in MenuCategories(query.Menu))
            {
                var category = tables.Category(group.Key);
                var appetite = category.Appetite(daypart)
                    * AreaAppetite(areaCrowds, baseline, crowdTotal, category.Name)
                    * neighbourhood.AppetiteFactor(category.Name, daypart, day, season)
                    * tables.SeasonAppetite(season, category.Name)
                    * (query.Raining ? tables.RainAppetite(category.Name) : 1.0);
                appetite = Math.Min(1.0, appetite);
                var market = Market(group.Value, query.MarketDemand, category.MarketFloor);
                var addOns = category.AddOnFactor > 0
                    ? Math.Min(1.0, appetite * market * (1 - category.NewCustomerWeight) * category.AddOnFactor)
                        * CategoryShare(query.CategoryShares, category.Name)
                    : 0;
                shares.Add((category.Name, appetite * market * category.NewCustomerWeight, appetite * category.NewCustomerWeight, addOns));
            }

            var contributions = Shared(Contributions(shares.Select(s => (s.Category, s.WithMarket))), query.CategoryShares);
            var menuAppetite = Math.Min(1.0, contributions.Values.Sum());
            var withoutMarket = Math.Min(1.0, Shared(Contributions(shares.Select(s => (s.Category, s.WithoutMarket))), query.CategoryShares).Values.Sum());
            var marketFactor = withoutMarket > 0 ? menuAppetite / withoutMarket : 1.0;

            var customerShares = new Dictionary<string, double>(StringComparer.Ordinal);
            var contributionTotal = contributions.Values.Sum();
            if (contributionTotal > 0)
                foreach (var pair in contributions) customerShares[pair.Key] = pair.Value / contributionTotal;

            var mainShare = shares.Where(s => tables.Category(s.Category).AddOnFactor <= 0)
                .Sum(s => customerShares.TryGetValue(s.Category, out var share) ? share : 0);
            var addOnsPerCustomer = mainShare * shares.Sum(s => s.AddOns);

            var customers = footfall * menuAppetite * query.Reputation;
            return new DemandEstimate(daypart, season, footfall, menuAppetite, marketFactor, customers, addOnsPerCustomer, customerShares,
                baseline * footfallFactor, areaCrowds.ToDictionary(p => p.Key, p => p.Value * footfallFactor, StringComparer.Ordinal));
        }

        private double AreaAppetite(IReadOnlyDictionary<string, double> crowds, double baseline, double total, string category) =>
            total > 0 ? (baseline + crowds.Sum(p => p.Value * tables.Area(p.Key).AppetiteFactor(category))) / total : 1.0;

        private Dictionary<string, double> AreaCrowds(AreaMix areas, Daypart daypart, DayOfWeek day, Season season, bool raining,
            IReadOnlyDictionary<string, double>? live, AreaMix? pitch = null)
        {
            var crowds = new Dictionary<string, double>(StringComparer.Ordinal);
            var names = areas.Presences.Keys.Concat(pitch?.Presences.Keys ?? Enumerable.Empty<string>()).Distinct().OrderBy(k => k, StringComparer.Ordinal);
            foreach (var name in names)
            {
                var area = tables.Area(name);
                var factor = live != null && live.TryGetValue(name, out var f) && !double.IsNaN(f) && !double.IsInfinity(f) && f >= 0 ? f : 1.0;
                var presence = Math.Max(areas.PresenceOf(name) * factor, pitch?.PresenceOf(name) ?? 0);
                crowds.Add(name, presence * area.Footfall(daypart, day) * area.DayFactor(day) * area.SeasonFactor(season)
                    * (raining ? area.Rain : 1.0));
            }
            return crowds;
        }

        private Dictionary<string, double> Contributions(IEnumerable<(string Category, double Share)> shares)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            var first = true;
            foreach (var s in shares.OrderByDescending(s => s.Share).ThenBy(s => s.Category, StringComparer.Ordinal))
            {
                result[s.Category] = first ? s.Share : tables.FurtherCategoryWeight * s.Share;
                first = false;
            }
            return result;
        }

        private double Market(IReadOnlyList<string> items, IReadOnlyDictionary<string, double>? demand, double categoryFloor)
        {
            var sum = 0.0;
            foreach (var item in items)
            {
                var value = 1.0;
                if (demand != null && demand.TryGetValue(item, out var raw) && !double.IsNaN(raw) && !double.IsInfinity(raw))
                    value = raw / 100.0;
                sum += Math.Min(tables.MarketCeiling, Math.Max(Math.Max(tables.MarketFloor, categoryFloor), value));
            }
            return sum / items.Count;
        }

        private SortedDictionary<string, List<string>> MenuCategories(IEnumerable<string> menu)
        {
            var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var item in menu)
            {
                var product = tables.Product(item);
                if (product == null) continue;
                if (!groups.TryGetValue(product.Category, out var items)) groups[product.Category] = items = new List<string>();
                if (!items.Contains(product.ItemName)) items.Add(product.ItemName);
            }
            foreach (var items in groups.Values) items.Sort(StringComparer.Ordinal);
            return groups;
        }

        private static Dictionary<string, double> Shared(Dictionary<string, double> contributions, IReadOnlyDictionary<string, double>? shares)
        {
            if (shares == null) return contributions;
            var total = contributions.Values.Sum();
            var cap = total > 1.0 ? 1.0 / total : 1.0;
            foreach (var category in contributions.Keys.ToList()) contributions[category] *= cap * CategoryShare(shares, category);
            return contributions;
        }

        private static double CategoryShare(IReadOnlyDictionary<string, double>? shares, string category)
        {
            if (shares == null || !shares.TryGetValue(category, out var share)) return 1.0;
            return double.IsNaN(share) || double.IsInfinity(share) ? 1.0 : Math.Max(0, Math.Min(1, share));
        }

        private static void RequireFactor(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, $"{name} must be finite and not negative.");
        }
    }
}
