using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Demand
{
    public enum VenueKind { Other, Nightclub, Theatre, Cinema, Office, Factory }

    // What the game shows of one venue near a pitch this hour (design: Live crowds from game businesses).
    public sealed class VenueNow
    {
        public VenueKind Kind { get; set; }
        public bool Open { get; set; }
        // Minutes since it closed, when it was open at some point in the last hour; null otherwise.
        public int? ClosedMinutesAgo { get; set; }
        // Staff at work now (offices and factories); null when the game doesn't say.
        public int? Workers { get; set; }
        // Guests in the last hour (the player's own nightclubs, theatres and cinemas); null when the game doesn't say.
        public int? Guests { get; set; }
        // The venue is the vehicle's partner (Pitches.Partnerships): the game side counts its crowd in full.
        public bool Partner { get; set; }
    }

    // How much of its usual crowd a venue brings now. A closed venue brings nobody; theatres and cinemas
    // let their audiences out at fixed times, a club's guests come out when it closes, and an office or
    // factory brings as many people as it has at work. 1.0 is the crowd the area tables assume.
    public sealed class LiveCrowds
    {
        public LiveCrowds(double closedWeight = 0, int surgeMinutes = 45, double theatreSurge = 2.5, IReadOnlyList<int>? theatreLetOuts = null,
            IReadOnlyList<int>? theatreWeekendLetOuts = null, double cinemaSurge = 2.0, int cinemaFirst = 17 * 60, int cinemaEvery = 150,
            int cinemaLast = 24 * 60 + 30, double weekendSurge = 1.5, int clubClosingMinutes = 30, double clubClosingSurge = 2.5,
            int referenceWorkers = 10, double minWorkerWeight = 0.3, double maxWorkerWeight = 2.0,
            IReadOnlyDictionary<string, VenueKind>? kinds = null, int referenceGuests = 20, double minGuestWeight = 0.3, double maxGuestWeight = 2.0)
        {
            ReferenceGuests = referenceGuests;
            MinGuestWeight = minGuestWeight;
            MaxGuestWeight = maxGuestWeight;
            ClosedWeight = closedWeight;
            SurgeMinutes = surgeMinutes;
            TheatreSurge = theatreSurge;
            TheatreLetOuts = theatreLetOuts ?? new[] { 22 * 60 + 30 };
            TheatreWeekendLetOuts = theatreWeekendLetOuts ?? new[] { 17 * 60 };
            CinemaSurge = cinemaSurge;
            CinemaFirst = cinemaFirst;
            CinemaEvery = cinemaEvery;
            CinemaLast = cinemaLast;
            WeekendSurge = weekendSurge;
            ClubClosingMinutes = clubClosingMinutes;
            ClubClosingSurge = clubClosingSurge;
            ReferenceWorkers = referenceWorkers;
            MinWorkerWeight = minWorkerWeight;
            MaxWorkerWeight = maxWorkerWeight;
            Kinds = kinds ?? new Dictionary<string, VenueKind>(StringComparer.Ordinal);
        }

        public double ClosedWeight { get; }
        public int SurgeMinutes { get; }
        public double TheatreSurge { get; }
        public IReadOnlyList<int> TheatreLetOuts { get; }
        public IReadOnlyList<int> TheatreWeekendLetOuts { get; }
        public double CinemaSurge { get; }
        public int CinemaFirst { get; }
        public int CinemaEvery { get; }
        // Minutes after the day's midnight; past 1440 means after midnight.
        public int CinemaLast { get; }
        public double WeekendSurge { get; }
        public int ClubClosingMinutes { get; }
        public double ClubClosingSurge { get; }
        public int ReferenceWorkers { get; }
        public double MinWorkerWeight { get; }
        public double MaxWorkerWeight { get; }
        // A player's venue brings its guests in the last hour divided by ReferenceGuests, between the two bounds,
        // times any surge.
        public int ReferenceGuests { get; }
        public double MinGuestWeight { get; }
        public double MaxGuestWeight { get; }

        private double GuestFactor(VenueNow venue) => venue.Guests is { } guests
            ? Math.Max(MinGuestWeight, Math.Min(MaxGuestWeight, guests / (double)ReferenceGuests))
            : 1.0;
        // Business type to venue kind; anything else is static.
        public IReadOnlyDictionary<string, VenueKind> Kinds { get; }

        public VenueKind KindOf(string? businessType) =>
            businessType != null && Kinds.TryGetValue(businessType, out var kind) ? kind : VenueKind.Other;

        public double Weight(VenueNow venue, int minuteOfDay, DayOfWeek day)
        {
            if (venue == null) throw new ArgumentNullException(nameof(venue));
            if (minuteOfDay < 0 || minuteOfDay >= 1440) throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
            var weekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
            switch (venue.Kind)
            {
                case VenueKind.Nightclub:
                    if (venue.Open) return GuestFactor(venue);
                    return venue.ClosedMinutesAgo is { } since && since >= 0 && since < ClubClosingMinutes ? ClubClosingSurge * GuestFactor(venue) : ClosedWeight;
                case VenueKind.Theatre:
                {
                    var letOuts = weekend ? TheatreLetOuts.Concat(TheatreWeekendLetOuts) : TheatreLetOuts;
                    var surge = InSurge(letOuts, minuteOfDay) ? TheatreSurge * (weekend ? WeekendSurge : 1.0) : 1.0;
                    // The audience comes out as the doors close, so a show ending at closing time still counts.
                    return venue.Open || (surge > 1.0 && venue.ClosedMinutesAgo is { } ago && ago < SurgeMinutes) ? surge * GuestFactor(venue) : ClosedWeight;
                }
                case VenueKind.Cinema:
                {
                    var surge = InSurge(CinemaShows(), minuteOfDay) ? CinemaSurge * (weekend ? WeekendSurge : 1.0) : 1.0;
                    return venue.Open || (surge > 1.0 && venue.ClosedMinutesAgo is { } ago && ago < SurgeMinutes) ? surge * GuestFactor(venue) : ClosedWeight;
                }
                case VenueKind.Office:
                case VenueKind.Factory:
                    if (!venue.Open && (venue.Workers ?? 0) <= 0) return ClosedWeight;
                    if (venue.Workers == null) return 1.0;
                    return Math.Max(MinWorkerWeight, Math.Min(MaxWorkerWeight, venue.Workers.Value / (double)ReferenceWorkers));
                default:
                    return 1.0;
            }
        }

        // Cinema shows end every CinemaEvery minutes from CinemaFirst to CinemaLast.
        private IEnumerable<int> CinemaShows()
        {
            for (var minute = CinemaFirst; minute <= CinemaLast; minute += CinemaEvery) yield return minute % 1440;
        }

        // Within SurgeMinutes after a let-out time, across midnight too.
        private bool InSurge(IEnumerable<int> letOuts, int minuteOfDay) =>
            letOuts.Any(time => ((minuteOfDay - time) % 1440 + 1440) % 1440 < SurgeMinutes);

        internal static LiveCrowds Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "closedWeight", "surgeMinutes", "theatre", "cinema", "weekendSurge", "clubClosing", "workers", "guests", "kinds");
            var guests = r.Object(obj, "guests");
            r.OnlyKeys(guests, "reference", "min", "max");
            var guestMin = r.Number(guests, "min", 0, 10);
            var guestMax = r.Number(guests, "max", 0, 10);
            if (guestMax < guestMin) throw r.Fail(DataReader.PathOf(guests, "max"), "must not be below min");
            var theatre = r.Object(obj, "theatre");
            r.OnlyKeys(theatre, "surge", "letOuts", "weekendLetOuts");
            var cinema = r.Object(obj, "cinema");
            r.OnlyKeys(cinema, "surge", "first", "every", "last");
            var club = r.Object(obj, "clubClosing");
            r.OnlyKeys(club, "minutes", "surge");
            var workers = r.Object(obj, "workers");
            r.OnlyKeys(workers, "reference", "min", "max");
            var kindsObj = r.Object(obj, "kinds");
            var kinds = new Dictionary<string, VenueKind>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(kindsObj))
            {
                var text = r.Text(kindsObj, entry.Name);
                if (!Enum.TryParse<VenueKind>(text, true, out var kind) || kind == VenueKind.Other)
                    throw r.Fail(DataReader.PathOf(kindsObj, entry.Name), "must be nightclub, theatre, cinema, office or factory");
                kinds.Add(entry.Name, kind);
            }
            var min = r.Number(workers, "min", 0, 10);
            var max = r.Number(workers, "max", 0, 10);
            if (max < min) throw r.Fail(DataReader.PathOf(workers, "max"), "must not be below min");
            var every = r.Integer(cinema, "every", 30, 1440);
            return new LiveCrowds(
                r.Number(obj, "closedWeight", 0, 1),
                r.Integer(obj, "surgeMinutes", 1, 240),
                r.Number(theatre, "surge", 1, 10),
                Minutes(r, theatre, "letOuts"),
                Minutes(r, theatre, "weekendLetOuts"),
                r.Number(cinema, "surge", 1, 10),
                r.Integer(cinema, "first", 0, 1439),
                every,
                r.Integer(cinema, "last", 0, 2879),
                r.Number(obj, "weekendSurge", 1, 5),
                r.Integer(club, "minutes", 0, 240),
                r.Number(club, "surge", 1, 10),
                r.Integer(workers, "reference", 1, 1000),
                min, max, kinds, r.Integer(guests, "reference", 1, 1000), guestMin, guestMax);
        }

        private static IReadOnlyList<int> Minutes(DataReader r, JObject parent, string name)
        {
            var list = r.List(parent, name);
            var path = DataReader.PathOf(parent, name);
            return list.Select((token, i) => (int)r.Number(token, $"{path}[{i}]", 0, 1439)).ToList();
        }
    }
}
