using System;
using System.Linq;
using FoodTrucks.Core.CrewDriving;
using FoodTrucks;
using UnityEngine;
using GleyTrafficSystem;
using GleyUrbanAssets;
class Tests {
 static int n;static void Check(bool ok,string label){if(!ok)throw new Exception(label);n++;}
 static (CarController,CurrentSceneData,TrafficLightsIntersection) World(bool red=false,int carLayer=12){
 var vgo=new GameObject(){layer=9};var v=vgo.AddComponent<VehicleComponent>();v.rb=vgo.AddComponent<Rigidbody>();var tgo=new GameObject(){layer=9};var trigger=tgo.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.attachedRigidbody=v.rb;v.frontTrigger=tgo.transform;tgo.transform.parent=v.transform;
 TrafficManager.Instance=new TrafficManager();TrafficManager.Instance.Pool.List.Add(v);
 var scene=new GameObject().AddComponent<CurrentSceneData>();scene.allWaypoints=new Waypoint[10];for(int i=0;i<10;i++){var w=new Waypoint(){listIndex=i,position=new Vector3(0,0,i*10)};if(i<9)w.neighbors.Add(i+1);scene.allWaypoints[i]=w;}
 var x=new TrafficLightsIntersection();x.stopWaypoints.Add(new IntersectionStopWaypointsIndex(){roadWaypoints=new(){3}});x.exitWaypoints.Add(5);scene.allLightsIntersections=new[]{x};scene.allWaypoints[3].Junction(x,true);scene.allWaypoints[3].stop=red;scene.allWaypoints[5].Junction(x,false);
 UnityEngine.Object.Scene=scene;Resources.Setup=new LayerSetup();var go=new GameObject(){layer=carLayer};var car=go.AddComponent<CarController>();var body=go.AddComponent<BoxCollider>();body.center=new Vector3(0,1,1);body.size=new Vector3(2,2,6);return(car,scene,x);
 }
 static bool Create(CarController car,out CrewProbeTraffic? traffic,out Vector3[] points,out string why)=>CrewProbeTraffic.TryCreate(car,new[]{0,1,2,3,4,5,6,7,8,9},-1,5,1,2,out traffic,out points,out why);
 static void Move(CarController car,CrewProbeTraffic t,float rear,out string? failure){car.transform.position=new Vector3(0,0,rear+1);t.Observe(rear,1,out _,out _,out failure);}
 static void VisibilityMasks(){
  const int bodyBit=1<<12;
  foreach(var player in new[]{false,true}){
   var (car,_,_)=World();
   var model=TrafficManager.Instance.Pool.List[0];
   var obstacles=player?0:bodyBit;var players=player?bodyBit:0;
   model.SetMasks(obstacles,players,bodyBit);
   Check(!Create(car,out _,out _,out var why)&&why.Contains("invisible"),
    player?"building/player overlap rejected":"building/obstacle overlap rejected");
   Check(car.gameObject.layer==12&&model.gameObject.layer==9,"overlap refusal does not mutate layers");
   model.SetMasks(obstacles,players,0);
   Check(Create(car,out var traffic,out _,out why),why);
   traffic!.Observe(-1,0,out _,out _,out var failure);
   Check(failure==null,"non-building mask admitted and observed");
   model.SetMasks(obstacles,players,bodyBit);
   traffic.Observe(-1,0,out _,out _,out failure);
   Check(failure!=null&&failure.Contains("visibility changed"),"building overlap introduced after admission faults");
   traffic.Dispose();
  }
  foreach(var rootLayer in new[]{9,12}){
   var (car,_,_)=World(false,rootLayer);
   var model=TrafficManager.Instance.Pool.List[0];
   model.gameObject.layer=rootLayer;
   model.SetMasks(1<<rootLayer,1<<rootLayer,1<<rootLayer);
   var body=car.GetComponentsInChildren<Collider>(true)[0];
   Check(!Create(car,out _,out _,out var why)&&why.Contains("invisible"),"same-root collider without rigidbody rejected despite mask overlap");
   body.attachedRigidbody=car.gameObject.AddComponent<Rigidbody>();
   Check(Create(car,out var traffic,out _,out why),why);
   traffic!.Observe(-1,0,out _,out _,out var failure);
   Check(failure==null&&traffic.ColliderOverrides==0,"same-root rigidbody precedes buildings including root outside setup traffic mask");
   body.attachedRigidbody=null;
   traffic.Observe(-1,0,out _,out _,out failure);
   Check(failure!=null&&failure.Contains("visibility changed"),"same-root rigidbody loss faults for actual model root");
   traffic.Dispose();
  }
  {
   var (car,_,_)=World();var setup=(LayerSetup)Resources.Setup!;
   setup.buildingsLayers=bodyBit;
   Check(!Create(car,out _,out _,out var why)&&why.Contains("invisible"),"future pool setup also preserves building precedence");
   setup.buildingsLayers=0;
   Check(Create(car,out var traffic,out _,out why),why);
   setup.buildingsLayers=bodyBit;
   traffic!.Observe(-1,0,out _,out _,out var failure);
   Check(failure!=null&&failure.Contains("visibility changed"),"future pool setup building overlap introduced after admission faults");
   traffic.Dispose();
  }
 }
 static void CertificateVisibility(){
  var (car,_,_)=World();var body=car.GetComponentsInChildren<Collider>(true)[0];car.vehicleCollider=body;
  var model=TrafficManager.Instance.Pool.List[0];var trigger=model.frontTrigger.GetComponentsInChildren<Collider>(true)[0];
  var rb=model.rb;
  var wheel=new GameObject(){layer=2}.AddComponent<BoxCollider>();wheel.size=new Vector3(.1f,.1f,.1f);
  var cert=new CrewProbeBody {Own=new[]{body,wheel},Wheel=wheel,Min=new Vector3(-1,-.5f,-2),Max=new Vector3(1,2,4)};
  bool Start(out CrewProbeTraffic? t,out string why)=>CrewProbeTraffic.TryCreate(car,new[]{0,1,2,3,4,5,6,7,8,9},-1,5,1,2,out t,out _,out why,bodyContract:cert);
  Check(Start(out var t,out var why),"exact certified wheel visibility exception: "+why);
  t!.Observe(-1,0,out _,out _,out var failure);Check(failure==null,"certificate observes");
  Physics.PairIgnore=true;t.Observe(-1,0,out _,out _,out failure);Check(failure!=null&&failure.Contains("pair ignores"),"pair ignore mutation fails closed");Physics.PairIgnore=false;t.Dispose();
  cert.Wheel=null;Check(!Start(out t,out why)&&why.Contains("invisible"),"unknown invisible solid has no exception");cert.Wheel=wheel;
  trigger.excludeLayers=1<<12;Check(!Start(out t,out why)&&why.Contains("overrides"),"trigger override refused");trigger.excludeLayers=0;
  rb.detectCollisions=false;Check(!Start(out t,out why)&&why.Contains("rigidbody"),"trigger collision disabled refused");rb.detectCollisions=true;
  trigger.enabled=false;Check(!Start(out t,out why)&&why.Contains("usable"),"active pooled model needs live trigger");trigger.enabled=true;
  trigger.gameObject.transform.position=new Vector3(0,-5,0);Check(!Start(out t,out why)&&why.Contains("height"),"nearby trigger must overlap authoritative body height");trigger.gameObject.transform.position=Vector3.zero;
  Check(Start(out t,out why),why);
  var road=new GameObject().AddComponent<BoxCollider>();road.center=new Vector3(0,-.5f,0);road.size=new Vector3(1,1,1);road.RaycastGround=true;Physics.Obstacle=road;
  t!.Observe(-1,0,out _,out var gap,out failure);Check(failure==null&&gap==null,"native support plane permits flat road despite full suspension envelope");
  road.center=new Vector3(0,-.3f,0);t.Observe(-1,0,out _,out gap,out failure);Check(failure==null&&gap==0,"raised compound support remains blocker");Physics.Obstacle=null;t.Dispose();
  car.transform.rotation=new Quaternion {pitch=-.049f};
  Check(Start(out t,out why),why);
  var overhead=new GameObject().AddComponent<BoxCollider>();overhead.center=new Vector3(0,2.18f,12);overhead.size=new Vector3(.1f,.04f,.1f);Physics.Obstacle=overhead;
  t!.Observe(-1,0,out _,out gap,out failure);Check(failure==null&&gap!=null&&t.BlockerQuery=="piece","pitched upper-front envelope detects overhead obstacle in lookahead, not only current body");Physics.Obstacle=null;t.Dispose();car.transform.rotation=Quaternion.identity;
  Check(Start(out t,out why),why);cert.GroundKnown=false;t!.Observe(-1,0,out _,out gap,out failure);Check(failure!=null&&gap==0,"unknown native wheel support fails closed");t.Dispose();
 }
 static void RoadsideClearance(){
  // diagnostic-long/obstacle-037.json and tuned-long/obstacle-046.json,
  // each translated from its recorded truck root to a straight route at the origin.
  // The integrated body harness has no overlap simulation; use this harness's
  // deterministic box overlap with the recorded certified physical dimensions.
  var (car,scene,_)=World();
  foreach(var waypoint in scene.allWaypoints)waypoint.position.z-=1.8f;
  var min=new Vector3(-1.23101687f,-.417f,-3.15f);var max=new Vector3(1.26f,3.1f,2.65f);
  var body=(BoxCollider)car.GetComponentsInChildren<Collider>(true)[0];body.center=(min+max)/2;body.size=max-min;car.vehicleCollider=body;
  var cert=new CrewProbeBody {Own=new[]{body},Min=min,Max=max};
  Check(CrewProbeTraffic.TryCreate(car,new[]{0,1,2,3,4,5,6,7,8,9},-1.8f,4.62228251f,1.52228272f,2.86456513f,
   out var t,out _,out var why,bodyContract:cert),why);
  var mailbox=new GameObject("roadside mailbox").AddComponent<BoxCollider>();
  mailbox.center=new Vector3(227.709f-225.324646f,.5873978f,23.6174183f-18.3097172f);
  mailbox.size=new Vector3(.547821045f,1.07479572f,.592723846f);Physics.Obstacle=mailbox;
  t!.Observe(0,0,out var stop,out var gap,out var failure);
  Check(failure==null&&stop==null&&gap==null&&t.BlockerId==null,"recorded roadside mailbox clears combined sweep footprint");
  // Inspect the actual overlap input, then move the same obstacle across its edge.
  var edge=Physics.LastCenter.x+Physics.LastHalf.x;
  var residual=mailbox.bounds.min.x-edge;
  // Use the rotated trash bag's conservative world AABB; native rerun checks its actual box.
  var trash=new GameObject("roadside trash bag").AddComponent<BoxCollider>();
  trash.center=new Vector3(227.754044f-225.325912f,.3597825f,26.18614f-21.89031f);
  trash.size=new Vector3(.8304138f,.9014699f,.7389641f);Physics.Obstacle=trash;
  t.Observe(0,0,out stop,out gap,out failure);
  Check(failure==null&&stop==null&&gap==null&&t.BlockerId==null,"recorded roadside trash bag clears combined sweep footprint");
  Check(residual>.15f&&residual<.16f,"query-derived combined footprint retains mailbox residual clearance");
  Check(trash.bounds.min.x-(Physics.LastCenter.x+Physics.LastHalf.x)>.05f,"query-derived combined footprint clears conservative trash bounds");
  Physics.Obstacle=mailbox;
  mailbox.center.x-=residual+.02f;
  t.Observe(0,0,out stop,out gap,out failure);
  Check(failure==null&&stop==null&&gap!=null&&t.BlockerQuery=="piece"&&ReferenceEquals(t.BlockerCollider,mailbox),
   "obstacle inside combined sweep footprint still blocks");
  Check(t.BlockerQueryCenter.x+t.BlockerQueryHalfExtents.x>mailbox.bounds.min.x,"blocker intersects actual adapter query");
  Physics.Obstacle=null;t.Dispose();
 }
 static void ObstacleCapture(){
  var (car,_,_)=World();Check(Create(car,out var t,out _,out var why),why);
  Check(t!.ObservationSequence==0&&t.BlockerCollider==null,"no fabricated observation at admission");
  var obstacle=new GameObject().AddComponent<BoxCollider>();obstacle.center=new Vector3(0,1,6);Physics.Obstacle=obstacle;
  t.Observe(-1,0,out _,out var gap,out var failure);
  Check(failure==null&&gap!=null&&t.BlockerQuery=="piece","capture decisive lookahead rather than clear body query");
  Check(ReferenceEquals(t.BlockerCollider,obstacle)&&t.BlockerId==obstacle.GetInstanceID(),"exact collider reference and cached identity");
  Check(t.BlockerQueryCenter==Physics.LastCenter&&t.BlockerQueryHalfExtents==Physics.LastHalf&&
   t.BlockerQueryRotation.angle==Physics.LastRotation.angle&&t.BlockerQueryRotation.pitch==Physics.LastRotation.pitch,
   "captured geometry is the decisive native overlap input");
  var center=t.BlockerQueryCenter;var boundsCenter=t.BlockerPosition;var sequence=t.ObservationSequence;
  obstacle.transform.position=new Vector3(100,0,0);
  Check(t.BlockerQueryCenter==center&&t.BlockerPosition==boundsCenter,"observation geometry does not follow live collider movement");
  obstacle.transform.position=Vector3.zero;
  t.Observe(-1,0,out _,out _,out _);
  long allocated=GC.GetAllocatedBytesForCurrentThread();
  for(int i=0;i<100;i++)t.Observe(-1,0,out _,out _,out _);
  Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"blocked observer capture allocates zero");
  Physics.Obstacle=null;t.Observe(-1,0,out _,out _,out failure);
  Check(failure==null&&t.ObservationSequence>sequence&&t.BlockerId==null&&t.BlockerCollider==null&&
   t.BlockerQueryCenter==Vector3.zero&&t.BlockerQueryHalfExtents==Vector3.zero,"clear observation resets snapshot and reference");
  Physics.Obstacle=obstacle;t.Observe(-1,0,out _,out _,out _);t.Dispose();
  Check(ReferenceEquals(t.BlockerCollider,obstacle)&&t.BlockerId==obstacle.GetInstanceID(),"last observation survives disposal for bridge read");
  t.Observe(-1,0,out _,out _,out failure);
  Check(failure!=null&&t.BlockerId==null&&t.BlockerCollider==null,"early disposed return also resets diagnostic capture");
  Physics.Obstacle=null;
 }
 static void Main(){
 RoadsideClearance();
 ObstacleCapture();
 CertificateVisibility();VisibilityMasks();
 var (car,s,x)=World(true);Check(Create(car,out var t,out var p,out var why),why);Check(t!.NativeTrafficId< -1,"negative id");p[0]=new Vector3(99,0,0);t.Observe(-1,0,out var stop,out _,out var failure);Check(failure==null&&stop==29.65f,"red boundary and detached path");
 s.allWaypoints[3].stop=false;t.Observe(-1,0,out stop,out _,out failure);Check(failure==null&&stop==null,"green leaves endpoint completion to Core");x.VehicleEnter(-1);
 for(int r=0;r<=26;r++)Move(car,t,r,out failure);Check(failure==null&&t.Registrations==1&&x.Members.Contains(-1)&&x.Members.Contains(t.NativeTrafficId),"front registration coexists");
 t.Dispose();Check(t.Retained&&t.Registrations==1&&x.Members.Contains(-1),"dispose retains body");car.transform.position=new Vector3(0,0,54);t.Retire();Check(t.Registrations==0&&x.Members.Count==1&&x.Members[0]==-1,"rear clearance leaves only own id");t.Dispose();Check(Create(car,out _,out _,out why)==false,"new probe wrong origin refused");
 (car,s,x)=World(true);Check(Create(car,out t,out _,out why),why);t!.Observe(-1,0,out _,out _,out _);for(int r=0;r<=24;r++)Move(car,t,r,out _);s.allWaypoints[3].stop=false;Move(car,t,26,out failure);Check(failure!=null&&failure.Contains("forbidden")&&t.Registrations==1,"old forbidden crossing survives green");
 t.Dispose();car.gameObject.activeInHierarchy=false;t.Retire();Check(t.Registrations==0,"destroyed/inactive body retires");
 (car,s,x)=World();s.allWaypoints[2].neighbors.Clear();Check(!Create(car,out _,out _,out why)&&why.Contains("directed"),"disconnected rejection");
 (car,s,x)=World();s.allWaypoints[3].giveWay=true;Check(!Create(car,out _,out _,out why)&&why.Contains("give-way"),"giveway rejection");
 (car,s,x)=World();s.allWaypoints[5].exit=false;Check(!Create(car,out _,out _,out why),"missing exit rejection");
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);Physics.Count=128;t!.Observe(-1,0,out stop,out var gap,out failure);Check(failure!=null&&failure.Contains("saturated")&&gap==0,"saturation holds");Physics.Count=0;t.Dispose();
 (car,s,x)=World(false,0);Check(!Create(car,out t,out _,out why)&&why.Contains("invisible")&&car.gameObject.layer==0,"invisible collider refused without layer mutation");
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);t!.Observe(-1,0,out _,out _,out _);long start=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100;i++)t.Observe(-1,0,out _,out _,out _);Check(GC.GetAllocatedBytesForCurrentThread()==start,"steady-state observer allocates zero in harness");t.Dispose();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);
 var go2=new GameObject();var car2=go2.AddComponent<CarController>();var body2=go2.AddComponent<BoxCollider>();body2.center=new Vector3(0,1,1);body2.size=new Vector3(2,2,6);
 Check(Create(car2,out var t2,out _,out why),why);Check(t!.NativeTrafficId!=t2!.NativeTrafficId,"independent traffic identities");
 for(int r=0;r<=26;r++){Move(car,t,r,out _);Move(car2,t2,r,out _);}Check(t.Registrations==1&&t2.Registrations==1&&x.Members.Count==2,"independent simultaneous membership");
 t.Dispose();Check(x.Members.Count==2,"first dispose retains both bodies");car.gameObject.activeInHierarchy=false;t.Retire();Check(x.Members.Count==1&&x.Members[0]==t2.NativeTrafficId,"first retirement preserves second");t2.Dispose();car2.gameObject.activeInHierarchy=false;t2.Retire();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);x.VehicleEnter(t!.NativeTrafficId-1);go2=new GameObject();car2=go2.AddComponent<CarController>();body2=go2.AddComponent<BoxCollider>();body2.center=new Vector3(0,1,1);body2.size=new Vector3(2,2,6);
 Check(!Create(car2,out _,out _,out why)&&why.Contains("collision"),"membership ID collision refuses before mutation");t.Dispose();
 (car,s,x)=World(false,9);var rb=car.gameObject.AddComponent<Rigidbody>();car.GetComponentsInChildren<Collider>(true)[0].attachedRigidbody=rb;Check(Create(car,out t,out _,out why),why);Check(t!.ColliderOverrides==0,"same-layer NWH rigidbody uses native dynamic branch");t.Dispose();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);Physics.Obstacle=car.GetComponentsInChildren<Collider>(true)[0];t!.Observe(-1,0,out _,out _,out failure);Check(failure==null&&t.Observed==0,"own collider filtered");Physics.Obstacle=new GameObject().AddComponent<BoxCollider>();t.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&gap==0&&t.Observed==1,"real nontrigger obstacle holds");Physics.Obstacle=null;t.Dispose();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);for(int r=0;r<=26;r++)Move(car,t!,r,out _);x.Reset();t!.Observe(26,0,out _,out _,out failure);Check(failure!=null&&failure.Contains("registry reset")&&x.Members.Contains(t.NativeTrafficId),"native reset holds and repairs occupied identity");t.Dispose();x.Reset();t.Retire();Check(x.Members.Contains(t.NativeTrafficId),"retained record repairs later reset");car.gameObject.activeInHierarchy=false;t.Retire();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);
 var ground=new GameObject().AddComponent<BoxCollider>();ground.center=new Vector3(0,-.5f,0);ground.size=new Vector3(1,1,1);ground.RaycastGround=true;
 Physics.Obstacle=ground;t!.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&gap==null&&t.Observed==0,"flat road inside padded query is below actual body");
 ground.center=new Vector3(0,-.3f,0);t.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&gap==0,"raised curb is not filtered as ground");Physics.Obstacle=null;t.Dispose();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);for(int r=0;r<=26;r++)Move(car,t!,r,out _);t!.Dispose();Check(t.Retained,"body inside junction retained");
 CrewProbeTraffic.NativeRemoving(car);Check(!t.Retained&&t.Registrations==0,"native destruction releases before Unity fake-null state");
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);var ahead=new GameObject().AddComponent<BoxCollider>();ahead.center=new Vector3(0,1,6);ahead.size=new Vector3(1,1,1);Physics.Obstacle=ahead;
 for(int i=0;i<500;i++){var noise=(i%2==0?1:-1)*.0001f;car.transform.position=new Vector3(0,0,noise);t!.Observe(-1+noise,0,out stop,out gap,out failure);if(failure!=null||stop!=null||gap==null)throw new Exception("near obstacle became signal/crossing: "+failure);}
 Check(true,"500 noisy obstacle observations retain gap without signal boundary");
 ahead.center=new Vector3(0,1,-3);t!.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&gap==null,"obstacle half a metre behind body does not block forward travel");ahead.center=new Vector3(2,1,1);t.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&gap!=null,"obstacle beside rear body remains in future swept envelope");Physics.Obstacle=null;t.Dispose();
 (car,s,x)=World(true);
 var parking=car.gameObject.AddComponent<VehicleParkingHelper>();var parkingGap=car.gameObject.AddComponent<BoxCollider>();parkingGap.size=new Vector3(100,100,100);parking.parallelParkingCollider=parkingGap;
 Check(CrewProbeTraffic.TryCreate(car,new[]{0,1,2,3,4,5,6,7,8,9},-1,5,1,2,out t,out p,out why,
    raw=>ProbeRoadPath.Prepare(raw.Select(v=>new ProbePoint(v.x,v.z)).ToArray(),new ProbeGeometry(3,5,1,2,30,2))),why);
 Check(p.Length>10&&p.Any(v=>v.z==30),"prepared path preserves original native marker among extra samples");
 Physics.Obstacle=parkingGap;t!.Observe(-1,0,out _,out gap,out failure);Check(failure==null&&gap==null&&t.BlockerId==null,"own parking proxy excluded from geometry is also excluded as an obstacle");Physics.Obstacle=null;
 t!.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&Math.Abs(stop!.Value-29.65f)<.001,"red boundary remaps to prepared distance and native mutation check uses source");
 s.allWaypoints[3].stop=false;t.Observe(-1,0,out stop,out gap,out failure);Check(failure==null&&stop==null,"prepared signal reads original native waypoint");
 s.allWaypoints[2].position=new Vector3(1,0,20);t.Observe(-1,0,out stop,out gap,out failure);Check(failure!=null&&failure.Contains("route changed"),"prepared path still detects raw native mutation");t.Dispose();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);x.VehicleEnter(-1);
 for(int r=0;r<=26;r++)Move(car,t!,r,out _);t!.Dispose();Check(t.Retained&&t.Registrations==1,"reverse takeover begins with occupied junction");
 car.transform.position=new Vector3(0,0,26.7f);t.Retire();Check(t.Retained&&x.Members.Contains(t.NativeTrafficId),"partial reversal preserves occupancy");
 car.transform.position=new Vector3(0,0,21);t.Retire();Check(!t.Retained&&t.Registrations==0&&x.Members.SequenceEqual(new[]{-1}),"whole body reversed behind entry releases only owned identity");
 car.transform.position=Vector3.zero;Check(Create(car,out t,out _,out why),"reverse clearance permits another probe: "+why);t!.Dispose();
 (car,s,x)=World();s.allLightsIntersections=Array.Empty<TrafficLightsIntersection>();s.allWaypoints=new Waypoint[11];
 for(int i=0;i<11;i++){var a=Math.PI*.5*i/10;var w=new Waypoint{listIndex=i,position=new Vector3((float)(15*(1-Math.Cos(a))),0,(float)(15*Math.Sin(a)))};if(i<10)w.neighbors.Add(i+1);s.allWaypoints[i]=w;}
 car.transform.position=new Vector3(0,0,1);
 Check(CrewProbeTraffic.TryCreate(car,Enumerable.Range(0,11).ToArray(),-1,5,1,2,out t,out p,out why,
    raw=>ProbeRoadPath.Prepare(raw.Select(v=>new ProbePoint(v.x,v.z)).ToArray(),new ProbeGeometry(3,5,1,2,30,2))),why);
 var pole=new GameObject().AddComponent<BoxCollider>();pole.size=new Vector3(.1f,2,.1f);pole.center=new Vector3((float)(15-10.5*Math.Cos(.7)),1,(float)(10.5*Math.Sin(.7)));Physics.Obstacle=pole;
 t!.Observe(0,0,out stop,out gap,out failure);Check(failure==null&&gap==null,"prepared 90 degree curve does not add giant corner squares for pole outside oriented envelope: "+failure);
 pole.center=new Vector3((float)(15-13*Math.Cos(.7)),1,(float)(13*Math.Sin(.7)));t.Observe(0,0,out stop,out gap,out failure);
 Check(failure==null&&gap!=null&&t.BlockerId==pole.GetInstanceID()&&t.BlockerLayer==pole.gameObject.layer&&t.BlockerQuery=="piece"&&t.BlockerGap==gap,"inside-curve blocker reports physical identity query and gap");
 Physics.Obstacle=null;t.Dispose();
 (car,s,x)=World();Check(Create(car,out t,out _,out why),why);t!.Observe(-1,0,out _,out _,out _);s.allWaypoints[3].stop=true;
 t.Observe(-1,8,out _,out _,out failure);Check(failure==null&&t.SignalStopChanges==1&&t.SignalChangeSpeed==8&&Math.Abs(t.SignalChangeClearance!.Value-26)<.001,"signal stop transition records measured speed and original front clearance");t.Dispose();
 Console.WriteLine($"{n} adapter harness assertions passed (stubbed native/physics; no live evidence).");
 }
}
