using System;

namespace FoodTrucks.Core.Fleet
{
    public static class LaneReach
    {
        public const double HeightMetres = 3.0;

        public const double SlackMetres = 1.0;

        public static bool Counts(double dx, double dy, double dz, double radius) =>
            dx * dx + dy * dy + dz * dz < radius * radius && Math.Abs(dy) <= HeightMetres;

        public static double CoverRadius(double spaceMetres, double probeRadius) => spaceMetres + probeRadius + SlackMetres;
    }
}
