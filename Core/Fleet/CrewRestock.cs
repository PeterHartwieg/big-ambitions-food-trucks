using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;

namespace FoodTrucks.Core.Fleet
{
    // Crews restock at the commissary (plan, "Commissary supply and crew restock"): a crewed vehicle standing in the bay
    // of the commissary where it rents its space loads from the commissary's stock with the Restock button's code before
    // it sets off for a stop, the morning's first one after a night in the bay included. A crew with nothing to sell
    // restocks first if it can, then decides whether to set off. HireRunner.Step asks Due; the game side loads and calls
    // HireRunner.Restocked.
    public static class CrewRestock
    {
        // Once per leg of the plan: the leg it is setting off (or waiting) for, keyed by the leg's start minute. A
        // restock that loaded nothing isn't tried again until the next leg, however long the crew waits.
        public static bool Due(VehicleState vehicle, ShiftLeg want, VehicleWhere where)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (want == null) throw new ArgumentNullException(nameof(want));
            return where.CanRestock && vehicle.RestockedFor != want.Start;
        }

        // The foods the commissary was short of after a restock: each fitted piece with room left for at least one more
        // of its lightest food, while the commissary has none of the piece's foods left (stock it still has but the cargo
        // refused isn't a shortage). Its foods, in the piece's order; pieces in fitting order.
        public static IReadOnlyList<string> Short(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargoAfter,
            IReadOnlyDictionary<string, int> stockLeft)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargoAfter == null) throw new ArgumentNullException(nameof(cargoAfter));
            if (stockLeft == null) throw new ArgumentNullException(nameof(stockLeft));
            var view = Storage.Of(tables, vehicleType, cargoAfter);
            var missing = new List<string>();
            if (view == null) return missing;
            foreach (var pool in view.Pools)
            {
                var foods = pool.Piece.Products.Where(p => tables.Product(p) != null).ToList();
                if (foods.Count == 0) continue;
                var lightest = foods.Min(p => tables.Product(p)!.StockWeight);
                if (pool.Room + 1e-9 < lightest) continue;
                if (foods.Any(p => stockLeft.TryGetValue(p, out var n) && n > 0)) continue;
                missing.AddRange(foods.Where(p => !missing.Contains(p)));
            }
            return missing;
        }

        // Whether to text the shortage now: once per vehicle and game day, kept in the save.
        public static bool TellShort(VehicleState vehicle, int day)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.RestockShortDay == day) return false;
            vehicle.RestockShortDay = day;
            return true;
        }
    }
}
