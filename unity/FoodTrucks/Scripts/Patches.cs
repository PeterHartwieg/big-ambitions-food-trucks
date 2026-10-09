#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Buildings;
using Dialogs;
using Entities;
using Enums;
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

            // Day summary and Econoview: the carts' profit counts like a business's (sales less the stock sold).
            Patch(typeof(FinancialSummaryHelper), "CreateFinancialSummary", new[] { typeof(int) }, nameof(DaySummary));
            Patch(AccessTools.TypeByName("UI.Smartphone.Apps.EconoView.EconoViewIncomeStatementScrollerController"), "LoadSalaryRows", Type.EmptyTypes, nameof(EconoViewRow));

            // City map: a Street food line of filters (buildings that bring crowds, the player's vehicles).
            Patch(typeof(CityMapFilters), "InitializeFilters", Type.EmptyTypes, typeof(MapFilters), postfix: nameof(MapFilters.InitializePostfix));
            Patch(typeof(CityMapFilters), "ApplyFilters", Type.EmptyTypes, typeof(MapFilters), postfix: nameof(MapFilters.ApplyPostfix));
            Patch(typeof(CityMapFilters), "ApplyFilters", new[] { typeof(CityBuildingController) }, typeof(MapFilters), postfix: nameof(MapFilters.BuildingPostfix));

            // Hires: the game's employees as a vehicle's crew. Their shift hours count as worked (paid at midnight),
            // a route counts as their task, and the "unassigned" to-do stays away while they have a vehicle.
            Patch(typeof(EmployeeHelper), "RunHourly", Type.EmptyTypes, nameof(CountCrewHour), prefix: true);
            Patch(AccessTools.TypeByName("AI.Employees.NoTaskAssignedComplaint"), "ConditionToComplainMet", new[] { typeof(EmployeeInstance) }, nameof(CrewHasTask));
            Patch(AccessTools.TypeByName("AI.Employees.NoTaskAssignedComplaint"), "ComplaintHandled", new[] { typeof(EmployeeInstance) }, nameof(CrewTaskHandled));
            Patch(AccessTools.TypeByName("UI.Tasks.TasksUI"), "CreateTodoTask", null, nameof(SkipCrewUnassignedTask), prefix: true);

            // Partnerships: a partner venue's facility satisfaction gets the bonus in the game's daily update.
            Patch(typeof(BusinessHelper), "UpdateSatisfaction", new[] { typeof(BuildingRegistration) }, typeof(Partners),
                prefix: nameof(Partners.SatisfactionPrefix), postfix: nameof(Partners.SatisfactionPostfix));

            // Business school: the Market Demands door also teaches the Food Safety course the licence needs.
            Patch(typeof(EducationDoorController), "StartLearning", Type.EmptyTypes, typeof(FoodSafetyCourse),
                prefix: nameof(FoodSafetyCourse.StartLearningPrefix));

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

        private static void Patch(Type? type, string method, Type[]? args, string patch, bool prefix = false)
        {
            if (prefix) Patch(type, method, args, typeof(Patches), prefix: patch);
            else Patch(type, method, args, typeof(Patches), postfix: patch);
        }

        private static void Patch(Type? type, string method, Type[]? args, Type handlers, string? prefix = null, string? postfix = null, string? finalizer = null)
        {
            // Overloads patched apart need their arguments in the log line to tell them apart.
            var name = $"{type?.Name ?? "(type not found)"}.{method}{(args != null && args.Length > 0 ? "(" + string.Join(", ", args.Select(a => a.Name)) + ")" : "")}";
            if (type == null)
            {
                Log.Error($"Patch {name}: Not active, type not found.");
                return;
            }
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

        private static void DaySummary(int dayNumber, FinancialSummary __result)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (__result == null || state == null) return;
                // Read-only data (written by a newer mod version) can't be marked, so it only counts days
                // already marked; otherwise Econoview's row and its total would disagree.
                if (!FoodTrucksSession.CanWrite && !SalesLedger.IsInSummary(state, dayNumber)) return;
                var profit = SalesLedger.ProfitCents(state, dayNumber);
                if (profit != 0) __result.totalProfit += profit / 100f;
                if (FoodTrucksSession.CanWrite) SalesLedger.MarkInSummary(state, dayNumber);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static MethodInfo? createRow;

        // One "Food carts" row in the income statement, next to the other income, so its total still
        // adds up with the day summary's profit.
        private static void EconoViewRow(object __instance)
        {
            try
            {
                var summaries = Traverse.Create(__instance).Field("_financialSummaries").GetValue<List<FinancialSummary>>();
                if (summaries == null || summaries.Count == 0) return;
                var state = FoodTrucksSession.CurrentState;
                // Only days whose stored total includes the carts, so the statement still adds up.
                var values = summaries.Select(s => SalesLedger.IsInSummary(state, s.dayNumber)
                    ? SalesLedger.ProfitCents(state, s.dayNumber) / 100f : 0f).ToList();
                if (values.All(v => v == 0f)) return;
                createRow ??= AccessTools.Method(__instance.GetType(), "CreateRow");
                if (createRow == null) return;
                var rowType = createRow.GetParameters()[0].ParameterType;
                createRow.Invoke(__instance, new object?[]
                {
                    Enum.Parse(rowType, values[0] >= 0 ? "Success" : "Danger"), "foodtrucks:econoview_row", values, true, null, null
                });
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void CountCrewHour()
        {
            try { HireCrew.CountHour(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void CrewHasTask(EmployeeInstance employeeInstance, ref bool __result)
        {
            try { if (__result && HireCrew.IsCrew(employeeInstance)) __result = false; }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void CrewTaskHandled(EmployeeInstance employeeInstance, ref bool __result)
        {
            try { if (!__result && HireCrew.IsCrew(employeeInstance)) __result = true; }
            catch (Exception ex) { Log.Error(ex); }
        }

        // Returning false skips creating the task; callers ignore the result.
        private static bool SkipCrewUnassignedTask(TodoTaskType type, string employeeId)
        {
            try { return type != TodoTaskType.EmployeeUnassigned || !HireCrew.IsCrew(HireCrew.Find(employeeId)); }
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
