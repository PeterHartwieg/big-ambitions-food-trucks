using System;

namespace FoodTrucks.Core.Fleet
{
    // Which of the traffic system's waypoints a hatch probe counts, and how far one read must reach to serve many
    // probes. The game's search (DensityManager.GetClosestWaypoint) takes waypoints whose squared 3D distance is
    // under the radius squared; the probe then keeps those within HeightMetres of its own height, so lanes on a
    // bridge over the street, or on the street under one, don't count.
    public static class LaneReach
    {
        public const double HeightMetres = 3.0;

        // Slack for float rounding at the read's edge.
        public const double SlackMetres = 1.0;

        public static bool Counts(double dx, double dy, double dz, double radius) =>
            dx * dx + dy * dy + dz * dz < radius * radius && Math.Abs(dy) <= HeightMetres;

        // The radius of one read, at any height, that holds every waypoint a probe of `probeRadius` counts anywhere
        // within `spaceMetres` (3D) of the read's centre: by the triangle inequality, spaceMetres + probeRadius.
        public static double CoverRadius(double spaceMetres, double probeRadius) => spaceMetres + probeRadius + SlackMetres;
    }
}
