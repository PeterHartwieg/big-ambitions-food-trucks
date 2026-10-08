using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public static class CustomerPick
    {
        // Choose a stocked category by demand, then its most plentiful item. Fixed ordinal order
        // makes the same roll choose the same item regardless of dictionary insertion order.
        public static string? MainItem(DemandTables tables, IReadOnlyDictionary<string, double> categoryShares,
            IReadOnlyDictionary<string, int> stockByItem, double roll)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (categoryShares == null) throw new ArgumentNullException(nameof(categoryShares));
            if (stockByItem == null) throw new ArgumentNullException(nameof(stockByItem));
            if (double.IsNaN(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(roll), "Roll must be in [0, 1).");

            var groups = StockedItems(tables, stockByItem)
                .GroupBy(item => tables.Product(item.Key)!.Category)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new
                {
                    Item = group.First().Key,
                    Units = group.Sum(item => (long)item.Value),
                    Share = categoryShares.TryGetValue(group.Key, out var share) && IsPositiveFinite(share) ? share : 0
                }).ToList();
            if (groups.Count == 0) return null;
            var largestShare = groups.Max(group => group.Share);
            // With no demand, keep selling from the category with the most units. Category ties
            // keep ordinal category order, and item ties keep ordinal item order.
            if (largestShare == 0) return groups.OrderByDescending(group => group.Units).First().Item;

            // Scale before summing so even very large finite shares cannot overflow the total.
            var target = roll * groups.Sum(group => group.Share / largestShare);
            string? last = null;
            foreach (var group in groups)
            {
                if (group.Share <= 0) continue;
                last = group.Item;
                var weight = group.Share / largestShare;
                if (target < weight) return group.Item;
                target -= weight;
            }
            return last;
        }

        // demand.json marks add-on categories with addOnFactor; the shipped one is cold drinks.
        public static string? AddOnItem(DemandTables tables, IReadOnlyDictionary<string, int> stockByItem)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (stockByItem == null) throw new ArgumentNullException(nameof(stockByItem));
            foreach (var item in StockedItems(tables, stockByItem))
                if (tables.Category(tables.Product(item.Key)!.Category).AddOnFactor > 0) return item.Key;
            return null;
        }

        // Carry a fractional drink count between customers, using SalesTick's progress rules.
        public static (int AddOns, double Progress) AdvanceAddOns(double progress, double addOnsPerCustomer)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress) || progress < 0 || progress >= 1) progress = 0;
            if (!IsPositiveFinite(addOnsPerCustomer)) return (0, progress);
            var total = progress + addOnsPerCustomer;
            var whole = Math.Floor(total);
            if (whole >= int.MaxValue) return (int.MaxValue, 0);
            return ((int)whole, total - whole);
        }

        private static IEnumerable<KeyValuePair<string, int>> StockedItems(DemandTables tables,
            IReadOnlyDictionary<string, int> stockByItem) =>
            stockByItem.Where(item => item.Value > 0 && tables.Product(item.Key) != null)
                .OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal);

        private static bool IsPositiveFinite(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
