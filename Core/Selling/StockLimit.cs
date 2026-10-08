using System;

namespace FoodTrucks.Core.Selling
{
    // A vehicle's stock limit in units, enforced when loading. The game counts cargo in boxes, and one
    // wholesale box of hot dogs holds 500 units, so the game's slots alone would never let a cart sell out.
    public static class StockLimit
    {
        // Units of a box that fit, from 0 to the box's units. unitsInVehicle is the total across all items.
        public static int CanLoad(int unitsInVehicle, int unitsInBox, int capacity)
        {
            if (unitsInBox <= 0 || capacity <= 0) return 0;
            var room = capacity - Math.Max(0, unitsInVehicle);
            return Math.Max(0, Math.Min(unitsInBox, room));
        }
    }
}
