using System;

namespace FoodTrucks.Core.Selling
{
    public static class SalesClock
    {
        public static double Start(double minute)
        {
            if (double.IsNaN(minute) || double.IsInfinity(minute)) throw new ArgumentOutOfRangeException(nameof(minute));
            return Math.Floor(minute);
        }

        public static double? NextStep(double from, double until, double stepMinutes = 1)
        {
            if (until - from < 1) return null;
            var nextHour = (Math.Floor(from / 60) + 1) * 60;
            return Math.Min(until, Math.Max(from + 1, Math.Min(from + stepMinutes, nextHour)));
        }

        public static int Elapsed(double from, double to) => (int)Math.Floor(to - from);
    }
}
