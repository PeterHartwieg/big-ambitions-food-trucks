using System;

namespace FoodTrucks.Core.Fleet
{
    // Selection limits for a single working spell. Selling still uses SalesSimulator, minute by minute.
    public static class ServingTime
    {
        public const int MinimumMinutes = 5;
        public const int MaximumMinutes = 12 * 60;
        public const int DefaultMinutes = 60;

        public static int Selection(int minutes) => Math.Max(MinimumMinutes, Math.Min(MaximumMinutes, minutes));

        // A work skip must let selling inspect every minute before the world can advance again. This bounds
        // sell-out/closure overshoot to the simulator's own one-minute step, regardless of the native skip curve.
        public static double Advance(double now, double requested)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || double.IsNaN(requested) || double.IsInfinity(requested) || requested < 0)
                throw new ArgumentOutOfRangeException();
            return Math.Min(requested, Math.Floor(now) + 1 - now);
        }
    }
}
