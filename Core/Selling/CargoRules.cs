using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public readonly struct CargoLine
    {
        public CargoLine(string? itemName, int amount, bool hasNestedCargo)
        {
            ItemName = itemName;
            Amount = amount;
            HasNestedCargo = hasNestedCargo;
        }

        public string? ItemName { get; }
        public int Amount { get; }
        public bool HasNestedCargo { get; }
    }

    public static class CargoRules
    {
        public static int FoodUnits(DemandTables tables, IEnumerable<CargoLine> cargo) =>
            (int)Math.Min(int.MaxValue, Math.Ceiling(FoodLoad(tables, cargo)));

        public static double FoodLoad(DemandTables tables, IEnumerable<CargoLine> cargo)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var load = 0.0;
            foreach (var line in cargo)
            {
                var product = tables.Product(line.ItemName);
                if (product != null && !line.HasNestedCargo && line.Amount > 0)
                    load += line.Amount * product.StockWeight;
            }
            return load;
        }

        public static IReadOnlyList<string> FittedEquipment(DemandTables tables, IEnumerable<CargoLine> cargo)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var fitted = new List<string>();
            foreach (var line in cargo)
            {
                var piece = tables.EquipmentForGameItem(line.ItemName);
                if (piece == null) continue;
                for (var i = 0; i < line.Amount; i++) fitted.Add(piece.Id);
            }
            return fitted;
        }

        public static int AllowedUnits(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            string? incomingItem, int incomingAmount, bool incomingHasNestedCargo) =>
            Storage.Check(tables, vehicleType, cargo, incomingItem, incomingAmount, incomingHasNestedCargo).Allowed;
    }
}
