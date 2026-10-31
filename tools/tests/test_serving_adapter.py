"""Exercise the actual native-control adapter with controlled game/UI boundaries, without a game process."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class ServingAdapterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        directory = tempfile.TemporaryDirectory(prefix='ft-serving-adapter-')
        cls.addClassCleanup(directory.cleanup)
        folder = Path(directory.name)
        major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
        files = [ROOT / 'unity/FoodTrucks/Scripts/ServingWork.cs', ROOT / 'Core/Fleet/ServingTime.cs']
        includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>''')
        source = HARNESS
        # Compile the real caller and Harmony handlers too: failures here must not skip CloseDays,
        # and the entry regression depends on SetVehicle's prefix running before its nested patches.
        for marker, filename, signature in (
                ('/* LATE_UPDATE */', 'CartSelling.cs', 'private void LateUpdate()'),
                ('/* PANEL_PREFIX */', 'Patches.cs', 'private static void PanelVehiclePrefix('),
                ('/* PANEL_POSTFIX */', 'Patches.cs', 'private static void PanelVehicle(')):
            text = (ROOT / 'unity/FoodTrucks/Scripts' / filename).read_text()
            start = text.index(signature)
            end = text.index('{', start) + 1
            depth = 1
            while depth:
                depth += (text[end] == '{') - (text[end] == '}')
                end += 1
            source = source.replace(marker, text[start:end])
        (folder / 'Program.cs').write_text(source)
        built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q', '-m:1', '-nr:false',
                                '-p:UseSharedCompilation=false'], capture_output=True, text=True, timeout=120)
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        cls.dll = folder / f'bin/Debug/net{major}.0/Harness.dll'

    def run_case(self, *args):
        ran = subprocess.run(['dotnet', str(self.dll), *args], capture_output=True, text=True, timeout=30)
        self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
        self.assertIn('PASS:', ran.stdout)

    def test_native_duration_speed_skip_sales_and_cleanup(self):
        self.run_case('controls')

    def test_destroyed_unity_objects_do_not_poison_next_session(self):
        for target in ('panel', 'speed', 'machine', 'ui', 'missing-ui'):
            with self.subTest(target=target):
                self.run_case('destroyed', target)

    def test_cleanup_failures_are_independent_and_reset_clears_stale_state(self):
        self.run_case('failures')
        self.run_case('reset')

    def test_vehicle_entry_before_late_update_keeps_native_driving_hud(self):
        patches = (ROOT / 'unity/FoodTrucks/Scripts/Patches.cs').read_text()
        self.assertRegex(patches, r'Patch\(typeof\(ItemPanelUI\), "SetVehicle",[^;]+prefix: nameof\(PanelVehiclePrefix\), postfix: nameof\(PanelVehicle\)\)')
        self.assertRegex(patches, r'Patch\(typeof\(VehicleController\), "DriveVehicle",[^;]+prefix: nameof\(ServingWork.DrivingPrefix\)\)')
        for kind in ('cart', 'truck'):
            for path in ('immediate', 'navigation', 'direct', 'other-vehicle'):
                with self.subTest(kind=kind, path=path):
                    self.run_case('entry', kind, path)

    def test_entered_vehicle_is_excluded_from_read_only_patches(self):
        self.run_case('entered')

    def test_cart_update_failure_is_logged_and_closing_days_still_runs(self):
        self.run_case('update')

    def test_quit_while_serving_skips_stock_panel_but_keeps_cleanup(self):
        # Issue #19: the native Toggle(false) dereferences GameManager, which is gone or unreturned while quitting.
        for kind in ('cart', 'truck'):
            for manager in ('missing', 'missing-unloading', 'unloading'):
                with self.subTest(kind=kind, manager=manager):
                    self.run_case('quit', kind, manager)

    def test_normal_endings_close_the_stock_panel_once(self):
        for kind, how in (('cart', 'finish'), ('cart', 'walk'), ('cart', 'reset'), ('truck', 'finish'), ('truck', 'reset')):
            with self.subTest(kind=kind, how=how):
                self.run_case('normal', kind, how)


HARNESS = r'''
using FoodTrucks;
using PlayerActivity;
using UI.ItemPanel;
using UnityEngine.UI;
class Harness {
    public static void Check(bool yes,string why) { if(!yes) throw new Exception(why); }
    static ServingActivity Open(string id="cart") {
        ServingActivity activity=id=="truck" ? new TestHatchActivity(id) : new CartWorkActivity(id,()=>CartSelling.Released=true);
        PlayerActivityUI.Show(activity); activity.ChangeState(PlayerActivityState.Running); return activity;
    }
    static System.Reflection.FieldInfo Field(string name)=>typeof(ServingWork).GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
    public static void Released() {
        foreach(var name in new[]{"active","cargo","stockPanel"}) Check(Field(name).GetValue(null)==null,"ownership released before cleanup: "+name);
    }
    static void Main(string[] args) {
        switch(args[0]) {
            case "controls": Controls(); break;
            case "destroyed": Destroyed(args[1]); break;
            case "failures": Failures(); break;
            case "reset": Reset(); break;
            case "entry": Entry(args[1],args[2]); break;
            case "entered": Entered(); break;
            case "update": UpdateFailure(); break;
            case "quit": Quit(args[1],args[2]); break;
            case "normal": Normal(args[1],args[2]); break;
            default: throw new Exception("unknown scenario");
        }
        Console.WriteLine("PASS: "+string.Join(" ",args));
    }
    static void Destroyed(string target) {
        var activity=Open(); var ui=InstanceBehavior<UI.UIs>.Instance;
        var panel=ui.playerHUD.itemPanelUI; var speed=ui.gameSpeed; var machine=ui.timeMachine;
        ServingWork.Arm(activity); ServingWork.SkipStarted(); machine.isRunning=true; speed.isFastForwarding=true;
        UnityEngine.Object? destroyed=target switch { "panel"=>panel,"speed"=>speed,"machine"=>machine,"ui"=>ui,_=>null };
        if(destroyed is not null) destroyed.Destroyed=true;
        if(target=="missing-ui") InstanceBehavior<UI.UIs>.Instance=null!;
        activity.MarkFinished();
        Released(); Check(activity.GetState()==PlayerActivityState.Finished,"activity finished during teardown");
        Check(SaveGameManager.Current.VehicleInstances[0].Updated==null,"listener detached even when Unity is gone");
        Check(Helpers.EnergyHelper.Work==null,"energy removed even when Unity is gone");
        Check(UnityEngine.Object.InvalidCalls==0,"destroyed Unity objects are never called");
        if(target is "ui" or "missing-ui") Check(speed.Resets==0 && machine.Cancelled==0,"destroyed/missing UIs are not traversed");
        Check(Log.Errors.Count==0,"destroyed-object checks avoid exceptions");
        ServingWork.Reset();
        InstanceBehavior<UI.UIs>.Instance=new(); PlayerActivityUI.IsPanelOpen=false;
        foreach(var id in new[]{"cart","truck"}) {
            var next=Open(id); Check(ServingWork.VehicleId==id && Helpers.EnergyHelper.Work!=null,"next session can start "+id);
            next.Finish(); Released();
        }
    }
    // CartSelling.OnDisable -> ServingWork.Reset during a city unload or application quit, with the HUD panel still alive.
    static void Quit(string id,string manager) {
        var activity=Open(id); var ui=InstanceBehavior<UI.UIs>.Instance;
        var panel=ui.playerHUD.itemPanelUI; var vehicle=SaveGameManager.Current.VehicleInstances.First(v=>v.id==id);
        ServingWork.Arm(activity); ServingWork.SkipStarted(); ui.timeMachine.isRunning=true; ui.gameSpeed.isFastForwarding=true;
        Check(panel.Visible && vehicle.Updated!=null && Helpers.EnergyHelper.Work!=null,"work started with a visible stock panel");
        bool paid=false; CartSelling.OnPay=()=>paid=true;
        if(manager.StartsWith("missing")) InstanceBehavior<GameManager>.Instance=null!;
        GameManager.isCitySceneBeingUnloaded=manager.EndsWith("unloading");
        ServingWork.Reset();
        Check(!Log.Errors.Any(e=>e.Contains("stock panel")),"no stock-panel error: "+string.Join("; ",Log.Errors));
        Check(panel.Toggles==0,"no stock-panel toggle while the manager is gone or the city unloads");
        Check(Log.Errors.Count==0,"quit teardown logs no errors: "+string.Join("; ",Log.Errors));
        Check(paid,"held orders are still booked");
        Check(vehicle.Updated==null,"cargo listener removed");
        Check(Helpers.EnergyHelper.Work==null,"work energy removed");
        Check(!ui.gameSpeed.isFastForwarding && ui.gameSpeed.Resets==1,"speed still reset");
        Check(ui.timeMachine.Cancelled==1,"time machine still cancelled");
        Check(activity.GetState()==PlayerActivityState.Finished,"activity finished");
        foreach(var field in typeof(ServingWork).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)) {
            var value=field.GetValue(null);
            Check(field.FieldType==typeof(bool) ? Equals(value,false) : value==null,"reset clears "+field.Name);
        }
        // A new city session (fresh manager) still starts and closes its panel normally.
        InstanceBehavior<GameManager>.Instance=new(); GameManager.isCitySceneBeingUnloaded=false; PlayerActivityUI.IsPanelOpen=false;
        foreach(var next in new[]{"cart","truck"}) {
            var work=Open(next); Check(ServingWork.VehicleId==next && panel.Visible,"next session can start "+next);
            var toggles=panel.Toggles; work.Finish(); Released();
            Check(panel.Toggles==toggles+1 && !panel.Visible,"next session closes its stock panel "+next);
            PlayerActivityUI.IsPanelOpen=false;
        }
        Check(Log.Errors.Count==0,"next sessions log no errors");
    }
    // stop_work, walking away, stepping out of the hatch and closing (OnDisable while playing), with a live manager and city.
    static void Normal(string id,string how) {
        var activity=Open(id); var panel=InstanceBehavior<UI.UIs>.Instance.playerHUD.itemPanelUI;
        var game=InstanceBehavior<GameManager>.Instance; game.selectedVehicle=new VehicleController();
        Check(panel.Visible && panel.Toggles==0,"work started with a visible stock panel");
        if(how=="finish") activity.Finish();
        else if(how=="walk") ServingWork.UpdateCart(new CartServing());
        else ServingWork.Reset();
        Released();
        Check(activity.GetState()==PlayerActivityState.Finished,"activity finished by "+how);
        Check(panel.Toggles==1 && panel.Hides==1 && !panel.Visible,"stock panel closed exactly once by "+how);
        Check(game.selectedVehicle==null,"native Toggle(false) ran to its end");
        Check(Log.Errors.Count==0,"normal endings log no errors: "+string.Join("; ",Log.Errors));
        ServingWork.Reset(); Check(panel.Toggles==1,"a later reset does not toggle again");
    }
    static void Failures() {
        var activity=Open(); var ui=InstanceBehavior<UI.UIs>.Instance;
        ServingWork.Arm(activity); ServingWork.SkipStarted(); ui.timeMachine.isRunning=true; ui.gameSpeed.isFastForwarding=true;
        bool releasedBeforePay=false;
        CartSelling.OnPay=()=>{
            releasedBeforePay=new[]{"active","cargo","stockPanel"}.All(name=>Field(name).GetValue(null)==null);
            throw new Exception("payment failed");
        };
        SaveGameManager.Current.VehicleInstances[0].ThrowOnRemove=true;
        ui.playerHUD.itemPanelUI.ThrowOnToggle=true; Helpers.EnergyHelper.ThrowOnRemove=true;
        ui.gameSpeed.ThrowOnReset=true; ui.timeMachine.ThrowOnCancel=true;
        activity.MarkFinished(); Released();
        Check(releasedBeforePay,"ownership is released before the first fallible callback");
        Check(Log.Errors.Count==6,"each cleanup failure is logged independently");
        Check(!ui.gameSpeed.isFastForwarding && ui.gameSpeed.Resets==1,"panel/energy failures do not skip speed cleanup");
        Check(ui.timeMachine.Cancelled==1,"speed failure does not skip time-machine cleanup");
        // Repeated End/MarkFinished must not run callbacks or mutate a newer panel.
        activity.MarkFinished(); ServingWork.End(activity);
        Check(Log.Errors.Count==6,"cleanup is idempotent after failures");
        ServingWork.Reset();
        foreach(var name in new[]{"active","armed","skipVehicle","cargo","stockPanel"}) Check(Field(name).GetValue(null)==null,"reset clears "+name);
    }
    static void Reset() {
        var activity=Open(); activity.MarkFinished();
        // Simulate the stale Finished activity left by the former throwing End implementation.
        Field("active").SetValue(null,activity); Field("armed").SetValue(null,activity);
        var cargo=SaveGameManager.Current.VehicleInstances[0]; cargo.Updated+=ServingWork.RefreshStock;
        Helpers.EnergyHelper.Work=EnergyConsumption.High;
        Field("cargo").SetValue(null,cargo);
        Field("stockPanel").SetValue(null,InstanceBehavior<UI.UIs>.Instance.playerHUD.itemPanelUI);
        Field("skipVehicle").SetValue(null,"cart"); Field("skipClosed").SetValue(null,true);
        Field("catchingUp").SetValue(null,true);
        ServingWork.Reset();
        Check(cargo.Updated==null && Helpers.EnergyHelper.Work==null,"reset cleans resources of an already Finished activity");
        foreach(var field in typeof(ServingWork).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)) {
            var value=field.GetValue(null);
            Check(field.FieldType==typeof(bool) ? Equals(value,false) : value==null,"reset unconditionally clears "+field.Name);
        }
        Field("active").SetValue(null,activity); Field("ending").SetValue(null,true);
        ServingWork.Reset(); Released(); Check(Equals(Field("ending").GetValue(null),false),"reset clears the reentrancy guard too");
        var next=Open("truck"); var calls=HireRoutes.Calls; ServingWork.BeforeLeaving("truck");
        Check(HireRoutes.Calls==calls+1,"catch-up is usable in next session"); next.Finish();
    }
    static void Entry(string id,string path) {
        var activity=Open(id); var panel=InstanceBehavior<UI.UIs>.Instance.playerHUD.itemPanelUI;
        InstanceBehavior<UI.UIs>.Instance.gameSpeed.isFastForwarding=true;
        var vehicle=SaveGameManager.Current.VehicleInstances.First(v=>v.id==(path=="other-vehicle" ? "other" : id));
        var controller=new VehicleController { vehicleInstance=vehicle };
        if(path=="direct") controller.EnterVehicle();
        else {
            ServingWork.DrivingPrefix();
            Check(activity.GetState()==PlayerActivityState.Finished && ServingWork.VehicleId==null,"drive intent finishes work before native entry/navigation");
            Check(CartSelling.Caught.Count>0 && CartSelling.CaughtOnFoot,"last elapsed sales are caught up on foot");
            if(path=="navigation") { PlayerActivityUI.IsPanelOpen=false; ServingWork.UpdateCart(new CartServing()); }
            controller.EnterVehicle();
        }
        Released();
        Check(activity.GetState()==PlayerActivityState.Finished && Helpers.EnergyHelper.Work==null,"entry ends work immediately");
        Check(id!="cart" || CartSelling.Released,"vehicle entry releases the cart serving stance");
        Check(!InstanceBehavior<UI.UIs>.Instance.gameSpeed.isFastForwarding,"entry resets fast-forward");
        Check(panel.parkButton.gameObject.activeSelf && panel.cargoContainer.Buttons.All(b=>b.interactable),"native Park and cargo controls survive same-frame entry");
        var hides=panel.Hides; activity.Finish(); ServingWork.Reset();
        Check(panel.Visible && panel.Hides==hides && panel.Vehicle==vehicle,"later cleanup preserves the game's entered-vehicle HUD");
        Check(SaveGameManager.Current.ActiveVehicleId==vehicle.id,"cleanup preserves the game's selected/entered vehicle");
    }
    static void Entered() {
        var activity=Open(); var panel=InstanceBehavior<UI.UIs>.Instance.playerHUD.itemPanelUI;
        SaveGameManager.Current.ActiveVehicleId="cart";
        panel.parkButton.gameObject.SetActive(true); panel.cargoContainer.Buttons=new[]{new Button()};
        ServingWork.ReadOnlyButtons(panel); ServingWork.ReadOnlyCargo(panel);
        Check(panel.parkButton.gameObject.activeSelf && panel.cargoContainer.Buttons.All(b=>b.interactable),"entered vehicles are excluded even before ownership cleanup");
        activity.Finish();
    }
    static void UpdateFailure() {
        PlayerActivityUI.ThrowOnShow=true;
        CartSelling.UpdateWork(new CartServing { VehicleId="cart" });
        Check(Log.Errors.Count==1 && Log.Errors[0].Contains("cart update"),"UpdateCart exception logged by actual LateUpdate");
        Check(CartSelling.ClosedDays==1,"failed work UI does not suppress hourly closing");
    }
    static void Controls() {
        var activity=Open(); var ui=InstanceBehavior<UI.UIs>.Instance;
        var panel=ui.playerHUD.itemPanelUI; var speed=ui.gameSpeed;
        Check(Helpers.EnergyHelper.Work==EnergyConsumption.High,"use the native register's actual energy setting");
        Check(panel.Vehicle==SaveGameManager.Current.VehicleInstances[0],"actual vehicle cargo, without selecting or entering it");
        Check(SaveGameManager.Current.ActiveVehicleId==null,"cargo HUD does not enter a vehicle");
        var templateButton=new Button() { transform=panel.cargoSlotTemplate };
        panel.cargoContainer.Buttons=panel.cargoContainer.Buttons.Append(templateButton).ToArray();
        ServingWork.ReadOnlyButtons(panel); ServingWork.ReadOnlyCargo(panel);
        Check(!panel.parkButton.gameObject.activeSelf && panel.cargoContainer.Buttons.All(b=>b==templateButton || !b.interactable),"HUD cannot move cargo or drive");
        Check(templateButton.interactable,"future normal cargo rows keep an interactive template");
        var toggle=ui.playerActivityUI.NativeToggle;
        ServingWork.SpeedOptionsPostfix(ui.playerActivityUI,toggle);
        Check(toggle.transform.parent.gameObject.activeSelf,"outdoor work gets native fast-forward");
        Check(!ServingWork.ActionPrefix(ui.playerActivityUI,(PlayerAction)2,toggle) && speed.isFastForwarding,"native shortcut reaches original toggle listener");
        Check(ServingWork.ActionPrefix(ui.playerActivityUI,(PlayerAction)3,toggle),"other native actions still handled by the game");
        ServingWork.Control("cart","fast_forward",null,false); Check(!speed.isFastForwarding,"native toggle off");
        ServingWork.Control("cart","skip_work",30,true);
        Check(activity.GetRemainingMinutesForTimeMachine()==30 && ui.timeMachine.Goal==30,"slider selection is absolute, not additive");
        Check(ServingWork.IsSkipping("cart") && !ServingWork.IsSkipping("other"),"only the selected window serves through a skip");
        float delta=100; ServingWork.TickPrefix(ref delta); Check(delta==.25f,"world waits at the next sales minute");
        activity.Perform(30); activity.Perform(30);
        Check(CartSelling.Caught.All(m=>m==100.75),"end-of-skip Perform uses absolute time, even when called twice");
        var refresh=panel.Refreshes; SaveGameManager.Current.VehicleInstances[0].Updated?.Invoke();
        Check(panel.Refreshes==refresh+1,"every cargo event refreshes the read-only HUD");
        // Even an immediate crew reopen must not carry this work skip past the first closure.
        ServingWork.WindowClosed("cart");
        delta=100; ServingWork.TickPrefix(ref delta); Check(delta==0,"no further world/needs tick after closure");
        ServingWork.TickPostfix();
        Check(ui.timeMachine.Cancelled==1 && !ui.timeMachine.isRunning,"window closure stops the native skip");
        activity.Finish(); Check(Helpers.EnergyHelper.Work==null && CartSelling.Released,"work spender and stance released");
        Check(!panel.Visible && ServingWork.VehicleId==null,"work cargo hidden on end");
        Check(SaveGameManager.Current.VehicleInstances[0].Updated==null,"cargo listener removed");
        ServingWork.SkipEnded(); CartSelling.Skipping=false;
        FoodTrucks.Core.Saves.StreetSales.Vehicle.IsOpen=true;
        activity=Open(); ServingWork.Control("cart","fast_forward",null,true);
        var hidden=panel.Hides; ServingWork.OtherPanel(); activity.Finish();
        Check(!speed.isFastForwarding && speed.Resets>0,"leaving restores native speed");
        Check(panel.Hides==hidden,"a newer game panel is preserved");
        Check(HireRoutes.Calls>0,"pending routes and sales are caught up before dropping the server");
        Check(Log.Errors.Count==0,"normal controls have no adapter errors");
        Console.WriteLine("PASS: native work controls and cleanup");
    }
}
public class InstanceBehavior<T> where T:new() { public static T Instance=new(); }
// The game's singleton lookup returns null once the application is quitting or the manager is destroyed.
public class GameManager : UnityEngine.Object { public static bool isCitySceneBeingUnloaded; public VehicleController? selectedVehicle; }
public enum EnergyConsumption { None,High }
public enum PlayerAction { Cancel=4 }
public class EmployeeStationController { public EnergyConsumption workEnergyConsumption=EnergyConsumption.High; }
public class VehicleInstance {
    public string id="cart"; public Action? Updated; public bool ThrowOnRemove;
    public void AddCallToOnItemsInCargoUpdated(Action a)=>Updated+=a;
    public void RemoveCallFromOnItemsInCargoUpdated(Action a) { Updated-=a; if(ThrowOnRemove) throw new Exception("listener removal failed"); }
}
public class VehicleController {
    public VehicleInstance vehicleInstance=SaveGameManager.Current.VehicleInstances[0];
    public void EnterVehicle() {
        SaveGameManager.Current.ActiveVehicleId=vehicleInstance.id;
        InstanceBehavior<UI.UIs>.Instance.playerHUD.itemPanelUI.SetVehicle(this);
    }
}
public class Game { public float Energy=100; public string? ActiveVehicleId; public List<VehicleInstance> VehicleInstances=new(){new(),new(){id="truck"},new(){id="other"}}; }
public static class SaveGameManager { public static Game Current=new(); }
public static class VehicleHelper { public static VehicleController GetVehicleController(VehicleInstance v)=>new(){vehicleInstance=v}; }
public static class TimeHelper { public static BigAmbitions.DayNightCycle.Timestamp Now()=>new(); }
namespace BigAmbitions.DayNightCycle { public class Timestamp { public int Minutes; public Timestamp AddMinutes(int m) { Minutes+=m; return this; } } }
namespace BigAmbitions.InputSystem { public static class InputActionHelper { public static void Reset(PlayerAction a){} } }
namespace Helpers {
    public static class PrefabHelper { public static object LoadItemControllerFromPrefab(string id)=>new EmployeeStationController(); }
    public static class EnergyHelper {
        public static EnergyConsumption? Work; public static bool ThrowOnRemove;
        public static void RemoveEnergySpender(string key) { if(key=="work") { Work=null; if(ThrowOnRemove) throw new Exception("energy removal failed"); } }
        public static void AddEnergySpender(string key,EnergyConsumption c)=>Work=c;
    }
}
namespace UnityEngine {
    // Unity's destroyed-object equality differs from CLR null (?. still calls a destroyed object).
    public class Object {
        public bool Destroyed; public static int InvalidCalls;
        public static bool operator ==(Object? a,Object? b) {
            bool nilA=ReferenceEquals(a,null) || a.Destroyed, nilB=ReferenceEquals(b,null) || b.Destroyed;
            return nilA || nilB ? nilA==nilB : ReferenceEquals(a,b);
        }
        public static bool operator !=(Object? a,Object? b)=>!(a==b);
        public override bool Equals(object? o)=>ReferenceEquals(this,o);
        public override int GetHashCode()=>base.GetHashCode();
        protected void RequireAlive() { if(Destroyed) { InvalidCalls++; throw new Exception("destroyed Unity object"); } }
    }
    public static class Time { public static int frameCount; public static float timeScale=1; }
    public class Transform { public Transform parent=>this; public GameObject gameObject=new(); public bool IsChildOf(Transform t)=>ReferenceEquals(this,t); }
    public class GameObject {
        public bool activeSelf=true; public Button[] Buttons=Array.Empty<Button>();
        public void SetActive(bool value)=>activeSelf=value;
        public T[] GetComponentsInChildren<T>(bool all)=>Buttons.Cast<T>().ToArray();
    }
}
namespace UnityEngine.UI {
    public class Button { public UnityEngine.GameObject gameObject=new(); public UnityEngine.Transform transform=new(); public bool interactable=true; }
    public class Toggle {
        public UnityEngine.Transform transform=new(); private bool on;
        public bool isOn { get=>on; set { on=value; InstanceBehavior<global::UI.UIs>.Instance.gameSpeed.isFastForwarding=value; } }
    }
}
namespace UI {
    public class UIs : UnityEngine.Object { public PlayerActivityUI playerActivityUI=new(); public Speed gameSpeed=new(); public Machine timeMachine=new(); public HUD playerHUD=new(); }
    public class HUD { public ItemPanel.ItemPanelUI itemPanelUI=new(); }
    public class Speed : UnityEngine.Object {
        public bool isFastForwarding,ThrowOnReset; public int Resets;
        public void Reset() { RequireAlive(); Resets++; if(ThrowOnReset) throw new Exception("speed reset failed"); }
    }
    public class Machine : UnityEngine.Object {
        public bool isRunning,ThrowOnCancel; public int Goal,Cancelled;
        public void CancelTimeMachine() { RequireAlive(); Cancelled++; if(ThrowOnCancel) throw new Exception("time-machine cancel failed"); isRunning=false; }
    }
}
namespace UI.Elements {
    public class LabelInfo { public LabelInfo(string key){} }
    public class ButtonInfo { public ButtonInfo(string name,string key,string color,Action click,PlayerAction action){} }
}
namespace UI.ItemPanel {
    public class ItemPanelUI : UnityEngine.Object {
        public Button parkButton=new(),autoParkButton=new(),sleepButton=new(),sellButton=new(),grabButton=new(),discardButton=new(),leaveButton=new(),placeButton=new(),useMopButton=new();
        public UnityEngine.Transform cargoSlotTemplate=new();
        public UnityEngine.GameObject cargoContainer=new() { Buttons=new[] {new Button(),new Button()} }; public VehicleInstance? Vehicle; public int Refreshes,Hides; public bool Visible,ThrowOnToggle;
        public void SetVehicle(VehicleController v) {
            Patches.BeforeVehicle(v);
            Vehicle=v.vehicleInstance; parkButton.gameObject.SetActive(true);
            ServingWork.ReadOnlyButtons(this); SetupCargoSlots(); Visible=true;
            Patches.AfterVehicle(this,v);
        }
        public void SetupCargoSlots() {
            Refreshes++; cargoContainer.Buttons=new[]{new Button(),new Button()};
            ServingWork.ReadOnlyCargo(this);
        }
        public int Toggles;
        // Like the native method, Toggle(false) clears GameManager.selectedVehicle without checking the singleton.
        public void Toggle(bool on) {
            Toggles++; RequireAlive(); if(ThrowOnToggle) throw new Exception("panel toggle failed");
            if(!on) InstanceBehavior<GameManager>.Instance.selectedVehicle=null;
            Visible=on; if(!on) Hides++;
        }
    }
}
namespace PlayerActivity {
    public enum PlayerActivityState { NotStarted,MovingTowardsActivity,Started,Running,Finished }
    public interface IPlayerActivity { PlayerActivityState GetState(); void Finish(); }
    public class PlayerActivityUI {
        public static bool IsPanelOpen,ThrowOnShow; public IPlayerActivity? GetCurrentActivity;
        private Toggle fastForwardToggle=new(); public Toggle NativeToggle=>fastForwardToggle;
        public static void Show(IPlayerActivity a) { if(ThrowOnShow) throw new Exception("work UI failed"); IsPanelOpen=true; InstanceBehavior<UI.UIs>.Instance.playerActivityUI.GetCurrentActivity=a; }
        public void ChangeSliderValue(float amount,bool exactMinutes) { var a=(ServingActivity)GetCurrentActivity!; a.OnSliderValueChanged((int)(a.GetCurrentSliderValue()+amount)); }
        public void ForceTimeMachine() {
            var machine=InstanceBehavior<UI.UIs>.Instance.timeMachine;
            machine.Goal=((ServingActivity)GetCurrentActivity!).GetTimeMachineGoal().Minutes;
            machine.isRunning=true; CartSelling.Skipping=true; ServingWork.SkipStarted();
        }
    }
}
namespace HarmonyLib { public static class AccessTools { public static System.Reflection.FieldInfo Field(Type t,string name)=>t.GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!; } }
namespace FoodTrucks.Core.Saves {
    public class VehicleState { public bool IsOpen=true; }
    public static class StreetSales { public static VehicleState Vehicle=new(); public static VehicleState Find(object state,string id)=>Vehicle; }
}
namespace FoodTrucks {
    public class CartServing { public string? VehicleId; public bool PlayerAt(string id)=>VehicleId==id; public void Release(string why)=>VehicleId=null; public void LateUpdate(){} }
    public static class FoodTrucksSession { public static object CurrentState=new(); }
    public static class Log { public static List<string> Errors=new(); public static void Error(string text)=>Errors.Add(text); public static void Error(Exception ex)=>Error(ex.ToString()); }
    public static class CartButton { public static void Hide(){} public static void OnSetVehicle(UI.ItemPanel.ItemPanelUI p,VehicleController v){} }
    public class CartSelling {
        public static bool Skipping,Released,CaughtOnFoot=true; public static List<double> Caught=new(); public static Action? OnPay;
        public static void PayAllHeldNow()=>OnPay?.Invoke();
        public static void CatchUpTo(double at) { Caught.Add(at); CaughtOnFoot &= string.IsNullOrEmpty(SaveGameManager.Current.ActiveVehicleId); }
        public static void CloseByPlayer(string id)=>FoodTrucks.Core.Saves.StreetSales.Vehicle.IsOpen=false;
        private CartServing cartServing=new(); private long closedAtHour=-1; public static int ClosedDays;
        private static void CloseDays()=>ClosedDays++;
        public static void UpdateWork(CartServing stance) { new CartSelling { cartServing=stance }.LateUpdate(); }
        /* LATE_UPDATE */
    }
    public static class Patches {
        public static void BeforeVehicle(VehicleController v)=>PanelVehiclePrefix(v);
        public static void AfterVehicle(UI.ItemPanel.ItemPanelUI p,VehicleController v)=>PanelVehicle(p,v);
        /* PANEL_PREFIX */
        /* PANEL_POSTFIX */
    }
    public static class HireCrew { public static double NowExact()=>100.75; public static long Now()=>100; }
    public static class HireRoutes { public static int Calls; public static void CatchUpTo(long at)=>Calls++; }
    public static class TruckWindow { public static bool ClearKeyLabels(int expected=2)=>true; public const string CloseButtonName="close"; public const PlayerAction NoKey=(PlayerAction)999; }
    internal class TestHatchActivity : ServingActivity {
        public TestHatchActivity(string id):base(id){}
        public override void Finish() { ServingWork.BeforeLeaving(VehicleId); MarkFinished(); }
        public override UI.Elements.LabelInfo GetHeadlineLabel()=>new("hatch");
        public override UI.Elements.ButtonInfo[] GetButtons()=>Array.Empty<UI.Elements.ButtonInfo>();
    }
}
'''
