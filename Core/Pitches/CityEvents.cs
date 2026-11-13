using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Pitches
{
    public enum BookingResult { Booked, NotFound, Over, Full }

    public sealed class CityEvents
    {
        private readonly PitchTables tables;

        public CityEvents(PitchTables tables) => this.tables = tables ?? throw new ArgumentNullException(nameof(tables));

        public List<CityEvent> Announce(EventsState state, int today, Func<EventPlace, bool>? usable = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
            var added = new List<CityEvent>();
            var from = state.LastAnnouncedDay <= 0 ? today : state.LastAnnouncedDay + 1;
            for (var announceDay = from; announceDay <= today; announceDay++)
            {
                foreach (var kind in tables.Kinds)
                {
                    var day = announceDay + kind.LeadDays;
                    if (day < today || !kind.Days.Contains(GameDays.Weekday(day))) continue;
                    var random = new SeededRandom(SeededRandom.SeedFor("event:" + kind.Id, day));
                    if (!random.Chance(kind.Chance)) continue;
                    var used = new HashSet<string>(state.Events.Where(e => e.Day == day).Select(e => e.PlaceId), StringComparer.Ordinal);
                    var places = tables.Places.Where(p => p.Kinds.Contains(kind.Id) && !used.Contains(p.Id) && (usable == null || usable(p))).ToList();
                    if (places.Count == 0) continue;
                    var place = places[(int)(random.NextDouble() * places.Count) % places.Count];
                    var city = new CityEvent
                    {
                        Id = state.NextId++,
                        Kind = kind.Id,
                        PlaceId = place.Id,
                        AnnouncedDay = announceDay,
                        Day = day,
                        Start = kind.Start,
                        End = kind.End,
                        Places = kind.Places,
                        FeeCents = kind.FeeCents
                    };
                    state.Events.Add(city);
                    added.Add(city);
                }
            }
            if (today > state.LastAnnouncedDay) state.LastAnnouncedDay = today;
            return added;
        }

        public static int Expire(EventsState state, int today, long soldThrough)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Events.RemoveAll(e => e.Day < today && EndsAt(e) <= soldThrough);
        }

        public static int Release(EventsState state, string vehicleId, long minute)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Events.Sum(e => Holds.Release(e.Holds, vehicleId, minute));
        }

        public static long UnusedPlaceRefund(CityEvent city)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            var claimed = city.Holds.Where(h => h.Until == null || h.Until > h.From)
                .Select(h => h.VehicleId).Distinct(StringComparer.Ordinal).Count();
            return checked(Math.Max(0, city.Booked - claimed) * city.FeeCents);
        }

        public static long StartsAt(CityEvent city) => (city.Day - 1) * 1440L + city.Start;
        public static long EndsAt(CityEvent city) => (city.Day - 1) * 1440L + city.End;
        public static bool Running(CityEvent city, long minute) => minute >= StartsAt(city) && minute < EndsAt(city);

        public static IEnumerable<CityEvent> Bookable(EventsState state, long minute) =>
            state.Events.Where(e => minute < EndsAt(e) && e.Booked < e.Places).OrderBy(StartsAt).ThenBy(e => e.Id);

        public static BookingResult Book(EventsState state, long eventId, long minute)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var city = state.Events.FirstOrDefault(e => e.Id == eventId);
            if (city == null) return BookingResult.NotFound;
            if (minute >= EndsAt(city)) return BookingResult.Over;
            if (city.Booked >= city.Places) return BookingResult.Full;
            city.Booked++;
            return BookingResult.Booked;
        }

        public static bool Claim(CityEvent city, string vehicleId, long minute)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            return Holds.Claim(city.Holds, vehicleId, minute, city.Booked) == null;
        }

        public static bool CanClaim(CityEvent city, string vehicleId, long minute) =>
            Holds.Blocker(city.Holds, vehicleId, minute, city.Booked) == null;

        public static CityEvent? RunningAt(EventsState state, string placeId, long minute) =>
            state.Events.FirstOrDefault(e => e.PlaceId == placeId && Running(e, minute));
    }
}
