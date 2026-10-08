using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Pitches
{
    // The city's events, announced and booked (CityEvents).
    public class EventsState
    {
        // The last day the city announced events on; 0 before the first.
        public int LastAnnouncedDay { get; set; }
        public long NextId { get; set; } = 1;
        public List<CityEvent> Events { get; set; } = new List<CityEvent>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CityEvent
    {
        public long Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string PlaceId { get; set; } = string.Empty;
        public int AnnouncedDay { get; set; }
        public int Day { get; set; }
        // Minutes after the event day's midnight.
        public int Start { get; set; }
        public int End { get; set; }
        // Places the player could book, the fee each, as announced (the data file may change later).
        public int Places { get; set; }
        public long FeeCents { get; set; }
        // Places the player has paid for.
        public int Booked { get; set; }
        // The vehicles holding a booked place, first come.
        public List<string> Vehicles { get; set; } = new List<string>();
        // The morning reminder went out.
        public bool Reminded { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // A vehicle's deal with one of the player's own venues.
    public class PartnershipState
    {
        public string VehicleId { get; set; } = string.Empty;
        // The venue's address, "street#number".
        public string Venue { get; set; } = string.Empty;
        public int SignedDay { get; set; }
        // The last game day the vehicle worked outside the venue for long enough (PartnershipRules.MinWorkedMinutes).
        public int? LastWorkedDay { get; set; }
        // Minutes worked outside the venue on WorkingDay, counted up to the minimum.
        public int WorkingDay { get; set; }
        public int WorkedMinutes { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
