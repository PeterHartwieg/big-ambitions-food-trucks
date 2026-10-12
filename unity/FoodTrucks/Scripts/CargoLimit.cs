#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using FoodTrucks.Core.Selling;
using Localizor;
using UI.Notification;

namespace FoodTrucks
{
    // Caps what goes into a Food Trucks vehicle: food up to the room left in the fitted piece that holds it
    // (Core's Storage: storage per piece), equipment up to its slots and one Extra Storage.
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

        // How much of this stack may go in, and why not all; null when the vehicle or item isn't limited.
        private static LoadCheck? Allowed(VehicleInstance vehicle, CargoInstance incoming)
        {
            var tables = DemandData.Tables;
            var kind = VehicleKind(vehicle);
            if (tables == null || kind == null || incoming == null || incoming.amount <= 0) return null;
            var nested = incoming.nestedCargoInstances != null && incoming.nestedCargoInstances.Count > 0;
            var check = Storage.Check(tables, kind, Lines(vehicle), incoming.itemName, incoming.amount, nested);
            return check.Allowed >= incoming.amount ? (LoadCheck?)null : check;
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
                __state = cargoInstanceToMerge.amount - allowed.Value.Allowed;
                cargoInstanceToMerge.amount = allowed.Value.Allowed;
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
            LoadCheck? allowed;
            try { allowed = Allowed(__instance, cargoInstance); }
            catch (Exception ex)
            {
                Log.Error(ex);
                return true;
            }
            if (allowed == null) return true;
            __result = false;
            try { Refused(__instance, cargoInstance.itemName, allowed.Value); }
            catch (Exception ex) { Log.Error(ex); }
            if (allowed.Value.Allowed <= 0) return false;

            int before;
            CargoInstance part;
            try
            {
                before = __instance.GetAmountByItemName(cargoInstance.itemName);
                part = cargoInstance.Copy();
                part.amount = allowed.Value.Allowed;
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

        // Prefix on ICargoHolder.TryToMergeAndMoveCargoBetweenHolders (a hand truck or a box in hand unloaded into a
        // vehicle). The game moves the stacks last-first, so food loaded onto a hand truck after its grill would reach
        // the vehicle before the grill and be refused for want of it. Into a food vehicle, the equipment that fits goes
        // first, each stack moved the way the game moves it; the original then moves the rest.
        public static void MoveEquipmentFirstPrefix(ICargoHolder fromHolder, ICargoHolder toHolder, out bool __state)
        {
            __state = false;
            try
            {
                var tables = DemandData.Tables;
                if (tables == null || !(toHolder is VehicleInstance vehicle) || VehicleKind(vehicle) == null || fromHolder == null) return;
                var cargo = fromHolder.GetCargoInstances();
                if (cargo == null) return;
                var equipment = cargo.Where(c => c != null && tables.EquipmentForGameItem(c.itemName) != null).ToList();
                // Food after equipment only matters when both are in the load.
                if (equipment.Count == 0 || equipment.Count == cargo.Count(c => c != null)) return;
                for (var i = equipment.Count - 1; i >= 0; i--)
                {
                    var stack = equipment[i];
                    // A piece that won't fit whole is left to the original, so its refusal shows once.
                    if (Allowed(vehicle, stack) != null) continue;
                    var amount = stack.amount;
                    toHolder.MergeIntoCargo(stack);
                    if (stack.amount <= 0 || toHolder.TryToAddToCargo(stack))
                    {
                        __state = true;
                        fromHolder.RemoveFromCargo(stack);
                    }
                    else if (amount > stack.amount)
                    {
                        __state = true;
                        fromHolder.OnItemsInCargoUpdated()?.Invoke();
                    }
                }
            }
            catch (Exception ex) { Log.Error("Cargo limit: equipment-first unload: " + ex); }
        }

        // Something moved in the prefix counts as a move, as it would have inside the original.
        public static void MoveEquipmentFirstPostfix(ref bool __result, bool __state)
        {
            if (__state) __result = true;
        }

        private static void Refused(VehicleInstance vehicle, string itemName, LoadCheck check)
        {
            var tables = DemandData.Tables;
            var kind = VehicleKind(vehicle);
            var info = kind == null ? null : tables?.Vehicle(kind);
            if (info == null) return;
            var piece = check.EquipmentId == null ? null : tables!.Equipment(check.EquipmentId);
            var pieceName = piece == null ? "" : StoragePanel.PieceName(vehicle, piece);
            string key, why;
            switch (check.Refusal)
            {
                case LoadRefusal.EquipmentSlots:
                    key = "foodtrucks:notification_equipment_full";
                    why = $"{info.EquipmentSlots} equipment slots";
                    break;
                case LoadRefusal.SecondStorage:
                    key = "foodtrucks:notification_storage_once";
                    why = "one Extra Storage counts";
                    break;
                case LoadRefusal.NoPiece:
                    key = piece == null ? "foodtrucks:notification_stock_nopiece_any" : "foodtrucks:notification_stock_nopiece";
                    why = $"no fitted {check.EquipmentId ?? "piece"} holds it";
                    break;
                default:
                    key = "foodtrucks:notification_stock_full";
                    why = $"{check.EquipmentId} holds {check.Capacity} units";
                    break;
            }
            Log.Info($"Cargo limit: {itemName} into {vehicle.vehicleTypeName} {vehicle.id} capped at {check.Allowed} ({why}).");
            Notices.Show(NotificationType.Warning, key,
                new Dictionary<string, string>
                {
                    { "units", check.Capacity.ToString() }, { "slots", info.EquipmentSlots.ToString() },
                    { "piece", pieceName }, { "food", LocalizorManager.GetLocalization(itemName, null) },
                    { "vehicle", LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null) }
                },
                4f, "foodtrucks_cargo_limit", notificationSound: false, trackOnSaveGame: false);
        }
    }
}
