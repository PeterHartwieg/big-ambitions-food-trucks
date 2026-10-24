#nullable enable
using System;
using FoodTrucks;
using UnityEngine;
using GleyTrafficSystem;
using GleyUrbanAssets;

internal static class TrafficIntegrated
{
    private static int assertions;
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); assertions++; }
    private sealed class World
    {
        internal readonly Scenarios.Rig Rig = new Scenarios.Rig(true);
        internal readonly VehicleComponent Ordinary, Raised;
        internal readonly CrewProbeBody Body;
        internal World(bool inactiveRaised = false)
        {
            TrafficManager.Instance = new TrafficManager(); Resources.Setup = new LayerSetup();
            var scene = new GameObject("scene").Add<CurrentSceneData>();
            scene.allWaypoints = new[] { new Waypoint { listIndex = 0, position = new Vector3(0, 0, -1.7f) },
                new Waypoint { listIndex = 1, position = new Vector3(0, 0, 28.3f) } };
            scene.allWaypoints[0].neighbors.Add(1); scene.allLightsIntersections = Array.Empty<TrafficLightsIntersection>();
            UnityEngine.Object.Scene = scene;
            Rig.Car.gameObject.layer = 12;
            Ordinary = Model("ordinary", 0, 1); Raised = Model("raised", .36f, 1.36f);
            Raised.gameObject.activeSelf = !inactiveRaised;
            foreach (var c in Rig.Car.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = 12;
            foreach (var w in Rig.Wheels) w.wheel.colliderGO.layer = 2; // Actual wheel visibility exception is necessary.
            Body = new CrewProbeBody(Rig.Car);
            Check(!Body.TrafficCertified && Body.CookedQueries == 0, "physical capture must not require any cooked traffic slice");
        }
        internal BoxCollider Trigger(VehicleComponent v) => v.frontTrigger.GetComponentsInChildren<BoxCollider>(true)[0];
        internal bool Start(out CrewProbeTraffic? traffic, out string why) => CrewProbeTraffic.TryCreate(Rig.Car,
            new[] { 0, 1 }, -1.7f, 4.7f, 1.3f, 2.5f, out traffic, out _, out why, bodyContract: Body);
        internal CrewProbeTraffic Start()
        { Check(Start(out var t, out var why), why); return t!; }
        internal void Observe(CrewProbeTraffic t, string? expected = null)
        {
            var writes = Writes.Count;
            t.Observe(0, 0, out _, out _, out var failure);
            Check(expected == null ? failure == null : failure != null && failure.Contains(expected), failure ?? "missing " + expected);
            Check(Writes.Count == writes, "observation wrote native state");
        }
    }
    private static VehicleComponent Model(string name, float lo, float hi)
    {
        var go = new GameObject(name) { layer = 9 }; var v = go.Add<VehicleComponent>(); v.rb = go.Add<Rigidbody>();
        var front = new GameObject("front"); front.transform.parent = go.transform; v.frontTrigger = front.transform;
        var child = new GameObject("box") { layer = 9 }; child.transform.parent = front.transform;
        var c = child.Add<BoxCollider>(); c.isTrigger = true; c.center = new Vector3(0, (lo + hi) * .5f, 0); c.size = new Vector3(2, hi - lo, 5);
        TrafficManager.Instance.Pool.List.Add(v); return v;
    }
    private static void Refuse(Action<World> edit, string code)
    {
        var w = new World(true); edit(w); var writes = Writes.Count;
        Check(!w.Start(out _, out var why) && why.Contains(code), why);
        Check(Writes.Count == writes, "admission failure wrote native settings");
        Check(CrewProbeTraffic.RecordCount == 0, "failed admission acquired ownership");
        Check(w.Body.BandCandidates <= 17 && w.Body.CookedQueries <= 17 * 8 * 8, "failed search exceeded candidate or cooked query budget");
    }
    private static void Mutate(Action<World> edit, string code)
    {
        var w = new World(); using var t = w.Start(); edit(w); w.Observe(t, code);
    }
    private static object? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    private static void Inspection()
    {
        var w = new World();var writes = Writes.Count;var owners = CrewProbeTraffic.RecordCount;
        var result = CrewProbeTraffic.InspectCoverage(w.Rig.Car);
        Check((bool)Property(result,"complete")! && (bool)Property(result,"selected")! && Property(result,"failure")==null,"read-only common-band inspection");
        Check(Writes.Count==writes && CrewProbeTraffic.RecordCount==owners,"inspection acquired ownership or wrote native settings");
        w.Trigger(w.Raised).center.y=2.5f;
        result=CrewProbeTraffic.InspectCoverage(w.Rig.Car);
        var failure=Property(result,"failure")!;
        Check((string)Property(failure,"code")! == "common_band_empty", "structured exact failure code");
        var detail=(string)Property(failure,"detail")!;
        Check(detail.Contains("pool=1")&&detail.Contains("collider=")&&detail.Contains("path=")&&detail.Contains("rootHeightDelta=")&&detail.Contains("upper="),"limiting profile diagnostics identify inactive/active model and collider");
        TrafficManager.IsInitialized=false;
        var body=new CrewProbeBody(w.Rig.Car);Check(body.Validate()==null&&!body.TrafficCertified,"independent physical capture without initialized Gley");
        result=CrewProbeTraffic.InspectCoverage(w.Rig.Car);
        Check(!(bool)Property(result,"complete")! && !(bool)Property(result,"selected")!,"uninitialized inventory is explicit");
        TrafficManager.IsInitialized=true;
    }
    private static void ReceiverOwnership()
    {
        Refuse(x=>x.Trigger(x.Raised).gameObject.Add<Rigidbody>(), "trigger_rigidbody_ownership_changed");
        Refuse(x=>x.Raised.frontTrigger.gameObject.Add<Rigidbody>(), "trigger_rigidbody_ownership_changed");
        Refuse(x=>x.Trigger(x.Ordinary).gameObject.Add<Rigidbody>(), "trigger_rigidbody_ownership_changed");
        Refuse(x=>x.Trigger(x.Raised).gameObject.Add<VehicleComponent>(), "trigger_receiver_changed");
        Refuse(x=>x.Raised.rb=null!, "gley_receiver_rigidbody_changed");
        Refuse(x=>x.Raised.rb=new GameObject("foreign body").Add<Rigidbody>(), "gley_receiver_rigidbody_changed");
        Refuse(x=>x.Raised.gameObject.Components.Remove(x.Raised.rb), "gley_receiver_rigidbody_changed");
        Refuse(x=>x.Trigger(x.Ordinary).AttachmentOverride=new GameObject("wrong live attachment").Add<Rigidbody>(), "trigger_attached_rigidbody_changed");
        Mutate(x=>x.Trigger(x.Ordinary).gameObject.Add<Rigidbody>(), "trigger_rigidbody_ownership_changed");
        Mutate(x=>x.Ordinary.frontTrigger.gameObject.Add<Rigidbody>(), "trigger_rigidbody_ownership_changed");
        Mutate(x=>x.Ordinary.rb=new GameObject("replacement body field").Add<Rigidbody>(), "gley_receiver_rigidbody_changed");
        Mutate(x=> {x.Ordinary.gameObject.Components.Remove(x.Ordinary.rb);x.Ordinary.rb=x.Ordinary.gameObject.Add<Rigidbody>();}, "gley_receiver_rigidbody_changed");
        Mutate(x=>x.Ordinary.gameObject.Components.Remove(x.Ordinary.rb), "gley_receiver_rigidbody_changed");
        Mutate(x=> {x.Ordinary.gameObject.Components.Remove(x.Ordinary);x.Ordinary.gameObject.Add<VehicleComponent>();}, "gley_receiver_rigidbody_changed");
        Mutate(x=>x.Trigger(x.Ordinary).gameObject.Add<VehicleComponent>(), "trigger_receiver_changed");
        Mutate(x=>x.Trigger(x.Ordinary).AttachmentOverride=new GameObject("wrong live attachment").Add<Rigidbody>(), "trigger_attached_rigidbody_changed");
        var w=new World(true);w.Trigger(w.Raised).enabled=false;
        using(var t=w.Start())
        {
            w.Trigger(w.Raised).gameObject.Add<Rigidbody>();w.Observe(t,"trigger_rigidbody_ownership_changed");
        }
        w=new World(true);w.Trigger(w.Raised).enabled=false;
        using(var t=w.Start())
        {
            w.Trigger(w.Raised).AttachmentOverride=new GameObject("future wrong attachment").Add<Rigidbody>();
            w.Observe(t); // Native attachedRigidbody is unavailable while inactive/disabled.
            w.Raised.gameObject.activeSelf=true;w.Trigger(w.Raised).enabled=true;
            w.Observe(t,"trigger_attached_rigidbody_changed");
        }
    }
    private static void YawRounding()
    {
        foreach (var uniform in new[] { .001f, 2f })
        {
        var w=new World();
        var parent=new GameObject("uniform parent");parent.transform.localScale=new Vector3(uniform,uniform,uniform);
        w.Ordinary.transform.parent=parent.transform;
        w.Ordinary.transform.MatrixDerivedScale=true;
        var box=w.Trigger(w.Ordinary);box.center=new Vector3(0,.5f/uniform,0);
        box.size=new Vector3(200/uniform,1/uniform,500/uniform); // Magnifies derived-dimension rounding.
        using(var t=w.Start())
        {
            var captured=w.Ordinary.transform.lossyScale;var noisy=false;
            var cooked=w.Body.CookedQueries;var candidates=w.Body.BandCandidates;
            var writes=Writes.Count;
            for(var yaw=-180f;yaw<=180f;yaw+=.37f)
            {
                w.Ordinary.transform.localRotation=Quaternion.Euler(0,yaw,0);
                w.Ordinary.frontTrigger.localRotation=Quaternion.Euler(0,yaw*.13f,0);
                noisy |= w.Ordinary.transform.lossyScale != captured;
                t.Observe(0,0,out _,out _,out var failure);Check(failure==null,failure??"");
            }
            Check(noisy,"float world-matrix yaw decomposition must actually change derived scale bits");
            Check(Writes.Count==writes&&w.Body.CookedQueries==cooked&&w.Body.BandCandidates==candidates,"yaw sweep wrote or recertified");
            parent.transform.localScale.x=MathF.BitIncrement(parent.transform.localScale.x);
            w.Observe(t,"trigger_authored_root_scale_changed");
        }
        }
        // Even a one-ULP authored edit stays forbidden; tolerance is for derived values only.
        Mutate(x=>x.Ordinary.transform.localScale.x=MathF.BitIncrement(1f),"trigger_authored_root_scale_changed");
        Mutate(x=>x.Trigger(x.Ordinary).size.x=MathF.BitIncrement(x.Trigger(x.Ordinary).size.x),"trigger_shape_scale_changed");
        Mutate(x=>x.Ordinary.transform.parent=new GameObject("new parent").transform,"trigger_root_reparented");
    }
    private static void DiagnosticPurity()
    {
        var unreadable=new World();
        using(var t=unreadable.Start())
        {
            unreadable.Ordinary.frontTrigger=new GameObject("replacement").transform;
            unreadable.Ordinary.gameObject.ThrowActiveRead=true;
            var writes=Writes.Count;
            t.Observe(0,0,out _,out _,out var detailFailure);
            Check(detailFailure!=null&&detailFailure.Contains("front_root_replaced")&&detailFailure.Contains("diagnostics_unavailable="),"throwing native diagnostic reads must preserve the actual reason code");
            Check(Writes.Count==writes,"diagnostic fallback wrote native settings");
        }
        var w=new World(true);w.Raised.transform.position=new Vector3(0,100,0);
        var result=CrewProbeTraffic.InspectCoverage(w.Rig.Car);
        Check(Property(result,"failure")==null && (bool)Property(result,"selected")!,"inactive elevated profile must not refuse common-band selection");
        Check(Property(result,"failing_shell")==null && Property(result,"failing_corner")==null && Property(result,"failing_point")==null,"successful inspection must not acquire irrelevant failure metadata");
        Check(!(bool)Property(result,"masks_checked")!,"coverage inspection explicitly reports unchecked native masks");
        w.Ordinary.transform.position=new Vector3(0,1,0);
        result=CrewProbeTraffic.InspectCoverage(w.Rig.Car);
        var failure=Property(result,"failure")!;
        Check((string)Property(failure,"code")! == "trigger_root_height_offset" && ((string)Property(failure,"detail")!).Contains("pool=0"),"real first refusal must survive later inactive noncovering report");
        Check(Property(result,"failing_shell")==null && Property(result,"failing_corner")==null,"height-domain failure must not acquire another trigger's corner");
        // An actual prism refusal's cached corner is also unchanged by later pure queries.
        Check(w.Body.SelectBand(.36f,1f)==null,"select diagnostic test band");
        w.Body.RecordCoverageFailure(w.Body.WorldBandMax+1,w.Body.WorldBandMax+2);
        var shell=w.Body.FailingShell;var corner=w.Body.FailingCorner;var point=w.Body.FailingPoint;
        Check(!w.Body.CoversInterval(-100,-99),"unrelated noncovering query");
        Check(w.Body.FailingShell==shell&&w.Body.FailingCorner==corner&&w.Body.FailingPoint==point,"pure query replaced latched coverage failure");
        Check(w.Body.SelectBand(.36f,1f)==null&&w.Body.FailingCorner==-1&&w.Body.FailingPoint==default(Vector3),"successful reselection clears all fault metadata");
        Check(w.Body.SelectBand(3,4)=="common_band_empty"&&w.Body.FailingAuthority==0&&w.Body.FailingShell==-1&&w.Body.FailingPoint==default(Vector3),"empty selection clears previous failure metadata");
        w=new World();((MeshCollider)w.Rig.Car.vehicleCollider!).CookedClosestPoint=p=>new Vector3(0,p.y,p.z);
        result=CrewProbeTraffic.InspectCoverage(w.Rig.Car);failure=Property(result,"failure")!;
        Check(!(bool)Property(result,"selected")!&&Property(result,"band_min")==null&&Property(result,"band_max")==null&&Property(result,"attempted_band_min")!=null,"failed cooked search reports an attempted candidate rather than a selected band");
        Check(!((string)Property(failure,"detail")!).Contains("limiting_floor")&&(string)Property(result,"failure_stage")! == "cooked_corner","cooked failure identifies its own proof stage, not limiting models");
        w=new World();((MeshCollider)w.Rig.Car.vehicleCollider!).sharedMesh=Scenarios.BoxMesh(new Vector3(0,1.7f,0),new Vector3(.5f,2.8f,6));
        result=CrewProbeTraffic.InspectCoverage(w.Rig.Car);
        Check((string)Property(result,"failure_stage")! == "outline"&&Property(result,"failing_corner")==null&&Property(result,"failing_point")==null,"outline failure has no stale cooked corner");
    }
    private static void Selection()
    {
        var w = new World(true); var disabled = w.Trigger(w.Raised); disabled.enabled = false;
        var writes = Writes.Count;
        using (var t = w.Start())
        {
            Check(w.Body.BandMin > .55f && w.Body.BandMax < .85f, "all profiles including inactive disabled raised floor choose common higher band");
            Check(w.Body.BandCandidates <= 17 && w.Body.CookedQueries == 64, "one eight-corner cooked proof per shell");
            Check(Writes.Count == writes, "admission wrote native settings");
            w.Raised.gameObject.activeSelf = true; disabled.enabled = true; w.Observe(t);
            Check(w.Body.Min.y < .01f && w.Body.Max.y >= 3.1f, "physical envelope remains full suspension/body");
        }
        // The old low slice is invalid, yet the selected higher cooked slice is valid.
        w = new World(); var mesh = (MeshCollider)w.Rig.Car.vehicleCollider!;
        mesh.CookedClosestPoint = p => p.y < .55f ? new Vector3(Math.Clamp(p.x, -.5f, .5f), p.y, p.z) : p;
        using (var t = w.Start()) w.Observe(t);
        // Midpoint fails. A deterministic endpoint candidate fits, without reselection later.
        w = new World(); mesh = (MeshCollider)w.Rig.Car.vehicleCollider!;
        mesh.CookedClosestPoint = p => p.y < .665f ? new Vector3(0, p.y, p.z) : p;
        using (var t = w.Start()) { Check(w.Body.BandCandidates > 1 && w.Body.BandCandidates <= 17, "bounded search reaches a higher cooked band"); w.Observe(t); }
        Refuse(x => { var c=x.Trigger(x.Raised);c.center.y=5.5f; }, "common_band_empty");
        Refuse(x => { var c=x.Trigger(x.Raised);c.center.y=2.5f; }, "common_band_empty");
        Refuse(x => ((MeshCollider)x.Rig.Car.vehicleCollider!).CookedClosestPoint = p => new Vector3(Math.Clamp(p.x, -.5f, .5f), p.y, p.z), "cooked_band_search_exhausted");
        // A wide roof does not prove the slice: independently cooked taper excludes every candidate.
        Refuse(x => ((MeshCollider)x.Rig.Car.vehicleCollider!).CookedClosestPoint = p => p.y < 1 ? new Vector3(Math.Clamp(p.x, -.5f, .5f), p.y, p.z) : p, "cooked_band_search_exhausted");
        w = new World();
        Refuse(x=>x.Trigger(x.Raised).transform.localScale.y=-1,"trigger_local_transform_unsupported");
        Refuse(x=>x.Trigger(x.Raised).transform.localRotation=Quaternion.Euler(1,0,0),"trigger_local_transform_unsupported");
        Refuse(x=> {var c=x.Trigger(x.Raised);c.transform.parent!.localScale=new Vector3(2,1,1);c.transform.localRotation=Quaternion.Euler(0,45,0);},"trigger_shear_or_local_tilt");
        // Separate left/right authorities can certify separate shells, but no union is used.
        var left = (MeshCollider)w.Rig.Car.vehicleCollider!;
        left.sharedMesh = Scenarios.BoxMesh(new Vector3(-.66f, 1.7f, 0), new Vector3(1.18f, 2.8f, 6));
        var right = new GameObject("right authority");right.transform.parent=w.Rig.Car.transform;right.layer=12;
        var rc=right.Add<BoxCollider>();rc.center=new Vector3(.66f,1.7f,0);rc.size=new Vector3(1.18f,2.8f,6);w.Rig.Car.additionalCollider=rc;
        var separate=new CrewProbeBody(w.Rig.Car);
        Check(separate.SelectBand(.36f,1f)==null,"different shells may choose separate cooked authorities");
        rc.size.x=.15f; left.sharedMesh=Scenarios.BoxMesh(new Vector3(-1.1f,1.7f,0),new Vector3(.3f,2.8f,6));
        separate=new CrewProbeBody(w.Rig.Car);
        Check(separate.SelectBand(.36f,1f)=="cooked_band_search_exhausted","separate authorities cannot bridge union gaps");
    }
    private static void Runtime()
    {
        var w = new World(); using (var t = w.Start())
        {
            foreach (var offset in new[] { -.05f, .05f })
                foreach (var pose in new[] { Quaternion.Euler(2.86f,0,0), Quaternion.Euler(0,0,2.86f), Quaternion.Euler(2.02f,0,2.02f) })
                {
                    w.Ordinary.transform.position = new Vector3(4,offset,8);w.Raised.transform.position=new Vector3(-4,offset,8);
                    w.Rig.Car.transform.rotation=pose;foreach(var wheel in w.Rig.Wheels)Scenarios.SetPose(wheel,.35f,5);
                    w.Observe(t);
                }
        }
        Mutate(x=>x.Ordinary.transform.position=new Vector3(0,.05001f,0),"trigger_root_height_offset");
        Mutate(x=>x.Ordinary.transform.rotation=Quaternion.Euler(.001f,0,0),"trigger_world_tilt");
        Mutate(x=>x.Trigger(x.Ordinary).transform.localPosition.y=2,"trigger_vertical_scale_changed");
        Mutate(x=>x.Trigger(x.Ordinary).size.y+=.01f,"trigger_shape_scale_changed");
        Mutate(x=>x.Trigger(x.Ordinary).transform.localScale.x=2,"trigger_vertical_scale_changed");
        Mutate(x=>x.Ordinary.frontTrigger=new GameObject("replacement").transform,"front_root_replaced");
        Mutate(x=>x.Ordinary.frontTrigger.parent=x.Raised.transform,"front_root_reparented");
        Mutate(x=>x.Trigger(x.Ordinary).gameObject.Add<BoxCollider>(),"trigger_collider_membership_changed");
        Mutate(x=>x.Trigger(x.Ordinary).gameObject.Components.Remove(x.Trigger(x.Ordinary)),"trigger_collider_membership_changed");
        Mutate(x=> {var c=x.Trigger(x.Ordinary);c.gameObject.Components.Remove(c);var b=c.gameObject.Add<BoxCollider>();b.isTrigger=true;},"trigger_collider_membership_changed");
        Mutate(x=>x.Trigger(x.Ordinary).transform.parent=x.Raised.frontTrigger,"trigger_hierarchy_membership_changed");
        Mutate(x=> {var child=new GameObject("added node");child.transform.parent=x.Ordinary.frontTrigger;},"trigger_hierarchy_membership_changed");
        Mutate(x=>x.Trigger(x.Ordinary).enabled=false,"no usable front trigger");
        Mutate(x=>Physics.PairIgnore=true,"pair ignores");Physics.PairIgnore=false;
        // A tiny tilt below the retained tolerance has an inner interval, not an outer AABB.
        var c=new GameObject("tiny tilt").Add<BoxCollider>();c.isTrigger=true;c.size=new Vector3(100000,.1f,2);c.transform.rotation=Quaternion.Euler(0,0,.0004f);
        Check(CrewProbeTrigger.WorldInterval(c,out _,out _,out _)=="trigger_world_interval_empty","tiny tilt side-face erosion rejects empty inner slab");
    }
    private static void Steady()
    {
        var w = new World(); using var t = w.Start();
        // Supported yaw/horizontal movement includes steering front-root yaw.
        w.Ordinary.transform.position=new Vector3(10,0,8);w.Ordinary.transform.rotation=Quaternion.Euler(0,30,0);
        w.Ordinary.frontTrigger.localRotation=Quaternion.Euler(0,10,0);w.Trigger(w.Ordinary).transform.localPosition.x=.1f;
        w.Observe(t);
        for(var i=0;i<3000;i++) t.Observe(0,0,out _,out _,out _);
        var writes=Writes.Count;var cooked=w.Body.CookedQueries;var candidates=w.Body.BandCandidates;
        var full=w.Body.FullValidations;var vertices=w.Body.MeshVertexReads;var start=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++) {t.Observe(0,0,out _,out _,out var failure);if(failure!=null)throw new Exception(failure);}
        var bytes=GC.GetAllocatedBytesForCurrentThread()-start;
        Check(bytes==0,"actual traffic/body steady allocations: "+bytes);
        Check(w.Body.CookedQueries==cooked&&w.Body.BandCandidates==candidates,"steady path cooked calls or reselection");
        Check(w.Body.FullValidations-full==10000&&w.Body.MeshVertexReads-vertices==10000,"full body geometry checked exactly once per Observe");
        Check(Writes.Count==writes,"steady native writes");
    }
    public static int Main()
    {
        try { Inspection();ReceiverOwnership();YawRounding();DiagnosticPurity();Selection();Runtime();Steady();Console.WriteLine($"integrated trigger assertions passed: {assertions}");return 0; }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
