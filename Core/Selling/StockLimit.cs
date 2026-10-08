using System;

namespace FoodTrucks.Core.Selling
{
    // A vehicle's stock limit in units, enforced when loading. The game counts cargo in boxes, and one
    // wholesale box of hot dogs holds 500 units, so the game's slots alone would never let a cart sell out.
    public static class StockLimit
    {
        // Whole items that fit in the remaining weighted load. No tolerance may overfill a vehicle.
        public static int CanLoad(double loadInVehicle, int unitsInBox, int capacity, double stockWeight)
        {
            if (double.IsNaN(loadInVehicle) || loadInVehicle < 0)
                throw new ArgumentOutOfRangeException(nameof(loadInVehicle));
            if (double.IsNaN(stockWeight) || double.IsInfinity(stockWeight) || stockWeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(stockWeight));
            if (unitsInBox <= 0 || capacity <= 0 || loadInVehicle >= capacity) return 0;
            // Search using the resulting load itself: division can round either side of a whole item.
            var lower = 0;
            var upper = unitsInBox;
            while (lower < upper)
            {
                var middle = lower + (int)(((long)upper - lower + 1) / 2);
                if (loadInVehicle + middle * stockWeight <= capacity) lower = middle;
                else upper = middle - 1;
            }
            return lower;
        }

        // Units of a box that fit, from 0 to the box's units. unitsInVehicle is the total across all items.
        public static int CanLoad(int unitsInVehicle, int unitsInBox, int capacity)
        {
            if (unitsInBox <= 0 || capacity <= 0) return 0;
            var room = capacity - Math.Max(0, unitsInVehicle);
            return Math.Max(0, Math.Min(unitsInBox, room));
        }
    }
}
