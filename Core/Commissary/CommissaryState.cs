using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Commissary
{
    public class CommissaryState
    {
        public List<string> Unlocked { get; set; } = new List<string>();
        public List<CommissarySpace> Spaces { get; set; } = new List<CommissarySpace>();
        public List<CommissaryOrder> Orders { get; set; } = new List<CommissaryOrder>();
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
        public long FeeCents { get; set; }
        public int OrderedDay { get; set; }
        public int DeliverDay { get; set; }
        // Deliver has observed a failed payment; clearing the debt schedules the next delivery window.
        public bool AwaitingPayment { get; set; }
        [JsonIgnore]
        public long GoodsCents => checked(Units * UnitPriceCents);
        [JsonIgnore]
        public long TotalCents => checked(GoodsCents + FeeCents);

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

    // Rent charges name a vehicle. An unpaid order payment instead names OrderId and includes its fee;
    // the adapter books only FeeCents as a fleet expense when paid, leaving goods in the cargo cost.
    public class CommissaryCharge
    {
        public string VehicleId { get; set; } = string.Empty;
        public string CommissaryId { get; set; } = string.Empty;
        public int Day { get; set; }
        public long Cents { get; set; }
        public long? OrderId { get; set; }
        public long FeeCents { get; set; }

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
        public long GoodsCents { get; }
        public long FeeCents { get; }
        public long Total => checked(GoodsCents + FeeCents);

        internal CommissaryRefund(long goodsCents, long feeCents)
        {
            GoodsCents = goodsCents;
            FeeCents = feeCents;
            _ = Total;
        }
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
                    order.Units <= 0 || order.UnitPriceCents < 0 || order.FeeCents < 0 || order.OrderedDay < 1 || order.DeliverDay <= order.OrderedDay)
                {
                    Preserve(state, "_discardedOrders", order, order?.ExtensionData);
                    state.Orders.RemoveAt(i);
                }
            }
            // Don't silently rewrite malformed financial obligations or ambiguous order IDs.
            if (state.Orders.Any(o => o.Id < 1) || state.Orders.Select(o => o.Id).Distinct().Count() != state.Orders.Count)
                throw new JsonSerializationException("Commissary order IDs must be unique and positive.");
            state.NextOrderId = Math.Max(Math.Max(1, state.NextOrderId), checked(state.Orders.Select(o => o.Id).DefaultIfEmpty(0).Max() + 1));
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
            if (state.Fines.Any(f => f == null || string.IsNullOrWhiteSpace(f.VehicleId) || f.Day < 1 || f.Cents < 0) ||
                state.Charges.Any(c => c == null || (!c.OrderId.HasValue && string.IsNullOrWhiteSpace(c.VehicleId)) ||
                    c.OrderId < 1 || string.IsNullOrWhiteSpace(c.CommissaryId) || c.Day < 1 || c.Cents < 0 || c.FeeCents < 0 || c.FeeCents > c.Cents))
                throw new JsonSerializationException("Commissary pending payments must name their vehicle and day, with amounts not negative.");
            if (state.RentPaidThroughDay < 0 || state.NightCheckedThroughDay < 0)
                throw new JsonSerializationException("Commissary day cursors must not be negative.");
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
            // Older saves have no AwaitingPayment flag. Observe their debts before the adapter can retry them.
            var unpaid = new HashSet<long>(state.Charges.Where(c => c.OrderId.HasValue).Select(c => c.OrderId!.Value));
            foreach (var order in state.Orders.Where(o => unpaid.Contains(o.Id))) order.AwaitingPayment = true;
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
