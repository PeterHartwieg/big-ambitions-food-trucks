#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// All native state the adapter can change is tracked, including redundant writes.
internal static class Writes
{
    internal static int Count;
    internal static void Set<T>(ref T field, T value) { Count++; field = value; }
}
namespace UnityEngine
{
    public class Behaviour
    {
        private bool on;
        public bool enabled { get => on; set => Writes.Set(ref on, value); }
        public Transform transform = new Transform();
    }
    public class Transform
    {
        public Vector3 position, forward = new Vector3(0, 0, 1), up = new Vector3(0, 1, 0);
        public Quaternion rotation;
        public Vector3 TransformPoint(Vector3 p) => position + p;
        public Vector3 InverseTransformPoint(Vector3 p) => p - position;
    }
    public struct Quaternion { }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3();
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b, a.y*b, a.z*b);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x + a.y*b.y + a.z*b.z;
    }
    public enum RigidbodyConstraints { None, FreezeRotationX }
    public enum RigidbodyInterpolation { None, Interpolate }
    public class Rigidbody
    {
        private Vector3 linear, angular;
        private bool kinematic = true;
        private RigidbodyConstraints constraint;
        private RigidbodyInterpolation interpolate;
        public Vector3 velocity { get => linear; set => Writes.Set(ref linear, value); }
        public Vector3 angularVelocity { get => angular; set => Writes.Set(ref angular, value); }
        public bool isKinematic { get => kinematic; set => Writes.Set(ref kinematic, value); }
        public RigidbodyConstraints constraints { get => constraint; set => Writes.Set(ref constraint, value); }
        public RigidbodyInterpolation interpolation { get => interpolate; set => Writes.Set(ref interpolate, value); }
    }
    public class AnimationCurve { public float Evaluate(float v) => 1; }
    public static class Mathf
    {
        public static float Min(float a, float b) => MathF.Min(a,b);
        public static float Max(float a, float b) => MathF.Max(a,b);
        public static float Abs(float a) => MathF.Abs(a);
        public static float Clamp01(float a) => Clamp(a,0,1);
        public static float Clamp(float a, float min, float max) => Math.Clamp(a,min,max);
    }
    public class Collider { public T? GetComponentInParent<T>() where T : class => null; }
    public static class Physics
    {
        public static Collider[] OverlapBox(Vector3 p, Vector3 h, Quaternion r, int mask) => Array.Empty<Collider>();
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static MethodInfo? DeclaredMethod(Type t, string name, Type[] args) =>
            t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
    }
}
namespace NWH.VehiclePhysics2
{
    public class VehicleController : Behaviour
    {
        public bool IsInitialized = true;
        public Rigidbody vehicleRigidbody = new Rigidbody();
        public Input input = new Input();
        public Steering steering = new Steering();
        public Powertrain powertrain = new Powertrain();
    }
    public class Input
    {
        private bool automatic;
        private float throttle, brakes, steering, handbrake;
        public bool autoSetInput { get => automatic; set => Writes.Set(ref automatic,value); }
        public float Throttle { get => throttle; set => Writes.Set(ref throttle,value); }
        public float Brakes { get => brakes; set => Writes.Set(ref brakes,value); }
        public float Steering { get => steering; set => Writes.Set(ref steering,value); }
        public float Handbrake { get => handbrake; set => Writes.Set(ref handbrake,value); }
    }
    public class Steering
    {
        private float angle;
        public float externallyAddedAngle { get => angle; set => Writes.Set(ref angle,value); }
        public float maximumSteerAngle = 30;
        public AnimationCurve? speedSensitiveSteeringCurve;
    }
    public class Transmission
    {
        public int Gear { get; private set; }
        public void ShiftInto(int value) { Writes.Count++; Gear = value; }
    }
    public class Engine
    {
        private bool commanded, starter, runningCache;
        public bool ignition { get => commanded; set => Writes.Set(ref commanded,value); }
        public bool starterActive { get => starter; set => Writes.Set(ref starter,value); }
        public bool IsRunning => runningCache;
        public int Starts, Stops;
        public void StartEngine() { Starts++; ignition = true; starterActive = true; }
        public void StopEngine() { Stops++; ignition = false; } // Intentionally leaves running cache untouched.
        internal void ObserveRunning() { runningCache = true; }
        public void Step() => throw new Exception("Adapter must never step physics to refresh the engine cache");
    }
    public class Wheel : Behaviour { public float Radius = .4f; }
    public class Entry { public Wheel wheelUAPI = new Wheel(); }
    public class Powertrain
    {
        public Engine engine = new Engine();
        public Transmission transmission = new Transmission();
        public List<Entry> wheels = new List<Entry>();
    }
}
public enum ParkingState { Undefined, Legal, Illegal }
public class VehicleInstance
{
    private ParkingState parking;
    private string area = "";
    private float condition;
    public ParkingState parkingState { get => parking; set => Writes.Set(ref parking,value); }
    public string parkingNeighbourhood { get => area; set => Writes.Set(ref area,value); }
    public float damage { get => condition; set => Writes.Set(ref condition,value); }
    public float fuel = 40;
}
public class VehicleType { public float maxSpeed = 50; }
public class Fuel { public float amount = 40; }
public class CarController : Behaviour
{
    public bool controlledByPlayer, FailStart;
    public VehicleInstance vehicleInstance = new VehicleInstance();
    public NWH.VehiclePhysics2.VehicleController vehicleController = new NWH.VehiclePhysics2.VehicleController();
    public VehicleType vehicleType = new VehicleType();
    public Fuel fuelModule = new Fuel();
    public int NativeStarts, NativeStops;
    // Behavioral test doubles for native transitions, not an engine simulation.
    private void StartEngine() => Transition(true);
    private void StopEngine() => Transition(false);
    private void Transition(bool start)
    {
        if (start) { NativeStarts++; vehicleController.powertrain.engine.StartEngine(); }
        else { NativeStops++; vehicleController.powertrain.engine.StopEngine(); vehicleController.powertrain.engine.starterActive = false; }
        vehicleController.enabled = start;
        vehicleController.vehicleRigidbody.isKinematic = !start;
        if (start)
        {
            foreach (var w in vehicleController.powertrain.wheels) w.wheelUAPI.enabled = true;
            if (FailStart) throw new InvalidOperationException("injected partial native start");
        }
    }
    public float GetCurrentCondition() => .2f;
    public void SavePosition() { Writes.Count++; }
}
public class GameInstance { }
public static class SaveGameManager
{
    public static GameInstance Current = new GameInstance();
    public static void MarkChange() { Writes.Count++; }
}
public static class BuildingManager { public static bool IsInsideBuilding; }
public class ParkingLaneGenerator { public bool isHandicapParking; public string neighbourhood = ""; }
public static class LayerHelper { public static int parkingAreaLayerMask; }
namespace Helpers
{
    public static class VehicleHelper
    {
        public static CarController? Car;
        public static CarController? GetVehicleController(VehicleInstance instance) => Car;
    }
}
namespace FoodTrucks
{
    // Body certification is an independent boundary, covered by its own harness.
    internal sealed class CrewProbeBody
    {
        internal static int Constructions;
        internal CrewProbeBody(CarController car) { Constructions++; }
        internal Vector3 Min => new Vector3(-1,0,-2);
        internal Vector3 Max => new Vector3(1,3,2);
        internal float PlanarTiltPadding(float rearZ) => 0;
    }
}
