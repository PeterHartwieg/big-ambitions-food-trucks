using System;

namespace FoodTrucks.Core.Selling
{
    // The demand-driven rate for SalesTick.Advance, replacing CartTuning.SalesPerHour once the game
    // side feeds the demand model. Until queues exist (Milestone 3), customers the window can't
    // serve are simply lost.
    public static class SalesRate
    {
        public static double PerHour(double customersPerHour, double conversion, double serviceCapacityPerHour)
        {
            if (!IsUsable(customersPerHour) || !IsUsable(conversion) || !IsUsable(serviceCapacityPerHour)) return 0;
            return Math.Min(customersPerHour * conversion, serviceCapacityPerHour);
        }

        private static bool IsUsable(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
