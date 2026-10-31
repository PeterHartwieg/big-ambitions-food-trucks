// Controlled stand-ins for the game boundaries Uninstall.cs touches. The native Delete is modelled on the
// game's VehicleHelper.Delete: it removes from VehicleInstances only, never from the private driver's list.
using FoodTrucks.Core.Saves;

namespace UnityEngine { static class Time { public static float unscaledTime = 1000f; } }

namespace Entities
{
    class VehicleType { public float price = 15000f; }
    class Cargo
    {
        public bool paid = true; public int amount = 2; public float pricePerUnit = 10f; public string? itemName = "food";
        public List<Cargo>? nestedCargoInstances;
        public Cargo() { nestedCargoInstances = new() { new Cargo(1, 5f) }; }
        public Cargo(int amount, float price) { this.amount = amount; pricePerUnit = price; }
    }
    class VehicleInstance
    {
        public string? id; public string vehicleTypeName; public VehicleType? VehicleType = new(); public List<Cargo> cargoInstances = new() { new Cargo() };
        public VehicleInstance(string? id, string type) { this.id = id; vehicleTypeName = type; }
    }
    class VehicleDeliveryContract { public string vehicleTypeName = ""; }
    class ProductMarketEntry { public string? itemName { get; set; } }
    class MarketEvent { public string? itemName { get; set; } public List<string> itemNames = new(); }
    class GameInstance
    {
        public List<VehicleInstance> VehicleInstances = new();
        public List<VehicleInstance>? privateDriverVehicleInstances = new();
        public string ActiveVehicleId = "";
        public List<VehicleDeliveryContract>? vehicleDeliveryContracts = new();
        public List<ProductMarketEntry>? productMarketEntries = new();
        public List<MarketEvent>? marketEvents = new();
    }
    static class SaveGameManager { public static GameInstance? Current = new(); }
    class TransactionInfo { public TransactionInfo(string key, Dictionary<string, string> data) {} }
    class VehicleController { public VehicleInstance vehicleInstance = null!; public object? vehicleCollider = new(), additionalCollider = null; }
}

namespace Helpers
{
    using Entities;
    static class VehicleHelper
    {
        public enum Fault { None, Throw, Keep }
        public static Fault DeleteFault;
        public static int Deletes, NullControllerDeletes;
        // Only a vehicle out in the world has a controller; one in the private driver's garage has none.
        public static VehicleController? GetVehicleController(VehicleInstance v) =>
            SaveGameManager.Current!.VehicleInstances.Contains(v) ? new VehicleController { vehicleInstance = v } : null;
        public static void Delete(this VehicleInstance v, VehicleController? c)
        {
            Deletes++;
            if (c == null) NullControllerDeletes++;
            if (DeleteFault == Fault.Throw) throw new Exception("native delete failed");
            if (DeleteFault == Fault.Keep) return;
            SaveGameManager.Current!.VehicleInstances.Remove(v);
        }
    }
}

namespace Localizor { static class LocalizorManager { public static string GetLocalization(string key, object? data) => key; } }
namespace GleyTrafficSystem { static class Manager { public static void TriggerColliderRemovedEvent(object o) {} } }

namespace FoodTrucks
{
    using Entities;
    static class FoodTrucksMod
    {
        internal const string VehicleTypePrefix = "foodtrucks:vehicletype_";
        internal static bool IsModVehicle(string? typeName) => typeName != null && typeName.StartsWith(VehicleTypePrefix, StringComparison.Ordinal);
    }
    static class ModItems { internal const string Prefix = "foodtrucks:itemname_"; }
    static class FoodTrucksSession
    {
        public static SaveState? CurrentState = new();
        public static LoadStatus LastLoadStatus = LoadStatus.Loaded;
        public static void MarkChanged() {}
    }
    static class CartSelling
    {
        public static bool Settle = true;
        public static List<string> Notices = new();
        public static void Notify(string key, params (string, string)[] data) => Notices.Add(key + string.Concat(data.Select(d => $" {d.Item1}={d.Item2}")));
        public static string Money(long cents) => cents.ToString();
        public static void CloseAllQuietly() {}
        public static bool PayAllTakingsNow() => Settle;
    }
    static class CartSale { public static void Retry(SaveState state) {} }
    static class Catering { public static void RemoveContact() {} }
    static class Commissary { public static long Reversed; public static void RemoveForUninstall() {} public static long ReverseDeduction(long cents) { Reversed += cents; return cents; } }
    class CampaignResult { public int Left { get; set; } }
    static class FleetRecruiting { public static CampaignResult EndForUninstall() => new(); }
    static class VehicleTodos { public static void RemoveAll(string reason) {} }
    static class TruckWindow { public static void StepOutNow(string reason) {} }
    static class HireCrew { public static int Released; public static void SettleAll() {} public static void ReleaseAll() => Released++; }
    static class Payments
    {
        public static bool Accept = true;
        public static List<long> Paid = new();
        public static bool PayIn(long cents, TransactionInfo info) { if (!Accept) return false; Paid.Add(cents); return true; }
    }
    static class Log { public static void Info(string s) {} public static void Error(string s) {} }
}
