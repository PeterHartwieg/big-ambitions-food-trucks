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

    // Whether a parked car stands in a bay's zone (BayParking). 0.9.9 asked only whether the car's middle was within 2 m
    // of the zone, so a long car parked at a stretch's end with its middle 2.5 m out still stood a metre into the paint
    // (live session 2, 13 October 2026). This takes the car's whole footprint, a rectangle turned with the car, against
    // each painted stretch (a rectangle in the zone's frame, grown by the margin), with a separating-axis test on both
    // rectangles' axes. A first version checked only the zone's axes, which is the car's bounding box: a car parked at
    // an angle near a stretch's corner counted although it stood clear (review round 1 of the mod round, Sol and Astra).
    public static class BayKeepOut
    {
        // spans: the zone's stretches as metres along from its foot; width: the paint's width, centred on the foot.
        // along, across: the car's middle in the zone's frame. forwardAlong, forwardAcross: the car's forward in that
        // frame (any length). halfLength, halfWidth: half the car's footprint. margin: added all round the zone.
        public static bool Overlaps(IEnumerable<(double From, double To)> spans, double width, double along, double across,
            double forwardAlong, double forwardAcross, double halfLength, double halfWidth, double margin)
        {
            if (spans == null) throw new ArgumentNullException(nameof(spans));
            var length = Math.Sqrt(forwardAlong * forwardAlong + forwardAcross * forwardAcross);
            var f = length < 1e-9 ? (A: 1.0, C: 0.0) : (A: forwardAlong / length, C: forwardAcross / length);
            var side = (A: -f.C, C: f.A);
            var halfAcross = width / 2 + margin;
            foreach (var (from, to) in spans)
            {
                var zone = (A: (from + to) / 2, C: 0.0);
                var halfAlong = (to - from) / 2 + margin;
                var d = (A: along - zone.A, C: across - zone.C);
                // The zone's axes: its half sizes against the car's reach along each.
                if (Math.Abs(d.A) >= halfAlong + Math.Abs(f.A) * halfLength + Math.Abs(side.A) * halfWidth) continue;
                if (Math.Abs(d.C) >= halfAcross + Math.Abs(f.C) * halfLength + Math.Abs(side.C) * halfWidth) continue;
                // The car's axes: its half sizes against the zone's reach along each.
                if (Math.Abs(d.A * f.A + d.C * f.C) >= halfLength + Math.Abs(f.A) * halfAlong + Math.Abs(f.C) * halfAcross) continue;
                if (Math.Abs(d.A * side.A + d.C * side.C) >= halfWidth + Math.Abs(side.A) * halfAlong + Math.Abs(side.C) * halfAcross) continue;
                return true;
            }
            return false;
        }
    }

    // Where the props by a commissary's door may stand (BayProps). Facades have pilasters, sills and recessed doors
    // (Hell's Kitchen, live session 3), so the wall a prop stands against is read where the prop goes, not at the door.
    public static class PropPlacement
    {
        // The facade line where a prop goes: the outermost wall hit (metres out from the door's own facade line, + to
        // the street) within maxShift either way. A hit farther out is something standing on the pavement (a hydrant,
        // a post), not the wall: the prop's overlap check refuses that place instead. Null without a usable hit.
        public static double? FacadeOffset(IEnumerable<double> hits, double maxShift)
        {
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            double? best = null;
            foreach (var h in hits)
                if (Math.Abs(h) <= maxShift && (best == null || h > best.Value)) best = h;
            return best;
        }

        // The middles to try for a prop of halfLength along one side of the door: from `start` (where the free facade
        // begins, metres from the door's middle) outwards in steps, while the prop ends within maxAlong of the door.
        public static IEnumerable<double> Offsets(double start, double halfLength, double maxAlong, double step)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            for (var s = start + halfLength; s + halfLength <= maxAlong + 1e-9; s += step) yield return s;
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
