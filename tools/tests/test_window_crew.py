"""Exercise the real window crew adapter (WindowCrew.cs) against stand-in game types, without Unity or game assemblies."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ['unity/FoodTrucks/Scripts/WindowCrew.cs', 'unity/FoodTrucks/Scripts/CartStanceWorld.cs',
           'Core/Fleet/CartStance.cs', 'Core/Fleet/WindowPlaces.cs']


class WindowCrewTests(unittest.TestCase):
    def test_cart_crew_behind_the_register_and_truck_crew_unchanged(self):
        with tempfile.TemporaryDirectory(prefix='ft-window-crew-') as folder:
            root = Path(folder)
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            items = ''.join(f'<Compile Include="{escape(str(ROOT / s))}" Link="{Path(s).name}" />' for s in SOURCES)
            (root / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>{items}</ItemGroup></Project>''')
            (root / 'Program.cs').write_text(HARNESS)
            built = subprocess.run(['dotnet', 'build', str(root / 'Harness.csproj'), '--nologo', '-v:q',
                                    '-m:1', '-nr:false', '-p:UseSharedCompilation=false'],
                                   capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(root / f'bin/Debug/net{major}.0/Harness.dll')],
                                 capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('PASS: window crew at the cart register and the truck hatch', ran.stdout)


# Stand-ins only. Transform follows Unity's convention for a vehicle turned about the vertical (yaw in radians):
# right (cos, 0, -sin), forward (sin, 0, cos), TransformPoint = position + right*x + up*y + forward*z.
HARNESS = r'''
using UnityEngine;
using Entities;
using Helpers;
using FoodTrucks;

namespace UnityEngine {
    public class Object { public static void Destroy(Object value) {} }
    public class GameObject : Object {
        public Transform transform = new(); public bool active; public GameObject(string name) {}
        public void SetActive(bool value) => active = value;
    }
    public class Transform {
        public Vector3 position; public Quaternion rotation;
        public Vector3 right => new(MathF.Cos(rotation.Yaw), 0, -MathF.Sin(rotation.Yaw));
        public Vector3 forward => new(MathF.Sin(rotation.Yaw), 0, MathF.Cos(rotation.Yaw));
        public Vector3 TransformPoint(Vector3 p) => position + right*p.x + new Vector3(0,p.y,0) + forward*p.z;
    }
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 zero => new(); public static Vector3 up => new(0,1,0);
        public Vector3 normalized => this*(1/MathF.Sqrt(sqrMagnitude));
        public static Vector3 Cross(Vector3 a,Vector3 b) => new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator -(Vector3 a) => new(-a.x,-a.y,-a.z);
        public static Vector3 operator *(Vector3 a,float b) => new(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a,Vector3 b) => MathF.Sqrt((a-b).sqrMagnitude);
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }
    public struct Quaternion {
        public float Yaw;
        public static Quaternion LookRotation(Vector3 facing) => new() { Yaw=MathF.Atan2(facing.x,facing.z) };
        public static float Angle(Quaternion a,Quaternion b) => MathF.Abs(MathF.IEEERemainder(a.Yaw-b.Yaw,2*MathF.PI))*180/MathF.PI;
    }
}
namespace Entities {
    public class CharacterData { public List<object>? elements; public int gender; public int ageInDays; }
    public class EmployeeInstance { public string id=""; public CharacterData? characterData; }
}
public class AppearanceSetter {
    public void SetAppearance(object data) {} public void SetRandomAppearance() {}
    public void SetRandomAppearance(int gender,int age,int seed) {}
}
public class ThirdPersonCharacter {
    public GameObject gameObject=new("character"); public bool isPlayer=true; public AppearanceSetter appearanceSetter=new();
    public Vector3 Position; public Quaternion Rotation;
    public void ForceToPosition(Vector3 p) => Position=p;
    public void ForceToRotation(Quaternion q) => Rotation=q;
}
namespace Helpers {
    public static class PrefabHelper {
        public static List<ThirdPersonCharacter> Made=new();
        public static T? CreatePrefab<T>(string path,Transform parent) where T : class {
            var c=new ThirdPersonCharacter(); Made.Add(c); return c as T;
        }
    }
    public static class PlayerHelper { public static Vector3 Position; public static Vector3 GetPosition() => Position; }
}
namespace FoodTrucks {
    public static class HireCrew { public static string Name(EmployeeInstance e) => e.id; }
    public static class TruckWindow { public static bool Player; public static bool PlayerAt(string id) => Player; }
    public static class CartSelling { public static bool Player; public static bool PlayerAtServingSpot(string id) => Player; }
    public static class Log {
        public static void Info(string s) {}
        public static void Error(string s) => throw new Exception(s);
    }
}
class Harness {
    static void Check(bool good,string message) { if(!good) throw new Exception(message); }
    static void Near(Vector3 a,Vector3 b,string why) => Check(Vector3.Distance(a,b)<.001f,$"{why}: expected {b}, got {a}");
    static Transform Cart(float yaw) => new() { position=new(4.5f,0.2f,-11f), rotation=new() { Yaw=yaw } };
    // Behind the cart (the side away from the queue) and level with the register, 0.615 m forward of the middle.
    static Vector3 Behind(Transform c,int side) => c.position-c.right*(side*.85f)+c.forward*.615f;
    static Vector3 Register(Transform c,int side) => c.position+c.right*(side*.29f)+c.forward*.615f;
    static ThirdPersonCharacter Show(string id,Transform t,string kind,int side,int crew,int serving) {
        WindowCrew.Reset(); PrefabHelper.Made.Clear();
        var people=Enumerable.Range(0,crew).Select(i => new EmployeeInstance { id="e"+i }).ToList();
        WindowCrew.Sync(id,t,kind,side,people,people.Take(serving).ToList());
        Check(PrefabHelper.Made.Count==crew,"one character per crew member");
        return PrefabHelper.Made[0];
    }
    static void Main() {
        var headings=new[]{0f,MathF.PI/2,MathF.PI,3*MathF.PI/2,.65f,-2.3f,5.9f};
        foreach(var side in new[]{1,-1}) foreach(var yaw in headings) {
            CartSelling.Player=false; TruckWindow.Player=false;
            var c=Cart(yaw); var worker=Show("cart",c,"cart",side,1,1);
            Near(worker.Position,Behind(c,side),$"cart worker behind the register, side {side}, yaw {yaw}");
            Check(Quaternion.Angle(worker.Rotation,Quaternion.LookRotation(c.right*side))<.01f,"cart worker faces the customers");
            // Straight ahead of the worker: the register, then the customers' side.
            var ahead=Register(c,side)-worker.Position; var facing=c.right*side;
            Check(MathF.Abs(ahead.x*facing.z-ahead.z*facing.x)<.001f && Vector3.Dot(ahead,facing)>0,"register straight ahead");
            Check(MathF.Abs(Vector3.Dot(worker.Position-c.position,c.forward)-.615f)<.001f,"level with the register, not the middle");
            // The same crew after the cart is pushed and turned: placed from the live transform.
            c.position=new(-30,0.2f,8); c.rotation=new() { Yaw=yaw+1.1f };
            WindowCrew.Sync("cart",c,"cart",side,new[]{ new EmployeeInstance { id="e0" } },null);
            Near(worker.Position,Behind(c,side),"follows the moved cart");
            Check(Quaternion.Angle(worker.Rotation,Quaternion.LookRotation(c.right*side))<.01f,"faces the customers after the move");
        }
        // No line laid out (side 0): the right-hand window side, as the register faces while closed.
        { var c=Cart(.3f); var w=Show("cart",c,"cart",0,1,1); Near(w.Position,Behind(c,1),"side 0 counts as the right"); }
        // The player serving at the register: the crew wait further back along the same side, a step apart.
        foreach(var side in new[]{1,-1}) {
            var c=Cart(1.2f); CartSelling.Player=true; PlayerHelper.Position=Behind(c,side);
            Show("cart",c,"cart",side,2,2);
            var a=PrefabHelper.Made[0].Position; var b=PrefabHelper.Made[1].Position;
            Near(a,Behind(c,side)-c.forward*.9f,"first waiting crew a step behind the player");
            Near(b,Behind(c,side)-c.forward*1.8f,"second waiting crew two steps behind");
            Check(Vector3.Distance(a,PlayerHelper.Position)>=.89f,"no crew on the player");
            // The player at the rear end: the crew wait towards the front instead.
            PlayerHelper.Position=c.position-c.forward*1.07f; Show("cart",c,"cart",side,1,1);
            Near(PrefabHelper.Made[0].Position,Behind(c,side)+c.forward*.9f,"crew go forward when the player is at the rear");
            CartSelling.Player=false;
        }
        // The truck's hatch is untouched: the middle place behind the hatch, facing out of the right side.
        foreach(var yaw in headings) {
            TruckWindow.Player=false; CartSelling.Player=true; // a cart flag never moves truck crew
            var t=Cart(yaw); var w=Show("truck",t,"truck",-1,1,1);
            Near(w.Position,t.TransformPoint(new(0.59f,0.94f,-1.10f)),"truck crew at the hatch");
            Check(Quaternion.Angle(w.Rotation,Quaternion.LookRotation(t.right))<.01f,"truck crew face the hatch");
        }
        CartSelling.Player=false;
        Console.WriteLine("PASS: window crew at the cart register and the truck hatch");
    }
}
'''
