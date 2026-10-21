using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    // Everything the model needs about one vehicle at one moment. The game adapter fills it.
    public sealed class DemandQuery
    {
        public AreaMix Areas { get; set; } = null!;
        // The game's neighbourhood ID, e.g. "ba:neighborhood_midtown". Unknown or null is neutral.
        public string? Neighbourhood { get; set; }
        // Item names on the menu, e.g. "ba:itemname_hotdog". Items without a product entry are ignored.
        public IReadOnlyCollection<string> Menu { get; set; } = Array.Empty<string>();
        public int MinuteOfDay { get; set; }
        public DayOfWeek Day { get; set; }
        // 1 to 12, from DemandTables.MonthOf(game day) or from a seasons mod.
        public int Month { get; set; } = 4;
        public bool Raining { get; set; }
        // The game's demand per item in this neighbourhood, 0 to 100. Missing items count as 100.
        public IReadOnlyDictionary<string, double>? MarketDemand { get; set; }
        // Health grade, regulars and the footfall other vehicles bring, combined (ReputationFactor.Combine).
        public double Reputation { get; set; } = 1.0;
        // Each category's share of its customers left by the player's other vehicles nearby
        // (Competition.Evaluate). Missing categories, or null, keep all their customers.
        public IReadOnlyDictionary<string, double>? CategoryShares { get; set; }
        // Each area's crowd now against what the area tables assume: venues open or closed, surges
        // (AreaClassifier.LiveFactors). Missing areas, or null, are as usual.
        public IReadOnlyDictionary<string, double>? AreaLive { get; set; }
        // The game's customer promotion variable relative to its default, so difficulty reaches trucks.
        public double Promotion { get; set; } = 1.0;
        // A premium spot's or an event's extra passers (PitchBonus); 1.0 on an ordinary pitch.
        public double PitchFootfall { get; set; } = 1.0;
        // The vehicle's paid marketing for today (MarketingRules); 1.0 without any.
        public double Marketing { get; set; } = 1.0;
        // A premium spot's or an event's own crowd: each area at least this presence, whatever the venues
        // nearby are doing (AreaLive applies to the spot's own areas only). Null on an ordinary pitch.
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
        // People passing per hour.
        public double Footfall { get; }
        // Contributions in passers/hour, including neighbourhood, conditions and promotion.
        public double Baseline { get; }
        public IReadOnlyDictionary<string, double> AreaFootfall { get; }
        // Share of passers who buy from this menu, market included.
        public double Appetite { get; }
        // How much the game's market cut the appetite: 1.0 is no cut.
        public double Market { get; }
        // Footfall × appetite × reputation: customers per hour before price and service limits.
        public double CustomersPerHour { get; }
        // Extra items (cold drinks) per customer on top of their main order.
        public double AddOnsPerCustomer { get; }
        // Each menu category's part of the customers, summing to 1 (empty when nobody comes).
        public IReadOnlyDictionary<string, double> CustomerShares { get; }
    }

    // customers/hour = footfall × appetite(menu) × market × conditions × reputation (design: Demand).
    // Conditions (season, rain) act on footfall and on each category's appetite; the market acts per
    // category, so a burger truck among burger bars loses only its burger customers.
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
            // After midnight the night out still belongs to the evening before.
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
                var market = Market(group.Value, query.MarketDemand);
                var addOns = category.AddOnFactor > 0
                    ? Math.Min(1.0, appetite * market * (1 - category.NewCustomerWeight) * category.AddOnFactor)
                        * CategoryShare(query.CategoryShares, category.Name)
                    : 0;
                shares.Add((category.Name, appetite * market * category.NewCustomerWeight, appetite * category.NewCustomerWeight, addOns));
            }

            // Other vehicles split a category's customers after the menu's ranking, so a contested
            // category never promotes another one to the menu's full-weight slot.
            var contributions = Shared(Contributions(shares.Select(s => (s.Category, s.WithMarket))), query.CategoryShares);
            var menuAppetite = Math.Min(1.0, contributions.Values.Sum());
            var withoutMarket = Math.Min(1.0, Shared(Contributions(shares.Select(s => (s.Category, s.WithoutMarket))), query.CategoryShares).Values.Sum());
            var marketFactor = withoutMarket > 0 ? menuAppetite / withoutMarket : 1.0;

            // Each category's part of the customers, after the further-category discount.
            var customerShares = new Dictionary<string, double>(StringComparer.Ordinal);
            var contributionTotal = contributions.Values.Sum();
            if (contributionTotal > 0)
                foreach (var pair in contributions) customerShares[pair.Key] = pair.Value / contributionTotal;

            // Add-ons ride only with customers who came for a main item, not with drink-only customers.
            var mainShare = shares.Where(s => tables.Category(s.Category).AddOnFactor <= 0)
                .Sum(s => customerShares.TryGetValue(s.Category, out var share) ? share : 0);
            var addOnsPerCustomer = mainShare * shares.Sum(s => s.AddOns);

            var customers = footfall * menuAppetite * query.Reputation;
            return new DemandEstimate(daypart, season, footfall, menuAppetite, marketFactor, customers, addOnsPerCustomer, customerShares,
                baseline * footfallFactor, areaCrowds.ToDictionary(p => p.Key, p => p.Value * footfallFactor, StringComparer.Ordinal));
        }

        // Weight appetite by the crowd here now, including the baseline's neutral factor of 1.
        // Common footfall factors cancel; using unscaled crowds also handles promotion/conditions of zero.
        // Older data can have no baseline and no area crowd; appetite stays neutral with no passers.
        private double AreaAppetite(IReadOnlyDictionary<string, double> crowds, double baseline, double total, string category) =>
            total > 0 ? (baseline + crowds.Sum(p => p.Value * tables.Area(p.Key).AppetiteFactor(category))) / total : 1.0;

        // Independent crowds before the factors common to every passer, in a stable area order.
        // A pitch's own areas (PitchAreas) raise an area's presence to at least theirs, after the venues' live factor.
        // An area's own season and rain factors act on its crowd only, on top of the factors every passer has.
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

        // What each category adds to the menu's appetite: the strongest counts in full, each further one
        // adds FurtherCategoryWeight of its share. Ties go to the category name, so menu order never matters.
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

        // The game's demand / 100 for the category's items on the menu, averaged, kept between floor and ceiling.
        private double Market(IReadOnlyList<string> items, IReadOnlyDictionary<string, double>? demand)
        {
            var sum = 0.0;
            foreach (var item in items)
            {
                var value = 1.0;
                if (demand != null && demand.TryGetValue(item, out var raw) && !double.IsNaN(raw) && !double.IsInfinity(raw))
                    value = raw / 100.0;
                sum += Math.Max(tables.MarketFloor, Math.Min(tables.MarketCeiling, value));
            }
            return sum / items.Count;
        }

        // Menu items grouped by category, in a stable order; unknown items and duplicates dropped.
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
            // Fixed item order keeps the market average bit-identical whatever the menu order.
            foreach (var items in groups.Values) items.Sort(StringComparer.Ordinal);
            return groups;
        }

        private static Dictionary<string, double> Shared(Dictionary<string, double> contributions, IReadOnlyDictionary<string, double>? shares)
        {
            if (shares == null) return contributions;
            // The menu's appetite is capped at 1. Apply that cap to the whole menu first, so splitting
            // one category's customers can't free room under the cap for the others.
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
