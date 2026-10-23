#if FOODTRUCKS_CREW_DRIVING_DEV
#nullable enable
using System;
using System.Collections.Generic;
using FoodTrucks.Core.CrewDriving;
using UnityEngine;
using NWH.WheelController3D;

namespace FoodTrucks
{
    // Read-only certificate for the narrow, zero-camber, undamaged +/-5 degree profile.
    // The owner must call ValidateWheelStep from the required native Step prefix.
    internal sealed class CrewProbeBody
    {
        private const float Epsilon = .00001f;
        // NWH writes shell poses in world space, so Unity's inverse transform leaves sub-mm
        // rounding in local positions. This allowance stays inside the 1 mm hull inset.
        private const float PoseEpsilon = .0005f;
        private const double Yaw = Math.PI / 36;
        private const double MaxTilt = .05;
        internal readonly Vector3 Min, Max;
        internal readonly Collider[] Own;
        internal readonly Collider? ParkingGap;
        internal long FullValidations { get; private set; }
        internal long MeshVertexReads { get; private set; }
        internal long WheelValidations { get; private set; }

        // Operating bounds rotate about the rear axle used by Core and traffic. Separate
        // tilt allowances keep future yaw-only queries outside every permitted pitch/roll pose.
        internal float PlanarTiltPadding(float rearZ) => TiltPadding(rearZ, false);
        internal float VerticalTiltPadding(float rearZ) => TiltPadding(rearZ, true);
        private float TiltPadding(float rearZ, bool vertical)
        {
            var x = Math.Max(Math.Abs(Min.x), Math.Abs(Max.x));
            var z = Math.Max(Math.Abs(Min.z - rearZ), Math.Abs(Max.z - rearZ));
            var horizontal = Math.Sqrt(x * x + z * z);
            var y = Math.Max(Math.Abs(Min.y), Math.Abs(Max.y));
            var sine = Math.Sin(MaxTilt); var cosine = Math.Cos(MaxTilt);
            // The planar frame follows the projected forward axis. Include pitch/roll cross
            // coupling as well as foreshortening, not just a single-axis rotation.
            return (float)(vertical ? y * (1 - cosine) + horizontal * sine :
                y * sine + horizontal * (1 - cosine + sine * sine / cosine));
        }
        private readonly CarController car;
        private readonly NWH.VehiclePhysics2.VehicleController nwh;
        private readonly object powertrain;
        private readonly Rigidbody body;
        private readonly Collider authority;
        private readonly Collider? additional;
        private readonly VehicleParkingHelper? parking;
        private readonly int parkingLayer;
        private readonly bool parkingTrigger;
        private readonly Solid[] solids;
        private readonly WheelState[] wheels;
        private readonly Bounds[] prisms;
        internal float RootY => car.transform.position.y;
        internal bool TrafficCertified { get; private set; }
        internal float BandMin { get; private set; }
        internal float BandMax => BandMin + .10f;
        internal float? SelectedBandMin => TrafficCertified ? BandMin : (float?)null;
        internal float? SelectedBandMax => TrafficCertified ? BandMax : (float?)null;
        internal float? AttemptedBandMin { get; private set; }
        internal string? FailureStage { get; private set; }
        internal int BandCandidates { get; private set; }
        internal long CookedQueries { get; private set; }
        internal int FailingShell { get; private set; } = -1;
        internal int FailingCorner { get; private set; } = -1;
        internal int FailingAuthority { get; private set; }
        internal Vector3 FailingPoint { get; private set; }
        private int minShell, maxShell, minCorner, maxCorner;
        private Vector3 minPoint, maxPoint;
        internal float WorldBandMin { get; private set; }
        internal float WorldBandMax { get; private set; }
        private readonly List<Collider> inventory = new List<Collider>(129);
        private readonly List<Vector3> vertices = new List<Vector3>(4096);
        private readonly List<int> triangles = new List<int>(1536);
        private readonly List<VehicleParkingHelper> parkingInventory = new List<VehicleParkingHelper>(2);

        // Capture every local link: a compensating reparent or mount edit is not accepted.
        private sealed class Pose
        {
            private readonly Transform[] links;
            private readonly Vector3[] positions, scales;
            private readonly Quaternion[] rotations;
            private readonly Transform root;
            internal Pose(Transform t, Transform root)
            {
                this.root = root;
                var path = new List<Transform>();
                for (var p = t; p != root; p = p.parent)
                {
                    if (p == null || path.Count >= 128) throw new InvalidOperationException("Collider/mount is outside the bounded truck hierarchy.");
                    path.Add(p);
                }
                links = path.ToArray(); positions = new Vector3[links.Length];
                scales = new Vector3[links.Length]; rotations = new Quaternion[links.Length];
                for (var i = 0; i < links.Length; i++)
                { positions[i] = links[i].localPosition; scales[i] = links[i].localScale; rotations[i] = links[i].localRotation; }
            }
            internal bool Same()
            {
                for (var i = 0; i < links.Length; i++)
                {
                    var t = links[i];
                    if (t == null || t.parent != (i + 1 < links.Length ? links[i + 1] : root) ||
                        !Exact(t.localPosition, positions[i]) || !Exact(t.localScale, scales[i]) || !t.localRotation.Equals(rotations[i])) return false;
                }
                return true;
            }
        }

        private sealed class Solid
        {
            internal readonly Collider Collider;
            internal readonly bool Wheel, Required, Trigger;
            internal readonly Mesh? Mesh;
            internal readonly Bounds Shape;
            internal readonly int Layer, Direction;
            internal readonly float Radius, Height;
            internal readonly Pose Pose;
            internal readonly Transform? Parent;
            internal readonly Vector3 Scale;
            internal readonly Vector3[]? BodyVertices;
            internal readonly int[]? BodyTriangles;
            internal readonly MeshColliderCookingOptions CookingOptions;
            internal Solid(Collider c, Transform root, bool wheel, bool required)
            {
                Collider = c; Wheel = wheel; Required = required; Trigger = c.isTrigger;
                Layer = c.gameObject.layer;
                Parent = c.transform.parent; Scale = c.transform.localScale;
                Pose = new Pose(wheel ? (Parent ?? throw new InvalidOperationException("Wheel shell has no parent.")) : c.transform, root);
                if (!CrewProbeShape.TryLocalBounds(c, out Shape)) throw new InvalidOperationException("Unsupported or invalid collider shape.");
                if (c is MeshCollider mesh)
                {
                    Mesh = mesh.sharedMesh;
                    CookingOptions = mesh.cookingOptions;
                    if (!mesh.convex && !Trigger) throw new InvalidOperationException("Non-convex solid is unsupported.");
                    if (required)
                    {
                        if (Mesh == null || !Mesh.isReadable || Mesh.vertexCount < 3 || Mesh.vertexCount > 256)
                            throw new InvalidOperationException("Authoritative body needs a readable 3..256 vertex mesh.");
                        BodyVertices = Mesh.vertices;
                        foreach (var v in BodyVertices)
                            if (!Finite(v) || !Enclosed(v, Shape.min, Shape.max))
                                throw new InvalidOperationException("Body vertices leave the declared mesh bounds.");
                        if (Mesh.subMeshCount != 1) throw new InvalidOperationException("Authoritative body needs one bounded triangle submesh.");
                        var indexCount = Mesh.GetIndexCount(0);
                        if (indexCount < 3 || indexCount > 1536 || indexCount % 3 != 0)
                            throw new InvalidOperationException("Authoritative body needs one bounded triangle submesh.");
                        var indices = new List<int>(1536);
                        Mesh.GetTriangles(indices, 0);
                        BodyTriangles = indices.ToArray();
                        foreach (var index in BodyTriangles)
                            if (index < 0 || index >= BodyVertices.Length) throw new InvalidOperationException("Invalid body triangle index.");
                    }
                }
                else if (required && !(c is BoxCollider)) throw new InvalidOperationException("Authoritative body must be a box or convex readable mesh.");
                if (c is SphereCollider sphere) Radius = sphere.radius;
                if (c is CapsuleCollider capsule) { Radius = capsule.radius; Height = capsule.height; Direction = capsule.direction; }
            }
        }

        private sealed class WheelState
        {
            internal readonly WheelController Controller;
            internal readonly object NativeEntry;
            internal readonly Wheel Wheel;
            internal readonly Spring Spring;
            internal readonly MeshCollider Top, Bottom;
            internal readonly Transform Shell;
            internal readonly GameObject ShellObject;
            internal readonly Pose MountPose;
            internal readonly Vector3 Pivot;
            internal readonly float Radius, Width, Travel;
            internal WheelState(WheelController wc, object entry, Transform root)
            {
                Controller = wc; NativeEntry = entry;
                Wheel = wc.wheel ?? throw new InvalidOperationException("Native wheel missing.");
                Spring = wc.spring ?? throw new InvalidOperationException("Native spring missing.");
                Top = Wheel.topMeshCollider; Bottom = Wheel.bottomMeshCollider;
                Shell = Wheel.colliderTransform; ShellObject = Wheel.colliderGO;
                if (Top == null || Bottom == null || Top == Bottom || Shell == null || ShellObject == null ||
                    ShellObject.transform != Shell || Top.transform != Shell || Bottom.transform != Shell)
                    throw new InvalidOperationException("Exact native wheel shell references are inconsistent.");
                MountPose = new Pose(wc.transform, root);
                Pivot = ToRoot(wc.transform, root, default);
                Radius = wc.Radius; Width = wc.Width; Travel = Spring.maxLength;
            }
        }

        internal CrewProbeBody(CarController car)
        {
            this.car = car ?? throw new ArgumentNullException(nameof(car));
            nwh = car.vehicleController;
            if (nwh == null || nwh.powertrain == null || nwh.powertrain.wheels == null)
                throw new InvalidOperationException("Native powertrain is unavailable.");
            powertrain = nwh.powertrain;
            body = nwh.vehicleRigidbody;
            authority = car.vehicleCollider;
            additional = car.additionalCollider;
            if (body == null || authority == null || additional == authority) throw new InvalidOperationException("Authoritative body is unavailable or duplicated.");
            car.GetComponentsInChildren(true, parkingInventory);
            if (parkingInventory.Count > 1) throw new InvalidOperationException("Multiple parking helpers are unsupported.");
            parking = parkingInventory.Count == 0 ? null : parkingInventory[0];
            ParkingGap = parking == null ? null : parking.parallelParkingCollider;
            if (ParkingGap != null) { parkingLayer = ParkingGap.gameObject.layer; parkingTrigger = ParkingGap.isTrigger; }
            if (ParkingGap == authority || (ParkingGap != null && ParkingGap == additional)) throw new InvalidOperationException("Parking gap aliases a physical body.");
            var count = nwh.powertrain.wheels.Count;
            if (count < 4 || count > 64) throw new InvalidOperationException("Expected 4..64 native wheels.");
            wheels = new WheelState[count];
            for (var i = 0; i < count; i++)
            {
                var entry = nwh.powertrain.wheels[i];
                if (entry == null || !(entry.wheelUAPI is WheelController wc)) throw new InvalidOperationException("Unsupported native wheel controller.");
                wheels[i] = new WheelState(wc, entry, car.transform);
                for (var j = 0; j < i; j++)
                    if (wheels[j].Controller == wc || wheels[j].Top == wheels[i].Top || wheels[j].Bottom == wheels[i].Top ||
                        wheels[j].Top == wheels[i].Bottom || wheels[j].Bottom == wheels[i].Bottom)
                        throw new InvalidOperationException("Duplicate native wheel or mesh collider reference.");
            }
            car.GetComponentsInChildren(true, inventory);
            if (inventory.Count == 0 || inventory.Count > 128) throw new InvalidOperationException("Expected 1..128 truck colliders.");
            Own = inventory.ToArray();
            if (Array.IndexOf(Own, authority) < 0 || (additional != null && Array.IndexOf(Own, additional) < 0))
                throw new InvalidOperationException("Authoritative body is outside truck inventory.");
            var captured = new List<Solid>();
            var min = Vector3.one * float.MaxValue; var max = Vector3.one * float.MinValue;
            foreach (var c in Own)
            {
                if (c == null) throw new InvalidOperationException("Missing native collider.");
                if (c == ParkingGap)
                {
                    if (c.enabled) throw new InvalidOperationException("Parking gap must be disabled.");
                    continue;
                }
                var s = new Solid(c, car.transform, IsWheel(c), c == authority || c == additional);
                captured.Add(s);
                if (!s.Trigger && !s.Wheel)
                {
                    if (!TryBounds(c, out var lo, out var hi)) throw new InvalidOperationException("Invalid physical bounds.");
                    min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
                }
            }
            solids = captured.ToArray();
            // Physical capture is independent of traffic and of any particular cooked slice.
            foreach (var w in wheels)
                foreach (var c in new[] { w.Top, w.Bottom })
                {
                    if (Array.IndexOf(Own, c) < 0 || !CrewProbeShape.TryLocalBounds(c, out var shape)) throw new InvalidOperationException("Wheel mesh is outside truck inventory.");
                    Reach(shape, out var reachX, out var reachZ);
                    min = Vector3.Min(min, w.Pivot + new Vector3(-(float)reachX, shape.min.y - w.Travel, -(float)reachZ));
                    max = Vector3.Max(max, w.Pivot + new Vector3((float)reachX, shape.max.y, (float)reachZ));
                }
            prisms = new Bounds[wheels.Length * 2];
            Min = min; Max = max;
            var reason = Validate();
            if (reason != null) throw new InvalidOperationException(reason);
        }

        private static void Reach(Bounds shape, out double x, out double z)
        {
            x = Math.Max(Math.Abs(shape.min.x), Math.Abs(shape.max.x)) + Math.Max(Math.Abs(shape.min.z), Math.Abs(shape.max.z)) * Math.Sin(Yaw);
            z = Math.Max(Math.Abs(shape.min.z), Math.Abs(shape.max.z)) + Math.Max(Math.Abs(shape.min.x), Math.Abs(shape.max.x)) * Math.Sin(Yaw);
        }

        // The finite search may conservatively miss a feasible slice. No selection is performed
        // after admission. Root-relative lever arms include the entire body, not the rear axle.
        internal string? SelectBand(float commonMin, float commonMax)
        {
            TrafficCertified = false; BandCandidates = 0; AttemptedBandMin = null;
            BandMin = WorldBandMin = WorldBandMax = 0;
            ClearFailure();
            var error = Validate(); if (error != null) return error;
            var allowance = VerticalTiltPadding(0) + .0501f;
            var first = Math.Max(Min.y, commonMin + allowance);
            var last = Math.Min(Max.y - .10f, commonMax - allowance - .10f);
            if (!Finite(first) || !Finite(last) || last < first) return "common_band_empty";
            var authorities = new List<Solid>(); var outlines = new List<ProbeFootprint>();
            foreach (var s in solids) if (s.Required) { authorities.Add(s); outlines.Add(Footprint(s)); }
            for (var candidate = 0; candidate < 17; candidate++)
            {
                var lo = candidate == 0 ? (first + last) * .5f : candidate == 1 ? first :
                    candidate == 2 ? last : first + (last - first) * (candidate - 2) / 15f;
                AttemptedBandMin = lo; BandCandidates++;
                var shell = 0; var all = true;
                foreach (var w in wheels)
                {
                    for (var part = 0; part < 2; part++, shell++)
                    {
                        var c = part == 0 ? w.Top : w.Bottom;
                        if (!CrewProbeShape.TryLocalBounds(c, out var shape)) return "wheel_shape_unreadable";
                        Reach(shape, out var reachX, out var reachZ);
                        var inside = false;
                        for (var i = 0; i < authorities.Count; i++)
                        {
                            ClearFailure(); FailingShell = shell;
                            FailingAuthority = authorities[i].Collider.GetInstanceID(); FailureStage = "outline";
                            if (outlines[i].ContainsYawSweep(new ProbePoint(w.Pivot.x, w.Pivot.z), new ProbePoint(shape.min.x, shape.min.z),
                                new ProbePoint(shape.max.x, shape.max.z), Yaw, .001) &&
                                TryPrism(authorities[i], w, shape, reachX, reachZ, lo, out prisms[shell]))
                            { inside = true; break; }
                        }
                        if (!inside) { FailingShell = shell; all = false; break; }
                    }
                    if (!all) break;
                }
                if (all) { BandMin = lo; AttemptedBandMin = null; TrafficCertified = true; ClearFailure(); UpdateWorldBand(); return null; }
            }
            return "cooked_band_search_exhausted";
        }

        private bool TryPrism(Solid s, WheelState w, Bounds shape, double reachX, double reachZ, float bandLo, out Bounds prism)
        {
            prism = default;
            var c = s.Collider;
            FailingAuthority = c.GetInstanceID(); FailureStage = "authority_unavailable";
            if (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger ||
                (c is MeshCollider mesh && !mesh.convex)) return false;
            var bandHi = bandLo + .10f;
            var wheelLo = w.Pivot.y + shape.min.y - w.Travel;
            var wheelHi = w.Pivot.y + shape.max.y;
            var distance = Math.Max(Math.Max(Math.Abs(wheelLo - bandLo), Math.Abs(wheelLo - bandHi)),
                Math.Max(Math.Abs(wheelHi - bandLo), Math.Abs(wheelHi - bandHi)));
            // Project along trigger up, preserving its local X/Z. Include both truck tilt
            // and the retained tiny Gley upright tolerance, plus the existing numeric inset.
            var padding = distance * Math.Tan(MaxTilt + CrewProbeTrigger.UprightTolerance) + .002;
            var min = new Vector3(w.Pivot.x - (float)(reachX + padding), bandLo, w.Pivot.z - (float)(reachZ + padding));
            var max = new Vector3(w.Pivot.x + (float)(reachX + padding), bandHi, w.Pivot.z + (float)(reachZ + padding));
            FailureStage = "prism_envelope";
            if (!Finite(min) || !Finite(max) || min.x < Min.x || min.y < Min.y || min.z < Min.z ||
                max.x > Max.x || max.y > Max.y || max.z > Max.z) return false;
            // This envelope check bounds the root-tilt lever arm. Cooked containment below
            // remains mandatory; an outer box is never used as the authority's inner proof.
            prism = new Bounds((min + max) * .5f, max - min);
            // Each shell fits ONE cooked convex authority. A union cannot bridge a gap.
            for (var i = 0; i < 8; i++)
            {
                var point = car.transform.TransformPoint(Corner(prism, i));
                FailingCorner = i; FailingPoint = point;
                FailureStage = "cooked_point_nonfinite";
                if (!Finite(point)) return false;
                Vector3 nearest;
                FailureStage = "cooked_query";
                try { CookedQueries++; nearest = c.ClosestPoint(point); }
                catch (Exception) { return false; }
                FailureStage = "cooked_corner";
                if (!Finite(nearest) || (nearest - point).sqrMagnitude > 1e-8f) return false;
            }
            return true;
        }

        internal void UpdateWorldBand()
        {
            var lo = float.MaxValue; var hi = float.MinValue;
            for (var shell = 0; shell < prisms.Length; shell++)
                for (var i = 0; i < 8; i++)
                {
                    var point = car.transform.TransformPoint(Corner(prisms[shell], i)); var y = point.y;
                    if (!Finite(y)) throw new InvalidOperationException("nonfinite_prism_world");
                    if (y < lo) { lo = y; minShell = shell; minCorner = i; minPoint = point; }
                    if (y > hi) { hi = y; maxShell = shell; maxCorner = i; maxPoint = point; }
                }
            WorldBandMin = lo; WorldBandMax = hi;
        }

        internal void ClearFailure()
        {
            FailingShell = FailingCorner = -1; FailingAuthority = 0; FailingPoint = default; FailureStage = null;
        }
        // Pure queries are also used for inactive/distant diagnostic entries. Only the
        // live admission/observation refusal may latch a failing prism/corner.
        internal bool CoversInterval(float lo, float hi) => TrafficCertified && WorldBandMin >= lo && WorldBandMax <= hi;
        internal void RecordCoverageFailure(float lo, float hi)
        {
            ClearFailure(); FailureStage = "world_coverage";
            if (!TrafficCertified || CoversInterval(lo, hi)) return;
            var below = WorldBandMin < lo;
            FailingShell = below ? minShell : maxShell; FailingCorner = below ? minCorner : maxCorner;
            FailingPoint = below ? minPoint : maxPoint;
        }
#if CREW_PROBE_BODY_TESTS
        // Legacy body-only harness convenience. Not compiled into the mod; production
        // computes world prism heights once in Traffic and uses CoversInterval directly.
        internal bool CoversTrigger(Collider trigger)
        {
            try
            {
                if (!TrafficCertified || ValidateIdentity() != null || trigger == null || !trigger.enabled ||
                    !trigger.gameObject.activeInHierarchy || !trigger.isTrigger) return false;
                UpdateWorldBand();
                return CrewProbeTrigger.WorldInterval(trigger, out var lo, out var hi, out _) == null && CoversInterval(lo, hi);
            }
            catch (Exception) { return false; }
        }

#endif

        private ProbeFootprint Footprint(Solid s)
        {
            var points = new List<ProbePoint>();
            if (s.BodyVertices != null)
            {
                foreach (var v in s.BodyVertices)
                {
                    var p = ToRoot(s.Collider.transform, car.transform, v);
                    if (!Finite(p)) throw new InvalidOperationException("Non-finite body vertex.");
                    points.Add(new ProbePoint(p.x, p.z));
                }
            }
            else for (var i = 0; i < 8; i++)
            {
                var p = ToRoot(s.Collider.transform, car.transform, Corner(s.Shape, i));
                if (!Finite(p)) throw new InvalidOperationException("Non-finite body corner.");
                points.Add(new ProbePoint(p.x, p.z));
            }
            return ProbeFootprint.Create(points);
        }

        internal bool IsWheel(Collider c)
        {
            foreach (var w in wheels) if (w != null && (w.Top == c || w.Bottom == c)) return true;
            return false;
        }
        internal bool ContainsWheel(WheelController wheel)
        {
            foreach (var w in wheels) if (w.Controller == wheel) return true;
            return false;
        }

        internal string? Validate()
        {
            try { return ValidateNative(); }
            catch (Exception) { return "Native body validation could not be read."; }
        }

        private string? ValidateIdentity()
        {
            if (car == null || nwh == null || body == null || car.vehicleController != nwh || nwh.vehicleRigidbody != body ||
                !ReferenceEquals(nwh.powertrain, powertrain) || car.vehicleCollider != authority || car.additionalCollider != additional)
                return "Native body/controller identity changed.";
            if (!Unit(car.transform.lossyScale) || !body.detectCollisions || body.includeLayers.value != 0 || body.excludeLayers.value != 0)
                return "Unsupported root scale or rigidbody collision configuration.";
            var up = car.transform.rotation * new Vector3(0, 1, 0);
            if (!Finite(up) || Math.Abs(up.sqrMagnitude - 1) > Epsilon ||
                Math.Atan2(Math.Sqrt((double)up.x * up.x + (double)up.z * up.z), up.y) > MaxTilt)
                return "Root tilt exceeds certified cooked prism profile.";
            return null;
        }

        private string? ValidateNative()
        {
            FullValidations++;
            var identity = ValidateIdentity(); if (identity != null) return identity;
            car.GetComponentsInChildren(true, parkingInventory);
            if (parkingInventory.Count != (parking == null ? 0 : 1) || (parking != null &&
                (parkingInventory[0] != parking || parking.parallelParkingCollider != ParkingGap))) return "Parking helper identity changed.";
            if (ParkingGap != null && (ParkingGap.enabled || ParkingGap.gameObject.layer != parkingLayer || ParkingGap.isTrigger != parkingTrigger ||
                ParkingGap.includeLayers.value != 0 || ParkingGap.excludeLayers.value != 0 || ParkingGap.layerOverridePriority != 0))
                return "Parking gap configuration is unsupported or changed.";
            car.GetComponentsInChildren(true, inventory);
            if (inventory.Count != Own.Length || inventory.Count > 128) return "Truck collider inventory changed.";
            foreach (var c in inventory) if (c == null || Array.IndexOf(Own, c) < 0) return "Truck collider identity changed.";
            if (nwh.powertrain.wheels == null || nwh.powertrain.wheels.Count != wheels.Length) return "Native wheel inventory changed.";
            for (var i = 0; i < wheels.Length; i++)
            {
                if (!ReferenceEquals(nwh.powertrain.wheels[i], wheels[i].NativeEntry) || nwh.powertrain.wheels[i].wheelUAPI != wheels[i].Controller)
                    return "Native wheel entry changed.";
                var error = ValidateWheel(wheels[i]); if (error != null) return error;
            }
            foreach (var s in solids)
            {
                var error = ValidateSolid(s, true); if (error != null) return error;
            }
            return null;
        }

        private string? ValidateSolid(Solid s, bool compareVertices)
        {
                var c = s.Collider;
                if (c == null || c.isTrigger != s.Trigger || c.gameObject.layer != s.Layer || c.includeLayers.value != 0 ||
                    c.excludeLayers.value != 0 || c.layerOverridePriority != 0) return "Collider identity, trigger, layer or override changed.";
                if (!s.Pose.Same() || (s.Wheel && (c.transform.parent != s.Parent || !Exact(c.transform.localScale, s.Scale)))) return "Collider hierarchy or fixed pose changed.";
                if (s.Required && (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger)) return "Authoritative body must remain active, enabled and solid.";
                if (!s.Trigger && (c.GetComponentInParent<Rigidbody>(true) != body || (c.enabled && c.attachedRigidbody != body)))
                    return "Solid collider belongs to another rigidbody.";
                if (!CrewProbeShape.TryLocalBounds(c, out var shape) || !Same(shape, s.Shape)) return "Collider dimensions changed.";
                if (c is MeshCollider mesh)
                {
                    if (mesh.sharedMesh != s.Mesh || (!s.Trigger && !mesh.convex) || mesh.cookingOptions != s.CookingOptions)
                        return "Collider mesh reference, convexity or cooking options changed.";
                    if (compareVertices && s.BodyVertices != null)
                    {
                        if (s.Mesh == null || !s.Mesh.isReadable || s.Mesh.vertexCount != s.BodyVertices.Length) return "Body mesh geometry changed.";
                        vertices.Clear(); s.Mesh.GetVertices(vertices); MeshVertexReads++;
                        if (vertices.Count != s.BodyVertices.Length) return "Body mesh vertex count changed.";
                        for (var i = 0; i < vertices.Count; i++) if (!Exact(vertices[i], s.BodyVertices[i])) return "Body mesh vertices changed.";
                        if (s.Mesh.subMeshCount != 1 || s.BodyTriangles == null || s.Mesh.GetIndexCount(0) != s.BodyTriangles.Length)
                            return "Body mesh triangle count changed.";
                        triangles.Clear(); s.Mesh.GetTriangles(triangles, 0);
                        if (triangles.Count != s.BodyTriangles.Length) return "Body mesh triangle count changed.";
                        for (var i = 0; i < triangles.Count; i++) if (triangles[i] != s.BodyTriangles[i]) return "Body mesh triangle indices changed.";
                    }
                }
                if (c is SphereCollider sphere && sphere.radius != s.Radius) return "Sphere radius changed.";
                if (c is CapsuleCollider capsule && (capsule.radius != s.Radius || capsule.height != s.Height || capsule.direction != s.Direction)) return "Capsule geometry changed.";
                if (!s.Trigger && !CurrentBoundsFit(c)) return "Current physical shape leaves certified envelope.";
            return null;
        }

        internal string? ValidateWheelStep(WheelController wheel)
        {
            WheelValidations++;
            try
            {
                var error = ValidateIdentity(); if (error != null) return error;
                WheelState? captured = null;
                foreach (var w in wheels) if (w.Controller == wheel) { captured = w; break; }
                if (captured == null) return "Wheel is not captured by this body contract.";
                error = ValidateWheel(captured); if (error != null) return error;
                // Full inventory and vertex comparison happens once in Observe. Native powertrain
                // stepping only changes wheel state; guard that exact wheel and required bodies here.
                foreach (var s in solids)
                    if (s.Required || s.Collider == captured.Top || s.Collider == captured.Bottom)
                    { error = ValidateSolid(s, false); if (error != null) return error; }
                return null;
            }
            catch (Exception) { return "Native wheel validation could not be read."; }
        }

        private string? ValidateWheel(WheelState w)
        {
            var wc = w.Controller;
            if (wc == null || !ReferenceEquals(wc.wheel, w.Wheel) || !ReferenceEquals(wc.spring, w.Spring) ||
                w.Wheel.topMeshCollider != w.Top || w.Wheel.bottomMeshCollider != w.Bottom ||
                w.Wheel.colliderTransform != w.Shell || w.Wheel.colliderGO != w.ShellObject ||
                w.Top == null || w.Bottom == null || w.Shell == null || w.ShellObject == null ||
                w.ShellObject.transform != w.Shell || w.Top.transform != w.Shell || w.Bottom.transform != w.Shell)
                return "Exact native wheel references changed.";
            if (wc.ParentRigidbody != body || wc.GetComponentInParent<Rigidbody>(true) != body || !wc.gameObject.activeInHierarchy ||
                !w.Shell.gameObject.activeInHierarchy) return "Wheel rigidbody or active hierarchy changed.";
            if (!w.MountPose.Same() || !Aligned(wc.transform) || !Unit(w.Shell.lossyScale)) return "Wheel mount axes, scale or fixed pose unsupported.";
            if (!Finite(w.Radius) || w.Radius <= 0 || !Finite(w.Width) || w.Width <= 0 || !Finite(w.Travel) || w.Travel < 0 ||
                wc.Radius != w.Radius || wc.Width != w.Width || w.Spring.maxLength != w.Travel) return "Wheel dimensions or suspension limit changed.";
            if (!Finite(wc.SteerAngle) || Math.Abs(wc.SteerAngle) > 5 || wc.Camber != 0 || wc.Damage != 0) return "Wheel exceeds straight steering/zero camber/zero damage profile.";
            var p = w.Wheel.localPosition;
            if (!Finite(p) || Math.Abs(p.x) > Epsilon || Math.Abs(p.z) > Epsilon || !Finite(w.Spring.length) ||
                w.Spring.length < -Epsilon || w.Spring.length > w.Travel + Epsilon || Math.Abs(p.y + w.Spring.length) > Epsilon)
                return "Wheel suspension position is outside certified travel.";
            var shell = ToRoot(w.Shell, car.transform, default);
            if (!Finite(shell) || Math.Abs(shell.x - w.Pivot.x) > PoseEpsilon || Math.Abs(shell.z - w.Pivot.z) > PoseEpsilon ||
                shell.y < w.Pivot.y - w.Travel - PoseEpsilon || shell.y > w.Pivot.y + PoseEpsilon) return "Wheel shell translation is unsupported.";
            var right = Axis(w.Shell, new Vector3(1, 0, 0));
            var up = Axis(w.Shell, new Vector3(0, 1, 0));
            var forward = Axis(w.Shell, new Vector3(0, 0, 1));
            if (!Near(up, new Vector3(0, 1, 0)) || !Finite(right) || !Finite(forward) ||
                Math.Abs(right.y) > Epsilon || Math.Abs(forward.y) > Epsilon ||
                Math.Abs(Math.Atan2(forward.x, forward.z)) > Yaw + Epsilon ||
                !Near(right, new Vector3(forward.z, 0, -forward.x))) return "Wheel shell rotation is outside certified yaw.";
            return null;
        }

        internal bool TryGround(out float y)
        {
            y = 0;
            try
            {
                if (wheels.Length != 4 || ValidateIdentity() != null) return false;
                var lo = float.MaxValue; var hi = float.MinValue; var sum = 0d;
                foreach (var w in wheels)
                {
                    var wc = w.Controller; var c = wc.HitCollider; var p = wc.HitPoint; var normal = wc.HitNormal;
                    if (!wc.IsGrounded || c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger ||
                        c.attachedRigidbody != null || c.GetComponentInParent<Rigidbody>(true) != null || Array.IndexOf(Own, c) >= 0 ||
                        !Finite(p) || !Finite(normal) || normal.y <= .99f) return false;
                    lo = Math.Min(lo, p.y); hi = Math.Max(hi, p.y); sum += p.y;
                }
                if (hi - lo > .02f) return false;
                y = (float)(sum / 4); return true;
            }
            catch (Exception) { return false; }
        }

        private bool CurrentBoundsFit(Collider c)
        {
            return TryBounds(c, out var lo, out var hi) && Enclosed(lo, Min, Max) && Enclosed(hi, Min, Max);
        }
        private bool TryBounds(Collider c, out Vector3 min, out Vector3 max)
        {
            min = Vector3.one * float.MaxValue; max = Vector3.one * float.MinValue;
            if (!CrewProbeShape.TryLocalBounds(c, out var shape)) return false;
            for (var i = 0; i < 8; i++)
            {
                var p = ToRoot(c.transform, car.transform, Corner(shape, i));
                if (!Finite(p)) return false;
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            return max.x > min.x && max.y > min.y && max.z > min.z;
        }
        // Compose local links rather than subtracting large world coordinates: a moving
        // truck must retain the same certificate far from the world's origin.
        private static Vector3 ToRoot(Transform t, Transform root, Vector3 p)
        {
            for (var depth = 0; t != root; depth++, t = t.parent)
            {
                if (t == null || depth >= 128) throw new InvalidOperationException("Shape is outside the bounded truck hierarchy.");
                p = t.localPosition + t.localRotation * Vector3.Scale(t.localScale, p);
            }
            return p;
        }
        private Vector3 Axis(Transform t, Vector3 axis) => ToRoot(t, car.transform, axis) - ToRoot(t, car.transform, default);
        private bool Aligned(Transform t) => Unit(t.lossyScale) && Near(Axis(t, new Vector3(1, 0, 0)), new Vector3(1, 0, 0)) &&
            Near(Axis(t, new Vector3(0, 1, 0)), new Vector3(0, 1, 0)) && Near(Axis(t, new Vector3(0, 0, 1)), new Vector3(0, 0, 1));
        private static Vector3 Corner(Bounds b, int i) => b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        private static bool Same(Bounds a, Bounds b) => Exact(a.center, b.center) && Exact(a.size, b.size);
        private static bool Enclosed(Vector3 p, Vector3 min, Vector3 max) => p.x >= min.x - PoseEpsilon && p.y >= min.y - PoseEpsilon && p.z >= min.z - PoseEpsilon &&
            p.x <= max.x + PoseEpsilon && p.y <= max.y + PoseEpsilon && p.z <= max.z + PoseEpsilon;
        private static bool Exact(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        private static bool Near(Vector3 a, Vector3 b) => Finite(a) && Finite(b) && Math.Abs(a.x - b.x) <= Epsilon && Math.Abs(a.y - b.y) <= Epsilon && Math.Abs(a.z - b.z) <= Epsilon;
        private static bool Unit(Vector3 v) => Near(v, Vector3.one);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}

#endif
