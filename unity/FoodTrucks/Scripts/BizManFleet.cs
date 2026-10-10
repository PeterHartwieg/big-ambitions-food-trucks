#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Entities;
using Extensions;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using Streets;
using UI;
using UI.Guiders;
using UI.Notification;
using UI.Smartphone.Apps.BizMan;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // BizMan: one "Street food" line at the end of the business list, and a fleet section with a card per vehicle.
    //
    // The game's list holds only building registrations, and its business pages, Insight, schedule and pricing all
    // read one. So the line is a list model with no registration (marked by its ID), and four patches keep it away
    // from code that would need one: the filter (a neighbourhood filter looks up the building), the cell (its
    // destination button uses the address), the click (it opens the business page by address), and the list
    // itself. The line only shows when all four are in place.
    //
    // The fleet section is a copy of BizMan's own warehouse section, placed right after it in the same list, with
    // a copy of the warehouse card per vehicle: its name, address and two small tables (drivers, inventory) hold
    // the vehicle's whereabouts, money, grade, crew and route, and what it sold today and has left. Clicking the
    // Street food line opens the section and scrolls to it; leaving the list closes it. If anything here fails,
    // the line is taken out and BizMan carries on without it.
    internal static class BizManFleet
    {
        private const string EntryId = "foodtrucks:streetfood";
        private const string BusinessesStatus = "common_businesses";

        private static bool ready, broken;
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        // Cells whose destination button the Street food line hid; a recycled cell gets it back.
        private static readonly HashSet<int> hiddenButtons = new HashSet<int>();
        private static Transform? section, cardTemplate;

        // Called once after patching: the line shows only when every patch that keeps it safe is in place.
        internal static void CheckReady()
        {
            ready = Has(typeof(BusinessScrollerController), "PopulateAllModels") && Has(typeof(BizManFilterToggle), "PassesFilter") &&
                    Has(typeof(BusinessCellView), "SetData") && Has(typeof(BizManList), "OpenBusiness");
            Log.Info(ready
                ? "BizMan: the Street food line and fleet section are on."
                : "BizMan: no Street food line (a patch it needs isn't active).");
        }

        private static bool Has(Type type, string method)
        {
            var original = AccessTools.Method(type, method);
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            return info != null && info.Prefixes.Concat(info.Postfixes).Any(p => p.PatchMethod.DeclaringType == typeof(BizManFleet));
        }

        private static bool IsOurs(BusinessCellView.BusinessModel? model) => model != null && model.Id == EntryId;

        // Logs a failure once and takes the line out of the list for the rest of the session.
        private static void Fail(string where, Exception ex, BusinessScrollerController? list = null)
        {
            broken = true;
            if (logged.Add(where)) Log.Error($"BizMan Street food ({where}), line taken out: {ex}");
            try
            {
                if (section != null) section.gameObject.SetActive(false);
                list?.RemoveModels(IsOurs);
            }
            catch (Exception again) { Log.Error(again); }
        }

        private static void Note(string message)
        {
            if (logged.Add(message)) Log.Info("BizMan: " + message);
        }

        // ---- The list line ----

        public static void PopulatePostfix(BusinessScrollerController __instance, List<BusinessCellView.BusinessModel> allModels)
        {
            if (!ready || broken || allModels == null) return;
            try
            {
                var state = FoodTrucksSession.CurrentState;
                var vehicles = Vehicles();
                if (state == null || vehicles.Count == 0) return;
                var model = (BusinessCellView.BusinessModel)FormatterServices.GetUninitializedObject(typeof(BusinessCellView.BusinessModel));
                // The constructor isn't run, so the list the field initializer would make is set here.
                AccessTools.Field(typeof(BusinessCellView.BusinessModel), "statuses").SetValue(model, new List<string> { BusinessesStatus });
                var today = SaveGameManager.Current.Day;
                var open = vehicles.Count(v => StreetSales.Find(state, v.Instance.id)?.IsOpen == true);
                var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
                model.Id = EntryId;
                model.BusinessName = Text("foodtrucks:econoview_row");
                model.BusinessType = "foodtrucks:bizman_type";
                model.BuildingType = string.Empty;
                model.Address = null;
                model.AvgDailyIncome = FleetReport.WeekProfitCents(state, today) / 100f / FleetReport.WeekDays;
                model.Alerts = 0;
                model.Satisfaction = 0;
                model.OpenStatus = (LanguageChangeEventDataHolder.Create("foodtrucks:bizman_open_count",
                        new Dictionary<string, string> { ["open"] = open.ToString(), ["total"] = vehicles.Count.ToString() }),
                    (Color)(open > 0 ? colors.green : colors.lightRed));
                // Sorts like the game's own: open businesses before closed ones.
                model.Status = open > 0 ? -1 : -2;
                model.IsRealEstate = false;
                allModels.Add(model);
            }
            catch (Exception ex) { Fail("list", ex); }
        }

        // The line has no building: a building type, business type or neighbourhood filter leaves it out, and the
        // "Businesses" status filter keeps it. Returning false skips the game's check, which would look the address up.
        public static bool FilterPrefix(BizManFilterToggle __instance, BusinessCellView.BusinessModel item, ref bool __result)
        {
            if (!IsOurs(item)) return true;
            try
            {
                var status = Traverse.Create(__instance).Field("_status").GetValue<string>();
                __result = status == BusinessesStatus;
            }
            catch (Exception ex)
            {
                // Left out this time; the list is mid-filter, so it is only dropped from the next load on.
                __result = false;
                Fail("filter", ex);
            }
            return false;
        }

        // The game's cell has drawn the line from the model; its destination button would ask for an address and
        // its satisfaction column reads 0%, so both are put right here. Other cells get their button back.
        public static void CellPostfix(BusinessCellView __instance, BusinessCellView.BusinessModel data)
        {
            try
            {
                var button = __instance.destinationButton;
                if (button == null) return;
                var id = __instance.GetInstanceID();
                if (IsOurs(data))
                {
                    button.onClick.RemoveAllListeners();
                    button.gameObject.SetActive(false);
                    hiddenButtons.Add(id);
                    if (Traverse.Create(__instance).Field("satisfaction").GetValue() is Component satisfaction) SetText(satisfaction.transform, "-");
                }
                else if (hiddenButtons.Remove(id)) button.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                // The cell is being drawn; the line is dropped from the next load on.
                Fail("cell", ex);
            }
        }

        // A click on the line opens the fleet section instead of a business page. Returning false skips the
        // game's handler, which would open BizMan's business page for an address the line doesn't have.
        public static bool OpenPrefix(BizManList __instance, BusinessCellView.BusinessModel businessModel)
        {
            if (!IsOurs(businessModel)) return true;
            try { Open(__instance); }
            catch (Exception ex) { Fail("fleet section", ex, Traverse.Create(__instance).Field("businessScrollerController").GetValue<BusinessScrollerController>()); }
            return false;
        }

        // Leaving the list closes the section.
        public static void ListClosedPostfix()
        {
            try { if (section != null) section.gameObject.SetActive(false); }
            catch (Exception ex) { Log.Error(ex); }
        }

        // ---- The fleet section ----

        private static void Open(BizManList list)
        {
            if (!Ensure(list) || section == null || cardTemplate == null) return;
            Fill();
            section.gameObject.SetActive(true);
            HireRoutes.Later(() =>
            {
                try { ScrollTo(section); }
                catch (Exception ex) { Log.Error(ex); }
            });
        }

        // Copies BizMan's warehouse section once per list (the list is rebuilt with each city scene).
        private static bool Ensure(BizManList list)
        {
            if (section != null && cardTemplate != null) return true;
            var warehouses = Traverse.Create(list).Field("warehouseList").GetValue<WarehouseList>();
            var template = warehouses == null ? null : Traverse.Create(warehouses).Field("warehouseEntry").GetValue<Transform>();
            var businesses = Traverse.Create(list).Field("businessScrollerController").GetValue<BusinessScrollerController>();
            if (warehouses == null || template == null || warehouses.transform.parent == null)
                throw new InvalidOperationException("BizMan's warehouse section or its card isn't where 1.0 had it.");
            // The copy has to be the warehouse section alone, not a panel that also holds the business list.
            if (businesses != null && businesses.transform.IsChildOf(warehouses.transform))
                throw new InvalidOperationException("BizMan's warehouse section holds the business list; copying it would copy the list.");
            var path = PathBelow(template, warehouses.transform)
                       ?? throw new InvalidOperationException("The warehouse card isn't inside the warehouse section.");

            // Copied under an inactive holder, so none of the copy's components wake before the warehouse loader is gone.
            var holder = new GameObject("FoodTrucks.Holder");
            holder.SetActive(false);
            holder.transform.SetParent(warehouses.transform.parent, false);
            var copy = UnityEngine.Object.Instantiate(warehouses.gameObject, holder.transform);
            copy.name = "FoodTrucks.StreetFoodSection";
            UnityEngine.Object.DestroyImmediate(copy.GetComponent<WarehouseList>());
            var card = copy.transform.Find(path) ?? throw new InvalidOperationException("The copied section has no warehouse card.");
            copy.SetActive(false);
            copy.transform.SetParent(warehouses.transform.parent, false);
            copy.transform.SetSiblingIndex(warehouses.transform.GetSiblingIndex() + 1);
            UnityEngine.Object.Destroy(holder);

            // The section's own heading becomes Street food, and its buttons (renting a warehouse and the like) go.
            foreach (var label in copy.GetComponentsInChildren<TextLocalizationComponent>(true))
            {
                if (label.transform.IsChildOf(card)) continue;
                Note($"fleet section label {PathBelow(label.transform, copy.transform)} = '{label.Key}'.");
                if (!string.IsNullOrEmpty(label.Key) && label.Key.IndexOf("warehouse", StringComparison.OrdinalIgnoreCase) >= 0)
                    label.SetData(LanguageChangeEventDataHolder.Create("foodtrucks:bizman_fleet_header"));
            }
            foreach (var button in copy.GetComponentsInChildren<Button>(true))
            {
                if (button.transform.IsChildOf(card) || card.IsChildOf(button.transform)) continue;
                Note($"fleet section button {PathBelow(button.transform, copy.transform)} hidden.");
                button.gameObject.SetActive(false);
            }
            foreach (var label in card.GetComponentsInChildren<TextLocalizationComponent>(true))
                Note($"fleet card label {PathBelow(label.transform, card)} = '{label.Key}'.");

            // The copy came with the player's warehouse cards; the game's own reset clears them off the template.
            card.ResetTemplate();
            section = copy.transform;
            cardTemplate = card;
            Log.Info($"BizMan: fleet section copied from the warehouse section (card at {path}).");
            return true;
        }

        private static string? PathBelow(Transform target, Transform root)
        {
            var parts = new List<string>();
            for (var t = target; t != null; t = t.parent)
            {
                if (t == root) return string.Join("/", Enumerable.Reverse(parts));
                parts.Add(t.name);
            }
            return null;
        }

        private static void Fill()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || cardTemplate == null) return;
            // The last opening's cards go: they are copies of the template, which the reset clears.
            cardTemplate.ResetTemplate();
            var names = StreetFoodBooks.VehicleNames();
            foreach (var vehicle in Vehicles().OrderBy(v => names.TryGetValue(v.Instance.id, out var n) ? n : v.Instance.id, StringComparer.Ordinal))
            {
                var card = UnityEngine.Object.Instantiate(cardTemplate, cardTemplate.parent);
                card.name = "FoodTrucks.VehicleCard";
                FillCard(card, state, vehicle.Instance, vehicle.WithDriver, names.TryGetValue(vehicle.Instance.id, out var name) ? name : vehicle.Instance.id);
                card.gameObject.SetActive(true);
            }
        }

        private static void FillCard(Transform card, SaveState state, VehicleInstance instance, bool withDriver, string name)
        {
            var vehicle = StreetSales.Find(state, instance.id);
            var today = SaveGameManager.Current.Day;
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;

            // A click on the card does nothing; the warehouse card's would open a business page.
            var root = card.GetComponent<Button>();
            if (root != null) root.onClick = new Button.ButtonClickedEvent();
            SetText(card.Find("WarehouseName"), name);
            SetText(card.Find("Address"), Where(instance, vehicle, withDriver));
            var destination = card.Find("Address/SetDestinationButton")?.GetComponent<Button>();
            if (destination != null)
            {
                destination.onClick = new Button.ButtonClickedEvent();
                var canGo = !withDriver && vehicle?.Transit == null;
                destination.gameObject.SetActive(canGo);
                if (canGo) destination.onClick.AddListener(() => Guard(() => GoTo(instance, name)));
            }
            Hide(card, "ManageDrivers");
            Hide(card, "ShowFullInventory");

            // The drivers table: money, health, crew and route, three columns a row.
            var money = FleetReport.Vehicle(state, instance.id, today);
            var crew = (vehicle?.Crew ?? new List<string>()).Select(HireCrew.Find).Where(e => e != null).Select(e => HireCrew.Name(e!)).ToList();
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName);
            var rows = new List<(string, string, string, bool)>
            {
                (Text("foodtrucks:bizman_fleet_today"), Amount("foodtrucks:bizman_fleet_sales", money.TodaySalesCents), Amount("foodtrucks:bizman_fleet_profit", money.TodayProfitCents), money.TodayProfitCents < 0),
                (Text("foodtrucks:bizman_fleet_week"), Amount("foodtrucks:bizman_fleet_sales", money.WeekSalesCents), Amount("foodtrucks:bizman_fleet_profit", money.WeekProfitCents), money.WeekProfitCents < 0),
                (Text("foodtrucks:bizman_fleet_health"), Capital(CartSelling.GradeText(vehicle)), "", false),
                (Text("foodtrucks:bizman_fleet_crew"), crew.Count == 0 ? Text("foodtrucks:bizman_fleet_crew_none") : string.Join(", ", crew),
                    Format("foodtrucks:bizman_fleet_crew_count", ("count", crew.Count.ToString()), ("limit", HireCrew.Limit(kind).ToString())), false),
                (Text("foodtrucks:bizman_fleet_route"), Route(vehicle), "", false),
            };
            // Storage per piece: one row per fitted food piece, "Hot Dog Grill  62/80 units", and the Extra Storage.
            var storage = StoragePanel.Of(instance);
            if (storage != null)
            {
                foreach (var pool in storage.Pools)
                    rows.Add((StoragePanel.PieceName(instance, pool.Piece),
                        Format("foodtrucks:bizman_fleet_storage_units", ("load", pool.LoadUnits.ToString()), ("capacity", pool.Capacity.ToString())),
                        "", false));
                if (storage.StoragePieces > 0)
                    rows.Add((Text("foodtrucks:bizman_fleet_storage_extra"), Text("foodtrucks:bizman_fleet_storage_doubles"), "", false));
            }
            Rows(card, "VehicleSlotsList/VehicleSlotEntry", rows.Count, (row, i) =>
            {
                var (label, first, second, loss) = rows[i];
                SetText(row.Find("SlotNumber"), label);
                SetText(row.Find("VehicleName"), first);
                SetText(row.Find("DriverStatus"), second);
                Colour(row.Find("DriverStatus"), loss ? (Color)colors.lightRed : (Color?)null);
            });

            // The inventory table: units sold today and units left of each item, most sold first, like the game's
            // sold-amount tooltip.
            var sold = SoldCounts.On(vehicle, today);
            var tables = DemandData.Tables;
            var stock = tables == null ? new Dictionary<string, int>() : CartDemand.StockOf(instance, CartDemand.MenuOf(tables, instance).Menu);
            var items = sold.Select(s => s.Item).Concat(stock.Where(s => s.Value > 0).Select(s => s.Key).OrderBy(i => i, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).ToList();
            var soldOf = sold.ToDictionary(s => s.Item, s => s.Units, StringComparer.Ordinal);
            Rows(card, "InventoryList/InventoryEntry", Math.Max(1, items.Count), (row, i) =>
            {
                var splitter = row.Find("Splitter");
                if (splitter != null) splitter.gameObject.SetActive(i < items.Count - 1);
                if (items.Count == 0)
                {
                    SetText(row.Find("ItemName"), Text("foodtrucks:bizman_fleet_nothing"));
                    SetText(row.Find("Amount"), "");
                    SetText(row.Find("DaysLeft"), "");
                    return;
                }
                var item = items[i];
                soldOf.TryGetValue(item, out var units);
                stock.TryGetValue(item, out var left);
                SetText(row.Find("ItemName"), Text(item));
                SetText(row.Find("Amount"), Format("foodtrucks:bizman_fleet_sold", ("count", units.ToString())));
                SetText(row.Find("DaysLeft"), Format("foodtrucks:bizman_fleet_left", ("count", left.ToString())));
                Colour(row.Find("DaysLeft"), left == 0 ? (Color)colors.lightRed : (Color?)null);
            });
        }

        // Fills copies of a card's row template; the template itself stays hidden.
        private static void Rows(Transform card, string path, int count, Action<Transform, int> fill)
        {
            var template = card.Find(path);
            if (template == null)
            {
                Note($"the warehouse card has no {path}; that part of the fleet card stays empty.");
                return;
            }
            template.gameObject.SetActive(false);
            for (var i = 0; i < count; i++)
            {
                var row = UnityEngine.Object.Instantiate(template, template.parent);
                fill(row, i);
                row.gameObject.SetActive(true);
            }
        }

        // The GPS to the vehicle. In 1.0 a vehicle's street address names the building or garage it is in and is
        // empty outdoors, so an indoor vehicle gets the game's own address destination (as a business's button
        // does) and an outdoor one a point: where it stands, or where the save has it when it isn't spawned.
        private static void GoTo(VehicleInstance instance, string name)
        {
            if (!string.IsNullOrEmpty(instance.streetName))
            {
                InstanceBehavior<UIs>.Instance.fullMenu.bizMan.business.SetDestination(instance.Address);
                return;
            }
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var references = InstanceBehavior<GlobalReferences>.Instance;
            // A point can't be kept as the save's destination, which is an address; the old one would come back.
            SaveGameManager.Current.customDestination = null;
            GuidersManager.SetGuiderTarget(position, name, references.vehiclePOIIcon, references.vehiclePOIBackgroundColor, DirectionGuiderType.Destination);
            Notifications.Show(NotificationType.Success, "notification_destination_set", new Dictionary<string, string> { { "address", name } });
            InstanceBehavior<UIs>.Instance.fullMenu.Toggle(show: false);
        }

        private static string Where(VehicleInstance instance, VehicleState? vehicle, bool withDriver)
        {
            if (vehicle?.Transit != null)
                return vehicle.Transit.ToStopIndex is int stop
                    ? Format("foodtrucks:bizman_fleet_where_transit", ("stop", (stop + 1).ToString()))
                    : Text("foodtrucks:bizman_fleet_where_moving");
            if (withDriver) return Text("foodtrucks:bizman_fleet_where_driver");
            var place = !string.IsNullOrEmpty(instance.streetName) ? instance.Address.ToFormattedString()
                : !string.IsNullOrEmpty(instance.parkingNeighbourhood) ? Text(instance.parkingNeighbourhood)
                : Text("foodtrucks:bizman_fleet_place_unknown");
            return Format(vehicle?.IsOpen == true ? "foodtrucks:bizman_fleet_where_open" : "foodtrucks:bizman_fleet_where_closed", ("place", place));
        }

        private static string Route(VehicleState? vehicle)
        {
            var schedule = vehicle?.Schedule;
            if (schedule == null || schedule.Stops.Count == 0) return Text("foodtrucks:bizman_fleet_route_none");
            var days = Text(StaffDialog.DaysKey(schedule.Days));
            return schedule.Stops.Count == 1
                ? Format("foodtrucks:bizman_fleet_route_stop", ("days", days))
                : Format("foodtrucks:bizman_fleet_route_stops", ("stops", schedule.Stops.Count.ToString()), ("days", days));
        }

        // Scrolls the list so the section's top shows, when the section sits in a scrolling list.
        private static void ScrollTo(Transform? target)
        {
            if (target == null || !target.gameObject.activeInHierarchy || !(target is RectTransform rect)) return;
            var scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || !scroll.vertical) return;
            Canvas.ForceUpdateCanvases();
            var content = scroll.content;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            var overflow = content.rect.height - viewport.rect.height;
            if (overflow <= 0f) return;
            var top = content.InverseTransformPoint(rect.TransformPoint(new Vector3(0f, rect.rect.yMax, 0f)));
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - (content.rect.yMax - top.y) / overflow);
        }

        // The player's food vehicles, those left with the private driver included.
        private static List<(VehicleInstance Instance, bool WithDriver)> Vehicles()
        {
            var game = SaveGameManager.Current;
            var all = new List<(VehicleInstance Instance, bool WithDriver)>();
            if (game == null) return all;
            foreach (var v in game.VehicleInstances ?? Enumerable.Empty<VehicleInstance>()) all.Add((v, false));
            foreach (var v in game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>()) all.Add((v, true));
            return all.Where(p => p.Instance != null && !string.IsNullOrEmpty(p.Instance.id) && FoodTrucksMod.KindOf(p.Instance.vehicleTypeName) != null)
                .GroupBy(p => p.Instance.id, StringComparer.Ordinal).Select(g => g.First())
                .ToList();
        }

        // Writes plain text into a label: through its localization component when it has one (so a language change
        // doesn't bring the template's key back), otherwise straight into the text component.
        private static void SetText(Transform? target, string text)
        {
            if (target == null) return;
            var localized = target.GetComponent<TextLocalizationComponent>();
            if (localized != null)
            {
                localized.SetValue(text, true);
                return;
            }
            var graphic = target.GetComponent<Graphic>();
            var property = graphic == null ? null : graphic.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.PropertyType == typeof(string)) property.SetValue(graphic, text);
        }

        // Null keeps the template's colour (each card is a fresh copy).
        private static void Colour(Transform? target, Color? colour)
        {
            var graphic = target == null ? null : target.GetComponent<Graphic>();
            if (graphic != null && colour != null) graphic.color = colour.Value;
        }

        private static void Hide(Transform card, string path)
        {
            var part = card.Find(path);
            if (part != null) part.gameObject.SetActive(false);
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static string Amount(string key, long cents) => Format(key, ("amount", CartSelling.Money(cents)));

        private static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);

        private static string Format(string key, params (string Key, string Value)[] values) =>
            LocalizorManager.GetLocalization(key, values.ToDictionary(v => v.Key, v => v.Value));
    }
}
