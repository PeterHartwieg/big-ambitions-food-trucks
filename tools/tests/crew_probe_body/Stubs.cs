#nullable enable
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public partial class Object { public string name = ""; private static int next; private readonly int id = ++next; public int GetInstanceID() => id; }
    public partial class GameObject : Object
    {
        public int layer;
        public bool activeSelf = true;
        public bool ThrowActiveRead;
        public bool activeInHierarchy => ThrowActiveRead ? throw new InvalidOperationException("destroyed native object") : activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public readonly Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        public GameObject(string name = "") { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T Add<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
    }
    public partial class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : Component
        { foreach (var c in gameObject.Components) if (c is T match) return match; return null; }
        public T? GetComponentInParent<T>(bool includeInactive = false) where T : Component
        {
            for (var t = transform; t != null; t = t.parent)
                if (t == transform || includeInactive || t.gameObject.activeInHierarchy)
                    foreach (var c in t.gameObject.Components) if (c is T match) return match;
            return null;
        }
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component
        { results.Clear(); Collect(transform, includeInactive, results); }
        private static void Collect<T>(Transform t, bool inactive, List<T> results) where T : Component
        {
            if (inactive || t.gameObject.activeInHierarchy)
            {
                foreach (var c in t.gameObject.Components) if (c is T match) results.Add(match);
                foreach (var child in t.Children) Collect(child, inactive, results);
            }
        }
    }
    public partial class Transform : Component
    {
        private Transform? owner;
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform? parent { get => owner; set { owner?.Children.Remove(this); owner = value; owner?.Children.Add(this); } }
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Quaternion rotation
        { get => parent == null ? localRotation : parent.rotation * localRotation;
          set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; }
        public Vector3 position { get => parent == null ? localPosition : parent.TransformPoint(localPosition); set => localPosition = parent == null ? value : parent.InverseTransformPoint(value); }
        public bool MatrixDerivedScale;
        public Vector3 lossyScale
        {
            get
            {
                if (!MatrixDerivedScale) return parent == null ? localScale : Vector3.Scale(parent.lossyScale, localScale);
                var world = WorldMatrix();
                return new Vector3(new System.Numerics.Vector3(world.M11, world.M12, world.M13).Length(),
                    new System.Numerics.Vector3(world.M21, world.M22, world.M23).Length(),
                    new System.Numerics.Vector3(world.M31, world.M32, world.M33).Length());
            }
        }
        private System.Numerics.Matrix4x4 WorldMatrix()
        {
            var scale = System.Numerics.Matrix4x4.CreateScale(localScale.x, localScale.y, localScale.z);
            var matrix = scale * localRotation.Matrix;
            return parent == null ? matrix : matrix * parent.WorldMatrix();
        }
        public Vector3 TransformPoint(Vector3 p)
        { p = Quaternion.Rotate(localRotation, Vector3.Scale(p, localScale)) + localPosition; return parent == null ? p : parent.TransformPoint(p); }
        public Vector3 InverseTransformPoint(Vector3 p)
        { if (parent != null) p = parent.InverseTransformPoint(p); p = Quaternion.Rotate(Quaternion.Inverse(localRotation), p - localPosition); return new Vector3(p.x / localScale.x, p.y / localScale.y, p.z / localScale.z); }
    }
    public partial struct Quaternion
    {
        private System.Numerics.Quaternion value;
        private Quaternion(System.Numerics.Quaternion v) { value = v; }
        internal System.Numerics.Matrix4x4 Matrix => System.Numerics.Matrix4x4.CreateFromQuaternion(value);
        public static Quaternion identity => new Quaternion(System.Numerics.Quaternion.Identity);
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(System.Numerics.Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180));
        public static Quaternion Inverse(Quaternion q) => new Quaternion(System.Numerics.Quaternion.Inverse(q.value));
        public static Vector3 operator *(Quaternion q, Vector3 v) => Rotate(q, v);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(a.value * b.value);
        internal static Vector3 Rotate(Quaternion q, Vector3 v) { var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.x, v.y, v.z), q.value); return new Vector3(p.X, p.Y, p.Z); }
        public override bool Equals(object? obj) => obj is Quaternion q && value.Equals(q.value);
        public bool Equals(Quaternion q) => value.Equals(q.value);
        public override int GetHashCode() => value.GetHashCode();
    }
    public partial struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this * (1 / magnitude) : default;
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : z; set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(MathF.Min(a.x, b.x), MathF.Min(a.y, b.y), MathF.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(MathF.Max(a.x, b.x), MathF.Max(a.y, b.y), MathF.Max(a.z, b.z));
    }
    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 extents => size * .5f;
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;
    }
    public struct LayerMask { public int value; public static implicit operator LayerMask(int v) => new LayerMask { value = v }; }
    public static class Writes { public static int Count; }
    public class Rigidbody : Component
    {
        private bool detect = true;
        private LayerMask include, exclude;
        public bool Throw;
        public bool detectCollisions { get => Throw ? throw new Exception("unavailable") : detect; set { Writes.Count++; detect = value; } }
        public LayerMask includeLayers { get => include; set { Writes.Count++; include = value; } }
        public LayerMask excludeLayers { get => exclude; set { Writes.Count++; exclude = value; } }
    }
    public partial class Collider : Component
    {
        private bool on = true, trigger;
        private LayerMask include, exclude;
        private int priority;
        public bool Throw;
        public bool enabled { get => on; set { Writes.Count++; on = value; } }
        public bool isTrigger { get => trigger; set { Writes.Count++; trigger = value; } }
        public Rigidbody? AttachmentOverride;
        public Rigidbody? attachedRigidbody => enabled && gameObject.activeInHierarchy ? AttachmentOverride ?? GetComponentInParent<Rigidbody>() : null;
        public LayerMask includeLayers { get => Throw ? throw new Exception("unavailable") : include; set { Writes.Count++; include = value; } }
        public LayerMask excludeLayers { get => exclude; set { Writes.Count++; exclude = value; } }
        public int layerOverridePriority { get => priority; set { Writes.Count++; priority = value; } }
        public Bounds bounds
        {
            get
            {
                // Only the external trigger may use a world AABB; physical certificates
                // must retain the root-local shape path (including disabled wheel shells).
#if TRAFFIC_INTEGRATION
                Bounds shape;
                if (this is BoxCollider box) shape = new Bounds(box.center, box.size);
                else if (this is MeshCollider mesh && mesh.sharedMesh != null) shape = mesh.sharedMesh.bounds;
                else throw new Exception("unsupported bounds");
#else
                if (!(this is BoxCollider b) || !isTrigger) throw new Exception("Physical world AABB must not be queried");
                var shape = new Bounds(b.center, b.size);
#endif
                if (Throw) throw new Exception("unavailable");
                var lo = Vector3.one * float.MaxValue; var hi = Vector3.one * float.MinValue;
                for (var i = 0; i < 8; i++)
                {
                    var p = transform.TransformPoint(shape.center + Vector3.Scale(shape.size * .5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
                return new Bounds((lo + hi) * .5f, hi - lo);
            }
        }
        public int ClosestPointReads;
        public Vector3 ClosestPoint(Vector3 point)
        {
            ClosestPointReads++;
            if (Throw) throw new Exception("unavailable");
            if (!enabled || !gameObject.activeInHierarchy) return point; // native disabled behavior
            var local = transform.InverseTransformPoint(point);
            if (this is MeshCollider m && m.CookedClosestPoint != null)
                return transform.TransformPoint(m.CookedClosestPoint(local));
            Bounds b;
            if (this is BoxCollider box) b = new Bounds(box.center, box.size);
            else if (this is MeshCollider mesh && mesh.sharedMesh != null) b = mesh.sharedMesh.bounds;
            else throw new Exception("unsupported closest point");
            return transform.TransformPoint(Vector3.Min(Vector3.Max(local, b.min), b.max));
        }
    }
    public class BoxCollider : Collider { public Vector3 center, size = Vector3.one; }
    public class SphereCollider : Collider { public Vector3 center; public float radius; }
    public class CapsuleCollider : Collider { public Vector3 center; public float radius, height; public int direction; }
    public class Mesh : Object
    {
        public Bounds bounds;
        public bool isReadable = true;
        public Vector3[] Data = Array.Empty<Vector3>();
        public int[] Indices = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
        public int subMeshCount = 1;
        public int VertexReads, TriangleReads;
        public int vertexCount => Data.Length;
        public Vector3[] vertices => (Vector3[])Data.Clone();
        public void GetVertices(List<Vector3> target) { VertexReads++; if (!isReadable) throw new Exception("unreadable"); target.Clear(); target.AddRange(Data); }
        public uint GetIndexCount(int submesh) { if (submesh != 0) throw new Exception("submesh"); return (uint)Indices.Length; }
        public void GetTriangles(List<int> target, int submesh)
        { TriangleReads++; if (!isReadable || submesh != 0) throw new Exception("unreadable submesh"); target.Clear(); target.AddRange(Indices); }
    }
    [Flags] public enum MeshColliderCookingOptions { None = 0, CookForFasterSimulation = 2, EnableMeshCleaning = 4, WeldColocatedVertices = 8, UseFastMidphase = 16 }
    public class MeshCollider : Collider
    {
        public Mesh? sharedMesh;
        public bool convex = true;
        public MeshColliderCookingOptions cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning;
        public Func<Vector3, Vector3>? CookedClosestPoint;
    }
    public static partial class Mathf { public static float Max(float a, float b) => MathF.Max(a, b); }
}
public class CarController : UnityEngine.Component
{
    public UnityEngine.Collider? vehicleCollider, additionalCollider;
    public NWH.VehiclePhysics2.VehicleController vehicleController = null!;
}
public class VehicleParkingHelper : UnityEngine.Component { public UnityEngine.Collider? parallelParkingCollider; }
namespace NWH.VehiclePhysics2
{
    public class VehicleController : UnityEngine.Component { public UnityEngine.Rigidbody vehicleRigidbody = null!; public Powertrain powertrain = new Powertrain(); }
    public class Powertrain { public List<Entry> wheels = new List<Entry>(); }
    public class Entry { public UnityEngine.Component wheelUAPI = null!; }
}
namespace NWH.WheelController3D
{
    using UnityEngine;
    public class WheelController : Component
    {
        public Wheel wheel = new Wheel();
        public Spring spring = new Spring();
        public float Radius { get => wheel.radius; set => wheel.radius = value; }
        public float Width { get => wheel.width; set => wheel.width = value; }
        public float SteerAngle, Camber, Damage;
        public Rigidbody ParentRigidbody = null!;
        public bool IsGrounded = true;
        public Vector3 HitPoint, HitNormal = new Vector3(0, 1, 0);
        public Collider HitCollider = null!;
    }
    public class Wheel
    {
        public MeshCollider topMeshCollider = null!, bottomMeshCollider = null!;
        public Transform colliderTransform = null!;
        public GameObject colliderGO = null!;
        public Vector3 localPosition;
        public float radius = .418f, width = .268f;
    }
    public class Spring { public float maxLength = .35f, length = .2f; }
}
