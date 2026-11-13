using System;

namespace FoodTrucks.Core.Fleet
{
    public static class CartStance
    {
        public const double RegisterAcross = 0.29;
        public const double RegisterHeight = 0.95;
        public const double RegisterAlong = 0.615;

        public const double BehindMetres = 0.85;

        public const double WindowOut = 1.05;

        // A server found this close to the working centre along the cart faces straight across it; one further
        // along (a fallback past an obstacle) faces the first customer instead, so they never face past the cart.
        public const double AcrossMetres = 0.35;

        public const double WaitSpacing = 0.9;

        public static int Side(int side) => side < 0 ? -1 : 1;

        public static (double X, double Y, double Z, double Yaw) Register(int side) =>
            (Side(side) * RegisterAcross, RegisterHeight, RegisterAlong, Side(side) * 90.0);

        public static double EquipmentYaw(int side, string model) =>
            Side(side) * (model == "PopcornMachine" || model == "IceCreamCabinet" ? 90.0 : -90.0);

        public static int DisplaySide(bool open, int? queueSide, int previous) =>
            open ? Side(queueSide ?? previous) : 1;

        public const double CounterAlong = 0.0;

        public static double WorkAlong(double? fittedFront, double? fittedRear) =>
            fittedFront.HasValue && fittedRear.HasValue ? (fittedFront.Value + fittedRear.Value) / 2.0 : CounterAlong;

        public static (double X, double Z) Server(int side, double behind = BehindMetres, double along = CounterAlong)
        {
            if (double.IsNaN(behind) || behind <= 0) throw new ArgumentOutOfRangeException(nameof(behind));
            return (-Side(side) * behind, along);
        }

        public static (double X, double Z) Window(int side, double outMetres, double along = CounterAlong) => (Side(side) * outMetres, along);

        public static bool FacesAcross(double along, double centre = CounterAlong) => Math.Abs(along - centre) <= AcrossMetres;

        public static (double X, double Z) Waiting(int side, int index, double? playerAlong, double behind = BehindMetres, double along = CounterAlong)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            var server = Server(side, behind, along);
            var direction = playerAlong.HasValue && playerAlong.Value < server.Z - 0.01 ? 1 : -1;
            return (server.X, server.Z + direction * WaitSpacing * (index + 1));
        }

        public static (double X, double Z) ToWorld((double X, double Z) cart, (double X, double Z) forward, (double X, double Z) local)
        {
            var (fx, fz) = Flat(forward);
            return (cart.X + local.X * fz + local.Z * fx, cart.Z - local.X * fx + local.Z * fz);
        }

        public static (double X, double Z) ToLocal((double X, double Z) cart, (double X, double Z) forward, (double X, double Z) world)
        {
            var (fx, fz) = Flat(forward);
            var dx = world.X - cart.X;
            var dz = world.Z - cart.Z;
            return (dx * fz - dz * fx, dx * fx + dz * fz);
        }

        public static (double X, double Z) DirectionToWorld((double X, double Z) forward, (double X, double Z) local)
        {
            var (x, z) = ToWorld((0, 0), forward, local);
            var length = Math.Sqrt(x * x + z * z);
            if (length < 1e-9) throw new ArgumentException("A direction needs a length.", nameof(local));
            return (x / length, z / length);
        }

        public static (double X, double Z) Facing((double X, double Z) forward, int side) => DirectionToWorld(forward, (Side(side), 0));

        public static double Yaw((double X, double Z) forward)
        {
            var (fx, fz) = Flat(forward);
            var degrees = Math.Atan2(fx, fz) * 180.0 / Math.PI;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        public static (double X, double Z) Forward(double yawDegrees)
        {
            var radians = yawDegrees * Math.PI / 180.0;
            return (Math.Sin(radians), Math.Cos(radians));
        }

        private static (double X, double Z) Flat((double X, double Z) forward)
        {
            var length = Math.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
            return length < 1e-6 ? (0.0, 1.0) : (forward.X / length, forward.Z / length);
        }
    }
}
