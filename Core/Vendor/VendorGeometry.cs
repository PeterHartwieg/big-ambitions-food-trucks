using System;
using FoodTrucks.Core.Fleet;

namespace FoodTrucks.Core.Vendor
{
    // Where another mod's customers stand at a vehicle, and how fast it serves them (VendorApi's snapshot).
    public static class VendorGeometry
    {
        // A truck's hatch is on its right, 1.10 m behind the middle; its line stands 2.0 m out and runs towards the back,
        // a step (1.0 m) apart (CartQueueView's truck shape). A cart's window is on its queue side at the counter; its
        // line runs straight out from the window, 0.8 m apart.
        public const double TruckOut = 2.0;
        public const double TruckAlong = -1.10;
        public const double TruckSpacing = 1.0;
        public const double CartSpacing = 0.8;

        // The window's customer side and the first waiting place, on the ground plane, for a vehicle standing at (x, z)
        // with heading `yaw` (Unity degrees). QueueHeading is the way the line runs, in the same degrees.
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

        // Orders an hour at the window: each server makes 60 / orderMinutes an hour at the team's speed. 0 when nobody serves.
        public static int MealsPerHour(double orderMinutes, double speed, int servers)
        {
            if (servers <= 0 || double.IsNaN(orderMinutes) || double.IsInfinity(orderMinutes) || orderMinutes <= 0 ||
                double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0) return 0;
            var perHour = Math.Floor(60.0 / orderMinutes * speed * servers);
            return perHour >= int.MaxValue ? int.MaxValue : (int)perHour;
        }
    }
}
