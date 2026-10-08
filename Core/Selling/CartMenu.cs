using System.Collections.Generic;

namespace FoodTrucks.Core.Selling
{
    // Milestone 1 menu: base-game food a cart may sell, whatever equipment it has.
    // Menu.FromEquipment replaces it once the game side fits equipment to vehicles.
    public static class CartMenu
    {
        private static readonly Menu menu = new Menu(new[]
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
        });

        public static IReadOnlyList<string> Items => menu.Items;

        public static bool IsOnMenu(string? itemName) => menu.IsOnMenu(itemName);

        // The item the next customer buys: the menu item with the most units in stock,
        // ties broken by menu order. Null when nothing on the menu is in stock.
        public static string? PickNext(IReadOnlyDictionary<string, int> unitsByItem) => menu.PickNext(unitsByItem);
    }
}
