using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Vendor
{
    // What another mod (a vendor API owner, docs/vendor-api.md) holds in this save: its lease, the vehicles it asked
    // for and, per vehicle, the opening counter and the owner of the current opening. Absent (null) in every save until
    // an owner first calls RequestOwner or Renew, so a game without such a mod saves exactly as before.
    public class VendorState
    {
        // The block's own layout version. A newer one is kept unread and untouched (VendorBook.TakeOut).
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        // One per owner.
        public List<VendorLease> Leases { get; set; } = new List<VendorLease>();
        // At most one owner per vehicle.
        public List<VendorRequest> Requests { get; set; } = new List<VendorRequest>();
        public List<VendorVehicle> Vehicles { get; set; } = new List<VendorVehicle>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // Times are absolute game minutes: (day - 1) × 1440 + minute of the day.
    public class VendorLease
    {
        public string Owner { get; set; } = string.Empty;
        // The lease holds for minutes before this one.
        public long Until { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VendorRequest
    {
        public string Owner { get; set; } = string.Empty;
        public string VehicleId { get; set; } = string.Empty;
        // The absolute game hour ((day - 1) × 24 + hour) of the first request: only an opening in a later hour is granted.
        public long Hour { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VendorVehicle
    {
        public string VehicleId { get; set; } = string.Empty;
        // The highest opening ID given to this vehicle; the next opening gets a higher one.
        public long LastOpeningId { get; set; }
        // The current (or last) opening's ID, 0 for none.
        public long OpeningId { get; set; }
        // The absolute game minute that opening started.
        public long OpenedAt { get; set; }
        // Who holds that opening: "" for Street Food.
        public string Owner { get; set; } = string.Empty;
        // The minute Street Food took this opening back from an owner; null when it never did. A sales catch-up still
        // behind it gets no Street Food customers before it (VendorBook.OwnCustomers).
        public long? HandedBackAt { get; set; }
        // Sales made through VendorApi.Sell for this vehicle.
        public long Sequence { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
