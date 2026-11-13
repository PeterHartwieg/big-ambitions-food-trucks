using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Reputation;

namespace FoodTrucks.Core.Saves
{
    public class SaveState
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string WrittenByModVersion { get; set; } = string.Empty;
        public int SaveCount { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public LicenceState Licence { get; set; } = new LicenceState();
        public List<VehicleState> Vehicles { get; set; } = new List<VehicleState>();
        public List<DailySales> DailySales { get; set; } = new List<DailySales>();
        public RegularsState Regulars { get; set; } = new RegularsState();
        public CateringState Catering { get; set; } = new CateringState();
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public CommissaryState Commissary { get; set; } = new CommissaryState();
        public EventsState Events { get; set; } = new EventsState();
        public Dictionary<string, List<PlaceHold>> PremiumHolds { get; set; } = new Dictionary<string, List<PlaceHold>>(StringComparer.Ordinal);
        public List<PartnershipState> Partnerships { get; set; } = new List<PartnershipState>();
        public List<CrewHours> CrewHours { get; set; } = new List<CrewHours>();
        public int? CrewHoursSince { get; set; }
        public Dictionary<string, long> CrewPaidHour { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public Dictionary<string, CrewServed> CrewServedHour { get; set; } = new Dictionary<string, CrewServed>(StringComparer.Ordinal);
        public List<CrewTaking> CrewTakings { get; set; } = new List<CrewTaking>();

        public List<FoodTrucks.Core.Fleet.CartSale> CartSales { get; set; } = new List<FoodTrucks.Core.Fleet.CartSale>();

        public List<VehicleNumber> VehicleNumbers { get; set; } = new List<VehicleNumber>();

        public FoodTrucks.Core.Vendor.VendorState? Vendor { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();

        public static void IncrementSaveCount(SaveState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.SaveCount < int.MaxValue) state.SaveCount++;
        }
    }

    public class VehicleNumber
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Number { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class LicenceState
    {
        public bool HasVendorLicence { get; set; }
        public int LicenceExpiresOnDay { get; set; }
        public int? FirstLicensedDay { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VehicleState
    {
        public string VehicleId { get; set; } = string.Empty;
        public JToken? Logo { get; set; }
        public bool IsOpen { get; set; }
        public double SaleProgress { get; set; }
        public int LifetimeSales { get; set; }
        public long LifetimeRevenueCents { get; set; }
        public Dictionary<string, long> CustomPriceCents { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public ServiceState? Service { get; set; }
        public string VehicleType { get; set; } = string.Empty;
        public bool HasPermit { get; set; }
        public HireSchedule? Schedule { get; set; }
        public TransitState? Transit { get; set; }
        public HealthState? Health { get; set; }
        public List<string> Crew { get; set; } = new List<string>();
        public List<ShiftPlan> Shifts { get; set; } = new List<ShiftPlan>();
        public int? HireStop { get; set; }
        public ArrivalState? Arrival { get; set; }
        public SkipState? Skipped { get; set; }
        public List<DayRecord> Days { get; set; } = new List<DayRecord>();
        public string? OpenPitch { get; set; }
        public int? LastOpenedDay { get; set; }
        public Dictionary<string, List<StockBatch>> StockBatches { get; set; } = new Dictionary<string, List<StockBatch>>(StringComparer.Ordinal);
        public string? PremiumSpot { get; set; }
        public List<SoldDay> Sold { get; set; } = new List<SoldDay>();
        public long? RestockedFor { get; set; }
        public string? RestockStockKey { get; set; }
        public int? RestockShortDay { get; set; }
        public bool ReturnPending { get; set; }
        public bool CrewWindowLost { get; set; }
        public string? MarketingLevel { get; set; }
        public int? MarketingStartsDay { get; set; }
        public int? MarketingPaidThroughDay { get; set; }
        public string? MarketingPaidLevel { get; set; }
        public long MarketingPaidCents { get; set; }
        public int? MarketingPreviousDay { get; set; }
        public string? MarketingPreviousLevel { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class DailySales
    {
        public int Day { get; set; }
        public long RevenueCents { get; set; }
        public long CostCents { get; set; }
        public bool InSummary { get; set; }
        // The day's money per vehicle, for Econoview's breakdown (additive field: days recorded by older
        // versions have none, and their profit shows as one "not broken down" figure).
        public List<VehicleDay> ByVehicle { get; set; } = new List<VehicleDay>();
        public long LicenceCents { get; set; }
        public long EventPlacesCents { get; set; }
        public long DeliveryFeesCents { get; set; }
        public long? SummaryCents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CrewServed
    {
        public long Hour { get; set; }
        public string VehicleId { get; set; } = string.Empty;

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CrewHours
    {
        public int Day { get; set; }
        public string EmployeeId { get; set; } = string.Empty;
        public string VehicleId { get; set; } = string.Empty;
        public int Hours { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class VehicleDay
    {
        public string VehicleId { get; set; } = string.Empty;
        public long SalesCents { get; set; }
        public long CateringCents { get; set; }
        public long StockCents { get; set; }
        public long WagesCents { get; set; }
        public long ParkingCents { get; set; }
        public long PermitCents { get; set; }
        public long CommissaryRentCents { get; set; }
        public long OvernightFineCents { get; set; }
        public long MarketingCents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ServiceState
    {
        public long RandomState { get; set; }
        public long Minute { get; set; }
        public long NextCustomerId { get; set; } = 1;
        public List<ServiceCustomer> Approaching { get; set; } = new List<ServiceCustomer>();
        public List<ServiceCustomer> Queue { get; set; } = new List<ServiceCustomer>();
        public List<ServiceCustomer> Window { get; set; } = new List<ServiceCustomer>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ServiceCustomer
    {
        public long Id { get; set; }
        public long ArrivedAtMinute { get; set; }
        public long JoinsAtMinute { get; set; }
        public double Patience { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Item { get; set; }
        public double MinutesLeft { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class HireSchedule
    {
        public List<ScheduleStop> Stops { get; set; } = new List<ScheduleStop>();
        public List<DayOfWeek> Closed { get; set; } = new List<DayOfWeek>();
        public List<CrewShift> CrewShifts { get; set; } = new List<CrewShift>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ScheduleStop
    {
        public DayOfWeek Day { get; set; }
        public string SpotKey { get; set; } = string.Empty;
        public double X { get; set; }
        public double Z { get; set; }
        public double Y { get; set; }
        public double Heading { get; set; }
        public int OpenAt { get; set; }
        public int CloseAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CrewShift
    {
        public string EmployeeId { get; set; } = string.Empty;
        public DayOfWeek Day { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public string? Station { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ShiftPlan
    {
        public int Day { get; set; }
        public List<ShiftLeg> Legs { get; set; } = new List<ShiftLeg>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ShiftLeg
    {
        public FoodTrucks.Core.Fleet.LegKind Kind { get; set; }
        public int StopIndex { get; set; }
        public long Start { get; set; }
        public long End { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class ArrivalState
    {
        public string SpotKey { get; set; } = string.Empty;
        public int? StopIndex { get; set; }
        public double X { get; set; }
        public double Z { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class DayRecord
    {
        public int Day { get; set; }
        public bool Opened { get; set; }
        public bool Cleaned { get; set; }
        public bool SoldStale { get; set; }
        public List<SpotMinutes> Visits { get; set; } = new List<SpotMinutes>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class SpotMinutes
    {
        public string SpotKey { get; set; } = string.Empty;
        public int Minutes { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class SoldDay
    {
        public int Day { get; set; }
        public Dictionary<string, int> Units { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class StockBatch
    {
        public int Day { get; set; }
        public int Units { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class SkipState
    {
        public string SpotKey { get; set; } = string.Empty;
        public int? StopIndex { get; set; }
        public long Until { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class TransitState
    {
        public string ToSpotKey { get; set; } = string.Empty;
        public double ToX { get; set; }
        public double ToZ { get; set; }
        public int? ToStopIndex { get; set; }
        public long? ToCloseAt { get; set; }
        public double ToY { get; set; }
        public double ToHeading { get; set; }
        public double FromX { get; set; }
        public double FromY { get; set; }
        public double FromZ { get; set; }
        public double FromHeading { get; set; }
        public int FromParkingState { get; set; }
        public long DepartsAt { get; set; }
        public long ArrivesAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
