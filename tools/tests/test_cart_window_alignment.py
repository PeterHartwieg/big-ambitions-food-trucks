"""Run the display adapter and the actual queue placement method with controlled Unity/nav fakes."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


def block(source, marker):
    start = source.index(marker)
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


class CartWindowAlignmentTests(unittest.TestCase):
    def test_immediate_mirroring_retained_side_and_snapped_window_anchor(self):
        source = (ROOT / 'unity/FoodTrucks/Scripts/CartQueueView.cs').read_text()
        # Keep the production placement method intact while replacing unrelated spawning/path/game adapters.
        shape = block(source, 'internal sealed class WindowShape')
        place = block(source, 'private static Vector3? PlaceAt(')
        reject = block(source, 'private sealed class Rejections')
        # Exercise the production notification in EnsureLine: its inputs are faked, as are its surrounding game calls.
        ensure = block(source, 'private QueueLine EnsureLine(')
        side = block(source, 'public static int? QueueSide(')
        with tempfile.TemporaryDirectory(prefix='ft-cart-alignment-') as folder:
            root = Path(folder)
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            sources = ['unity/FoodTrucks/Scripts/VehicleDisplays.cs', 'Core/Fleet/CartStance.cs']
            items = ''.join(f'<Compile Include="{escape(str(ROOT / s))}" Link="{Path(s).name}" />' for s in sources)
            (root / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{items}</ItemGroup></Project>''')
            methods = ('\nnamespace FoodTrucks {\n' + shape + '\npartial class QueueLine {\n' + place + '\n' + reject +
                       '\n}\npartial class CartQueueView {\n' + side + '\n' + ensure + '\n}\n}\n')
            (root / 'Program.cs').write_text(HARNESS + methods)
            built = subprocess.run(['dotnet', 'build', str(root / 'Harness.csproj'), '--nologo', '-v:q', '-m:1',
                                    '-nr:false', '-p:UseSharedCompilation=false'], capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(root / f'bin/Debug/net{major}.0/Harness.dll')], capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('PASS: immediate cart displays and anchored first customer', ran.stdout)


HARNESS = r'''
using System.Collections;
using System.Reflection;
using FoodTrucks;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using UnityEngine;
using UnityEngine.AI;
namespace UnityEngine {
    public class Object {
        public static void Destroy(Object o) {}
        public static GameObject Instantiate(GameObject o,Transform p,bool world)=>new(o.name);
    }
    public class MonoBehaviour:Object {}
    public class Renderer { public GameObject gameObject=new("renderer"); public bool enabled; }
    public class Collider:Object {}
    public class GameObject:Object {
        public string name; public int layer; public bool active;
        public Transform transform;
        public GameObject(string n) { name=n; transform=new(this); }
        public void SetActive(bool a)=>active=a;
        public T[] GetComponentsInChildren<T>(bool all)=>Array.Empty<T>();
    }
    public class Transform:IEnumerable<Transform> {
        public GameObject gameObject; public string name=>gameObject.name;
        public Vector3 localPosition,position; public Quaternion localRotation,rotation;
        public Dictionary<string,Transform> Children=new();
        public Transform(GameObject go)=>gameObject=go;
        public Transform Find(string name)=>Children[name];
        public IEnumerator<Transform> GetEnumerator()=>Children.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero=>new();
    }
    public struct Quaternion { public float Yaw; public static Quaternion Euler(float x,float y,float z)=>new(){Yaw=y}; }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.AI {
    public struct NavMeshQueryFilter {}
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh {
        public static Vector3 Snap;
        public static bool SamplePosition(Vector3 p,out NavMeshHit h,float r,NavMeshQueryFilter f) {h=new(){position=Snap};return true;}
    }
}
namespace FoodTrucks.Core.Saves {
    public class VehicleState { public string VehicleId="cart"; public bool IsOpen=true; public object Service=new(); }
    public static class StreetSales { public static VehicleState? Find(object state,string id)=>FoodTrucksSession.Vehicle; }
}
namespace FoodTrucks.Core.Demand {
    public class DemandTables {
        public Info Vehicle(string kind)=>new(); public Info Equipment(string piece)=>new();
    }
    public class Info { public int CoolerSlots=1; public string? Display; }
}
namespace FoodTrucks.Core.Selling {
    public static class CargoRules { public static string[] FittedEquipment(object tables,object lines)=>Array.Empty<string>(); }
    public static class EquipmentLayout {
        public const string CoolerPlace="DrinksCoolerPlace";
        public static (int Slot,string Piece)[] Place(string[] f,int slots,int coolers,Func<string,string> key,string?[] previous)=>Array.Empty<(int,string)>();
    }
}
public class VehicleInstance { public string id="cart", vehicleTypeName="cart"; }
public class VehicleController {
    public VehicleInstance vehicleInstance=new(); public Transform transform=new GameObject("cart").transform; public bool visible=true;
    public GameObject gameObject=>transform.gameObject; public int GetInstanceID()=>1;
}
namespace Helpers { public static class VehicleHelper { public static VehicleController[] AllPlayerVehicles=Array.Empty<VehicleController>(); } }
namespace FoodTrucks {
    public static class FoodTrucksSession { public static object CurrentState=new(); public static VehicleState Vehicle=new(); }
    public static class DemandData { public static FoodTrucks.Core.Demand.DemandTables Tables=new(); public static Rules Rules=new(); }
    public class Rules { public ServiceRules Service=new(); public QueueRules Queue=new(); }
    public class ServiceRules { public int QueueLimit(string k,int max)=>max; }
    public class QueueRules { public int MaxQueue=4; }
    public static class CargoLimit { public static object Lines(VehicleInstance v)=>new(); }
    public class Bundle { public T? LoadAsset<T>(string p) where T:class=>null; }
    public static class FoodTrucksMod { public const string CartTypeName="cart",TruckTypeName="truck"; public static Bundle? Bundle; public static string? KindOf(string? k)=>k; }
    public static class CartSelling { public static bool PlayerAtServingSpot(string id)=>false; public static void Paid(string id,long c) {} }
    public static class Log { public static void Info(string s){} public static void Error(string s)=>throw new Exception(s); }
    public class TrafficLanes {}
    partial class QueueLine {
        public int Side; public object Service=new(); public Action? OnSpawned; public Action<long>? Paid;
        public bool MovedFrom(Transform t)=>false;
        public static int NextSide=-1;
        public static QueueLine Create(string id,Transform t,int limit,object service,WindowShape shape)=>new(){Side=NextSide,Service=service};
        public static float FlatDistance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.z-b.z)*(a.z-b.z));
        public static string? OnStreet(Vector3 p,TrafficLanes lanes)=>null;
        public static bool Accept(Vector3 wanted,bool first,string kind)=>PlaceAt(wanted,WindowShape.For(kind),new(),new(),new(),first).HasValue;
    }
    partial class CartQueueView {
        static CartQueueView instance=new(); int spawnedTotal; readonly Dictionary<string,QueueLine> lines=new();
        readonly Dictionary<string,(object Service,int Side)> lastSides=new();
        void Retire(string id) {lines.Remove(id);lastSides.Remove(id);} void Relay(QueueLine l)=>lines.Clear();
        public static void Open(VehicleController c) {instance.lines.Clear();instance.EnsureLine(FoodTrucksSession.Vehicle,c);}
        public static void Away()=>instance.lines.Clear();
    }
}
class Harness {
    static void Check(bool ok,string msg) {if(!ok)throw new Exception(msg);}
    static void Invoke(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(o,null);
    static void Main() {
        var c=new VehicleController();
        foreach(var n in new[]{"Cart_Body","Cash_Register","EquipmentFront","EquipmentRear","DrinksCoolerPlace","Cart_Canopy","Cart_Canopy_Furled"})
            c.transform.Children[n]=new GameObject(n).transform;
        Helpers.VehicleHelper.AllPlayerVehicles=new[]{c};
        var display=new VehicleDisplays();Invoke(display,"OnEnable");Invoke(display,"Update");
        var reg=c.transform.Find("Cash_Register");var cooler=c.transform.Find("DrinksCoolerPlace");
        Check(reg.localPosition.x==.29f,"default right");
        // No time passes: the equipment poll is still throttled. EnsureLine must mirror in this same call.
        CartQueueView.Open(c);
        Check(reg.localPosition.x==-.29f && reg.localRotation.Yaw==-90 && cooler.localPosition.x==-.443f,"immediate left register and cooler");
        CartQueueView.Away();Time.unscaledTime=1;Invoke(display,"Update");
        Check(reg.localPosition.x==-.29f && cooler.localPosition.x==-.443f,"retain left with no visible line");
        Check(CartQueueView.QueueSide("cart")==-1,"queue remembers its side while hidden");
        QueueLine.NextSide=0;CartQueueView.Open(c);
        Check(CartQueueView.QueueSide("cart")==-1 && reg.localPosition.x==-.29f,"empty relayout keeps the opening side");
        FoodTrucksSession.Vehicle.Service=new();CartQueueView.Away();
        Check(CartQueueView.QueueSide("cart")==null,"new opening does not inherit an old queue side");
        QueueLine.NextSide=1;CartQueueView.Open(c);
        Check(reg.localPosition.x==.29f && cooler.localPosition.x==.443f,"immediate relayout to right");
        FoodTrucksSession.Vehicle.IsOpen=false;Time.unscaledTime=2;Invoke(display,"Update");
        Check(reg.localPosition.x==.29f,"closed right");Invoke(display,"OnDisable");
        foreach(var d in new[]{.0f,.09f,.11f,.4f,.46f}) {
            var wanted=new Vector3(1.05f,0,.615f);NavMesh.Snap=new(1.05f,0,.615f+d);
            Check(QueueLine.Accept(wanted,true,"cart")== (d<=.1f),"first cart customer anchor "+d);
            Check(QueueLine.Accept(wanted,false,"cart")== (d<=.45f),"other cart places retain tolerance "+d);
            Check(QueueLine.Accept(wanted,true,"truck")== (d<=.2f),"truck unchanged "+d);
        }
        Console.WriteLine("PASS: immediate cart displays and anchored first customer");
    }
}
'''
