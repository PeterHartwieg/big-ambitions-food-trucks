using System;

namespace FoodTrucks.Core.Fleet
{
    // Where everyone at a truck's hatch stands, as offsets along the truck (metres from the hatch's middle,
    // positive towards the cab). Every place stays within halfWidth of the middle, so nobody stands beside the
    // opening behind the wall. Without the player the crew spread evenly about the middle. With the player at
    // the hatch, the player keeps the middle, so they don't shift when crew come and go, and the crew take the
    // places either side, nearest first: +1, -1, +2, -2 spacings. Crew who don't fit get null: they wait
    // inside the truck, away from the hatch.
    public static class WindowPlaces
    {
        public static (double? Player, double?[] Crew) Offsets(int crew, bool player, double spacing, double halfWidth)
        {
            if (crew < 0) throw new ArgumentOutOfRangeException(nameof(crew));
            if (double.IsNaN(spacing) || spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
            if (double.IsNaN(halfWidth) || halfWidth < 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
            var places = new double?[crew];
            // A small tolerance so a place exactly at the edge still counts as inside.
            var steps = (int)Math.Floor(halfWidth / spacing + 1e-9);
            if (!player)
            {
                // Evenly about the middle: n people need (n - 1) / 2 spacings each side.
                var fit = Math.Min(crew, (int)Math.Floor(2 * halfWidth / spacing + 1e-9) + 1);
                for (var i = 0; i < fit; i++) places[i] = (i - (fit - 1) / 2.0) * spacing;
                return (null, places);
            }
            var sideways = 2 * steps;
            for (var i = 0; i < crew && i < sideways; i++)
            {
                var step = i / 2 + 1;
                places[i] = (i % 2 == 0 ? step : -step) * spacing;
            }
            return (0.0, places);
        }
    }
}
