#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Localizor;
using UnityEngine;

namespace FoodTrucks
{
    // The safe uninstall step: removes every Food Trucks vehicle from the save and refunds it.
    // A save that still holds a mod vehicle breaks once the mod is gone (it loads somewhere else
    // without world interaction if the cart was held, and the game's forced tax payment throws on
    // the unknown vehicle type). The game can't sell hand vehicles, so the mod refunds them itself.
    // Runs only when asked twice within ConfirmSeconds.
    internal static class Uninstall
    {
        private const float ConfirmSeconds = 10f;
        private static float armedUntil = -1f;

        public static void Request()
        {
            var game = SaveGameManager.Current;
            if (game == null) return;
            var vehicles = game.VehicleInstances.Where(v => FoodTrucksMod.IsModVehicle(v.vehicleTypeName)).ToList();
            var deliveries = PendingDeliveries(game);
            if (vehicles.Count == 0 && deliveries == 0)
            {
                // Nothing to take back, but the bookings contact and its gigs, and the commissaries with their stock, still go.
                Catering.RemoveContact();
                try { Commissary.RemoveForUninstall(); }
                catch (Exception ex) { Log.Error("Uninstall: commissary: " + ex); }
                CartSelling.Notify("foodtrucks:notification_uninstall_none");
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

        // Removes first and refunds only a vehicle that is really gone, so a retry after a failure
        // can never refund the same vehicle twice.
        private static void Run(GameInstance game, List<VehicleInstance> vehicles)
        {
            // Out of the truck's hatch before the truck goes.
            TruckWindow.StepOutNow("uninstall");
            // Hours the crew earned at windows the player opened go on the game's counters before anything closes.
            try { HireCrew.SettleAll(); }
            catch (Exception ex) { Log.Error("Uninstall: settling crew hours: " + ex); }
            CartSelling.CloseAllQuietly();
            HireCrew.ReleaseAll();
            Catering.RemoveContact();
            try { Commissary.RemoveForUninstall(); }
            catch (Exception ex) { Log.Error("Uninstall: commissary: " + ex); }
            long refundCents = 0;
            long unpaidCents = 0;
            var removed = 0;
            foreach (var vehicle in vehicles)
            {
                var cents = RefundCents(vehicle);
                // A vanilla label, so the history still reads well after the mod is removed.
                var name = vehicle.VehicleType != null ? LocalizorManager.GetLocalization(vehicle.vehicleTypeName, null) : vehicle.vehicleTypeName;
                try
                {
                    var controller = VehicleHelper.GetVehicleController(vehicle);
                    // As the game's own vehicle sale does, so traffic forgets the collider.
                    if (controller != null)
                    {
                        try
                        {
                            if (controller.vehicleCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.vehicleCollider);
                            if (controller.additionalCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.additionalCollider);
                        }
                        catch (Exception ex) { Log.Error($"Uninstall: collider event for {vehicle.id}: {ex}"); }
                    }
                    vehicle.Delete(controller);
                }
                catch (Exception ex) { Log.Error($"Uninstall: removing {vehicle.id} threw: {ex}"); }

                if (game.VehicleInstances.Contains(vehicle))
                {
                    Log.Error($"Uninstall: {vehicle.vehicleTypeName} {vehicle.id} is still in the save; not refunded.");
                    continue;
                }
                removed++;
                if (cents > 0)
                {
                    var info = new TransactionInfo("ba:transaction_itemsold", new Dictionary<string, string> { { "itemSoldInfo", name } });
                    if (Payments.PayIn(cents, info)) refundCents += cents;
                    else
                    {
                        unpaidCents += cents;
                        Log.Error($"Uninstall: {vehicle.id} removed but its refund of {CartSelling.Money(cents)} was not paid.");
                    }
                }
                Log.Info($"Uninstall: removed {vehicle.vehicleTypeName} {vehicle.id}, refund {CartSelling.Money(cents)}.");
            }
            FoodTrucksSession.MarkChanged();
            var left = vehicles.Count - removed;
            Log.Info($"Uninstall: {removed} removed, {left} left, refunded {CartSelling.Money(refundCents)}, unpaid {CartSelling.Money(unpaidCents)}.");
            CartSelling.Notify(left == 0 && unpaidCents == 0 ? "foodtrucks:notification_uninstall_done" : "foodtrucks:notification_uninstall_partial",
                ("count", removed.ToString()), ("left", left.ToString()), ("refund", CartSelling.Money(refundCents)), ("unpaid", CartSelling.Money(unpaidCents)));
        }

        // The purchase price, plus what was paid for the stock still in it, including goods packed
        // inside a carried item (nested cargo).
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

        private static int PendingDeliveries(GameInstance game) =>
            game.vehicleDeliveryContracts?.Count(c => c != null && FoodTrucksMod.IsModVehicle(c.vehicleTypeName)) ?? 0;

        // Orders made before delivery was refused would bring a vehicle back after the cleanup.
        // Delivery is paid on arrival, so a cancelled order owes nothing.
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
