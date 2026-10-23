using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Commissary;
using FoodTrucks;
using Helpers;

static class Program
{
    static VehicleController Reset()
    {
        FoodTrucksSession.CurrentState = new SaveState(); FoodTrucksSession.CanWrite = true;
        SaveGameManager.Current = new Game(); Payments.Total = 0; Payments.Accept = true;
        HudConfirm.onShow = () => {}; HudConfirm.Callback = null; HudConfirm.isOpen = false;
        Commissary.Site = new Site { Id = "bay" }; CartSelling.Settle = true;
        VehicleHelper.FailDelete = false; VehicleHelper.ThrowAfterDelete = false;
        var cart = new VehicleInstance(); SaveGameManager.Current.VehicleInstances.Add(cart);
        var controller = new VehicleController { vehicleInstance = cart }; VehicleHelper.Controller = controller;
        FoodTrucksSession.CurrentState.Vehicles.Add(new VehicleState { VehicleId = cart.id, VehicleType = "cart", HasPermit = true, Crew = new() { "employee" } });
        FoodTrucksSession.CurrentState.Commissary.Spaces.Add(new CommissarySpace { VehicleId = cart.id, CommissaryId = "bay", Kind = "cart" });
        return controller;
    }
    static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
    static void Ask(VehicleController c) { CartSale.Ask(c); Check(HudConfirm.Callback != null, "confirmation missing"); }
    static void Main()
    {
        var c = Reset(); Ask(c);
        Check(SaveGameManager.Current.VehicleInstances.Count == 1 && Payments.Total == 0, "cancel/no click changed state");
        Check(!HudConfirm.AllowSkip, "destructive confirmation can be skipped");
        var click = HudConfirm.Callback!; click(); click();
        Check(Payments.Total == 82500 && SaveGameManager.Current.VehicleInstances.Count == 0, "normal sale or duplicate callback");
        Check(!FoodTrucksSession.CurrentState!.Vehicles[0].HasPermit && FoodTrucksSession.CurrentState.Commissary.Spaces.Count == 0, "business still active");
        Check(FoodTrucksSession.CurrentState.Vehicles[0].Crew.Count == 0, "crew not released");
        CartSale.Retry(FoodTrucksSession.CurrentState); Check(Payments.Total == 82500, "retry paid twice");

        c = Reset(); HudConfirm.onShow = null; CartSale.Ask(c);
        Check(HudConfirm.Callback == null && Payments.Total == 0, "UI absence autoconfirmed");
        c = Reset(); FoodTrucksSession.CanWrite = false; CartSale.Ask(c);
        Check(HudConfirm.Callback == null, "read-only quote accepted");
        c = Reset(); Ask(c); FoodTrucksSession.CanWrite = false; HudConfirm.Callback!();
        Check(Payments.Total == 0 && SaveGameManager.Current.VehicleInstances.Count == 1, "read-only confirmation accepted");
        c = Reset(); Ask(c); FoodTrucksSession.CurrentState = new SaveState(); HudConfirm.Callback!();
        Check(Payments.Total == 0, "stale save callback");
        c = Reset(); Ask(c); Commissary.Site = null; HudConfirm.Callback!();
        Check(Payments.Total == 0, "cart moved out of bay");
        c = Reset(); Ask(c); c.vehicleInstance.cargoInstances[0].amount++; HudConfirm.Callback!();
        Check(Payments.Total == 0 && SaveGameManager.Current.VehicleInstances.Count == 1, "changed quote accepted");
        c = Reset(); Ask(c); CartSelling.Settle = false; HudConfirm.Callback!();
        Check(Payments.Total == 0 && SaveGameManager.Current.VehicleInstances.Count == 1, "unpaid takings lost");
        c = Reset(); Ask(c); VehicleHelper.FailDelete = true; HudConfirm.Callback!();
        Check(Payments.Total == 0 && FoodTrucksSession.CurrentState!.CartSales.Count == 0, "failed delete paid/left claim");
        Check(FoodTrucksSession.CurrentState.Vehicles[0].Crew.Count == 1 && FoodTrucksSession.CurrentState.Commissary.Spaces.Count == 1, "failed delete closed business");
        c = Reset(); Ask(c); VehicleHelper.ThrowAfterDelete = true; HudConfirm.Callback!();
        Check(Payments.Total == 82500, "deleted vehicle not settled after listener exception");
        c = Reset(); Ask(c); Payments.Accept = false; HudConfirm.Callback!();
        Check(SaveGameManager.Current.VehicleInstances.Count == 0 && FoodTrucksSession.CurrentState!.CartSales.Count == 1, "failed payment not retained");
        var codec = new SaveCodec(); FoodTrucksSession.CurrentState = codec.Load(codec.Serialize(FoodTrucksSession.CurrentState!)).State!;
        Payments.Accept = true; CartSale.Retry(FoodTrucksSession.CurrentState); CartSale.Retry(FoodTrucksSession.CurrentState);
        Check(Payments.Total == 82500 && FoodTrucksSession.CurrentState.CartSales.Count == 0, "reload payment not exactly once");
        c = Reset(); FoodTrucksSession.CurrentState!.CartSales.Add(new FoodTrucks.Core.Fleet.CartSale { VehicleId = "cart", Name = "cart", Cents = 82500, Day = 1 });
        CartSale.Retry(FoodTrucksSession.CurrentState);
        Check(Payments.Total == 0 && FoodTrucksSession.CurrentState.CartSales.Count == 0, "interrupted owned sale paid or stuck");
        c = Reset(); c.vehicleInstance.vehicleTypeName = "truck"; Check(!CartSale.CanOffer(c.vehicleInstance), "offered truck");
        c = Reset(); c.vehicleInstance.cargoInstances.Add(new Cargo { paid = false, amount = 999 });
        Check(CartSale.Quote(c.vehicleInstance) == 82500, "unpaid inventory counted");
        c.vehicleInstance.cargoInstances[0].nestedCargoInstances.Add(new Cargo { amount = 2 });
        Check(CartSale.Quote(c.vehicleInstance) == 97500, "nested inventory omitted");
        c = Reset(); CtaManager.ctaKey = "click_to_drive"; CtaManager.ctaAction = () => {};
        OverlayButtons.CommissaryCartCtaPostfix(c);
        Check(CtaManager.ctaKey == "click_to_use" && CtaManager.ctaAction == null, "cart menu inaccessible");
        CtaManager.ctaKey = "click_to_add"; CtaManager.ctaAction = () => {};
        OverlayButtons.CommissaryCartCtaPostfix(c); Check(CtaManager.ctaAction != null, "loading overridden");
        CtaManager.ctaKey = "click_to_drive"; Commissary.Site = null;
        OverlayButtons.CommissaryCartCtaPostfix(c); Check(CtaManager.ctaAction != null, "outside bay overridden");
        c = Reset(); PlayerHelper.IsUsingVehicle = true; CtaManager.ctaKey = "click_to_drive";
        OverlayButtons.CommissaryCartCtaPostfix(c); Check(CtaManager.ctaAction != null, "pushing overridden");
        PlayerHelper.IsUsingVehicle = false; PlayerHelper.ItemInstanceInHands = new object();
        OverlayButtons.CommissaryCartCtaPostfix(c); Check(CtaManager.ctaAction != null, "held cargo overridden");
        PlayerHelper.ItemInstanceInHands = null; c.vehicleInstance.vehicleTypeName = "truck";
        OverlayButtons.CommissaryCartCtaPostfix(c); Check(CtaManager.ctaAction != null, "truck overridden");
        Console.WriteLine("PASS: cart sale cancellation, closure, quotes, stale callbacks, read-only saves, payment/deletion failures, retry and reload");
    }
}
class Game { public List<VehicleInstance> VehicleInstances = new(); public List<VehicleInstance> privateDriverVehicleInstances = new(); public string ActiveVehicleId = ""; public int Day = 1; }
static class SaveGameManager { public static Game Current = new(); }
class VehicleType { public float price = 1000; }
class Cargo { public bool paid = true; public int amount = 1; public string itemName = "food"; public List<Cargo> nestedCargoInstances = new(); public float GetSellingPrice() => (amount + nestedCargoInstances.Sum(c => c.amount)) * 100 * ItemHelper.sellingMultiplier; }
class VehicleInstance { public string id = "cart", vehicleTypeName = "cart"; public VehicleType VehicleType = new(); public bool zeroSellingPrice = false; public List<Cargo> cargoInstances = new() { new Cargo() }; public float CalculateRepairCost() => 0; }
class EntityController {}
class VehicleController : EntityController { public VehicleInstance vehicleInstance = null!; public object? vehicleCollider = null, additionalCollider = null; }
static class ItemHelper { public static float sellingMultiplier = .75f; }
static class HudConfirm { public static bool isOpen, AllowSkip; public static Action? onShow, Callback; public static void Show(object h, object b, Action callback, object? cancel, string key, object? cancelKey, bool allowConfirmationSkip, bool isNegative) { Callback = callback; AllowSkip = allowConfirmationSkip; } }
namespace UI { class UIs { public TimeMachine timeMachine = new(); } }
class TimeMachine { public bool isRunning = false; }
static class InstanceBehavior<T> where T : new() { public static T Instance = new(); }
static class TimeHelper { public static int CurrentDay = 1, CurrentHour = 8; public static float CurrentMinute = 5; }
class TransactionInfo { public TransactionInfo(string k, Dictionary<string,string> d) {} }
namespace Helpers { static class VehicleHelper { public static bool FailDelete, ThrowAfterDelete; public static VehicleController? Controller; public static VehicleController? GetVehicleController(VehicleInstance v) => Controller; public static void Delete(this VehicleInstance v, VehicleController? c) { if (FailDelete) throw new Exception("before"); SaveGameManager.Current.VehicleInstances.Remove(v); if (ThrowAfterDelete) throw new Exception("after"); } } }
namespace Localizor { static class LocalizorManager { public static object Localize(string k, object? d) => k; public static string GetLocalization(string k, object? d) => k; } }
namespace GleyTrafficSystem { static class Manager { public static void TriggerColliderRemovedEvent(object o) {} } }
namespace FoodTrucks
{
    static class FoodTrucksSession { public static SaveState? CurrentState = new(); public static bool CanWrite = true; public static void MarkChanged() {} }
    static class FoodTrucksMod { public static string KindOf(string kind) => kind; }
    class Site { public string Id = "bay"; }
    static class Commissary { public static Site? Site = new(); public static Site? SiteOf(VehicleInstance v) => Site; public static void SettleRentForSale() {} }
    static class CartSelling { public static bool Settle = true; public static void Notify(string key, params (string,string)[] data) {} public static string Money(long cents) => cents.ToString(); public static bool PayVehicleTakingsNow(VehicleInstance c) => Settle; }
    static class HireCrew { public static void SettleVehicle(string id) {} public static void Remove(VehicleState v, string id, string reason) => v.Crew.Remove(id); }
    static class StreetFoodBooks { public static Dictionary<string,string> VehicleNames() => new() { ["cart"] = "Cart 1" }; }
    static class Payments { public static bool Accept = true; public static long Total; public static bool PayIn(long cents, TransactionInfo i, int day) { if (!Accept) return false; Total += cents; return true; } }
    static class Log { public static void Info(string s) {} public static void Error(string s) {} }
    static class VehicleTodos { public static void ReconcileNow() {} }
}

static class PlayerHelper { public static bool IsUsingVehicle; public static object? ItemInstanceInHands; }
static class CtaManager { public static string ctaKey = ""; public static Action? ctaAction; }
