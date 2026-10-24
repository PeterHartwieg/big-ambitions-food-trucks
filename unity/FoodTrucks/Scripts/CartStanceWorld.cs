#nullable enable
using FoodTrucks.Core.Fleet;
using UnityEngine;

namespace FoodTrucks
{
    // Core's CartStance places, from a cart's live transform: on the ground at the cart's root height (the body's
    // ground-level origin), turned only about the vertical.
    internal static class CartStanceWorld
    {
        private static (double X, double Z) Flat(Transform cart) => (cart.forward.x, cart.forward.z);

        public static Vector3 Point(Transform cart, (double X, double Z) local)
        {
            var p = cart.position;
            var (x, z) = CartStance.ToWorld((p.x, p.z), Flat(cart), local);
            return new Vector3((float)x, p.y, (float)z);
        }

        // The way the server faces: across the cart towards the customers' side.
        public static Vector3 Facing(Transform cart, int side)
        {
            var (x, z) = CartStance.Facing(Flat(cart), side);
            return new Vector3((float)x, 0f, (float)z);
        }

        public static Vector3 Forward(Transform cart)
        {
            var (x, z) = CartStance.DirectionToWorld(Flat(cart), (0, 1));
            return new Vector3((float)x, 0f, (float)z);
        }

        // How far forward of the cart's middle a world point is.
        public static double Along(Transform cart, Vector3 world) =>
            CartStance.ToLocal((cart.position.x, cart.position.z), Flat(cart), (world.x, world.z)).Z;
    }
}
