using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public enum TransferKind
    {
        // From the commissary's stock into the vehicle.
        Take,
        // From the vehicle back into the commissary's stock.
        Return
    }

    // One line of the bay's Commissary stock list: "Take Hotdog: 1,200 here", "Return Griddle: 1 on board".
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
        // A piece of equipment, moved one at a time; otherwise a food, moved by the unit.
        public bool IsPiece { get; }
        // Take: the units at the commissary. Return: the units on board that can go back.
        public int Units { get; }
        // Take: the most that loads now (a piece: 0 or 1). Return: all of Units.
        public int Most { get; }
        // Take: why Most is short of what was asked (Storage.Check's reason); None for a return.
        public LoadRefusal Refusal { get; }
    }

    // The two-way stock transfer at a commissary's bay (plan, "UI flow: the vehicle panel holds only cargo"; Peter,
    // 15 October 2026). Replaces Restock: the player picks what to take or give back. Loads follow storage per piece
    // (Storage.Check), so a take never asks for more than fits; a piece that leaves sends back the food that no
    // longer fits without it. Decides only; the game side moves the cargo and the stock.
    public static class StockTransfer
    {
        // Every choice the list offers: a take for each item in stock here (foods, then pieces), a return for each food
        // on board and each fitted piece. `stock` is the commissary's units by item; `onBoard` the vehicle's units that
        // can go back by item (the game leaves out unpaid, sealed and nested stacks). Items that are neither a food
        // nor a piece of equipment aren't the commissary's business and are left out.
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
                foreach (var pair in stock.Where(p => p.Value > 0 && Known(tables, p.Key) && IsPiece(tables, p.Key) == piece)
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

        // How many of the item load now when `wanted` are asked for, and why not all: the same rule as every other load
        // (CargoLimit), so the game's own refusal never fires on a take.
        public static LoadCheck Take(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo, string item, int wanted)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (string.IsNullOrWhiteSpace(item)) throw new ArgumentException("An item name is required.", nameof(item));
            return Storage.Check(tables, vehicleType, cargo, item, Math.Max(0, wanted), false);
        }

        // Fill up: food only, as Restock filled it (Storage.Restock: each fitted piece from its foods, a shared piece
        // split evenly by weight). Equipment in stock stays; a piece is fitted only when picked by name.
        public static RestockPlan FillUp(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo, IReadOnlyDictionary<string, int> stock)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            var foods = stock.Where(p => p.Value > 0 && tables.EquipmentForGameItem(p.Key) == null)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            return Storage.Restock(tables, vehicleType, cargo, foods);
        }

        // The food that has to go back with `pieces` of a piece of equipment: in each pool the piece made bigger (its own
        // kind's, or every pool for an Extra Storage), what the removal pushes over the smaller room, taken from the
        // food with the most load first so the pool stays balanced. Food that was over its room before the removal (an
        // old save) isn't sent back on its account. A pool with no piece left loses its room, so all its food goes.
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
                var room = after.Pools.FirstOrDefault(p => p.EquipmentId == pool.EquipmentId)?.Capacity ?? 0;
                // Keep what was over the old room over the new one too: only the removal's share goes back.
                var keep = room + Math.Max(0, pool.Load - pool.Capacity);
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

        // The cargo with `count` of an item taken out, last stacks first.
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
