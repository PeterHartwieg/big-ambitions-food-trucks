using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Fleet
{
    // A route's simplified map for the vehicle page: the stops' and the vehicle's world x, z placed on a panel as
    // fractions (u from the left, v from the bottom), north up as the game's city map opens (its camera stands south
    // of what it shows, so +z is up and +x right). One scale for both axes, so distances keep their proportions; the
    // points are centred and keep `margin` (a fraction of the panel) clear on every side.
    public sealed class RouteMap
    {
        private RouteMap(IReadOnlyList<(double U, double V)> stops, (double U, double V)? vehicle, bool loops)
        {
            Stops = stops;
            Vehicle = vehicle;
            Loops = loops;
        }

        public IReadOnlyList<(double U, double V)> Stops { get; }
        public (double U, double V)? Vehicle { get; }
        // The line runs from the last stop back to the first: the crew starts the next day from where it ended.
        public bool Loops { get; }

        // `aspect`: the panel's width over its height.
        public static RouteMap Layout(IReadOnlyList<(double X, double Z)> stops, (double X, double Z)? vehicle, bool runsDaily,
            double aspect, double margin = 0.12, double minimumMetres = 80)
        {
            if (stops == null) throw new ArgumentNullException(nameof(stops));
            if (stops.Count == 0) return new RouteMap(Array.Empty<(double, double)>(), null, false);
            if (double.IsNaN(aspect) || aspect <= 0) aspect = 1;
            margin = Math.Max(0, Math.Min(0.4, margin));
            var all = stops.ToList();
            if (vehicle != null) all.Add(vehicle.Value);
            var minX = all.Min(p => p.X);
            var maxX = all.Max(p => p.X);
            var minZ = all.Min(p => p.Z);
            var maxZ = all.Max(p => p.Z);
            var midX = (minX + maxX) / 2;
            var midZ = (minZ + maxZ) / 2;
            // Metres per panel height: what fits both ways, never less than minimumMetres (a one-stop route isn't a dot
            // in an endless zoom).
            var usable = 1 - 2 * margin;
            var height = Math.Max(minimumMetres, Math.Max(maxZ - minZ, (maxX - minX) / aspect) / usable);
            var width = height * aspect;
            (double, double) Place((double X, double Z) p) => (0.5 + (p.X - midX) / width, 0.5 + (p.Z - midZ) / height);
            return new RouteMap(stops.Select(Place).ToList(), vehicle == null ? ((double, double)?)null : Place(vehicle.Value),
                runsDaily && stops.Count > 1);
        }
    }
}
