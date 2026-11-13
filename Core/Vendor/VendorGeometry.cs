using System;
using FoodTrucks.Core.Fleet;

namespace FoodTrucks.Core.Vendor
{
    public static class VendorGeometry
    {
        public const double TruckOut = 2.0;
        public const double TruckAlong = -1.10;
        public const double TruckSpacing = 1.0;
        public const double CartSpacing = 0.8;

        public static (double X, double Z, double QueueX, double QueueZ, double QueueHeading) Window(string kind, int side, double x, double z, double yaw)
        {
            var forward = CartStance.Forward(yaw);
            var truck = kind == "truck";
            var local = truck ? (TruckOut, TruckAlong) : CartStance.Window(side, CartStance.WindowOut);
            var along = truck ? (0.0, -1.0) : ((double)CartStance.Side(side), 0.0);
            var spacing = truck ? TruckSpacing : CartSpacing;
            var window = CartStance.ToWorld((x, z), forward, local);
            var direction = CartStance.DirectionToWorld(forward, along);
            return (window.X, window.Z, window.X + direction.X * spacing, window.Z + direction.Z * spacing, CartStance.Yaw(direction));
        }

        public static int MealsPerHour(double orderMinutes, double speed, int servers)
        {
            if (servers <= 0 || double.IsNaN(orderMinutes) || double.IsInfinity(orderMinutes) || orderMinutes <= 0 ||
                double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0) return 0;
            var perHour = Math.Floor(60.0 / orderMinutes * speed * servers);
            return perHour >= int.MaxValue ? int.MaxValue : (int)perHour;
        }
    }
}
