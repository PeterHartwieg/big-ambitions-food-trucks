using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Fleet
{
    public sealed class RouteMap
    {
        private RouteMap(IReadOnlyList<(double U, double V)> stops, (double U, double V)? vehicle, bool loops,
            double centreX = 0, double centreZ = 0, double width = 0, double height = 0, (double U, double V)? home = null)
        {
            Home = home;
            Stops = stops;
            Vehicle = vehicle;
            Loops = loops;
            CentreX = centreX;
            CentreZ = centreZ;
            WidthMetres = width;
            HeightMetres = height;
        }

        public double CentreX { get; }
        public double CentreZ { get; }
        public double WidthMetres { get; }
        public double HeightMetres { get; }

        public (double U, double V) Place(double x, double z) =>
            WidthMetres <= 0 || HeightMetres <= 0 ? (0.5, 0.5) : (0.5 + (x - CentreX) / WidthMetres, 0.5 + (z - CentreZ) / HeightMetres);

        public bool Shows(double ax, double az, double bx, double bz) =>
            Math.Max(ax, bx) >= CentreX - WidthMetres / 2 && Math.Min(ax, bx) <= CentreX + WidthMetres / 2 &&
            Math.Max(az, bz) >= CentreZ - HeightMetres / 2 && Math.Min(az, bz) <= CentreZ + HeightMetres / 2;

        public IReadOnlyList<(double U, double V)> Stops { get; }
        public (double U, double V)? Vehicle { get; }
        public (double U, double V)? Home { get; }
        public bool Loops { get; }

        public static RouteMap Layout(IReadOnlyList<(double X, double Z)> stops, (double X, double Z)? vehicle, bool runsDaily,
            double aspect, double margin = 0.12, double minimumMetres = 250, (double X, double Z)? home = null)
        {
            if (stops == null) throw new ArgumentNullException(nameof(stops));
            if (stops.Count == 0) return new RouteMap(Array.Empty<(double, double)>(), null, false);
            if (double.IsNaN(aspect) || aspect <= 0) aspect = 1;
            margin = Math.Max(0, Math.Min(0.4, margin));
            var all = stops.ToList();
            if (vehicle != null) all.Add(vehicle.Value);
            if (home != null) all.Add(home.Value);
            var minX = all.Min(p => p.X);
            var maxX = all.Max(p => p.X);
            var minZ = all.Min(p => p.Z);
            var maxZ = all.Max(p => p.Z);
            var midX = (minX + maxX) / 2;
            var midZ = (minZ + maxZ) / 2;
            var usable = 1 - 2 * margin;
            var height = Math.Max(minimumMetres, Math.Max(maxZ - minZ, (maxX - minX) / aspect) / usable);
            var width = height * aspect;
            (double, double) Place((double X, double Z) p) => (0.5 + (p.X - midX) / width, 0.5 + (p.Z - midZ) / height);
            return new RouteMap(stops.Select(Place).ToList(), vehicle == null ? ((double, double)?)null : Place(vehicle.Value),
                home == null && runsDaily && stops.Count > 1, midX, midZ, width, height, home == null ? ((double, double)?)null : Place(home.Value));
        }
    }
}
