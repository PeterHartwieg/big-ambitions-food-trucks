using System.Collections.Generic;

namespace FoodTrucks.Core.Demand
{
    public enum ZoneMode
    {
        Override,
        Add
    }

    // A point on the ground in world metres (Unity's x and z).
    public readonly struct ZonePoint
    {
        public ZonePoint(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double X { get; }
        public double Z { get; }
    }

    // A hand-drawn polygon from zones.json that replaces or adds to the automatic area mix.
    public sealed class Zone
    {
        public Zone(string id, ZoneMode mode, double share, AreaMix areas, IReadOnlyList<ZonePoint> polygon)
        {
            Id = id;
            Mode = mode;
            Share = share;
            Areas = areas;
            Polygon = polygon;
        }

        public string Id { get; }
        public ZoneMode Mode { get; }
        // For Add zones: the zone's part of the blended mix, 0 to 1. Override zones use 1.
        public double Share { get; }
        public AreaMix Areas { get; }
        public IReadOnlyList<ZonePoint> Polygon { get; }
    }
}
