using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    // One cargo entry as the game adapter reads it; nested cargo stays opaque to Core.
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
        // Only loose products count towards the food limit, whether or not currently on the menu.
        public static int FoodUnits(DemandTables tables, IEnumerable<CargoLine> cargo) =>
            CountUnits(tables, cargo, equipment: false);

        // Each mapped entry fits Amount pieces, in cargo order. Contents of nested cargo are not read.
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

        // Core enforces unit and equipment limits; the game still enforces shared cargo slots.
        public static int AllowedUnits(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            string? incomingItem, int incomingAmount, bool incomingHasNestedCargo)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var amount = Math.Max(0, incomingAmount);
            var vehicle = tables.Vehicle(vehicleType);
            if (amount == 0 || vehicle == null) return amount;
            // Equipment takes a slot whether or not it carries stock inside it (a stocked drinks fridge).
            if (tables.EquipmentForGameItem(incomingItem) != null)
                return StockLimit.CanLoad(CountUnits(tables, cargo, equipment: true), amount, vehicle.EquipmentSlots);
            if (incomingHasNestedCargo) return amount;
            if (tables.Product(incomingItem) != null)
                return StockLimit.CanLoad(FoodUnits(tables, cargo), amount, vehicle.StockUnits);
            return amount;
        }

        private static int CountUnits(DemandTables tables, IEnumerable<CargoLine> cargo, bool equipment)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var units = 0;
            foreach (var line in cargo)
            {
                var counts = equipment ? tables.EquipmentForGameItem(line.ItemName) != null
                    : !line.HasNestedCargo && tables.Product(line.ItemName) != null;
                if (counts && line.Amount > 0)
                    units = (int)Math.Min(int.MaxValue, (long)units + line.Amount);
            }
            return units;
        }
    }
}
