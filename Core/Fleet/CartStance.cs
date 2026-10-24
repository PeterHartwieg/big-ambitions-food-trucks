using System;

namespace FoodTrucks.Core.Fleet
{
    // Where a food cart is worked from. Everything is first worked out in the cart's own frame (x across, positive to
    // its right; z along, positive forward; metres from the root at the body's centre, CartAssets), then carried into
    // the world by the cart's live position and heading, so the answer is the same at any heading and follows the
    // cart wherever it is pushed, parked or loaded.
    //
    // The cash register stands on the counter's front corner on the customers' side, facing them (CartAssets puts it
    // at x 0.29, z 0.615 turned to +X, the right; on the left side it is mirrored). Whoever serves, an employee or the
    // player, stands behind the cart straight across the counter from it, facing the customers, and the first
    // customer stands at the same point along the cart on the other side. Before 0.10.7 the server stood at the
    // body's middle (z 0) and the crew's side came from the world-space direction to the first customer's NavMesh
    // place, so they stood 0.6 m back from the register and turned off the cart's axis by the place's snap.
    public static class CartStance
    {
        // The register's place on the counter for a queue on the right, as CartAssets builds it (art/README.md).
        public const double RegisterAcross = 0.29;
        public const double RegisterHeight = 0.95;
        public const double RegisterAlong = 0.615;

        // The server's centre from the cart's centre line. Level with the register the root's box collider and
        // NavMesh obstacle reach x 0.48 (the wheels, at z 0, reach 0.50), so a 0.3 m body radius is 0.07 m clear.
        public const double BehindMetres = 0.85;

        // The first customer's distance out from the cart's centre line (CartQueueView's cart window).
        public const double WindowOut = 1.05;

        // A server found this close to the register's place along the cart faces straight across it; one further
        // along (a fallback past an obstacle) faces the first customer instead, so they never face past the cart.
        public const double AcrossMetres = 0.35;

        // Crew who don't serve while the player does wait further along the same side, a step apart.
        public const double WaitSpacing = 0.9;

        // Queue sides are 1 (the cart's right) or -1 (its left); anything else counts as the right, the side the
        // register faces while the cart is closed.
        public static int Side(int side) => side < 0 ? -1 : 1;

        // The register in the cart's frame: its position and the turn about the vertical that faces it to the
        // customers (the model faces +Z; +90 degrees turns it to the right, -90 to the left).
        public static (double X, double Y, double Z, double Yaw) Register(int side) =>
            (Side(side) * RegisterAcross, RegisterHeight, RegisterAlong, Side(side) * 90.0);

        // An open cart retains its chosen customer side while its queue is temporarily out of view.
        public static int DisplaySide(bool open, int? queueSide, int previous) =>
            open ? Side(queueSide ?? previous) : 1;

        // The server's place in the cart's frame, `behind` metres out on the side away from the customers.
        public static (double X, double Z) Server(int side, double behind = BehindMetres)
        {
            if (double.IsNaN(behind) || behind <= 0) throw new ArgumentOutOfRangeException(nameof(behind));
            return (-Side(side) * behind, RegisterAlong);
        }

        // The first customer's place in the cart's frame, `outMetres` out on the customers' side.
        public static (double X, double Z) Window(int side, double outMetres) => (Side(side) * outMetres, RegisterAlong);

        // Whether a server standing at `along` (z in the cart's frame) faces straight across the cart.
        public static bool FacesAcross(double along) => Math.Abs(along - RegisterAlong) <= AcrossMetres;

        // A crew member who waits while the player serves: on the server's side of the cart, `index` + 1 steps along
        // from the server's place, towards the rear unless the player stands further back than the server (at a
        // fallback by the rear end), then towards the front. playerAlong is the player's z in the cart's frame.
        public static (double X, double Z) Waiting(int side, int index, double? playerAlong, double behind = BehindMetres)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            var server = Server(side, behind);
            var direction = playerAlong.HasValue && playerAlong.Value < server.Z - 0.01 ? 1 : -1;
            return (server.X, server.Z + direction * WaitSpacing * (index + 1));
        }

        // A point in the cart's frame carried into the world, on the ground plane. forward is the cart's forward
        // direction (any length; only x and z are used, so a cart on a slope still works). Matches Unity's
        // Transform.TransformPoint for a cart turned only about the vertical: right is (forward.z, -forward.x).
        public static (double X, double Z) ToWorld((double X, double Z) cart, (double X, double Z) forward, (double X, double Z) local)
        {
            var (fx, fz) = Flat(forward);
            return (cart.X + local.X * fz + local.Z * fx, cart.Z - local.X * fx + local.Z * fz);
        }

        // A world point on the ground plane in the cart's frame: the inverse of ToWorld.
        public static (double X, double Z) ToLocal((double X, double Z) cart, (double X, double Z) forward, (double X, double Z) world)
        {
            var (fx, fz) = Flat(forward);
            var dx = world.X - cart.X;
            var dz = world.Z - cart.Z;
            return (dx * fz - dz * fx, dx * fx + dz * fz);
        }

        // A direction in the cart's frame carried into the world, unit length.
        public static (double X, double Z) DirectionToWorld((double X, double Z) forward, (double X, double Z) local)
        {
            var (x, z) = ToWorld((0, 0), forward, local);
            var length = Math.Sqrt(x * x + z * z);
            if (length < 1e-9) throw new ArgumentException("A direction needs a length.", nameof(local));
            return (x / length, z / length);
        }

        // The way the server faces in the world: straight across the cart towards the customers.
        public static (double X, double Z) Facing((double X, double Z) forward, int side) => DirectionToWorld(forward, (Side(side), 0));

        // The cart's heading as Unity's yaw in degrees (0 faces +Z, 90 faces +X), for logs and tests.
        public static double Yaw((double X, double Z) forward)
        {
            var (fx, fz) = Flat(forward);
            var degrees = Math.Atan2(fx, fz) * 180.0 / Math.PI;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        // The cart's forward direction for a heading in degrees.
        public static (double X, double Z) Forward(double yawDegrees)
        {
            var radians = yawDegrees * Math.PI / 180.0;
            return (Math.Sin(radians), Math.Cos(radians));
        }

        // A cart lying on its side has no usable forward on the ground; it is treated as facing +Z.
        private static (double X, double Z) Flat((double X, double Z) forward)
        {
            var length = Math.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
            return length < 1e-6 ? (0.0, 1.0) : (forward.X / length, forward.Z / length);
        }
    }
}
