using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.CrewDriving
{
    /// <summary>A short, fixed native chain prepared for ProbePath; never a router or corner repairer.</summary>
    public sealed class ProbeRoadPath
    {
        public const double CorridorMetres = .5;
        public const double MaxSampleSpacing = 1;
        public const double MinSampleSpacing = .1;
        public const double MaxNativeTurnRadians = .6;
        private const double FlatnessMetres = .002;
        private readonly ProbePoint[] points;
        private readonly int[] markers;
        private readonly double[] parameters;
        public ProbePath Path { get; }
        /// <summary>Detached copies. Path and the result cannot be mutated through these arrays.</summary>
        public ProbePoint[] Points => (ProbePoint[])points.Clone();
        public int[] SourcePointIndices => (int[])markers.Clone();
        /// <summary>For each sample: original segment index + cubic parameter in [0,1].
        /// Original marker i has parameter exactly i. Interpolate native Y between floor(p) and ceil(p);
        /// this is NOT distance along the prepared path. The final marker has parameter source.Length-1.</summary>
        public double[] SourceParameters => (double[])parameters.Clone();
        /// <summary>Certified upper bound over the continuous cubics, not just sampled headings.</summary>
        public double CurvatureBound { get; }

        private ProbeRoadPath(ProbePoint[] points, int[] markers, double[] parameters, ProbeGeometry geometry, double curvature)
        {
            this.points = points; this.markers = markers; this.parameters = parameters;
            Path = new ProbePath(points);
            Path.ValidateGeometry(geometry);
            CurvatureBound = curvature;
        }

        /// <summary>Interpolates every source marker exactly using chord-length cubic Hermite tangents.
        /// Refuses sharp native turns, ambiguous topology, excess corridor/steering, or exhausted bounds.
        /// All continuous cubics and returned chords stay inside the 0.5 m capsule of their OWN native
        /// segment and advance strictly along it. Clearance of the actual truck still requires native sweeps.
        /// Native signal planes must retain their original position/tangent separately from this curve.</summary>
        public static ProbeRoadPath Prepare(ProbePoint[] source, ProbeGeometry geometry)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (source == null || source.Length < 2 || source.Length > ProbePath.MaxPoints)
                throw new ArgumentException("Supply 2..2048 native directed points.", nameof(source));
            source = (ProbePoint[])source.Clone();
            var lengths = new double[source.Length - 1];
            var directions = new ProbePoint[lengths.Length];
            var total = 0.0;
            for (var i = 0; i < source.Length; i++)
            {
                if (!ProbeMath.In(source[i].X, -100000, 100000) || !ProbeMath.In(source[i].Z, -100000, 100000))
                    throw Bad("Nonfinite or excessive native coordinate.");
                if (i == 0) continue;
                var delta = Sub(source[i], source[i - 1]);
                var length = Norm(delta);
                if (length < MinSampleSpacing) throw Bad("Repeated or too-close native markers.");
                total += length;
                if (total > ProbePath.MaxLength) throw Bad("Native chain exceeds 1000 metres.");
                lengths[i - 1] = length; directions[i - 1] = Scale(delta, 1 / length);
                if (i > 1 && Math.Abs(Math.Atan2(Cross(directions[i - 2], directions[i - 1]),
                        Dot(directions[i - 2], directions[i - 1]))) > MaxNativeTurnRadians)
                    throw Bad("Sharp native corner; supply an already smooth road chain.");
            }
            for (var i = 0; i < source.Length - 1; i++)
                for (var j = i + 2; j < source.Length - 1; j++)
                    if (Intersects(source[i], source[i + 1], source[j], source[j + 1]))
                        throw Bad("Native chain crosses, repeats or retraces itself.");

            var tangents = new ProbePoint[source.Length];
            tangents[0] = directions[0]; tangents[tangents.Length - 1] = directions[directions.Length - 1];
            for (var i = 1; i < source.Length - 1; i++)
                tangents[i] = Unit(Add(Scale(directions[i - 1], lengths[i]), Scale(directions[i], lengths[i - 1])));
            if (lengths.Length > 1)
            {
                // One-sided derivative extrapolation avoids an artificial zero-curvature endpoint tangent.
                tangents[0] = Unit(Add(directions[0], Scale(Sub(directions[0], directions[1]),
                    lengths[0] / (lengths[0] + lengths[1]))));
                var last = lengths.Length - 1;
                tangents[tangents.Length - 1] = Unit(Add(directions[last], Scale(Sub(directions[last], directions[last - 1]),
                    lengths[last] / (lengths[last] + lengths[last - 1]))));
            }

            var output = new List<ProbePoint> { source[0] };
            var parameters = new List<double> { 0 };
            var markers = new int[source.Length];
            var budget = 131072; // bounded validation work, across the entire preparation
            var curvature = 0.0;
            for (var i = 0; i < lengths.Length; i++)
            {
                var curve = new Cubic(source[i], Add(source[i], Scale(tangents[i], lengths[i] / 3)),
                    Sub(source[i + 1], Scale(tangents[i + 1], lengths[i] / 3)), source[i + 1]);
                var bound = Certify(curve, source[i], source[i + 1], directions[i], geometry.MaxCurvature * .65, 0, ref budget);
                curvature = Math.Max(curvature, bound);
                // The derivative's quadratic control hull bounds speed everywhere. Refine for heading
                // as well as distance; do not leave a short fragment when an original marker is reached.
                var steps = (int)Math.Ceiling(curve.SpeedBound * Math.Max(1 / .95, bound / .08));
                steps = Math.Max(1, steps);
                if (steps > ProbePath.MaxPoints - output.Count) throw Bad("Prepared path exceeds 2048 samples.");
                for (var j = 1; j <= steps; j++)
                {
                    var t = (double)j / steps;
                    var point = j == steps ? source[i + 1] : curve.At(t);
                    var spacing = Norm(Sub(point, output[output.Count - 1]));
                    if (spacing < MinSampleSpacing || spacing > MaxSampleSpacing)
                        throw Bad("Cannot sample this curve with 0.1..1 metre spans.");
                    output.Add(point); parameters.Add(i + t);
                }
                markers[i + 1] = output.Count - 1;
            }
            return new ProbeRoadPath(output.ToArray(), markers, parameters.ToArray(), geometry, curvature);
        }

        private static double Certify(Cubic c, ProbePoint start, ProbePoint end, ProbePoint direction,
            double limit, int depth, ref int budget)
        {
            if (--budget < 0) throw Bad("Curve validation budget exhausted.");
            var v0 = Scale(Sub(c.B, c.A), 3); var v1 = Scale(Sub(c.C, c.B), 3); var v2 = Scale(Sub(c.D, c.C), 3);
            var w0 = Scale(Sub(v1, v0), 2); var w1 = Scale(Sub(v2, v1), 2);
            // v(t) is in the convex hull of v0..v2; w(t) is in the hull of w0,w1.
            // Positive directed projection lower-bounds |v| and proves no cusp/backtracking.
            var speed = Math.Min(Dot(v0, direction), Math.Min(Dot(v1, direction), Dot(v2, direction)));
            var numerator = Math.Max(Math.Max(Math.Abs(Cross(v0, w0)), Math.Abs(Cross(v0, w1))),
                Math.Max(Math.Max(Math.Abs(Cross(v1, w0)), Math.Abs(Cross(v1, w1))),
                    Math.Max(Math.Abs(Cross(v2, w0)), Math.Abs(Cross(v2, w1)))));
            var bound = speed > 0 ? numerator / (speed * speed * speed) : double.PositiveInfinity;
            // A capsule is convex. Its containing all four controls certifies the WHOLE curve,
            // including between samples; the same capsule also contains each returned chord.
            var corridor = DistanceToSegment(c.A, start, end) <= CorridorMetres &&
                DistanceToSegment(c.B, start, end) <= CorridorMetres &&
                DistanceToSegment(c.C, start, end) <= CorridorMetres &&
                DistanceToSegment(c.D, start, end) <= CorridorMetres;
            var flat = Math.Max(DistanceToSegment(c.B, c.A, c.D), DistanceToSegment(c.C, c.A, c.D));
            if (speed > 0 && bound <= limit && corridor && flat <= FlatnessMetres) return bound;
            if (depth == 14) throw Bad("Curve cannot be certified inside the directed corridor and steering envelope.");
            c.Split(out var left, out var right);
            return Math.Max(Certify(left, start, end, direction, limit, depth + 1, ref budget),
                Certify(right, start, end, direction, limit, depth + 1, ref budget));
        }

        private readonly struct Cubic
        {
            internal readonly ProbePoint A, B, C, D;
            internal Cubic(ProbePoint a, ProbePoint b, ProbePoint c, ProbePoint d) { A = a; B = b; C = c; D = d; }
            internal double SpeedBound => 3 * Math.Max(Norm(Sub(B, A)), Math.Max(Norm(Sub(C, B)), Norm(Sub(D, C))));
            internal ProbePoint At(double t)
            {
                var u = 1 - t;
                return Add(Add(Scale(A, u * u * u), Scale(B, 3 * u * u * t)),
                    Add(Scale(C, 3 * u * t * t), Scale(D, t * t * t)));
            }
            internal void Split(out Cubic left, out Cubic right)
            {
                var ab = Mid(A, B); var bc = Mid(B, C); var cd = Mid(C, D);
                var abc = Mid(ab, bc); var bcd = Mid(bc, cd); var mid = Mid(abc, bcd);
                left = new Cubic(A, ab, abc, mid); right = new Cubic(mid, bcd, cd, D);
            }
        }

        private static ArgumentException Bad(string message) => new ArgumentException(message, "source");
        private static ProbePoint Add(ProbePoint a, ProbePoint b) => new ProbePoint(a.X + b.X, a.Z + b.Z);
        private static ProbePoint Sub(ProbePoint a, ProbePoint b) => new ProbePoint(a.X - b.X, a.Z - b.Z);
        private static ProbePoint Scale(ProbePoint a, double s) => new ProbePoint(a.X * s, a.Z * s);
        private static ProbePoint Mid(ProbePoint a, ProbePoint b) => Add(a, Scale(Sub(b, a), .5));
        private static double Dot(ProbePoint a, ProbePoint b) => a.X * b.X + a.Z * b.Z;
        private static double Cross(ProbePoint a, ProbePoint b) => a.X * b.Z - a.Z * b.X;
        private static double Norm(ProbePoint p) => ProbeMath.Distance(p.X, p.Z);
        private static ProbePoint Unit(ProbePoint p) => Scale(p, 1 / Norm(p));
        private static double DistanceToSegment(ProbePoint p, ProbePoint a, ProbePoint b)
        {
            var d = Sub(b, a);
            var t = ProbeMath.Clamp(Dot(Sub(p, a), d) / Dot(d, d), 0, 1);
            return Norm(Sub(p, Add(a, Scale(d, t))));
        }
        private static bool Intersects(ProbePoint a, ProbePoint b, ProbePoint c, ProbePoint d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                Math.Max(a.Z, b.Z) < Math.Min(c.Z, d.Z) || Math.Max(c.Z, d.Z) < Math.Min(a.Z, b.Z)) return false;
            return Cross(Sub(b, a), Sub(c, a)) * Cross(Sub(b, a), Sub(d, a)) <= 0 &&
                Cross(Sub(d, c), Sub(a, c)) * Cross(Sub(d, c), Sub(b, c)) <= 0;
        }
    }
}
