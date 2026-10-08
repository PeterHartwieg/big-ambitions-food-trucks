using System;

namespace FoodTrucks.Core.Reputation
{
    // The single reputation factor DemandQuery takes: the grade on the window, the regulars at this spot
    // on this weekday, and the extra footfall the player's other vehicles nearby bring. Competition's
    // per-category split goes into DemandQuery.CategoryShares instead.
    public static class ReputationFactor
    {
        public static double Combine(double healthFactor, double regularsFactor, double footfallFactor) =>
            Usable(healthFactor) * Usable(regularsFactor) * Usable(footfallFactor);

        private static double Usable(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 1.0 : value;
    }
}
