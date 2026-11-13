using System.Collections.Generic;

namespace FoodTrucks.Core.Selling
{
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

        public static string? PickNext(IReadOnlyDictionary<string, int> unitsByItem) => menu.PickNext(unitsByItem);
    }
}
