#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Buildings;
using Dialogs;
using Entities;
using FoodTrucks.Core.Saves;
using BigAmbitions.Items;
using HarmonyLib;
using Helpers;
using UI.ItemPanel;

namespace FoodTrucks
{
    // Harmony patches. Each one logs an Active or Not active line at startup.
    internal static class Patches
    {
        private const string HarmonyId = "foodtrucks.mod";
        private static Harmony? harmony;

        public static void Apply()
        {
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            Log.Info($"Harmony {typeof(Harmony).Assembly.GetName().Version}.");

            // Tax: street food revenue counts as business income, on the statement, the threshold and the projection.
            Patch(typeof(TaxCalculationHelper), "GetBusinessIncome", new[] { typeof(int), typeof(int) }, nameof(TaxIncomeRows));
            Patch(typeof(TaxCalculationHelper), "GetBusinessSales", new[] { typeof(int), typeof(int) }, nameof(TaxSales));
            Patch(typeof(TaxCalculationHelper), "GetBusinessIncomeTotal", new[] { typeof(int), typeof(int) }, nameof(TaxIncomeTotal));

            // Dealer: list the cart next to the vanilla stock at one dealer with a spawn point.
            Patch(typeof(VehicleContractSettings), "SetListOfVehiclesForSale", Type.EmptyTypes, nameof(DealerStock));
            // Delivery would put a hand vehicle into a warehouse slot, which nothing has tested.
            Patch(typeof(VehicleStoreDialog), "OnVehicleSettingsSet", Type.EmptyTypes, nameof(RefuseDelivery), prefix: true);

            // Item panel: the cart's Open button, hidden whenever the panel shows something else.
            Patch(typeof(ItemPanelUI), "SetVehicle", new[] { typeof(VehicleController) }, nameof(PanelVehicle));
            Patch(typeof(ItemPanelUI), "SetItemInstance", null, nameof(PanelOther));
            Patch(typeof(ItemPanelUI), "SetMiniGameMode", null, nameof(PanelOther));

            // Cargo limit: food up to the vehicle's units, equipment up to its slots.
            Patch(typeof(VehicleInstance), "MergeIntoCargo", new[] { typeof(CargoInstance) },
                typeof(CargoLimit), prefix: nameof(CargoLimit.MergePrefix), finalizer: nameof(CargoLimit.MergeFinalizer));
            Patch(typeof(VehicleInstance), "TryToAddToCargo", new[] { typeof(CargoInstance) },
                typeof(CargoLimit), prefix: nameof(CargoLimit.AddPrefix));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }

        private static void Patch(Type type, string method, Type[]? args, string patch, bool prefix = false)
        {
            if (prefix) Patch(type, method, args, typeof(Patches), prefix: patch);
            else Patch(type, method, args, typeof(Patches), postfix: patch);
        }

        private static void Patch(Type type, string method, Type[]? args, Type handlers, string? prefix = null, string? postfix = null, string? finalizer = null)
        {
            var name = $"{type.Name}.{method}";
            try
            {
                var original = AccessTools.Method(type, method, args);
                if (original == null)
                {
                    Log.Error($"Patch {name}: Not active, method not found.");
                    return;
                }
                harmony!.Patch(original,
                    prefix: prefix == null ? null : new HarmonyMethod(handlers, prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(handlers, postfix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(handlers, finalizer));
                Log.Info($"Patch {name}: Active.");
            }
            catch (Exception ex)
            {
                Log.Error($"Patch {name}: Not active, {ex.Message}");
            }
        }

        private static float RevenueFor(int firstDay, int lastDay)
        {
            return SalesLedger.SumCents(FoodTrucksSession.CurrentState, firstDay, lastDay) / 100f;
        }

        private static void TaxIncomeRows(int periodStartDay, int latestRealDay, ref List<(string, float)> __result)
        {
            try
            {
                var revenue = RevenueFor(periodStartDay, latestRealDay);
                if (revenue <= 0f) return;
                __result ??= new List<(string, float)>();
                // The tax statement prints this name as plain text.
                __result.Add(("Street food", revenue));
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void TaxSales(int periodStartDay, int latestRealDay, ref float __result)
        {
            try { __result += RevenueFor(periodStartDay, latestRealDay); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void TaxIncomeTotal(int periodStartDay, int latestRealDay, ref decimal __result)
        {
            try { __result += TaxCalculationHelper.RoundCurrency(RevenueFor(periodStartDay, latestRealDay)); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static MethodInfo? addVehicle;

        // General US Trucks, 1 7th Avenue: it has the spawn point that a vehicle without a showcase
        // model needs (ContractVehicleForSale.Purchase uses the building's first custom position).
        private const string DealerStreet = "ba:street_seventhavenue";
        private const int DealerNumber = 1;

        // Only in person: a phone order would deliver the cart into a warehouse slot.
        private static void DealerStock(VehicleContractSettings __instance)
        {
            try
            {
                var dialog = DialogController.current;
                var address = dialog?.contact?.Address;
                if (dialog == null || dialog.dialogType != DialogType.Physical || address == null) return;
                if (address.streetName != DealerStreet || address.streetNumber != DealerNumber) return;
                addVehicle ??= AccessTools.Method(typeof(VehicleContractSettings), "TryAddVehicleByVehicleType");
                if (addVehicle == null) return;
                foreach (var typeName in FoodTrucksMod.SoldAtDealer)
                {
                    if (FoodTrucksMod.RegisteredVehicleTypes.Contains(typeName))
                        addVehicle.Invoke(__instance, new object?[] { typeName, null });
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // Returning false skips the dialog step; a null entry keeps the dialog open, as the game's own errors do.
        private static bool RefuseDelivery(ref DialogEntry? __result)
        {
            try
            {
                var settings = DialogController.current?.GetInputTransform<VehicleContractSettings>(null);
                if (settings == null || !settings.isDelivery) return true;
                if (!FoodTrucksMod.IsModVehicle(settings.selectedVehicleForSale?.VehicleName)) return true;
                CartSelling.Notify("foodtrucks:notification_no_delivery");
                __result = null;
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return true;
            }
        }

        private static void PanelVehicle(ItemPanelUI __instance, VehicleController vehicle)
        {
            try { CartButton.OnSetVehicle(__instance, vehicle); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void PanelOther()
        {
            try { CartButton.Hide(); }
            catch (Exception ex) { Log.Error(ex); }
        }
    }
}
