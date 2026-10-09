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
using UI.Smartphone.Apps.BizMan;

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

            // Day summary and Econoview: the street food profit counts like a business's, and Econoview breaks it
            // down per vehicle. Crew wages and parking are noted per vehicle as the game charges them, and move
            // from the game's own Econoview rows to the vehicles.
            var scroller = AccessTools.TypeByName(StreetFoodBooks.ScrollerTypeName);
            Patch(typeof(FinancialSummaryHelper), "CreateFinancialSummary", new[] { typeof(int) }, typeof(StreetFoodBooks), postfix: nameof(StreetFoodBooks.DaySummary));
            Patch(scroller, "LoadSalaryRows", Type.EmptyTypes, typeof(StreetFoodBooks), postfix: nameof(StreetFoodBooks.IncomeRows));
            Patch(scroller, "LoadOngoingExpenseRows", Type.EmptyTypes, typeof(StreetFoodBooks), postfix: nameof(StreetFoodBooks.OngoingExpenseRows));
            Patch(scroller, "LoadFeeRows", Type.EmptyTypes, typeof(StreetFoodBooks), postfix: nameof(StreetFoodBooks.FeeRows));
            Patch(typeof(EmployeeInstance), "PayWage", new[] { typeof(BuildingRegistration) }, typeof(StreetFoodBooks),
                prefix: nameof(StreetFoodBooks.WagePrefix), postfix: nameof(StreetFoodBooks.WagePostfix));
            Patch(typeof(ParkingSimulator), "RunDaily", Type.EmptyTypes, typeof(StreetFoodBooks),
                prefix: nameof(StreetFoodBooks.ParkingDailyPrefix), postfix: nameof(StreetFoodBooks.ParkingDailyPostfix));
            Patch(typeof(ParkingSimulator), "RunHourly", Type.EmptyTypes, typeof(StreetFoodBooks),
                prefix: nameof(StreetFoodBooks.ParkingHourlyPrefix), postfix: nameof(StreetFoodBooks.ParkingHourlyPostfix));
            StreetFoodBooks.CheckMoves();

            // BizMan: a Street food line at the end of the business list that opens a fleet section. The line has no
            // building registration, so the filter, the cell and the click are patched to keep it away from code
            // that needs one; it only shows when all four patches are active (BizManFleet.CheckReady).
            Patch(typeof(BusinessScrollerController), "PopulateAllModels", null, typeof(BizManFleet), postfix: nameof(BizManFleet.PopulatePostfix));
            Patch(typeof(BizManFilterToggle), "PassesFilter", null, typeof(BizManFleet), prefix: nameof(BizManFleet.FilterPrefix));
            Patch(typeof(BusinessCellView), "SetData", null, typeof(BizManFleet), postfix: nameof(BizManFleet.CellPostfix));
            Patch(typeof(BizManList), "OpenBusiness", null, typeof(BizManFleet), prefix: nameof(BizManFleet.OpenPrefix));
            Patch(typeof(BizManList), "OnDisable", Type.EmptyTypes, typeof(BizManFleet), postfix: nameof(BizManFleet.ListClosedPostfix));
            BizManFleet.CheckReady();

            // City map: a Street food line of filters (buildings that bring crowds, the player's vehicles).
            Patch(typeof(CityMapFilters), "InitializeFilters", Type.EmptyTypes, typeof(MapFilters), postfix: nameof(MapFilters.InitializePostfix));
            Patch(typeof(CityMapFilters), "ApplyFilters", Type.EmptyTypes, typeof(MapFilters), postfix: nameof(MapFilters.ApplyPostfix));
            Patch(typeof(CityMapFilters), "ApplyFilters", new[] { typeof(CityBuildingController) }, typeof(MapFilters), postfix: nameof(MapFilters.BuildingPostfix));

            // Hires: the game's employees as a vehicle's crew. Their shift hours count as worked (paid at midnight),
            // a route counts as their task, and the "unassigned" to-do stays away while they have a vehicle.
            Patch(typeof(EmployeeHelper), "RunHourly", Type.EmptyTypes, typeof(Patches), prefix: nameof(CountCrewHour), postfix: nameof(AfterGameCount));
            Patch(typeof(EmployeeHelper), "PayDailyWages", Type.EmptyTypes, nameof(CloseCrewDay), prefix: true);
            // A change of employment settles what the mod owes a hire on the spot, before the game's own payout: a building
            // or warehouse job (the game clears the old shifts first), a replacement, resigning, fired or retired.
            Patch(typeof(EmployeeHelper), "UnassignEmployeeFromAllWorkshifts", new[] { typeof(EmployeeInstance) }, nameof(SettleOnShiftsCleared), prefix: true);
            Patch(typeof(EmployeeInstance), "ResignBeforeReplacement", Type.EmptyTypes, nameof(SettleOnReplacement), prefix: true);
            Patch(typeof(EmployeeInstance), "Resign", Type.EmptyTypes, nameof(SettleOnResign), prefix: true);
            Patch(typeof(EmployeeInstance), "RemoveEmployee", Type.EmptyTypes, nameof(SettleOnRemove), prefix: true);
            // While the game's tick runs its hourly and daily events, a settle catches the sales up only to the hour being
            // processed (the clock's minute already holds the rest of a time skip's advance).
            Patch(typeof(GameManager), "RunMainGameTick", new[] { typeof(float) }, typeof(Patches),
                prefix: nameof(EnterGameTick), finalizer: nameof(LeaveGameTick));
            Patch(AccessTools.TypeByName("AI.Employees.NoTaskAssignedComplaint"), "ConditionToComplainMet", new[] { typeof(EmployeeInstance) }, nameof(CrewHasTask));
            Patch(AccessTools.TypeByName("AI.Employees.NoTaskAssignedComplaint"), "ComplaintHandled", new[] { typeof(EmployeeInstance) }, nameof(CrewTaskHandled));
            Patch(AccessTools.TypeByName("UI.Tasks.TasksUI"), "CreateTodoTask", null, nameof(SkipCrewUnassignedTask), prefix: true);

            // Partnerships: a partner venue's facility satisfaction gets the bonus in the game's daily update.
            Patch(typeof(BusinessHelper), "UpdateSatisfaction", new[] { typeof(BuildingRegistration) }, typeof(Partners),
                prefix: nameof(Partners.SatisfactionPrefix), postfix: nameof(Partners.SatisfactionPostfix));

            // Business school: the Market Demands door also teaches the Food Safety course the licence needs.
            Patch(typeof(EducationDoorController), "StartLearning", Type.EmptyTypes, typeof(FoodSafetyCourse),
                prefix: nameof(FoodSafetyCourse.StartLearningPrefix));

            // Parking lanes: parked cars generated on a cart are cleared, as for the game's own vehicles.
            Patch(typeof(VehicleHelper), "DestroyBlockingVehicles",
                new[] { typeof(UnityEngine.GameObject), typeof(Vehicles.VehicleTypes.VehicleType), typeof(UnityEngine.Transform), typeof(bool) },
                typeof(CartParts), prefix: nameof(CartParts.DestroyBlockingPrefix));

            // Truck window: a save made at the hatch puts the player outside by the truck, not inside its body.
            Patch(typeof(PlayerHelper), "SaveCurrentPosition", Type.EmptyTypes, typeof(TruckWindow), postfix: nameof(TruckWindow.SavePositionPostfix));

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

        private static void CountCrewHour()
        {
            try { HireCrew.CountHour(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void EnterGameTick() => HireCrew.InGameTick++;

        private static Exception? LeaveGameTick(Exception? __exception)
        {
            if (HireCrew.InGameTick > 0) HireCrew.InGameTick--;
            return __exception;
        }

        private static void SettleOnShiftsCleared(EmployeeInstance employeeInstance) => SettleLeaving(employeeInstance, "shifts cleared");
        private static void SettleOnReplacement(EmployeeInstance __instance) => SettleLeaving(__instance, "set to be replaced");
        private static void SettleOnResign(EmployeeInstance __instance) => SettleLeaving(__instance, "resigning");
        private static void SettleOnRemove(EmployeeInstance __instance) => SettleLeaving(__instance, "leaving");

        private static void SettleLeaving(EmployeeInstance? employee, string why)
        {
            try { if (employee != null) HireCrew.SettleLeaving(employee, why); }
            catch (Exception ex) { Log.Error(ex); }
        }

        // The day's last crew hours, into the closing day, before the game pays wages at midnight.
        private static void CloseCrewDay()
        {
            try { HireCrew.BeforePayroll(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        // The hours the game counted itself this hour (building shifts), so a crew's serving doesn't count them again.
        private static void AfterGameCount()
        {
            try { HireCrew.AfterGameCount(); }
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
