using System;

namespace FoodTrucks.Core.Fleet
{
    public static class WindowPlaces
    {
        public static (double? Player, double?[] Crew) Offsets(int crew, bool player, double spacing, double halfWidth)
        {
            if (crew < 0) throw new ArgumentOutOfRangeException(nameof(crew));
            if (double.IsNaN(spacing) || spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
            if (double.IsNaN(halfWidth) || halfWidth < 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
            var places = new double?[crew];
            var steps = (int)Math.Floor(halfWidth / spacing + 1e-9);
            if (!player)
            {
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
