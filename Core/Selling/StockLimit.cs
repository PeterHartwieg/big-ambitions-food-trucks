using System;

namespace FoodTrucks.Core.Selling
{
    public static class StockLimit
    {
        public static int CanLoad(double loadInVehicle, int unitsInBox, int capacity, double stockWeight)
        {
            if (double.IsNaN(loadInVehicle) || loadInVehicle < 0)
                throw new ArgumentOutOfRangeException(nameof(loadInVehicle));
            if (double.IsNaN(stockWeight) || double.IsInfinity(stockWeight) || stockWeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(stockWeight));
            if (unitsInBox <= 0 || capacity <= 0 || loadInVehicle >= capacity) return 0;
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

        public static int CanLoad(int unitsInVehicle, int unitsInBox, int capacity)
        {
            if (unitsInBox <= 0 || capacity <= 0) return 0;
            var room = capacity - Math.Max(0, unitsInVehicle);
            return Math.Max(0, Math.Min(unitsInBox, room));
        }
    }
}
