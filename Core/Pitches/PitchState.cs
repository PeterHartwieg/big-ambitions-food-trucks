using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Pitches
{
    public class EventsState
    {
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
        public int Start { get; set; }
        public int End { get; set; }
        // Places the player could book, the fee each, as announced (the data file may change later).
        public int Places { get; set; }
        public long FeeCents { get; set; }
        public int Booked { get; set; }
        public List<PlaceHold> Holds { get; set; } = new List<PlaceHold>();
        public bool Reminded { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class PlaceHold
    {
        public string VehicleId { get; set; } = string.Empty;
        public long From { get; set; }
        public long? Until { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class PartnershipState
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Venue { get; set; } = string.Empty;
        public int SignedDay { get; set; }
        public int? LastWorkedDay { get; set; }
        public int? ScoredDay { get; set; }
        public int? BonusForDay { get; set; }
        public int WorkingDay { get; set; }
        public int WorkedMinutes { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
