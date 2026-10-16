using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;

namespace FoodTrucks.Core.Fleet
{
    // Crews restock at the commissary (plan, "Commissary supply and crew restock"): a crewed vehicle standing in the bay
    // of the commissary where it rents its space loads food from the commissary's stock with Fill up's code (the
    // panel's Commissary stock) before it sets off for a stop, the morning's first one after a night in the bay included. A crew with nothing to sell
    // restocks first if it can, then decides whether to set off. HireRunner.Step asks Due; the game side loads and calls
    // HireRunner.Restocked.
    public static class CrewRestock
    {
        // Once per leg of the plan (the leg it is setting off or waiting for, keyed by its start minute), and again in
        // the same leg only when the commissary's stock of the vehicle's foods has changed since the last attempt (a
        // delivery at 08:00 after a check at 07:40 that found it empty; review round 2, Astra). The key is read after the
        // last attempt's loading, so one change brings at most one more attempt: never a loop.
        public static bool Due(VehicleState vehicle, ShiftLeg want, VehicleWhere where)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (want == null) throw new ArgumentNullException(nameof(want));
            if (!where.CanRestock) return false;
            // The morning restock is keyed by its end, the start of the drive it belongs to (HireRunner.Restocked).
            var key = want.Kind == LegKind.Restock ? want.End : want.Start;
            // A restock leg planned again (a shift edit) with other times: a restock already made within it counts.
            var done = vehicle.RestockedFor == key ||
                       (want.Kind == LegKind.Restock && vehicle.RestockedFor >= want.Start && vehicle.RestockedFor <= want.End + (want.End - want.Start));
            return !done || (where.StockKey != null && where.StockKey != vehicle.RestockStockKey);
        }

        // What the commissary holds of the foods the vehicle's fitted pieces serve, as one string ("item=units;…", by
        // item): equal keys mean nothing there changed for this vehicle.
        public static string StockKey(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo, IReadOnlyDictionary<string, int> stock)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            var view = Storage.Of(tables, vehicleType, cargo);
            var foods = view == null ? new List<string>() : view.Pools.SelectMany(p => p.Piece.Products).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            return string.Join(";", foods.Select(f => f + "=" + (stock.TryGetValue(f, out var n) ? n : 0)));
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
