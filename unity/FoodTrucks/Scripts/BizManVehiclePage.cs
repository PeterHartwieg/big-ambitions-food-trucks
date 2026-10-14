#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using UI;
using UI.Components;
using UI.ItemPanel;
using UI.Smartphone.Apps.BizMan;
using UI.Smartphone.Apps.BizMan.LogisticsManagers;
using UI.Smartphone.Apps.BizMan.Warehouse;
using UnityEngine;
using UnityEngine.UI;
using Extensions;
using GameDropdown = UI.Elements.Dropdown;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // A vehicle's own page in BizMan (Peter, 11 October 2026): crew, route and partner venue, in place of the old
    // Hires dialog on the vehicle panel. Clicking a vehicle's card in the Street food section opens it; the panel's
    // Hires button opens it too. It sits in the fleet section's card list, outside BizManBusiness, so the game's
    // business page, its settings and its unsaved-changes check never see it; while it shows, the vehicle cards are
    // hidden, and Back brings the section back. The section scrolls and lays the page out like a card.
    //
    // Built from copies of the game's own BizMan parts:
    // - a crew row, the days row and the partner row: the warehouse Drivers tab's DriverStation (a label and a
    //   UI.Elements.Dropdown), one per staff slot, its dropdown listing the employees who can join;
    // - the stops: the logistics manager plan's ReorderableList with its LogisticsManagerDestinationUI row (name,
    //   remove button, dropdown), one row per stop with two time dropdowns, dragged to reorder;
    // - buttons and labels: the plan's "Show warehouse" button and its destinations label.
    // Every part is looked up when the page is first built; if any is missing, one error is logged and the vehicle
    // panel's Hires button opens the old dialog instead. Every change goes straight into the mod's save data.
    internal static class BizManVehiclePage
    {
        private static bool broken;
        private static BizManList? builtFor;
        private static Transform? page, holder;
        private static readonly List<GameObject> hiddenSiblings = new List<GameObject>();
        private static string? vehicleId;

        // The templates, copies kept inactive under `holder`, and the paths of their parts.
        private static GameObject? buttonTemplate, labelTemplate, rowTemplate, listTemplate;
        private static string buttonLabelPath = "", rowLabelPath = "", rowNamePath = "", rowEmptyPath = "", rowDropdownPath = "";
        private static string entryPath = "", entryNamePath = "", entryRemovePath = "", entryDropdownPath = "";
        private static readonly List<string> entryHiddenPaths = new List<string>();
        // Buttons in the stop row that stay but no longer do anything (1.0: the collapse button is the whole header).
        private static readonly List<string> entryInertPaths = new List<string>();

        public static bool IsOpen => page != null && page.gameObject.activeSelf;

        // ---- Opening ----

        // The vehicle panel's Hires button: BizMan opens on the vehicle's page; if the page can't be built, the old dialog.
        public static void OpenFromPanel(ItemPanelUI panel)
        {
            var cart = VehicleHelper.GetCurrentVehicle();
            if (cart == null) return;
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
            if (bizMan == null || bizMan.list == null || !Ensure(bizMan.list))
            {
                Log.Info($"Hires: the BizMan vehicle page isn't available; the old crew and route dialog opens for {cart.id}.");
                StaffDialog.Show(panel);
                return;
            }
            OpenFor(cart.id);
        }

        // Opens BizMan on the vehicle's page, from anywhere (the panel, the end of a map pick).
        public static void OpenFor(string id)
        {
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
            if (bizMan == null || bizMan.list == null || !Ensure(bizMan.list))
            {
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            bizMan.Open();
            // The list wakes over the next frame (BizManList.OnEnable loads its parts one frame on).
            HireRoutes.Later(() => Guard(() =>
            {
                BizManFleet.ShowSection(bizMan.list);
                Show(bizMan.list, id);
            }));
        }

        // A card in the fleet section was clicked.
        public static void Show(BizManList list, string id)
        {
            if (!Ensure(list) || page == null)
            {
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            vehicleId = id;
            // Every time: the section may have just been filled with fresh cards (opening from the vehicle panel).
            foreach (Transform sibling in page.parent)
            {
                if (sibling == page || !sibling.gameObject.activeSelf) continue;
                hiddenSiblings.Add(sibling.gameObject);
                sibling.gameObject.SetActive(false);
            }
            page.gameObject.SetActive(true);
            Refresh();
            Log.Info($"BizMan: vehicle page for {KindOf(id)} {id}.");
            HireRoutes.Later(() => Guard(() =>
            {
                var scroll = page != null ? page.GetComponentInParent<ScrollRect>() : null;
                if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            }));
        }

        // Back to the fleet list, or the list itself closing: the list's parts come back.
        public static void Close()
        {
            try
            {
                if (page != null) page.gameObject.SetActive(false);
                foreach (var sibling in hiddenSiblings)
                    if (sibling != null) sibling.SetActive(true);
                hiddenSiblings.Clear();
                vehicleId = null;
            }
            catch (Exception ex) { Log.Error("BizMan vehicle page, closing: " + ex); }
        }

        private static void Back()
        {
            Close();
            var list = builtFor;
            if (list != null) BizManFleet.ShowSection(list);
        }

        // On leaving the city: the page belongs to its scene's BizMan.
        public static void Reset()
        {
            page = null;
            holder = null;
            builtFor = null;
            hiddenSiblings.Clear();
            vehicleId = null;
        }

        // ---- Building ----

        // Builds the page once per BizMan list. False (with one error logged) when a part it copies is missing.
        private static bool Ensure(BizManList list)
        {
            if (page != null && builtFor == list) return true;
            if (broken) return false;
            try
            {
                Build(list);
                builtFor = list;
                return true;
            }
            catch (Exception ex)
            {
                broken = true;
                Log.Error($"BizMan vehicle page: {ex.Message} The vehicle panel's Hires button opens the old crew and route dialog instead.");
                if (page != null) Object.Destroy(page.gameObject);
                page = null;
                holder = null;
                return false;
            }
        }

        private static void Build(BizManList list)
        {
            var cards = BizManFleet.EnsureCardList(list) ?? throw new InvalidOperationException("The Street food section couldn't be built.");
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan ?? throw Missing("the BizMan app");
            var business = bizMan.business ?? throw Missing("BizMan's business page");

            // The warehouse Drivers tab's row.
            var drivers = business.GetComponentsInChildren<Drivers>(true).FirstOrDefault() ?? throw Missing("the warehouse Drivers tab");
            var station = Need(drivers.driverStationTemplate, "Drivers.driverStationTemplate");
            var slotLabel = Field<TextLocalizationComponent>(station, "slotNumberField");
            var vehicleName = Field<Component>(station, "vehicleNameField");
            var noVehicle = Field<GameObject>(station, "noVehicleAssignedObj");
            var dropdown = Field<GameDropdown>(station, "driverDropdown");

            // The logistics manager plan's parts.
            var plans = Need(business.logisticsLogisticsManagersPlanList, "BizManBusiness.logisticsLogisticsManagersPlanList");
            var plan = Need(plans.logisticsManagerPlanUI, "LogisticsManagersPlanList.logisticsManagerPlanUI");
            var destination = Field<LogisticsManagerDestinationUI>(plan, "destinationTemplate");
            var reorderable = Field<ReorderableList>(plan, "destinationsReorderableList");
            var button = Field<Button>(plan, "showWarehouseButton");
            var buttonLabel = Field<TextLocalizationComponent>(plan, "showWarehouseLabel");
            var label = Field<TextLocalizationComponent>(plan, "destinationsAmountLabel");
            var entryName = Field<TextLocalizationComponent>(destination, "nameText");
            var entryRemove = Field<Button>(destination, "removeButton");
            var entryDropdown = Field<GameDropdown>(destination, "businessTargetDropdown");
            var entryCollapse = Field<Button>(destination, "collapseButton");
            var entryArrow = Field<RectTransform>(destination, "arrowIcon");
            var productsHeader = Field<RectTransform>(destination, "productsHeader");
            var noProducts = Field<RectTransform>(destination, "noProducts");
            var productsList = Need(destination.productsList, "LogisticsManagerDestinationUI.productsList");
            if (destination.GetComponent<ReorderableListItem>() == null) throw Missing("the stop row's ReorderableListItem");
            if (destination.transform.parent != reorderable.transform) throw new InvalidOperationException("The logistics plan's destination row isn't directly in its reorderable list.");

            buttonLabelPath = Path(buttonLabel.transform, button.transform, "the Show warehouse label");
            rowLabelPath = Path(slotLabel.transform, station.transform, "the driver row's slot label");
            rowNamePath = Path(vehicleName.transform, station.transform, "the driver row's vehicle name");
            rowEmptyPath = Path(noVehicle.transform, station.transform, "the driver row's no-vehicle mark");
            rowDropdownPath = Path(dropdown.transform, station.transform, "the driver row's dropdown");
            entryPath = Path(destination.transform, reorderable.transform, "the destination row");
            entryNamePath = Path(entryName.transform, destination.transform, "the destination row's name");
            entryRemovePath = Path(entryRemove.transform, destination.transform, "the destination row's remove button");
            entryDropdownPath = Path(entryDropdown.transform, destination.transform, "the destination row's dropdown");
            // Dragging starts on the row's handle (all of the row when it has none); the page mustn't hide it.
            var handle = destination.GetComponent<ReorderableListItem>().handle;
            var handlePath = handle == null ? "" : Path(handle, destination.transform, "the stop row's drag handle");
            // The parts the page uses in each stop row; nothing that holds one of them may be hidden.
            var kept = new List<(string Path, string What)>
            {
                (entryNamePath, "name"), (entryRemovePath, "remove button"), (entryDropdownPath, "dropdown"),
            };
            if (handle != null) kept.Add((handlePath, "drag handle"));
            bool Holds(string part, string path) => part.Length == 0 || path == part || path.StartsWith(part + "/", StringComparison.Ordinal);
            entryHiddenPaths.Clear();
            entryInertPaths.Clear();
            // In 1.0 the collapse button is the row's whole header (name, remove button, dropdown and drag handle in it):
            // it stays, its button switched off; a collapse button on its own is hidden.
            var collapsePath = Path(entryCollapse.transform, destination.transform, "the destination row's collapse button");
            (kept.Any(k => Holds(collapsePath, k.Path)) ? entryInertPaths : entryHiddenPaths).Add(collapsePath);
            entryHiddenPaths.Add(Path(entryArrow, destination.transform, "the destination row's arrow"));
            entryHiddenPaths.Add(Path(productsHeader, destination.transform, "the destination row's products header"));
            entryHiddenPaths.Add(Path(noProducts, destination.transform, "the destination row's no-products note"));
            entryHiddenPaths.Add(Path(productsList.transform, destination.transform, "the destination row's products list"));
            foreach (var (keptPath, what) in kept)
                if (entryHiddenPaths.Any(p => p.Length > 0 && Holds(p, keptPath)))
                    throw new InvalidOperationException($"The stop row's {what} is inside a part the page hides.");

            // The page, in the fleet section's card list (1.0: a vertical layout in a scroll view), laid out like a card.
            var parent = cards;
            var go = new GameObject("FoodTrucks.VehiclePage", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 24);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var parentLaysOut = parent.GetComponent<LayoutGroup>() != null;
            if (!parentLaysOut) go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var holderGo = new GameObject("FoodTrucks.PageTemplates", typeof(RectTransform));
            holderGo.SetActive(false);
            holderGo.transform.SetParent(go.transform, false);
            holder = holderGo.transform;
            page = go.transform;

            buttonTemplate = Object.Instantiate(button.gameObject, holder);
            buttonTemplate.name = "FoodTrucks.PageButton";
            labelTemplate = Object.Instantiate(label.gameObject, holder);
            labelTemplate.name = "FoodTrucks.PageLabel";
            rowTemplate = Object.Instantiate(station.gameObject, holder);
            rowTemplate.name = "FoodTrucks.PageRow";
            Object.DestroyImmediate(rowTemplate.GetComponent<DriverStation>());
            listTemplate = Object.Instantiate(reorderable.gameObject, holder);
            listTemplate.name = "FoodTrucks.PageStops";
            // A drag disables the list's layout group and adds, then removes, a LayoutElement on its object. Both references
            // must be the copy's own (Instantiate remaps only references inside the copied object), or a drag on this page
            // would change the game's logistics plan; and that object must have no LayoutElement of its own for the removal
            // to take.
            var copiedList = listTemplate.GetComponent<ReorderableList>() ?? throw Missing("the copied ReorderableList");
            var listLayout = new List<string>();
            foreach (var field in new[] { "verticalLayoutGroup", "contentSizeFitter" })
            {
                var value = Traverse.Create(copiedList).Field(field).GetValue<Component>();
                if (value == null) { listLayout.Add($"{field} none"); continue; }
                var path = BizManFleet.PathBelow(value.transform, listTemplate.transform) ??
                           throw Missing($"the copied list's {field} inside the copy (it points to {PathTo(value.transform)})");
                if (field == "verticalLayoutGroup" && value.GetComponent<LayoutElement>() != null)
                    throw Missing("a list whose layout object has no LayoutElement of its own");
                listLayout.Add($"{field} at '{path}'");
            }
            var entry = listTemplate.transform.Find(entryPath) ?? throw Missing("the copied destination row");
            // The copy came with the plan's own rows, if it had been opened; the game's reset clears them off the template.
            entry.ResetTemplate();
            var ui = entry.GetComponent<LogisticsManagerDestinationUI>();
            if (ui != null) Object.DestroyImmediate(ui);
            var item = entry.GetComponent<ReorderableListItem>();
            item.list = listTemplate.GetComponent<ReorderableList>();
            foreach (var hidden in entryHiddenPaths) Hide(entry, hidden);
            foreach (var inert in entryInertPaths)
            {
                var part = Find(entry, inert).GetComponent<Button>();
                if (part == null) continue;
                part.onClick = new Button.ButtonClickedEvent();
                part.enabled = false;
            }

            // Each part is laid out by its own preferred size; a part with none keeps the height it had.
            foreach (var template in new[] { buttonTemplate, labelTemplate, rowTemplate })
                KeepHeight(template, template == buttonTemplate ? button.transform : template == labelTemplate ? label.transform : station.transform);
            KeepHeight(entry.gameObject, destination.transform);

            var scroll = parent.GetComponentInParent<ScrollRect>();
            Log.Info($"BizMan: vehicle page built from the warehouse driver row ({PathTo(station.transform)}) and the logistics plan " +
                     $"({PathTo(plan.transform)}; stop rows dragged by {(handle == null ? "the whole row" : handlePath)}; " +
                     $"collapse button {(entryInertPaths.Count > 0 ? "kept, switched off" : "hidden")} at '{collapsePath}'; the copied list's {string.Join(", ", listLayout)}); it sits in {PathTo(parent)} " +
                     $"({(parentLaysOut ? "laid out by its parent" : "sized by itself")}, {(scroll != null ? "scrolling" : "no scroll view")}).");
        }

        // A part with no layout of its own keeps the height the game gave it, and fills the page's width.
        private static void KeepHeight(GameObject copy, Transform original)
        {
            var element = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
            if (element.preferredHeight <= 0f && copy.GetComponent<LayoutGroup>() == null && original is RectTransform rect && rect.rect.height > 1f)
                element.preferredHeight = rect.rect.height;
        }

        // ---- Filling ----

        private static void RefreshLater() => HireRoutes.Later(() => Guard(Refresh));

        private static void Refresh()
        {
            if (page == null || holder == null || vehicleId == null) return;
            foreach (Transform child in page)
            {
                if (child == holder) continue;
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var id = vehicleId;
            var instance = Instance(id);
            if (state == null || rules == null || instance == null)
            {
                AddButton(Text("foodtrucks:bizman_page_back"), Back);
                AddLabel(Text("foodtrucks:bizman_page_gone"));
                return;
            }
            HireCrew.Check(state);
            var vehicle = StreetSales.Find(state, id);
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName) ?? "cart";
            var canWrite = FoodTrucksSession.CanWrite;
            var names = StreetFoodBooks.VehicleNames();
            var name = names.TryGetValue(id, out var n) ? n : id;

            AddButton(Text("foodtrucks:bizman_page_back"), Back);
            var title = AddLabel(name);
            // The title a size up (the label is TextMesh Pro, which the mod doesn't reference: by reflection).
            var graphic = title.GetComponent<Graphic>();
            var fontSize = graphic == null ? null : graphic.GetType().GetProperty("fontSize");
            if (fontSize != null && fontSize.PropertyType == typeof(float)) fontSize.SetValue(graphic, (float)fontSize.GetValue(graphic) * 1.3f);
            AddLabel(BizManFleet.Where(instance, vehicle, IsWithDriver(id)));
            if (!canWrite) AddLabel(Text("foodtrucks:bizman_page_readonly"));

            // Crew: one row per staff slot, and one for each member past it (the truck's staff went from 3 to 2 on 14 October
            // 2026), so they can be taken off. While the crew is over the limit no row offers anyone new: replacing someone
            // would take them off and then refuse the newcomer.
            var limit = HireCrew.Limit(kind);
            var crew = vehicle?.Crew ?? new List<string>();
            AddLabel(Format("foodtrucks:bizman_page_crew_header", ("count", crew.Count.ToString()), ("limit", limit.ToString())));
            var available = HireCrew.Available(state);
            for (var slot = 0; slot < Math.Max(limit, crew.Count); slot++)
            {
                var current = slot < crew.Count ? HireCrew.Find(crew[slot]) : null;
                var choices = new List<EmployeeInstance?> { null };
                if (current != null) choices.Add(current);
                if (crew.Count <= limit) choices.AddRange(available);
                var labels = choices.Select(e => e == null ? Text("foodtrucks:bizman_page_unassigned") : CrewOption(e)).ToList();
                var index = slot;
                AddRow(Format("foodtrucks:bizman_page_crew_slot", ("number", (slot + 1).ToString())), labels, current != null ? 1 : 0, canWrite,
                    chosen => ChooseCrew(id, index, choices[chosen]?.id));
            }

            // Route: days, stops, add.
            var schedule = vehicle?.Schedule;
            var count = schedule?.Stops.Count ?? 0;
            AddLabel(Format("foodtrucks:bizman_page_route_header", ("count", count.ToString()), ("max", rules.Fleet.MaxStops.ToString())));
            if (schedule != null)
            {
                var dayKeys = StaffDialog.DayChoices.Select(c => c.Key).ToList();
                var currentDays = StaffDialog.DaysKey(schedule.Days);
                if (!dayKeys.Contains(currentDays)) dayKeys.Add(currentDays);
                AddRow(Text("foodtrucks:bizman_page_days"), dayKeys.Select(Text).ToList(), dayKeys.IndexOf(currentDays), canWrite,
                    chosen => ChooseDays(id, dayKeys[chosen]));
                if (count > 1) AddLabel(Text("foodtrucks:bizman_page_route_hint"));
                // A route saved under older rules that fails today's: the crew doesn't run it, and the page says why.
                var problem = vehicle == null ? ScheduleProblem.None : new HireRunner(rules.Fleet).RouteProblem(vehicle);
                if (problem != ScheduleProblem.None) AddLabel(Text(RouteStops.ProblemKey(problem)));
                AddStops(id, schedule, canWrite);
            }
            else AddLabel(Text(StopPicker.Ready ? "foodtrucks:bizman_page_route_empty" : "foodtrucks:bizman_page_route_empty_nomap"));
            if (StopPicker.Ready)
            {
                var add = AddButton(Text("foodtrucks:bizman_page_add_stop"), () => AddOnMap(id));
                add.interactable = canWrite && count < rules.Fleet.MaxStops;
            }

            // Partner venue.
            AddLabel(Text("foodtrucks:bizman_page_partner_header"));
            AddPartner(state, id, instance, canWrite);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)page);
        }

        private static void AddStops(string id, HireSchedule schedule, bool canWrite)
        {
            var listGo = Object.Instantiate(listTemplate!, page);
            listGo.name = "FoodTrucks.Stops";
            listGo.SetActive(true);
            var reorder = listGo.GetComponent<ReorderableList>();
            var template = listGo.transform.Find(entryPath);
            for (var i = 0; i < schedule.Stops.Count; i++)
            {
                var entry = Object.Instantiate(template, listGo.transform);
                entry.name = "FoodTrucks.Stop";
                var item = entry.GetComponent<ReorderableListItem>();
                if (item != null) item.list = canWrite ? reorder : null;
                FillStop(entry, id, schedule, i, canWrite);
                entry.gameObject.SetActive(true);
            }
            if (canWrite) reorder.OnItemReordered += (from, to) => Guard(() => MoveStop(id, from, to));
            reorder.Reinitialize();
        }

        private static void FillStop(Transform entry, string id, HireSchedule schedule, int index, bool canWrite)
        {
            var stop = schedule.Stops[index];
            var latest = RouteStops.LatestClose;
            BizManFleet.SetText(entry.Find(entryNamePath), Format("foodtrucks:bizman_page_stop", ("number", (index + 1).ToString()), ("place", RouteStops.Label(stop))));

            // The opening and closing times, side by side: the row's dropdown and a copy, in a row of their own.
            var open = entry.Find(entryDropdownPath).GetComponent<GameDropdown>();
            var times = new GameObject("FoodTrucks.Times", typeof(RectTransform));
            var row = (RectTransform)times.transform;
            var openRect = (RectTransform)open.transform;
            row.SetParent(openRect.parent, false);
            row.SetSiblingIndex(openRect.GetSiblingIndex());
            row.anchorMin = openRect.anchorMin;
            row.anchorMax = openRect.anchorMax;
            row.pivot = openRect.pivot;
            row.anchoredPosition = openRect.anchoredPosition;
            row.sizeDelta = openRect.sizeDelta;
            var rowElement = times.AddComponent<LayoutElement>();
            var openElement = open.GetComponent<LayoutElement>();
            rowElement.preferredHeight = openRect.rect.height > 1f ? openRect.rect.height : openElement != null ? openElement.preferredHeight : -1f;
            rowElement.flexibleWidth = 1f;
            var horizontal = times.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 8f;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = true;
            horizontal.childForceExpandHeight = true;
            var close = Object.Instantiate(open.gameObject, row).GetComponent<GameDropdown>();
            open.transform.SetParent(row, false);
            open.transform.SetSiblingIndex(0);
            foreach (var dd in new[] { open, close })
            {
                var element = dd.GetComponent<LayoutElement>() ?? dd.gameObject.AddComponent<LayoutElement>();
                element.flexibleWidth = 1f;
            }

            var opens = RouteEditor.OpenTimes(schedule, RouteStops.Step, latest, index);
            if (!opens.Contains(stop.OpenAt)) opens.Add(stop.OpenAt);
            opens.Sort();
            SetUp(open, opens.Select(m => Format("foodtrucks:bizman_page_opens", ("time", StaffDialog.Clock(m)))).ToList(), opens.IndexOf(stop.OpenAt), canWrite,
                chosen => Retime(id, index, opens[chosen], null));
            var closes = RouteEditor.CloseTimes(schedule, stop.OpenAt, RouteStops.Step, latest, index);
            if (!closes.Contains(stop.CloseAt)) closes.Add(stop.CloseAt);
            closes.Sort();
            SetUp(close, closes.Select(m => Format("foodtrucks:bizman_page_closes", ("time", StaffDialog.Clock(m)))).ToList(), closes.IndexOf(stop.CloseAt), canWrite,
                chosen => Retime(id, index, stop.OpenAt, closes[chosen]));

            var remove = entry.Find(entryRemovePath).GetComponent<Button>();
            remove.onClick = new Button.ButtonClickedEvent();
            remove.interactable = canWrite;
            var place = RouteStops.Label(stop);
            remove.onClick.AddListener(() => Guard(() =>
            {
                var body = LanguageChangeEventDataHolder.Create("foodtrucks:bizman_page_remove_body",
                    new Dictionary<string, string> { ["number"] = (index + 1).ToString(), ["place"] = place });
                HudConfirm.Show(LanguageChangeEventDataHolder.Create("foodtrucks:bizman_page_remove_header"), body,
                    () => Guard(() => RemoveStop(id, index)), null, allowConfirmationSkip: false, isNegative: true);
            }));
        }

        private static void AddPartner(SaveState state, string id, VehicleInstance instance, bool canWrite)
        {
            var current = Partners.VenueOf(id);
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var venues = new List<string?> { null };
            var labels = new List<string> { Text("foodtrucks:bizman_page_partner_none") };
            if (current != null)
            {
                venues.Add(current);
                labels.Add(Partners.Name(current));
            }
            foreach (var venue in Partners.VenuesNear(position).Where(v => v.Address != current && Partnerships.ForVenue(state, v.Address) == null))
            {
                venues.Add(venue.Address);
                labels.Add(Format("foodtrucks:bizman_page_partner_option", ("venue", Partners.Name(venue.Address)),
                    ("kind", Text("foodtrucks:venue_" + venue.Kind.ToString().ToLowerInvariant()))));
            }
            AddRow(Text("foodtrucks:bizman_page_partner"), labels, current != null ? 1 : 0, canWrite && venues.Count > 1,
                chosen => ChoosePartner(id, venues[chosen]));
            AddLabel(Text(venues.Count > 1 ? "foodtrucks:bizman_page_partner_hint" : "foodtrucks:bizman_page_partner_none_near"));
        }

        // ---- Parts ----

        private static Button AddButton(string text, Action click)
        {
            var copy = Object.Instantiate(buttonTemplate!, page);
            copy.SetActive(true);
            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Guard(click));
            button.interactable = true;
            BizManFleet.SetText(Find(copy.transform, buttonLabelPath), text);
            return button;
        }

        private static TextLocalizationComponent AddLabel(string text)
        {
            var copy = Object.Instantiate(labelTemplate!, page);
            copy.SetActive(true);
            var label = copy.GetComponent<TextLocalizationComponent>();
            label.SetValue(text, true);
            return label;
        }

        private static void AddRow(string title, List<string> options, int selected, bool interactable, Action<int> chosen)
        {
            var row = Object.Instantiate(rowTemplate!, page).transform;
            row.gameObject.SetActive(true);
            BizManFleet.SetText(Find(row, rowLabelPath), title);
            Hide(row, rowNamePath);
            Hide(row, rowEmptyPath);
            var dropdown = Find(row, rowDropdownPath).GetComponent<GameDropdown>();
            SetUp(dropdown, options, selected, interactable, chosen);
        }

        private static void SetUp(GameDropdown dropdown, List<string> options, int selected, bool interactable, Action<int> chosen)
        {
            dropdown.SetOptions(options, localize: false, selectedOption: selected);
            dropdown.onOptionSelected.RemoveAllListeners();
            dropdown.onOptionSelected.AddListener(index => Guard(() =>
            {
                if (index >= 0 && index < options.Count && index != selected) chosen(index);
            }));
            dropdown.SetInteractable(interactable);
        }

        // ---- Changes ----

        // The crew dropdown's choice for one slot (null: Unassigned). Also the test bridge's crew command.
        internal static void ChooseCrew(string id, int slot, string? employeeId)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            var vehicle = StreetSales.GetOrAdd(state, id);
            var kind = KindOf(id);
            if (string.IsNullOrEmpty(vehicle.VehicleType)) vehicle.VehicleType = kind;
            var currentId = slot < vehicle.Crew.Count ? vehicle.Crew[slot] : null;
            if (currentId == employeeId) return;
            if (currentId != null)
            {
                var leaving = HireCrew.Find(currentId);
                HireCrew.Remove(vehicle, currentId, "unassigned on the BizMan page");
                if (leaving != null) CartSelling.Say("foodtrucks:notification_crew_letgo", kind, ("name", HireCrew.Name(leaving)));
            }
            var joining = employeeId == null ? null : HireCrew.Find(employeeId);
            if (joining != null)
            {
                if (HireCrew.Add(state, vehicle, joining))
                {
                    // The new hand takes the slot they were picked for.
                    vehicle.Crew.Remove(joining.id);
                    vehicle.Crew.Insert(Math.Min(slot, vehicle.Crew.Count), joining.id);
                    CartSelling.Say("foodtrucks:notification_crew_added", kind, ("name", HireCrew.Name(joining)));
                }
                else CartSelling.Notify("foodtrucks:notification_crew_cant_join", ("name", HireCrew.Name(joining)));
            }
            RefreshLater();
        }

        private static void ChooseDays(string id, string key)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            var choice = StaffDialog.DayChoices.FirstOrDefault(c => c.Key == key);
            var rules = DemandData.Rules;
            if (vehicle?.Schedule == null || choice.Days == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var problem = RouteEditor.SetDays(new HireSchedules(rules.Fleet), vehicle, choice.Days);
            if (problem != ScheduleProblem.None)
            {
                Log.Info($"Route: days {key} for {id} refused: {problem}.");
                CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                RefreshLater();
                return;
            }
            RouteStops.Changed(vehicle);
            Log.Info($"Route: {id} runs {(choice.Days.Length == 0 ? "every day" : string.Join(", ", choice.Days))} (BizMan page).");
            RefreshLater();
        }

        // A new opening time keeps the stop's length where it fits; a new closing time keeps its opening.
        private static void Retime(string id, int index, int open, int? close)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            var rules = DemandData.Rules;
            if (vehicle?.Schedule == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var newClose = close ?? RouteEditor.CloseFor(vehicle.Schedule, index, open, RouteStops.Step, RouteStops.LatestClose);
            var problem = newClose == null ? ScheduleProblem.Overlap : RouteEditor.SetTimes(new HireSchedules(rules.Fleet), vehicle, index, open, newClose.Value);
            if (problem != ScheduleProblem.None)
            {
                Log.Info($"Route: new hours {StaffDialog.Clock(open)}-{(newClose is int c ? StaffDialog.Clock(c) : "?")} for stop {index + 1} of {id} refused: {problem}.");
                CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
            }
            else
            {
                RouteStops.Changed(vehicle);
                Log.Info($"Route: stop {index + 1} of {id} now {StaffDialog.Clock(open)}-{StaffDialog.Clock(newClose!.Value)} (BizMan page).");
            }
            RefreshLater();
        }

        private static void MoveStop(string id, int from, int to)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            var rules = DemandData.Rules;
            if (vehicle?.Schedule == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var problem = RouteEditor.MovePlace(new HireSchedules(rules.Fleet), vehicle, from, to);
            if (problem == ScheduleProblem.None && from != to)
            {
                RouteStops.Changed(vehicle);
                Log.Info($"Route: stop {from + 1} of {id} moved to {to + 1}; the times stay in their slots (BizMan page).");
            }
            else if (problem != ScheduleProblem.None) Log.Info($"Route: moving stop {from + 1} to {to + 1} of {id} refused: {problem}.");
            RefreshLater();
        }

        private static void RemoveStop(string id, int index)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (vehicle == null) return;
            RouteStops.Remove(vehicle, index);
            RefreshLater();
        }

        private static void AddOnMap(string id)
        {
            var rules = DemandData.Rules;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (rules != null && vehicle?.Schedule != null && vehicle.Schedule.Stops.Count >= rules.Fleet.MaxStops)
            {
                CartSelling.Notify("foodtrucks:notification_route_full", ("max", rules.Fleet.MaxStops.ToString()));
                return;
            }
            StopPicker.Start(id);
        }

        private static void ChoosePartner(string id, string? venue)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            var vehicle = StreetSales.GetOrAdd(state, id);
            var kind = KindOf(id);
            if (Partners.VenueOf(id) is string current && current != venue) Partners.End(state, vehicle, kind);
            if (venue != null && Partners.VenueOf(id) == null) Partners.Sign(state, vehicle, venue, kind);
            RefreshLater();
        }

        // ---- Helpers ----

        private static string CrewOption(EmployeeInstance employee) =>
            Format("foodtrucks:bizman_page_crew_option", ("name", HireCrew.Name(employee)), ("skill", HireCrew.Skill(employee).ToString("F0")),
                ("satisfaction", employee.satisfaction.ToString("F0")), ("wage", CartSelling.Money((long)Math.Round(employee.hourlyWage * 100))));

        private static VehicleInstance? Instance(string id)
        {
            var game = SaveGameManager.Current;
            return game?.VehicleInstances.FirstOrDefault(v => v.id == id) ?? game?.privateDriverVehicleInstances?.FirstOrDefault(v => v.id == id);
        }

        private static bool IsWithDriver(string id) =>
            SaveGameManager.Current?.VehicleInstances.All(v => v.id != id) == true &&
            SaveGameManager.Current.privateDriverVehicleInstances?.Any(v => v.id == id) == true;

        private static string KindOf(string id) => FoodTrucksMod.KindOf(Instance(id)?.vehicleTypeName) ?? "cart";

        private static T Field<T>(object owner, string name) where T : class =>
            Need(Traverse.Create(owner).Field(name).GetValue<T>(), $"{owner.GetType().Name}.{name}");

        private static T Need<T>(T? value, string what) where T : class
        {
            if (value == null || (value is Object unityObject && unityObject == null)) throw Missing(what);
            return value;
        }

        private static InvalidOperationException Missing(string what) => new InvalidOperationException($"{what} isn't where 1.0 had it.");

        private static string Path(Transform target, Transform root, string what) =>
            BizManFleet.PathBelow(target, root) ?? throw new InvalidOperationException($"{what} isn't inside the part it belongs to.");

        // An empty path is the part itself.
        private static Transform Find(Transform root, string path) => path.Length == 0 ? root : root.Find(path) ?? root;

        private static void Hide(Transform root, string path)
        {
            if (path.Length == 0) return;
            var part = root.Find(path);
            if (part != null) part.gameObject.SetActive(false);
        }

        private static string PathTo(Transform target)
        {
            var parts = new List<string>();
            for (var t = target; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error("BizMan vehicle page: " + ex); }
        }

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);

        private static string Format(string key, params (string Key, string Value)[] values) =>
            LocalizorManager.GetLocalization(key, values.ToDictionary(v => v.Key, v => v.Value));
    }
}
