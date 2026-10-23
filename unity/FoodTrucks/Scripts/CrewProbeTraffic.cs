#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using FoodTrucks.Core.CrewDriving;
using GleyTrafficSystem;
using GleyUrbanAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // Dev-only Gate A adapter. Main thread only. Minimal patterns adapted from Driven Rides
    // 90981d0061418a4d4742792d90c62c6a686f2a80: Scripts/GleyGraph.cs (native graph),
    // GleyTraffic.cs (cached intersection binding), NwhCar.cs (visibility masks),
    // NwhPlanner.cs (nonalloc corridor queries). No native implementation is reproduced here.
    internal sealed class CrewProbeTraffic : IDisposable
    {
        private const int MaxPoints = 512, MaxIntersections = 4096, MaxPool = 1024;
        private const int MaxQueries = 256;
        private const float Margin = 0.35f, MaxLook = 180f;
        private static readonly FieldInfo? IntersectionField = Field(typeof(Waypoint), "associatedIntersection");
        private static readonly FieldInfo? MembersField = Field(typeof(GenericIntersection), "carsInIntersection");
        private static readonly FieldInfo? PoolField = Field(typeof(TrafficManager), "trafficVehicles");
        private static readonly FieldInfo? ObstaclesField = Field(typeof(VehicleComponent), "obstacleLayers");
        private static readonly FieldInfo? PlayersField = Field(typeof(VehicleComponent), "playerLayers");
        private static readonly FieldInfo? BuildingsField = Field(typeof(VehicleComponent), "buildingLayers");
        // LayerMask is a value type: FieldInfo.GetValue would box three masks per pooled car per step.
        private static readonly Func<VehicleComponent, LayerMask>? ReadObstacles = MaskReader(ObstaclesField);
        private static readonly Func<VehicleComponent, LayerMask>? ReadPlayers = MaskReader(PlayersField);
        private static readonly Func<VehicleComponent, LayerMask>? ReadBuildings = MaskReader(BuildingsField);
        private static readonly Dictionary<int, CrewProbeTraffic> Owners = new Dictionary<int, CrewProbeTraffic>();
        private static int generation;

        private sealed class Crossing
        {
            internal TrafficLightsIntersection Intersection = null!;
            internal List<int> Members = null!;
            internal int Entry, Exit, EntrySource, ExitSource;
            internal bool Entered, Cleared, Denied, LastStop;
        }

        private sealed class Visibility
        {
            internal readonly VehicleComponent Vehicle;
            internal readonly Rigidbody ExpectedBody;
            internal readonly string ModelName;
            internal readonly int ModelId, FrontId, ExpectedBodyId;
            internal readonly string[] TriggerPaths;
            internal readonly int[] TriggerIds;
            internal readonly Collider[] Triggers;
            internal readonly CrewProbeTrigger?[] Profiles;
            internal readonly int RootLayer;
            internal readonly Transform Front;
            private readonly Transform[] nodes, frontPath;
            private readonly List<Transform> traversal = new List<Transform>(128);
            private readonly List<Collider> components = new List<Collider>(17);

            internal Visibility(VehicleComponent v)
            {
                Vehicle = v; ModelName = v.name; ModelId = v.GetInstanceID();
                RootLayer = v.gameObject.layer; Front = v.frontTrigger;
                ExpectedBody = v.rb ?? throw new InvalidOperationException("gley_receiver_rigidbody_changed");
                ExpectedBodyId = ExpectedBody.GetInstanceID();
                if (Front == null) throw new InvalidOperationException("front_root_missing");
                FrontId = Front.GetInstanceID();
                var receiverError = ReceiverError();
                if (receiverError != null) throw new InvalidOperationException(receiverError);
                var path = new List<Transform>();
                for (var t = Front; t != v.transform; t = t.parent)
                {
                    if (t == null || path.Count >= 128) throw new InvalidOperationException("front_root_outside_model");
                    path.Add(t);
                }
                frontPath = path.ToArray();
                Walk(); nodes = traversal.ToArray();
                var colliders = new List<Collider>(16);
                foreach (var t in nodes)
                {
                    t.GetComponents(components);
                    if (colliders.Count + components.Count > 16) throw new InvalidOperationException("trigger_inventory_unbounded");
                    colliders.AddRange(components);
                }
                if (colliders.Count == 0) throw new InvalidOperationException("trigger_inventory_empty");
                Triggers = colliders.ToArray(); Profiles = new CrewProbeTrigger?[Triggers.Length];
                TriggerIds = new int[Triggers.Length]; TriggerPaths = new string[Triggers.Length];
                for (var i = 0; i < Triggers.Length; i++) { TriggerIds[i] = Triggers[i].GetInstanceID(); TriggerPaths[i] = PathOf(Triggers[i].transform); }
                var found = false;
                for (var i = 0; i < Triggers.Length; i++)
                    if (Triggers[i].isTrigger)
                    {
                        found = true;
                        try
                        {
                            var deliveryError = DeliveryError(Triggers[i]);
                            if (deliveryError != null) throw new InvalidOperationException(deliveryError);
                            Profiles[i] = new CrewProbeTrigger(Triggers[i], v.transform);
                        }
                        catch (Exception ex) { throw new InvalidOperationException(Detail(ex.Message, this, -1, Triggers[i], null)); }
                    }
                if (!found) throw new InvalidOperationException("trigger_inventory_no_trigger");
            }
            private string? ReceiverError() => Vehicle == null || ExpectedBody == null || Vehicle.rb != ExpectedBody ||
                Vehicle.GetComponent<Rigidbody>() != ExpectedBody || Vehicle.GetComponent<VehicleComponent>() != Vehicle
                ? "gley_receiver_rigidbody_changed" : null;
            private string? DeliveryError(Collider trigger)
            {
                if (trigger.GetComponentInParent<VehicleComponent>(true) != Vehicle) return "trigger_receiver_changed";
                if (trigger.GetComponentInParent<Rigidbody>(true) != ExpectedBody) return "trigger_rigidbody_ownership_changed";
                if (trigger.enabled && trigger.gameObject.activeInHierarchy && trigger.attachedRigidbody != ExpectedBody)
                    return "trigger_attached_rigidbody_changed";
                return null;
            }
            private void Walk()
            {
                traversal.Clear(); traversal.Add(Front);
                for (var i = 0; i < traversal.Count; i++)
                {
                    var t = traversal[i];
                    if (t == null || t.childCount > 128 - traversal.Count) throw new InvalidOperationException("trigger_hierarchy_unbounded");
                    for (var j = 0; j < t.childCount; j++) traversal.Add(t.GetChild(j));
                }
            }
            internal string? Validate(CrewProbeBody? body, int poolIndex)
            {
                if (Vehicle == null || Vehicle.frontTrigger != Front) return "front_root_replaced";
                var receiverError = ReceiverError(); if (receiverError != null) return receiverError;
                for (var i = 0; i < frontPath.Length; i++)
                    if (frontPath[i] == null || frontPath[i].parent != (i + 1 < frontPath.Length ? frontPath[i + 1] : Vehicle.transform))
                        return "front_root_reparented";
                Walk();
                if (traversal.Count != nodes.Length) return "trigger_hierarchy_membership_changed";
                var count = 0;
                for (var i = 0; i < nodes.Length; i++)
                {
                    if (traversal[i] != nodes[i]) return "trigger_hierarchy_membership_changed";
                    nodes[i].GetComponents(components);
                    if (components.Count > Triggers.Length - count) return "trigger_collider_membership_changed";
                    foreach (var c in components)
                    {
                        if (c == null || c != Triggers[count]) return "trigger_collider_membership_changed";
                        var profile = Profiles[count++];
                        if (c.isTrigger != (profile != null)) return "trigger_flag_changed";
                        var error = profile == null ? null : DeliveryError(c) ?? profile.Validate(); if (error != null) throw new InvalidOperationException(Detail(error, this, poolIndex, c, body));
                    }
                }
                return count == Triggers.Length ? null : "trigger_collider_membership_changed";
            }
        }

        private static Visibility[] CaptureVisibility(List<VehicleComponent> pool)
        {
            if (pool.Count == 0 || pool.Count > MaxPool) throw new InvalidOperationException("pool_unbounded_or_empty");
            var models = new Visibility[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                var v = pool[i];
                if (v == null) throw new InvalidOperationException($"model_missing pool={i}");
                try { models[i] = new Visibility(v); }
                catch (Exception ex) { throw new InvalidOperationException($"{ex.Message} pool={i} model={v.name}#{v.GetInstanceID()}"); }
            }
            return models;
        }

        private static string? SelectBand(CrewProbeBody body, Visibility[] models, out float lo, out float hi)
        {
            lo = float.MinValue; hi = float.MaxValue;
            var lowerModel = 0; var upperModel = 0; Collider? lower = null, upper = null;
            for (var i = 0; i < models.Length; i++)
                foreach (var profile in models[i].Profiles)
                    if (profile != null)
                    {
                        if (profile.Min > lo) { lo = profile.Min; lowerModel = i; lower = profile.Collider; }
                        if (profile.Max < hi) { hi = profile.Max; upperModel = i; upper = profile.Collider; }
                    }
            var error = body.SelectBand(lo, hi);
            if (error == null) return null;
            if (error != "common_band_empty") return error + BodyDetail(body);
            // The limiting floor and ceiling can belong to different inactive models.
            // Name both instead of blaming the first pool entry for an empty intersection.
            return error + $" common=[{lo:R},{hi:R}] lower=(" + Detail("limiting_floor", models[lowerModel], lowerModel, lower, body) +
                ") upper=(" + Detail("limiting_ceiling", models[upperModel], upperModel, upper, body) + ")";
        }

        private static string PathOf(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null && path.Length < 1024; p = p.parent) path = p.name + "/" + path;
            return path;
        }
        private static string BodyDetail(CrewProbeBody? body) =>
            $" band=[{body?.SelectedBandMin:R},{body?.SelectedBandMax:R}] candidateMin={body?.AttemptedBandMin:R} " +
            $"prismWorld=[{body?.WorldBandMin:R},{body?.WorldBandMax:R}] stage={body?.FailureStage} shell={body?.FailingShell} corner={body?.FailingCorner} " +
            $"authority={body?.FailingAuthority} failingPoint={body?.FailingPoint}";

        private static string Detail(string code, Visibility model, int index, Collider? c, CrewProbeBody? body)
        {
            // Fault diagnostics must preserve the original reason even if Unity has already
            // destroyed a captured object. Cached identities remain useful in that case.
            var colliderIndex = -1;
            if (model.Triggers != null)
                for (var i = 0; i < model.Triggers.Length; i++) if (ReferenceEquals(model.Triggers[i], c)) { colliderIndex = i; break; }
            var colliderId = colliderIndex < 0 ? 0 : model.TriggerIds[colliderIndex];
            var path = colliderIndex < 0 ? "<front root>" : model.TriggerPaths[colliderIndex];
            var prefix = $"{code} pool={index} model={model.ModelName}#{model.ModelId} front={model.FrontId} expectedBody={model.ExpectedBodyId} collider={colliderId} path={path}";
            try
            {
                var up = default(Vector3); var lo = 0f; var hi = 0f;
                if (c != null) CrewProbeTrigger.WorldInterval(c, out lo, out hi, out up);
                float? delta = body == null ? (float?)null : model.Vehicle.transform.position.y - body.RootY;
                var actualRootBody = model.Vehicle.GetComponent<Rigidbody>();
                var actualParentBody = c == null ? null : c.GetComponentInParent<Rigidbody>(true);
                var actualAttachedBody = c == null ? null : c.attachedRigidbody;
                return prefix + $" receiverRb={model.Vehicle.rb?.GetInstanceID()} rootBody={actualRootBody?.GetInstanceID()} " +
                    $"nearestBody={actualParentBody?.GetInstanceID()} attachedBody={actualAttachedBody?.GetInstanceID()} " +
                    $"modelActive={model.Vehicle.gameObject.activeInHierarchy} active={c?.gameObject.activeInHierarchy} enabled={c?.enabled} " +
                    $"up=({up.x:R},{up.y:R},{up.z:R}) tilt={Math.Atan2(Math.Sqrt(up.x * up.x + up.z * up.z), up.y):R} " +
                    $"rootHeightDelta={delta:R} safe=[{lo:R},{hi:R}]" + BodyDetail(body);
            }
            catch (Exception ex) { return prefix + " diagnostics_unavailable=" + ex.GetType().Name; }
        }

        private readonly CarController car;
        private readonly CurrentSceneData scene;
        private readonly TrafficManager manager;
        private readonly LayerSetup setup;
        private readonly List<VehicleComponent> pool;
        private readonly Visibility[] visibility;
        private readonly Waypoint[] route;
        private readonly Vector3[] points, sourcePoints;
        private readonly float[] along;
        private readonly Crossing[] crossings;
        private readonly Collider[] own;
        private readonly Collider? parkingGap;
        private readonly CrewProbeBody? bodyContract;
        private readonly Collider[] hits = new Collider[128];
        private readonly int id, carId;
        private readonly float rearZ, frontAhead, rearBehind, width, height, bottom;
        private readonly float corridorTolerance, headingCosine, headingAllowance;
        private readonly Vector3 localMin, localMax;
        private string? fault;
        private bool disposed;
        private bool removing;
        private CrewProbeTrafficHold? hold;
        private int queries;
        private int visibleLayers;
        private float measuredRear;
        private float? supportPlane;

        internal int Registrations
        {
            get { var n = 0; for (var i = 0; i < crossings.Length; i++) if (crossings[i].Entered) n++; return n; }
        }
        internal int ColliderOverrides => 0; // Gate A never changes native collider layers.
        internal int Observed { get; private set; }
        internal int? BlockerId { get; private set; }
        internal int? BlockerLayer { get; private set; }
        internal Vector3 BlockerPosition { get; private set; }
        internal string? BlockerQuery { get; private set; }
        internal float? BlockerGap { get; private set; }
        // Only value copies and the exact reference are captured in the observer. Native shape,
        // hierarchy and JSON reads belong to the explicit bridge diagnostic, never this hot path.
        internal long ObservationSequence { get; private set; }
        internal Collider? BlockerCollider { get; private set; }
        internal Vector3 BlockerQueryCenter { get; private set; }
        internal Vector3 BlockerQueryHalfExtents { get; private set; }
        internal Quaternion BlockerQueryRotation { get; private set; }
        internal float BlockerQueryGround { get; private set; }
        internal float BlockerQuerySupport { get; private set; }
        internal float BlockerQueryTopLimit { get; private set; }
        internal float? BlockerQueryIgnoreBehind { get; private set; }
        internal CarController EvidenceCar => car;
        internal Vector3 PhysicalLocalMin => localMin;
        internal Vector3 PhysicalLocalMax => localMax;
        internal bool ObserverDisposed => disposed;
        internal int SignalStopChanges { get; private set; }
        internal float? SignalChangeSpeed { get; private set; }
        internal float? SignalChangeClearance { get; private set; }
        internal float ObservationMilliseconds { get; private set; }
        // Dispose can transfer responsibility to an explicit retained-body record. It never lies about zero leaks.
        internal bool Retained => disposed && Owners.TryGetValue(carId, out var owner) && owner == this;
        internal int NativeTrafficId => id;
        internal string? Failure => fault;
        internal static int RecordCount => Owners.Count;

        internal static void NativeRemoving(CarController car)
        {
            if (!Owners.TryGetValue(car.GetInstanceID(), out var record)) return;
            record.removing = true;
            record.Retire();
        }

        private static FieldInfo? Field(Type t, string name) => t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static Func<VehicleComponent, LayerMask>? MaskReader(FieldInfo? field)
        {
            if (field == null || field.FieldType != typeof(LayerMask)) return null;
            try
            {
                var parameter = Expression.Parameter(typeof(VehicleComponent));
                return Expression.Lambda<Func<VehicleComponent, LayerMask>>(Expression.Field(parameter, field), parameter).Compile();
            }
            catch { return null; } // Unsupported runtime binding refuses admission, never an allocating fallback.
        }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        private CrewProbeTraffic(CarController car, CurrentSceneData scene, LayerSetup setup,
            List<VehicleComponent> pool, Visibility[] visibility, int visibleLayers, Waypoint[] route, Vector3[] points, float[] along,
            Crossing[] crossings, Collider[] own, Collider? parkingGap, Vector3[] sourcePoints,
            int id, float rearZ, float frontAhead, float rearBehind, float width, Vector3 min, Vector3 max, CrewProbeBody? bodyContract)
        {
            this.bodyContract = bodyContract;
            corridorTolerance = bodyContract == null ? .5f : .2f;
            headingAllowance = bodyContract == null ? .35f : .05f;
            headingCosine = Mathf.Cos(headingAllowance);
            this.car = car; this.scene = scene; this.setup = setup; this.pool = pool;
            this.visibility = visibility; this.visibleLayers = visibleLayers;
            manager = TrafficManager.Instance; this.route = route; this.points = points; this.along = along;
            this.crossings = crossings; this.own = own; this.parkingGap = parkingGap; this.sourcePoints = sourcePoints;
            this.id = id; carId = car.GetInstanceID(); this.rearZ = rearZ; this.frontAhead = frontAhead;
            this.rearBehind = rearBehind; this.width = width; localMin = min; localMax = max;
            height = max.y - min.y; bottom = min.y;
        }

        internal static bool TryCreate(CarController car, int[] waypointIds, float rearZ, float frontAhead,
            float rearBehind, float width, out CrewProbeTraffic? traffic, out Vector3[] points, out string why,
            Func<Vector3[], ProbeRoadPath>? prepareBeforeAdmission = null, CrewProbeBody? bodyContract = null)
        {
            traffic = null; points = Array.Empty<Vector3>(); why = "";
            CrewProbeTraffic? candidate = null;
            try
            {
                if (car == null || !car.gameObject.activeInHierarchy || Owners.ContainsKey(car.GetInstanceID()))
                    throw new InvalidOperationException("missing, inactive or already owned/retained native car");
                if (waypointIds == null || waypointIds.Length < 2 || waypointIds.Length > MaxPoints)
                    throw new InvalidOperationException("route needs 2..512 native waypoints");
                if (!Finite(rearZ) || !Finite(frontAhead) || !Finite(rearBehind) || !Finite(width) ||
                    Mathf.Abs(rearZ) > 20f || frontAhead < 1f || frontAhead > 20f || rearBehind < 0f || rearBehind > 10f || width < 0.5f || width > 5f ||
                    (car.transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
                    throw new InvalidOperationException("invalid measured truck envelope");
                if (IntersectionField == null || MembersField == null || PoolField == null || ObstaclesField == null ||
                    ReadObstacles == null || ReadPlayers == null || ReadBuildings == null || !TrafficManager.HasInstance || !TrafficManager.IsInitialized)
                    throw new InvalidOperationException("required Gley bindings/initialization missing");
                // FindObjectOfType only at admission; do not use GetSceneInstance (it can create a scene).
                var scene = Object.FindObjectOfType<CurrentSceneData>();
                var setup = Resources.Load<LayerSetup>("LayerSetupData");
                var pool = (PoolField.GetValue(TrafficManager.Instance) as TrafficVehicles)?.GetVehicleList();
                if (scene == null || scene.allWaypoints == null || setup == null || pool == null || pool.Count == 0 || pool.Count > MaxPool)
                    throw new InvalidOperationException("native scene, layer setup or bounded initialized Gley pool unavailable");
                var visibility = CaptureVisibility(pool);
                if (bodyContract != null)
                {
                    var selectionError = SelectBand(bodyContract, visibility, out _, out _);
                    if (selectionError != null) throw new InvalidOperationException(selectionError);
                }
                var visibleLayers = ReadVisibleLayers(setup, pool, visibility, out var rigidbodyLayers, bodyContract);
                var route = new Waypoint[waypointIds.Length];
                var path = new Vector3[route.Length];
                var distances = new float[route.Length];
                var seen = new HashSet<int>();
                var crossings = new List<Crossing>();
                Crossing? open = null;
                for (var i = 0; i < route.Length; i++)
                {
                    var index = waypointIds[i];
                    if (index < 0 || index >= scene.allWaypoints.Length || !seen.Add(index))
                        throw new InvalidOperationException("invalid/repeated native waypoint");
                    var w = scene.allWaypoints[index];
                    if (w == null || w.listIndex != index || !Finite(w.position) || w.temporaryDisabled || w.giveWay)
                        throw new InvalidOperationException("missing/disabled waypoint or unsupported give-way");
                    route[i] = w; path[i] = w.position;
                    if (i > 0)
                    {
                        if (route[i - 1].neighbors == null || !route[i - 1].neighbors.Contains(index))
                            throw new InvalidOperationException("route is not a connected directed native chain");
                        var delta = path[i] - path[i - 1];
                        var length = Flat(delta).magnitude;
                        if (length < 0.25f || length > 30f || Mathf.Abs(delta.y) > 0.05f || Mathf.Abs(path[i].y - path[0].y) > 0.1f)
                            throw new InvalidOperationException("native segment outside 0.25..30 m / near-level probe route");
                        distances[i] = distances[i - 1] + length;
                        if (distances[i] > 2000f) throw new InvalidOperationException("route exceeds 2000 m");
                        if (i > 1 && Vector3.Dot(Flat(path[i - 1] - path[i - 2]).normalized, Flat(delta).normalized) < 0f)
                            throw new InvalidOperationException("turn exceeds 90 degrees");
                    }
                    var x = IntersectionField.GetValue(w) as IIntersection;
                    if (i == 0 || i == route.Length - 1)
                        if (x != null || !TrafficManager.IsNotIntersection(w))
                            throw new InvalidOperationException("route starts/ends in a junction");
                    if (w.enter)
                    {
                        if (open != null || w.exit || !(x is TrafficLightsIntersection lights) ||
                            !(MembersField.GetValue(lights) is List<int> members) || !OnLightRoad(lights, index))
                            throw new InvalidOperationException("unsupported/uninitialized or overlapping junction entry");
                        foreach (var previous in crossings)
                            if (ReferenceEquals(previous.Intersection, lights)) throw new InvalidOperationException("repeated intersection unsupported");
                        open = new Crossing { Intersection = lights, Members = members, Entry = i, Denied = w.stop, LastStop = w.stop };
                    }
                    else if (w.exit)
                    {
                        if (open == null || !ReferenceEquals(open.Intersection, x) || !open.Intersection.exitWaypoints.Contains(index))
                            throw new InvalidOperationException("junction exit has no matching entry");
                        open.Exit = i; crossings.Add(open); open = null;
                    }
                    else if (x != null || w.stop || (!TrafficManager.IsNotIntersection(w) && open == null))
                        throw new InvalidOperationException("unsupported signal or unbounded junction interior");
                }
                if (open != null) throw new InvalidOperationException("junction entry without exit");
                foreach (var c in crossings)
                    if (distances[c.Entry] <= frontAhead + Margin || distances[route.Length - 1] - distances[c.Exit] <= rearBehind + frontAhead + 1f)
                        throw new InvalidOperationException("insufficient start/end body clearance from junction");

                // Collision-check our negative namespace against ALL native membership lists, not just this route.
                var all = new List<GenericIntersection>();
                if (scene.allLightsIntersections == null || scene.allPriorityIntersections == null ||
                    scene.allLightsIntersections.Length + scene.allPriorityIntersections.Length > MaxIntersections)
                    throw new InvalidOperationException("native intersection inventory unavailable/unbounded");
                foreach (var x in scene.allLightsIntersections) if (x != null) all.Add(x);
                foreach (var x in scene.allPriorityIntersections) if (x != null) all.Add(x);
                foreach (var c in crossings) if (!all.Contains(c.Intersection)) throw new InvalidOperationException("junction absent from native inventory");
                if (generation >= 100000000) throw new InvalidOperationException("traffic generation exhausted");
                var id = -1700000000 - ++generation; // Never -1; native lists store arbitrary ints without indexing them.
                foreach (var x in all)
                    if (!(MembersField.GetValue(x) is List<int> members) || members.Count > MaxPool + MaxPoints || members.Contains(id))
                        throw new InvalidOperationException("native membership missing, unbounded or traffic ID collision");

                var bodyError = bodyContract?.Validate();
                if (bodyError != null) throw new InvalidOperationException(bodyError);
                var gap = bodyContract?.ParkingGap ?? car.GetComponentInChildren<VehicleParkingHelper>(true)?.parallelParkingCollider;
                var colliders = bodyContract?.Own ?? Array.FindAll(car.GetComponentsInChildren<Collider>(true), c => c != gap);
                if (colliders.Length == 0 || colliders.Length > 128) throw new InvalidOperationException("missing/unbounded truck colliders");
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = -min;
                var solids = 0;
                foreach (var collider in colliders)
                {
                    if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
                    if (!Finite(collider.bounds.center) || !Finite(collider.bounds.extents)) throw new InvalidOperationException("invalid collider bounds");
                    solids++;
                    // Measure shape-local bounds, independent of the truck's world heading. Unknown shapes
                    // are rejected instead of deriving inflated dimensions from rotated world AABBs.
                    Bounds b;
                    if (collider is BoxCollider box) b = new Bounds(box.center, box.size);
                    else if (collider is MeshCollider mesh && mesh.sharedMesh != null) b = mesh.sharedMesh.bounds;
                    else if (collider is SphereCollider sphere) b = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    else if (collider is CapsuleCollider capsule)
                    {
                        var size = Vector3.one * (capsule.radius * 2f); size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                        b = new Bounds(capsule.center, size);
                    }
                    else throw new InvalidOperationException("unsupported native body collider shape");
                    for (var k = 0; k < 8; k++)
                    {
                        var p = car.transform.InverseTransformPoint(collider.transform.TransformPoint(b.center + Vector3.Scale(b.extents, Signs(k))));
                        min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    }
                }
                if (bodyContract != null) { min = bodyContract.Min; max = bodyContract.Max; }
                if (solids == 0 || max.y - min.y > 8f || max.y - min.y < 0.2f ||
                    max.z - rearZ > frontAhead + 0.1f || rearZ - min.z > rearBehind + 0.1f ||
                    Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)) * 2f > width + 0.1f)
                    throw new InvalidOperationException("supplied dimensions do not enclose actual native colliders");
                ValidateBodyVisibility(car, colliders, bodyContract, visibleLayers, rigidbodyLayers, visibility);
                var sourcePoints = path;
                // One immutable prepared curve drives Core, projection, obstacle sweeps and marker distances.
                // The native points/tangents remain separate for mutation checks and physical signal planes.
                var prepared = prepareBeforeAdmission?.Invoke((Vector3[])sourcePoints.Clone());
                var markers = prepared?.SourcePointIndices;
                if (prepared != null)
                {
                    var samples = prepared.Points; var parameters = prepared.SourceParameters;
                    path = new Vector3[samples.Length]; distances = new float[samples.Length];
                    for (var i = 0; i < samples.Length; i++)
                    {
                        var segment = Math.Min(sourcePoints.Length - 2, (int)parameters[i]);
                        var y = Mathf.LerpUnclamped(sourcePoints[segment].y, sourcePoints[segment + 1].y, (float)(parameters[i] - segment));
                        path[i] = new Vector3((float)samples[i].X, y, (float)samples[i].Z);
                        if (i > 0) distances[i] = distances[i - 1] + Flat(path[i] - path[i - 1]).magnitude;
                    }
                }
                if (bodyContract != null)
                {
                    var heading = Flat(path[1] - path[0]).normalized;
                    for (var i = 1; i < path.Length; i++)
                        if (Vector3.Dot(heading, Flat(path[i] - path[i - 1]).normalized) < .99995f)
                            throw new InvalidOperationException("certified straight profile refuses prepared curve heading");
                }
                foreach (var c in crossings)
                {
                    c.EntrySource = c.Entry; c.ExitSource = c.Exit;
                    if (markers != null) { c.Entry = markers[c.Entry]; c.Exit = markers[c.Exit]; }
                }
                candidate = new CrewProbeTraffic(car, scene, setup, pool, visibility, visibleLayers, route, path, distances, crossings.ToArray(),
                    colliders, gap, sourcePoints, id, rearZ, frontAhead, rearBehind, width, min, max, bodyContract);
                if (!candidate.Measure(out var rear, out _, out _, out var measureWhy) || Mathf.Abs(rear) > 1f || measureWhy != null)
                    throw new InvalidOperationException(measureWhy ?? "rear axle must begin within 1 m of route start");
                candidate.measuredRear = rear;
                foreach (var c in candidate.crossings)
                    if (candidate.BodyPlane(c.EntrySource, true) >= -Margin) throw new InvalidOperationException("actual front already at/past junction entry");
                // Everything above is read-only. Publish mutation/lifetime only after complete admission validation.
                Owners.Add(candidate.carId, candidate);
                traffic = candidate;
                points = (Vector3[])path.Clone(); // Caller cannot mutate the observer's committed geometry.
                return true;
            }
            catch (Exception ex)
            {
                if (candidate != null) candidate.RollbackAdmission();
                why = "traffic admission: " + ex.Message;
                return false;
            }
        }

        private static Vector3 Signs(int i) => new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
        private static bool OnLightRoad(TrafficLightsIntersection light, int id)
        {
            if (light.stopWaypoints == null || light.exitWaypoints == null || light.exitWaypoints.Count == 0) return false;
            foreach (var road in light.stopWaypoints) if (road?.roadWaypoints != null && road.roadWaypoints.Contains(id)) return true;
            return false;
        }

        // Check actual initialized pool masks AND the setup for future pooled cars. Never pretend that
        // Gley sees NWH merely because both have colliders. No global collision matrix/mask mutation.
        private static int ReadVisibleLayers(LayerSetup setup, List<VehicleComponent> pool, Visibility[] visibility,
            out int rigidbodyLayers, CrewProbeBody? body = null)
        {
            if (setup == null || pool.Count != visibility.Length) throw new InvalidOperationException("native visibility inventory changed");
            rigidbodyLayers = setup.trafficLayers.value;
            var mask = ((setup.obstaclesLayers.value | setup.playerLayers.value) & ~setup.buildingsLayers.value) | rigidbodyLayers;
            for (var i = 0; i < pool.Count; i++)
            {
                var v = pool[i];
                if (v == null || v != visibility[i].Vehicle || v.gameObject.layer != visibility[i].RootLayer)
                    throw new InvalidOperationException(Detail("native Gley model/layer replaced", visibility[i], i, null, body));
                var inventoryError = visibility[i].Validate(body, i);
                if (inventoryError != null) throw new InvalidOperationException(Detail(inventoryError, visibility[i], i, null, body));
                // Native OnTriggerEnter explicitly handles a SAME-layer non-Gley rigidbody as a dynamic
                // obstacle (StopInDistance); it does not assign it a pool ID. Require that rigidbody below.
                // Root recognition precedes buildings, which precede obstacles and players. Building
                // recognition is excluded because native BuildingInteraction is AvoidReverse.
                var rootLayerBit = 1 << v.gameObject.layer;
                rigidbodyLayers |= rootLayerBit;
                mask &= ((ReadObstacles!(v).value | ReadPlayers!(v).value) & ~ReadBuildings!(v).value) | rootLayerBit;
                // Trigger colliders can live on child layers, independently of the Gley root.
                foreach (var trigger in visibility[i].Triggers)
                {
                    if (trigger == null) throw new InvalidOperationException("native Gley trigger removed");
                    if (!trigger.isTrigger) continue;
                    for (var layer = 0; layer < 32; layer++)
                        if ((mask & (1 << layer)) != 0 && Physics.GetIgnoreLayerCollision(trigger.gameObject.layer, layer)) mask &= ~(1 << layer);
                }
            }
            return mask;
        }

        // A certificate exempts only exact NWH wheel references; every other solid keeps native
        // visibility requirements. Default overrides avoid reimplementing Unity's priority resolver.
        private static void ValidateBodyVisibility(CarController car, Collider[] colliders, CrewProbeBody? contract,
            int visible, int rigidbodyLayers, Visibility[] models)
        {
            if (contract != null)
            {
                if (!contract.TrafficCertified) throw new InvalidOperationException("traffic_band_not_selected");
                contract.ClearFailure();
                contract.UpdateWorldBand(); // Once per Observe, outside both trigger and solid-pair loops.
                for (var m = 0; m < models.Length; m++)
                {
                    var model = models[m]; var usable = false;
                    foreach (var trigger in model.Triggers)
                    {
                        if (!trigger.isTrigger) continue;
                        if (trigger.includeLayers.value != 0 || trigger.excludeLayers.value != 0 || trigger.layerOverridePriority != 0)
                            throw new InvalidOperationException(Detail("unsupported Gley trigger layer overrides", model, m, trigger, contract));
                        var rb = model.ExpectedBody;
                        if (rb == null || !rb.detectCollisions || rb.includeLayers.value != 0 || rb.excludeLayers.value != 0)
                            throw new InvalidOperationException(Detail("Gley trigger rigidbody collision settings unavailable/unsupported", model, m, trigger, contract));
                        if (!trigger.enabled || !trigger.gameObject.activeInHierarchy) continue;
                        usable = true;
                        // Far parked pool transforms cannot establish a road-height guarantee.
                        // Every nearby live instance must obey the explicit restricted domain.
                        if (!CrewProbeTrigger.Nearby(trigger, car.transform.position)) continue;
                        var reason = CrewProbeTrigger.WorldInterval(trigger, out var lo, out var hi, out _);
                        if (reason == null && Math.Abs(model.Vehicle.transform.position.y - car.transform.position.y) > .05f)
                            reason = "trigger_root_height_offset";
                        if (reason == null && !contract.CoversInterval(lo, hi))
                        { contract.RecordCoverageFailure(lo, hi); reason = "trigger_world_height_coverage"; }
                        if (reason != null) throw new InvalidOperationException(Detail(reason, model, m, trigger, contract));
                    }
                    if (model.Vehicle.gameObject.activeInHierarchy && !usable)
                        throw new InvalidOperationException(Detail("active Gley model has no usable front trigger", model, m, null, contract));
                }
            }
            foreach (var collider in colliders)
            {
                if (collider == null) throw new InvalidOperationException("native collider removed");
                if (collider.isTrigger || collider == contract?.ParkingGap) continue;
                if (contract == null && (!collider.enabled || !collider.gameObject.activeInHierarchy)) continue;
                if (contract != null && contract.TrafficCertified && contract.IsWheel(collider)) continue;
                var bit = 1 << collider.gameObject.layer;
                if ((visible & bit) == 0 || ((rigidbodyLayers & bit) != 0 && collider.enabled && collider.attachedRigidbody == null))
                    throw new InvalidOperationException($"native obstacle visibility changed/invisible: {collider.GetType().Name} #{collider.GetInstanceID()}, layer {collider.gameObject.layer}");
                if (contract == null) continue;
                foreach (var model in models)
                    foreach (var trigger in model.Triggers)
                        if (trigger.isTrigger && Physics.GetIgnoreCollision(trigger, collider))
                            throw new InvalidOperationException("native body/trigger pair ignores collision");
            }
        }

        // Read-only inspection shares admission inventory, receiver ownership, band selection
        // and height coverage. Native visibility masks are deliberately not checked here;
        // masks_checked is explicit. No traffic record or engine/controller is acquired.
        internal static object InspectCoverage(CarController car)
        {
            CrewProbeBody? body = null; Visibility[]? models = null;
            var reports = new List<object>(); var complete = false; string? failure = null;
            float lo = 0, hi = 0;
            try
            {
                body = new CrewProbeBody(car);
                if (!TrafficManager.HasInstance || !TrafficManager.IsInitialized) throw new InvalidOperationException("pool_unavailable");
                var pool = (PoolField?.GetValue(TrafficManager.Instance) as TrafficVehicles)?.GetVehicleList();
                if (pool == null) throw new InvalidOperationException("pool_unavailable");
                models = CaptureVisibility(pool); complete = true;
                failure = SelectBand(body, models, out lo, out hi);
                if (failure == null)
                {
                    try { ValidateBodyVisibility(car, body.Own, body, -1, 0, models); }
                    catch (Exception ex) { failure = ex.Message; }
                }
                for (var i = 0; i < models.Length; i++)
                    for (var j = 0; j < models[i].Triggers.Length; j++)
                    {
                        var c = models[i].Triggers[j]; var profile = models[i].Profiles[j];
                        var error = CrewProbeTrigger.WorldInterval(c, out var bottom, out var top, out var up);
                        var nearby = CrewProbeTrigger.Nearby(c, car.transform.position);
                        var rootDelta = models[i].Vehicle.transform.position.y - car.transform.position.y;
                        var covers = error == null && body.CoversInterval(bottom, top);
                        var coverageReason = !c.isTrigger ? "not_trigger" : !c.enabled ? "trigger_disabled" :
                            !c.gameObject.activeInHierarchy ? "trigger_inactive" : error ??
                            (Math.Abs(rootDelta) > .05f ? "trigger_root_height_offset" : !covers ? "trigger_world_height_coverage" : null);
                        reports.Add(new { pool_index = i, model = models[i].Vehicle.name, model_id = models[i].Vehicle.GetInstanceID(),
                            front_id = models[i].Front.GetInstanceID(), model_active = models[i].Vehicle.gameObject.activeInHierarchy,
                            collider_id = c.GetInstanceID(), path = PathOf(c.transform), profile_min = profile?.Min, profile_max = profile?.Max, enabled = c.enabled, active = c.gameObject.activeInHierarchy,
                            world_up = new[] { up.x, up.y, up.z }, tilt = Math.Atan2(Math.Sqrt(up.x * up.x + up.z * up.z), up.y),
                            root_height_delta = rootDelta,
                            nearby, safe_min = bottom, safe_max = top, geometry_reason = error, coverage_reason = coverageReason,
                            covers_prisms = covers, live_coverage_valid = coverageReason == null });
                    }
            }
            catch (Exception ex) { failure = ex.Message; }
            var failureCode = failure == null ? null : failure.Split(new[] { ' ', '(' }, 2)[0];
            return new { read_only = true, complete, selected = body?.TrafficCertified ?? false,
                failure = failure == null ? null : new { code = failureCode, detail = failure }, native_stopping_verified = false, masks_checked = false,
                root_height_limit = .05f, upright_tolerance = CrewProbeTrigger.UprightTolerance,
                common_min = lo, common_max = hi, band_min = body?.SelectedBandMin, band_max = body?.SelectedBandMax, attempted_band_min = body?.AttemptedBandMin,
                prism_world_min = body?.WorldBandMin, prism_world_max = body?.WorldBandMax,
                candidates = body?.BandCandidates, cooked_queries = body?.CookedQueries,
                failure_stage = failure == null ? null : body?.FailureStage,
                failing_shell = failure != null && body != null && body.FailingShell >= 0 ? body.FailingShell : (int?)null,
                failing_corner = failure != null && body != null && body.FailingCorner >= 0 ? body.FailingCorner : (int?)null,
                failing_authority = failure != null && body != null && body.FailingAuthority != 0 ? body.FailingAuthority : (int?)null,
                failing_point = failure != null && body != null && body.FailureStage != null && body.FailingCorner >= 0
                    ? new[] { body.FailingPoint.x, body.FailingPoint.y, body.FailingPoint.z } : null,
                nearby_coverage = reports };
        }

        internal void Observe(float rearAlong, float speedMs, out float? stopAlong, out float? gapAhead, out string? failure)
        {
            var started = Stopwatch.GetTimestamp();
            stopAlong = null; gapAhead = null; failure = null; Observed = 0; queries = 0;
            BlockerId = BlockerLayer = null; BlockerQuery = null; BlockerGap = null; BlockerPosition = Vector3.zero;
            ObservationSequence++;
            BlockerCollider = null; BlockerQueryCenter = BlockerQueryHalfExtents = Vector3.zero;
            BlockerQueryRotation = default; BlockerQueryGround = BlockerQuerySupport = BlockerQueryTopLimit = 0f;
            BlockerQueryIgnoreBehind = null;
            try
            {
                if (disposed) { failure = "traffic observer disposed"; stopAlong = 0f; return; }
                if (car == null || scene == null || manager == null || !TrafficManager.HasInstance ||
                    TrafficManager.Instance != manager || !TrafficManager.IsInitialized || !Owners.TryGetValue(carId, out var owner) || owner != this)
                    throw new InvalidOperationException("native traffic/controller lifetime changed");
                if (!Finite(rearAlong) || !Finite(speedMs) || speedMs < 0f || speedMs > 40f)
                    throw new InvalidOperationException("invalid progress/speed observation");
                var measured = Measure(out var actualRear, out var actualFront, out var actualBack, out var why);
                // Junction checks run BEFORE any supplied progress clamp or fault return. Even an illegal
                // crossing must remain represented until the rear clears; a later green cannot erase it.
                foreach (var c in crossings)
                {
                    var stopped = route[c.EntrySource].stop;
                    if (!c.LastStop && stopped && !c.Entered && !c.Cleared)
                    {
                        SignalStopChanges++; SignalChangeSpeed = speedMs;
                        SignalChangeClearance = -BodyPlane(c.EntrySource, true);
                    }
                    c.LastStop = stopped;
                }
                UpdateCrossings(actualRear);
                if (!measured) throw new InvalidOperationException(why);
                if (fault != null) throw new InvalidOperationException(fault);
                if (Mathf.Abs(actualRear - measuredRear) > 5f) throw new InvalidOperationException("native body moved more than observer step budget");
                measuredRear = actualRear;
                if (Mathf.Abs(actualRear - rearAlong) > 2f)
                    throw new InvalidOperationException("reported progress differs from measured rear axle");
                for (var i = 0; i < route.Length; i++)
                    if (route[i].temporaryDisabled || scene.allWaypoints[route[i].listIndex] != route[i] || route[i].position != sourcePoints[i])
                        throw new InvalidOperationException("committed native route changed");
                if (pool.Count == 0 || pool.Count > MaxPool || (PoolField!.GetValue(manager) as TrafficVehicles)?.GetVehicleList() != pool)
                    throw new InvalidOperationException("native traffic pool replaced/unbounded");
                visibleLayers = ReadVisibleLayers(setup, pool, visibility, out var rigidbodyLayers, bodyContract);
                var bodyError = bodyContract?.Validate();
                if (bodyError != null) throw new InvalidOperationException(bodyError);
                ValidateBodyVisibility(car, own, bodyContract, visibleLayers, rigidbodyLayers, visibility);
                supportPlane = null;
                if (bodyContract != null)
                {
                    if (!bodyContract.TryGround(out var support)) throw new InvalidOperationException("level native wheel support unavailable");
                    supportPlane = support;
                }
                foreach (var c in crossings)
                    if (!c.Entered && !c.Cleared && route[c.EntrySource].stop)
                    {
                        Min(ref stopAlong, along[c.Entry] - Margin);
                        // Close to the marker, preserve the ORIGINAL native plane as an additional
                        // conservative front gap. Smoothing must never move a traffic-light boundary.
                        if (actualRear + frontAhead + width + 5f >= along[c.Entry])
                            Min(ref gapAhead, -BodyPlane(c.EntrySource, true) - Margin);
                    }
                // The Core path endpoint is its terminal boundary. Returning it here as an earlier
                // signal boundary would leave Core Holding forever instead of completing the probe.
                // Conservative 1 m/s² braking + one second reaction + full body. If this budget cannot
                // cover it, hold rather than silently shorten observation at higher speeds.
                var look = speedMs * speedMs / 2f + speedMs + frontAhead + rearBehind + 5f;
                if (look > MaxLook) throw new InvalidOperationException("braking horizon exceeds bounded observer");
                var end = Mathf.Min(along[along.Length - 1], actualFront + Mathf.Max(15f, look));
                Sweep(actualRear, actualFront, actualBack, end, ref stopAlong, ref gapAhead);
            }
            catch (Exception ex)
            {
                fault = fault ?? ex.Message; failure = fault;
                // Fail closed: current front boundary, so the caller requests an immediate physical hold.
                stopAlong = Finite(rearAlong) ? rearAlong + frontAhead : 0f; gapAhead = 0f;
            }
            finally { ObservationMilliseconds = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
        }

        private static void Min(ref float? value, float candidate) { if (!value.HasValue || candidate < value.Value) value = candidate; }

        private void UpdateCrossings(float actualRear)
        {
            foreach (var c in crossings)
            {
                if (c.Cleared) continue;
                ReconcileRegistry(c);
                var front = BodyPlane(c.EntrySource, true);
                // A distant curved route may cross an infinite entry plane long before reaching that
                // junction. Use independently measured route locality, never caller-clamped progress.
                if (!c.Entered && actualRear + frontAhead + width >= along[c.Entry] && front >= 0f)
                {
                    // Lights inherit GenericIntersection.IsPathFree=false. Do NOT call CanChange here.
                    // Native WaypointManager.TrafficLightChanged updates stop (red AND amber).
                    if (c.Denied || route[c.EntrySource].stop) fault = "measured front crossed forbidden signal boundary";
                    if (c.Members.Contains(id)) throw new InvalidOperationException("traffic identity membership collision");
                    c.Intersection.VehicleEnter(id); c.Entered = true;
                    if (!c.Members.Contains(id)) throw new InvalidOperationException("native junction registration failed");
                }
                if (c.Entered)
                {
                    // Minimum support of the WHOLE measured body past exit plane, not rear-axle progress.
                    if (actualRear - rearBehind > along[c.Exit] && BodyPlane(c.ExitSource, false) > Margin)
                    {
                        c.Intersection.VehicleLeft(id); c.Entered = false; c.Cleared = true;
                    }
                }
                else c.Denied = route[c.EntrySource].stop;
            }
        }

        private void ReconcileRegistry(Crossing c)
        {
            if (!(MembersField!.GetValue(c.Intersection) is List<int> current) || current.Count > MaxPool + MaxPoints)
                throw new InvalidOperationException("native junction registry unavailable/unbounded");
            if (!ReferenceEquals(current, c.Members))
            {
                if (c.Entered) c.Members.Remove(id);
                c.Members = current;
                fault = fault ?? "native junction registry reset";
            }
            if (c.Entered && !c.Members.Contains(id))
            {
                // A native reset must not erase a body that still blocks the junction. Re-establish
                // only our explicit identity, then fail the drive; the retained-body record owns it.
                c.Intersection.VehicleEnter(id);
                if (!c.Members.Contains(id)) throw new InvalidOperationException("native junction registration repair failed");
                fault = fault ?? "native junction registration lost";
            }
        }

        private float BodyPlane(int index, bool maximum)
        {
            var tangent = Flat(sourcePoints[index] - sourcePoints[index - 1]).normalized;
            var result = maximum ? float.MinValue : float.MaxValue;
            for (var k = 0; k < 8; k++)
            {
                var local = new Vector3((k & 1) == 0 ? localMin.x : localMax.x,
                    (k & 2) == 0 ? localMin.y : localMax.y, (k & 4) == 0 ? localMin.z : localMax.z);
                var d = Vector3.Dot(car.transform.TransformPoint(local) - sourcePoints[index], tangent);
                result = maximum ? Mathf.Max(result, d) : Mathf.Min(result, d);
            }
            return result;
        }

        private bool Measure(out float rear, out float front, out float back, out string? why)
        {
            rear = front = back = 0f; why = null;
            if (car == null || !Finite(car.transform.position) || !Finite(car.transform.forward)) { why = "invalid native body pose"; return false; }
            var axle = car.transform.TransformPoint(new Vector3(0, 0, rearZ));
            rear = Project(axle, out var segment, out var lateral);
            if (lateral > corridorTolerance || Vector3.Dot(Flat(car.transform.forward).normalized, Flat(points[segment + 1] - points[segment]).normalized) < headingCosine)
            { why = "native body outside route corridor/alignment"; return false; }
            front = float.MinValue; back = float.MaxValue;
            // Independent physical support, including overhang and sideways body extent through turns.
            for (var k = 0; k < 8; k++)
            {
                var local = new Vector3((k & 1) == 0 ? localMin.x : localMax.x,
                    (k & 2) == 0 ? localMin.y : localMax.y, (k & 4) == 0 ? localMin.z : localMax.z);
                var coordinate = Project(car.transform.TransformPoint(local), out _, out _);
                front = Mathf.Max(front, coordinate); back = Mathf.Min(back, coordinate);
            }
            return true;
        }

        // Projection deliberately extrapolates first/last segments. Never hide actual overshoot by clamping.
        private float Project(Vector3 p, out int segment, out float lateral)
        {
            var best = float.MaxValue; var result = 0f; segment = 0;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var d = Flat(points[i + 1] - points[i]); var length = along[i + 1] - along[i];
                var t = Vector3.Dot(Flat(p - points[i]), d) / (length * length);
                if (i != 0) t = Mathf.Max(0f, t);
                if (i != points.Length - 2) t = Mathf.Min(1f, t);
                var distance = Flat(p - (points[i] + d * t)).sqrMagnitude;
                if (distance < best) { best = distance; result = along[i] + t * length; segment = i; }
            }
            lateral = Mathf.Sqrt(best); return result;
        }

        private void Sweep(float rear, float front, float back, float end, ref float? stop, ref float? gap)
        {
            // Extra forward/piece-length and corner-radius clearance; lateral coverage
            // uses the combined physical and uncertainty envelope below directly.
            const float extraClearance = .10f;
            // A full envelope along each segment. Dense prepared turns fit inside the oriented
            // envelope; only a corner over 0.1 rad needs the wider fallback square.
            // This intentionally overbounds turning/offtracking; interpolated narrow boxes alone miss the
            // front overhang swinging across a polyline corner. Stationary interpretation of ALL obstacles.
            var overhang = Mathf.Max(frontAhead, rearBehind);
            var radius = Mathf.Sqrt(overhang * overhang + width * width / 4f) + extraClearance + corridorTolerance;
            // Width/front/rear include the certificate's planar tilt allowance. Together
            // with corridor tracking and heading/chord allowances this is the full lateral
            // envelope; no additional side-clearance term is needed.
            var chordAllowance = bodyContract == null ? .1f : .02f;
            var sweepHalfWidth = width / 2f + corridorTolerance + overhang * Mathf.Sin(headingAllowance + chordAllowance);
            var y = car.transform.TransformPoint(new Vector3(0, 0, rearZ)).y + bottom + height / 2f;
            var sweepHalfHeight = height / 2f + .1f + (bodyContract?.VerticalTiltPadding(rearZ) ?? 0f);
            var actualCentre = car.transform.TransformPoint((localMin + localMax) / 2f);
            if (Blocked(actualCentre, (localMax - localMin) / 2f + Vector3.one * 0.1f, car.transform.rotation, points[0].y, "body"))
            { BlockerGap = 0f; Min(ref gap, 0f); return; }
            Project(car.transform.TransformPoint(new Vector3(0, 0, rearZ)), out var currentSegment, out _);
            var fraction = (rear - along[currentSegment]) / (along[currentSegment + 1] - along[currentSegment]);
            var referenceY = Mathf.LerpUnclamped(points[currentSegment].y, points[currentSegment + 1].y, fraction);
            for (var i = 0; i < points.Length - 1; i++)
            {
                if (along[i + 1] < back || along[i] > end) continue;
                var lo = Mathf.Max(along[i], rear); var hi = Mathf.Min(along[i + 1], end);
                if (hi < lo) continue;
                var tangent = Flat(points[i + 1] - points[i]).normalized;
                var a = points[i] + tangent * (lo - along[i]); var b = points[i] + tangent * (hi - along[i]);
                a.y = Mathf.LerpUnclamped(points[i].y, points[i + 1].y, (lo - along[i]) / (along[i + 1] - along[i]));
                b.y = Mathf.LerpUnclamped(points[i].y, points[i + 1].y, (hi - along[i]) / (along[i + 1] - along[i]));
                var centre = (a + b) / 2f + tangent * ((frontAhead - rearBehind) / 2f);
                centre.y = y + ((a.y + b.y) / 2f - referenceY);
                // Split into <=2 m pieces: bounded nearest conservative boundary instead of one huge box.
                var steps = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / 2f));
                for (var s = 0; s < steps; s++)
                {
                    var start = Mathf.Lerp(lo, hi, s / (float)steps); var finish = Mathf.Lerp(lo, hi, (s + 1f) / steps);
                    var at = centre + tangent * ((start + finish - lo - hi) / 2f);
                    at.y = y + Mathf.LerpUnclamped(points[i].y, points[i + 1].y,
                        ((start + finish) / 2f - along[i]) / (along[i + 1] - along[i])) - referenceY;
                    if (Blocked(at, new Vector3(sweepHalfWidth, sweepHalfHeight,
                        (finish - start + frontAhead + rearBehind) / 2f + extraClearance + width * .5f * Mathf.Sin(headingAllowance + chordAllowance)), Quaternion.LookRotation(tangent), a.y, "piece", back))
                    {
                        // The swept box is parameterized by REAR-AXLE travel. Convert that free travel
                        // to a gap; it is not a signal plane through the truck's current front.
                        BlockerGap = Mathf.Max(0f, start - rear); Min(ref gap, BlockerGap.Value); return;
                    }
                }
                if (i + 1 < points.Length - 1 && along[i + 1] <= end &&
                    Vector3.Dot(tangent, Flat(points[i + 2] - points[i + 1]).normalized) < 0.9950042f)
                {
                    var at = points[i + 1]; at.y = centre.y;
                    if (Blocked(at, new Vector3(radius, sweepHalfHeight, radius), Quaternion.identity, points[i + 1].y, "corner", back))
                    {
                        BlockerGap = Mathf.Max(0f, along[i + 1] - radius - front); Min(ref gap, BlockerGap.Value); return;
                    }
                }
            }
        }

        private bool Blocked(Vector3 at, Vector3 half, Quaternion rotation, float ground, string query, float? ignoreBehind = null)
        {
            if (++queries > MaxQueries) throw new InvalidOperationException("bounded physics query budget exhausted");
            var count = Physics.OverlapBoxNonAlloc(at, half, hits, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) throw new InvalidOperationException("obstacle buffer saturated");
            for (var i = 0; i < count; i++)
            {
                var c = hits[i];
                if (c == null || c.isTrigger || IsOwn(c)) continue;
                if (ignoreBehind is float back)
                {
                    var b = c.bounds;
                    var furthest = Project(b.center, out _, out _) + Mathf.Sqrt(b.extents.x * b.extents.x + b.extents.z * b.extents.z);
                    if (furthest < back - .01f) continue;
                }
                // Only a static surface wholly BELOW the queried body is ground. Do not filter an entire
                // road/building layer (a divider can share it); tall/compound surfaces cause a safe false stop.
                // Remove only the query's 10 cm vertical padding when deciding what lies below the body.
                var support = ground;
                var topLimit = at.y - half.y + .11f;
                if (bodyContract != null)
                {
                    support = supportPlane ?? float.NaN;
                    if (!Finite(support) || Mathf.Abs(support - ground) > .1f)
                        throw new InvalidOperationException("level native wheel support unavailable or differs from route");
                    topLimit = support + .01f;
                }
                if (c.attachedRigidbody == null && c.bounds.max.y <= topLimit &&
                    c.Raycast(new Ray(new Vector3(at.x, support + 2f, at.z), Vector3.down), out var hit, 4f) &&
                    hit.normal.y > 0.9f && Mathf.Abs(hit.point.y - support) < (bodyContract == null ? .6f : .01f)) continue;
                BlockerId = c.GetInstanceID(); BlockerLayer = c.gameObject.layer; BlockerPosition = c.bounds.center; BlockerQuery = query;
                BlockerCollider = c; BlockerQueryCenter = at; BlockerQueryHalfExtents = half; BlockerQueryRotation = rotation;
                BlockerQueryGround = ground; BlockerQuerySupport = support; BlockerQueryTopLimit = topLimit;
                BlockerQueryIgnoreBehind = ignoreBehind;
                Observed++; return true;
            }
            return false;
        }

        private bool IsOwn(Collider c)
        {
            if (c == parkingGap) return true;
            for (var i = 0; i < own.Length; i++) if (own[i] == c) return true;
            return false;
        }

        public void Dispose()
        {
            if (disposed) { Retire(); return; }
            disposed = true;
            // Cleanup cannot erase a stopped obstruction from native junction occupancy. This explicit
            // record survives the controller; no hidden ID, no TriggerColliderRemovedEvent while body lives.
            try
            {
                // Cancellation may happen between physics and the next Observe. Account for that last
                // body movement synchronously, including forbidden crossings, before releasing anything.
                if (car != null && scene != null) { Measure(out var rear, out _, out _, out _); UpdateCrossings(rear); }
            }
            catch (Exception ex) { fault = fault ?? "traffic retirement: " + ex.Message; }
            Retire();
            if (Retained)
            {
                if (car != null)
                {
                    // Independent record: attaching it to the car risks OnDestroy running before the
                    // CarController becomes Unity-null, leaving no callback to retire its membership.
                    var record = new GameObject("FoodTrucks retained traffic body");
                    Object.DontDestroyOnLoad(record);
                    hold = record.AddComponent<CrewProbeTrafficHold>(); hold.Traffic = this;
                }
            }
        }

        internal void Retire()
        {
            try { RetireCore(); }
            catch (Exception ex) { fault = fault ?? "retained traffic cleanup: " + ex.Message; }
        }

        private void RetireCore()
        {
            var gone = removing || car == null || !car.gameObject.activeInHierarchy || scene == null;
            var rear = gone ? float.MaxValue : Project(car!.transform.TransformPoint(new Vector3(0, 0, rearZ)), out _, out _);
            foreach (var c in crossings)
            {
                if (!c.Entered) continue;
                if (!gone) ReconcileRegistry(c);
                if (!gone)
                {
                    var beyondExit = rear - rearBehind > along[c.Exit] && BodyPlane(c.ExitSource, false) > Margin;
                    // Native takeover can reverse out through the original entry. Keep occupancy
                    // until the WHOLE body is behind that physical plane and local route progress
                    // agrees; a distant curved segment cannot clear an infinite plane by accident.
                    var beforeEntry = rear + Mathf.Max(frontAhead, rearBehind) + width < along[c.Entry] &&
                        BodyPlane(c.EntrySource, true) < -Margin;
                    if (!beyondExit && !beforeEntry) continue;
                }
                // Native scene resets may replace the list. Remove only our own identity from its original
                // registry as well; do not touch another generation's membership or a Driven Rides -1.
                if (ReferenceEquals(MembersField!.GetValue(c.Intersection), c.Members)) c.Intersection.VehicleLeft(id);
                else c.Members.Remove(id);
                c.Entered = false;
            }
            if (Registrations == 0 && ColliderOverrides == 0 && Owners.TryGetValue(carId, out var owner) && owner == this)
            {
                Owners.Remove(carId);
                if (hold != null) { hold.Traffic = null; Object.Destroy(hold.gameObject); hold = null; }
            }
        }

        private void RollbackAdmission()
        {
            if (Owners.TryGetValue(carId, out var owner) && owner == this) Owners.Remove(carId);
        }
    }

    // Emergency cancellation while physically blocking a junction is NOT normal completion. Lifecycle
    // must report retained counts, hold the body, and require explicit recovery/takeover. The native
    // intersection is released only after whole-body clearance or actual body removal, never on a timer.
    internal sealed class CrewProbeTrafficHold : MonoBehaviour
    {
        internal CrewProbeTraffic? Traffic;
        private void Update() { Traffic?.Retire(); }
        private void OnDestroy() { Traffic?.Retire(); }
    }
}
