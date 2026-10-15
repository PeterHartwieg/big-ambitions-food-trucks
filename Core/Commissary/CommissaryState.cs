using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Commissary
{
    public class CommissaryState
    {
        public List<string> Unlocked { get; set; } = new List<string>();
        public List<CommissarySpace> Spaces { get; set; } = new List<CommissarySpace>();
        public List<CommissaryOrder> Orders { get; set; } = new List<CommissaryOrder>();
        // Phone orders that repeat every day or every week (CommissaryStandingOrders); each due day books its delivery.
        public List<CommissaryStandingOrder> StandingOrders { get; set; } = new List<CommissaryStandingOrder>();
        public long NextStandingId { get; set; } = 1;
        // Food boxes delivered from phone orders per commissary, item and game week, for the weekly limit, which counts
        // them over all commissaries (CommissarySupply). Weeks before the current one are dropped.
        public List<CommissaryWeekBoxes> WeekBoxes { get; set; } = new List<CommissaryWeekBoxes>();
        // Deliveries whose fee is paid: one per commissary and delivery day (Commissaries.Deliver), so a later run the same
        // day, or one after a payment that threw, doesn't charge it again. Days before yesterday are dropped.
        public List<CommissaryFeePaid> FeesPaid { get; set; } = new List<CommissaryFeePaid>();
        // Separate batches, including when prices differ, in arrival order. No averaging of goods costs.
        public Dictionary<string, List<CommissaryStockLine>> Stock { get; set; } = new Dictionary<string, List<CommissaryStockLine>>(StringComparer.Ordinal);
        public long NextOrderId { get; set; } = 1;
        public long NextBatchId { get; set; } = 1;
        public int? RentPaidThroughDay { get; set; }
        // Released spaces keep their processed rent day, including failed charges, across saves and re-renting.
        public Dictionary<string, int> ReleasedRentPaidThroughDay { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public int? NightCheckedThroughDay { get; set; }
        // Like CateringState.Unpaid: only payments that failed. The adapter adds failures and removes paid
        // retries, recording the expense in SalesLedger only when the game actually takes the money.
        public List<CommissaryFine> Fines { get; set; } = new List<CommissaryFine>();
        public List<CommissaryCharge> Charges { get; set; } = new List<CommissaryCharge>();
        // Each commissary's bay as resolved in this city (CommissaryBays): the points the search started from and where
        // it found legal parking, so a load decides "in the bay" from the same points without searching again.
        public Dictionary<string, CommissaryBaySet> Bays { get; set; } = new Dictionary<string, CommissaryBaySet>(StringComparer.Ordinal);
        // Units of commissary-ordered stock (deducted under the commissary's tax label) out on the fleet's vehicles, per
        // item, so a return keeps that origin (Commissaries.TookOnVehicle, ReturnFromVehicle). Additive.
        public Dictionary<string, int> OrderedOnVehicles { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        // Their deducted cost in cents, per item, beside the units: taken out at the pool's average cost (the game merges
        // stacks at an averaged price, so the cargo's price isn't it; review round 3 of the UI flow). Additive.
        public Dictionary<string, long> OrderedOnVehiclesCents { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        // Overnight cleaning waiting for a vehicle's street side to be read, by vehicle: the service days
        // (Commissaries.CleanedOvernight, ResolvePendingCleaning). Additive.
        public Dictionary<string, List<int>> PendingCleaning { get; set; } = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryBaySet
    {
        // BaySnap.RuleVersion when it was searched: a newer rule searches again.
        public int Rule { get; set; }
        // Where the search started (the data point, else the building's entrances), in order.
        public List<CommissaryPoint> Origins { get; set; } = new List<CommissaryPoint>();
        // The bays found on legal parking; empty when the search found none (that commissary has no bay).
        public List<CommissaryBayPoint> Points { get; set; } = new List<CommissaryBayPoint>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryPoint
    {
        public double X { get; set; }
        public double Z { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryBayPoint
    {
        public double X { get; set; }
        public double Z { get; set; }
        // The way the parking lane runs there.
        public double AlongX { get; set; }
        public double AlongZ { get; set; }
        // The entrance its street side was checked from (Core BaySide): a vehicle is in this bay only on that side.
        // Null on a point stored without one, whose side is then unknown.
        public double? DoorX { get; set; }
        public double? DoorZ { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissarySpace
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string CommissaryId { get; set; } = string.Empty;
        public int SinceDay { get; set; }
        // Each space has its own cursor: renting after today's fleet rent run must still charge its first day.
        // This marks charges returned to the adapter, including ones held in Charges after a failed payment.
        public int? RentPaidThroughDay { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryOrder
    {
        public long Id { get; set; }
        public string CommissaryId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int Units { get; set; }
        public long UnitPriceCents { get; set; }
        // The delivery fee paid with this order: set on delivery, the flat fee on the first order of that morning's delivery
        // at its commissary and 0 on the others (Commissaries.Deliver); 0 while it waits.
        public long FeeCents { get; set; }
        public int OrderedDay { get; set; }
        // Paid when it arrives (Commissaries.Deliver), at the delivery minute of this day.
        public int DeliverDay { get; set; }
        // Its food boxes, counted towards its item's weekly limit on phone orders in DeliverDay's week; 0 for equipment.
        public int CappedBoxes { get; set; }
        // The standing order that booked it on its delivery day (then OrderedDay is DeliverDay); null for a phone order.
        public long? StandingOrderId { get; set; }
        // The day its boxes count on for the weekly limit when that isn't DeliverDay: a standing delivery brought
        // forward by Ctrl+Alt+F3 counts in the week of its scheduled day. Null: DeliverDay.
        public int? CountDay { get; set; }
        [JsonIgnore]
        public int LimitDay => CountDay ?? DeliverDay;
        [JsonIgnore]
        public long GoodsCents => checked(Units * UnitPriceCents);
        [JsonIgnore]
        public long TotalCents => checked(GoodsCents + FeeCents);

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public enum CommissaryRepeat
    {
        Once,
        Daily,
        // Every week on the weekday of its first delivery.
        Weekly
    }

    // A phone order that repeats: each due day it books one delivery for that day's delivery minute at that day's prices.
    public class CommissaryStandingOrder
    {
        public long Id { get; set; }
        public string CommissaryId { get; set; } = string.Empty;
        [JsonConverter(typeof(StringEnumConverter))]
        public CommissaryRepeat Repeat { get; set; } = CommissaryRepeat.Daily;
        public int CreatedDay { get; set; }
        // The next delivery day not booked yet.
        public int NextDay { get; set; }
        public List<CommissaryStandingLine> Lines { get; set; } = new List<CommissaryStandingLine>();
        // Texted that no vehicle has a space there; cleared once a delivery books again.
        public bool NoSpaceTold { get; set; }
        // Items texted as no longer sold; each cleared once it sells again.
        public List<string> NotSoldTold { get; set; } = new List<string>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryFeePaid
    {
        public string CommissaryId { get; set; } = string.Empty;
        public int Day { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryWeekBoxes
    {
        public string CommissaryId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        // The week's first day (a Monday: 1, 8, 15 ...).
        public int WeekStart { get; set; }
        public int Boxes { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryStandingLine
    {
        public string ItemName { get; set; } = string.Empty;
        public int Boxes { get; set; }
        // Food, counted towards its weekly limit.
        public bool Capped { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryStockLine
    {
        public string ItemName { get; set; } = string.Empty;
        public int Units { get; set; }
        public long UnitPriceCents { get; set; }
        // Stable arrival order, also carried by taken lines so ReturnToStock restores the original FIFO order.
        public long BatchId { get; set; }
        // Unknown order fields live in their own namespace: they must never override stock fields such as BatchId.
        public IDictionary<string, JToken> OrderMetadata { get; set; } = new Dictionary<string, JToken>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    // A day's rent for a vehicle's space that the game refused, tried again. Orders are paid on delivery and never wait
    // here (Commissaries.Deliver).
    public class CommissaryCharge
    {
        public string VehicleId { get; set; } = string.Empty;
        public string CommissaryId { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryFine
    {
        public string VehicleId { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class CommissaryVehicle
    {
        public string VehicleId { get; set; } = string.Empty;
        public bool AtOwnCommissaryBay { get; set; }
        // Within its own bay's radius, but which side of the street it stands on couldn't be read yet (no driving lanes
        // read round the door, as just after a load): the night check waits rather than fine it (review round 2, Astra
        // and Sol). Not saved.
        [JsonIgnore]
        public bool BaySideUnknown { get; set; }
        // In its bay only because its commissary's search found no legal parking (no bay to stand in): spared the fine,
        // but not cleaned there overnight (Commissaries.CleanedOvernight). Not saved.
        [JsonIgnore]
        public bool BayGrace { get; set; }
        public bool PlayerInside { get; set; }
        public bool OnLateStop { get; set; }
        public bool InTransit { get; set; }
        // Parked at or inside a building the player rents or owns (Peter, 9 October 2026): not fined, with or without a space.
        public bool AtPlayerProperty { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class CommissaryRefund
    {
        // Delivered stock at its goods price; its delivery fees were for a delivery made and aren't refunded.
        public long GoodsCents { get; }
        public long Total => GoodsCents;
        // Open orders cancelled: not paid yet, so nothing to refund.
        public int CancelledOrders { get; }
        public int CancelledStandingOrders { get; }

        internal CommissaryRefund(long goodsCents, int cancelledOrders, int cancelledStandingOrders)
        {
            GoodsCents = goodsCents;
            CancelledOrders = cancelledOrders;
            CancelledStandingOrders = cancelledStandingOrders;
        }
    }

    // What one delivery run did: the orders paid and put into stock, and those whose payment the game refused (cancelled).
    public sealed class CommissaryDeliveries
    {
        internal CommissaryDeliveries(IReadOnlyList<CommissaryOrder> delivered, IReadOnlyList<CommissaryOrder> refused)
        {
            Delivered = delivered;
            Refused = refused;
        }

        public IReadOnlyList<CommissaryOrder> Delivered { get; }
        public IReadOnlyList<CommissaryOrder> Refused { get; }
    }

    public static class CommissaryValidation
    {
        // Loading repairs known bad entries, without consulting item/commissary/kind catalogues: unknown IDs
        // may belong to a newer mod. Metadata on discarded entries is retained in ExtensionData for round trips.
        public static void Validate(CommissaryState state)
        {
            if (state == null) throw new JsonSerializationException("Commissary state must not be null.");
            state.ExtensionData ??= new Dictionary<string, JToken>();
            state.Unlocked ??= new List<string>();
            state.Unlocked = state.Unlocked.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            state.Spaces ??= new List<CommissarySpace>();
            state.ReleasedRentPaidThroughDay ??= new Dictionary<string, int>(StringComparer.Ordinal);
            if (state.ReleasedRentPaidThroughDay.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0))
                throw new JsonSerializationException("Released rent cursors must name vehicles with days not negative.");
            var vehicles = new HashSet<string>(StringComparer.Ordinal);
            // Last entry wins, as a later rental replaces a vehicle's old space.
            for (var i = state.Spaces.Count - 1; i >= 0; i--)
            {
                var space = state.Spaces[i];
                if (space == null || string.IsNullOrWhiteSpace(space.VehicleId) || string.IsNullOrWhiteSpace(space.CommissaryId) ||
                    string.IsNullOrWhiteSpace(space.Kind) || space.SinceDay < 1 || !vehicles.Add(space.VehicleId))
                {
                    Preserve(state, "_discardedSpaces", space, space?.ExtensionData);
                    state.Spaces.RemoveAt(i);
                }
                else if (space.RentPaidThroughDay < space.SinceDay - 1) space.RentPaidThroughDay = space.SinceDay - 1;
            }
            state.Orders ??= new List<CommissaryOrder>();
            for (var i = state.Orders.Count - 1; i >= 0; i--)
            {
                var order = state.Orders[i];
                if (order == null || string.IsNullOrWhiteSpace(order.CommissaryId) || string.IsNullOrWhiteSpace(order.ItemName) ||
                    order.Units <= 0 || order.UnitPriceCents < 0 || order.FeeCents < 0 || order.OrderedDay < 1 || order.CappedBoxes < 0 || order.CountDay < 1 ||
                    order.DeliverDay < order.OrderedDay || (order.DeliverDay == order.OrderedDay && order.StandingOrderId == null))
                {
                    Preserve(state, "_discardedOrders", order, order?.ExtensionData);
                    state.Orders.RemoveAt(i);
                }
            }
            // Don't silently rewrite malformed financial obligations or ambiguous order IDs.
            if (state.Orders.Any(o => o.Id < 1) || state.Orders.Select(o => o.Id).Distinct().Count() != state.Orders.Count)
                throw new JsonSerializationException("Commissary order IDs must be unique and positive.");
            state.NextOrderId = Math.Max(Math.Max(1, state.NextOrderId), checked(state.Orders.Select(o => o.Id).DefaultIfEmpty(0).Max() + 1));
            state.StandingOrders ??= new List<CommissaryStandingOrder>();
            for (var i = state.StandingOrders.Count - 1; i >= 0; i--)
            {
                var standing = state.StandingOrders[i];
                if (standing != null) standing.NotSoldTold ??= new List<string>();
                if (standing?.Lines != null) standing.Lines.RemoveAll(l => l == null || string.IsNullOrWhiteSpace(l.ItemName) || l.Boxes <= 0);
                if (standing == null || standing.Id < 1 || string.IsNullOrWhiteSpace(standing.CommissaryId) || standing.CreatedDay < 1 ||
                    standing.NextDay < 1 || !Enum.IsDefined(typeof(CommissaryRepeat), standing.Repeat) || standing.Repeat == CommissaryRepeat.Once ||
                    standing.Lines == null || standing.Lines.Count == 0 || state.StandingOrders.Take(i).Any(s => s?.Id == standing.Id))
                {
                    Preserve(state, "_discardedStandingOrders", standing, standing?.ExtensionData);
                    state.StandingOrders.RemoveAt(i);
                }
            }
            state.FeesPaid ??= new List<CommissaryFeePaid>();
            state.FeesPaid.RemoveAll(f => f == null || string.IsNullOrWhiteSpace(f.CommissaryId) || f.Day < 1);
            state.WeekBoxes ??= new List<CommissaryWeekBoxes>();
            state.WeekBoxes.RemoveAll(w => w == null || string.IsNullOrWhiteSpace(w.CommissaryId) || string.IsNullOrWhiteSpace(w.ItemName) ||
                                           w.WeekStart < 1 || w.Boxes < 0);
            state.NextStandingId = Math.Max(Math.Max(1, state.NextStandingId), checked(state.StandingOrders.Select(o => o.Id).DefaultIfEmpty(0).Max() + 1));
            state.Stock ??= new Dictionary<string, List<CommissaryStockLine>>(StringComparer.Ordinal);
            foreach (var id in state.Stock.Keys.ToList())
            {
                if (string.IsNullOrWhiteSpace(id) || state.Stock[id] == null)
                    throw new JsonSerializationException("Commissary stock must name its commissary and hold a list.");
                var lines = state.Stock[id];
                for (var i = lines.Count - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (line == null || line.Units <= 0 || line.UnitPriceCents < 0 || string.IsNullOrWhiteSpace(line.ItemName))
                    {
                        Preserve(state, "_discardedStock", line, line?.ExtensionData);
                        lines.RemoveAt(i);
                    }
                }
            }
            var batches = state.Stock.Values.SelectMany(lines => lines).ToList();
            state.NextBatchId = Math.Max(Math.Max(1, state.NextBatchId), checked(batches.Select(l => l.BatchId).DefaultIfEmpty(0).Max() + 1));
            foreach (var line in batches.Where(l => l.BatchId <= 0)) line.BatchId = NextBatch(state);
            if (batches.Select(l => l.BatchId).Distinct().Count() != batches.Count)
                throw new JsonSerializationException("Commissary stock batch IDs must be unique.");
            state.Fines ??= new List<CommissaryFine>();
            state.Charges ??= new List<CommissaryCharge>();
            // Before 0.9.10 an order paid at booking could wait here unpaid, naming no vehicle. Orders are paid on
            // delivery now, so such a charge is set aside (kept in the save, not collected).
            for (var i = state.Charges.Count - 1; i >= 0; i--)
            {
                var charge = state.Charges[i];
                if (charge != null && string.IsNullOrWhiteSpace(charge.VehicleId) && charge.ExtensionData?.Keys.Any(k => string.Equals(k, "orderId", StringComparison.OrdinalIgnoreCase)) == true)
                {
                    Preserve(state, "_discardedCharges", charge, charge.ExtensionData);
                    state.Charges.RemoveAt(i);
                }
            }
            if (state.Fines.Any(f => f == null || string.IsNullOrWhiteSpace(f.VehicleId) || f.Day < 1 || f.Cents < 0) ||
                state.Charges.Any(c => c == null || string.IsNullOrWhiteSpace(c.VehicleId) ||
                    string.IsNullOrWhiteSpace(c.CommissaryId) || c.Day < 1 || c.Cents < 0))
                throw new JsonSerializationException("Commissary pending payments must name their vehicle and day, with amounts not negative.");
            if (state.RentPaidThroughDay < 0 || state.NightCheckedThroughDay < 0)
                throw new JsonSerializationException("Commissary day cursors must not be negative.");
            // Bookkeeping only: entries that make no sense are dropped, never refused.
            state.OrderedOnVehicles ??= new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in state.OrderedOnVehicles.Keys.ToList())
                if (string.IsNullOrWhiteSpace(item) || state.OrderedOnVehicles[item] <= 0) state.OrderedOnVehicles.Remove(item);
            state.OrderedOnVehiclesCents ??= new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var item in state.OrderedOnVehiclesCents.Keys.ToList())
                if (!state.OrderedOnVehicles.ContainsKey(item ?? "") || state.OrderedOnVehiclesCents[item!] < 0) state.OrderedOnVehiclesCents.Remove(item!);
            state.PendingCleaning ??= new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var vehicle in state.PendingCleaning.Keys.ToList())
            {
                var days = state.PendingCleaning[vehicle]?.Where(d => d >= 1).Distinct().ToList();
                if (string.IsNullOrWhiteSpace(vehicle) || days == null || days.Count == 0) state.PendingCleaning.Remove(vehicle);
                else state.PendingCleaning[vehicle] = days;
            }
            // A resolved bay is only a cache of the city: a malformed one is dropped and searched again.
            state.Bays ??= new Dictionary<string, CommissaryBaySet>(StringComparer.Ordinal);
            foreach (var id in state.Bays.Keys.ToList())
            {
                var set = state.Bays[id];
                if (string.IsNullOrWhiteSpace(id) || !CommissaryBays.WellFormed(set))
                {
                    Preserve(state, "_discardedBays", set, set?.ExtensionData);
                    state.Bays.Remove(id);
                }
            }
        }

        internal static long NextBatch(CommissaryState state)
        {
            var id = state.NextBatchId;
            state.NextBatchId = checked(id + 1);
            return id;
        }

        private static void Preserve(CommissaryState state, string key, object? entry, IDictionary<string, JToken>? extra)
        {
            if (entry == null || extra == null || extra.Count == 0) return;
            // A collision with a future field is retained too, instead of replacing its value.
            if (!state.ExtensionData.TryGetValue(key, out var token) || !(token is JArray archive))
            {
                archive = new JArray();
                if (token != null) archive.Add(token.DeepClone());
                state.ExtensionData[key] = archive;
            }
            archive.Add(JObject.FromObject(entry));
        }
    }
}
