using System.Collections.Generic;

namespace FoodTrucks.Core.Demand
{
    public enum ZoneMode
    {
        Override,
        Add
    }

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
        public double Share { get; }
        public AreaMix Areas { get; }
        public IReadOnlyList<ZonePoint> Polygon { get; }
    }
}
