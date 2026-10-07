#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Buildings;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;

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

            // Dealers: list the mod's vehicles next to the vanilla stock.
            Patch(typeof(VehicleContractSettings), "SetListOfVehiclesForSale", Type.EmptyTypes, nameof(DealerStock));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }

        private static void Patch(Type type, string method, Type[] args, string postfix)
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
                harmony!.Patch(original, postfix: new HarmonyMethod(typeof(Patches), postfix));
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

        // Spike: every vehicle dealer lists the mod's vehicles. Milestone 1 picks one dealer.
        private static void DealerStock(VehicleContractSettings __instance)
        {
            try
            {
                addVehicle ??= AccessTools.Method(typeof(VehicleContractSettings), "TryAddVehicleByVehicleType");
                if (addVehicle == null) return;
                foreach (var typeName in FoodTrucksMod.RegisteredVehicleTypes)
                    addVehicle.Invoke(__instance, new object?[] { typeName, null });
            }
            catch (Exception ex) { Log.Error(ex); }
        }
    }
}
