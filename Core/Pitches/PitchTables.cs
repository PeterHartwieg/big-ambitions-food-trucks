using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Pitches
{
    // A curated pitch outside one building (design: Pitches). The game side finds the building by its address
    // and measures from its entrances; a vehicle within the premium radius stands on the spot.
    public sealed class PremiumSpot
    {
        public PremiumSpot(string id, string name, string address, AreaMix areas, double footfall)
        {
            Id = id;
            Name = name;
            Address = address;
            Areas = areas;
            Footfall = footfall;
        }

        public string Id { get; }
        // Locale key of the spot's name.
        public string Name { get; }
        // "street#number", as the game's address: "ba:street_fifthstreet#2".
        public string Address { get; }
        // The spot's character: areas raised to at least these presences, as a zone in add mode does.
        public AreaMix Areas { get; }
        // Multiplies the passers at the spot.
        public double Footfall { get; }
    }

    // A place the city holds events at.
    public sealed class EventPlace
    {
        public EventPlace(string id, string name, string address, IReadOnlyList<string> kinds)
        {
            Id = id;
            Name = name;
            Address = address;
            Kinds = kinds;
        }

        public string Id { get; }
        public string Name { get; }
        public string Address { get; }
        // The event kinds held here.
        public IReadOnlyList<string> Kinds { get; }
    }

    // A kind of city event: on which weekdays, how likely, how far ahead the city announces it, its hours, the
    // crowd it brings and what a vendor's place costs.
    public sealed class EventKind
    {
        public EventKind(string id, string name, IReadOnlyList<DayOfWeek> days, double chance, int leadDays, int start, int end,
            double footfall, AreaMix areas, int places, long feeCents)
        {
            Id = id;
            Name = name;
            Days = days;
            Chance = chance;
            LeadDays = leadDays;
            Start = start;
            End = end;
            Footfall = footfall;
            Areas = areas;
            Places = places;
            FeeCents = feeCents;
        }

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<DayOfWeek> Days { get; }
        // Chance of an event of this kind on each of its weekdays.
        public double Chance { get; }
        public int LeadDays { get; }
        // Minutes after the day's midnight; End is after Start, within the day.
        public int Start { get; }
        public int End { get; }
        public double Footfall { get; }
        public AreaMix Areas { get; }
        // Places the player's vehicles can book; the rest go to other vendors.
        public int Places { get; }
        public long FeeCents { get; }
    }

    // Venues the player owns that a vehicle can partner with, and what the partner gets.
    public sealed class PartnershipRules
    {
        public PartnershipRules(double radiusMetres = 50, double surge = 1.5, IReadOnlyList<VenueKind>? venues = null,
            int satisfactionBonus = 5, int minWorkedMinutes = 60)
        {
            RadiusMetres = radiusMetres;
            Surge = surge;
            Venues = venues ?? new[] { VenueKind.Nightclub, VenueKind.Theatre, VenueKind.Cinema, VenueKind.Office, VenueKind.Factory };
            SatisfactionBonus = satisfactionBonus;
            MinWorkedMinutes = minWorkedMinutes;
        }

        public double RadiusMetres { get; }
        // Multiplies the partner venue's surges (a club's closing, a show letting out).
        public double Surge { get; }
        public IReadOnlyList<VenueKind> Venues { get; }
        // Added to the venue's facility satisfaction after a day the partner worked outside it.
        public int SatisfactionBonus { get; }
        // Minutes open outside the venue that count as having worked there that day.
        public int MinWorkedMinutes { get; }
    }

    // The validated contents of data/pitches.json: premium spots, event places and kinds, partnerships.
    public sealed class PitchTables
    {
        public const int SchemaVersion = 1;

        private static readonly string[] WeekdayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public PitchTables(double premiumRadiusMetres, IReadOnlyList<PremiumSpot> spots, double eventRadiusMetres,
            IReadOnlyList<EventPlace> places, IReadOnlyList<EventKind> kinds, PartnershipRules partnerships)
        {
            PremiumRadiusMetres = premiumRadiusMetres;
            Spots = spots;
            EventRadiusMetres = eventRadiusMetres;
            Places = places;
            Kinds = kinds;
            Partnerships = partnerships;
        }

        public double PremiumRadiusMetres { get; }
        public IReadOnlyList<PremiumSpot> Spots { get; }
        public double EventRadiusMetres { get; }
        public IReadOnlyList<EventPlace> Places { get; }
        public IReadOnlyList<EventKind> Kinds { get; }
        public PartnershipRules Partnerships { get; }

        public PremiumSpot? Spot(string? id) => id == null ? null : Spots.FirstOrDefault(s => s.Id == id);
        public EventPlace? Place(string? id) => id == null ? null : Places.FirstOrDefault(p => p.Id == id);
        public EventKind? Kind(string? id) => id == null ? null : Kinds.FirstOrDefault(k => k.Id == id);

        // Areas are checked against the demand tables, so a typo fails at load, not in the middle of a sale.
        public static PitchTables Parse(string? json, DemandTables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var r = new DataReader("pitches.json");
            var root = r.Parse(json);
            r.OnlyKeys(root, "schemaVersion", "premium", "events", "partnerships");
            r.SchemaVersion(root, SchemaVersion);

            var premium = r.Object(root, "premium");
            r.OnlyKeys(premium, "radiusMetres", "spots");
            var spots = new List<PremiumSpot>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in r.List(premium, "spots"))
            {
                var spot = token as JObject ?? throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(spot, "id", "name", "address", "areas", "footfall");
                var id = r.Text(spot, "id");
                if (!ids.Add(id)) throw r.Fail(DataReader.PathOf(spot, "id"), "must be unique");
                spots.Add(new PremiumSpot(id, r.Text(spot, "name"), Address(r, spot), Areas(r, spot, tables), r.Number(spot, "footfall", 0, 10)));
            }

            var events = r.Object(root, "events");
            r.OnlyKeys(events, "radiusMetres", "places", "kinds");
            var kinds = new List<EventKind>();
            var kindIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in r.List(events, "kinds"))
            {
                var kind = token as JObject ?? throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(kind, "id", "name", "days", "chance", "leadDays", "start", "end", "footfall", "areas", "places", "fee");
                var id = r.Text(kind, "id");
                if (!kindIds.Add(id)) throw r.Fail(DataReader.PathOf(kind, "id"), "must be unique");
                var days = new List<DayOfWeek>();
                var dayList = r.List(kind, "days");
                if (dayList.Count == 0) throw r.Fail(dayList.Path, "must not be empty");
                foreach (var day in dayList)
                {
                    var index = Array.IndexOf(WeekdayNames, r.Text(day, day.Path));
                    if (index < 0) throw r.Fail(day.Path, "must be a weekday from sun to sat");
                    if (!days.Contains((DayOfWeek)index)) days.Add((DayOfWeek)index);
                }
                var start = Time(r, kind, "start");
                var end = Time(r, kind, "end");
                if (end <= start) throw r.Fail(DataReader.PathOf(kind, "end"), "must be after start, on the same day");
                kinds.Add(new EventKind(id, r.Text(kind, "name"), days, r.Number(kind, "chance", 0, 1), r.Integer(kind, "leadDays", 1, 14),
                    start, end, r.Number(kind, "footfall", 0, 20), Areas(r, kind, tables), r.Integer(kind, "places", 1, 10),
                    (long)Math.Round(r.Number(kind, "fee", 0, 1_000_000) * 100)));
            }

            var places = new List<EventPlace>();
            var placeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in r.List(events, "places"))
            {
                var place = token as JObject ?? throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(place, "id", "name", "address", "kinds");
                var id = r.Text(place, "id");
                if (!placeIds.Add(id)) throw r.Fail(DataReader.PathOf(place, "id"), "must be unique");
                var placeKinds = new List<string>();
                var kindList = r.List(place, "kinds");
                if (kindList.Count == 0) throw r.Fail(kindList.Path, "must not be empty");
                foreach (var k in kindList)
                {
                    var name = r.Text(k, k.Path);
                    if (!kindIds.Contains(name)) throw r.Fail(k.Path, "must be one of the event kinds");
                    if (!placeKinds.Contains(name)) placeKinds.Add(name);
                }
                places.Add(new EventPlace(id, r.Text(place, "name"), Address(r, place), placeKinds));
            }

            var partners = r.Object(root, "partnerships");
            r.OnlyKeys(partners, "radiusMetres", "surge", "venues", "satisfactionBonus", "minWorkedMinutes");
            var venues = new List<VenueKind>();
            foreach (var token in r.List(partners, "venues"))
            {
                var text = r.Text(token, token.Path);
                if (!Enum.TryParse<VenueKind>(text, true, out var venue) || venue == VenueKind.Other)
                    throw r.Fail(token.Path, "must be nightclub, theatre, cinema, office or factory");
                if (!venues.Contains(venue)) venues.Add(venue);
            }
            var partnerships = new PartnershipRules(r.Number(partners, "radiusMetres", 1, 500), r.Number(partners, "surge", 1, 10), venues,
                r.Integer(partners, "satisfactionBonus", 0, 100), r.Integer(partners, "minWorkedMinutes", 0, 1440));

            return new PitchTables(r.Number(premium, "radiusMetres", 1, 500), spots, r.Number(events, "radiusMetres", 1, 500), places, kinds, partnerships);
        }

        private static string Address(DataReader r, JObject obj)
        {
            var address = r.Text(obj, "address");
            var hash = address.LastIndexOf('#');
            if (hash <= 0 || !int.TryParse(address.Substring(hash + 1), out var number) || number < 0)
                throw r.Fail(DataReader.PathOf(obj, "address"), "must be street#number, as in ba:street_fifthstreet#2");
            return address;
        }

        private static AreaMix Areas(DataReader r, JObject obj, DemandTables tables)
        {
            var areas = r.Object(obj, "areas");
            var presences = new List<KeyValuePair<string, double>>();
            foreach (var entry in r.Entries(areas))
            {
                if (!tables.HasArea(entry.Name)) throw r.Fail(entry.Path, "is not an area in demand.json");
                presences.Add(new KeyValuePair<string, double>(entry.Name, r.Number(entry.Value, entry.Value.Path, 0, 1)));
            }
            return AreaMix.FromPresences(presences);
        }

        private static int Time(DataReader r, JObject obj, string name)
        {
            var token = r.Required(obj, name);
            var text = r.Text(token, token.Path);
            if (text.Length != 5 || text[2] != ':' || text.Where((c, n) => n != 2).Any(c => c < '0' || c > '9'))
                throw r.Fail(token.Path, "must be a time HH:MM");
            var hour = int.Parse(text.Substring(0, 2));
            var minute = int.Parse(text.Substring(3, 2));
            if (hour > 23 || minute > 59) throw r.Fail(token.Path, "must be a time HH:MM");
            return hour * 60 + minute;
        }

        // Splits "street#number" into the game's street name and number.
        public static (string Street, int Number) SplitAddress(string address)
        {
            var hash = address.LastIndexOf('#');
            return (address.Substring(0, hash), int.Parse(address.Substring(hash + 1)));
        }
    }
}
