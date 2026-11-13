using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public enum TransferKind
    {
        Take,
        Return
    }

    public sealed class TransferChoice
    {
        internal TransferChoice(TransferKind kind, string item, bool isPiece, int units, int most, LoadRefusal refusal)
        {
            Kind = kind;
            Item = item;
            IsPiece = isPiece;
            Units = units;
            Most = most;
            Refusal = refusal;
        }

        public TransferKind Kind { get; }
        public string Item { get; }
        public bool IsPiece { get; }
        public int Units { get; }
        public int Most { get; }
        public LoadRefusal Refusal { get; }
    }

    public static class StockTransfer
    {
        public static IReadOnlyList<TransferChoice> Choices(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            IReadOnlyDictionary<string, int> stock, IReadOnlyDictionary<string, int> onBoard)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            if (onBoard == null) throw new ArgumentNullException(nameof(onBoard));
            var lines = cargo.ToList();
            var choices = new List<TransferChoice>();
            foreach (var piece in new[] { false, true })
                foreach (var pair in stock.Where(p => p.Value > 0 && !string.IsNullOrWhiteSpace(p.Key) && IsPiece(tables, p.Key) == piece)
                             .OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var check = Take(tables, vehicleType, lines, pair.Key, piece ? 1 : pair.Value);
                    choices.Add(new TransferChoice(TransferKind.Take, pair.Key, piece, pair.Value, check.Allowed, check.Refusal));
                }
            foreach (var piece in new[] { false, true })
                foreach (var pair in onBoard.Where(p => p.Value > 0 && Known(tables, p.Key) && IsPiece(tables, p.Key) == piece)
                             .OrderBy(p => p.Key, StringComparer.Ordinal))
                    choices.Add(new TransferChoice(TransferKind.Return, pair.Key, piece, pair.Value, pair.Value, LoadRefusal.None));
            return choices;
        }

        public static LoadCheck Take(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo, string item, int wanted)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (string.IsNullOrWhiteSpace(item)) throw new ArgumentException("An item name is required.", nameof(item));
            return Storage.Check(tables, vehicleType, cargo, item, Math.Max(0, wanted), false);
        }

        public static RestockPlan FillUp(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo, IReadOnlyDictionary<string, int> stock)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            var foods = stock.Where(p => p.Value > 0 && tables.EquipmentForGameItem(p.Key) == null)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            return Storage.Restock(tables, vehicleType, cargo, foods);
        }

        public static IReadOnlyDictionary<string, int> FoodLeavingWith(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            string pieceItem, int pieces = 1)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var leaving = new Dictionary<string, int>(StringComparer.Ordinal);
            if (tables.EquipmentForGameItem(pieceItem) == null || pieces <= 0) return leaving;
            var lines = cargo.ToList();
            var before = Storage.Of(tables, vehicleType, lines);
            if (before == null) return leaving;
            var after = Storage.Of(tables, vehicleType, Without(lines, pieceItem, pieces))!;
            foreach (var pool in before.Pools)
            {
                var keep = after.Pools.FirstOrDefault(p => p.EquipmentId == pool.EquipmentId)?.Capacity ?? 0;
                if (keep >= pool.Capacity) continue;
                var units = pool.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                var load = pool.Load;
                while (load > keep + 1e-9)
                {
                    var next = units.Where(p => p.Value > 0)
                        .OrderByDescending(p => p.Value * tables.Product(p.Key)!.StockWeight).ThenBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => p.Key).FirstOrDefault();
                    if (next == null) break;
                    units[next]--;
                    load -= tables.Product(next)!.StockWeight;
                    leaving[next] = (leaving.TryGetValue(next, out var n) ? n : 0) + 1;
                }
            }
            return leaving;
        }

        private static bool IsPiece(DemandTables tables, string item) => tables.EquipmentForGameItem(item) != null;

        private static bool Known(DemandTables tables, string item) => IsPiece(tables, item) || tables.Product(item) != null;

        private static List<CargoLine> Without(List<CargoLine> lines, string item, int count)
        {
            var result = new List<CargoLine>(lines);
            for (var i = result.Count - 1; i >= 0 && count > 0; i--)
            {
                var line = result[i];
                if (line.ItemName != item || line.Amount <= 0) continue;
                var take = Math.Min(count, line.Amount);
                count -= take;
                if (take == line.Amount) result.RemoveAt(i);
                else result[i] = new CargoLine(line.ItemName, line.Amount - take, line.HasNestedCargo);
            }
            return result;
        }
    }
}
