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
                // No vehicles, but the bookings contact and its gigs, the commissaries with their stock and the new foods'
                // market entries still go: asked twice, as with vehicles, since it pays back stock and clears state.
                if (Time.unscaledTime > armedUntil)
                {
                    armedUntil = Time.unscaledTime + ConfirmSeconds;
                    CartSelling.Notify("foodtrucks:notification_uninstall_confirm_none");
                    return;
                }
                armedUntil = -1f;
                // Crew sales of vehicles since sold still wait for midnight: paid in now.
                try { CartSelling.PayAllCrewTakingsNow(); }
                catch (Exception ex) { Log.Error("Uninstall: crew sales: " + ex); }
                Catering.RemoveContact();
                RemoveMarketEntries(game);
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
            // The crew's sales waiting for midnight, today's too, are paid in before the mod goes.
            try { CartSelling.PayAllCrewTakingsNow(); }
            catch (Exception ex) { Log.Error("Uninstall: crew sales: " + ex); }
            HireCrew.ReleaseAll();
            RemoveMarketEntries(game);
            Catering.RemoveContact();
            try { Commissary.RemoveForUninstall(); }
            catch (Exception ex) { Log.Error("Uninstall: commissary: " + ex); }
            long refundCents = 0;
            long unpaidCents = 0;
            var removed = 0;
            long modItemCents = 0;
            foreach (var vehicle in vehicles)
            {
                var cents = RefundCents(vehicle);
                var modCents = ModItemCents(vehicle);
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
            var left = vehicles.Count - removed;
            Log.Info($"Uninstall: {removed} removed, {left} left, refunded {CartSelling.Money(refundCents)}, unpaid {CartSelling.Money(unpaidCents)}.");
            CartSelling.Notify(left == 0 && unpaidCents == 0 ? "foodtrucks:notification_uninstall_done" : "foodtrucks:notification_uninstall_partial",
                ("count", removed.ToString()), ("left", left.ToString()), ("refund", CartSelling.Money(refundCents)), ("unpaid", CartSelling.Money(unpaidCents)));
        }

        // What was paid for the mod's own items in the vehicle's cargo (nested cargo included).
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

        // The new foods' market rows and events, should the game have made any (a hype event, a pre-1.0 upgrade): once
        // the mod is gone, the game's daily market update would look their items up and throw.
        private static void RemoveMarketEntries(GameInstance game)
        {
            try
            {
                static bool Ours(string? name) => name != null && name.StartsWith(ModItems.Prefix, StringComparison.Ordinal);
                var entries = game.productMarketEntries?.RemoveAll(e => e != null && Ours(e.itemName)) ?? 0;
                var events = 0;
                if (game.marketEvents != null)
                {
                    // An event about our items only goes; one that also names the game's items just loses ours.
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
