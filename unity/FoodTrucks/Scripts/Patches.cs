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
        // Methods left patched by Remove until the game exits: ones Harmony can't restore (a static method on an interface,
        // ICargoHolder) and the dropdown guard. A later Apply in the same run (the game reloads mods when the mod list
        // changes) doesn't patch them twice.
        private static readonly HashSet<MethodBase> keptPatched = new HashSet<MethodBase>();

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

            // Vehicle cargo panel: commissary stock while driving/pushing; serving owns a read-only view.
            Patch(typeof(ItemPanelUI), "SetVehicle", new[] { typeof(VehicleController) }, typeof(Patches),
                prefix: nameof(PanelVehiclePrefix), postfix: nameof(PanelVehicle));
            Patch(typeof(ItemPanelUI), "SetItemInstance", null, nameof(PanelOther));
            Patch(typeof(ItemPanelUI), "SetMiniGameMode", null, nameof(PanelOther));

            // Outdoor serving keeps the native activity toggle, time machine and vehicle cargo HUD.
            Patch(typeof(PlayerActivity.PlayerActivityUI), "SetUpSpeedOptions", Type.EmptyTypes, typeof(ServingWork), postfix: nameof(ServingWork.SpeedOptionsPostfix));
            Patch(typeof(PlayerActivity.PlayerActivityUI), "OnPlayerActionPressed", new[] { typeof(PlayerAction) }, typeof(ServingWork), prefix: nameof(ServingWork.ActionPrefix));
            Patch(typeof(GameManager), "RunMainGameTick", new[] { typeof(float) }, typeof(ServingWork), prefix: nameof(ServingWork.TickPrefix), postfix: nameof(ServingWork.TickPostfix));
            Patch(typeof(ItemPanelUI), "UpdateButtonsVisibility", Type.EmptyTypes, typeof(ServingWork), postfix: nameof(ServingWork.ReadOnlyButtons));
            Patch(typeof(ItemPanelUI), "SetupCargoSlots", Type.EmptyTypes, typeof(ServingWork), postfix: nameof(ServingWork.ReadOnlyCargo));
            Patch(typeof(VehicleController), "DriveVehicle", Type.EmptyTypes, typeof(ServingWork), prefix: nameof(ServingWork.DrivingPrefix));

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
            // Midnight parking: rent covers food vehicles in their rented commissary bay, before the ledger notes costs.
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

            Patch(typeof(LogoCustomizer), "Refresh", Type.EmptyTypes, typeof(TruckLogoEditor), prefix: nameof(TruckLogoEditor.RefreshPrefix));
            Patch(typeof(LogoCustomizer), "UpdateSaveButton", Type.EmptyTypes, typeof(TruckLogoEditor), prefix: nameof(TruckLogoEditor.UpdateSavePrefix));
            Patch(typeof(LogoCustomizer), "SaveLogo", Type.EmptyTypes, typeof(TruckLogoEditor), prefix: nameof(TruckLogoEditor.SavePrefix));
            TruckLogoEditor.CheckReady();
            Patch(typeof(BusinessLogoGenerator), "SetUpSettings", new[] { typeof(string), typeof(LogoSettings) }, typeof(TruckLogos), prefix: nameof(TruckLogos.GeneratorPrefix));

            // A vehicle's crew shifts on the game's own Schedule tab, on an in-memory stand-in registration (VehicleSchedule).
            // Every patch here does nothing unless that stand-in is on the business page.
            var schedule = typeof(UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper);
            Patch(typeof(BizManBusiness), "RefreshData", Type.EmptyTypes, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.RefreshPrefix));
            Patch(typeof(BizManBusiness), "LoadAlerts", Type.EmptyTypes, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.AlertsPrefix));
            Patch(typeof(BizManBusiness), "OnDisable", Type.EmptyTypes, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.BusinessClosedPrefix));
            Patch(typeof(UI.Smartphone.Apps.BizMan.Schedule.BizManSchedule), "OnDisable", Type.EmptyTypes, typeof(VehicleSchedule),
                prefix: nameof(VehicleSchedule.ScheduleClosedPrefix));
            Patch(schedule, "FetchEmployees", new[] { typeof(Address) }, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.EmployeesPrefix));
            Patch(schedule, "FetchWorkstations", Type.EmptyTypes, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.WorkstationsPrefix));
            Patch(schedule, "UpdateEmployeeAfterWorkShiftChange", new[] { typeof(EmployeeInstance), typeof(bool) }, typeof(VehicleSchedule),
                prefix: nameof(VehicleSchedule.EmployeeChangedPrefix));
            Patch(schedule, "UpdateEmployeesAfterWorkShiftChange", new[] { typeof(List<EmployeeInstance>) }, typeof(VehicleSchedule),
                prefix: nameof(VehicleSchedule.EmployeeChangedPrefix));
            Patch(typeof(EmployeeInstance), "UpdateWeeklyHoursAndDays", new[] { typeof(List<ScheduleDay>) }, typeof(VehicleSchedule),
                postfix: nameof(VehicleSchedule.WeeklyHoursPostfix));
            // A dropdown with a selected option past its list shows the placeholder instead of throwing (the quit's localization reload).
            Patch(typeof(UI.Elements.Dropdown), "UpdateSelectedOptionText", Type.EmptyTypes, nameof(StaleDropdownPrefix), prefix: true);
            Patch(typeof(ScheduleAutoFillerHelper), "AutoFillSchedule", null, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.AutoFillPrefix));
            Patch(typeof(UI.Smartphone.Apps.BizMan.Schedule.ScheduleDaySelectionController), "UpdateHrManagerAvailability", new[] { typeof(bool) },
                typeof(VehicleSchedule), postfix: nameof(VehicleSchedule.AutoAvailablePostfix));
            Patch(typeof(UI.Smartphone.Apps.BizMan.Schedule.ScheduleHourToggle), "OnPointerDown", new[] { typeof(UnityEngine.EventSystems.PointerEventData) },
                typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.HourTogglePrefix));
            Patch(typeof(UI.Smartphone.Apps.BizMan.Schedule.ScheduleDayButton), "OnOpenToggleChange", new[] { typeof(bool) }, typeof(VehicleSchedule),
                prefix: nameof(VehicleSchedule.DayTogglePrefix));
            Patch(typeof(UI.Smartphone.Apps.BizMan.Schedule.ScheduleScrollerController), "LoadStation", new[] { typeof(ItemInstance) }, typeof(VehicleSchedule),
                postfix: nameof(VehicleSchedule.StationPostfix));
            Patch(typeof(BizManBusiness), "SetAddress", new[] { typeof(Address) }, typeof(VehicleSchedule), prefix: nameof(VehicleSchedule.SetAddressPrefix));
            VehicleSchedule.CheckReady();

            // City map pick: "Add as a stop" in a building's card while the BizMan vehicle page's map pick is on.
            Patch(typeof(UI.InGameUI.BuildingResume), "SetButtons", Type.EmptyTypes, typeof(StopPicker), postfix: nameof(StopPicker.SetButtonsPostfix));
            StopPicker.CheckReady();

            // City map: a Street food line of filters (buildings that bring crowds, the player's vehicles).
            Patch(typeof(CityMapFilters), "InitializeFilters", Type.EmptyTypes, typeof(MapFilters), postfix: nameof(MapFilters.InitializePostfix));
            Patch(typeof(CityMapFilters), "ApplyFilters", Type.EmptyTypes, typeof(MapFilters), postfix: nameof(MapFilters.ApplyPostfix));
            Patch(typeof(CityMapFilters), "ApplyFilters", new[] { typeof(CityBuildingController) }, typeof(MapFilters), postfix: nameof(MapFilters.BuildingPostfix));

            // Hires: the game's employees as a vehicle's crew. Their shift hours count as worked (paid at midnight),
            // a route counts as their task, and the "unassigned" to-do stays away while they have a vehicle.
            Patch(typeof(EmployeeHelper), "RunHourly", Type.EmptyTypes, typeof(Patches), prefix: nameof(CountCrewHour), postfix: nameof(AfterGameCount));
            Patch(typeof(EmployeeHelper), "PayDailyWages", Type.EmptyTypes, nameof(CloseCrewDay), prefix: true);
            // Window takings: player and crew sales are paid together where the game pays its shops' day (BusinessHelper.RunDaily
            // in GameManager.NewDay), before the day summary, taxes and the midnight bank balance.
            Patch(typeof(BusinessHelper), "RunDaily", Type.EmptyTypes, nameof(SettleSalesTakings), prefix: true);
            // Flush customers while cargo exists; the next sales tick refunds takings once deletion succeeded.
            Patch(typeof(VehicleHelper), "Delete", new[] { typeof(VehicleInstance), typeof(VehicleController) }, typeof(Patches),
                prefix: nameof(VehicleRemoving));
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

            // Vehicle warnings as the game's to-dos (VehicleTodos): text, click and destination for our to-do types, the game's
            // completion check kept off them (it throws on a type it doesn't know), and none of ours in a save file. Made only
            // when the text, check, click and save patches are all active.
            var tasksUi = typeof(UI.Tasks.TasksUI);
            Patch(tasksUi, "GetTodoDescription", new[] { typeof(TodoTask), typeof(bool) }, typeof(VehicleTodos), prefix: nameof(VehicleTodos.DescriptionPrefix));
            Patch(tasksUi, "CheckForCompletedTodoTasks", Type.EmptyTypes, typeof(VehicleTodos),
                prefix: nameof(VehicleTodos.CompletionCheckPrefix), finalizer: nameof(VehicleTodos.CompletionCheckFinalizer));
            Patch(tasksUi, "ClickTask", new[] { typeof(TodoTask) }, typeof(VehicleTodos), prefix: nameof(VehicleTodos.ClickPrefix));
            Patch(tasksUi, "AddTodoTaskToUI", new[] { typeof(TodoTask), typeof(bool) }, typeof(VehicleTodos), postfix: nameof(VehicleTodos.AddToUiPostfix));
            Patch(typeof(SaveGameSerializationHelper), "SerializeJsonData", new[] { typeof(string), typeof(GameInstance) }, typeof(VehicleTodos),
                prefix: nameof(VehicleTodos.SerializePrefix), finalizer: nameof(VehicleTodos.SerializeFinalizer));
            Patch(typeof(SaveGameSerializationHelper), "SerializeBinaryData", new[] { typeof(string), typeof(GameInstance), typeof(bool) }, typeof(VehicleTodos),
                prefix: nameof(VehicleTodos.SerializePrefix), finalizer: nameof(VehicleTodos.SerializeFinalizer));
            Patch(typeof(SaveGameManager), "InitializeLoadedSave", Type.EmptyTypes, typeof(VehicleTodos), postfix: nameof(VehicleTodos.LoadedPostfix));
            VehicleTodos.CheckReady();

            // Save data: the block each save was loaded with goes back into a save that would go out without it, and a
            // city up without the mod's city part (the game's mod loader skipped it) gets the session late (SaveGuard).
            Patch(typeof(SaveGameManager), "InitializeLoadedSave", Type.EmptyTypes, typeof(SaveGuard), postfix: nameof(SaveGuard.LoadedPostfix));
            Patch(typeof(SaveGameSerializationHelper), "SerializeJsonData", new[] { typeof(string), typeof(GameInstance) }, typeof(SaveGuard),
                prefix: nameof(SaveGuard.SerializePrefix));
            Patch(typeof(SaveGameSerializationHelper), "SerializeBinaryData", new[] { typeof(string), typeof(GameInstance), typeof(bool) }, typeof(SaveGuard),
                prefix: nameof(SaveGuard.SerializePrefix));
            Patch(typeof(GlobalEvents), "InvokeOnGameLoaded", Type.EmptyTypes, typeof(SaveGuard), postfix: nameof(SaveGuard.GameLoadedPostfix));
            Patch(typeof(BigAmbitions.ModsInternal.ModDiscoveryRegistry), "DiscoverAllModsAsync", new[] { typeof(Action<float>) }, typeof(SaveGuard),
                prefix: nameof(SaveGuard.DiscoveryPrefix));

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

            // Commissary bays: parked cars the lanes put into a bay go back to their pool (BayParking).
            Patch(typeof(ParkingLaneGenerator), "GenerateParkedVehicles", new[] { typeof(int), typeof(bool) },
                typeof(BayParking), prefix: nameof(BayParking.GeneratingPrefix), postfix: nameof(BayParking.GeneratedPostfix));

            // Truck window: a save made at the hatch puts the player outside by the truck, not inside its body.
            Patch(typeof(PlayerHelper), "SaveCurrentPosition", Type.EmptyTypes, typeof(TruckWindow), postfix: nameof(TruckWindow.SavePositionPostfix));
            // The food vehicle's on-foot menu: Open for business, Back to the hatch, Close and Clean, next to the game's Enter vehicle.
            Patch(typeof(Player.HUD.ItemInfoOverlays.VehicleOverlay), "UpdateOverlay", new[] { typeof(EntityController) },
                typeof(OverlayButtons), postfix: nameof(OverlayButtons.UpdateOverlayPostfix));
            // On foot, a click on a cart (or an empty truck) opens that menu instead of pushing or entering it at once.
            Patch(typeof(Player.HUD.ItemInfoOverlays.VehicleCtaBehavior), "GetCta", new[] { typeof(EntityController) },
                typeof(OverlayButtons), postfix: nameof(OverlayButtons.CtaPostfix));

            Patch(typeof(Player.HUD.ItemInfoOverlays.CtaManager), "UpdateCta", new[] { typeof(EntityController) },
                typeof(OverlayButtons), postfix: nameof(OverlayButtons.CommissaryCartCtaPostfix));

            // Commissary orders: the game's phone order form (as Square Appliances shows it) in a commissary's conversation,
            // made the commissary's: its address and delivery time, boxes at wholesale, Core's fee and total. Only while
            // the form shows for a commissary contact; the order form is used only when all six are active.
            var form = typeof(UI.Dialog.DeliveryContractSettingsBase);
            var store = typeof(UI.Dialog.FurnitureDeliveryContractSettings);
            Patch(form, "Start", Type.EmptyTypes, typeof(CommissaryOrderForm), postfix: nameof(CommissaryOrderForm.StartPostfix));
            Patch(form, "SetUpInventoryList", Type.EmptyTypes, typeof(CommissaryOrderForm), postfix: nameof(CommissaryOrderForm.InventoryPostfix));
            Patch(form, "AddItemToList", new[] { typeof(string) }, typeof(CommissaryOrderForm), postfix: nameof(CommissaryOrderForm.AddItemPostfix));
            Patch(form, "UpdateTotalPrice", Type.EmptyTypes, typeof(CommissaryOrderForm), postfix: nameof(CommissaryOrderForm.TotalPostfix));
            Patch(store, "get_MaxContractAmount", Type.EmptyTypes, typeof(CommissaryOrderForm), postfix: nameof(CommissaryOrderForm.MaxAmountPostfix));
            Patch(store, "UpdateItemsListTitle", Type.EmptyTypes, typeof(CommissaryOrderForm), postfix: nameof(CommissaryOrderForm.TitlePostfix));
            CommissaryOrderForm.CheckReady();

            // Logistics plans: a commissary with a rented space is a destination of the game's warehouse plans, filled from
            // the plan's warehouse at its 02:00 run (LogisticsCommissary). Offered only when all four are active.
            var planUi = typeof(UI.Smartphone.Apps.BizMan.LogisticsManagers.LogisticsManagerPlanUI);
            Patch(typeof(Buildings.Office.Headquarters.LogisticsManagerPlan), "DeliverDestination", new[] { typeof(LogisticsManagerPlanDestination) },
                typeof(LogisticsCommissary), prefix: nameof(LogisticsCommissary.DeliverPrefix));
            Patch(planUi, "GetListOfAvailableProducts", new[] { typeof(LogisticsManagerPlanDestination) }, typeof(LogisticsCommissary),
                prefix: nameof(LogisticsCommissary.ProductsPrefix));
            Patch(planUi, "IsProductInUse", new[] { typeof(Address), typeof(string) }, typeof(LogisticsCommissary), prefix: nameof(LogisticsCommissary.InUsePrefix));
            Patch(typeof(UI.Smartphone.Apps.BizMan.LogisticsManagers.LogisticsManagerDestinationUI), "SetUp", null, typeof(LogisticsCommissary),
                postfix: nameof(LogisticsCommissary.SetUpPostfix));
            LogisticsCommissary.CheckReady();

            // The recruitment agency recruits for the street food fleet without a building business (FleetRecruiting).
            Patch(typeof(Dialogs.RecruitmentAgencyDialog), "NoBusinesses", Type.EmptyTypes, typeof(FleetRecruiting), prefix: nameof(FleetRecruiting.NoBusinessesPrefix));
            Patch(typeof(Dialogs.RecruitmentAgencyDialog), "OnRecruitmentSettingsSet", Type.EmptyTypes, typeof(FleetRecruiting),
                prefix: nameof(FleetRecruiting.SettingsSetPrefix), postfix: nameof(FleetRecruiting.SettingsSetPostfix));
            Patch(typeof(UI.Dialog.RecruitmentSettings), "OnEnable", Type.EmptyTypes, typeof(FleetRecruiting), postfix: nameof(FleetRecruiting.SettingsEnablePostfix));
            Patch(typeof(UI.Dialog.RecruitmentSettings), "SelectBusiness", new[] { typeof(int) }, typeof(FleetRecruiting), prefix: nameof(FleetRecruiting.SelectBusinessPrefix));
            Patch(typeof(UI.Dialog.RecruitmentCampaignsList), "SetUpContract", new[] { typeof(RecruitmentCampaign) }, typeof(FleetRecruiting),
                prefix: nameof(FleetRecruiting.ContractPrefix));

            // The mod's own contacts (Street Food Bookings, the commissaries) have no delete button and survive "Delete all".
            Patch(typeof(UI.Smartphone.Apps.Contacts.ContactScrollerController), "LoadContact", null, typeof(ModContacts),
                postfix: nameof(ModContacts.LoadContactPostfix));
            Patch(typeof(ContactsHelper), "GetPermanentIds", Type.EmptyTypes, typeof(ModContacts), postfix: nameof(ModContacts.PermanentIdsPostfix));

            // Storage per piece in the vehicle's cargo slots: each piece's "62/80" and a tooltip with its foods.
            Patch(typeof(UI.PlayerHUD.CargoItemUi), "SetUp", new[] { typeof(CargoItem), typeof(ICargoHolder), typeof(bool) },
                typeof(StoragePanel), postfix: nameof(StoragePanel.CargoSlotPostfix));
            // Its sell question reads the amount label: "Sell Hotdog Grill", not "Sell (62/80) Hotdog Grill".
            Patch(typeof(UI.PlayerHUD.CargoItemUi), "OnSellClick", new[] { typeof(CargoItem), typeof(CargoInstance) },
                typeof(StoragePanel), prefix: nameof(StoragePanel.SellPrefix), finalizer: nameof(StoragePanel.SellFinalizer));

            // Cargo limit: food up to the room in the fitted piece that holds it, equipment up to its slots.
            Patch(typeof(VehicleInstance), "MergeIntoCargo", new[] { typeof(CargoInstance) },
                typeof(CargoLimit), prefix: nameof(CargoLimit.MergePrefix), finalizer: nameof(CargoLimit.MergeFinalizer));
            Patch(typeof(VehicleInstance), "TryToAddToCargo", new[] { typeof(CargoInstance) },
                typeof(CargoLimit), prefix: nameof(CargoLimit.AddPrefix));
            // A hand truck or box unloaded into a food vehicle: its equipment goes in before its food.
            Patch(typeof(ICargoHolder), "TryToMergeAndMoveCargoBetweenHolders", new[] { typeof(ICargoHolder), typeof(ICargoHolder) },
                typeof(CargoLimit), prefix: nameof(CargoLimit.MoveEquipmentFirstPrefix), postfix: nameof(CargoLimit.MoveEquipmentFirstPostfix));
            // A food vehicle's cargo change (a crew sale, say) plays the game's box sound only with the player near it.
            Patch(typeof(VehicleController), "OnItemAddedToCargo", Type.EmptyTypes, typeof(CargoLimit), prefix: nameof(CargoLimit.BoxSoundPrefix));
        }

        // The test bridge's patches, with -foodtrucks-test only, so the game is untouched otherwise. run_until: a prefix on
        // the game's tick, first, so the mod's own tick prefix sees the clamped step. save: the real outcome of the game's
        // serialisation and compression for the bridge's save.
        public static void ApplyTestBridge()
        {
            PatchBridge(typeof(GameManager), "RunMainGameTick", new[] { typeof(float) }, "run_until",
                prefix: nameof(TestBridgeCommands.ClampTickPrefix), first: true);
            var helper = typeof(SaveGameSerializationHelper);
            PatchBridge(helper, "SerializeJsonData", new[] { typeof(string), typeof(GameInstance) }, "save",
                finalizer: nameof(TestBridgeCommands.SerializeFinalizer));
            PatchBridge(helper, "SerializeBinaryData", new[] { typeof(string), typeof(GameInstance), typeof(bool) }, "save",
                finalizer: nameof(TestBridgeCommands.SerializeFinalizer));
            PatchBridge(helper, "CompressBinaryData", new[] { typeof(string), typeof(string) }, "save",
                finalizer: nameof(TestBridgeCommands.CompressFinalizer));
            PatchBridge(typeof(UI.Load.LoadingScreen), "StartLoading", null, "load",
                postfix: nameof(TestBridgeCommands.StartLoadingPostfix));
            // Every collision of a food vehicle that plays NWH's crash clip, logged (TestBridgeCollisions).
            var nwhVehicle = AccessTools.TypeByName("NWH.VehiclePhysics2.VehicleController");
            if (nwhVehicle == null) Log.Error("Patch VehicleController.OnCollisionEnter, test bridge collisions: Not active, type not found.");
            else PatchBridge(nwhVehicle, "OnCollisionEnter", new[] { typeof(UnityEngine.Collision) }, "collisions",
                postfix: nameof(TestBridgeCommands.VehicleCollisionPostfix));
            TestBridgeCommands.ReportLoadCheck();
            CrewProbeHooks.Install(harmony!);
        }

        // args null: the method's only overload, named without its parameters.
        private static void PatchBridge(Type type, string method, Type[]? args, string what, string? prefix = null, string? finalizer = null,
            bool first = false, string? postfix = null)
        {
            var name = args == null ? $"{type.Name}.{method}, test bridge {what}" : $"{type.Name}.{method}({string.Join(", ", args.Select(a => a.Name))}), test bridge {what}";
            if (harmony == null)
            {
                Log.Error($"Patch {name}: Not active, Harmony isn't set up.");
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
                if (keptPatched.Contains(original))
                {
                    Log.Info($"Patch {name}: Active (kept from before the mods were reloaded).");
                    return;
                }
                HarmonyMethod? Handler(string? handler) => handler == null ? null
                    : new HarmonyMethod(typeof(TestBridgeCommands), handler) { priority = first ? HarmonyLib.Priority.First : HarmonyLib.Priority.Normal };
                harmony.Patch(original, prefix: Handler(prefix), postfix: Handler(postfix), finalizer: Handler(finalizer));
                Log.Info($"Patch {name}: Active.");
                if (what == "save") TestBridgeCommands.SaveHooks++;
            }
            catch (Exception ex)
            {
                Log.Error($"Patch {name}: Not active, {ex.Message}");
            }
        }

        // One method at a time: UnpatchAll threw on the interface method above (Harmony rebuilds the original and can't
        // give it an interface as owner), which stopped the mod's unload and the game's own quit cleanup after it.
        public static void Remove()
        {
            if (harmony == null) return;
            List<MethodBase> methods;
            try { methods = harmony.GetPatchedMethods().ToList(); }
            catch (Exception ex)
            {
                // Nothing removed: keep the instance, so a later Apply in this run returns early instead of patching twice.
                Log.Error("Unpatch: " + ex.Message);
                return;
            }
            foreach (var method in methods)
            {
                if (method.DeclaringType == null || method.DeclaringType.IsInterface)
                {
                    keptPatched.Add(method);
                    Log.Info($"Unpatch {method.DeclaringType?.Name}.{method.Name}: kept, Harmony can't restore a method on an interface.");
                    continue;
                }
                // The dropdown guard is needed after the unload: the game reloads localization once the mods are unloaded.
                if (method.DeclaringType == typeof(UI.Elements.Dropdown) && method.Name == "UpdateSelectedOptionText")
                {
                    keptPatched.Add(method);
                    continue;
                }
                try { harmony.Unpatch(method, HarmonyPatchType.All, HarmonyId); }
                catch (Exception ex)
                {
                    // Harmony may have dropped the patch's record while the old detour stays: treat it as still patched.
                    keptPatched.Add(method);
                    Log.Error($"Unpatch {method.DeclaringType.Name}.{method.Name}: {ex.Message}");
                }
            }
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
                if (keptPatched.Contains(original))
                {
                    Log.Info($"Patch {name}: Active (kept from before the mods were reloaded).");
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

        private static void SettleSalesTakings()
        {
            try { CartSelling.SettleClosingDay(); }
            catch (Exception ex) { Log.Error("Window takings at midnight: " + ex); }
        }

        private static void VehicleRemoving(VehicleInstance vehicleInstance)
        {
            try { CartSelling.BeforeVehicleRemoved(vehicleInstance); }
            catch (Exception ex) { Log.Error("Window takings before vehicle removal: " + ex); }
        }

        // The hours the game counted itself this hour (building shifts), so a crew's serving doesn't count them again.
        private static void AfterGameCount()
        {
            try { HireCrew.AfterGameCount(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        // UI.Elements.Dropdown.UpdateSelectedOptionText: a dropdown whose option list was emptied or shortened after it was
        // set keeps its old selected index, and the next text update threw. The game updates every dropdown's text when it
        // reloads localization, which it does while unloading mods on quit, so one such dropdown stopped the quit cleanup
        // (a player's report). Such a dropdown shows its placeholder instead.
        private static bool loggedStaleDropdown;
        private static void StaleDropdownPrefix(List<string>? ____options, ref int ____selectedOptionIndex)
        {
            if (____selectedOptionIndex < 0 || (____options != null && ____selectedOptionIndex < ____options.Count)) return;
            if (!loggedStaleDropdown)
            {
                loggedStaleDropdown = true;
                Log.Info($"Dropdown: selected option {____selectedOptionIndex} of {____options?.Count ?? 0}; shown as unselected.");
            }
            ____selectedOptionIndex = -1;
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

        private static void PanelVehiclePrefix(VehicleController vehicle)
        {
            try { ServingWork.VehiclePanel(vehicle); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void PanelVehicle(ItemPanelUI __instance, VehicleController vehicle)
        {
            try { CartButton.OnSetVehicle(__instance, vehicle); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void PanelOther()
        {
            try { ServingWork.OtherPanel(); CartButton.Hide(); }
            catch (Exception ex) { Log.Error(ex); }
        }
    }
}
