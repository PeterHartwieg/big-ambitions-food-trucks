"""Exercise the actual native-control adapter with controlled game/UI boundaries, without a game process."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class ServingAdapterTests(unittest.TestCase):
    def test_native_duration_speed_skip_sales_and_cleanup(self):
        with tempfile.TemporaryDirectory(prefix='ft-serving-adapter-') as directory:
            folder = Path(directory)
            major = subprocess.check_output(['dotnet', '--version'], text=True).split('.')[0]
            files = [ROOT / 'unity/FoodTrucks/Scripts/ServingWork.cs', ROOT / 'Core/Fleet/ServingTime.cs']
            includes = ''.join(f'<Compile Include="{escape(str(p))}" />' for p in files)
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net{major}.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>''')
            (folder / 'Program.cs').write_text(HARNESS)
            built = subprocess.run(['dotnet', 'build', str(folder / 'Harness.csproj'), '--nologo', '-v:q', '-m:1', '-nr:false',
                                    '-p:UseSharedCompilation=false'], capture_output=True, text=True, timeout=120)
            self.assertEqual(built.returncode, 0, built.stdout + built.stderr)
            ran = subprocess.run(['dotnet', str(folder / f'bin/Debug/net{major}.0/Harness.dll')],
                                 capture_output=True, text=True, timeout=30)
            self.assertEqual(ran.returncode, 0, ran.stdout + ran.stderr)
            self.assertIn('PASS: native work controls and cleanup', ran.stdout)


HARNESS = r'''
using FoodTrucks;
using PlayerActivity;
using UnityEngine.UI;
class Harness {
    static void Check(bool yes,string why) { if(!yes) throw new Exception(why); }
    static CartWorkActivity Open() {
        var activity=new CartWorkActivity("cart",()=>CartSelling.Released=true);
        PlayerActivityUI.Show(activity); activity.ChangeState(PlayerActivityState.Running); return activity;
    }
    static void Main() {
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
        Console.WriteLine("PASS: native work controls and cleanup");
    }
}
public class InstanceBehavior<T> where T:new() { public static T Instance=new(); }
public enum EnergyConsumption { None,High }
public enum PlayerAction { Cancel=4 }
public class EmployeeStationController { public EnergyConsumption workEnergyConsumption=EnergyConsumption.High; }
public class VehicleInstance {
    public string id="cart"; public Action? Updated;
    public void AddCallToOnItemsInCargoUpdated(Action a)=>Updated+=a;
    public void RemoveCallFromOnItemsInCargoUpdated(Action a)=>Updated-=a;
}
public class VehicleController { public VehicleInstance vehicleInstance=SaveGameManager.Current.VehicleInstances[0]; }
public class Game { public float Energy=100; public string? ActiveVehicleId; public List<VehicleInstance> VehicleInstances=new(){new()}; }
public static class SaveGameManager { public static Game Current=new(); }
public static class VehicleHelper { public static VehicleController GetVehicleController(VehicleInstance v)=>new(); }
public static class TimeHelper { public static BigAmbitions.DayNightCycle.Timestamp Now()=>new(); }
namespace BigAmbitions.DayNightCycle { public class Timestamp { public int Minutes; public Timestamp AddMinutes(int m) { Minutes+=m; return this; } } }
namespace BigAmbitions.InputSystem { public static class InputActionHelper { public static void Reset(PlayerAction a){} } }
namespace Helpers {
    public static class PrefabHelper { public static object LoadItemControllerFromPrefab(string id)=>new EmployeeStationController(); }
    public static class EnergyHelper {
        public static EnergyConsumption? Work;
        public static void RemoveEnergySpender(string key) { if(key=="work") Work=null; }
        public static void AddEnergySpender(string key,EnergyConsumption c)=>Work=c;
    }
}
namespace UnityEngine {
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
    public class UIs { public PlayerActivityUI playerActivityUI=new(); public Speed gameSpeed=new(); public Machine timeMachine=new(); public HUD playerHUD=new(); }
    public class HUD { public ItemPanel.ItemPanelUI itemPanelUI=new(); }
    public class Speed { public bool isFastForwarding; public int Resets; public void Reset()=>Resets++; }
    public class Machine {
        public bool isRunning; public int Goal,Cancelled;
        public void CancelTimeMachine() { Cancelled++; isRunning=false; }
    }
}
namespace UI.Elements {
    public class LabelInfo { public LabelInfo(string key){} }
    public class ButtonInfo { public ButtonInfo(string name,string key,string color,Action click,PlayerAction action){} }
}
namespace UI.ItemPanel {
    public class ItemPanelUI {
        public Button parkButton=new(),autoParkButton=new(),sleepButton=new(),sellButton=new(),grabButton=new(),discardButton=new(),leaveButton=new(),placeButton=new(),useMopButton=new();
        public UnityEngine.Transform cargoSlotTemplate=new();
        public UnityEngine.GameObject cargoContainer=new() { Buttons=new[] {new Button(),new Button()} }; public VehicleInstance? Vehicle; public int Refreshes,Hides; public bool Visible;
        public void SetVehicle(VehicleController v) { Vehicle=v.vehicleInstance; Visible=true; }
        public void SetupCargoSlots()=>Refreshes++;
        public void Toggle(bool on) { Visible=on; if(!on) Hides++; }
    }
}
namespace PlayerActivity {
    public enum PlayerActivityState { NotStarted,MovingTowardsActivity,Started,Running,Finished }
    public interface IPlayerActivity { PlayerActivityState GetState(); void Finish(); }
    public class PlayerActivityUI {
        public static bool IsPanelOpen; public IPlayerActivity? GetCurrentActivity;
        private Toggle fastForwardToggle=new(); public Toggle NativeToggle=>fastForwardToggle;
        public static void Show(IPlayerActivity a) { IsPanelOpen=true; InstanceBehavior<UI.UIs>.Instance.playerActivityUI.GetCurrentActivity=a; }
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
    public class CartServing { public string? VehicleId; public bool PlayerAt(string id)=>VehicleId==id; public void Release(string why)=>VehicleId=null; }
    public static class FoodTrucksSession { public static object CurrentState=new(); }
    public static class Log { public static void Error(string text)=>throw new Exception(text); }
    public static class CartButton { public static void Hide(){} }
    public static class CartSelling {
        public static bool Skipping,Released; public static List<double> Caught=new();
        public static void PayAllHeldNow(){} public static void CatchUpTo(double at)=>Caught.Add(at);
        public static void CloseByPlayer(string id)=>FoodTrucks.Core.Saves.StreetSales.Vehicle.IsOpen=false;
    }
    public static class HireCrew { public static double NowExact()=>100.75; }
    public static class HireRoutes { public static int Calls; public static void CatchUpTo(long at)=>Calls++; }
    public static class TruckWindow { public static bool ClearKeyLabels(int expected=2)=>true; public const string CloseButtonName="close"; public const PlayerAction NoKey=(PlayerAction)999; }
}
'''
