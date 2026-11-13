#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using Helpers;
using Localizor;
using UnityEngine;

namespace FoodTrucks
{
    internal static class Uninstall
    {
        private const float ConfirmSeconds = 10f;
        private static float armedUntil = -1f;

        public static void Request()
        {
            var game = SaveGameManager.Current;
            if (game == null) return;
            var vehicles = ModVehicles(game);
            var deliveries = PendingDeliveries(game);
            if (vehicles.Count == 0 && deliveries == 0)
            {
                if (Time.unscaledTime > armedUntil)
                {
                    armedUntil = Time.unscaledTime + ConfirmSeconds;
                    CartSelling.Notify("foodtrucks:notification_uninstall_confirm_none");
                    return;
                }
                armedUntil = -1f;
                if (!SettleTakings()) return;
                Catering.RemoveContact();
                RemoveMarketEntries(game);
                try { Commissary.RemoveForUninstall(); }
                catch (Exception ex) { Log.Error("Uninstall: commissary: " + ex); }
                var campaignsLeft = EndFleetCampaigns();
                try { VehicleTodos.RemoveAll("uninstall"); }
                catch (Exception ex) { Log.Error("Uninstall: to-dos: " + ex); }
                if (campaignsLeft != 0) return;
                var stillOwned = ModVehicles(game).Count;
                if (stillOwned == 0 && PendingDeliveries(game) == 0) CartSelling.Notify("foodtrucks:notification_uninstall_none");
                else CartSelling.Notify("foodtrucks:notification_uninstall_partial",
                    ("count", "0"), ("left", stillOwned.ToString()), ("refund", CartSelling.Money(0)), ("unpaid", CartSelling.Money(0)));
                return;
            }
            if (vehicles.Any(v => v.id == game.ActiveVehicleId))
            {
                CartSelling.Notify("foodtrucks:notification_uninstall_release");
                return;
            }
            if (Time.unscaledTime > armedUntil)
            {
                armedUntil = Time.unscaledTime + ConfirmSeconds;
                CartSelling.Notify("foodtrucks:notification_uninstall_confirm",
                    ("count", vehicles.Count.ToString()), ("deliveries", deliveries.ToString()));
                return;
            }
            armedUntil = -1f;
            CancelDeliveries(game);
            Run(game, vehicles);
        }

        private static void Run(GameInstance game, List<VehicleInstance> vehicles)
        {
            TruckWindow.StepOutNow("uninstall");
            try { HireCrew.SettleAll(); }
            catch (Exception ex) { Log.Error("Uninstall: settling crew hours: " + ex); }
            CartSelling.CloseAllQuietly();
            if (!SettleTakings()) return;
            HireCrew.ReleaseAll();
            RemoveMarketEntries(game);
            Catering.RemoveContact();
            try { Commissary.RemoveForUninstall(); }
            catch (Exception ex) { Log.Error("Uninstall: commissary: " + ex); }
            var campaignsLeft = EndFleetCampaigns();
            try { VehicleTodos.RemoveAll("uninstall"); }
            catch (Exception ex) { Log.Error("Uninstall: to-dos: " + ex); }
            long refundCents = 0;
            long unpaidCents = 0;
            var removed = 0;
            long modItemCents = 0;
            foreach (var vehicle in vehicles)
            {
                var cents = RefundCents(vehicle);
                var modCents = ModItemCents(vehicle);
                var name = vehicle.VehicleType != null ? LocalizorManager.GetLocalization(vehicle.vehicleTypeName, null) : vehicle.vehicleTypeName;
                if (!RemoveEverywhere(game, vehicle.id))
                {
                    Log.Error($"Uninstall: {vehicle.vehicleTypeName} {vehicle.id} is still in the save; not refunded.");
                    continue;
                }
                removed++;
                if (cents > 0)
                {
                    var info = new TransactionInfo("ba:transaction_itemsold", new Dictionary<string, string> { { "itemSoldInfo", name } });
                    if (Payments.PayIn(cents, info))
                    {
                        refundCents += cents;
                        modItemCents += modCents;
                    }
                    else
                    {
                        unpaidCents += cents;
                        Log.Error($"Uninstall: {vehicle.id} removed but its refund of {CartSelling.Money(cents)} was not paid.");
                    }
                }
                Log.Info($"Uninstall: removed {vehicle.vehicleTypeName} {vehicle.id}, refund {CartSelling.Money(cents)}.");
            }
            // The mod's own items can only have come from a commissary, whose orders were deductible: paid back, they
            // come off this tax year's commissary deduction too. The game's own foods ordered there can't be told from
            // wholesaler stock, so their deduction stays (accepted).
            if (modItemCents > 0)
                Log.Info($"Uninstall: {CartSelling.Money(Commissary.ReverseDeduction(modItemCents))} of this tax year's commissary deduction taken back for the mod's items refunded with the vehicles.");
            FoodTrucksSession.MarkChanged();
            var left = ModVehicles(game).Count;
            var deliveriesLeft = PendingDeliveries(game);
            if (deliveriesLeft > 0) Log.Error($"Uninstall: {deliveriesLeft} vehicle deliveries are still pending.");
            Log.Info($"Uninstall: {removed} removed, {left} left, refunded {CartSelling.Money(refundCents)}, unpaid {CartSelling.Money(unpaidCents)}.");
            CartSelling.Notify(left == 0 && deliveriesLeft == 0 && unpaidCents == 0 && campaignsLeft == 0 ? "foodtrucks:notification_uninstall_done" : "foodtrucks:notification_uninstall_partial",
                ("count", removed.ToString()), ("left", left.ToString()), ("refund", CartSelling.Money(refundCents)), ("unpaid", CartSelling.Money(unpaidCents)));
        }

        private static List<VehicleInstance> ModVehicles(GameInstance game) =>
            Owned(game).Where(v => FoodTrucksMod.IsModVehicle(v.vehicleTypeName))
                .GroupBy(v => string.IsNullOrEmpty(v.id) ? null : v.id, StringComparer.Ordinal)
                .SelectMany(g => g.Key == null ? g : g.Take(1))
                .ToList();

        private static IEnumerable<VehicleInstance> Owned(GameInstance game) =>
            (game.VehicleInstances ?? Enumerable.Empty<VehicleInstance>())
                .Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>())
                .Where(v => v != null);

        private static bool RemoveEverywhere(GameInstance game, string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            bool Ours(VehicleInstance? v) => v != null && v.id == id && FoodTrucksMod.IsModVehicle(v.vehicleTypeName);
            foreach (var vehicle in (game.VehicleInstances ?? new List<VehicleInstance>()).Where(Ours).ToList())
            {
                if (!game.VehicleInstances!.Contains(vehicle)) continue;
                try
                {
                    var controller = VehicleHelper.GetVehicleController(vehicle);
                    if (controller != null)
                    {
                        try
                        {
                            if (controller.vehicleCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.vehicleCollider);
                            if (controller.additionalCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.additionalCollider);
                        }
                        catch (Exception ex) { Log.Error($"Uninstall: collider event for {id}: {ex}"); }
                    }
                    vehicle.Delete(controller);
                }
                catch (Exception ex) { Log.Error($"Uninstall: removing {id} threw: {ex}"); }
            }
            if (game.VehicleInstances != null && game.VehicleInstances.Any(v => v != null && v.id == id)) return false;
            try
            {
                var stored = game.privateDriverVehicleInstances?.RemoveAll(Ours) ?? 0;
                if (stored > 0) Log.Info($"Uninstall: took {id} out of the private driver's garage.");
            }
            catch (Exception ex) { Log.Error($"Uninstall: removing {id} from the private driver's garage threw: {ex}"); }
            return !Owned(game).Any(v => v.id == id);
        }

        private static bool SettleTakings()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state != null && state.CartSales.Count > 0)
            {
                CartSale.Retry(state);
                if (state.CartSales.Count > 0)
                {
                    CartSelling.Notify("foodtrucks:cart_sale_uninstall_pending");
                    return false;
                }
            }
            try { if (CartSelling.PayAllTakingsNow()) return true; }
            catch (Exception ex) { Log.Error("Uninstall: window sales: " + ex); }
            var newer = FoodTrucksSession.CurrentState == null && FoodTrucksSession.LastLoadStatus == FoodTrucks.Core.Saves.LoadStatus.NewerVersion;
            Log.Error(newer
                ? "Uninstall: the save was written by a newer Food Trucks version; cleanup stopped. Uninstall with that version."
                : "Uninstall: unsettled window sales remain; cleanup stopped. Run the uninstall step again.");
            CartSelling.Notify(newer ? "foodtrucks:notification_uninstall_newer_version" : "foodtrucks:notification_uninstall_sales_failed");
            return false;
        }

        private static int EndFleetCampaigns()
        {
            try { return FleetRecruiting.EndForUninstall().Left; }
            catch (Exception ex)
            {
                Log.Error("Uninstall: fleet recruitment campaigns: " + ex);
                CartSelling.Notify("foodtrucks:notification_uninstall_fleet_campaigns_failed");
                return 1;
            }
        }

        private static long ModItemCents(VehicleInstance vehicle)
        {
            double total = 0;
            foreach (var entry in vehicle.cargoInstances)
            {
                if (entry == null || !entry.paid) continue;
                if (IsModItem(entry.itemName)) total += (double)entry.amount * entry.pricePerUnit;
                if (entry.nestedCargoInstances == null) continue;
                foreach (var nested in entry.nestedCargoInstances)
                    if (nested != null && IsModItem(nested.itemName)) total += (double)nested.amount * nested.pricePerUnit;
            }
            return (long)Math.Round(total * 100);
        }

        private static bool IsModItem(string? itemName) => itemName != null && itemName.StartsWith(ModItems.Prefix, StringComparison.Ordinal);

        private static long RefundCents(VehicleInstance vehicle)
        {
            double total = vehicle.VehicleType?.price ?? 0f;
            foreach (var entry in vehicle.cargoInstances)
            {
                if (entry == null || !entry.paid) continue;
                total += (double)entry.amount * entry.pricePerUnit;
                if (entry.nestedCargoInstances == null) continue;
                foreach (var nested in entry.nestedCargoInstances)
                {
                    if (nested != null) total += (double)nested.amount * nested.pricePerUnit;
                }
            }
            return (long)Math.Round(total * 100);
        }

        private static void RemoveMarketEntries(GameInstance game)
        {
            try
            {
                static bool Ours(string? name) => name != null && name.StartsWith(ModItems.Prefix, StringComparison.Ordinal);
                var entries = game.productMarketEntries?.RemoveAll(e => e != null && Ours(e.itemName)) ?? 0;
                var events = 0;
                if (game.marketEvents != null)
                {
                    var emptied = new HashSet<MarketEvent>();
                    foreach (var marketEvent in game.marketEvents.Where(e => e?.itemNames != null))
                        if (marketEvent.itemNames.RemoveAll(Ours) > 0 && marketEvent.itemNames.Count == 0) emptied.Add(marketEvent);
                    events = game.marketEvents.RemoveAll(e => e != null && (Ours(e.itemName) || emptied.Contains(e)));
                }
                if (entries > 0 || events > 0) FoodTrucksSession.MarkChanged();
                Log.Info($"Uninstall: {entries} market entries and {events} market events for the mod's foods removed.");
            }
            catch (Exception ex) { Log.Error("Uninstall: market entries: " + ex); }
        }

        private static int PendingDeliveries(GameInstance game) =>
            game.vehicleDeliveryContracts?.Count(c => c != null && FoodTrucksMod.IsModVehicle(c.vehicleTypeName)) ?? 0;

        private static int CancelDeliveries(GameInstance game)
        {
            var cancelled = game.vehicleDeliveryContracts?.RemoveAll(c => c != null && FoodTrucksMod.IsModVehicle(c.vehicleTypeName)) ?? 0;
            if (cancelled == 0) return 0;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Uninstall: cancelled {cancelled} pending vehicle deliveries.");
            return cancelled;
        }
    }
}
