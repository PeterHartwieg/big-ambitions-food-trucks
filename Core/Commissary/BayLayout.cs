using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Commissary
{
    public static class BayLayout
    {
        public const double StepMetres = 0.5;
        public const double MinStretchMetres = 2.0;

        public static (double X, double Z) PaintAxis(double acrossX, double acrossZ)
        {
            var length = Math.Sqrt(acrossX * acrossX + acrossZ * acrossZ);
            if (length < 1e-9) throw new ArgumentException("The direction across the street has no length.");
            return (-acrossZ / length, acrossX / length);
        }

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

        public static IReadOnlyList<double> Cuts(double from, double to)
        {
            var cuts = new List<double> { from };
            for (var m = Math.Floor(from) + 1; m < to - 1e-6; m++)
                if (m > from + 1e-6) cuts.Add(m);
            if (to > from) cuts.Add(to);
            return cuts;
        }
    }

    public static class BayKeepOut
    {
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
                if (Math.Abs(d.A) >= halfAlong + Math.Abs(f.A) * halfLength + Math.Abs(side.A) * halfWidth) continue;
                if (Math.Abs(d.C) >= halfAcross + Math.Abs(f.C) * halfLength + Math.Abs(side.C) * halfWidth) continue;
                if (Math.Abs(d.A * f.A + d.C * f.C) >= halfLength + Math.Abs(f.A) * halfAlong + Math.Abs(f.C) * halfAcross) continue;
                if (Math.Abs(d.A * side.A + d.C * side.C) >= halfWidth + Math.Abs(side.A) * halfAlong + Math.Abs(side.C) * halfAcross) continue;
                return true;
            }
            return false;
        }

        public static List<TCar> CarsInZones<TZone, TCar>(IEnumerable<TZone> zones, Func<TZone, IEnumerable<TCar>> near, Func<TZone, TCar, bool> inZone)
            where TCar : class
        {
            if (zones == null) throw new ArgumentNullException(nameof(zones));
            if (near == null) throw new ArgumentNullException(nameof(near));
            if (inZone == null) throw new ArgumentNullException(nameof(inZone));
            var taken = new HashSet<TCar>();
            var result = new List<TCar>();
            foreach (var zone in zones)
                foreach (var car in near(zone).ToList())
                    if (car != null && !taken.Contains(car) && inZone(zone, car))
                    {
                        taken.Add(car);
                        result.Add(car);
                    }
            return result;
        }
    }

    public static class PropPlacement
    {
        public static double? FacadeOffset(IEnumerable<double> hits, double maxShift)
        {
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            double? best = null;
            foreach (var h in hits)
                if (Math.Abs(h) <= maxShift && (best == null || h > best.Value)) best = h;
            return best;
        }

        public static IEnumerable<double> Offsets(double start, double halfLength, double maxAlong, double step)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            for (var s = start + halfLength; s + halfLength <= maxAlong + 1e-9; s += step) yield return s;
        }
    }

    public enum BayStretchSource
    {
        Probed,
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
