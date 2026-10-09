using System;

namespace FoodTrucks.Core.Fleet
{
    // Where everyone at a truck's hatch stands, as offsets along the truck (metres from the hatch's middle,
    // positive towards the cab). Without the player the crew spread evenly about the middle. With the player
    // at the hatch, the player keeps the middle, so they don't shift when crew come and go, and the crew take
    // the places either side, nearest first: +1, -1, +2, -2 spacings.
    public static class WindowPlaces
    {
        public static (double? Player, double[] Crew) Offsets(int crew, bool player, double spacing)
        {
            if (crew < 0) throw new ArgumentOutOfRangeException(nameof(crew));
            if (double.IsNaN(spacing) || spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
            var places = new double[crew];
            if (!player)
            {
                for (var i = 0; i < crew; i++) places[i] = (i - (crew - 1) / 2.0) * spacing;
                return (null, places);
            }
            for (var i = 0; i < crew; i++)
            {
                var step = i / 2 + 1;
                places[i] = (i % 2 == 0 ? step : -step) * spacing;
            }
            return (0.0, places);
        }
    }
}
