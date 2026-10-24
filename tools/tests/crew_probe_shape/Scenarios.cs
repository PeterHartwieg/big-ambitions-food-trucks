#nullable enable
using System;
using FoodTrucks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Reuse the visibility harness's instrumented Unity surface without compiling the bridge command.
namespace FoodTrucks
{
    internal static partial class TestBridgeCommands
    {
        private static object CrewDrive(JObject args) => throw new Exception("Bridge command touched by shape helper");
    }
}

internal static class Scenarios
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(float expected, float actual) => Check(Math.Abs(expected - actual) < .0001f, $"Expected {expected}, got {actual}");
    private static void Vec(Vector3 expected, Vector3 actual) { Near(expected.x, actual.x); Near(expected.y, actual.y); Near(expected.z, actual.z); }
    private static T Child<T>(Transform root) where T : Component, new()
    {
        var go = new GameObject(typeof(T).Name);
        go.transform.parent = root; root.Children.Add(go.transform); return go.Add<T>();
    }
    private static void Bounds(Collider c, Transform root, Vector3 expectedMin, Vector3 expectedMax)
    {
        Check(CrewProbeShape.TryBounds(c, root, out var min, out var max), "Shape should be supported");
        Vec(expectedMin, min); Vec(expectedMax, max);
    }
    private static void Invalid(Collider c, Transform root)
    {
        Check(!CrewProbeShape.TryBounds(c, root, out var min, out var max), "Invalid shape should be refused");
        Vec(default, min); Vec(default, max);
    }
    public static void Main(string[] args)
    {
        var root = new GameObject("Root").transform;
        var box = Child<BoxCollider>(root);
        switch (args[0])
        {
            case "rotation":
                root.position = new Vector3(100, 20, -30); root.yaw = .7f; root.scale = new Vector3(2, 3, .5f);
                box.center = new Vector3(1, 0, 0); box.size = new Vector3(2, 4, 6);
                box.transform.localPosition = new Vector3(3, 0, 4);
                box.transform.yaw = MathF.PI / 2; box.transform.scale = new Vector3(2, 1, 1);
                Bounds(box, root, new Vector3(0, -2, 0), new Vector3(6, 2, 4));
                // The target root need not be an ancestor of the collider.
                var other = new GameObject("OtherRoot").transform;
                other.position = new Vector3(10, 0, 0); other.yaw = MathF.PI / 2;
                var standalone = new GameObject("Standalone").Add<BoxCollider>();
                standalone.size = new Vector3(2, 4, 6); standalone.transform.position = new Vector3(13, 0, 0);
                Bounds(standalone, other, new Vector3(-3, -2, 2), new Vector3(3, 2, 4));
                break;
            case "shapes":
                box.center = new Vector3(1, 2, 3); box.size = new Vector3(2, 4, 6);
                Bounds(box, root, new Vector3(0, 0, 0), new Vector3(2, 4, 6));
                var mesh = Child<MeshCollider>(root);
                mesh.sharedMesh = new Mesh { bounds = new Bounds(new Vector3(-1, 2, 3), new Vector3(4, 6, 8)) };
                Bounds(mesh, root, new Vector3(-3, -1, -1), new Vector3(1, 5, 7));
                var sphere = Child<SphereCollider>(root); sphere.center = new Vector3(1, 2, 3); sphere.radius = 2;
                Bounds(sphere, root, new Vector3(-1, 0, 1), new Vector3(3, 4, 5));
                var capsule = Child<CapsuleCollider>(root); capsule.radius = 2; capsule.height = 8;
                for (var direction = 0; direction < 3; direction++)
                {
                    capsule.direction = direction;
                    var extent = new Vector3(2, 2, 2); extent[direction] = 4;
                    Bounds(capsule, root, extent * -1, extent);
                }
                capsule.height = 1; // Native capsule cannot be shorter than its diameter.
                Bounds(capsule, root, new Vector3(-2, -2, -2), new Vector3(2, 2, 2));
                // Shape enclosure follows transformed corners, including reflected/nonuniform scales.
                sphere.transform.scale = new Vector3(-2, 3, .5f);
                Bounds(sphere, root, new Vector3(-6, 0, .5f), new Vector3(2, 12, 2.5f));
                break;
            case "disabled":
                box.size = new Vector3(2, 4, 6); box.enabled = false; box.ThrowWorldBounds = true;
                Bounds(box, root, new Vector3(-1, -2, -3), new Vector3(1, 2, 3));
                box.gameObject.activeSelf = false;
                Bounds(box, root, new Vector3(-1, -2, -3), new Vector3(1, 2, 3));
                var meshDisabled = Child<MeshCollider>(root); meshDisabled.enabled = false;
                meshDisabled.sharedMesh = new Mesh { bounds = new Bounds(default, new Vector3(2, 4, 6)) };
                Bounds(meshDisabled, root, new Vector3(-1, -2, -3), new Vector3(1, 2, 3));
                break;
            case "invalid":
                foreach (var bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    box.size = new Vector3(1, bad, 1); Invalid(box, root);
                    var sphereBad = Child<SphereCollider>(root); sphereBad.radius = bad; Invalid(sphereBad, root);
                    var capsuleBad = Child<CapsuleCollider>(root); capsuleBad.radius = bad; capsuleBad.height = 2; Invalid(capsuleBad, root);
                    capsuleBad.radius = 1; capsuleBad.height = bad; Invalid(capsuleBad, root);
                    var meshBad = Child<MeshCollider>(root);
                    meshBad.sharedMesh = new Mesh { bounds = new Bounds(default, new Vector3(1, 1, bad)) }; Invalid(meshBad, root);
                }
                box.size = Vector3.one; box.center = new Vector3(float.NaN, 0, 0); Invalid(box, root);
                var invalidAxis = Child<CapsuleCollider>(root); invalidAxis.radius = 1; invalidAxis.height = 2;
                invalidAxis.direction = -1; Invalid(invalidAxis, root); invalidAxis.direction = 3; Invalid(invalidAxis, root);
                var meshCenter = Child<MeshCollider>(root);
                meshCenter.sharedMesh = new Mesh { bounds = new Bounds(new Vector3(0, float.PositiveInfinity, 0), Vector3.one) }; Invalid(meshCenter, root);
                var hugeSphere = Child<SphereCollider>(root); hugeSphere.radius = float.MaxValue; Invalid(hugeSphere, root);
                Invalid(Child<Collider>(root), root); Invalid(Child<MeshCollider>(root), root);
                Invalid(null!, root); Invalid(box, null!);
                break;
            case "transforms":
                box.transform.scale = new Vector3(0, 1, 1); Invalid(box, root);
                box.transform.scale = Vector3.one; box.transform.localPosition = new Vector3(float.NaN, 0, 0); Invalid(box, root);
                box.transform.localPosition = new Vector3(0, float.PositiveInfinity, 0); Invalid(box, root);
                box.transform.localPosition = default; root.scale = new Vector3(1, 0, 1); Invalid(box, root);
                root.scale = Vector3.one; box.transform.yaw = float.NaN; Invalid(box, root);
                break;
            case "allocations":
                // Warm the JIT before measuring only repeated helper calls.
                for (var i = 0; i < 1000; i++) Check(CrewProbeShape.TryBounds(box, root, out _, out _), "Warmup");
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 10000; i++) Check(CrewProbeShape.TryBounds(box, root, out _, out _), "Repeat");
                Check(GC.GetAllocatedBytesForCurrentThread() == before, "Repeated bounds conversion allocated managed memory");
                break;
            default: throw new Exception("Unknown scenario");
        }
        Check(Collider.WorldBoundsReads == 0, "Helper must never consult collider world bounds");
        Writes.Count = 0;
        CrewProbeShape.TryBounds(box, root, out _, out _);
        Check(Writes.Count == 0, "Shape helper changed Unity state");
        Console.WriteLine("PASS: " + args[0]);
    }
}
