using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Pitches
{
    public enum BookingResult { Booked, NotFound, Over, Full }

    // Temporary pitches the city announces a few days ahead (design: Pitches, Event pitches): a weekend market,
    // a street festival. The player books places by phone for a fee; during the event's hours a booked vehicle
    // at the place gets its crowd. Absolute minutes are (day - 1) × 1440 + minute of the day.
    public sealed class CityEvents
    {
        private readonly PitchTables tables;

        public CityEvents(PitchTables tables) => this.tables = tables ?? throw new ArgumentNullException(nameof(tables));

        // The city's announcements up to today, each day's once: for every kind, the day LeadDays ahead may get an
        // event at one of its places, by a draw seeded from the kind and that day, so the same save always gets
        // the same events. Days the game skipped are caught up; an event already over is never announced. Returns
        // the new events. `usable` leaves out places the game side can't find (null: every place).
        public List<CityEvent> Announce(EventsState state, int today, Func<EventPlace, bool>? usable = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
            var added = new List<CityEvent>();
            // A new save, or one from before events: start today rather than replaying its whole history.
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

        // Events from before today that ended before `soldThrough` are dropped: the earliest minute any open window
        // still has to sell, so a catch-up over one or more midnights still sells through them.
        public static int Expire(EventsState state, int today, long soldThrough)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Events.RemoveAll(e => e.Day < today && EndsAt(e) <= soldThrough);
        }

        // A vehicle that closes, leaves or is gone gives its places back at that minute; the booking stays paid for.
        public static int Release(EventsState state, string vehicleId, long minute)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Events.Sum(e => Holds.Release(e.Holds, vehicleId, minute));
        }

        // Every distinct vehicle that ever held a place used one booking, including a running event.
        // Releasing and reclaiming the same place must not consume another refundable booking.
        public static long UnusedPlaceRefund(CityEvent city)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            var claimed = city.Holds.Select(h => h.VehicleId).Distinct(StringComparer.Ordinal).Count();
            return checked(Math.Max(0, city.Booked - claimed) * city.FeeCents);
        }

        public static long StartsAt(CityEvent city) => (city.Day - 1) * 1440L + city.Start;
        public static long EndsAt(CityEvent city) => (city.Day - 1) * 1440L + city.End;
        public static bool Running(CityEvent city, long minute) => minute >= StartsAt(city) && minute < EndsAt(city);

        // Events the player can still book a place at: announced, not over, with a place left.
        public static IEnumerable<CityEvent> Bookable(EventsState state, long minute) =>
            state.Events.Where(e => minute < EndsAt(e) && e.Booked < e.Places).OrderBy(StartsAt).ThenBy(e => e.Id);

        // Books one more place; the caller charges the fee only when this says Booked.
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

        // A vehicle at the event while it runs takes one of the booked places at that minute, first come; one
        // already holding a place keeps it. False when every booked place was held by others then, or none was booked.
        public static bool Claim(CityEvent city, string vehicleId, long minute)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            return Holds.Claim(city.Holds, vehicleId, minute, city.Booked) == null;
        }

        public static bool CanClaim(CityEvent city, string vehicleId, long minute) =>
            Holds.Blocker(city.Holds, vehicleId, minute, city.Booked) == null;

        // The event running at that minute at a place, if any.
        public static CityEvent? RunningAt(EventsState state, string placeId, long minute) =>
            state.Events.FirstOrDefault(e => e.PlaceId == placeId && Running(e, minute));
    }
}
