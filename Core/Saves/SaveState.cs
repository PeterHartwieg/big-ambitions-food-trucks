using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class LicenceState
    {
        public bool HasVendorLicence { get; set; }
        public int LicenceExpiresOnDay { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VehicleState
    {
        public string VehicleId { get; set; } = string.Empty;
        public bool IsOpen { get; set; }
        public int LifetimeSales { get; set; }
        public long LifetimeRevenueCents { get; set; }

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
}
