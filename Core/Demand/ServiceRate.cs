using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    // How fast the window serves. An order takes the longest service time of its items, so a burger
    // slows the whole queue (design: Equipment and menu).
    public static class ServiceRate
    {
        // Average minutes per order: each category's slowest item on the menu, weighted by that
        // category's share of the customers (equal weights when the shares are empty). Drink-only
        // customers count at the drinks' own time; add-on drinks ride with a slower main item and add
        // nothing. 0 when nothing on the menu is a product.
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

        // Customers the window can serve per hour.
        public static double CustomersPerHour(int staff, double orderMinutes)
        {
            if (staff <= 0 || double.IsNaN(orderMinutes) || double.IsInfinity(orderMinutes) || orderMinutes <= 0) return 0;
            return staff * 60.0 / orderMinutes;
        }
    }
}
