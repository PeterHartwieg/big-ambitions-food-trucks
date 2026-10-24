#nullable enable
using System;
using FoodTrucks.Core.Fleet;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    // Shared ground placement for the player and visible cart crew. NPC ground plus the native street
    // exclusions are required even when the cart itself has a legal pitch or the player mesh covers the road.
    internal static class CartGroundPlacement
    {
        private const float FallbackMetres = 3f;
        private const float SampleStep = 0.25f;
        internal static bool IsSafe(Vector3 p, TrafficLanes lanes) =>
            NavMesh.SamplePosition(p, out var pedestrian, 0.5f, Extensions.NavMeshHelper.NpcNavMeshFilter) &&
            (p.x - pedestrian.position.x) * (p.x - pedestrian.position.x) +
            (p.z - pedestrian.position.z) * (p.z - pedestrian.position.z) <= 0.01f &&
            Mathf.Abs(p.y - pedestrian.position.y) <= 0.3f && QueueLine.OnStreet(p, lanes) == null;

        internal static bool Find(Transform cart, int side, float radius, (double X, double Z) preferred,
            NavMeshQueryFilter filter, TrafficLanes lanes, Func<Vector3, bool> allowed,
            out Vector3 spot, out string placement, out bool behindCart)
        {
            var center = cart.position;
            var outward = CartStanceWorld.Facing(cart, side);
            var forward = CartStanceWorld.Forward(cart);
            bool Safe(Vector3 p) => IsSafe(p, lanes) && allowed(p);

            bool Search(Vector3 wanted, Func<Vector3, bool> geometry, out Vector3 found, out bool fallback)
            {
                bool Valid(Vector3 p) => geometry(p) && Mathf.Abs(p.y - center.y) <= 0.5f &&
                    Vector3.Distance(p, wanted) <= FallbackMetres && Safe(p);
                if (NavMesh.SamplePosition(wanted, out var exact, 0.1f, filter) && Valid(exact.position))
                {
                    found = exact.position;
                    fallback = false;
                    return true;
                }
                Vector3? best = null;
                if (NavMesh.SamplePosition(wanted, out var nearest, FallbackMetres, filter) && Valid(nearest.position)) best = nearest.position;
                for (var across = -FallbackMetres; across <= FallbackMetres; across += SampleStep)
                for (var along = -FallbackMetres; along <= FallbackMetres; along += SampleStep)
                {
                    var probe = wanted + outward * across + forward * along;
                    if (!NavMesh.SamplePosition(probe, out var hit, SampleStep, filter)) continue;
                    // CalculatePath is synchronous: only prove candidates that could improve the nearest safe result.
                    if (best.HasValue && (hit.position - wanted).sqrMagnitude >= (best.Value - wanted).sqrMagnitude) continue;
                    if (Valid(hit.position)) best = hit.position;
                }
                found = best ?? wanted;
                fallback = true;
                return best.HasValue;
            }

            // The body/wheels extend 0.50 m sideways. Keep the server's radius clear of the hull on each side.
            var clearance = 0.50f + radius;
            var behind = CartStanceWorld.Point(cart, (-CartStance.Side(side) * Mathf.Max((float)Math.Abs(preferred.X), clearance), preferred.Z));
            behindCart = true;
            if (Search(behind, p => Vector3.Dot(center - p, outward) >= clearance - 0.01f &&
                Vector3.Dot(center - p, outward) <= 1.5f &&
                Mathf.Abs(Vector3.Dot(p - behind, forward)) <= (float)CartStance.AcrossMetres, out spot, out var usedFallback))
            {
                placement = usedFallback ? "at a fallback behind the cart, opposite the queue" : "behind the cart at the equipment centre, opposite the queue";
                return true;
            }
            // No end-of-cart service: an employee there cannot reach the counter controls. Queue layout
            // tries both sides with this same rule, and shows no line if neither has safe counter-side ground.
            spot = center;
            placement = "left where they stand";
            return false;
        }
    }
}
