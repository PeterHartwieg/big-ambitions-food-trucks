using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Catering
{
    public sealed class CateringPlace
    {
        public string PlaceId { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public double X { get; set; }
        public double Z { get; set; }
    }

    public sealed class OfferContext
    {
        public int Day { get; set; }
        public IReadOnlyList<CateringPlace> Places { get; set; } = Array.Empty<CateringPlace>();
        public int EligibleVehicles { get; set; }
        public long LifetimeServed { get; set; }
        public int? MaxUnits { get; set; }
        // Only categories an eligible vehicle can sell belong here.
        public IReadOnlyDictionary<string, long> ReferencePriceCents { get; set; } = new Dictionary<string, long>();
    }

    public enum AcceptFailure { UnknownOffer, Expired, TooManyGigs }

    public readonly struct AcceptResult
    {
        internal AcceptResult(CateringGig gig) { Gig = gig; Failure = null; }
        internal AcceptResult(AcceptFailure failure) { Gig = null; Failure = failure; }
        public bool Success => Gig != null;
        public CateringGig? Gig { get; }
        public AcceptFailure? Failure { get; }
    }

    public static class CateringOffers
    {
        public static IReadOnlyList<CateringOffer> ForDay(CateringRules rules, CateringState state, OfferContext context)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            CateringValidation.Validate(state);
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Day <= state.LastOfferDay) return Array.Empty<CateringOffer>();
            GameTime.Absolute(context.Day, 0);
            if (context.Places == null || context.ReferencePriceCents == null) throw new ArgumentException("Offer places and prices must not be null.", nameof(context));
            if (context.Places.Any(p => p == null || string.IsNullOrEmpty(p.PlaceId))) throw new ArgumentException("Offer places need IDs.", nameof(context));
            if (context.ReferencePriceCents.Values.Any(p => p < 0)) throw new ArgumentException("Reference prices must not be negative.", nameof(context));
            if (context.MaxUnits < 0) throw new ArgumentOutOfRangeException(nameof(context.MaxUnits));
            state.LastOfferDay = context.Day;
            if (context.EligibleVehicles <= 0 || context.LifetimeServed <= 0 || rules.MaxOffersPerDay == 0) return Array.Empty<CateringOffer>();

            var maxGuests = context.MaxUnits.HasValue ? (int)Math.Min(1000, Math.Floor(context.MaxUnits.Value / rules.UnitsPerGuest)) : 1000;
            var random = new SeededRandom(state.RandomState);
            var mean = rules.OffersPerDayAtMaturity * Math.Min(1, context.LifetimeServed / (double)rules.MaturityServed) * state.BookingReputation;
            var count = Poisson(random, mean, rules.MaxOffersPerDay);
            var offers = new List<CateringOffer>();
            for (var i = 0; i < count; i++)
            {
                var gigDay = checked(context.Day + Integer(random, rules.LeadDays));
                var weekday = GameDays.Weekday(gigDay);
                var kinds = rules.Kinds.Where(k => k.Guests.Min <= maxGuests && k.Days.Contains(weekday)
                    && context.Places.Any(p => k.Areas.Contains(p.Area, StringComparer.Ordinal))
                    && k.Categories.Keys.Any(context.ReferencePriceCents.ContainsKey)).OrderBy(k => k.Id, StringComparer.Ordinal).ToList();
                if (kinds.Count == 0) continue;
                var kind = Pick(kinds, k => k.Weight, random);
                var places = context.Places.Where(p => kind.Areas.Contains(p.Area, StringComparer.Ordinal))
                    .OrderBy(p => p.PlaceId, StringComparer.Ordinal).ToList();
                var place = places[Integer(random, (0, places.Count - 1))];
                var start = (int)Math.Min(1425, RoundQuarter(Integer(random, kind.Start)));
                var duration = (int)Math.Min(2147483640L, Math.Max(15, RoundQuarter(Integer(random, kind.DurationMinutes))));
                var guests = Integer(random, (kind.Guests.Min, Math.Min(kind.Guests.Max, maxGuests)));
                var categories = kind.Categories.Where(c => context.ReferencePriceCents.ContainsKey(c.Key)).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
                var category = Pick(categories, c => c.Value, random).Key;
                var fee = checked((long)Math.Round(guests * rules.UnitsPerGuest * context.ReferencePriceCents[category] * rules.FeeMarkup, MidpointRounding.AwayFromZero));
                var replyDay = Math.Min(checked(context.Day + rules.ReplyDays), gigDay);
                var offer = new CateringOffer
                {
                    Id = state.NextOfferId, Kind = kind.Id, PostedDay = context.Day, ReplyByDay = replyDay,
                    GigDay = gigDay, StartMinute = start, DurationMinutes = duration, PlaceId = place.PlaceId,
                    Guests = guests, RequiredCategory = category, FeeCents = fee
                };
                state.NextOfferId = checked(state.NextOfferId + 1);
                state.Offers.Add(offer);
                offers.Add(offer);
            }
            state.RandomState = random.State;
            return offers.AsReadOnly();
        }

        public static AcceptResult Accept(CateringRules rules, CateringState state, long offerId, int day, int minute, int eligibleVehicles)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            CateringValidation.Validate(state);
            var now = GameTime.Absolute(day, minute);
            var offer = state.Offers.FirstOrDefault(o => o.Id == offerId);
            if (offer == null) return new AcceptResult(AcceptFailure.UnknownOffer);
            if (now >= Deadline(offer)) return new AcceptResult(AcceptFailure.Expired);
            if (!HasCapacity(state, offer, eligibleVehicles)) return new AcceptResult(AcceptFailure.TooManyGigs);
            var gig = new CateringGig
            {
                Id = offer.Id, Kind = offer.Kind, PostedDay = offer.PostedDay, ReplyByDay = offer.ReplyByDay,
                GigDay = offer.GigDay, StartMinute = offer.StartMinute, DurationMinutes = offer.DurationMinutes,
                PlaceId = offer.PlaceId, Guests = offer.Guests, RequiredCategory = offer.RequiredCategory, FeeCents = offer.FeeCents,
                ExtensionData = offer.ExtensionData.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), Status = GigStatus.Booked
            };
            state.Offers.Remove(offer);
            state.Gigs.Add(gig);
            return new AcceptResult(gig);
        }

        public static bool Decline(CateringState state, long offerId)
        {
            CateringValidation.Validate(state);
            return state.Offers.RemoveAll(o => o.Id == offerId) > 0;
        }

        public static IReadOnlyList<CateringOffer> ExpireOffers(CateringState state, int day, int minute)
        {
            CateringValidation.Validate(state);
            var now = GameTime.Absolute(day, minute);
            var expired = state.Offers.Where(o => now >= Deadline(o)).ToList();
            foreach (var offer in expired) state.Offers.Remove(offer);
            return expired.AsReadOnly();
        }

        private static long Deadline(CateringOffer offer) => Math.Min(GameTime.Absolute(offer.ReplyByDay, 1439) + 1, GameTime.Absolute(offer.GigDay, offer.StartMinute));

        private static bool HasCapacity(CateringState state, CateringOffer offer, int vehicles)
        {
            if (vehicles <= 0) return false;
            var start = GameTime.Absolute(offer.GigDay, offer.StartMinute);
            var end = start + offer.DurationMinutes;
            var changes = new List<(long Minute, int Change)> { (start, 1), (end, -1) };
            foreach (var gig in state.Gigs.Where(g => g.Status == GigStatus.Booked || g.Status == GigStatus.Started))
            {
                var otherStart = GameTime.Absolute(gig.GigDay, gig.StartMinute);
                var otherEnd = otherStart + gig.DurationMinutes;
                if (otherStart >= end || otherEnd <= start) continue;
                changes.Add((Math.Max(start, otherStart), 1));
                changes.Add((Math.Min(end, otherEnd), -1));
            }
            var active = 0;
            // An ending gig frees its truck before another gig starts at the same minute.
            foreach (var change in changes.OrderBy(c => c.Minute).ThenBy(c => c.Change))
            {
                active += change.Change;
                if (active > vehicles) return false;
            }
            return true;
        }

        private static int Integer(SeededRandom random, (int Min, int Max) range) =>
            (int)(range.Min + (long)(random.NextDouble() * ((long)range.Max - range.Min + 1)));

        private static long RoundQuarter(int minutes) => (long)(Math.Round(minutes / 15.0, MidpointRounding.AwayFromZero) * 15);

        private static T Pick<T>(IReadOnlyList<T> list, Func<T, double> weight, SeededRandom random)
        {
            // Scale first so even very large valid weights cannot overflow their sum.
            var scale = list.Max(weight);
            var draw = random.NextDouble() * list.Sum(v => weight(v) / scale);
            foreach (var value in list)
            {
                draw -= weight(value) / scale;
                if (draw < 0) return value;
            }
            return list[list.Count - 1];
        }

        private static int Poisson(SeededRandom random, double mean, int cap)
        {
            var count = 0;
            // Independent Poisson draws add, keeping each draw inside SeededRandom's supported range.
            while (mean > 0 && count < cap)
            {
                var part = Math.Min(60, mean);
                count = (int)Math.Min(cap, (long)count + random.Poisson(part));
                mean -= part;
            }
            return count;
        }
    }
}
