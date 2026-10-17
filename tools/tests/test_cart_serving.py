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
            (root / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup><Compile Include="{source}" Link="CartServing.cs" /></ItemGroup></Project>''')
            (root / 'Program.cs').write_text(HARNESS)
            built = subprocess.run(['dotnet', 'build', str(root / 'Harness.csproj'), '--nologo', '-v:q',
                                    '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                                   capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(root / f'bin/Debug/net{major}.0/Harness.dll')],
                                 capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('PASS: cart serving placement, fallback, idle hold and release', ran.stdout)


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
    public class Transform {
        public Vector3 position; public Quaternion rotation;
        public Vector3 right => new(MathF.Cos(rotation.Yaw), 0, -MathF.Sin(rotation.Yaw));
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
    public static class NavMesh {
        public static Func<Vector3,float,Vector3?> Ground = (p,r)=>p;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,NavMeshQueryFilter filter) {
            Harness.Check(filter.agentTypeID==42 && filter.areaMask==7,"must use actual player filter");
            var result=Ground(p,radius); hit=new() { position=result??default }; return result.HasValue;
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
namespace FoodTrucks.Core.Saves {
    public class VehicleState { public bool IsOpen=true; }
    public static class StreetSales { public static VehicleState Vehicle=new(); public static VehicleState Find(object? state,string id)=>Vehicle; }
}
namespace FoodTrucks {
    public static class FoodTrucksSession { public static object CurrentState=new(); }
    public static class CartSelling { public static bool Skipping; }
    public static class TruckWindow { public static string? CurrentPhase; public static string? Phase()=>CurrentPhase; }
    public static class CartQueueView {
        public static (int Side, Vector3? Window)? Window;
        public static void Recheck() {}
        public static (int Side, Vector3? Window)? PrepareServing(VehicleController controller)=>Window;
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
        FoodTrucks.Core.Saves.StreetSales.Vehicle=new(); CartSelling.Skipping=false;
        BuildingManager.IsInsideBuilding=false; TruckWindow.CurrentPhase=null;
        NavMesh.Ground=(p,r)=>p; Log.Lines.Clear();
        var cart=new VehicleController(); cart.transform.rotation=new() { Yaw=yaw };
        CartQueueView.Window=(side,cart.transform.right*side*1.05f);
        return (new(),PlayerHelper.PlayerController,cart);
    }
    static void Main() {
        foreach(var side in new[]{1,-1}) foreach(var yaw in new[]{0f,.4f,1.57f,3.14f}) {
            var (stance,p,c)=Setup(side,yaw); var agent=p.Character.navmeshAgent;
            p.hasOnGoalReachedAction=true; agent.hasPath=true;
            stance.Open(c); var expected=c.transform.position-c.transform.right*(side*.85f);
            Near(p.transform.position,expected,"opposite customer side at every heading");
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
        {
            var (s,p,c)=Setup(); var farther=new Vector3(-2.8f,0,1.1f); var closest=new Vector3(-1.1f,0,1.5f);
            var customer=new Vector3(.1f,0,0); var upperFloor=new Vector3(-.85f,.7f,0);
            NavMesh.Ground=(at,r)=>new[]{customer,upperFloor,farther,closest}
                .Where(x=>Vector3.Distance(at,x)<=r).OrderBy(x=>(x-at).sqrMagnitude).Cast<Vector3?>().FirstOrDefault();
            s.Open(c); Near(p.transform.position,closest,"nearest valid fallback behind, not customer side or upper floor");
            Check(Log.Lines.Any(x=>x.Contains("nearest walkable fallback")),"fallback logged"); s.Release("done");
        }
        {
            var (s,p,c)=Setup(); NavMesh.Ground=(at,r)=>new Vector3(.85f,0,0);
            s.Open(c); Check(p.Resets==0 && p.Character.navmeshAgent.Warps==0,"never warp to customer side");
            Check(Log.Lines.Any(x=>x.Contains("no walkable serving point")),"unavailable spot logged");
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
        {
            var (s,p,c)=Setup(); p.Character.navmeshAgent.WarpWorks=false; s.Open(c);
            p.transform.position=new(1,0,0); s.LateUpdate(); Near(p.transform.position,new(1,0,0),"failed warp never held");
            Check(Log.Lines.Any(x=>x.Contains("warp to")),"failed warp logged");
        }
        Check(UnityEngine.Object.Destroyed>0,"temporary teleport targets cleaned up");
        Console.WriteLine("PASS: cart serving placement, fallback, idle hold and release");
    }
}
'''
