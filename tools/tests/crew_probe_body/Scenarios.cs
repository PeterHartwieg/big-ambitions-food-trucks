#nullable enable
using System;
using System.Collections.Generic;
using FoodTrucks;
using UnityEngine;
using NWH.WheelController3D;
using NWH.VehiclePhysics2;

internal static class Scenarios
{
    internal sealed class Rig
    {
        internal readonly CarController Car;
        internal readonly Rigidbody Body;
        internal readonly WheelController[] Wheels = new WheelController[4];
        internal readonly VehicleParkingHelper Parking;
        internal readonly BoxCollider Road;
        internal Rig(bool meshBody = false)
        {
            var go = new GameObject("truck");
            Car = go.Add<CarController>(); Body = go.Add<Rigidbody>();
            Car.vehicleController = go.Add<VehicleController>(); Car.vehicleController.vehicleRigidbody = Body;
            if (meshBody)
            {
                var c = go.Add<MeshCollider>(); c.sharedMesh = BoxMesh(new Vector3(0, 1.7f, 0), new Vector3(2.5f, 2.8f, 6)); Car.vehicleCollider = c;
            }
            else
            {
                var c = go.Add<BoxCollider>(); c.center = new Vector3(0, 1.7f, 0); c.size = new Vector3(2.5f, 2.8f, 6); Car.vehicleCollider = c;
            }
            Parking = go.Add<VehicleParkingHelper>();
            Parking.parallelParkingCollider = Child(go.transform, "gap").Add<BoxCollider>(); Parking.parallelParkingCollider.enabled = false;
            Road = new GameObject("road").Add<BoxCollider>(); Road.size = new Vector3(20, 1, 20);
            for (var i = 0; i < 4; i++)
            {
                var mount = Child(go.transform, "mount");
                mount.transform.localPosition = new Vector3(i % 2 == 0 ? -.87f : .87f, .7f, i < 2 ? -1.7f : 1.7f);
                var wc = mount.Add<WheelController>(); Wheels[i] = wc; wc.ParentRigidbody = Body; wc.HitCollider = Road;
                Car.vehicleController.powertrain.wheels.Add(new Entry { wheelUAPI = wc });
                // Native references are authoritative; support sibling shells as well as child shells.
                var shell = Child(go.transform, "shell");
                wc.wheel.colliderGO = shell; wc.wheel.colliderTransform = shell.transform;
                wc.wheel.topMeshCollider = shell.Add<MeshCollider>();
                wc.wheel.topMeshCollider.sharedMesh = BoxMesh(new Vector3(0, .2f, 0), new Vector3(.268f, .436f, .836f));
                wc.wheel.bottomMeshCollider = shell.Add<MeshCollider>();
                wc.wheel.bottomMeshCollider.sharedMesh = BoxMesh(new Vector3(0, -.18f, 0), new Vector3(.22f, .36f, .72f));
                wc.wheel.bottomMeshCollider.enabled = false;
                SetPose(wc, .2f, 0);
            }
        }
        internal CrewProbeBody Capture()
        {
            var c = new CrewProbeBody(Car);
            var allowance = c.VerticalTiltPadding(0) + .0501f;
            var reason = c.SelectBand(.35f - allowance - .00005f, .45f + allowance + .00005f);
            if (reason != null) throw new InvalidOperationException(reason);
            return c;
        }
    }
    private static GameObject Child(Transform parent, string name)
    { var go = new GameObject(name); go.transform.parent = parent; return go; }
    internal static Mesh BoxMesh(Vector3 center, Vector3 size)
    {
        var bounds = new Bounds(center, size); var data = new Vector3[8];
        for (var i = 0; i < 8; i++) data[i] = center + Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return new Mesh { bounds = bounds, Data = data };
    }
    internal static void SetPose(WheelController wc, float length, float angle)
    {
        wc.spring.length = length; wc.wheel.localPosition = new Vector3(0, -length, 0); wc.SteerAngle = angle;
        wc.wheel.colliderTransform.position = wc.transform.TransformPoint(wc.wheel.localPosition);
        wc.wheel.colliderTransform.rotation = wc.transform.rotation * Quaternion.Euler(0, angle, 0);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Good(CrewProbeBody body) { var reason = body.Validate(); Check(reason == null, reason ?? ""); }
    private static void Refuse(Action<Rig> edit, bool admission = false, bool mesh = false)
    {
        var rig = new Rig(mesh); var contract = admission ? null : rig.Capture(); edit(rig);
        var before = Writes.Count;
        if (admission)
        {
            try { rig.Capture(); throw new Exception("admission accepted mutation"); }
            catch (InvalidOperationException) { }
        }
        else Check(contract!.Validate() != null, "observation accepted mutation");
        Check(Writes.Count == before, "refusal mutated native configuration");
    }
    private static void Normal()
    {
        foreach (var mesh in new[] { false, true })
        {
            var r = new Rig(mesh); var writes = Writes.Count; var c = r.Capture(); Good(c);
            Check(Writes.Count == writes, "capture wrote native configuration");
            Check(c.Own.Length == 10 && c.ParkingGap == r.Parking.parallelParkingCollider, "inventory");
            Check(c.Min.y < .01f && c.Max.y >= 3.1f, "full static suspension envelope excludes bottom");
            Check(c.IsWheel(r.Wheels[0].wheel.bottomMeshCollider) && !c.IsWheel(r.Car.vehicleCollider!), "exact wheel membership");
            Check(!c.ContainsWheel(new GameObject().Add<WheelController>()), "unknown wheel membership");
            for (var n = 0; n <= 20; n++) foreach (var w in r.Wheels)
            {
                SetPose(w, w.spring.maxLength * n / 20, -5 + n * .5f);
                w.wheel.bottomMeshCollider.enabled = n % 2 == 0; Good(c);
            }
            // A controller's own child shell is equally valid.
            r = new Rig();
            foreach (var w in r.Wheels) { w.wheel.colliderTransform.parent = w.transform; SetPose(w, .2f, 0); }
            Good(r.Capture());
            r = new Rig(); r.Car.transform.localPosition = new Vector3(10, 0, 12); r.Car.transform.localRotation = Quaternion.Euler(0, 20, 0);
            foreach (var w in r.Wheels) { SetPose(w, .2f, 0); w.wheel.colliderTransform.localRotation = Quaternion.identity; }
            c = r.Capture(); Good(c);
            // Independent pitched/rolled corner projection must stay inside both operating allowances.
            const float rearZ = -1.7f;
            var planar = c.PlanarTiltPadding(rearZ); var vertical = c.VerticalTiltPadding(rearZ);
            for (var corner=0;corner<8;corner++) for (var degrees=-2.86f;degrees<=2.86f;degrees+=.13f)
            {
                var q = new Vector3((corner&1)==0?c.Min.x:c.Max.x,(corner&2)==0?c.Min.y:c.Max.y,((corner&4)==0?c.Min.z:c.Max.z)-rearZ);
                foreach (var rotation in new[]{Quaternion.Euler(degrees,0,0),Quaternion.Euler(0,0,degrees),Quaternion.Euler(degrees*.707f,0,degrees*.707f)})
                {
                    var f=rotation*new Vector3(0,0,1);
                    var heading=(float)(Math.Atan2(f.x,f.z)*180/Math.PI);
                    var v=Quaternion.Euler(0,-heading,0)*(rotation*q);
                    Check(Math.Abs(v.x-q.x)<=planar+.00001f&&Math.Abs(v.z-q.z)<=planar+.00001f,"planar tilt enclosure");
                    Check(Math.Abs(v.y-q.y)<=vertical+.00001f,"vertical tilt enclosure");
                }
            }
            var reads = c.MeshVertexReads; var validations = c.FullValidations;
            foreach (var w in r.Wheels) Check(c.ValidateWheelStep(w) == null, "cheap native wheel prefix");
            c.TryGround(out _);
            Check(c.FullValidations == validations && c.MeshVertexReads == reads, "wheel prefixes and support do not repeat full inventory/vertex reads");
            r.Car.transform.localPosition = new Vector3(500, 0, 500);
            r.Car.transform.localRotation = Quaternion.Euler(0, 40, 0);
            foreach (var w in r.Wheels) SetPose(w, .2f, 4);
            Good(c);
            r.Car.transform.localPosition = new Vector3(10000, 0, -10000);
            r.Car.transform.localRotation = Quaternion.Euler(0, 70, 0); Good(c);
        }
    }
    private static void Identities()
    {
        foreach (var primary in new[] { false, true })
            foreach (var enabled in new[] { false, true })
            {
                var rig = new Rig(); var original = rig.Car.vehicleCollider!;
                var foreign = new GameObject("foreign authority").Add<BoxCollider>(); foreign.enabled = enabled;
                if (primary) { rig.Car.additionalCollider = original; rig.Car.vehicleCollider = foreign; }
                else rig.Car.additionalCollider = foreign;
                var writes = Writes.Count;
                try { new CrewProbeBody(rig.Car); throw new Exception("foreign authoritative role accepted by physical capture"); }
                catch (InvalidOperationException ex) { Check(ex.Message.Contains("outside truck inventory"), ex.Message); }
                Check(Writes.Count == writes, "foreign-role refusal wrote state");
            }
        Refuse(r => r.Wheels[0].wheel.topMeshCollider = r.Wheels[1].wheel.topMeshCollider);
        Refuse(r => { var w = r.Wheels[0].wheel; (w.topMeshCollider, w.bottomMeshCollider) = (w.bottomMeshCollider, w.topMeshCollider); });
        Refuse(r => r.Wheels[0].wheel.colliderGO = new GameObject());
        Refuse(r => r.Wheels[0].wheel.colliderTransform = r.Wheels[1].transform);
        Refuse(r => r.Wheels[0].spring = new Spring());
        Refuse(r => r.Wheels[0].wheel = new Wheel());
        Refuse(r => r.Wheels[0].ParentRigidbody = new GameObject().Add<Rigidbody>());
        Refuse(r => r.Car.vehicleController.powertrain.wheels.Reverse());
        Refuse(r => r.Car.vehicleController.powertrain = new Powertrain());
        Refuse(r => r.Car.vehicleCollider = r.Wheels[0].wheel.topMeshCollider);
        Refuse(r => r.Car.vehicleCollider!.enabled = false);
        Refuse(r => r.Car.gameObject.activeSelf = false);
        Refuse(r => r.Car.vehicleCollider!.isTrigger = true);
        Refuse(r => r.Parking.parallelParkingCollider = null);
        Refuse(r => r.Parking.parallelParkingCollider!.enabled = true);
        Refuse(r => Child(r.Car.transform, "new solid").Add<BoxCollider>());
        Refuse(r => Child(r.Car.transform, "new trigger").Add<BoxCollider>().isTrigger = true);
        Refuse(r => r.Car.gameObject.Components.Remove(r.Car.vehicleCollider!));
        Refuse(r => { var go = Child(r.Car.transform, "nested"); go.Add<Rigidbody>(); go.Add<BoxCollider>(); }, true);
        Refuse(r => { var go = Child(r.Car.transform, "nested disabled"); go.Add<Rigidbody>(); go.Add<BoxCollider>().enabled = false; }, true);
        Refuse(r => { var go = Child(r.Car.transform, "inactive nested body"); go.Add<Rigidbody>(); go.activeSelf = false; Child(go.transform, "solid").Add<BoxCollider>().enabled = false; }, true);
        Refuse(r => r.Wheels[0].wheel.colliderGO.Add<Rigidbody>(), true);
        Refuse(r => r.Wheels[0].wheel.colliderGO.Add<Rigidbody>());
        Refuse(r => r.Wheels[0].wheel.bottomMeshCollider = r.Wheels[0].wheel.topMeshCollider, true);
        Refuse(r => r.Wheels[1].wheel.topMeshCollider = r.Wheels[0].wheel.topMeshCollider, true);
        var normal = new Rig(); var extra = Child(normal.Car.transform, "unknown solid").Add<BoxCollider>(); extra.enabled = false; extra.size = new Vector3(3, 4, 7);
        var body = normal.Capture(); Check(!body.IsWheel(extra), "unknown incorrectly gets wheel exception");
        Check(body.Min.z <= -3.5f && body.Max.z >= 3.5f, "disabled unknown missing from full envelope");
        extra.enabled = true; Good(body); // traffic parent remains responsible for its visibility.
    }
    private static void Geometry()
    {
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.isReadable = false, true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).convex = false, true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Data[0].x += .1f, false, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).cookingOptions ^= MeshColliderCookingOptions.WeldColocatedVertices, false, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Indices[0] = 7, false, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Indices = new int[1539], false, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.subMeshCount = 2, false, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh = BoxMesh(new Vector3(0, 1.7f, 0), new Vector3(2.5f, 2.8f, 6)), false, true);
        Refuse(r => ((BoxCollider)r.Car.vehicleCollider!).size.x += .01f);
        Refuse(r => r.Wheels[0].wheel.topMeshCollider.sharedMesh = BoxMesh(new Vector3(0, .2f, 0), new Vector3(.268f, .436f, .836f)));
        Refuse(r => r.Wheels[0].wheel.bottomMeshCollider.sharedMesh!.bounds.size.z += .01f);
        Refuse(r => r.Wheels[0].wheel.topMeshCollider.convex = false);
        Refuse(r => r.Wheels[0].wheel.topMeshCollider.sharedMesh!.bounds.size.x = 0, true);
        Refuse(r => r.Wheels[0].transform.localPosition.x = 1.3f, true);
        Refuse(r => r.Car.transform.localScale.x = 2, true);
        Refuse(r => r.Car.vehicleCollider!.transform.localScale.x = float.NaN, true);
        // Body AABB admits this, but its diamond's actual convex projection excludes the wheels.
        Refuse(r => {
            var mesh = ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!;
            mesh.Data = new[] { new Vector3(-1.25f, .3f, 0), new Vector3(0, .3f, -3), new Vector3(1.25f, .3f, 0), new Vector3(0, .3f, 3),
                new Vector3(-1.25f, 3.1f, 0), new Vector3(0, 3.1f, -3), new Vector3(1.25f, 3.1f, 0), new Vector3(0, 3.1f, 3) };
        }, true, true);
        // A separate body must not bridge gaps through a joined convex hull.
        Refuse(r => {
            var a = (BoxCollider)r.Car.vehicleCollider!; a.center.x = -1.1f; a.size.x = .3f;
            var b = Child(r.Car.transform, "additional").Add<BoxCollider>(); b.center = new Vector3(1.1f, 1.7f, 0); b.size = new Vector3(.3f, 2.8f, 6); r.Car.additionalCollider = b;
        }, true);
        var rig = new Rig(); var optional = Child(rig.Car.transform, "additional").Add<BoxCollider>();
        optional.center = new Vector3(0, 1.5f, 0); rig.Car.additionalCollider = optional; var cert = rig.Capture();
        optional.enabled = false; Check(cert.Validate() != null, "optional body disabled");
    }

    // Project onto a plane only when outside; test points violate at most this plane.
    private static Vector3 Clip(Vector3 p, Vector3 normal, float limit)
    {
        var excess = p.x * normal.x + p.y * normal.y + p.z * normal.z - limit;
        return excess > 0 ? p - normal * (excess / normal.sqrMagnitude) : p;
    }
    private static void Cooked()
    {
        // Source hull and bounds remain wide while the independently controlled cooked hull shrinks.
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).CookedClosestPoint = p =>
            new Vector3(Math.Clamp(p.x, -.95f, .95f), p.y, p.z), true, true);
        // The unpadded yaw reach fits this box; the mandatory tilt padding does not.
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh =
            BoxMesh(new Vector3(0, 1.7f, 0), new Vector3(2.1f, 2.8f, 6)), true, true);
        // The wide roof keeps the raw projection wide; the actual low band excludes wheels.
        Refuse(r => {
            var c = (MeshCollider)r.Car.vehicleCollider!;
            for (var i = 0; i < c.sharedMesh!.Data.Length; i++)
                if (c.sharedMesh.Data[i].y < 1) c.sharedMesh.Data[i].x *= .4f;
            c.CookedClosestPoint = p => Clip(p, new Vector3(Math.Sign(p.x), -.75f / 2.8f, 0), .5f - .3f * .75f / 2.8f);
        }, true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).CookedClosestPoint = p => new Vector3(float.NaN, 0, 0), true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).CookedClosestPoint = p => new Vector3(float.PositiveInfinity, 0, 0), true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).Throw = true, true, true);
        Refuse(r => r.Car.vehicleCollider!.enabled = false, true, true);
        Refuse(r => r.Car.gameObject.activeSelf = false, true, true);
        Refuse(r => { var b = (BoxCollider)r.Car.vehicleCollider!; b.size.y = .14f; }, true);

        // Clip just the body's outer corners: a whole-truck bounding box would fail, but
        // each separate wheel prism fits in this convex octagonal extrusion.
        var rig = new Rig(true); var mc = (MeshCollider)rig.Car.vehicleCollider!;
        var polygon = new[] { new Vector3(-1.25f, 0, -2.05f), new Vector3(-.3f, 0, -3), new Vector3(.3f, 0, -3),
            new Vector3(1.25f, 0, -2.05f), new Vector3(1.25f, 0, 2.05f), new Vector3(.3f, 0, 3),
            new Vector3(-.3f, 0, 3), new Vector3(-1.25f, 0, 2.05f) };
        var data = new Vector3[16]; var indices = new List<int>();
        for (var i = 0; i < 8; i++)
        {
            data[i] = polygon[i] + new Vector3(0, .3f, 0); data[i + 8] = polygon[i] + new Vector3(0, 3.1f, 0);
            var next = (i + 1) % 8;
            indices.AddRange(new[] { i, next, i + 8, next, next + 8, i + 8 });
            if (i > 0 && i < 7) indices.AddRange(new[] { 0, i + 1, i, 8, i + 8, i + 9 });
        }
        mc.sharedMesh!.Data = data; mc.sharedMesh.Indices = indices.ToArray();
        mc.CookedClosestPoint = p => Clip(p, new Vector3(Math.Sign(p.x), 0, Math.Sign(p.z)), 3.3f);
        var writes = Writes.Count; var c = rig.Capture(); Good(c);
        Check(mc.ClosestPointReads == 64, "every corner of all eight wheel prisms must query the cooked body");
        Check(Writes.Count == writes, "cooked certification wrote native state");
        Check(c.Min.x == -1.25f && c.Max.x == 1.25f && c.Min.y < .01f, "physical envelope was narrowed or raised");

        // A rolled default box's projection passes, but its low section does not.
        rig = new Rig(); var go = Child(rig.Car.transform, "rotated body");
        go.transform.localRotation = Quaternion.Euler(0, 0, 20); go.transform.localPosition.y = 1.7f;
        var box = go.Add<BoxCollider>(); box.size = new Vector3(2.5f, 2.8f, 6);
        rig.Car.gameObject.Components.Remove(rig.Car.vehicleCollider!); rig.Car.vehicleCollider = box;
        try { rig.Capture(); throw new Exception("rotated box accepted without a cooked prism"); }
        catch (InvalidOperationException) { Check(box.ClosestPointReads > 0, "rotated box never checked cooked geometry"); }
    }

    private static BoxCollider Trigger(float lo = .3f, float hi = .5f)
    {
        var t = new GameObject("Gley trigger").Add<BoxCollider>(); t.isTrigger = true;
        t.center = new Vector3(0, (lo + hi) * .5f, 0); t.size = new Vector3(8, hi - lo, 8); return t;
    }
    private static void Triggers()
    {
        var rig = new Rig(true); var c = rig.Capture(); var t = Trigger();
        Check(c.CoversTrigger(t), "valid vertical trigger refused");
        t.transform.localRotation = Quaternion.Euler(0, 40, 0); Check(c.CoversTrigger(t), "vertical yaw trigger refused");
        Check(!c.CoversTrigger(Trigger(.3f, .4f)), "short trigger accepted despite missing prism top");
        Check(!c.CoversTrigger(Trigger(.4f, .5f)), "trigger missing prism bottom accepted");
        Check(!c.CoversTrigger(Trigger(2, 3)), "high trigger accepted");
        t.transform.localRotation = Quaternion.Euler(.001f, 0, 0); Check(!c.CoversTrigger(t), "tilted trigger accepted");
        t.transform.localRotation = Quaternion.Euler(.0005f, 0, .0005f); Check(!c.CoversTrigger(t), "combined trigger tilt exceeds tolerance");
        t.transform.localRotation = Quaternion.Euler(0, 0, 1); Check(!c.CoversTrigger(t), "rolled trigger accepted");
        Check(!c.CoversTrigger(null!), "null trigger accepted");
        var unknown = new GameObject().Add<SphereCollider>(); unknown.isTrigger = true; unknown.radius = 10;
        Check(!c.CoversTrigger(unknown), "unknown trigger shape accepted");
        t = Trigger(); t.enabled = false; Check(!c.CoversTrigger(t), "disabled trigger accepted");
        t = Trigger(); t.gameObject.activeSelf = false; Check(!c.CoversTrigger(t), "inactive trigger accepted");
        t = Trigger(); t.isTrigger = false; Check(!c.CoversTrigger(t), "solid trigger accepted");
        t = Trigger(); t.size.x = 0; Check(!c.CoversTrigger(t), "zero trigger size accepted");
        t = Trigger(); t.size.y = float.NaN; Check(!c.CoversTrigger(t), "nonfinite trigger size accepted");
        t = Trigger(); t.transform.localScale.z = 0; Check(!c.CoversTrigger(t), "zero trigger scale accepted");
        t = Trigger(); t.transform.localScale.x = float.PositiveInfinity; Check(!c.CoversTrigger(t), "nonfinite trigger scale accepted");
        t = Trigger(); t.size.y = float.PositiveInfinity; Check(!c.CoversTrigger(t), "unreadable trigger accepted");

        // All prism corners, rather than root-local height alone, determine trigger coverage.
        t = Trigger(-1, 1);
        rig.Car.transform.localPosition = new Vector3(500, 0, 500);
        rig.Car.transform.localRotation = Quaternion.Euler(2, 40, 1);
        foreach (var w in rig.Wheels) SetPose(w, w.spring.maxLength, 4);
        Good(c); Check(c.CoversTrigger(t), "valid tilted root and tall trigger refused");
        foreach (var w in rig.Wheels) Check(c.ValidateWheelStep(w) == null, "tilted root cheap Step refused");
        Check(!c.CoversTrigger(Trigger(.34f, .46f)), "tilted prism corners escaped height check");
        // Capture itself also works at the translated/tilted pose with native world-space shells.
        Good(rig.Capture());
        rig.Car.transform.localRotation = Quaternion.Euler(3, 40, 0);
        Check(c.Validate() != null && !c.CoversTrigger(t), "above-limit root tilt accepted");
        foreach (var w in rig.Wheels) Check(c.ValidateWheelStep(w) != null, "above-limit root tilt accepted in cheap prefix");
        Refuse(r => r.Car.transform.localRotation = Quaternion.Euler(3, 0, 0), true);
        Refuse(r => r.Car.transform.localRotation = Quaternion.Euler(float.NaN, 0, 0), true);
    }
    private static void Filtering()
    {
        Refuse(r => r.Body.detectCollisions = false);
        Refuse(r => r.Body.includeLayers = 1);
        Refuse(r => r.Body.excludeLayers = 1);
        Refuse(r => r.Car.vehicleCollider!.includeLayers = 1);
        Refuse(r => r.Car.vehicleCollider!.excludeLayers = 1);
        Refuse(r => r.Car.vehicleCollider!.layerOverridePriority = 1);
        Refuse(r => r.Wheels[0].wheel.bottomMeshCollider.excludeLayers = 1);
        Refuse(r => r.Parking.parallelParkingCollider!.includeLayers = 1);
        Refuse(r => r.Wheels[0].wheel.bottomMeshCollider.layerOverridePriority = -1, true);
        Refuse(r => r.Wheels[0].wheel.topMeshCollider.gameObject.layer++);
        Refuse(r => r.Car.vehicleCollider!.Throw = true);
        Refuse(r => r.Body.Throw = true);
        Refuse(r => r.Body.detectCollisions = false, true);
    }
    private static void Step()
    {
        Refuse(r => r.Wheels[0].SteerAngle = 6);
        Refuse(r => r.Wheels[1].SteerAngle = -6);
        for (var i = 0; i < 4; i++)
        {
            var r = new Rig(); var c = r.Capture(); r.Wheels[i].SteerAngle = 6;
            Check(c.ValidateWheelStep(r.Wheels[i]) != null, "actual affected wheel refused before its native step");
        }
        Refuse(r => r.Wheels[0].SteerAngle = float.NaN);
        Refuse(r => r.Wheels[0].Camber = .01f);
        Refuse(r => r.Wheels[0].Damage = .001f);
        Refuse(r => r.Wheels[0].Damage = .001f, true);
        Refuse(r => r.Wheels[0].Camber = .001f, true);
        Refuse(r => r.Wheels[0].Radius += .001f);
        Refuse(r => r.Wheels[0].Width += .001f);
        Refuse(r => r.Wheels[0].spring.maxLength += .001f);
        Refuse(r => r.Wheels[0].spring.length = .351f);
        Refuse(r => r.Wheels[0].spring.length = -.001f);
        Refuse(r => r.Wheels[0].spring.length = float.NaN);
        Refuse(r => r.Wheels[0].wheel.localPosition.x = .001f);
        Refuse(r => r.Wheels[0].wheel.localPosition.z = .001f);
        Refuse(r => r.Wheels[0].transform.localPosition.x += .001f);
        Refuse(r => r.Wheels[0].transform.localRotation = Quaternion.Euler(0, 1, 0));
        Refuse(r => r.Wheels[0].transform.localRotation = Quaternion.Euler(1, 0, 0), true);
        Refuse(r => r.Wheels[0].transform.localScale.x = 1.01f);
        Refuse(r => r.Wheels[0].wheel.colliderTransform.localScale.z = 1.01f);
        Refuse(r => r.Wheels[0].wheel.colliderTransform.localPosition.z += .001f);
        Refuse(r => r.Wheels[0].wheel.colliderTransform.localRotation = Quaternion.Euler(1, 0, 0));
        Refuse(r => r.Wheels[0].wheel.colliderTransform.localRotation = Quaternion.Euler(0, 6, 0));
        Refuse(r => r.Wheels[0].wheel.colliderTransform.parent = r.Wheels[1].transform);
        var rig = new Rig(); var cert = rig.Capture();
        Check(cert.ValidateWheelStep(new GameObject().Add<WheelController>()) != null, "unknown Step accepted");
        rig = new Rig(true); cert = rig.Capture();
        ((MeshCollider)rig.Car.vehicleCollider!).cookingOptions ^= MeshColliderCookingOptions.WeldColocatedVertices;
        Check(cert.ValidateWheelStep(rig.Wheels[0]) != null, "body cooking change accepted in cheap prefix");
        rig = new Rig(); cert = rig.Capture();
        rig.Wheels[0].wheel.bottomMeshCollider.cookingOptions ^= MeshColliderCookingOptions.WeldColocatedVertices;
        Check(cert.ValidateWheelStep(rig.Wheels[0]) != null, "affected wheel cooking change accepted in cheap prefix");
    }
    private static void Ground()
    {
        var r = new Rig(); var c = r.Capture(); Check(c.TryGround(out var y) && y == 0, "level ground refused");
        r.Wheels[0].HitPoint.y = .019f; Check(c.TryGround(out y) && y > 0 && y < .02f, "small level variance");
        r.Wheels[0].HitPoint.y = .021f; Check(!c.TryGround(out _), "uneven ground accepted"); r.Wheels[0].HitPoint.y = 0;
        r.Wheels[0].HitNormal.y = .99f; Check(!c.TryGround(out _), "slope accepted"); r.Wheels[0].HitNormal.y = 1;
        r.Wheels[0].IsGrounded = false; Check(!c.TryGround(out _), "ungrounded wheel accepted"); r.Wheels[0].IsGrounded = true;
        r.Wheels[0].HitCollider = r.Wheels[1].wheel.topMeshCollider; Check(!c.TryGround(out _), "own wheel support"); r.Wheels[0].HitCollider = r.Road;
        r.Wheels[0].HitPoint.x = float.NaN; Check(!c.TryGround(out _), "nonfinite support"); r.Wheels[0].HitPoint.x = 0;
        r.Road.gameObject.Add<Rigidbody>(); Check(!c.TryGround(out _), "dynamic support accepted");
    }
    private static void Limits()
    {
        Refuse(r => { for (var i = 0; i < 119; i++) Child(r.Car.transform, "extra").Add<BoxCollider>(); }, true);
        Refuse(r => { while (r.Car.vehicleController.powertrain.wheels.Count < 65) r.Car.vehicleController.powertrain.wheels.Add(r.Car.vehicleController.powertrain.wheels[0]); }, true);
        Refuse(r => r.Car.vehicleController.powertrain.wheels.RemoveAt(3), true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Data = new Vector3[4097], true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Data = new Vector3[2], true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Data[0].x = -10, true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.subMeshCount = 2, true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Indices = new int[1539], true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Indices = Array.Empty<int>(), true, true);
        Refuse(r => ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!.Indices[0] = -1, true, true);
    }
    private static void Allocations()
    {
        var r = new Rig(true); var c = r.Capture(); var t = Trigger();
        for (var i = 0; i < 3000; i++) { Good(c); Check(c.ValidateWheelStep(r.Wheels[i % 4]) == null && c.TryGround(out _) && c.CoversTrigger(t), "warmup"); }
        var mesh = ((MeshCollider)r.Car.vehicleCollider!).sharedMesh!;
        var reads = mesh.TriangleReads; var vertexReads = c.MeshVertexReads; var full = c.FullValidations;
        for (var i = 0; i < 100; i++)
            Check(c.ValidateWheelStep(r.Wheels[i % 4]) == null && c.TryGround(out _) && c.CoversTrigger(t), "cheap guards");
        Check(reads == mesh.TriangleReads && vertexReads == c.MeshVertexReads && full == c.FullValidations,
            "cheap guards repeated full mesh/inventory validation");
        var writes = Writes.Count; var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        { if (c.Validate() != null || c.ValidateWheelStep(r.Wheels[i % 4]) != null || !c.TryGround(out _) || !c.CoversTrigger(t)) throw new Exception("steady validation failed"); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(allocated == 0, "steady allocations: " + allocated); Check(Writes.Count == writes, "steady writes");
        Check(mesh.TriangleReads - reads == 10000 && c.MeshVertexReads - vertexReads == 10000 && c.FullValidations - full == 10000,
            "full mesh comparison must happen exactly once per observation");
    }
    public static int Main(string[] args)
    {
        try
        {
            switch (args[0])
            { case "normal": Normal(); break; case "identities": Identities(); break; case "geometry": Geometry(); break; case "filtering": Filtering(); break;
              case "step": Step(); break; case "ground": Ground(); break; case "limits": Limits(); break; case "allocations": Allocations(); break;
              case "cooked": Cooked(); break; case "triggers": Triggers(); break; default: throw new Exception("unknown scenario"); }
            Console.WriteLine("PASS: " + args[0]); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
