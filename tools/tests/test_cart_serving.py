"""Exercise the real cart adapter against controlled navigation, without Unity or game assemblies."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CartServingTests(unittest.TestCase):
    def test_placement_fallback_hold_and_release(self):
        with tempfile.TemporaryDirectory(prefix='ft-cart-serving-') as folder:
            root = Path(folder)
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            source = escape(str(ROOT / 'unity/FoodTrucks/Scripts/CartServing.cs'))
            ground = escape(str(ROOT / 'unity/FoodTrucks/Scripts/CartGroundPlacement.cs'))
            stance = escape(str(ROOT / 'Core/Fleet/CartStance.cs'))
            world = escape(str(ROOT / 'unity/FoodTrucks/Scripts/CartStanceWorld.cs'))
            (root / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup><Compile Include="{source}" Link="CartServing.cs" /><Compile Include="{ground}" Link="CartGroundPlacement.cs" /><Compile Include="{stance}" Link="CartStance.cs" /><Compile Include="{world}" Link="CartStanceWorld.cs" /></ItemGroup></Project>''')
            (root / 'Program.cs').write_text(HARNESS)
            built = subprocess.run(['dotnet', 'build', str(root / 'Harness.csproj'), '--nologo', '-v:q',
                                    '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                                   capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(root / f'bin/Debug/net{major}.0/Harness.dll')],
                                 capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('PASS: cart serving pavement, reachable placements, fallback, idle hold and release', ran.stdout)


# Purpose-built fakes, not a game implementation. SamplePosition sees a controlled set of walkable
# points; the event represents either click navigation or WASD. Real baked mesh/physics remain in CHECKS.
HARNESS = r'''
using UnityEngine;
using UnityEngine.AI;
using Helpers;
using FoodTrucks;

namespace UnityEngine {
    public class Object { public static int Destroyed; public static void Destroy(Object value) => Destroyed++; }
    public class GameObject : Object { public Transform transform = new(); public GameObject(string name) {} }
    public class Transform : System.Collections.Generic.IEnumerable<Transform> {
        public string name=""; public Vector3 localPosition;
        public Dictionary<string,Transform> Children=new();
        public Transform? Find(string n)=>Children.GetValueOrDefault(n);
        public IEnumerator<Transform> GetEnumerator()=>Children.Values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        public Vector3 position; public Quaternion rotation;
        public Vector3 right => new(MathF.Cos(rotation.Yaw), 0, -MathF.Sin(rotation.Yaw));
        public Vector3 forward => new(MathF.Sin(rotation.Yaw), 0, MathF.Cos(rotation.Yaw));
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position=p; rotation=q; }
    }
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new(); public static Vector3 up => new(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public void Normalize() { var n=MathF.Sqrt(sqrMagnitude); if(n>0) { x/=n; y/=n; z/=n; } }
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b) => new(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a,Vector3 b) => MathF.Sqrt((a-b).sqrMagnitude);
        public static Vector3 Cross(Vector3 a,Vector3 b) => new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    public struct Quaternion {
        public float Yaw;
        public static Quaternion LookRotation(Vector3 facing) => new() { Yaw=MathF.Atan2(facing.x,facing.z) };
        public static float Angle(Quaternion a,Quaternion b) => MathF.Abs(MathF.IEEERemainder(a.Yaw-b.Yaw,2*MathF.PI))*180/MathF.PI;
    }
    public static class Mathf { public static float Max(float a,float b)=>MathF.Max(a,b); public static float Abs(float n)=>MathF.Abs(n); }
}
namespace UnityEngine.AI {
    public struct NavMeshQueryFilter { public int agentTypeID, areaMask; }
    public struct NavMeshHit { public Vector3 position; }
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status; }
    public static class NavMesh {
        public static Func<Vector3,float,Vector3?> Ground = (p,r)=>p;
        public static Func<Vector3,float,Vector3?> PedestrianGround = (p,r)=>p;
        public static Func<Vector3,(bool,NavMeshPathStatus)> Route = p=>(true,NavMeshPathStatus.PathComplete);
        public static Vector3 Start;
        public static HashSet<NavMeshPath> Paths=new();
        public static List<Vector3> Destinations=new();
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,NavMeshQueryFilter filter) {
            Harness.Check((filter.agentTypeID==42 && filter.areaMask==7) ||
                          (filter.agentTypeID==0 && filter.areaMask==-1),"must use actual player or NPC filter");
            var result=filter.agentTypeID==42 ? Ground(p,radius) : PedestrianGround(p,radius);
            hit=new() { position=result??default }; return result.HasValue;
        }
        public static bool CalculatePath(Vector3 start,Vector3 target,NavMeshQueryFilter filter,NavMeshPath path) {
            Harness.Check(filter.agentTypeID==42 && filter.areaMask==7,"path uses the player's mesh");
            Harness.Check(Vector3.Distance(start,Start)<.001f,"path starts at the player's current position");
            Paths.Add(path); Destinations.Add(target);
            var (found,status)=Route(target); path.status=status; return found;
        }
    }
    public class NavMeshAgent {
        public Transform transform = new(); public bool isActiveAndEnabled=true, isOnNavMesh=true, hasPath, pathPending;
        public bool updatePosition=true, updateRotation=true, isStopped;
        public int agentTypeID=42,areaMask=7,Warps; public float radius=.3f; public Vector3 velocity; public bool WarpWorks=true;
        public bool Warp(Vector3 p) { Warps++; if(WarpWorks) transform.position=p; return WarpWorks; }
    }
}
public class NavEvent {
    private event Action? changed; public void AddListener(Action a)=>changed+=a;
    public void RemoveListener(Action a)=>changed-=a; public void Invoke()=>changed?.Invoke();
}
public class ThirdPersonCharacter { public NavMeshAgent navmeshAgent=new(); public Vector3 velocity; }
public class PlayerController {
    public ThirdPersonCharacter Character=new(); public Transform transform=>Character.navmeshAgent.transform;
    public NavEvent PlayerChangedNavigation=new(); public bool hasOnGoalReachedAction; public int Resets;
    public void ResetNavigation() { Resets++; hasOnGoalReachedAction=false; Character.navmeshAgent.hasPath=false; }
}
public class VehicleInstance { public string id="cart"; }
public class VehicleController { public VehicleInstance vehicleInstance=new(); public Transform transform=new(); }
public class Game { public string? ActiveVehicleId; }
public static class SaveGameManager { public static Game? Current=new(); }
public static class BuildingManager { public static bool IsInsideBuilding; }
namespace Parking.UndergroundParking { public static class UndergroundParkingManager { public static bool IsInsideParking; } }
namespace UI.Load { public static class LoadScene { public static bool isLoading; } }
namespace PlayerActivity { public static class PlayerActivityUI { public static bool IsPanelOpen; } }
namespace Helpers {
    public static class PlayerHelper {
        public static PlayerController? PlayerController;
        public static void Teleport(Transform target) { PlayerController!.Character.navmeshAgent.Warp(target.position); PlayerController.transform.rotation=target.rotation; }
    }
}
namespace Extensions {
    public static class NavMeshHelper {
        public static NavMeshQueryFilter NpcNavMeshFilter=>new() { agentTypeID=0,areaMask=-1 };
    }
}
namespace FoodTrucks.Core.Saves {
    public class VehicleState { public bool IsOpen=true; }
    public static class StreetSales { public static VehicleState Vehicle=new(); public static VehicleState Find(object? state,string id)=>Vehicle; }
}
namespace FoodTrucks {
internal static class ServingWork {
        public static bool OwnedSkip;
        public static bool IsSkipping(string id) => OwnedSkip;
        public static void BeforeLeaving(string id) {}
    }
    public static class FoodTrucksSession { public static object CurrentState=new(); }
    public static class CartSelling { public static bool Skipping; }
    public static class TruckWindow { public static string? CurrentPhase; public static string? Phase()=>CurrentPhase; }
    public static class CartQueueView {
        public static (int Side, Vector3? Window)? Window;
        public static void Recheck() {}
        public static (int Side, Vector3? Window)? PrepareServing(VehicleController controller)=>Window;
    }
    public class TrafficLanes {
        public static TrafficLanes Current=new(); public static int Reads; public static Vector3 Center;
        public static TrafficLanes Near(Vector3 center,float radius) {
            Harness.Check(radius==30f,"read the same traffic radius as the queue");
            Center=center; Reads++; return Current;
        }
    }
    public static class QueueLine {
        public static Func<Vector3,string?> Street=p=>null;
        public static string? OnStreet(Vector3 p,TrafficLanes lanes) {
            Harness.Check(ReferenceEquals(lanes,TrafficLanes.Current),"reuse the cart's nearby lanes for every candidate");
            return Street(p);
        }
    }
    public static class Log {
        public static List<string> Lines=new(); public static void Info(string s)=>Lines.Add(s);
        public static void Error(string s)=>throw new Exception(s);
    }
}
class Harness {
    public static void Check(bool good,string message) { if(!good) throw new Exception(message); }
    static void Near(Vector3 a,Vector3 b,string why)=>Check(Vector3.Distance(a,b)<.001f,why);
    static (CartServing,PlayerController,VehicleController) Setup(int side=1,float yaw=0) {
        PlayerHelper.PlayerController=new(); SaveGameManager.Current=new();
        FoodTrucks.Core.Saves.StreetSales.Vehicle=new(); CartSelling.Skipping=false; ServingWork.OwnedSkip=false;
        BuildingManager.IsInsideBuilding=false; TruckWindow.CurrentPhase=null;
        NavMesh.Ground=(p,r)=>p; NavMesh.PedestrianGround=(p,r)=>p;
        NavMesh.Route=p=>(true,NavMeshPathStatus.PathComplete); NavMesh.Paths.Clear(); NavMesh.Destinations.Clear();
        QueueLine.Street=p=>null; TrafficLanes.Reads=0; TrafficLanes.Current=new(); Log.Lines.Clear();
        PlayerHelper.PlayerController.transform.position=new(2,0,-2); NavMesh.Start=PlayerHelper.PlayerController.transform.position;
        var cart=new VehicleController(); cart.transform.rotation=new() { Yaw=yaw };
        CartQueueView.Window=(side,Window(cart,side));
        return (new(),PlayerHelper.PlayerController,cart);
    }
    // The server and first customer share the counter centre, independent of the corner register.
    const float Along=0f;
    static Vector3 Window(VehicleController c,int side)=>c.transform.position+c.transform.right*(side*1.05f)+c.transform.forward*Along;
    static Vector3 Behind(VehicleController c,int side)=>c.transform.position-c.transform.right*(side*.85f)+c.transform.forward*Along;
    static Vector3? Sample(Vector3 at,float radius,params Vector3[] points)=>points
        .Where(x=>Vector3.Distance(at,x)<=radius).OrderBy(x=>(x-at).sqrMagnitude).Cast<Vector3?>().FirstOrDefault();
    static void Stayed(CartServing s,PlayerController p,VehicleController c,string why) {
        var position=p.transform.position; var rotation=p.transform.rotation;
        p.hasOnGoalReachedAction=true; p.Character.navmeshAgent.hasPath=true;
        s.Open(c); Near(p.transform.position,position,why);
        Check(p.Resets==0 && p.Character.navmeshAgent.Warps==0,"no navigation reset or warp: "+why);
        Check(p.hasOnGoalReachedAction && p.Character.navmeshAgent.hasPath,"keep original interaction: "+why);
        Check(Quaternion.Angle(p.transform.rotation,rotation)<.001f && !s.PlayerAt("cart"),"no stance: "+why);
        Check(Log.Lines.Any(x=>x.Contains("player left where they stand")),"unavailable spot logged: "+why);
    }
    static void Main() {
        foreach(var side in new[]{1,-1}) foreach(var yaw in new[]{0f,.4f,1.5707964f,3.1415927f,4.712389f,-2.2f}) {
            var (stance,p,c)=Setup(side,yaw); var agent=p.Character.navmeshAgent;
            c.transform.position=new(3.5f,0,-7.25f); NavMesh.Start=p.transform.position;
            CartQueueView.Window=(side,Window(c,side));
            p.hasOnGoalReachedAction=true; agent.hasPath=true;
            stance.Open(c); var expected=Behind(c,side);
            Near(p.transform.position,expected,"behind the cart at the equipment midpoint at every heading");
            Check(MathF.Abs(Vector3.Dot(p.transform.position-c.transform.position,c.transform.forward)-Along)<.001f,"at the counter centre");
            Check(Log.Lines.Any(x=>x.Contains("behind the cart at the equipment centre")),"register placement logged");
            Check(TrafficLanes.Reads==1,"lanes read once for placement");
            Near(TrafficLanes.Center,c.transform.position,"lanes read round cart");
            Check(NavMesh.Paths.Count==1 && NavMesh.Destinations.Count==1,"exact point has a complete player path");
            Check(p.Resets==1 && !p.hasOnGoalReachedAction && !agent.hasPath,"old interaction dropped before warp");
            Check(Quaternion.Angle(p.transform.rotation,Quaternion.LookRotation(c.transform.right*side))<.001f,"faces window");
            p.transform.position+=new Vector3(.05f,0,0); p.transform.rotation=default; stance.LateUpdate();
            Near(p.transform.position,expected,"idle nudge held");
            Check(agent.isActiveAndEnabled && agent.updatePosition && agent.updateRotation && !agent.isStopped,"native flags unchanged");
            agent.hasPath=true; p.hasOnGoalReachedAction=true; p.PlayerChangedNavigation.Invoke();
            p.transform.position=new(10,0,10); stance.LateUpdate();
            Near(p.transform.position,new(10,0,10),"click walking is free");
            Check(agent.hasPath && p.hasOnGoalReachedAction && p.Resets==1,"new path and callback preserved");
        }
        // Two fitted general spots use their actual midpoint; one/none and a dedicated fridge use counter centre.
        foreach(var side in new[]{1,-1}) foreach(var yaw in new[]{0f,.65f,1.5707964f,3.1415927f})
        foreach(var count in new[]{0,1,2}) {
            var (s,p,c)=Setup(side,yaw);
            var front=new Transform { localPosition=new(0,.963f,.6f) };
            var rear=new Transform { localPosition=new(0,.963f,-.2f) };
            c.transform.Children["EquipmentFront"]=front; c.transform.Children["EquipmentRear"]=rear;
            c.transform.Children["EquipmentCooler"]=new Transform {localPosition=new(0,.5f,-.32f)};
            c.transform.Children["EquipmentCooler"].Children["fitted"]=new Transform {name="Display_CartDrinksCooler"};
            if(count>0)front.Children["fitted"]=new Transform {name="Display_HotDogRoller"};
            if(count>1)rear.Children["fitted"]=new Transform {name="Display_BurgerStation"};
            var centre=count==2 ? .2f : 0f;
            CartQueueView.Window=(side,c.transform.position+c.transform.right*(side*1.05f)+c.transform.forward*centre);
            s.Open(c);
            Near(p.transform.position,c.transform.position-c.transform.right*(side*.85f)+c.transform.forward*centre,"fitted midpoint or counter fallback");
            Check(Quaternion.Angle(p.transform.rotation,Quaternion.LookRotation(c.transform.right*side))<.01f,"face customers at fitted midpoint");
            if(count==2) { rear.Children.Clear();s.LateUpdate();Check(!s.PlayerAt("cart"),"equipment-centre change releases stale stance"); }
            s.Release("done");
        }
        {
            var (s,p,c)=Setup(); var farther=new Vector3(-2.8f,0,1.1f); var closest=new Vector3(-1.1f,0,.235f);
            var customer=new Vector3(.1f,0,0); var upperFloor=new Vector3(-.85f,.7f,0);
            var safeEnd=new Vector3(0,0,1.07f);
            NavMesh.Ground=(at,r)=>Sample(at,r,customer,upperFloor,farther,closest,safeEnd);
            s.Open(c); Near(p.transform.position,closest,"nearest valid fallback behind takes priority over a safe end");
            Check(Log.Lines.Any(x=>x.Contains("fallback behind the cart")),"fallback logged");
            Check(NavMesh.Paths.Count==1,"reuse one path across the search"); s.Release("done");
        }
        {
            var (s,p,c)=Setup(); NavMesh.Ground=(at,r)=>Sample(at,r,new Vector3(.85f,0,0));
            Stayed(s,p,c,"never warp into the queue alongside the cart");
        }
        // A parallel kerb puts the preferred opposite side on the road. Exercise both queue sides and rotated
        // carts, with the player's mesh still covering the road and only the NPC mesh proving pedestrian ground.
        foreach(var side in new[]{1,-1}) foreach(var yaw in new[]{0f,.4f,1.57f,3.14f}) {
            var (s,p,c)=Setup(side,yaw); var outward=c.transform.right*side;
            NavMesh.PedestrianGround=(at,r)=>Vector3.Dot(at-c.transform.position,outward)>=-.001f ? at : null;
            Stayed(s,p,c,"kerbside cart cannot serve from an end");
        }
        // Positive pedestrian membership does not replace any of the queue's additional road exclusions.
        foreach(var reason in new[]{"parking lane","Crosswalk trigger","Roads layer","traffic lane / roadside strip"}) {
            var (s,p,c)=Setup(); QueueLine.Street=at=>at.x<-.01f ? reason : null;
            Stayed(s,p,c,"street exclusion: "+reason);
            Check(!NavMesh.Destinations.Any(at=>at.x<-.01f),"excluded road is rejected before path calculation");
        }
        {
            var (s,p,c)=Setup(); QueueLine.Street=at=>at.x<-.01f || at.z>0 ? "parking lane" : null;
            Stayed(s,p,c,"no rear-end fallback");
        }
        {
            var (s,p,c)=Setup(); var end=new Vector3(.35f,0,1.35f);
            NavMesh.Ground=(at,r)=>Sample(at,r,end);
            Stayed(s,p,c,"no front-end fallback");
        }
        // Exact and nearest fallback points can lie behind a fence. Reject partial/invalid paths and a failed
        // CalculatePath even if it leaves PathComplete in the reused path; take a farther reachable point.
        foreach(var bad in new[]{(true,NavMeshPathStatus.PathPartial),(true,NavMeshPathStatus.PathInvalid),(false,NavMeshPathStatus.PathComplete)}) {
            foreach(var exact in new[]{true,false}) {
                var (s,p,c)=Setup(); var blocked=new Vector3(exact ? -.85f : -1.1f,0,Along); var reachable=new Vector3(-1.5f,0,.285f);
                NavMesh.Ground=(at,r)=>Sample(at,r,blocked,reachable);
                NavMesh.Route=at=>Vector3.Distance(at,blocked)<.01f ? bad : (true,NavMeshPathStatus.PathComplete);
                s.Open(c); Near(p.transform.position,reachable,"complete path required for exact and fallback points: "+bad);
                Check(NavMesh.Destinations.Any(at=>Vector3.Distance(at,blocked)<.01f),"disconnected candidate tested");
                Check(NavMesh.Paths.Count==1,"reuse path after an incomplete calculation");
            }
        }
        foreach(var rejected in new[]{"NPC","NPC snap","player","street","path"}) {
            var (s,p,c)=Setup();
            switch(rejected) {
                case "NPC": NavMesh.PedestrianGround=(at,r)=>null; break;
                case "NPC snap": NavMesh.PedestrianGround=(at,r)=>at+new Vector3(.3f,0,0); break;
                case "player": NavMesh.Ground=(at,r)=>null; break;
                case "street": QueueLine.Street=at=>"Crosswalk trigger"; break;
                case "path": NavMesh.Route=at=>(true,NavMeshPathStatus.PathPartial); break;
            }
            Stayed(s,p,c,"all placements fail "+rejected);
        }
        {
            var (s,p,c)=Setup(); var blocked=new Vector3(-1.1f,0,0);
            NavMesh.Ground=(at,r)=>Sample(at,r,blocked); NavMesh.Route=at=>(true,NavMeshPathStatus.PathInvalid);
            Stayed(s,p,c,"only ground across a fence is disconnected");
        }
        foreach(var reason in new[]{"close","walk","path","window","cart","skip","vehicle","session","player","offmesh"}) {
            var (s,p,c)=Setup(); s.Open(c);
            switch(reason) {
                case "close": s.Closed("cart"); break;
                case "walk": p.Character.velocity=new(1,0,0); break;
                case "path": p.Character.navmeshAgent.hasPath=true; break;
                case "window": FoodTrucks.Core.Saves.StreetSales.Vehicle.IsOpen=false; break;
                case "cart": c.transform.position=new(1,0,0); break;
                case "skip": CartSelling.Skipping=true; break;
                case "vehicle": SaveGameManager.Current!.ActiveVehicleId="other"; break;
                case "session": s.Release("session ended"); break;
                case "player": PlayerHelper.PlayerController=new(); break;
                case "offmesh": p.Character.navmeshAgent.isOnNavMesh=false; break;
            }
            s.LateUpdate(); p.Character.velocity=default; p.Character.navmeshAgent.hasPath=false;
            p.transform.position+=new Vector3(.1f,0,0); var here=p.transform.position; s.LateUpdate();
            Near(p.transform.position,here,"release survives later frame: "+reason);
            Check(Log.Lines.Any(x=>x.Contains("stance released")),"release reason logged: "+reason);
        }
        // A cart moved and turned after a stance (pushed elsewhere, re-placed, or a save loaded with it elsewhere): the
        // old stance ends, and opening again places from the cart's live transform, never the old place.
        foreach(var side in new[]{1,-1}) {
            var (s,p,c)=Setup(side,0); s.Open(c); Near(p.transform.position,Behind(c,side),"first placement");
            c.transform.position=new(-12,0,30); c.transform.rotation=new() { Yaw=2.0f }; s.LateUpdate();
            Check(!s.PlayerAt("cart"),"moving the cart ends the stance");
            NavMesh.Start=p.transform.position; CartQueueView.Window=(side,Window(c,side)); s.Open(c);
            Near(p.transform.position,Behind(c,side),"placed again from the moved and turned cart");
            Check(Quaternion.Angle(p.transform.rotation,Quaternion.LookRotation(c.transform.right*side))<.001f,"faces the customers after the move");
        }
        // A behind-the-cart fallback still faces straight across the cart, not at a snapped queue place.
        {
            var (s,p,c)=Setup(); var fallback=new Vector3(-1.2f,0,.3f);
            NavMesh.Ground=(at,r)=>Sample(at,r,fallback); CartQueueView.Window=(1,new Vector3(1.3f,0,-.2f));
            s.Open(c); Near(p.transform.position,fallback,"behind fallback");
            Check(Quaternion.Angle(p.transform.rotation,Quaternion.LookRotation(c.transform.right))<.001f,"behind fallback faces across the cart");
        }
        // A behind-the-cart point far along the cart (an obstacle at the register) must be rejected, avoiding
        // straight out past the cart's end.
        foreach(var side in new[]{1,-1}) {
            var (s,p,c)=Setup(side,.9f); var far=c.transform.position-c.transform.right*(side*.85f)+c.transform.forward*2.5f;
            NavMesh.Ground=(at,r)=>Sample(at,r,far);
            Stayed(s,p,c,"cannot serve far along the cart away from the counter");
        }
        {
            var (s,p,c)=Setup(); p.Character.navmeshAgent.WarpWorks=false; s.Open(c);
            p.transform.position=new(1,0,0); s.LateUpdate(); Near(p.transform.position,new(1,0,0),"failed warp never held");
            Check(Log.Lines.Any(x=>x.Contains("warp to")),"failed warp logged");
        }
        {
            var (s,p,c)=Setup(); s.Open(c);
            CartSelling.Skipping=true; ServingWork.OwnedSkip=true; s.LateUpdate();
            Check(s.PlayerAt(c.vehicleInstance.id),"owned work skip keeps the serving stance");
            ServingWork.OwnedSkip=false; s.LateUpdate();
            Check(!s.PlayerAt(c.vehicleInstance.id),"unrelated skip releases the serving stance");
        }
        Check(UnityEngine.Object.Destroyed>0,"temporary teleport targets cleaned up");
        Console.WriteLine("PASS: cart serving pavement, reachable placements, fallback, idle hold and release");
    }
}
'''
