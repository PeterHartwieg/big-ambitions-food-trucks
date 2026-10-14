using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Commissary
{
    // One item a logistics run brings: what is missing up to its target, priced per unit.
    public sealed class CommissaryLogisticsLine
    {
        internal CommissaryLogisticsLine(string itemName, int units, long unitPriceCents)
        {
            ItemName = itemName;
            Units = units;
            UnitPriceCents = unitPriceCents;
        }

        public string ItemName { get; }
        public int Units { get; }
        public long UnitPriceCents { get; }
    }

    public sealed class CommissaryLogisticsPlan
    {
        internal CommissaryLogisticsPlan(bool accepted, IReadOnlyList<CommissaryLogisticsLine> lines, IReadOnlyList<string> unpriced)
        {
            Accepted = accepted;
            Lines = lines;
            Unpriced = unpriced;
        }

        // False when the commissary takes no deliveries (no space rented there): nothing is brought.
        public bool Accepted { get; }
        public IReadOnlyList<CommissaryLogisticsLine> Lines { get; }
        // Items with a target but no price in the catalogue (a mod's item gone, say): left alone.
        public IReadOnlyList<string> Unpriced { get; }
    }

    // A commissary as a destination of the game's logistics plans (Peter, 14 October 2026). At the game's 02:00 run the
    // plan's warehouse fills the commissary up to each stock target; the goods become batches at the wholesale price, as
    // orders do, so the uninstall refund treats them alike. No fee and no money: the game's logistics charge none.
    // The game moves the goods; this decides how many.
    public static class CommissaryLogistics
    {
        // OrderMetadata on a batch a logistics run brought: "source" is SourceValue, "day" the game day, "from" the warehouse.
        public const string SourceKey = "source";
        public const string SourceValue = "logistics";

        // A plan delivers only where the player has a space, as ordering by phone needs one.
        public static bool Accepts(CommissaryState state, string commissaryId) => CommissaryOrdering.CanOrder(state, commissaryId);

        // What to bring: each target less what the commissary holds of that item, in the targets' order. The game fills
        // target by target and reads the stock again each time, so two targets for one item come to the larger one.
        // Targets of 0 or less are skipped, as the game skips them; an item without a price is reported, not brought.
        public static CommissaryLogisticsPlan Plan(CommissaryState state, string commissaryId,
            IEnumerable<KeyValuePair<string, int>> targets, IReadOnlyDictionary<string, long> unitPriceCents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (unitPriceCents == null) throw new ArgumentNullException(nameof(unitPriceCents));
            if (string.IsNullOrWhiteSpace(commissaryId)) throw new ArgumentException("ID must not be empty.", nameof(commissaryId));
            if (!Accepts(state, commissaryId))
                return new CommissaryLogisticsPlan(false, Array.Empty<CommissaryLogisticsLine>(), Array.Empty<string>());
            var wanted = new List<KeyValuePair<string, int>>();
            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.Key) || target.Value <= 0) continue;
                var index = wanted.FindIndex(w => w.Key == target.Key);
                if (index < 0) wanted.Add(target);
                else if (target.Value > wanted[index].Value) wanted[index] = target;
            }
            var lines = new List<CommissaryLogisticsLine>();
            var unpriced = new List<string>();
            foreach (var target in wanted)
            {
                if (!unitPriceCents.TryGetValue(target.Key, out var price) || price < 0)
                {
                    unpriced.Add(target.Key);
                    continue;
                }
                var missing = target.Value - Held(state, commissaryId, target.Key);
                if (missing > 0) lines.Add(new CommissaryLogisticsLine(target.Key, (int)missing, price));
            }
            return new CommissaryLogisticsPlan(true, lines, unpriced);
        }

        // Units arrived from the warehouse: one new batch, last in the commissary's FIFO order.
        public static CommissaryStockLine Receive(CommissaryState state, string commissaryId, string itemName, int units,
            long unitPriceCents, int day, string from)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(commissaryId)) throw new ArgumentException("ID must not be empty.", nameof(commissaryId));
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An item name is required.", nameof(itemName));
            if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (unitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(unitPriceCents));
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            _ = checked(units * unitPriceCents);
            var line = new CommissaryStockLine
            {
                ItemName = itemName, Units = units, UnitPriceCents = unitPriceCents, BatchId = CommissaryValidation.NextBatch(state),
                OrderMetadata = new Dictionary<string, JToken>(StringComparer.Ordinal)
                {
                    [SourceKey] = SourceValue,
                    ["day"] = day,
                    ["from"] = from ?? string.Empty
                }
            };
            if (!state.Stock.TryGetValue(commissaryId, out var stock)) state.Stock[commissaryId] = stock = new List<CommissaryStockLine>();
            stock.Add(line);
            return line;
        }

        // The goods value of stock a logistics run brought, still in the commissaries. Its cost was never booked under the
        // commissary's tax label, so the uninstall refund takes only the rest off that label's deduction.
        public static long GoodsCents(CommissaryState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            long cents = 0;
            foreach (var line in state.Stock.Values.SelectMany(l => l))
                if (FromLogistics(line)) cents = checked(cents + checked(line.Units * line.UnitPriceCents));
            return cents;
        }

        public static bool FromLogistics(CommissaryStockLine line) =>
            line?.OrderMetadata != null && line.OrderMetadata.TryGetValue(SourceKey, out var source) &&
            source.Type == JTokenType.String && (string?)source == SourceValue;

        // What the commissary holds of an item, all batches.
        public static long Held(CommissaryState state, string commissaryId, string itemName)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Stock.TryGetValue(commissaryId, out var stock)
                ? stock.Where(l => l.ItemName == itemName).Sum(l => (long)l.Units)
                : 0;
        }

        // A commissary stands in a plan as a street number on a street that has no building registration, so every
        // game check on it finds none (adapter: CommissaryLogistics). The number is fixed per commissary id, so a save
        // keeps its destinations when data/commissaries.json is reordered: FNV-1a of the id, always negative.
        public static int AddressNumber(string commissaryId)
        {
            if (string.IsNullOrWhiteSpace(commissaryId)) throw new ArgumentException("ID must not be empty.", nameof(commissaryId));
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in commissaryId)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return -1 - (int)(hash & 0x3FFFFFFF);
            }
        }

        // The commissary a street number stands for, among these ids; null for none.
        public static string? IdFor(IEnumerable<string> commissaryIds, int number)
        {
            if (commissaryIds == null) throw new ArgumentNullException(nameof(commissaryIds));
            if (number >= 0) return null;
            return commissaryIds.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && AddressNumber(id) == number);
        }
    }
}
