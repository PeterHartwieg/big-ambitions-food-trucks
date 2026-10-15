using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public enum StockLevel { Ok, Low, Empty }

    // One food in one fitted piece, for the Stock box on a vehicle's BizMan page: how many are on board and how many
    // the piece would hold if it held only this food.
    public sealed class FoodStock
    {
        public FoodStock(string equipmentId, string item, int units, int capacity, StockLevel level)
        {
            EquipmentId = equipmentId;
            Item = item;
            Units = units;
            Capacity = capacity;
            Level = level;
        }

        public string EquipmentId { get; }
        public string Item { get; }
        public int Units { get; }
        public int Capacity { get; }
        public StockLevel Level { get; }
    }

    // Low stock the game's way: a shelf at or under a quarter of its capacity makes a "low stock" to-do, an empty one
    // "empty stock" (ItemHelper, TodoTaskType). A food's capacity is its piece's room in that food's own units.
    public static class StockLevels
    {
        public const double LowShare = 0.25;

        public static StockLevel Level(int units, int capacity)
        {
            if (units <= 0) return StockLevel.Empty;
            return capacity > 0 && units <= capacity * LowShare ? StockLevel.Low : StockLevel.Ok;
        }

        // Whole items of a food a pool would hold on its own (a coffee machine's 40 units hold 160 cups).
        public static int CapacityFor(DemandTables tables, StoragePool pool, string item)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            var weight = tables.Product(item)?.StockWeight ?? 1.0;
            if (weight <= 0) weight = 1.0;
            return (int)Math.Min(int.MaxValue, Math.Floor(pool.Capacity / weight + 1e-9));
        }

        // Every food of every fitted piece, in fitting order then the piece's own order, foods with none on board included.
        public static IReadOnlyList<FoodStock> Of(DemandTables tables, StorageView view)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (view == null) throw new ArgumentNullException(nameof(view));
            var list = new List<FoodStock>();
            foreach (var pool in view.Pools)
                foreach (var item in pool.Piece.Products)
                {
                    pool.Units.TryGetValue(item, out var units);
                    var capacity = CapacityFor(tables, pool, item);
                    list.Add(new FoodStock(pool.EquipmentId, item, units, capacity, Level(units, capacity)));
                }
            return list;
        }
    }
}
