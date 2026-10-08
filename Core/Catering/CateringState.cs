using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Catering
{
    public enum GigStatus { Booked, Started, Completed, Failed }
    public enum GigFailure { Late, MissingMenu, NotEnoughStock, LeftEarly, NoShow, Unfinished }

    public class CateringState
    {
        // Zero is a valid seed; the adapter may call New on first use.
        public long RandomState { get; set; }
        public int LastOfferDay { get; set; } = int.MinValue;
        public long NextOfferId { get; set; } = 1;
        public double BookingReputation { get; set; } = 1;
        public List<CateringOffer> Offers { get; set; } = new List<CateringOffer>();
        public List<CateringGig> Gigs { get; set; } = new List<CateringGig>();
        public List<CateringGig> History { get; set; } = new List<CateringGig>();
        // Fees of completed gigs the game didn't credit yet (a payment that failed); the adapter retries them.
        public List<UnpaidFee> Unpaid { get; set; } = new List<UnpaidFee>();
        public int Completed { get; set; }
        public int Failed { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();

        public static CateringState New(long seed) => new CateringState { RandomState = seed };
        public static CateringState New(CateringRules rules, long seed) => new CateringState
        {
            RandomState = seed,
            BookingReputation = (rules ?? throw new ArgumentNullException(nameof(rules))).ReputationStart
        };
    }

    public class CateringOffer
    {
        public long Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int PostedDay { get; set; }
        public int ReplyByDay { get; set; }
        public int GigDay { get; set; }
        public int StartMinute { get; set; }
        public int DurationMinutes { get; set; }
        public string PlaceId { get; set; } = string.Empty;
        public int Guests { get; set; }
        public string RequiredCategory { get; set; } = string.Empty;
        public long FeeCents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class UnpaidFee
    {
        public long GigId { get; set; }
        public string VehicleId { get; set; } = string.Empty;
        public string PlaceId { get; set; } = string.Empty;
        public long FeeCents { get; set; }
        public int Units { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CateringGig : CateringOffer
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public GigStatus Status { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public GigFailure? Failure { get; set; }
        public string VehicleId { get; set; } = string.Empty;
        // The adapter excludes these units from street SalesInput.Stock until Finish draws them.
        public int ReservedUnits { get; set; }
        public int? ArrivedDay { get; set; }
        public int? ArrivedMinute { get; set; }
    }

    public static class CateringValidation
    {
        internal static void Validate(CateringState state)
        {
            if (state == null) throw new JsonSerializationException("Catering state must not be null.");
            if (state.Unpaid == null || state.Unpaid.Any(u => u == null || u.VehicleId == null || u.PlaceId == null || u.FeeCents < 0 || u.Units < 0))
                throw new JsonSerializationException("Catering's unpaid fees must name their vehicle and place, with amounts not negative.");
            if (state.Offers == null || state.Gigs == null || state.History == null)
                throw new JsonSerializationException("Catering lists must not be null.");
            if (double.IsNaN(state.BookingReputation) || double.IsInfinity(state.BookingReputation) || state.BookingReputation < 0 || state.BookingReputation > 1)
                throw new JsonSerializationException("Booking reputation must be finite and between 0 and 1.");
            if (state.NextOfferId < 1 || state.Completed < 0 || state.Failed < 0)
                throw new JsonSerializationException("Catering counters must not be negative and NextOfferId must be positive.");
            var ids = new HashSet<long>();
            foreach (var offer in state.Offers) Check(offer, state.NextOfferId, ids);
            foreach (var gig in state.Gigs) Check(gig, state.NextOfferId, ids);
            foreach (var gig in state.History) Check(gig, state.NextOfferId, ids);
        }

        private static void Check(CateringOffer offer, long nextId, HashSet<long> ids)
        {
            if (offer == null) throw new JsonSerializationException("Catering lists must not contain null entries.");
            if (offer.Id < 1 || offer.Id >= nextId || !ids.Add(offer.Id))
                throw new JsonSerializationException("Catering IDs must be unique, positive and below NextOfferId.");
            if (offer.FeeCents < 0 || offer.Guests < 1)
                throw new JsonSerializationException("Catering fees must not be negative and guests must be positive.");
            if (offer.GigDay < 1 || offer.PostedDay < 1 || offer.ReplyByDay < 1 || offer.StartMinute < 0 || offer.StartMinute > 1439 || offer.DurationMinutes < 1)
                throw new JsonSerializationException("Catering dates, minutes and duration must be valid.");
            if (offer is CateringGig gig)
            {
                if (gig.VehicleId == null || gig.ReservedUnits < 0)
                    throw new JsonSerializationException("Catering reservations need a vehicle ID and nonnegative units.");
                if (!Enum.IsDefined(typeof(GigStatus), gig.Status) || (gig.Failure.HasValue && !Enum.IsDefined(typeof(GigFailure), gig.Failure.Value)))
                    throw new JsonSerializationException("Catering status and failure must be known.");
                if (gig.ArrivedDay.HasValue != gig.ArrivedMinute.HasValue || gig.ArrivedDay < 1 || gig.ArrivedMinute < 0 || gig.ArrivedMinute > 1439)
                    throw new JsonSerializationException("Catering arrival must have a valid day and minute together.");
            }
        }
    }
}
