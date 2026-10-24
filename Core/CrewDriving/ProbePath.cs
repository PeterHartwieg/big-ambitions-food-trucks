using System;

namespace FoodTrucks.Core.CrewDriving
{
    public readonly struct ProbeProjection
    {
        public double Along { get; }
        public double Distance { get; }
        public double Lateral { get; }
        public double Heading { get; }
        public ProbePoint Point { get; }
        internal ProbeProjection(double along, double distance, double lateral, double heading, ProbePoint point)
        { Along = along; Distance = distance; Lateral = lateral; Heading = heading; Point = point; }
    }

    /// <summary>Copied, immutable directed rear-axle polyline; never rounds or plans corners.</summary>
    public sealed class ProbePath
    {
        public const int MaxPoints = 2048;
        public const double MaxLength = 1000;
        public const double MaxProjectionWindow = 100;
        private readonly ProbePoint[] points;
        private readonly double[] along;
        public int Count => points.Length;
        public double Length => along[along.Length - 1];
        public double MaxCurvature { get; }
        /// <summary>Bound on the circular-arc/chord error assumed by the sampled-curve contract.</summary>
        public double ChordErrorBound { get; }

        public ProbePath(ProbePoint[] points)
        {
            if (points == null || points.Length < 2 || points.Length > MaxPoints)
                throw new ArgumentException("Supply 2..2048 directed points.", nameof(points));
            this.points = (ProbePoint[])points.Clone();
            along = new double[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                if (!ProbeMath.In(points[i].X, -100000, 100000) || !ProbeMath.In(points[i].Z, -100000, 100000))
                    throw new ArgumentException("Nonfinite or excessive path coordinate.", nameof(points));
                if (i == 0) continue;
                var span = ProbeMath.Distance(points[i].X - points[i - 1].X, points[i].Z - points[i - 1].Z);
                if (span < .1 || span > 100) throw new ArgumentException("Segments must be 0.1..100 metres.", nameof(points));
                along[i] = along[i - 1] + span;
                if (along[i] > MaxLength) throw new ArgumentException("Probe path exceeds 1000 metres.", nameof(points));
                if (i < 2) continue;
                var previous = along[i - 1] - along[i - 2];
                var turn = Math.Abs(ProbeMath.Wrap(Heading(i - 1) - Heading(i - 2)));
                if (turn < 1e-8) continue;
                // Only finely sampled curves, never an arbitrary street-corner fillet. A real polyline kink
                // has infinite curvature: acceptance assumes these are chords of a prevalidated smooth curve.
                if (turn > .12 || span > 2 || previous > 2)
                    throw new ArgumentException("Resupply the turn as a validated finely sampled curve.", nameof(points));
                var curvature = turn / ((span + previous) * .5);
                MaxCurvature = Math.Max(MaxCurvature, curvature);
                ChordErrorBound = Math.Max(ChordErrorBound, curvature * Math.Max(span * span, previous * previous) / 8);
                if (ChordErrorBound > .04) throw new ArgumentException("Curve chord envelope exceeds 4 cm.", nameof(points));
            }
            // Refuse crossings and overlapping/retraced segments, including a closed loop. Local projection
            // must not silently select a different leg at a crossing. Startup work only, bounded by MaxPoints.
            for (var i = 0; i < points.Length - 1; i++)
                for (var j = i + 2; j < points.Length - 1; j++)
                    if (Intersects(points[i], points[i + 1], points[j], points[j + 1]))
                        throw new ArgumentException("Probe path crosses or retraces itself.", nameof(points));
        }

        public void ValidateGeometry(ProbeGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (Length < geometry.Front + ProbeController.StopMargin + 2 || MaxCurvature > geometry.MaxCurvature * .65)
                throw new ArgumentException("Path is too short or turns beyond the truck's conservative steering envelope.");
        }

        /// <summary>Conservative tube radius around the supplied path containing every accepted body pose.
        /// The adapter must validate this tube is clear, or validate a tighter measured swept body envelope.</summary>
        public double RequiredSweptRadius(ProbeGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            var extent = Math.Max(geometry.Front, geometry.Rear);
            return ProbeMath.Distance(extent, geometry.Width / 2) + ProbeController.CorridorTolerance + ChordErrorBound;
        }

        /// <summary>Clamped rear-axle path coordinate in metres; no extension or route advancement.</summary>
        public ProbePoint Sample(double metres)
        {
            if (!ProbeMath.Finite(metres)) throw new ArgumentOutOfRangeException(nameof(metres));
            var s = ProbeMath.Clamp(metres, 0, Length);
            var i = Segment(s);
            return At(i, s);
        }

        public double HeadingAt(double metres)
        {
            if (!ProbeMath.Finite(metres)) throw new ArgumentOutOfRangeException(nameof(metres));
            return Heading(Segment(ProbeMath.Clamp(metres, 0, Length)));
        }

        /// <summary>Nearest point strictly INSIDE [from,to], at most 100 m of path. No allocations.
        /// Distance includes longitudinal error at the window ends, so an out-of-window pose cannot look on-path.</summary>
        public bool TryProject(double x, double z, double from, double to, out ProbeProjection projection)
        {
            projection = default;
            if (!ProbeMath.In(x, -100000, 100000) || !ProbeMath.In(z, -100000, 100000) ||
                !ProbeMath.In(from, 0, Length) || !ProbeMath.In(to, from, Length) || to - from > MaxProjectionWindow)
                return false;
            var best = double.MaxValue;
            for (var i = Segment(from); i < points.Length - 1 && along[i] <= to; i++)
            {
                if (along[i + 1] < from) continue;
                var a = points[i]; var b = points[i + 1];
                var dx = b.X - a.X; var dz = b.Z - a.Z;
                var length = along[i + 1] - along[i];
                var s = ProbeMath.Clamp(along[i] + ((x - a.X) * dx + (z - a.Z) * dz) / length,
                    Math.Max(from, along[i]), Math.Min(to, along[i + 1]));
                var p = At(i, s);
                var distance2 = (x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z);
                if (distance2 >= best) continue;
                best = distance2;
                projection = new ProbeProjection(s, Math.Sqrt(distance2),
                    ((x - p.X) * dz - (z - p.Z) * dx) / length, Heading(i), p);
            }
            return best < double.MaxValue;
        }

        private ProbePoint At(int segment, double s)
        {
            var a = points[segment]; var b = points[segment + 1];
            var f = (s - along[segment]) / (along[segment + 1] - along[segment]);
            return new ProbePoint(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f);
        }

        private int Segment(double s)
        {
            var lo = 0; var hi = along.Length - 1;
            while (lo + 1 < hi)
            { var mid = (lo + hi) / 2; if (along[mid] <= s) lo = mid; else hi = mid; }
            return lo;
        }
        private double Heading(int i) => Math.Atan2(points[i + 1].X - points[i].X, points[i + 1].Z - points[i].Z);
        private static double Cross(ProbePoint a, ProbePoint b, ProbePoint p) =>
            (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X);
        private static bool Intersects(ProbePoint a, ProbePoint b, ProbePoint c, ProbePoint d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                Math.Max(a.Z, b.Z) < Math.Min(c.Z, d.Z) || Math.Max(c.Z, d.Z) < Math.Min(a.Z, b.Z)) return false;
            return Cross(a, b, c) * Cross(a, b, d) <= 0 && Cross(c, d, a) * Cross(c, d, b) <= 0;
        }
    }
}
