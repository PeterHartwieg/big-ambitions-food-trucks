using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Fleet
{
    public enum Ground
    {
        Unknown,
        Pavement,
        Road
    }

    public enum HatchVerdict
    {
        Pavement,
        WrongWay,
        NoPavement,
        Unclear
    }

    public sealed class SideReading
    {
        public SideReading(double? pavement, double? road)
        {
            Pavement = pavement;
            Road = road;
        }

        public double? Pavement { get; }
        public double? Road { get; }

        public bool Clear => Pavement != null && (Road == null || Pavement < Road);

        public bool RoadFirst => Road != null && (Pavement == null || Road <= Pavement);

        public override string ToString() =>
            $"pavement {(Pavement is { } p ? $"{p:F1} m" : "none")}, road {(Road is { } r ? $"{r:F1} m" : "none")}";
    }

    public static class HatchSide
    {
        public static readonly double[] Offsets = { 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 };

        public static readonly double[] Along = { -1.9, -1.1, -0.3 };

        public const double LaneHalfWidthMetres = 1.75;

        public const double RoadsideMetres = 3.25;

        public static Ground Classify(double? laneMetres, bool roadLayer, bool parkingLane, bool crosswalk, bool walkable)
        {
            if (roadLayer || (laneMetres is { } lane && lane <= LaneHalfWidthMetres)) return Ground.Road;
            if (parkingLane || crosswalk || !walkable) return Ground.Unknown;
            if (laneMetres is { } near && near <= RoadsideMetres) return Ground.Unknown;
            return Ground.Pavement;
        }

        public static SideReading Read(IEnumerable<(double Offset, IReadOnlyList<Ground> Points)> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            double? pavement = null, road = null;
            foreach (var (offset, points) in rows.OrderBy(r => r.Offset))
            {
                if (points == null || points.Count == 0) continue;
                if (pavement == null && points.Count(p => p == Ground.Pavement) * 2 > points.Count) pavement = offset;
                if (road == null && points.Count(p => p == Ground.Road) * 2 > points.Count) road = offset;
            }
            return new SideReading(pavement, road);
        }

        public static HatchVerdict Decide(SideReading hatch, SideReading opposite)
        {
            if (hatch == null) throw new ArgumentNullException(nameof(hatch));
            if (opposite == null) throw new ArgumentNullException(nameof(opposite));
            if (hatch.Clear) return HatchVerdict.Pavement;
            if (hatch.RoadFirst) return opposite.Clear ? HatchVerdict.WrongWay : HatchVerdict.NoPavement;
            return HatchVerdict.Unclear;
        }

        public static bool Opens(HatchVerdict verdict) => verdict == HatchVerdict.Pavement || verdict == HatchVerdict.Unclear;
    }
}
