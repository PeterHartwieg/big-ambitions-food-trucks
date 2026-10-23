#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using UI;

namespace FoodTrucks
{
    // Business closure at a commissary: one cart and its cargo, at the native resale multiplier.
    // The game's VehicleInstance.GetSellingPrice returns zero for hand vehicles, including their cargo.
    internal static class CartSale
    {
        private static bool selling;

        internal static bool CanOffer(VehicleInstance? cart) => cart != null &&
            FoodTrucksMod.KindOf(cart.vehicleTypeName) == "cart" && Commissary.SiteOf(cart) != null;

        private static string? Refusal(VehicleInstance cart, SaveState? expected = null, string? siteId = null)
        {
            var state = FoodTrucksSession.CurrentState;
            var game = SaveGameManager.Current;
            if (state == null || !FoodTrucksSession.CanWrite) return "foodtrucks:notification_data_readonly";
            if (expected != null && !ReferenceEquals(state, expected)) return "foodtrucks:cart_sale_changed";
            if (game == null || !game.VehicleInstances.Contains(cart) || FoodTrucksMod.KindOf(cart.vehicleTypeName) != "cart" ||
                state.CartSales.Any(s => s.VehicleId == cart.id)) return "foodtrucks:cart_sale_changed";
            var record = state.Vehicles.FirstOrDefault(v => v.VehicleId == cart.id);
            if (!string.IsNullOrEmpty(game.ActiveVehicleId) || record?.IsOpen == true || record?.Transit != null ||
                InstanceBehavior<UIs>.Instance?.timeMachine?.isRunning == true) return "foodtrucks:cart_sale_close_first";
            var site = Commissary.SiteOf(cart);
            if (site == null || (siteId != null && site.Id != siteId)) return "foodtrucks:cart_sale_at_commissary";
            return null;
        }

        internal static long Quote(VehicleInstance cart) => CartSales.Quote(cart.VehicleType.price, ItemHelper.sellingMultiplier,
            cart.CalculateRepairCost(), cart.cargoInstances.Where(c => c != null && c.paid).Select(c => (double)c.GetSellingPrice()),
            cart.zeroSellingPrice);

        internal static void Ask(VehicleController controller)
        {
            if (selling || controller == null || HudConfirm.isOpen) return;
            var cart = controller.vehicleInstance;
            var refusal = Refusal(cart);
            if (refusal != null) { CartSelling.Notify(refusal); return; }
            // HudConfirm otherwise auto-confirms when its UI is absent. Never sell without a visible confirmation.
            if (HudConfirm.onShow == null) { CartSelling.Notify("foodtrucks:cart_sale_unavailable"); return; }
            var state = FoodTrucksSession.CurrentState!;
            var siteId = Commissary.SiteOf(cart)!.Id;
            var cents = Quote(cart);
            var name = StreetFoodBooks.VehicleNames().TryGetValue(cart.id, out var named)
                ? named : LocalizorManager.GetLocalization(cart.vehicleTypeName, null);
            var used = false;
            HudConfirm.Show(LocalizorManager.Localize("foodtrucks:cart_sale_title", (object?)null),
                LocalizorManager.Localize("foodtrucks:cart_sale_confirm", new { vehicle = name, price = CartSelling.Money(cents) }),
                () =>
                {
                    if (used) return;
                    used = true;
                    Confirm(cart, state, siteId, name, cents);
                }, null, "foodtrucks:overlay_sell_cart", null, allowConfirmationSkip: false, isNegative: true);
        }

        private static void Confirm(VehicleInstance cart, SaveState state, string siteId, string name, long cents)
        {
            if (selling) return;
            selling = true;
            try
            {
                var refusal = Refusal(cart, state, siteId);
                if (refusal != null) { CartSelling.Notify(refusal); return; }
                if (Quote(cart) != cents) { CartSelling.Notify("foodtrucks:cart_sale_changed"); return; }
                HireCrew.SettleVehicle(cart.id);
                if (!CartSelling.PayVehicleTakingsNow(cart))
                {
                    CartSelling.Notify("foodtrucks:cart_sale_takings_pending");
                    return;
                }
                // Catch-up/held orders must never change the quoted inventory silently.
                if (Quote(cart) != cents) { CartSelling.Notify("foodtrucks:cart_sale_changed"); return; }
                Commissary.SettleRentForSale();
                var game = SaveGameManager.Current;
                var sale = new Core.Fleet.CartSale
                {
                    VehicleId = cart.id, Name = name, Cents = cents, Day = game.Day,
                    Minute = HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60 + (int)TimeHelper.CurrentMinute)
                };
                // Remember the claim before deletion; payment is only allowed once native ownership is gone.
                state.CartSales.Add(sale);
                FoodTrucksSession.MarkChanged();
                try
                {
                    var controller = VehicleHelper.GetVehicleController(cart);
                    if (controller != null)
                    {
                        if (controller.vehicleCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.vehicleCollider);
                        if (controller.additionalCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.additionalCollider);
                    }
                    cart.Delete(controller);
                }
                catch (Exception ex) { Log.Error("Cart sale: removing " + cart.id + ": " + ex); }
                if (game.VehicleInstances.Contains(cart))
                {
                    state.CartSales.Remove(sale);
                    FoodTrucksSession.MarkChanged();
                    CartSelling.Notify("foodtrucks:cart_sale_failed");
                    return;
                }
                foreach (var cargo in cart.cargoInstances.Where(c => c != null && c.paid))
                {
                    Commissaries.LeftVehicle(state.Commissary, cargo.itemName, cargo.amount);
                    foreach (var nested in cargo.nestedCargoInstances)
                        Commissaries.LeftVehicle(state.Commissary, nested.itemName, nested.amount);
                }
                Complete(state, sale);
                CartSelling.Notify(state.CartSales.Contains(sale) ? "foodtrucks:cart_sale_payment_pending" : "foodtrucks:cart_sale_done",
                    ("vehicle", name), ("price", CartSelling.Money(cents)));
            }
            catch (Exception ex)
            {
                Log.Error("Cart sale: " + ex);
                CartSelling.Notify("foodtrucks:cart_sale_interrupted");
            }
            finally { selling = false; }
        }

        // Also runs after reload. A failed native payout stays saved, and a still-owned cart never pays out.
        internal static void Retry(SaveState state)
        {
            if (selling || !FoodTrucksSession.CanWrite) return;
            selling = true;
            try
            {
                foreach (var sale in state.CartSales.ToList())
                {
                    if (SaveGameManager.Current.VehicleInstances.Concat(SaveGameManager.Current.privateDriverVehicleInstances ??
                        Enumerable.Empty<VehicleInstance>()).Any(v => v.id == sale.VehicleId))
                    {
                        // A reload captured an interrupted deletion. No proceeds were paid while owned; discard its quote.
                        state.CartSales.Remove(sale);
                        FoodTrucksSession.MarkChanged();
                        continue;
                    }
                    Complete(state, sale);
                }
            }
            finally { selling = false; }
        }

        private static void Complete(SaveState state, Core.Fleet.CartSale sale)
        {
            var record = state.Vehicles.FirstOrDefault(v => v.VehicleId == sale.VehicleId);
            if (record != null)
                foreach (var employee in record.Crew.ToList()) HireCrew.Remove(record, employee, "cart sold");
            CartSales.Close(state, sale.VehicleId, sale.Minute);
            FoodTrucksSession.MarkChanged();
            var info = new TransactionInfo("ba:transaction_itemsold", new Dictionary<string, string> { { "itemSoldInfo", sale.Name } });
            if (sale.Cents == 0 || Payments.PayIn(sale.Cents, info, sale.Day))
            {
                state.CartSales.Remove(sale);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Cart sale: {sale.VehicleId}, cart and inventory paid {CartSelling.Money(sale.Cents)}; business closed.");
            }
            VehicleTodos.ReconcileNow();
        }
    }
}
