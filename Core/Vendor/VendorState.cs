using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Vendor
{
    public class VendorState
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public List<VendorLease> Leases { get; set; } = new List<VendorLease>();
        public List<VendorRequest> Requests { get; set; } = new List<VendorRequest>();
        public List<VendorVehicle> Vehicles { get; set; } = new List<VendorVehicle>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VendorLease
    {
        public string Owner { get; set; } = string.Empty;
        public long Until { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VendorRequest
    {
        public string Owner { get; set; } = string.Empty;
        public string VehicleId { get; set; } = string.Empty;
        public long Hour { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VendorVehicle
    {
        public string VehicleId { get; set; } = string.Empty;
        public long LastOpeningId { get; set; }
        public long OpeningId { get; set; }
        public long OpenedAt { get; set; }
        public string Owner { get; set; } = string.Empty;
        public long? HandedBackAt { get; set; }
        public long Sequence { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
