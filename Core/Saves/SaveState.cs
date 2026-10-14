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
        // Visited commissaries, spaces, orders, stock and failed payments (older saves read an empty state).
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public CommissaryState Commissary { get; set; } = new CommissaryState();
        // The city's events and the player's bookings (additive field: older saves read none).
        public EventsState Events { get; set; } = new EventsState();
        // Who held each premium spot when, by spot ID (Holds).
        public Dictionary<string, List<PlaceHold>> PremiumHolds { get; set; } = new Dictionary<string, List<PlaceHold>>(StringComparer.Ordinal);
        // Vehicles partnered with the player's own venues.
        public List<PartnershipState> Partnerships { get; set; } = new List<PartnershipState>();
        // Hours each hire worked on each vehicle per payroll day, so the wage the game pays at midnight is split
        // over the vehicles worked (additive field: older saves read none). Dropped once the wage is paid.
        public List<CrewHours> CrewHours { get; set; } = new List<CrewHours>();
        // The first payroll day whose hours are all in CrewHours, set once by the first version that logs them
        // (additive field: null in older saves). Wages for earlier days go to the crew the hire is on at payment.
        public int? CrewHoursSince { get; set; }
        // The last game hour ((day - 1) × 24 + hour) each hire was paid for as crew, so an hour that is both a route
        // hour and an hour at a window the player opened is paid once (additive field: older saves read none).
        public Dictionary<string, long> CrewPaidHour { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        // The hour in whose first half each hire last served a window the player opened, and on which vehicle, until
        // that hour is over and decided (CrewPay.CloseHours; additive field: older saves read none).
        public Dictionary<string, CrewServed> CrewServedHour { get; set; } = new Dictionary<string, CrewServed>(StringComparer.Ordinal);
        // Crew sales not paid in yet, per vehicle and day; paid in after midnight like the game's shops (CrewTakings;
        // additive field: older saves read none).
        public List<CrewTaking> CrewTakings { get; set; } = new List<CrewTaking>();

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
        // The day the first licence was bought.
        public int? FirstLicensedDay { get; set; }

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
        // Item names keep their exact spelling; absent in older schema-1 saves.
        public Dictionary<string, long> CustomPriceCents { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        // The queue and the orders at the window while open (SalesSimulator); null when closed.
        public ServiceState? Service { get; set; }
        // "cart" or "truck", as in economy.json. Set when the mod first sees the vehicle.
        public string VehicleType { get; set; } = string.Empty;
        public bool HasPermit { get; set; }
        // A hire's route; null when the player runs the vehicle.
        public HireSchedule? Schedule { get; set; }
        // Set while a hire drives between stops; the vehicle is off the map until it arrives.
        public TransitState? Transit { get; set; }
        // Hidden cleanliness points and the grade on the window; null until the vehicle first opens.
        public HealthState? Health { get; set; }
        // The game's employee IDs of the hires who run this vehicle, in the order they were taken on.
        public List<string> Crew { get; set; } = new List<string>();
        // The route's planned days still running: today's, and yesterday's while a late stop runs past midnight.
        public List<ShiftPlan> Shifts { get; set; } = new List<ShiftPlan>();
        // The stop a hire opened the window at; null when the player opened it or it is closed.
        public int? HireStop { get; set; }
        // Where the crew last parked it for a stop: a truck may stand further from the stop than the stop radius.
        public ArrivalState? Arrival { get; set; }
        // A stop the crew couldn't park at, given up on until its opening ends.
        public SkipState? Skipped { get; set; }
        // What happened each day not yet counted into health and regulars: opened, cleaned, stale stock, and the
        // minutes open at each spot. Counted and dropped when a new day starts (ReputationBook.CloseDaysBefore).
        public List<DayRecord> Days { get; set; } = new List<DayRecord>();
        // The pitch the open window stands on, fixed when it opened; null when closed.
        public string? OpenPitch { get; set; }
        // The working day the vehicle last opened on, so cleaning after a late shift reaches that day.
        public int? LastOpenedDay { get; set; }
        // Food on board by the day it came on, oldest first, sold oldest first (stale stock).
        public Dictionary<string, List<StockBatch>> StockBatches { get; set; } = new Dictionary<string, List<StockBatch>>(StringComparer.Ordinal);
        // The premium spot this open window holds (first come among the player's vehicles); null when none.
        public string? PremiumSpot { get; set; }
        // Units of each item sold today and yesterday, for BizMan's fleet page (SoldCounts; additive field: older
        // saves read none). Older days are dropped as new sales come in.
        public List<SoldDay> Sold { get; set; } = new List<SoldDay>();
        // The start minute of the route leg the crew last restocked at the commissary for (CrewRestock): once per leg.
        public long? RestockedFor { get; set; }
        // The game day the commissary last texted what a crew restock was short of: once a day.
        public int? RestockShortDay { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // Street food revenue per in-game day, the mod's own record for income tax.
    public class DailySales
    {
        public int Day { get; set; }
        public long RevenueCents { get; set; }
        // What the stock sold that day cost at the wholesaler (additive field: older saves read 0).
        public long CostCents { get; set; }
        // True once this day's profit has been added to the game's financial summary, so Econoview shows
        // the carts' row only where the stored total includes it (summaries from before 0.3.0 don't).
        public bool InSummary { get; set; }
        // The day's money per vehicle, for Econoview's breakdown (additive field: days recorded by older
        // versions have none, and their profit shows as one "not broken down" figure).
        public List<VehicleDay> ByVehicle { get; set; } = new List<VehicleDay>();
        // The vendor licence bought that day; it belongs to the whole fleet, not to one vehicle.
        public long LicenceCents { get; set; }
        // Event places booked that day, less places paid back; below 0 on a day with more refunds than bookings.
        public long EventPlacesCents { get; set; }
        // Delivery fees paid that day, less refunded fees; goods become cost of goods only when sold.
        public long DeliveryFeesCents { get; set; }
        // What the day summary added to the game's total profit for this day, kept when the day is marked.
        // Null for days marked by older versions, which added sales less stock cost (SalesLedger.SummaryCents).
        public long? SummaryCents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // Hours one hire was counted as working on one vehicle on one payroll day (the game's day the hours fell in).
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

    // One vehicle's money on one day. Every amount is 0 or more: income and costs alike.
    public class VehicleDay
    {
        public string VehicleId { get; set; } = string.Empty;
        // Street sales at the window.
        public long SalesCents { get; set; }
        // Catering fees.
        public long CateringCents { get; set; }
        // The wholesale cost of the stock sold or served.
        public long StockCents { get; set; }
        // The crew's wages, which the game pays as unassigned staff wages (moved out of that row in Econoview).
        public long WagesCents { get; set; }
        // Parking fees and tickets, which the game charges in its own "Parking fees" row (moved out of it too).
        public long ParkingCents { get; set; }
        // The vehicle's vending permit.
        public long PermitCents { get; set; }
        public long CommissaryRentCents { get; set; }
        public long OvernightFineCents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // SalesSimulator's state for one open vehicle. Minute counts simulated minutes since it opened.
    public class ServiceState
    {
        public long RandomState { get; set; }
        public long Minute { get; set; }
        public long NextCustomerId { get; set; } = 1;
        // Customers walking up to the line, in the order they arrived; each joins Queue at JoinsAtMinute
        // (additive field: saves from 0.9.0 and earlier have none and load with an empty list).
        public List<ServiceCustomer> Approaching { get; set; } = new List<ServiceCustomer>();
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
        // While approaching: the minute they turned up. In the queue: the minute they joined it, which is
        // where their patience counts from.
        public long ArrivedAtMinute { get; set; }
        // Approaching only: the minute they reach the line and join the queue.
        public long JoinsAtMinute { get; set; }
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
        // Height and facing (degrees) where the vehicle stood: a cart is put back exactly there.
        public double Y { get; set; }
        public double Heading { get; set; }
        public int OpenAt { get; set; }
        public int CloseAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // One day of a hire's route as HireSchedules.PlanDay planned it. Times are absolute game minutes.
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
        // The stop it was parked for, when known; a route can visit one spot twice.
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
        // The stop given up, when known; a route can visit one spot twice.
        public int? StopIndex { get; set; }
        public long Until { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // Times are absolute game minutes: (day - 1) × 1440 + minute of the day.
    public class TransitState
    {
        public string ToSpotKey { get; set; } = string.Empty;
        public double ToX { get; set; }
        public double ToZ { get; set; }
        // The stop it is going to, and when that stop's planned opening ends; absent in older transits.
        public int? ToStopIndex { get; set; }
        public long? ToCloseAt { get; set; }
        public double ToY { get; set; }
        public double ToHeading { get; set; }
        // Where it left from, so it can go back there when the stop has no room for it.
        public double FromX { get; set; }
        public double FromY { get; set; }
        public double FromZ { get; set; }
        public double FromHeading { get; set; }
        // The game's parking state where it left from, as a number, to restore if it goes back there.
        public int FromParkingState { get; set; }
        public long DepartsAt { get; set; }
        public long ArrivesAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }
}
