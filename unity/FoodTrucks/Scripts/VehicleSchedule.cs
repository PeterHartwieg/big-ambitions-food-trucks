#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using Buildings;
using Entities;
using Extensions;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using UI;
using UI.Components;
using UI.Smartphone.Apps.BizMan.Schedule;
using UnityEngine;
using UnityEngine.UI;
using GameDropdown = UI.Elements.Dropdown;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // A vehicle's crew shifts on the game's own Schedule tab (Peter, 16 October 2026): the BizMan business page opens on
    // an in-memory BuildingRegistration that stands in for the vehicle. It is never added to the game's registrations or
    // saved, has no building, and holds the vehicle's week as the game's ScheduleDays: each weekday open unless its route
    // is closed (the day buttons' open toggle opens and closes the route day, as for a shop: Peter, 16 October 2026), its
    // opening hours the route day's hours (WeekPlan.RouteHours: they follow the route, so the hour toggles are refused),
    // and the crew's shifts on the vehicle's rows, synthetic workstations copied from a vanilla cash register (customer
    // service): one per staff place, Serving 1 (and 2 on a truck), ids ft:<vehicle>:serving1 / serving2. Anyone on shift
    // serves, drives and restocks (no driving row: Peter).
    //
    // The mod's save keeps Core's model (HireSchedule.CrewShifts with their row); the stand-in's days are built from it
    // when the tab opens and every change the game makes is read back into it (SyncBack), so nothing of the game's types
    // is saved. The patches below are inert unless the stand-in is showing: the business page shows only the Schedule tab
    // with the vehicle's name; the crew are the employees; the rows are the workstations; the game's employee updates,
    // which would zero a crew member's hours and give them the idle to-do, read the shifts back and set their weekly work
    // the mod's way (HireCrew.SetWeeklyWork); the auto-fill button runs Core's auto schedule (the game's needs an HR
    // manager and a real building); closing the page puts the business page's own fields back and returns to the vehicle.
    internal static class VehicleSchedule
    {
        private const string BaseItem = "ba:itemname_cashregister";
        private const string ServingSkill = "ba:skill_customerservice";

        private static BuildingRegistration? standIn;
        private static string? vehicleId;
        private static (Address? Address, Building? Building, BuildingRegistration? Registration)? saved;
        private static string? baseItem;

        // The stand-in is on the business page.
        internal static bool Showing { get; private set; }
        // The stand-in's business page is closing: its schedule's OnDisable (which may run after the page's own) is
        // skipped too. Cleared on the next frame, and by any other business page.
        private static bool closing;
        private static bool ready;

        // Every guard patch is active (Patches.Apply, then CheckReady). Without one of them a stand-in with no address
        // would reach game code that then works on every employee in the save, so the tab isn't offered (fail closed).
        internal static bool Ready => ready;

        private static readonly (Type Type, string Method, Type[]? Args, bool Postfix)[] Guards =
        {
            (typeof(BizManBusiness), "RefreshData", Type.EmptyTypes, false),
            (typeof(BizManBusiness), "LoadAlerts", Type.EmptyTypes, false),
            (typeof(BizManBusiness), "OnDisable", Type.EmptyTypes, false),
            (typeof(BizManBusiness), "SetAddress", new[] { typeof(Address) }, false),
            (typeof(BizManSchedule), "OnDisable", Type.EmptyTypes, false),
            (typeof(ScheduleHelper), "FetchEmployees", new[] { typeof(Address) }, false),
            (typeof(ScheduleHelper), "FetchWorkstations", Type.EmptyTypes, false),
            (typeof(ScheduleHelper), "UpdateEmployeeAfterWorkShiftChange", new[] { typeof(EmployeeInstance), typeof(bool) }, false),
            (typeof(ScheduleHelper), "UpdateEmployeesAfterWorkShiftChange", new[] { typeof(List<EmployeeInstance>) }, false),
            (typeof(EmployeeInstance), "UpdateWeeklyHoursAndDays", new[] { typeof(List<ScheduleDay>) }, true),
            (typeof(ScheduleAutoFillerHelper), "AutoFillSchedule", null, false),
            (typeof(ScheduleDaySelectionController), "UpdateHrManagerAvailability", new[] { typeof(bool) }, true),
            (typeof(ScheduleHourToggle), "OnPointerDown", new[] { typeof(UnityEngine.EventSystems.PointerEventData) }, false),
            (typeof(ScheduleDayButton), "OnOpenToggleChange", new[] { typeof(bool) }, false),
            (typeof(ScheduleScrollerController), "LoadStation", new[] { typeof(ItemInstance) }, true),
        };

        internal static void CheckReady()
        {
            var missing = new List<string>();
            foreach (var (type, method, args, postfix) in Guards)
            {
                var original = AccessTools.Method(type, method, args);
                var info = original == null ? null : Harmony.GetPatchInfo(original);
                var patches = info == null ? null : postfix ? info.Postfixes : info.Prefixes;
                if (patches == null || !patches.Any(p => p.PatchMethod.DeclaringType == typeof(VehicleSchedule))) missing.Add($"{type.Name}.{method}");
            }
            ready = missing.Count == 0;
            Log.Info(ready ? "Schedule: on (a vehicle's crew shifts on the game's Schedule tab)."
                : $"Schedule: off, patches not active: {string.Join(", ", missing)}; the vehicle page's Schedule button is disabled.");
        }

        private static bool IsStandIn(BuildingRegistration? registration) => Showing && registration != null && registration == standIn;

        private static bool Here => Showing && ScheduleHelper.Business != null && ScheduleHelper.Business.buildingRegistration == standIn;

        internal static void Reset()
        {
            Showing = false;
            standIn = null;
            vehicleId = null;
            saved = null;
        }

        // ---- Opening ----

        // Opens BizMan's business page on the vehicle's Schedule tab.
        internal static void Open(string id)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
            var vehicle = state == null ? null : StreetSales.Find(state, id);
            if (state == null || rules == null || bizMan == null || bizMan.business == null || vehicle == null || !FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            if (!ready)
            {
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            var item = BaseItemName();
            if (item == null)
            {
                Log.Error("Schedule: no assignable customer service item to copy for the vehicle's rows; the Schedule tab isn't offered.");
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            var names = StreetFoodBooks.VehicleNames();
            var kind = string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;
            var registration = new BuildingRegistration
            {
                BusinessName = names.TryGetValue(id, out var name) ? name : id,
                RentedByPlayer = false,
            };
            foreach (var row in WeekPlan.Stations(HireCrew.Limit(kind)))
            {
                var instance = new ItemInstance(item) { id = RowId(id, row), alias = RowName(row).ToUpperInvariant() };
                registration.itemInstances[instance.id] = instance;
            }
            // Shifts on a row the vehicle hasn't (an old driving row, a second row on a cart) move to a free one or go.
            var notes = WeekPlan.RepairShifts(vehicle, WeekPlan.Stations(HireCrew.Limit(kind)));
            foreach (var note in notes) Log.Info(note);
            if (notes.Count > 0) FoodTrucksSession.MarkChanged();
            registration.scheduleDays = Days(vehicle, id);
            var business = bizMan.business;
            if (!Showing) saved = (business.address, business.building, business.buildingRegistration);
            closing = false;
            standIn = registration;
            vehicleId = id;
            Showing = true;
            try
            {
                business.address = null;
                business.building = null;
                business.buildingRegistration = registration;
                business.SetInitialTab("Schedule");
                ((Component)bizMan.list).gameObject.SetActive(false);
                business.Open();
            }
            catch (Exception ex)
            {
                // Back as it was: the page's own fields, no stand-in, the list.
                var (address, building, previous) = saved ?? (null, null, null);
                business.address = address;
                business.building = building;
                business.buildingRegistration = previous;
                Reset();
                ((Component)bizMan.list).gameObject.SetActive(true);
                Log.Error($"Schedule: opening the Schedule tab for {id} failed: {ex}");
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            Log.Info($"Schedule: the game's Schedule tab opened for {kind} {id} ({vehicle.Crew.Count} crew, {vehicle.Schedule?.CrewShifts.Count ?? 0} shifts).");
        }

        private static string? BaseItemName()
        {
            if (baseItem != null) return baseItem;
            bool Fits(Item? item) => item != null && item.assignable && item.suitableSkills != null && item.suitableSkills.Contains(ServingSkill) &&
                                     !item.suitableSkills.Contains("ba:skill_deliverydriver");
            var chosen = Fits(ItemsGetter.GetByName(BaseItem, suppressError: true)) ? BaseItem
                : ItemsGetter.AllItems?.Where(Fits).Select(i => ((Object)i).name).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
            if (chosen != null) Log.Info($"Schedule: the vehicle's rows are copies of {chosen}.");
            return baseItem = chosen;
        }

        private static string RowId(string id, string station) => $"ft:{id}:{station}";

        private static string? StationOf(string? itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return null;
            var at = itemInstanceId!.LastIndexOf(':');
            return at < 0 ? null : itemInstanceId.Substring(at + 1);
        }

        private static string RowName(string station) =>
            BizManVehiclePage.Format("foodtrucks:schedule_row_serving", ("number", station.Substring("serving".Length)));

        // The game's weekday for one of ours, and back, by name (the game's days are numbered 1 to 7 from Monday).
        private static DayOfWeek WeekdayOf(DayOfWeekOrdered day) =>
            Enum.TryParse<DayOfWeek>(day.ToString(), true, out var parsed) ? parsed : WeekPlan.Days[Math.Max(0, Math.Min(6, (int)day - 1))];

        // The vehicle's week as the game's ScheduleDays, Monday first as the game indexes them (GetScheduleDay(i) is i - 1).
        private static List<ScheduleDay> Days(VehicleState vehicle, string id)
        {
            var rules = DemandData.Rules!;
            var home = HireRoutes.HomeOf(vehicle);
            var routeDays = WeekPlan.RouteDays(new HireSchedules(rules.Fleet), vehicle.Schedule, Travel.ModeFor(vehicle.VehicleType), HireRoutes.GameSpeed(),
                HireRoutes.HomePoint(home), home?.BayStops);
            var hours = WeekPlan.RouteHours(routeDays);
            var days = new List<ScheduleDay>();
            for (var i = 1; i <= 7; i++)
            {
                var ordered = (DayOfWeekOrdered)i;
                var day = WeekdayOf(ordered);
                days.Add(new ScheduleDay
                {
                    day = ordered,
                    // Open unless the route day is closed; shifts outside the route's hours may be set (paid, nothing to do).
                    isOpen = !WeekPlan.IsClosed(vehicle.Schedule, day),
                    openingHourSlots = WeekPlan.OpeningSlots(hours, day).Select(s => new OpeningHourSlot(s.Start, s.End)).ToList(),
                    workShifts = Shifts(vehicle, id, day),
                });
            }
            return days;
        }

        private static List<WorkShift> Shifts(VehicleState vehicle, string id, DayOfWeek day) =>
            (vehicle.Schedule?.CrewShifts ?? new List<CrewShift>()).Where(s => s.Day == day && vehicle.Crew.Contains(s.EmployeeId) && HireCrew.Find(s.EmployeeId) != null)
            .Select(s => new WorkShift
            {
                startingHour = s.Start, endingHour = s.End, employeeId = s.EmployeeId,
                itemInstanceId = RowId(id, s.Station ?? WeekPlan.Serving(1)), type = WorkShiftType.Default,
            }).ToList();

        // ---- Reading the game's changes back ----

        // The stand-in's shifts into the vehicle's save data, when they changed: today's plans follow, and the crew's
        // weekly hours and days on the game's employees.
        private static void SyncBack()
        {
            if (!Showing || standIn == null || vehicleId == null) return;
            var state = FoodTrucksSession.CurrentState;
            var vehicle = state == null ? null : StreetSales.Find(state, vehicleId);
            if (vehicle == null || !FoodTrucksSession.CanWrite) return;
            var read = new List<CrewShift>();
            foreach (var day in standIn.scheduleDays ?? new List<ScheduleDay>())
                foreach (var shift in day.workShifts ?? new List<WorkShift>())
                    if (shift != null && !string.IsNullOrEmpty(shift.employeeId) && vehicle.Crew.Contains(shift.employeeId) && shift.endingHour > shift.startingHour)
                        read.Add(new CrewShift
                        {
                            EmployeeId = shift.employeeId, Day = WeekdayOf(day.day), Start = Math.Max(0, shift.startingHour),
                            End = Math.Min(24, shift.endingHour), Station = StationOf(shift.itemInstanceId) ?? WeekPlan.Serving(1),
                        });
            var shifts = WeekPlan.Sanitize(read, out var dropped);
            if (dropped > 0) Log.Error($"Schedule: {dropped} shifts of {vehicleId} from the Schedule tab overlap another and weren't kept.");
            var schedule = vehicle.Schedule ??= new HireSchedule();
            string Key(IEnumerable<CrewShift> list) => string.Join(";", list.Select(s => $"{s.EmployeeId},{s.Day},{s.Start},{s.End},{s.Station}").OrderBy(x => x, StringComparer.Ordinal));
            if (Key(shifts) == Key(schedule.CrewShifts))
            {
                HireCrew.SetWeeklyWork(vehicle);
                return;
            }
            schedule.CrewShifts = shifts;
            WeekPlan.SortShifts(schedule);
            HireRunner.ReplanShifts(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Schedule: {vehicleId} now has {shifts.Count} shifts: " +
                     string.Join(", ", shifts.Select(s => $"{s.EmployeeId} {s.Day} {s.Start:D2}-{s.End:D2} {s.Station}")) + ".");
        }

        // Core's auto schedule for the week or a day (the game's auto-fill button), written to the save and to the
        // stand-in's days in place (the screen holds them).
        private static void Auto(ScheduleDay? only)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var vehicle = state == null || vehicleId == null ? null : StreetSales.Find(state, vehicleId);
            if (vehicle == null || rules == null || standIn == null || !FoodTrucksSession.CanWrite) return;
            SyncBack();
            var kind = string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;
            var home = HireRoutes.HomeOf(vehicle);
            var routeDays = WeekPlan.RouteDays(new HireSchedules(rules.Fleet), vehicle.Schedule, Travel.ModeFor(kind), HireRoutes.GameSpeed(),
                HireRoutes.HomePoint(home), home?.BayStops);
            var wishes = vehicle.Crew.Select(c => HireCrew.Find(c) is EmployeeInstance e ? HireCrew.Wish(e) : new CrewWish(c)).ToList();
            vehicle.Schedule ??= new HireSchedule();
            vehicle.Schedule.CrewShifts = WeekPlan.AutoSchedule(rules.Fleet, routeDays, wishes, HireCrew.Limit(kind), vehicle.Schedule.CrewShifts,
                only == null ? null : new[] { WeekdayOf(only.day) });
            foreach (var day in standIn.scheduleDays) day.workShifts = Shifts(vehicle, vehicleId!, WeekdayOf(day.day));
            HireRunner.ReplanShifts(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Schedule: auto schedule for {vehicleId} ({(only == null ? "the week" : WeekdayOf(only.day).ToString())}): " +
                     string.Join(", ", vehicle.Schedule.CrewShifts.Select(s => $"{s.EmployeeId} {s.Day} {s.Start:D2}-{s.End:D2} {s.Station}")) + ".");
            // The screen again, on the same data.
            InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan?.business?.bizManSchedule?.LoadScheduler();
        }

        // ---- Patches (Patches.Apply), inert unless the stand-in is showing ----

        // BizManBusiness.RefreshData: only the Schedule tab, the vehicle's name and type, no logo, security, closed toggle,
        // requirements or alerts.
        public static bool RefreshPrefix(BizManBusiness __instance)
        {
            if (!IsStandIn(__instance.buildingRegistration)) return true;
            try
            {
                var page = Traverse.Create(__instance);
                if (page.Field("menu").GetValue<Transform>() is Transform menu)
                    foreach (Transform tab in menu) tab.gameObject.SetActive(tab.name == "Schedule");
                page.Field("_tabs").SetValue(new List<string> { "Schedule" });
                page.Field("_businesses").SetValue(new List<BuildingRegistration> { standIn! });
                page.Method("SetTab", new[] { typeof(string) }).GetValue("Schedule");
                if (page.Field("businessNameDropdown").GetValue<GameDropdown>() is GameDropdown names)
                {
                    names.SetOptions(new List<string> { standIn!.BusinessName }, localize: false, selectedOption: 0);
                    names.SetInteractable(false);
                }
                var vehicle = vehicleId == null ? null : StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
                if (page.Field("businessTypeLabel").GetValue<TextLocalizationComponent>() is TextLocalizationComponent type)
                    type.Key = vehicle?.VehicleType == "truck" ? "foodtrucks:vehicletype_foodtruck" : "foodtrucks:vehicletype_foodcart";
                if (page.Field("temporarilyClosedToggle").GetValue<Toggle>() is Toggle closed) closed.transform.parent.gameObject.SetActive(false);
                page.Field("securityInfo").GetValue<GameObject>()?.SetActive(false);
                if (page.Field("businessLogo").GetValue<Image>() is Image logo)
                {
                    logo.sprite = InstanceBehavior<GlobalReferences>.Instance.vehiclePOIIcon;
                    logo.preserveAspect = true;
                }
                page.Field("requirementTemplate").GetValue<Transform>()?.ResetTemplate();
                page.Field("alertTemplate").GetValue<Transform>()?.ResetTemplate();
            }
            catch (Exception ex) { Log.Error("Schedule: business page: " + ex); }
            return false;
        }

        // BizManBusiness.LoadAlerts: the vehicle's to-dos are on its own page; none here (the stand-in has no address).
        public static bool AlertsPrefix(BizManBusiness __instance)
        {
            if (!IsStandIn(__instance.buildingRegistration)) return true;
            var page = Traverse.Create(__instance);
            page.Field("alertTemplate").GetValue<Transform>()?.ResetTemplate();
            page.Field("_scheduleLoadBizManAlerts").SetValue(false);
            return false;
        }

        // BizManBusiness.OnDisable: the shifts are read back, the page's own fields put back, and BizMan returns to the
        // vehicle's page if it is still open. The game's registration-change events aren't sent for the stand-in.
        public static bool BusinessClosedPrefix(BizManBusiness __instance)
        {
            if (!IsStandIn(__instance.buildingRegistration)) return true;
            var id = vehicleId;
            try
            {
                SyncBack();
                var page = Traverse.Create(__instance);
                if (page.Field("_currentContainer").GetValue<Transform>() is Transform container) container.gameObject.SetActive(false);
                if (page.Field("_currentTabText").GetValue<Graphic>() is Graphic text)
                    text.color = page.Field("_defaultTabColor").GetValue<Color32>();
                page.Field("_currentContainer").SetValue(null);
                page.Field("_currentTabText").SetValue(null);
                if (page.Field("businessNameDropdown").GetValue<GameDropdown>() is GameDropdown names) names.SetInteractable(true);
                if (page.Field("menu").GetValue<Transform>() is Transform menu)
                    foreach (Transform tab in menu) tab.gameObject.SetActive(true);
                var (address, building, registration) = saved ?? (null, null, null);
                __instance.address = address;
                __instance.building = building;
                __instance.buildingRegistration = registration;
                Log.Info($"Schedule: the Schedule tab for {id} closed.");
            }
            catch (Exception ex) { Log.Error("Schedule: closing: " + ex); }
            finally
            {
                // The session ends here whatever happened; the schedule's own OnDisable, which may come after this one, is
                // skipped through `closing` until the next frame.
                Reset();
                closing = true;
                HireRoutes.Later(() =>
                {
                    closing = false;
                    var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
                    if (id != null && bizMan != null && ((Component)bizMan).gameObject.activeInHierarchy && !((Component)bizMan.business).gameObject.activeSelf)
                        BizManVehiclePage.OpenFor(id);
                });
            }
            return false;
        }

        // BizManBusiness.SetAddress: the page moves to a real business (BizMan.Open from a notification, the name
        // dropdown) while the stand-in shows: the vehicle's session ends first, its shifts read back.
        public static void SetAddressPrefix(BizManBusiness __instance)
        {
            closing = false;
            if (!IsStandIn(__instance.buildingRegistration)) return;
            var id = vehicleId;
            try
            {
                SyncBack();
                var page = Traverse.Create(__instance);
                if (page.Field("businessNameDropdown").GetValue<GameDropdown>() is GameDropdown names) names.SetInteractable(true);
                Log.Info($"Schedule: the business page moved on from {id}'s Schedule tab.");
            }
            catch (Exception ex) { Log.Error("Schedule: leaving for another business: " + ex); }
            finally { Reset(); }
        }

        // BizManSchedule.OnDisable: no customer entries, theatre or missing-employee checks or address event for the stand-in.
        public static bool ScheduleClosedPrefix(BizManSchedule __instance)
        {
            // Only the stand-in's own page (or its closing, when the page's OnDisable ran first): never a real business's.
            var shown = Traverse.Create(__instance).Field("bizManBusiness").GetValue<BizManBusiness>()?.buildingRegistration;
            if (!closing && !IsStandIn(shown)) return true;
            try
            {
                if (__instance.scheduleConfirm != null) ((Component)__instance.scheduleConfirm).gameObject.SetActive(false);
                SyncBack();
            }
            catch (Exception ex) { Log.Error("Schedule: closing the schedule: " + ex); }
            return false;
        }

        // ScheduleHelper.FetchEmployees: the vehicle's crew.
        public static bool EmployeesPrefix()
        {
            if (!Here) return true;
            var vehicle = vehicleId == null ? null : StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            // Every crew member who exists, off sick, training or being replaced too: the game draws their shift bars and
            // looks them up by id (GetEmployeeColor). Who works is decided by the mod's own rules, not this list.
            var crew = vehicle == null ? new List<EmployeeInstance>()
                : vehicle.Crew.Select(HireCrew.Find).Where(e => e != null).Select(e => e!).ToList();
            Traverse.Create(typeof(ScheduleHelper)).Property("Employees").SetValue(crew);
            ScheduleHelper.EmployeesById ??= new Dictionary<string, EmployeeInstance>();
            ScheduleHelper.EmployeesById.Clear();
            foreach (var employee in crew) ScheduleHelper.EmployeesById[employee.id] = employee;
            return false;
        }

        // ScheduleHelper.FetchWorkstations: the vehicle's rows.
        public static bool WorkstationsPrefix()
        {
            if (!Here) return true;
            var rows = new Dictionary<string, ItemInstance>();
            foreach (var item in standIn!.itemInstances.Values) rows[item.id] = item;
            Traverse.Create(typeof(ScheduleHelper)).Property("WorkstationsById").SetValue(rows);
            return false;
        }

        // ScheduleHelper.UpdateEmployeeAfterWorkShiftChange(employee, single) and UpdateEmployeesAfterWorkShiftChange: the
        // game would set the hours from the stand-in's days as a building's (zero for a crew member, who has no address),
        // unassign them and give them the idle to-do. Instead the shifts are read back and the mod sets their weekly work.
        public static bool EmployeeChangedPrefix()
        {
            if (!Here) return true;
            try { SyncBack(); }
            catch (Exception ex) { Log.Error("Schedule: reading the shifts back: " + ex); }
            return false;
        }

        // EmployeeInstance.UpdateWeeklyHoursAndDays (a paste of a day, a removed shift): the crew's weekly work follows the
        // shifts again.
        public static void WeeklyHoursPostfix(EmployeeInstance __instance)
        {
            if (!Showing || __instance == null || vehicleId == null) return;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            if (vehicle == null || !vehicle.Crew.Contains(__instance.id)) return;
            try { SyncBack(); }
            catch (Exception ex) { Log.Error("Schedule: weekly hours: " + ex); }
        }

        // ScheduleAutoFillerHelper.AutoFillSchedule: Core's auto schedule for the stand-in.
        public static bool AutoFillPrefix(BuildingRegistration business, ScheduleDay day)
        {
            if (!IsStandIn(business)) return true;
            try { Auto(day); }
            catch (Exception ex) { Log.Error("Schedule: auto schedule: " + ex); }
            return false;
        }

        // ScheduleDaySelectionController.UpdateHrManagerAvailability: the auto-fill buttons work for a vehicle with crew,
        // without an HR manager (Core's auto schedule, not the game's solver).
        public static void AutoAvailablePostfix(object __instance)
        {
            if (!Here) return;
            try
            {
                var controller = Traverse.Create(__instance);
                var crew = ScheduleHelper.Employees?.Count ?? 0;
                controller.Field("_hasHrManager").SetValue(true);
                if (controller.Field("autoScheduleButton").GetValue<Button>() is Button button) button.interactable = crew > 0;
                var tooltip = controller.Field("autoScheduleTooltip").GetValue<object>();
                if (tooltip != null) Traverse.Create(tooltip).Field("descriptionKey").SetValue(crew == 0 ? "bizman_schedule_employee_no_employees" : "bizman_schedule_auto_scheduler_all_tooltip");
                foreach (var dayButton in controller.Field("_scheduleDayButtons").GetValue<List<ScheduleDayButton>>() ?? new List<ScheduleDayButton>())
                    dayButton.UpdateHrManagerAvailability(true);
            }
            catch (Exception ex) { Log.Error("Schedule: auto schedule buttons: " + ex); }
        }

        // ScheduleHourToggle.OnPointerDown: the opening hours are the route day's; they change with the route.
        public static bool HourTogglePrefix()
        {
            if (!Here) return true;
            CartSelling.Notify("foodtrucks:notification_schedule_hours_route");
            return false;
        }

        // ScheduleDayButton.OnOpenToggleChange: the day's open toggle opens or closes the vehicle's route that weekday, as for
        // a shop (Peter, 16 October 2026). The route is changed first (RouteEditor.SetClosed, refused as before when a night
        // would run into a working morning: the toggle goes back); then the game's own handler marks the day and reloads it.
        // Today is planned again (RouteStops.Changed): a window open at a stop of a closed day closes and its drive back
        // stays owed (VehicleState.ReturnPending), so nothing is stranded.
        public static bool DayTogglePrefix(ScheduleDayButton __instance, bool isOpen)
        {
            if (!Here) return true;
            try
            {
                var state = FoodTrucksSession.CurrentState;
                var rules = DemandData.Rules;
                var vehicle = state == null || vehicleId == null ? null : StreetSales.Find(state, vehicleId);
                var day = WeekdayOf((DayOfWeekOrdered)__instance.dayIndex);
                var problem = vehicle == null || rules == null || !FoodTrucksSession.CanWrite ? ScheduleProblem.NoStops
                    : RouteEditor.SetClosed(new HireSchedules(rules.Fleet), vehicle, day, !isOpen);
                if (problem != ScheduleProblem.None)
                {
                    Traverse.Create(__instance).Field("openToggle").GetValue<Toggle>()?.SetIsOnWithoutNotify(!isOpen);
                    Log.Info($"Schedule: {(isOpen ? "opening" : "closing")} {day} for {vehicleId} refused: {problem}.");
                    CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                    return false;
                }
                RouteStops.Changed(vehicle!);
                OpeningHours(vehicle!);
                Log.Info($"Schedule: {vehicleId} {(isOpen ? "open" : "closed")} on {day} (the Schedule tab's day toggle).");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Schedule: day toggle: " + ex);
                Traverse.Create(__instance).Field("openToggle").GetValue<Toggle>()?.SetIsOnWithoutNotify(!isOpen);
                return false;
            }
        }

        // The stand-in's opening hours again after the route changed (in place: the screen holds the days).
        private static void OpeningHours(VehicleState vehicle)
        {
            if (standIn?.scheduleDays == null) return;
            var rules = DemandData.Rules!;
            var home = HireRoutes.HomeOf(vehicle);
            var hours = WeekPlan.RouteHours(WeekPlan.RouteDays(new HireSchedules(rules.Fleet), vehicle.Schedule, Travel.ModeFor(vehicle.VehicleType),
                HireRoutes.GameSpeed(), HireRoutes.HomePoint(home), home?.BayStops));
            foreach (var day in standIn.scheduleDays)
                day.openingHourSlots = WeekPlan.OpeningSlots(hours, WeekdayOf(day.day)).Select(s => new OpeningHourSlot(s.Start, s.End)).ToList();
        }

        // ScheduleScrollerController.LoadStation: a vehicle's row brings no customers and has nothing attached.
        public static void StationPostfix(object __instance)
        {
            if (!Here) return;
            var data = Traverse.Create(__instance).Field("data").GetValue<List<ScheduleWorkstationModel>>();
            if (data == null || data.Count == 0) return;
            data[data.Count - 1].customersPerHour = 0;
            data[data.Count - 1].attachedItems = null;
        }

        // ---- The test bridge ----

        // One crew member's shift (the bridge's crew schedule op): added to their shifts that day, or with `replace` in
        // place of them; `start` == `end` removes their shifts that day. Their other shifts stay unless replaced, so a
        // shift running now isn't ended by adding a later one (playtest a14: replacing closed a truck its crew served).
        internal static ShiftProblem SetForBridge(string id, string employeeId, DayOfWeek day, int start, int end, string? station, bool replace = false)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var vehicle = state == null ? null : StreetSales.Find(state, id);
            if (vehicle == null || rules == null) return ShiftProblem.NotCrew;
            ShiftProblem problem;
            if (start == end)
            {
                problem = vehicle.Crew.Contains(employeeId) ? ShiftProblem.None : ShiftProblem.NotCrew;
                if (problem == ShiftProblem.None) WeekPlan.RemoveShift(vehicle, employeeId, day);
            }
            else if (replace) problem = WeekPlan.SetShift(rules.Fleet, vehicle, employeeId, day, start, end, station, HireCrew.Limit(vehicle.VehicleType));
            else problem = WeekPlan.AddShift(rules.Fleet, vehicle, employeeId, day, start, end, station, HireCrew.Limit(vehicle.VehicleType));
            if (problem != ShiftProblem.None) return problem;
            Changed(vehicle);
            Log.Info($"Schedule: {employeeId} on {id} {(start == end ? "has no shift" : replace ? "works only" : "also works")} {day}" +
                     $"{(start == end ? "" : $" {start:D2}-{end:D2}")} (test bridge).");
            return ShiftProblem.None;
        }

        // Removes one of a crew member's shifts that day, the one starting at `start` (the bridge's crew unschedule op).
        // False, with nothing changed, when they have no shift starting then.
        internal static bool RemoveForBridge(string id, string employeeId, DayOfWeek day, int start)
        {
            var state = FoodTrucksSession.CurrentState;
            var vehicle = state == null ? null : StreetSales.Find(state, id);
            if (vehicle == null || !WeekPlan.RemoveShiftAt(vehicle, employeeId, day, start)) return false;
            Changed(vehicle);
            Log.Info($"Schedule: {employeeId}'s {day} shift from {start:D2}:00 on {id} removed (test bridge).");
            return true;
        }

        private static void Changed(VehicleState vehicle)
        {
            HireRunner.ReplanShifts(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
        }
    }
}
