#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoodTrucks
{
    // Local TRS profiles work for inactive pool objects and disabled colliders. World AABBs
    // are never used as an inner slab. This deliberately supports only upright box triggers.
    internal sealed class CrewProbeTrigger
    {
        internal const float UprightTolerance = .00001f;
        // Only derived matrix lengths receive this allowance. Every authored scale and
        // parent link, including the model's world ancestors, remains an exact invariant.
        private const float DerivedTolerance = .00001f;
        private readonly Transform root;
        private readonly Transform[] links, rootLinks;
        private readonly float[] heights;
        private readonly Vector3[] scales, rootScales;
        private readonly Vector3 center, size, rootScale;
        private readonly bool trigger;
        private readonly int layer;
        private readonly Vector3 dimensions;
        internal readonly Collider Collider;
        internal readonly float Min, Max;

        internal CrewProbeTrigger(Collider collider, Transform root)
        {
            Collider = collider; this.root = root; trigger = collider.isTrigger; layer = collider.gameObject.layer;
            if (!(collider is BoxCollider box)) throw new InvalidOperationException("trigger_shape_unsupported");
            center = box.center; size = box.size; rootScale = root.lossyScale;
            var ancestors = new List<Transform>();
            for (var t = root; t != null; t = t.parent)
            {
                if (ancestors.Count >= 128 || !Positive(t.localScale)) throw new InvalidOperationException("trigger_root_transform_unsupported");
                ancestors.Add(t);
            }
            rootLinks = ancestors.ToArray(); rootScales = new Vector3[rootLinks.Length];
            for (var i = 0; i < rootLinks.Length; i++) rootScales[i] = rootLinks[i].localScale;
            var path = new List<Transform>();
            for (var t = collider.transform; t != root; t = t.parent)
            {
                if (t == null || path.Count >= 128) throw new InvalidOperationException("trigger_outside_model");
                path.Add(t);
            }
            links = path.ToArray(); heights = new float[links.Length]; scales = new Vector3[links.Length];
            for (var i = 0; i < links.Length; i++) { heights[i] = links[i].localPosition.y; scales[i] = links[i].localScale; }
            var error = ReadLocal(out var lo, out var hi, out dimensions);
            if (error != null) throw new InvalidOperationException(error);
            // Root tilt is at most twice the upright tolerance: nominal trigger up and world
            // trigger up are both bounded. Erode for its full root-relative lever arm.
            var lever = 0f;
            for (var i = 0; i < 8; i++)
            {
                var p = Local(Corner(box, i));
                lever = Math.Max(lever, Vector3.Scale(p, rootScale).magnitude);
            }
            // The extra derived-length allowance bounds rounding of the captured root scale.
            // Live coverage still uses the current TRS chain and the exact inner slab.
            var margin = lever * (2 * UprightTolerance + DerivedTolerance) + .00001f;
            Min = lo + margin; Max = hi - margin;
            if (Min >= Max) throw new InvalidOperationException("trigger_profile_empty");
        }

        internal string? Validate()
        {
            var c = Collider;
            if (c == null || c.isTrigger != trigger || c.gameObject.layer != layer) return "trigger_identity_changed";
            if (!(c is BoxCollider box) || !Exact(box.center, center) || !Exact(box.size, size) || !NearDerived(root.lossyScale, rootScale))
                return "trigger_shape_scale_changed";
            for (var i = 0; i < rootLinks.Length; i++)
            {
                var t = rootLinks[i];
                if (t == null || t.parent != (i + 1 < rootLinks.Length ? rootLinks[i + 1] : null)) return "trigger_root_reparented";
                if (!Exact(t.localScale, rootScales[i])) return "trigger_authored_root_scale_changed";
            }
            for (var i = 0; i < links.Length; i++)
            {
                var t = links[i];
                if (t == null || t.parent != (i + 1 < links.Length ? links[i + 1] : root)) return "trigger_reparented";
                if (t.localPosition.y != heights[i] || !Exact(t.localScale, scales[i])) return "trigger_vertical_scale_changed";
                // Horizontal translation and native steering yaw remain supported.
                var up = t.localRotation * new Vector3(0, 1, 0);
                if (!Upright(up)) return "trigger_local_tilt";
            }
            var error = ReadLocal(out _, out _, out var dims);
            if (error != null) return error;
            if (!NearDerived(dims, dimensions)) return "trigger_shape_scale_changed";
            return null;
        }

        private Vector3 Local(Vector3 p)
        {
            foreach (var t in links) p = t.localPosition + t.localRotation * Vector3.Scale(p, t.localScale);
            return p;
        }
        private Vector3 LocalVector(Vector3 v)
        {
            foreach (var t in links) v = t.localRotation * Vector3.Scale(v, t.localScale);
            return v;
        }
        private string? ReadLocal(out float lo, out float hi, out Vector3 dims)
        {
            lo = hi = 0; dims = default;
            if (!Finite(center) || !Positive(size) || !Positive(rootScale)) return "trigger_dimensions_invalid";
            foreach (var t in links)
                if (!Finite(t.localPosition) || !Positive(t.localScale) || !Upright(t.localRotation * new Vector3(0, 1, 0)))
                    return "trigger_local_transform_unsupported";
            var x = Vector3.Scale(LocalVector(new Vector3(1, 0, 0)), rootScale);
            var y = Vector3.Scale(LocalVector(new Vector3(0, 1, 0)), rootScale);
            var z = Vector3.Scale(LocalVector(new Vector3(0, 0, 1)), rootScale);
            if (!Orthogonal(x, y, z) || !Upright(y.normalized)) return "trigger_shear_or_local_tilt";
            dims = new Vector3(x.magnitude * size.x, y.magnitude * size.y, z.magnitude * size.z);
            // Highest lower face and lowest upper face, never enclosing bounds.
            lo = float.MinValue; hi = float.MaxValue;
            for (var i = 0; i < 8; i++)
            {
                var p = Vector3.Scale(Local(Corner((BoxCollider)Collider, i)), rootScale);
                if (!Finite(p)) return "trigger_profile_nonfinite";
                if ((i & 2) == 0) lo = Math.Max(lo, p.y); else hi = Math.Min(hi, p.y);
            }
            return hi > lo ? null : "trigger_profile_empty";
        }

        internal static string? WorldInterval(Collider collider, out float lo, out float hi, out Vector3 up)
        {
            lo = hi = 0; up = collider == null ? default : collider.transform.rotation * new Vector3(0, 1, 0);
            if (!(collider is BoxCollider box)) return "trigger_shape_unsupported";
            if (!Finite(box.center) || !Positive(box.size)) return "trigger_dimensions_invalid";
            if (!WorldAxes(box.transform, out var x, out var y, out var z)) return "trigger_world_transform_unsupported";
            up = y.normalized;
            var c = box.transform.TransformPoint(box.center);
            if (!Finite(c)) return "trigger_world_nonfinite";
            var sideways = (Math.Abs(x.y) * box.size.x + Math.Abs(z.y) * box.size.z) * .5f;
            var vertical = y.y * box.size.y * .5f;
            lo = c.y - vertical + sideways; hi = c.y + vertical - sideways;
            if (!Upright(up)) return "trigger_world_tilt";
            return Finite(lo) && Finite(hi) && hi > lo ? null : "trigger_world_interval_empty";
        }
        internal static bool Nearby(Collider c, Vector3 truck)
        {
            if (!(c is BoxCollider box)) return true;
            var center = box.transform.TransformPoint(box.center);
            // The circumsphere of an orthogonal box covers horizontal offsets as well as
            // the box center. Invalid transforms must reach the evaluator and fail closed.
            var size = Vector3.Scale(box.size, box.transform.lossyScale);
            if (!Finite(center) || !Positive(size)) return true;
            var radius = size.magnitude * .5f + 100f;
            return (center - truck).sqrMagnitude < radius * radius;
        }
        private static bool WorldAxes(Transform t, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            x = new Vector3(1, 0, 0); y = new Vector3(0, 1, 0); z = new Vector3(0, 0, 1);
            var count = 0;
            for (var p = t; p != null; p = p.parent)
            {
                if (++count > 128 || !Positive(p.localScale) || !Finite(p.localPosition)) return false;
                x = p.localRotation * Vector3.Scale(x, p.localScale);
                y = p.localRotation * Vector3.Scale(y, p.localScale);
                z = p.localRotation * Vector3.Scale(z, p.localScale);
            }
            return Orthogonal(x, y, z);
        }
        private static bool Orthogonal(Vector3 x, Vector3 y, Vector3 z) => Positive(new Vector3(x.magnitude, y.magnitude, z.magnitude)) &&
            Math.Abs(Vector3.Dot(x.normalized, y.normalized)) <= UprightTolerance &&
            Math.Abs(Vector3.Dot(x.normalized, z.normalized)) <= UprightTolerance &&
            Math.Abs(Vector3.Dot(y.normalized, z.normalized)) <= UprightTolerance;
        private static bool Upright(Vector3 up) => Finite(up) && (up - new Vector3(0, 1, 0)).sqrMagnitude <= UprightTolerance * UprightTolerance;
        private static Vector3 Corner(BoxCollider b, int i) => b.center + Vector3.Scale(b.size * .5f,
            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        private static bool NearDerived(Vector3 a, Vector3 b) => Positive(a) && Positive(b) &&
            Math.Abs(a.x - b.x) <= DerivedTolerance * Math.Abs(b.x) &&
            Math.Abs(a.y - b.y) <= DerivedTolerance * Math.Abs(b.y) &&
            Math.Abs(a.z - b.z) <= DerivedTolerance * Math.Abs(b.z);
        private static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        private static bool Positive(Vector3 v) => Finite(v) && v.x > 0 && v.y > 0 && v.z > 0;
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
