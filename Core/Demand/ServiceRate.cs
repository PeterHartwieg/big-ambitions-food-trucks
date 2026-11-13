using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    public static class ServiceRate
    {
        public static double OrderMinutes(DemandTables tables, IEnumerable<string> menu, IReadOnlyDictionary<string, double>? customerShares)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            var slowest = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var item in menu)
            {
                var product = tables.Product(item);
                if (product == null) continue;
                slowest.TryGetValue(product.Category, out var minutes);
                slowest[product.Category] = Math.Max(minutes, product.ServiceMinutes);
            }
            if (slowest.Count == 0) return 0;

            var weightTotal = 0.0;
            var weighted = 0.0;
            foreach (var pair in slowest.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var weight = customerShares == null || customerShares.Count == 0
                    ? 1.0
                    : customerShares.TryGetValue(pair.Key, out var share) ? share : 0;
                weighted += weight * pair.Value;
                weightTotal += weight;
            }
            return weightTotal > 0 ? weighted / weightTotal : slowest.Values.Average();
        }

        public static double CustomersPerHour(int staff, double orderMinutes)
        {
            if (staff <= 0 || double.IsNaN(orderMinutes) || double.IsInfinity(orderMinutes) || orderMinutes <= 0) return 0;
            return staff * 60.0 / orderMinutes;
        }
    }
}
