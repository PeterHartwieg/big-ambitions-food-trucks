#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
#pragma warning disable CS0414

namespace UnityEngine
{
    public static class Writes { public static int Count; }
    public class Object
    {
        private static int next;
        private readonly int id = ++next;
        public virtual string name { get; set; } = "";
        public int GetInstanceID() => id;
        public static object? Scene;
        public static T? FindObjectOfType<T>() where T : class => Scene as T;
    }
    public class GameObject : Object
    {
        private int storedLayer;
        private bool active = true;
        public int layer { get => storedLayer; set { Writes.Count++; storedLayer = value; } }
        public bool activeSelf { get => active; set { Writes.Count++; active = value; } }
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public string tag = "Untagged";
        public readonly Transform transform;
        public readonly List<Component> Components = new List<Component>();
        public GameObject(string label) { name = label; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T Add<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); Writes.Count++; return c; }
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public override string name { get => gameObject.name; set => throw new InvalidOperationException("Native name write"); }
        public T[] GetComponents<T>() where T : Component => gameObject.Components.OfType<T>().ToArray();
        public bool CompareTag(string tag) => gameObject.tag == tag;
    }
    public class Transform : Component
    {
        public Transform? parent;
        public List<Transform> Children = new List<Transform>();
        public Vector3 localPosition;
        public Vector3 position {
            get => parent == null ? localPosition : parent.TransformPoint(localPosition);
            set => localPosition = parent == null ? value : parent.InverseTransformPoint(value);
        }
        public Vector3 scale = Vector3.one;
        public float yaw;
        public Quaternion rotation => new Quaternion { y = MathF.Sin(yaw*.5f), w = MathF.Cos(yaw*.5f) };
        public int childCount => Children.Count;
        public Transform GetChild(int i) => Children[i];
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public Vector3 TransformPoint(Vector3 p)
        {
            p = Vector3.Scale(p, scale);
            p = new Vector3(p.x * MathF.Cos(yaw) + p.z * MathF.Sin(yaw), p.y, -p.x * MathF.Sin(yaw) + p.z * MathF.Cos(yaw)) + localPosition;
            return parent == null ? p : parent.TransformPoint(p);
        }
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            if (parent != null) p = parent.InverseTransformPoint(p);
            p -= localPosition;
            p = new Vector3(p.x * MathF.Cos(yaw) - p.z * MathF.Sin(yaw), p.y, p.x * MathF.Sin(yaw) + p.z * MathF.Cos(yaw));
            return new Vector3(p.x / scale.x, p.y / scale.y, p.z / scale.z);
        }
    }
    public struct Quaternion { public float x,y,z,w; public static Vector3 operator *(Quaternion q,Vector3 v) => v; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : z; set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public static Vector3 one => new Vector3(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
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
        public Vector3 extents => size * .5f;
        public Vector3 min => center - extents;
        public Vector3 max => center + extents;
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
    }
    public class Rigidbody : Component
    {
        private LayerMask include, exclude;
        private bool collisions = true, kinematic;
        public bool ThrowState;
        public LayerMask includeLayers { get => ThrowState ? throw new InvalidOperationException("Rigidbody state unavailable") : include; set { Writes.Count++; include = value; } }
        public LayerMask excludeLayers { get => exclude; set { Writes.Count++; exclude = value; } }
        public bool detectCollisions { get => collisions; set { Writes.Count++; collisions = value; } }
        public bool isKinematic { get => kinematic; set { Writes.Count++; kinematic = value; } }
    }
    public class Collider : Component
    {
        private bool on = true, trigger;
        public bool enabled { get => on; set { Writes.Count++; on = value; } }
        public bool isTrigger { get => trigger; set { Writes.Count++; trigger = value; } }
        public Rigidbody? attachedRigidbody;
        private LayerMask include, exclude;
        private int priority;
        public bool ThrowOverrides, ThrowWorldBounds;
        public static int WorldBoundsReads;
        public Bounds ReportedWorldBounds = new Bounds(new Vector3(10, 2, 30), new Vector3(2, 4, 6));
        public LayerMask includeLayers { get => ThrowOverrides ? throw new InvalidOperationException("Collider overrides unavailable") : include; set { Writes.Count++; include = value; } }
        public LayerMask excludeLayers { get => exclude; set { Writes.Count++; exclude = value; } }
        public int layerOverridePriority { get => priority; set { Writes.Count++; priority = value; } }
        public Bounds bounds {
            get {
                WorldBoundsReads++;
                if (!enabled || !gameObject.activeInHierarchy || ThrowWorldBounds)
                    throw new InvalidOperationException("World bounds unavailable");
                return ReportedWorldBounds;
            }
        }
    }
    public class BoxCollider : Collider { public Vector3 center, size = Vector3.one; }
    public class SphereCollider : Collider { public Vector3 center; public float radius; }
    public class CapsuleCollider : Collider { public Vector3 center; public float radius, height; public int direction; }
    public class Mesh : Object { public Bounds bounds; public int vertexCount=8; public bool isReadable=true; }
    public class MeshCollider : Collider { public Mesh? sharedMesh; public bool convex; }
    public struct LayerMask
    {
        public int value;
        public static implicit operator LayerMask(int mask) => new LayerMask { value = mask };
        public static string LayerToName(int layer) => "Layer" + layer;
    }
    public static class Physics
    {
        public static HashSet<(int, int)> Ignored = new HashSet<(int, int)>();
        public static bool GetIgnoreLayerCollision(int a, int b) => Ignored.Contains((a, b)) || Ignored.Contains((b, a));
    }
    public static class Mathf { public static float Max(float a, float b) => MathF.Max(a, b); }
}
public class VehicleInstance { public string id = "owned", vehicleTypeName = "foodtruck"; }
public class CarController : UnityEngine.Component
{
    public VehicleInstance? vehicleInstance = new VehicleInstance();
    public UnityEngine.Collider? vehicleCollider, additionalCollider;
    public Nwh? vehicleController = new Nwh();
}
public class VehicleParkingHelper : UnityEngine.Component { public UnityEngine.Collider? parallelParkingCollider; }
public class Nwh { public Powertrain? powertrain = new Powertrain(); public bool enabled; public UnityEngine.Rigidbody vehicleRigidbody = new UnityEngine.Rigidbody(); }
public class Powertrain { public List<Wheel?>? wheels = new List<Wheel?>(); public Engine engine = new Engine(); }
public class Engine { public bool ignition, IsRunning, starterActive; }
public class Wheel { public UnityEngine.Component? wheelUAPI; }
public class Game { public List<VehicleInstance> VehicleInstances = new List<VehicleInstance>(); }
public static class SaveGameManager { public static Game Current = new Game(); }
namespace Helpers
{
    public static class VehicleHelper
    {
        public static CarController? Car;
        public static CarController? GetVehicleController(VehicleInstance instance) => Car;
    }
}
namespace GleyTrafficSystem
{
    using UnityEngine;
    public class VehicleComponent : Component
    {
        public Transform? frontTrigger;
        private LayerMask obstacleLayers, playerLayers, buildingLayers;
        public void Masks(int obstacle, int player, int building) { obstacleLayers = obstacle; playerLayers = player; buildingLayers = building; }
    }
    public class TrafficVehicles
    {
        public List<VehicleComponent?> Models = new List<VehicleComponent?>();
        public List<VehicleComponent?> GetVehicleList() => Models;
    }
    public class TrafficManager
    {
        public static bool HasInstance = true, IsInitialized = true;
        public static int InstanceReads;
        private static readonly TrafficManager singleton = new TrafficManager();
        public static TrafficManager Instance { get { InstanceReads++; return singleton; } }
        private TrafficVehicles trafficVehicles = new TrafficVehicles();
        public TrafficVehicles Pool => trafficVehicles;
    }
    public class Waypoint { public int listIndex; public Vector3 position; public int[] neighbors = Array.Empty<int>(); public bool enter, exit, stop, giveWay, temporaryDisabled; }
}
namespace GleyUrbanAssets
{
    public class CurrentSceneData { public GleyTrafficSystem.Waypoint[]? allWaypoints; }
}
namespace FoodTrucks
{
    public class BridgeError : Exception { public BridgeError(string message) : base(message) { } }
    public static class FoodTrucksMod { public const string TruckTypeName = "foodtruck"; }
    // Any accidental probe/ownership command invocation fails immediately.
    public static class CrewDriveProbe
    {
        internal static CrewProbeTraffic? ObstacleTraffic => throw new Exception("Probe obstacle traffic touched during visibility inspection");
        public static object Status() => throw new Exception("Probe status touched");
        public static object StopProbe(Guid session, long generation) => throw new Exception("Probe stop touched");
        public static object StartProbe(string id, int[] ids, float speed) => throw new Exception("Probe start touched");
    }
    internal sealed class CrewProbeTraffic { internal static object InspectCoverage(CarController car) => throw new Exception("Trigger coverage touched during visibility inspection"); }
    internal sealed class CrewProbeBody {
        internal bool TrafficCertified => false;
        internal CrewProbeBody(CarController car) => throw new Exception("Body contract constructed during visibility inspection");
        internal UnityEngine.Vector3 Min, Max;
        internal bool TryGround(out float y) { y=0; throw new Exception("Ground touched"); }
        internal string? Validate()=>throw new Exception("Body validated");
    }
    internal static class CrewProbeTrafficEvidence {
        internal static JObject InspectObstacle(CrewProbeTraffic? traffic)=>throw new Exception("Obstacle evidence touched during visibility inspection");
        internal static object Inspect(CarController car)=>throw new Exception("Traffic evidence touched during visibility inspection");
    }
    public class CrewProbeVehicle { public CrewProbeVehicle(CarController car) => throw new Exception("Native wrapper constructed"); }
    internal static partial class TestBridgeCommands
    {
        internal static object Invoke(JObject args) => CrewDrive(args);
        private static string? Str(JObject args, string name) => (string?)args[name];
        private static string Need(JObject args, string name, string description) => Str(args, name) ?? throw new BridgeError(description);
        private static double? Num(JObject args, string name) => (double?)args[name];
    }
}

namespace NWH.WheelController3D {
 public class WheelController : UnityEngine.Component { public Wheel wheel = new(); }
 public class Wheel { public UnityEngine.MeshCollider? topMeshCollider, bottomMeshCollider; }
}
