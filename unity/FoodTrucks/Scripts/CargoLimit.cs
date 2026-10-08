#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using FoodTrucks.Core.Selling;
using UI.Notification;

namespace FoodTrucks
{
    // Caps what goes into a Food Trucks vehicle: food up to its unit limit, equipment up to its slots.
    // Every way the game loads a vehicle goes through VehicleInstance.MergeIntoCargo (tops up existing
    // stacks) or TryToAddToCargo (fills slots), so these two hooks cover them all. A partial load is
    // reported the way the game already reports a vehicle filling up: TryToAddToCargo returns false and
    // the source keeps the rest, so a box in the player's hands stays there with what didn't fit.
    internal static class CargoLimit
    {
        [ThreadStatic] private static bool inside;

        public static string? VehicleKind(VehicleInstance? vehicle) => FoodTrucksMod.KindOf(vehicle?.vehicleTypeName);

        public static IEnumerable<CargoLine> Lines(VehicleInstance vehicle) =>
            vehicle.cargoInstances.Where(c => c != null).Select(c =>
                new CargoLine(c.itemName, c.amount, c.nestedCargoInstances != null && c.nestedCargoInstances.Count > 0));

        // How many units of this stack may go in; null when the vehicle or item isn't limited.
        private static int? Allowed(VehicleInstance vehicle, CargoInstance incoming)
        {
            var tables = DemandData.Tables;
            var kind = VehicleKind(vehicle);
            if (tables == null || kind == null || incoming == null || incoming.amount <= 0) return null;
            var nested = incoming.nestedCargoInstances != null && incoming.nestedCargoInstances.Count > 0;
            var allowed = CargoRules.AllowedUnits(tables, kind, Lines(vehicle), incoming.itemName, incoming.amount, nested);
            return allowed >= incoming.amount ? (int?)null : allowed;
        }

        // Prefix: hide the part that doesn't fit from the merge; the finalizer gives it back to the source,
        // also when a cargo listener throws inside the merge. Safe because merging never keeps a reference
        // to the incoming stack. The add that follows a merge reports the cap, so this one stays quiet.
        public static void MergePrefix(VehicleInstance __instance, CargoInstance cargoInstanceToMerge, out int __state)
        {
            __state = 0;
            if (inside) return;
            try
            {
                var allowed = Allowed(__instance, cargoInstanceToMerge);
                if (allowed == null) return;
                __state = cargoInstanceToMerge.amount - allowed.Value;
                cargoInstanceToMerge.amount = allowed.Value;
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        public static Exception? MergeFinalizer(Exception? __exception, CargoInstance cargoInstanceToMerge, int __state)
        {
            if (__state > 0) cargoInstanceToMerge.amount += __state;
            return __exception;
        }

        // Prefix: when only part fits, add a copy of that part (the game may store the very object it is
        // given, and the source must keep its own), then take what went in off the source and report false.
        // Once the copy has been offered, the original never runs: whatever went in is taken off the source,
        // even if a cargo listener throws on the way.
        public static bool AddPrefix(VehicleInstance __instance, CargoInstance cargoInstance, ref bool __result)
        {
            if (inside) return true;
            int? allowed;
            try { allowed = Allowed(__instance, cargoInstance); }
            catch (Exception ex)
            {
                Log.Error(ex);
                return true;
            }
            if (allowed == null) return true;
            __result = false;
            try { Refused(__instance, cargoInstance.itemName); }
            catch (Exception ex) { Log.Error(ex); }
            if (allowed.Value <= 0) return false;

            int before;
            CargoInstance part;
            try
            {
                before = __instance.GetAmountByItemName(cargoInstance.itemName);
                part = cargoInstance.Copy();
                part.amount = allowed.Value;
                if (cargoInstance.nestedCargoInstances != null)
                    part.nestedCargoInstances = new List<NestedCargoInstance>(cargoInstance.nestedCargoInstances);
            }
            catch (Exception ex)
            {
                // Nothing has moved yet: refuse the load rather than let the full stack through.
                Log.Error(ex);
                return false;
            }
            inside = true;
            try { __instance.TryToAddToCargo(part); }
            catch (Exception ex) { Log.Error("Cargo limit: partial load threw: " + ex); }
            finally
            {
                inside = false;
                var moved = __instance.GetAmountByItemName(cargoInstance.itemName) - before;
                if (moved > 0) cargoInstance.amount -= Math.Min(moved, cargoInstance.amount);
            }
            return false;
        }

        private static void Refused(VehicleInstance vehicle, string itemName)
        {
            var tables = DemandData.Tables;
            var kind = VehicleKind(vehicle);
            var info = kind == null ? null : tables?.Vehicle(kind);
            if (info == null) return;
            var equipment = tables!.EquipmentForGameItem(itemName) != null;
            Log.Info($"Cargo limit: {itemName} into {vehicle.vehicleTypeName} {vehicle.id} capped ({(equipment ? $"{info.EquipmentSlots} equipment slots" : $"{info.StockUnits} units of food")}).");
            Notifications.Show(NotificationType.Warning,
                equipment ? "foodtrucks:notification_equipment_full" : "foodtrucks:notification_stock_full",
                new Dictionary<string, string> { { "units", info.StockUnits.ToString() }, { "slots", info.EquipmentSlots.ToString() } },
                4f, "foodtrucks_cargo_limit", notificationSound: false, trackOnSaveGame: false);
        }
    }
}
