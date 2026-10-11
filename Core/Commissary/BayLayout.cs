using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.Commissary
{
    // Where the painted bay goes on the parking lane by a commissary's door (the game side is BayMarkers). Positions
    // along the kerb are metres from the door's foot on the parking lane's middle line, measured along PaintAxis.
    public static class BayLayout
    {
        public const double StepMetres = 0.5;
        // A stretch of parking lane shorter than this isn't painted.
        public const double MinStretchMetres = 2.0;

        // The paint's +x axis (x, z) for a marker that faces away from the door: the marker's root is turned with
        // Unity's LookRotation(-across, up), whose right axis is cross(up, forward) = (forward.z, 0, -forward.x). The
        // kerb is probed along this same axis, so a stretch measured on one side of the door is painted on that side.
        public static (double X, double Z) PaintAxis(double acrossX, double acrossZ)
        {
            var length = Math.Sqrt(acrossX * acrossX + acrossZ * acrossZ);
            if (length < 1e-9) throw new ArgumentException("The direction across the street has no length.");
            return (-acrossZ / length, acrossX / length);
        }

        // The stretches of legal parking to paint within `reach` either way of the bay point. Without a stretch of at
        // least MinStretchMetres within reach, nothing is painted: paint is never drawn where the game has no legal
        // parking (0.9.8 drew an assumed lane on a no-parking kerb; Peter, 11 October 2026: "The purple area is not on
        // a parking spot").
        public static BayStretches Stretches(double reach, Func<double, bool> inParkingLane)
        {
            var spans = new List<(double From, double To)>();
            double? start = null;
            var steps = (int)Math.Floor(2 * reach / StepMetres + 1e-6);
            for (var i = 0; i <= steps; i++)
            {
                var x = -reach + i * StepMetres;
                var inLane = inParkingLane(x);
                var last = i == steps;
                if (inLane && start == null) start = x;
                if ((!inLane || last) && start != null)
                {
                    var end = inLane ? Math.Min(x, reach) : x - StepMetres;
                    if (end - start.Value >= MinStretchMetres) spans.Add((start.Value, end));
                    start = null;
                }
            }
            return new BayStretches(spans.Count > 0 ? BayStretchSource.Probed : BayStretchSource.NoneUsable, spans);
        }

        // Where to cut a painted edge from `from` to `to` so the paint follows the road: at each whole metre between
        // them, and at both ends. The road's height is sampled at whole metres.
        public static IReadOnlyList<double> Cuts(double from, double to)
        {
            var cuts = new List<double> { from };
            for (var m = Math.Floor(from) + 1; m < to - 1e-6; m++)
                if (m > from + 1e-6) cuts.Add(m);
            if (to > from) cuts.Add(to);
            return cuts;
        }
    }

    public enum BayStretchSource
    {
        // From the game's parking colliders.
        Probed,
        // Parking found, but no stretch long enough within reach: nothing painted.
        NoneUsable,
    }

    public sealed class BayStretches
    {
        public BayStretches(BayStretchSource source, IReadOnlyList<(double From, double To)> spans)
        {
            Source = source;
            Spans = spans;
        }

        public BayStretchSource Source { get; }
        public IReadOnlyList<(double From, double To)> Spans { get; }
    }
}
