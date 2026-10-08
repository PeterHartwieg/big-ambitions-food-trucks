using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.Selling
{
    // Milestone 1 menu: base-game food a cart may sell, whatever equipment it has.
    // Milestone 2 replaces this with the menu that follows the fitted equipment.
    public static class CartMenu
    {
        public static readonly IReadOnlyList<string> Items = new[]
        {
            "ba:itemname_hotdog",
            "ba:itemname_burger",
            "ba:itemname_frenchfries",
            "ba:itemname_pizza",
            "ba:itemname_cupofcoffee",
            "ba:itemname_croissant",
            "ba:itemname_donut",
            "ba:itemname_cupcake",
            "ba:itemname_icecream",
            "ba:itemname_sodacan",
        };

        public static bool IsOnMenu(string? itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            foreach (var item in Items)
            {
                if (string.Equals(item, itemName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // The item the next customer buys: the menu item with the most units in stock,
        // ties broken by menu order. Null when nothing on the menu is in stock.
        public static string? PickNext(IReadOnlyDictionary<string, int> unitsByItem)
        {
            if (unitsByItem == null) throw new ArgumentNullException(nameof(unitsByItem));
            string? best = null;
            var bestUnits = 0;
            foreach (var item in Items)
            {
                if (unitsByItem.TryGetValue(item, out var units) && units > bestUnits)
                {
                    best = item;
                    bestUnits = units;
                }
            }
            return best;
        }
    }
}
