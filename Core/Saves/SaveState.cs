using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Reputation;

namespace FoodTrucks.Core.Saves
{
    public class SaveState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string WrittenByModVersion { get; set; } = string.Empty;
        public int SaveCount { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public LicenceState Licence { get; set; } = new LicenceState();
        public List<VehicleState> Vehicles { get; set; } = new List<VehicleState>();
        public List<DailySales> DailySales { get; set; } = new List<DailySales>();
        // Regulars belong to the whole fleet, by spot and weekday.
        public RegularsState Regulars { get; set; } = new RegularsState();
        // Offers, booked gigs and the booking reputation. Its random state is seeded by the game side
        // on first use (CateringState.New); 0 until then.
        public CateringState Catering { get; set; } = new CateringState();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();

        public static void IncrementSaveCount(SaveState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.SaveCount < int.MaxValue) state.SaveCount++;
        }
    }

    public class LicenceState
    {
        public bool HasVendorLicence { get; set; }
        // The last day the licence is valid.
        public int LicenceExpiresOnDay { get; set; }
        // The day the first licence was bought: the clock for permit unlocks.
        public int? FirstLicensedDay { get; set; }
        // The highest permit cap reached. The cap never drops, even when vehicles with sales leave the fleet.
        public int PermitsUnlocked { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VehicleState
    {
        public string VehicleId { get; set; } = string.Empty;
        public bool IsOpen { get; set; }
        public double SaleProgress { get; set; }
        public int LifetimeSales { get; set; }
        public long LifetimeRevenueCents { get; set; }
        // The queue and the orders at the window while open (SalesSimulator); null when closed.
        public ServiceState? Service { get; set; }
        // "cart" or "truck", as in economy.json. Empty for vehicles saved before Milestone 4.
        public string VehicleType { get; set; } = string.Empty;
        public bool HasPermit { get; set; }
        // A hire's route; null when the player runs the vehicle.
        public HireSchedule? Schedule { get; set; }
        // Set while a hire drives between stops; the vehicle is off the map until it arrives.
        public TransitState? Transit { get; set; }
        // Hidden cleanliness points and the grade on the window; null until the vehicle first opens.
        public HealthState? Health { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // Street food revenue per in-game day, the mod's own record for income tax.
    public class DailySales
    {
        public int Day { get; set; }
        public long RevenueCents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // SalesSimulator's state for one open vehicle. Minute counts simulated minutes since it opened.
    public class ServiceState
    {
        public long RandomState { get; set; }
        public long Minute { get; set; }
        public long NextCustomerId { get; set; } = 1;
        // Waiting customers, first in line first.
        public List<ServiceCustomer> Queue { get; set; } = new List<ServiceCustomer>();
        // Orders being made, one per serving worker.
        public List<ServiceCustomer> Window { get; set; } = new List<ServiceCustomer>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ServiceCustomer
    {
        public long Id { get; set; }
        public long ArrivedAtMinute { get; set; }
        // Minutes this customer waits in dry weather before leaving.
        public double Patience { get; set; }
        // The demand category the customer came for.
        public string Category { get; set; } = string.Empty;
        // At the window only: the item being made and the work left on it.
        public string? Item { get; set; }
        public double MinutesLeft { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // A hire's daily route: stops in order, each with its opening hours (minutes after the day's midnight).
    public class HireSchedule
    {
        // Days of the week the route runs; empty means every day.
        public List<DayOfWeek> Days { get; set; } = new List<DayOfWeek>();
        public List<ScheduleStop> Stops { get; set; } = new List<ScheduleStop>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ScheduleStop
    {
        // The game side's key for the spot, the same key regulars use.
        public string SpotKey { get; set; } = string.Empty;
        public double X { get; set; }
        public double Z { get; set; }
        public int OpenAt { get; set; }
        public int CloseAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // Times are absolute game minutes: (day - 1) × 1440 + minute of the day.
    public class TransitState
    {
        public string ToSpotKey { get; set; } = string.Empty;
        public double ToX { get; set; }
        public double ToZ { get; set; }
        public long DepartsAt { get; set; }
        public long ArrivesAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
