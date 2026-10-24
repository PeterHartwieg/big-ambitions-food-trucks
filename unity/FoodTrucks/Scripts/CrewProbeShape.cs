#if FOODTRUCKS_CREW_DRIVING_DEV
#nullable enable
using UnityEngine;

namespace FoodTrucks
{
    // Shape-local enclosure converted to root-local space, independent of collider enabled state.
    // No physics queries, world-AABB fallback, or managed allocations in repeated calls.
    internal static class CrewProbeShape
    {
        internal static bool TryBounds(Collider c, Transform root, out Vector3 min, out Vector3 max)
        {
            min = max = default;
            if (c == null || root == null || !TryLocalBounds(c, out var shape)) return false;
            var lower = Vector3.one * float.MaxValue;
            var upper = Vector3.one * float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var corner = shape.center + Vector3.Scale(shape.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = root.InverseTransformPoint(c.transform.TransformPoint(corner));
                if (!Finite(local)) return false;
                lower = Vector3.Min(lower, local); upper = Vector3.Max(upper, local);
            }
            if (!Positive(upper - lower)) return false;
            min = lower; max = upper;
            return true;
        }

        internal static bool TryLocalBounds(Collider c, out Bounds shape)
        {
            shape = default;
            if (c is BoxCollider box) shape = new Bounds(box.center, box.size);
            else if (c is MeshCollider mesh && mesh.sharedMesh != null) shape = mesh.sharedMesh.bounds;
            else if (c is SphereCollider sphere)
            {
                if (!Positive(sphere.radius)) return false;
                shape = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2));
            }
            else if (c is CapsuleCollider capsule && capsule.direction >= 0 && capsule.direction < 3)
            {
                if (!Positive(capsule.radius) || !Positive(capsule.height)) return false;
                var size = Vector3.one * (capsule.radius * 2);
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2);
                shape = new Bounds(capsule.center, size);
            }
            else return false;
            return Finite(shape.center) && Positive(shape.size);
        }

        private static bool Positive(float value) => value > 0 && !float.IsInfinity(value);
        private static bool Positive(Vector3 v) => Positive(v.x) && Positive(v.y) && Positive(v.z);
        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) ||
            float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));
    }
}

#endif
