using System;

namespace FoodTrucks.Core.Reputation
{
    public static class ReputationFactor
    {
        public static double Combine(double healthFactor, double regularsFactor, double footfallFactor) =>
            Usable(healthFactor) * Usable(regularsFactor) * Usable(footfallFactor);

        private static double Usable(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 1.0 : value;
    }
}
