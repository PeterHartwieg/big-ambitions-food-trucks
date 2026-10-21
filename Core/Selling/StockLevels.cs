using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;

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

        // Whole items of a food a pool would hold on its own (a coffee machine's 60 units hold 240 cups).
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

        // The foods the vehicle actually stocks, for its warnings and its Stock box: a food that exists in the game now
        // (`exists`, from the adapter; null: all do; a Bakery food without the Bakery mod doesn't) and that is on board
        // now or in the vehicle's sales record (VehicleState.Sold, the last two days it sold on, kept until it sells
        // again). So a food sold out stays flagged until it sells again elsewhere for two days, and a piece's other foods
        // the player never carries (a coffee machine's tea) are left out.
        public static IReadOnlyList<FoodStock> Stocked(DemandTables tables, StorageView view, VehicleState? vehicle, Func<string, bool>? exists = null)
        {
            var sold = new HashSet<string>(StringComparer.Ordinal);
            if (vehicle?.Sold != null)
                foreach (var day in vehicle.Sold)
                    if (day?.Units != null)
                        foreach (var pair in day.Units)
                            if (pair.Value > 0) sold.Add(pair.Key);
            return Of(tables, view).Where(f => (exists == null || exists(f.Item)) && (f.Units > 0 || sold.Contains(f.Item))).ToList();
        }
    }
}
