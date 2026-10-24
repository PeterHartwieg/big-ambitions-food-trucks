"""Run the real probe/hooks and Core against instrumented native boundaries.

The fake vehicle/traffic wrappers deliberately do not simulate NWH physics. Native
geometry, fuel/damage persistence, braking and Harmony installation still need
the parent's native compile and in-game checks.
"""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class CrewDriveAdapterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        directory = tempfile.TemporaryDirectory(prefix='ft-crew-drive-adapter-')
        cls.addClassCleanup(directory.cleanup)
        folder = Path(directory.name)
        # Isolate Core's build outputs from the concurrently compiling parent.
        shutil.copytree(ROOT / 'Core', folder / 'Core',
                        ignore=shutil.ignore_patterns('bin', 'obj'))
        major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
        includes = ''.join(
            f'<Compile Include="{escape(str(ROOT / "unity/FoodTrucks/Scripts" / name))}" />'
            for name in ('CrewDriveProbe.cs', 'CrewProbeHooks.cs'))
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><DefineConstants>FOODTRUCKS_CREW_DRIVING_DEV</DefineConstants>
<OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>
<Compile Include="Program.cs" />{includes}<ProjectReference Include="Core/FoodTrucks.Core.csproj" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" />
</ItemGroup></Project>''')
        (folder / 'Program.cs').write_text(HARNESS)
        built = subprocess.run(
            ['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q',
             '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false'],
            capture_output=True, text=True, timeout=120)
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        cls.dll = folder / f'bin/Debug/net{major}.0/Harness.dll'

    def run_case(self, *args):
        ran = subprocess.run(['dotnet', str(self.dll), *args], capture_output=True,
                             text=True, timeout=30)
        self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
        self.assertIn('PASS:', ran.stdout)

    def test_start_waits_for_both_native_ready_signals_and_can_cancel_untouched(self):
        self.run_case('starting')

    def test_real_core_steps_and_cleanup_revokes_before_native_callbacks(self):
        self.run_case('lifecycle')

    def test_startup_failure_unwinds_partial_native_ownership(self):
        for boundary in ('traffic', 'placement', 'begin'):
            with self.subTest(boundary=boundary):
                self.run_case('startup-failure', boundary)

    def test_cleanup_errors_hold_ownership_and_block_commands_until_retry(self):
        for boundary in ('hold', 'sync', 'traffic', 'restore'):
            with self.subTest(boundary=boundary):
                self.run_case('failure', boundary)

    def test_duplicate_and_stale_session_generation_requests_preserve_owner(self):
        self.run_case('stale')

    def test_replacement_controller_never_receives_old_owner_restore(self):
        self.run_case('replacement')

    def test_player_entry_prefix_cleans_before_native_input_and_refuses_failure(self):
        self.run_case('entry')

    def test_save_prefix_refuses_before_native_bookkeeping_and_allows_retry(self):
        self.run_case('save')

    def test_observed_fuel_and_damage_reach_cleanup_without_adapter_rollback(self):
        self.run_case('consumption')

    def test_fifty_lifecycles_leave_no_owners_or_boundary_resources(self):
        self.run_case('repeat')

    def test_collision_records_evidence_without_assigning_fault(self):
        self.run_case('collision')

    def test_damage_callback_after_probe_cleanup_is_flushed_before_save(self):
        self.run_case('late-damage')

    def test_sideways_or_angular_motion_cannot_pass_as_endpoint_braking(self):
        for kind in ('lateral', 'angular'):
            with self.subTest(kind=kind):
                self.run_case('endpoint-motion', kind)


    def test_native_wheel_fault_holds_before_step_and_defers_cleanup(self):
        self.run_case('wheel-guard')

    def test_wheel_guard_does_not_write_after_save_authority_changes(self):
        self.run_case('wheel-session')


HARNESS = r'''
using System.Reflection;
using FoodTrucks;
using FoodTrucks.Core.CrewDriving;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using UnityEngine;

static class Harness {
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    internal static readonly List<string> Events=new();
    internal static Action<string>? NativeCallback;
    static void Check(bool value,string why) { if(!value) throw new Exception(why+"; events="+string.Join(",",Events)); }
    internal static object? Field(object target,string name)=>target.GetType().GetField(name,Private)!.GetValue(target);
    static CrewDriveProbe Active()=>(CrewDriveProbe)typeof(CrewDriveProbe).GetField("active",Private)!.GetValue(null)!;
    static void Tick(object target,string method,params object[] args) {
        try { target.GetType().GetMethod(method,Private)!.Invoke(target,args); }
        catch(TargetInvocationException ex) { throw ex.InnerException!; }
    }
    static bool Hook(string name,params object[] args) {
        try { return (bool)typeof(CrewProbeHooks).GetMethod(name,Private)!.Invoke(null,args)!; }
        catch(TargetInvocationException ex) { throw ex.InnerException!; }
    }
    static int Owners()=>(int)CrewDriveProbe.Status()["owners"]!;
    static string Phase()=>(string)CrewDriveProbe.Status()["phase"]!;
    static (Guid Session,long Generation) Identity() {
        var s=CrewDriveProbe.Status(); return (Guid.Parse((string)s["session"]!), (long)s["generation"]!);
    }
    static void Stop((Guid Session,long Generation) token)=>CrewDriveProbe.StopProbe(token.Session,token.Generation);
    static void Refused(Action call,string why) {
        try { call(); } catch(BridgeError) { return; }
        throw new Exception("Expected BridgeError: "+why);
    }
    static void WheelSession() {
        var car=Setup();var probe=Driving(car);var native=CrewProbeVehicle.Last!;
        var wheel=new NWH.WheelController3D.WheelController {ParentRigidbody=native.Body};native.Contract.Wheel=wheel;
        native.Body.velocity=new(0,0,1);var touches=car.NativeTouches;
        SaveGameManager.Current=new GameInstance();
        Check(CrewDriveProbe.BeforeWheelStep(wheel),"lost token does not intercept another save's native step");
        Check(car.NativeTouches==touches&&!native.Body.isKinematic&&native.Body.velocity.z==1,"no stale native hold writes");
        Tick(probe,"Update");
        Check(Owners()==0&&car.NativeTouches==touches,"deferred cleanup releases without touching foreign save body");
    }
    static void WheelGuard() {
        var car=Setup();var probe=Driving(car);var native=CrewProbeVehicle.Last!;
        var wheel=new NWH.WheelController3D.WheelController {ParentRigidbody=native.Body};native.Contract.Wheel=wheel;
        Check(CrewDriveProbe.BeforeWheelStep(wheel),"valid wheel may step");
        native.Body.velocity=new(0,0,1);native.Contract.Error="actual wheel angle exceeds certificate";
        Check(!CrewDriveProbe.BeforeWheelStep(wheel),"invalid wheel skipped before native simulation");
        Check(native.Body.isKinematic&&native.Body.velocity.sqrMagnitude==0,"captured body held before physics");
        Check(Owners()==1&&!Events.Contains("restore"),"cleanup deferred outside wheel iteration");
        var count=Events.Count;
        Check(!CrewDriveProbe.BeforeWheelStep(wheel)&&Events.Count==count,"later owned wheel step skipped without duplicate cleanup");
        Check(CrewDriveProbe.BeforeWheelStep(new()),"unrelated wheels untouched");
        Tick(probe,"Update");Released(car);
        Check(Phase()=="Cancelled"&&(string)CrewDriveProbe.Status()["wheel_guard_failure"]! != "","fault remains diagnostic");
    }
    static CarController Setup(string id="truck") {
        var instance=new VehicleInstance { id=id };
        var car=new CarController { vehicleInstance=instance };
        SaveGameManager.Current.VehicleInstances.Add(instance);
        Helpers.VehicleHelper.Cars[id]=car;
        return car;
    }
    static CrewDriveProbe Start(CarController car,bool ready=true) {
        if(ready) { car.vehicleController.IsInitialized=true; CrewDriveProbe.StationaryReady(car); }
        CrewDriveProbe.StartProbe(car.vehicleInstance.id,new[]{10,11},2);
        return Active();
    }
    static CrewDriveProbe Driving(CarController car) {
        var created=CrewProbeVehicle.Created;
        var probe=Start(car); Tick(probe,"Update");
        Check(Phase()=="Driving" && CrewProbeVehicle.Created==created+1,"native startup reaches real Core Driving");
        Check(Events.SequenceEqual(new[]{"construct","traffic-create","begin"}),"no native driving command before startup completes");
        return probe;
    }
    internal static void Boundary(string name) { Events.Add(name); NativeCallback?.Invoke(name); }
    static void Released(CarController car) {
        Check(!CrewDriveProbe.Active && Owners()==0,"ownership released");
        Check(!CrewDriveProbe.Owns(car) && !CrewDriveProbe.Owns(car.vehicleInstance.id),"both ownership lookups released");
        Check(car.GetComponent<CrewDriveProbe>()==null,"probe component removed");
        Check(CrewProbeTraffic.Live==0,"traffic registrations/overrides removed");
    }
    static void Starting() {
        var car=Setup(); var probe=Start(car,false);
        Tick(probe,"Update"); Tick(probe,"FixedUpdate");
        Check(Phase()=="Starting" && CrewProbeVehicle.Created==0 && Events.Count==0,"wait without touching native wrapper");
        CrewDriveProbe.StationaryReady(car); Tick(probe,"Update");
        Check(CrewProbeVehicle.Created==0,"stationary marker alone does not imply initialized NWH");
        Check(CrewDriveProbe.StopAll("cancel starting"),"Starting cancellation succeeds"); Released(car);
        Check(Events.Count==0,"Starting cancel performs no native mutations");
        car=Setup("other"); car.vehicleController.IsInitialized=true;
        probe=Start(car,false); Tick(probe,"Update");
        Check(CrewProbeVehicle.Created==0,"initialized NWH alone does not imply stationary handling completed");
        Time.realtimeSinceStartup=16; Tick(probe,"Update"); Released(car);
        Check(Phase()=="Cancelled" && CrewProbeVehicle.Created==0,"startup timeout leaves native untouched");
    }
    static void Lifecycle() {
        var car=Setup(); var probe=Driving(car);
        Tick(probe,"FixedUpdate");
        var native=CrewProbeVehicle.Last!;
        Check(native.Commands==1 && native.Throttle>0 && (int)CrewDriveProbe.Status()["steps"]! ==1,"real Core command reaches native boundary");
        native.Body.velocity=new Vector3(0,0,2);
        NativeCallback=name=>{
            if(name is not ("hold" or "sync" or "traffic-dispose" or "restore")) return;
            Check(CrewDriveProbe.Owns(car) && Owners()==1,"retain exact ownership through every cleanup boundary");
            Check((bool)Field(probe,"commandsRevoked")!,"commands revoked before cleanup");
            Check(((ProbeController)Field(probe,"controller")!).State==ProbeState.Cancelled,"real Core controller cancelled before cleanup");
            var commands=native.Commands; Tick(probe,"FixedUpdate"); Tick(probe,"Update");
            Check(native.Commands==commands,"reentrant frames cannot issue commands");
            Check(!CrewDriveProbe.StopAll("reentrant"),"reentrant cleanup cannot release early");
        };
        Check(CrewDriveProbe.StopAll("test"),"normal cleanup succeeds"); NativeCallback=null;
        Check(Events.Skip(4).SequenceEqual(new[]{"hold","sync","traffic-dispose","restore"}),"hold, synchronize, dispose, restore order");
        Check((bool)CrewDriveProbe.Status()["forced_interruption_hold"]!,"moving interrupt records forced hold");
        Released(car); var count=Events.Count; Tick(probe,"FixedUpdate"); Tick(probe,"OnDisable"); Tick(probe,"OnDestroy");
        Check(Events.Count==count && CrewDriveProbe.StopAll("again"),"released callbacks and duplicate stop are inert");
    }
    static void Failure(string boundary) {
        var car=Setup(); var probe=Driving(car); Tick(probe,"FixedUpdate");
        var native=CrewProbeVehicle.Last!; native.Body.velocity=new Vector3(0,0,2);
        CrewProbeVehicle.Fail=boundary; CrewProbeTraffic.FailDispose=boundary=="traffic";
        Check(!CrewDriveProbe.StopAll("injected "+boundary),"cleanup failure reported");
        Check(CrewDriveProbe.Active && CrewDriveProbe.Owns(car) && Owners()==1 && Phase()=="Holding","failed cleanup retains owner in Holding");
        Check(native.Body.isKinematic && native.Body.velocity.sqrMagnitude==0 && native.Throttle==0 && native.Handbrake==1,"failed cleanup reapplies safe hold");
        Check(Events.Contains("restore") && Events.Contains("traffic-dispose"),"other cleanup boundaries attempted after failure");
        var commands=native.Commands; var count=Events.Count;
        for(var i=0;i<3;i++) { Tick(probe,"FixedUpdate"); Tick(probe,"Update"); }
        Check(native.Commands==commands && Events.Count==count,"later frames do not restart or drive failed cleanup");
        Refused(()=>CrewDriveProbe.StartProbe("truck",new[]{10,11},2),"failed cleanup excludes another owner");
        CrewProbeVehicle.Fail=null; CrewProbeTraffic.FailDispose=false;
        Check(CrewDriveProbe.StopAll("retry"),"retry completes"); Released(car);
        Check(Log.Errors.Count>0,"failure recorded");
    }
    static void StartupFailure(string boundary) {
        var car=Setup(); var probe=Start(car);
        CrewProbeTraffic.FailCreate=boundary=="traffic";
        CrewProbeVehicle.Fail=boundary=="begin" ? "begin" : null;
        CrewProbeVehicle.RearPosition=boundary=="placement" ? new Vector3(10,0,0) : default;
        Tick(probe,"Update"); Released(car);
        Check(Phase()=="Cancelled" && Log.Errors.Count>0,"startup error cancelled and recorded");
        Check(CrewProbeVehicle.Last!.Commands==0,"failed startup never commands driving");
        if(boundary=="begin") {
            Check(Events.Contains("hold") && Events.Contains("sync") && Events.Contains("restore"),"partially started engine unwinds all native changes");
            Check(Events.Contains("traffic-dispose"),"partially started engine releases traffic resources");
        } else {
            Check(car.NativeTouches==0 && !Events.Contains("hold") && !Events.Contains("sync") && !Events.Contains("restore"),"read-only admission refusal never holds, saves or stops native engine");
            Check(!(bool)CrewDriveProbe.Status()["forced_interruption_hold"]!,"admission refusal is not an interruption hold");
            if(boundary=="placement") Check(Events.Contains("traffic-dispose"),"placement refusal releases admitted traffic record");
            car.Condition=.65f;
            CrewDriveProbe.FlushObservedDamage(car);
            Check(car.vehicleInstance.damage!=.65f,"refused admission did not register late damage writes");
        }
        var count=Events.Count; Tick(probe,"FixedUpdate");
        Check(Events.Count==count,"failed startup cannot later drive");
    }
    static void Stale() {
        var car=Setup(); var probe=Driving(car); var first=Identity();
        var count=Events.Count;
        Refused(()=>CrewDriveProbe.StartProbe("truck",new[]{10,11},2),"duplicate start");
        Check(ReferenceEquals(probe,Active()) && Events.Count==count && Owners()==1,"duplicate start does not touch live owner");
        Stop(first); Released(car); Events.Clear();
        probe=Driving(car); var second=Identity(); count=Events.Count;
        Check(first.Session==second.Session && first.Generation<second.Generation,"generations increase in same session");
        Refused(()=>Stop(first),"old generation");
        Refused(()=>CrewDriveProbe.StopProbe(Guid.NewGuid(),second.Generation),"foreign session");
        Check(ReferenceEquals(probe,Active()) && Events.Count==count && Owners()==1,"stale stop cannot touch new owner");
        Stop(second); CrewDriveProbe.ResetSession(); Events.Clear();
        probe=Driving(car); count=Events.Count;
        Refused(()=>Stop(second),"detached session");
        Check(Events.Count==count && CrewDriveProbe.Owns(car),"previous session cannot stop new session");
        CrewDriveProbe.StopAll("done"); Released(car);
    }
    static void Replacement() {
        var old=Setup(); var probe=Driving(old); var native=CrewProbeVehicle.Last!;
        var replacement=new CarController { vehicleInstance=old.vehicleInstance, Input=77 };
        Helpers.VehicleHelper.Cars["truck"]=replacement;
        replacement.vehicleInstance.fuel=18; replacement.vehicleInstance.damage=.73f;
        Check(!CrewDriveProbe.Owns(replacement) && CrewDriveProbe.StopVehicle(replacement,"replacement entry"),"replacement does not inherit old ownership");
        Tick(probe,"FixedUpdate"); Released(old);
        Check(native.Restores==1 && ReferenceEquals(native.Car,old),"captured old body restored");
        Check(replacement.Input==77 && replacement.NativeTouches==0,"new controller receives no old hold/sync/restore");
        Check(replacement.vehicleInstance.fuel==18 && replacement.vehicleInstance.damage==.73f && !Events.Contains("sync"),
            "old cleanup never writes the replacement's shared vehicle instance");
        old.Condition=.95f; CrewDriveProbe.FlushObservedDamage(old);
        Check(replacement.vehicleInstance.damage==.73f,"old damage callback cannot overwrite replacement");
        Events.Clear(); var next=Start(replacement); Tick(probe,"OnDestroy");
        Check(ReferenceEquals(next,Active()) && CrewDriveProbe.Owns(replacement) && Events.Count==0,"old component callbacks cannot mutate new owner");
        CrewDriveProbe.StopAll("done"); Released(replacement);
    }
    static bool Enter(CarController car) {
        if(!Hook("Enter",car)) return false;
        Check(!CrewDriveProbe.Owns(car),"prefix releases probe before native entry");
        Boundary("native-entry"); car.Input=99; car.controlledByPlayer=true;
        SaveGameManager.Current.ActiveVehicleId=car.vehicleInstance.id; return true;
    }
    static void Entry() {
        var car=Setup(); var probe=Driving(car);
        Check(Enter(car),"entry allowed after cleanup");
        Check(Events.IndexOf("restore")<Events.IndexOf("native-entry"),"native input installed after restoration");
        Tick(probe,"FixedUpdate"); Tick(probe,"OnDestroy");
        Check(car.Input==99,"later old callbacks preserve player input"); Released(car);
        car.controlledByPlayer=false; SaveGameManager.Current.ActiveVehicleId=null; Events.Clear();
        probe=Driving(car); CrewProbeVehicle.Fail="sync";
        Check(!Enter(car) && !car.controlledByPlayer && SaveGameManager.Current.ActiveVehicleId==null &&
            !Events.Contains("native-entry") && CrewDriveProbe.Owns(car),"failed cleanup refuses native entry");
        CrewProbeVehicle.Fail=null; Check(Enter(car),"entry retry succeeds"); Released(car);
    }
    static int NativeSaveCalls;
    static bool Save() {
        object[] args={true};
        if(!Hook("Save",args)) return (bool)args[0];
        NativeSaveCalls++; Boundary("native-save-bookkeeping"); return true;
    }
    static void SaveCase() {
        var car=Setup(); var probe=Driving(car); CrewProbeVehicle.Fail="restore";
        Check(!Save() && NativeSaveCalls==0,"save prefix sets false and refuses before native bookkeeping");
        Check(CrewDriveProbe.Owns(car) && Phase()=="Holding","refused save retains cleanup owner");
        Tick(probe,"FixedUpdate"); CrewProbeVehicle.Fail=null;
        Check(Save() && NativeSaveCalls==1,"retry reaches native save once"); Released(car);
        Check(Events.Last()=="native-save-bookkeeping","native bookkeeping starts after cleanup");
    }
    static void Consumption() {
        var car=Setup(); var probe=Driving(car); var native=CrewProbeVehicle.Last!;
        Check(native.FuelBefore==40 && native.DamageBefore==.1f,"boundary captured initial observations");
        car.fuelModule.amount=31.25f; car.Condition=.35f;
        CrewDriveProbe.StopAll("after observed native consumption"); Released(car);
        Check(car.fuelModule.amount==31.25f && car.Condition==.35f,"adapter never resets actual fuel/damage");
        Check(car.vehicleInstance.fuel==31.25f && car.vehicleInstance.damage==.35f,"cleanup synchronizes actual observations to native instance");
        var s=CrewDriveProbe.Status();
        Check((float)s["fuel_now_l"]! ==31.25f && (float)s["damage_now"]! ==.35f,"status reports actual remaining tank and damage");
    }
    static void Repeat() {
        var car=Setup(); long generation=0;
        for(var i=0;i<50;i++) {
            var probe=Start(car); Tick(probe,"Update"); Tick(probe,"FixedUpdate");
            Check(Identity().Generation>generation,"generation monotonic"); generation=Identity().Generation;
            Check(CrewDriveProbe.StopAll("cycle "+i),"repeated stop"); Released(car);
            Check((int)CrewDriveProbe.Status()["junction_registrations"]! ==0 && (int)CrewDriveProbe.Status()["collider_overrides"]! ==0,"no residual traffic resources");
            Tick(probe,"OnDestroy");
        }
        Check(CrewProbeVehicle.Created==50 && CrewProbeTraffic.Created==50,"all fifty lifecycles reached native boundaries");
    }
    static void CollisionCase() {
        var car=Setup(); var probe=Driving(car); Tick(probe,"FixedUpdate");
        Tick(probe,"OnCollisionEnter",new Collision { collider=new Collider { name="other-car" }, relativeVelocity=new Vector3(0,0,3) });
        Released(car); var s=CrewDriveProbe.Status(); var entry=(JObject)((JArray)s["contact_log"]!)[0];
        Check((int)s["contacts"]! ==1 && (string)entry["attribution"]! =="requires inspection","collision does not infer fault");
        Check((string)entry["other"]! =="other-car" && (float)entry["relative_speed_ms"]! ==3 && (int)entry["step"]! ==1,"collision captures actual observation context");
        Check(entry["throttle"]!=null && entry["precontact_desired_ms"]!=null,"precontact command included for inspection");
    }
    static void LateDamage() {
        var car=Setup(); var probe=Driving(car);
        CrewDriveProbe.StopAll("contact before DamageHandler callback");
        car.Condition=.65f;
        CrewDriveProbe.FlushObservedDamage(car);
        Check(car.vehicleInstance.damage==.65f,"damage from later native collision callback is saved");
        SaveGameManager.Current=new GameInstance(); car.Condition=.9f;
        CrewDriveProbe.FlushObservedDamage(car);
        Check(car.vehicleInstance.damage==.65f,"stale session cannot flush damage into new world");
    }
    static void EndpointMotion(string kind) {
        var car=Setup(); var probe=Driving(car); var native=CrewProbeVehicle.Last!;
        if(kind=="lateral") native.Body.velocity=new Vector3(2,0,0);
        else native.Body.angularVelocity=new Vector3(0,1,0);
        Tick(probe,"End","Endpoint reached by physical braking",false);
        Released(car);
        Check(Phase()=="Cancelled" && (bool)CrewDriveProbe.Status()["forced_interruption_hold"]!,"residual motion is an interruption, never braking success");
    }
    static void Main(string[] args) {
        CrewDriveProbe.HooksReady=true;
        switch(args[0]) {
            case "wheel-guard": WheelGuard(); break;
            case "wheel-session": WheelSession(); break;
            case "starting": Starting(); break;
            case "lifecycle": Lifecycle(); break;
            case "failure": Failure(args[1]); break;
            case "startup-failure": StartupFailure(args[1]); break;
            case "stale": Stale(); break;
            case "replacement": Replacement(); break;
            case "entry": Entry(); break;
            case "save": SaveCase(); break;
            case "consumption": Consumption(); break;
            case "repeat": Repeat(); break;
            case "collision": CollisionCase(); break;
            case "late-damage": LateDamage(); break;
            case "endpoint-motion": EndpointMotion(args[1]); break;
            default: throw new Exception("unknown case");
        }
        Console.WriteLine("PASS: "+string.Join(" ",args));
    }
}

// Controlled Unity lifetime: destroyed components are removed immediately so tests
// can start another lifecycle. Old callback invocations are exercised explicitly.
namespace UnityEngine {
    public class Object {
        static int sequence; readonly int identity=++sequence;
        public int GetInstanceID()=>identity;
        public static void Destroy(Object obj) { if(obj is MonoBehaviour component) component.gameObject.Remove(component); }
    }
    public class MonoBehaviour : Object { public GameObject gameObject=null!; }
    public class GameObject {
        readonly List<MonoBehaviour> components=new();
        public T AddComponent<T>() where T:MonoBehaviour,new() { var item=new T { gameObject=this }; components.Add(item); return item; }
        public T? GetComponent<T>() where T:class=>components.OfType<T>().FirstOrDefault();
        public void Remove(MonoBehaviour obj)=>components.Remove(obj);
    }
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude=>x*x+y*y+z*z; public Vector3 normalized=>magnitude>0?new(x/magnitude,y/magnitude,z/magnitude):new(); public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z); public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z; public void Normalize(){this=normalized;} public float magnitude=>MathF.Sqrt(sqrMagnitude);
    }
    public struct Quaternion { }
    public class Transform { public Vector3 forward=new(0,0,1); }
    public class Rigidbody { public Vector3 velocity,angularVelocity; public bool isKinematic=true; }
    public class Collider { public string name=""; }
    public class Collision { public Collider? collider; public Vector3 relativeVelocity; }
    public static class Time { public static float realtimeSinceStartup,timeScale=1,fixedDeltaTime=.02f; }
    public static class Mathf { public static float Sqrt(float v)=>MathF.Sqrt(v);
        public static float Abs(float x)=>MathF.Abs(x); public static float Atan2(float x,float y)=>MathF.Atan2(x,y);
        public static float Min(float a,float b)=>MathF.Min(a,b); public static float Max(float a,float b)=>MathF.Max(a,b);
    }
}
public class VehicleInstance { public string id="",vehicleTypeName=FoodTrucksMod.TruckTypeName; public float fuel=40,damage=.1f; }
public class VehicleController : UnityEngine.Object {
    public VehicleInstance vehicleInstance=new(); public GameObject gameObject=new(); public Transform transform=new();
    public T? GetComponent<T>() where T:class=>gameObject.GetComponent<T>();
}
public class CarController : VehicleController {
    public bool controlledByPlayer; public NativeController vehicleController=new(); public Fuel fuelModule=new();
    public float Condition=.1f,Input=7; public int NativeTouches;
    public float GetCurrentCondition()=>Condition;
}
public class Fuel { public bool useFuel=true; public float amount=40; }
public class NativeController { public bool IsInitialized; public Steering steering=new(); }
public class Steering { public float angle; }
namespace NWH.WheelController3D { public class WheelController { public Rigidbody? ParentRigidbody; public void Step(){} } }
namespace NWH.VehiclePhysics2.Damage {
    public class DamageHandler {
        public CarController? Car;
        public static bool IsCollisionValid(Collision collision)=>true;
        public T? GetComponentInParent<T>() where T:class=>Car as T;
    }
}
public class GameInstance { public List<VehicleInstance> VehicleInstances=new(); public string? ActiveVehicleId; }
public static class SaveGameManager { public static GameInstance Current=new(); }
public static class GameManager { public static bool isCitySceneBeingUnloaded; }
public static class BuildingManager { public static bool IsInsideBuilding; }
public class GameSpeed { public bool paused; public int speed=1; }
public class GameSpeedController { }
namespace Helpers {
    public static class VehicleHelper {
        public static Dictionary<string,CarController> Cars=new();
        public static VehicleController? GetVehicleController(VehicleInstance instance)=>Cars.GetValueOrDefault(instance.id);
    }
}
namespace Timemachine { public class TimeMachine { public void StartTimeMachine(){} } }
namespace UI.ItemPanel { public class ItemPanelUI { } }
namespace Vehicles { public class SkipBridgeHelper { } }
namespace HarmonyLib {
    // Installation is intentionally not emulated; call the compiled real handlers.
    public static class Priority { public const int First=800; }
    public class HarmonyMethod { public int priority; public HarmonyMethod(Type t,string name){} }
    public class Harmony { public void Patch(MethodInfo original,HarmonyMethod? prefix=null,HarmonyMethod? postfix=null){} }
    public static class AccessTools { public static MethodInfo? Method(Type t,string name,Type[]? args)=>null; }
}
namespace FoodTrucks {
    public static class FoodTrucksMod { public const string TruckTypeName="foodtrucks:truck"; }
    public static class TestBridge { public static bool Running=true; }
    public class BridgeError : Exception { public BridgeError(string message):base(message){} }
    public static class TestBridgeCommands {
        public static string? DrivingVehicle; public static void RequireFtCity(string name){}
        public static bool Paused()=>false; public static bool TimeSkipRunning()=>false; public static bool ProbeClockNormal=>true;
    }
    public static class TruckWindow { public static bool PlayerAt(string id)=>false; }
    public static class FoodTrucksSession { public static bool CanWrite=true; public static SaveState CurrentState=new(); }
    public static class Log {
        public static List<string> Errors=new(); public static void Info(string value){}
        public static void Error(string value)=>Errors.Add(value); public static void Error(Exception ex)=>Error(ex.ToString());
    }
    // Instrument the native wrapper boundary, not the lifecycle implementation.
    internal sealed class CrewProbeBody { internal long FullValidations,MeshVertexReads; internal string? Error; internal NWH.WheelController3D.WheelController? Wheel; internal bool ContainsWheel(NWH.WheelController3D.WheelController w)=>w==Wheel; internal string? ValidateWheelStep(NWH.WheelController3D.WheelController w)=>Error; }
    internal sealed class CrewProbeVehicle {
        internal static int Created; internal static CrewProbeVehicle? Last; internal static string? Fail;
        internal readonly CarController Car; internal readonly NativeController Nwh; internal readonly Rigidbody Body=new(); internal readonly CrewProbeBody Contract=new();
        internal readonly float Wheelbase=3,FrontAhead=4,RearBehind=1,Width=2,MaxSteer=30,TopMs=10,RearZ=0;
        internal readonly float FuelBefore,DamageBefore;
        internal int Commands,Restores; internal float Throttle,Handbrake; readonly float originalInput;
        internal CrewProbeVehicle(CarController car) {
            Car=car; Nwh=car.vehicleController; FuelBefore=car.fuelModule.amount; DamageBefore=car.Condition;
            originalInput=car.Input; Created++; Last=this; Harness.Boundary("construct");
        }
        internal static Vector3 RearPosition;
        internal Vector3 RearAxle=>RearPosition; internal float Speed=>Body.velocity.z;
        internal float LockAt(float speed)=>30;
        internal void Begin() { Body.isKinematic=false; Car.Input=0; Boundary("begin"); }
        internal void Command(float steer,float throttle,float brake,float handbrake) {
            Car.NativeTouches++; Commands++; Throttle=throttle; Handbrake=handbrake; Harness.Boundary("command");
        }
        void Boundary(string name) { Car.NativeTouches++; Harness.Boundary(name); if(Fail==name) throw new Exception("injected "+name); }
        internal void StationaryHold() { Body.velocity=new(); Body.angularVelocity=new(); Body.isKinematic=true; Throttle=0; Handbrake=1; Boundary("hold"); }
        internal void Synchronize() {
            Boundary("sync"); Car.vehicleInstance.fuel=Car.fuelModule.amount; Car.vehicleInstance.damage=Car.Condition;
        }
        internal void Restore() {
            Boundary("restore"); Restores++; Car.Input=originalInput;
            // Restoring captured controls may remove the cleanup hold. A failure
            // in another boundary must trigger a fresh hold AFTER this callback.
            Throttle=.2f; Handbrake=.3f;
        }
    }
    internal sealed class CrewProbeTraffic : IDisposable {
        internal static int Created,Live; internal static bool FailDispose,FailCreate;
        internal static int RecordCount=>Live;
        internal static void NativeRemoving(CarController car) { }
        bool disposed; internal int Registrations=>disposed ? 0 : 1; internal int ColliderOverrides=>disposed ? 0 : 1;
        internal bool Retained=>false; internal string? Failure=>null; internal int NativeTrafficId=>-1700000001;
        internal float ObservationMilliseconds=>0; internal int? BlockerId=>null; internal int? BlockerLayer=>null; internal Vector3 BlockerPosition=>default;
        internal string? BlockerQuery=>null; internal float? BlockerGap=>null; internal int SignalStopChanges=>0;
        internal float? SignalChangeSpeed=>null; internal float? SignalChangeClearance=>null;
        internal int Observed=>0;
        internal static bool TryCreate(CarController car,int[] ids,float rear,float front,float behind,float width,
            out CrewProbeTraffic traffic,out Vector3[] points,out string? error, Func<Vector3[], ProbeRoadPath>? prepareBeforeAdmission=null,CrewProbeBody? bodyContract=null) {
            Harness.Boundary("traffic-create");
            if(FailCreate) { traffic=null!; points=Array.Empty<Vector3>(); error="injected traffic validation"; return false; }
            traffic=new(); Created++; Live++;
            points=new[]{new Vector3(0,0,0),new Vector3(0,0,50)}; prepareBeforeAdmission?.Invoke(points); error=null; return true;
        }
        internal void Observe(float distance,float speed,out float? stop,out float? gap,out string? error) { stop=null; gap=null; error=null; }
        public void Dispose() {
            Harness.Boundary("traffic-dispose"); if(FailDispose) throw new Exception("injected traffic dispose");
            if(!disposed) { Live--; disposed=true; }
        }
    }
}
'''
