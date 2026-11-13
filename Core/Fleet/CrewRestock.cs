using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;

namespace FoodTrucks.Core.Fleet
{
    public static class CrewRestock
    {
        public static bool Due(VehicleState vehicle, ShiftLeg want, VehicleWhere where)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (want == null) throw new ArgumentNullException(nameof(want));
            if (!where.CanRestock) return false;
            var key = want.Kind == LegKind.Restock ? want.End : want.Start;
            var done = vehicle.RestockedFor == key ||
                       (want.Kind == LegKind.Restock && vehicle.RestockedFor >= want.Start && vehicle.RestockedFor <= want.End + (want.End - want.Start));
            return !done || (where.StockKey != null && where.StockKey != vehicle.RestockStockKey);
        }

        public static string StockKey(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo, IReadOnlyDictionary<string, int> stock)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (stock == null) throw new ArgumentNullException(nameof(stock));
            var view = Storage.Of(tables, vehicleType, cargo);
            var foods = view == null ? new List<string>() : view.Pools.SelectMany(p => p.Piece.Products).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            return string.Join(";", foods.Select(f => f + "=" + (stock.TryGetValue(f, out var n) ? n : 0)));
        }

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

        public static bool TellShort(VehicleState vehicle, int day)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.RestockShortDay == day) return false;
            vehicle.RestockShortDay = day;
            return true;
        }
    }
}
