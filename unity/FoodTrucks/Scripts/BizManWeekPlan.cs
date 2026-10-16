#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Characters.Skills;
using Entities;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using UI;
using UI.Components;
using UI.Smartphone.Apps.BizMan;
using UI.Smartphone.Apps.BizMan.Schedule;
using Parking.UndergroundParking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GameDropdown = UI.Elements.Dropdown;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The vehicle page's two tabs and its Week plan tab (Peter, 16 October 2026; mockup build/ui-mockup/week-plan.html):
    // a vehicle is planned per weekday like a shop. Across the page: the day tiles (copies of the game's schedule day
    // button, with its open toggle and its copy, paste, paste to all, clear and auto schedule buttons on the chosen
    // tile), the crew slots, the chosen day's route (the commissary at both ends, drive rows, stop cards; a copy of the
    // logistics plan's reorderable list, as before) beside its map, and the shift grid: one row per crew member over the
    // route day's hours, shift bars to drag and resize (copies of the game's WorkShiftSlider, driven by the mod's own
    // pointer handler), the route day as a band on top and red where nobody is on shift. Every change writes the mod's
    // save at once, as the game's schedule screen applies edits without a save button. No instruction text: only real
    // problems, in red.
    internal static class BizManWeekPlan
    {
        // ---- State ----

        // Week plan or Overview, kept while the phone stays open; each vehicle's chosen weekday.
        internal static bool WeekTab { get; private set; }
        private static readonly Dictionary<string, DayOfWeek> chosen = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal);
        // The copied day, as the game's ScheduleDayButton.CopiedScheduleDay (any vehicle's).
        private static DayCopy? copied;

        // The copied parts and the paths of their pieces.
        private static BizManList? builtFor;
        private static GameObject? tabTemplate, dayTemplate, sliderTemplate;
        private static Color? tabColour;
        private static string dayLabelPath = "", dayTogglePath = "", daySelectPath = "", dayOpenPath = "", dayClosedPath = "", daySelectedPath = "";
        private static string dayCopyPath = "", dayPastePath = "", dayAutoPath = "", dayClearPath = "", dayPasteAllPath = "";
        private static string sliderNamePath = "", sliderHoursPath = "", sliderBackgroundPath = "", sliderLeftPath = "", sliderRightPath = "";

        internal static void Reset()
        {
            builtFor = null;
            tabTemplate = dayTemplate = sliderTemplate = null;
            tabColour = null;
            WeekTab = false;
            copied = null;
            chosen.Clear();
        }

        // The weekday shown for the vehicle: the one chosen last, else today.
        internal static DayOfWeek DayFor(string id) =>
            chosen.TryGetValue(id, out var day) ? day : Core.Common.GameDays.Weekday(Math.Max(1, SaveGameManager.Current?.Day ?? 1));

        // ---- The game's parts ----

        // Copies, once per BizMan list, the business page's tab title, the schedule screen's day button and its work shift
        // bar into the Street food page's template holder, taking their game logic out. Each is optional: without it the
        // page draws a plain one. One log line names what was copied.
        private static void EnsureParts()
        {
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
            var list = bizMan?.list;
            if (list == null || builtFor == list) return;
            var holder = BizManFleet.EnsurePage(list);
            if (holder == null) return;
            builtFor = list;
            tabTemplate = dayTemplate = sliderTemplate = null;
            var found = new List<string>();
            var missing = new List<string>();
            var business = bizMan!.business;
            try
            {
                var menu = business == null ? null : Traverse.Create(business).Field("menu").GetValue<Transform>();
                var tab = menu != null && menu.childCount > 0 ? menu.GetChild(0) : null;
                if (tab != null && tab.GetComponent<Button>() != null)
                {
                    tabTemplate = Object.Instantiate(tab.gameObject, holder);
                    tabTemplate.name = "FoodTrucks.Tab";
                    var graphic = tabTemplate.GetComponent<Graphic>();
                    tabColour = graphic != null ? graphic.color : (Color?)null;
                    found.Add($"the business page's tab ({PathTo(tab)})");
                }
                else missing.Add("the business page's tab menu (BizManBusiness.menu)");
            }
            catch (Exception ex) { missing.Add("the business page's tab: " + ex.Message); }

            var schedule = business == null ? null : business.bizManSchedule;
            try
            {
                var days = schedule == null ? null : Traverse.Create(schedule).Field("daySelectionController").GetValue<ScheduleDaySelectionController>();
                var source = days == null ? null : Traverse.Create(days).Field("scheduleDayButtonTemplate").GetValue<Transform>();
                if (source != null && source.GetComponent<ScheduleDayButton>() != null) TakeDayButton(source, holder, found);
                else missing.Add("the schedule screen's day button (ScheduleDaySelectionController.scheduleDayButtonTemplate)");
            }
            catch (Exception ex)
            {
                dayTemplate = null;
                missing.Add("the schedule day button: " + ex.Message);
            }
            try
            {
                var scroller = schedule == null ? null : Traverse.Create(schedule).Field("scrollerController").GetValue<ScheduleScrollerController>();
                var cell = scroller == null ? null : scroller.prefab;
                var slider = cell == null ? null : Traverse.Create(cell).Field("workShiftSliderPrefab").GetValue<WorkShiftSlider>();
                if (slider != null) TakeSlider(slider, holder, found);
                else missing.Add("the schedule screen's work shift bar (ScheduleCellView.workShiftSliderPrefab)");
            }
            catch (Exception ex)
            {
                sliderTemplate = null;
                missing.Add("the work shift bar: " + ex.Message);
            }
            Log.Info($"BizMan: week plan parts copied: {(found.Count == 0 ? "none" : string.Join(", ", found))}" +
                     (missing.Count == 0 ? "; nothing missing." : $"; missing {string.Join("; ", missing)} (plain ones drawn instead)."));
        }

        private static void TakeDayButton(Transform source, Transform holder, List<string> found)
        {
            var copy = Object.Instantiate(source.gameObject, holder);
            copy.name = "FoodTrucks.DayTile";
            var button = copy.GetComponent<ScheduleDayButton>();
            string PathOf(string field)
            {
                var value = Traverse.Create(button).Field(field).GetValue<object>();
                var transform = value is Component c ? c.transform : value is GameObject g ? g.transform : null;
                return transform == null ? "" : BizManFleet.PathBelow(transform, copy.transform) ?? "";
            }
            dayLabelPath = PathOf("dayLabel");
            if (dayLabelPath.Length == 0) dayLabelPath = PathOf("dayText");
            dayTogglePath = PathOf("openToggle");
            daySelectPath = PathOf("selectDayButton");
            dayOpenPath = PathOf("openBackground");
            dayClosedPath = PathOf("closedBackground");
            daySelectedPath = PathOf("selectedBackground");
            dayCopyPath = PathOf("copyScheduleButton");
            dayPastePath = PathOf("pasteScheduleButton");
            dayAutoPath = PathOf("autoScheduleButton");
            dayClearPath = PathOf("clearScheduleButton");
            dayPasteAllPath = PathOf("pasteToAllDaysButton");
            Object.DestroyImmediate(button);
            // Its labels keep the game's day key otherwise.
            foreach (var label in copy.GetComponentsInChildren<TextLocalizationComponent>(true)) label.Key = "";
            // The buttons' own calls went to the game's day button (persistent listeners): dropped, the page sets its own.
            foreach (var b in copy.GetComponentsInChildren<Button>(true)) b.onClick = new Button.ButtonClickedEvent();
            foreach (var t in copy.GetComponentsInChildren<Toggle>(true)) t.onValueChanged = new Toggle.ToggleEvent();
            dayTemplate = copy;
            found.Add($"the schedule day button ({PathTo(source)}; label '{dayLabelPath}', toggle '{dayTogglePath}', buttons " +
                      $"{string.Join(", ", new[] { dayCopyPath, dayPastePath, dayPasteAllPath, dayClearPath, dayAutoPath }.Select(p => p.Length == 0 ? "-" : p))})");
        }

        private static void TakeSlider(WorkShiftSlider slider, Transform holder, List<string> found)
        {
            var copy = Object.Instantiate(slider.gameObject, holder);
            copy.name = "FoodTrucks.ShiftBar";
            var own = copy.GetComponent<WorkShiftSlider>();
            string PathOf(string field)
            {
                var value = Traverse.Create(own).Field(field).GetValue<object>();
                var transform = value is Component c ? c.transform : value is GameObject g ? g.transform : null;
                return transform == null ? "" : BizManFleet.PathBelow(transform, copy.transform) ?? "";
            }
            sliderNamePath = PathOf("employeeNameLabel");
            sliderHoursPath = PathOf("hoursLabel");
            sliderBackgroundPath = PathOf("background");
            sliderLeftPath = PathOf("leftHandle");
            sliderRightPath = PathOf("rightHandle");
            foreach (var hidden in new[] { PathOf("warningObj"), PathOf("sickOverlayObj") })
                if (hidden.Length > 0 && copy.transform.Find(hidden) is Transform part) part.gameObject.SetActive(false);
            // The game's drag, handles, tooltip and the bar itself write the open building's schedule: all go.
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true).ToList())
            {
                var type = behaviour.GetType();
                if (type == typeof(WorkShiftSlider) || type == typeof(WorkShiftDrag) || type == typeof(WorkShiftSliderHandle) || type.Name == "EmployeeTooltip")
                    Object.DestroyImmediate(behaviour);
            }
            foreach (var label in copy.GetComponentsInChildren<TextLocalizationComponent>(true)) label.Key = "";
            sliderTemplate = copy;
            found.Add($"the work shift bar ({copy.GetComponentsInChildren<Graphic>(true).Length} graphics; name '{sliderNamePath}', hours '{sliderHoursPath}', handles '{sliderLeftPath}', '{sliderRightPath}')");
        }

        // ---- Tabs ----

        // Overview and Week plan, at the top of the first column, in the business page's tab style: the chosen one white,
        // the other in the game's own tab colour, a line under the chosen one.
        internal static void Tabs(Transform column)
        {
            EnsureParts();
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var unit = BizManParts.SmallLabelHeight();
            var row = Rect("FoodTrucks.Tabs", column);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = unit * 1.8f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = unit;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            foreach (var (week, key) in new[] { (false, "foodtrucks:bizman_week_tab_overview"), (true, "foodtrucks:bizman_week_tab_week") })
            {
                var selected = week == WeekTab;
                Transform tab;
                if (tabTemplate != null)
                {
                    tab = Object.Instantiate(tabTemplate, row).transform;
                    tab.gameObject.SetActive(true);
                    BizManParts.SetText(tab, Text(key));
                }
                else
                {
                    tab = BizManParts.SmallLabel(row, Text(key)) ?? Rect("Tab", row);
                    tab.gameObject.AddComponent<Button>();
                }
                tab.name = week ? "FoodTrucks.TabWeek" : "FoodTrucks.TabOverview";
                BizManParts.Colour(tab, selected ? (Color)colors.white : tabColour ?? (Color)colors.lightGrey);
                var tabElement = tab.GetComponent<LayoutElement>() ?? tab.gameObject.AddComponent<LayoutElement>();
                var text = tab.GetComponent<Graphic>();
                tabElement.preferredWidth = text is ILayoutElement layoutElement && layoutElement.preferredWidth > 1f ? layoutElement.preferredWidth + unit : unit * 6f;
                if (selected)
                {
                    var line = Rect("Underline", tab);
                    line.anchorMin = new Vector2(0f, 0f);
                    line.anchorMax = new Vector2(1f, 0f);
                    line.pivot = new Vector2(0.5f, 0f);
                    line.sizeDelta = new Vector2(0f, Mathf.Max(2f, unit * 0.12f));
                    var image = line.gameObject.AddComponent<Image>();
                    image.color = colors.white;
                    image.raycastTarget = false;
                }
                var button = tab.GetComponent<Button>();
                if (button == null) continue;
                button.onClick = new Button.ButtonClickedEvent();
                var target = week;
                button.onClick.AddListener(() => Guard(() =>
                {
                    if (WeekTab == target) return;
                    WeekTab = target;
                    Log.Info($"BizMan: vehicle page tab {(target ? "Week plan" : "Overview")}.");
                    BizManVehiclePage.RefreshLater();
                }));
            }
        }

        // ---- The Week plan tab ----

        internal static void Fill(Transform column, SaveState state, RuleTables rules, VehicleState? vehicle, VehicleInstance instance, string id, string kind,
            bool canWrite)
        {
            EnsureParts();
            var day = DayFor(id);
            var schedule = vehicle?.Schedule;
            var mode = Travel.ModeFor(kind);
            var speed = HireRoutes.GameSpeed();
            var home = vehicle == null ? null : HireRoutes.HomeOf(vehicle);
            var schedules = new HireSchedules(rules.Fleet);
            var routeDays = WeekPlan.RouteDays(schedules, schedule, mode, speed, HireRoutes.HomePoint(home), home?.BayStops);
            var crew = (vehicle?.Crew ?? new List<string>()).ToList();

            DayBar(column, rules, vehicle, id, kind, day, routeDays, crew, canWrite);
            Crew(column, state, vehicle, id, kind, canWrite);

            // The day's route beside its map.
            var pair = Rect("FoodTrucks.RouteAndMap", column);
            var pairLayout = pair.gameObject.AddComponent<HorizontalLayoutGroup>();
            pairLayout.spacing = BizManParts.SmallLabelHeight();
            pairLayout.childControlWidth = pairLayout.childControlHeight = true;
            pairLayout.childForceExpandWidth = true;
            pairLayout.childForceExpandHeight = false;
            var left = Stack("FoodTrucks.RouteColumn", pair, 3f);
            var right = Stack("FoodTrucks.MapColumn", pair, 2f);
            Route(left, rules, vehicle, instance, id, kind, day, routeDays[WeekPlan.Position(day)], canWrite);
            var map = new BizManParts.Box(right, Format("foodtrucks:bizman_week_map_title", ("day", DayName(day))));
            if (schedule != null && WeekPlan.StopsOf(schedule, day).Count > 0) VehiclePageDrawings.Route(map, schedule, day, instance, vehicle);
            else map.Paragraph(Text("foodtrucks:bizman_page_route_no_stops"));

            Shifts(column, rules, vehicle, id, kind, day, routeDays, crew, canWrite);
        }

        // ---- Day tiles ----

        private static void DayBar(Transform column, RuleTables rules, VehicleState? vehicle, string id, string kind, DayOfWeek chosenDay,
            IReadOnlyList<RouteDay> routeDays, List<string> crew, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var unit = BizManParts.SmallLabelHeight();
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_week_days_title"));
            var row = Rect("FoodTrucks.DayTiles", box.Root);
            box.Add(row);
            var element = row.gameObject.AddComponent<LayoutElement>();
            var tileHeight = dayTemplate != null && dayTemplate.transform is RectTransform t && t.rect.height > 1f ? t.rect.height : unit * 2.6f;
            element.minHeight = element.preferredHeight = tileHeight + unit * 1.2f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = unit * 0.35f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            var today = Core.Common.GameDays.Weekday(Math.Max(1, SaveGameManager.Current?.Day ?? 1));
            var schedule = vehicle?.Schedule;
            foreach (var day in WeekPlan.Days)
            {
                var routeDay = routeDays[WeekPlan.Position(day)];
                var gaps = crew.Count > 0 && !routeDay.Empty && WeekPlan.Gaps(routeDay, schedule, crew).Count > 0;
                var closed = WeekPlan.IsClosed(schedule, day);
                var hours = routeDay.Empty ? "–" : $"{Clock(routeDay.Start)}–{Clock(routeDay.End)}";
                Tile(row, day, day == chosenDay, closed, hours, gaps, day == today, id, kind, rules, vehicle, canWrite, unit, tileHeight, colors);
            }
            // Without the game's tile, the chosen day's actions as the box's own buttons.
            if (dayTemplate == null)
            {
                box.Button(Text("foodtrucks:bizman_week_copy"), () => Copy(id, chosenDay), canWrite);
                if (copied != null) box.Button(Text("foodtrucks:bizman_week_paste"), () => Paste(id, chosenDay, all: false), canWrite);
                if (copied != null && copied.From == chosenDay) box.Button(Text("foodtrucks:bizman_week_paste_all"), () => Paste(id, chosenDay, all: true), canWrite);
                box.Button(Text("foodtrucks:bizman_week_clear"), () => Clear(id, chosenDay), canWrite);
                box.Button(Text("foodtrucks:bizman_week_auto_day"), () => Auto(id, kind, chosenDay), canWrite && crew.Count > 0);
            }
        }

        private static void Tile(Transform row, DayOfWeek day, bool selected, bool closed, string hours, bool gaps, bool today, string id, string kind,
            RuleTables rules, VehicleState? vehicle, bool canWrite, float unit, float tileHeight, Colors colors)
        {
            var name = DayName(day);
            Transform tile;
            Button? select;
            Toggle? open = null;
            if (dayTemplate != null)
            {
                tile = Object.Instantiate(dayTemplate, row).transform;
                tile.gameObject.SetActive(true);
                BizManParts.SetText(Part(tile, dayLabelPath), name);
                BizManParts.Colour(Part(tile, dayLabelPath), selected ? (Color)colors.black : (Color)colors.white);
                Show(tile, daySelectedPath, selected);
                Show(tile, dayOpenPath, !selected && !closed);
                Show(tile, dayClosedPath, !selected && closed);
                select = Part(tile, daySelectPath)?.GetComponent<Button>() ?? tile.GetComponent<Button>();
                open = Part(tile, dayTogglePath)?.GetComponent<Toggle>();
                // The chosen tile's own day buttons, as the game's (copy, paste, paste to all days, clear, auto schedule).
                var hasCrew = vehicle?.Crew?.Count > 0;
                TileButton(tile, dayCopyPath, selected && !closed, canWrite, () => Copy(id, day));
                TileButton(tile, dayPastePath, copied != null && !closed, canWrite, () => Paste(id, day, all: false));
                TileButton(tile, dayPasteAllPath, selected && copied != null && copied.From == day, canWrite, () => Paste(id, day, all: true));
                TileButton(tile, dayClearPath, selected, canWrite, () => Clear(id, day));
                TileButton(tile, dayAutoPath, selected && !closed && hasCrew, canWrite, () => Auto(id, kind, day));
            }
            else
            {
                tile = Rect("FoodTrucks.DayTile", row);
                var background = tile.gameObject.AddComponent<Image>();
                background.color = selected ? (Color)colors.white : closed ? new Color(0.23f, 0.26f, 0.30f, 0.9f) : new Color(0.11f, 0.13f, 0.17f, 0.9f);
                select = tile.gameObject.AddComponent<Button>();
                if (BizManParts.SmallLabel(tile, name) is RectTransform label)
                {
                    Place(label, 0.08f, 0.5f, 1f, 1f);
                    BizManParts.Colour(label, selected ? (Color)colors.black : (Color)colors.white);
                }
            }
            var tileElement = tile.GetComponent<LayoutElement>() ?? tile.gameObject.AddComponent<LayoutElement>();
            tileElement.minWidth = 0f;
            tileElement.preferredWidth = unit * 5f;
            tileElement.flexibleWidth = 1f;
            tileElement.minHeight = tileElement.preferredHeight = tileHeight;
            tile.name = "FoodTrucks.Day." + day;

            // The route day's hours, "Today", and a red dot when part of the day has nobody on shift.
            var lower = Rect("FoodTrucks.DayHours", tile);
            Place(lower, 0.08f, 0f, 1f, 0.45f);
            if (BizManParts.SmallLabel(lower, hours) is RectTransform hoursLabel)
            {
                Place(hoursLabel, 0f, 0f, 1f, 1f);
                BizManParts.Colour(hoursLabel, selected ? (Color)colors.darkGrey : (Color)colors.lightGrey);
            }
            if (today && BizManParts.SmallLabel(tile, Text("foodtrucks:bizman_week_today")) is RectTransform todayLabel)
            {
                Place(todayLabel, 0.55f, 0.55f, 0.9f, 1f);
                BizManParts.Colour(todayLabel, selected ? (Color)colors.green : (Color)colors.lime);
            }
            if (gaps)
            {
                var dot = Rect("FoodTrucks.Gap", tile);
                dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(1f, 1f);
                dot.anchoredPosition = new Vector2(-unit * 0.3f, -unit * 0.3f);
                dot.sizeDelta = new Vector2(unit * 0.4f, unit * 0.4f);
                var image = dot.gameObject.AddComponent<Image>();
                image.sprite = VehiclePageDrawings.Circle();
                image.color = selected ? (Color)colors.red : (Color)colors.lightRed;
                image.raycastTarget = false;
            }

            if (select != null)
            {
                select.onClick = new Button.ButtonClickedEvent();
                select.onClick.AddListener(() => Guard(() =>
                {
                    chosen[id] = day;
                    BizManVehiclePage.RefreshLater();
                }));
            }
            if (open != null)
            {
                open.onValueChanged = new Toggle.ToggleEvent();
                open.SetIsOnWithoutNotify(!closed);
                open.interactable = canWrite;
                open.onValueChanged.AddListener(isOn => Guard(() => SetClosed(id, day, !isOn, rules)));
            }
        }

        private static void TileButton(Transform tile, string path, bool visible, bool canWrite, System.Action click)
        {
            var part = Part(tile, path);
            if (part == null) return;
            part.gameObject.SetActive(visible);
            var button = part.GetComponent<Button>();
            if (button == null) return;
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = canWrite;
            button.onClick.AddListener(() => Guard(click));
        }

        // ---- Crew ----

        // One row per staff slot, and one for each member past it (the truck's staff went from 3 to 2 on 14 October 2026),
        // so they can be taken off. While the crew is over the limit no row offers anyone new.
        private static void Crew(Transform column, SaveState state, VehicleState? vehicle, string id, string kind, bool canWrite)
        {
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_crew_title"));
            var limit = HireCrew.Limit(kind);
            var crew = vehicle?.Crew ?? new List<string>();
            box.Section(Format("foodtrucks:bizman_page_crew_header", ("count", crew.Count.ToString()), ("limit", limit.ToString())));
            var available = HireCrew.Available(state);
            for (var slot = 0; slot < Math.Max(limit, crew.Count); slot++)
            {
                var current = slot < crew.Count ? HireCrew.Find(crew[slot]) : null;
                var choices = new List<EmployeeInstance?> { null };
                if (current != null) choices.Add(current);
                if (crew.Count <= limit) choices.AddRange(available);
                var labels = choices.Select(e => e == null ? Text("foodtrucks:bizman_page_unassigned") : BizManVehiclePage.CrewOption(e)).ToList();
                var index = slot;
                BizManVehiclePage.AddRow(box, Format("foodtrucks:bizman_page_crew_slot", ("number", (slot + 1).ToString())), labels, current != null ? 1 : 0, canWrite,
                    picked => BizManVehiclePage.ChooseCrew(id, index, choices[picked]?.id));
            }
        }

        // ---- The day's route ----

        private static void Route(Transform column, RuleTables rules, VehicleState? vehicle, VehicleInstance instance, string id, string kind, DayOfWeek day,
            RouteDay routeDay, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var schedule = vehicle?.Schedule;
            var stops = WeekPlan.StopsOf(schedule, day);
            var box = new BizManParts.Box(column, Format("foodtrucks:bizman_week_route_title", ("day", DayName(day)),
                ("count", stops.Count.ToString()), ("max", rules.Fleet.MaxStops.ToString())));
            if (WeekPlan.IsClosed(schedule, day)) box.Paragraph(Text("foodtrucks:bizman_week_closed"));
            if (vehicle != null && stops.Count > 0)
            {
                // What keeps the route from running as planned: a route that fails the rules, the crew, the permit.
                var home = HireRoutes.HomeOf(vehicle);
                foreach (var problem in new HireRunner(rules.Fleet).RouteProblems(vehicle, HireRoutes.GameSpeed(), HireRoutes.HomePoint(home), home?.BayStops))
                    box.Paragraph(Text(RouteStops.ProblemKey(problem)), (Color)colors.red);
                var limit = HireCrew.Limit(kind);
                var hands = vehicle.Crew.Count(c => HireCrew.Find(c) != null);
                if (hands == 0) box.Paragraph(Text("foodtrucks:bizman_page_route_no_crew"), (Color)colors.red);
                else if (hands < limit)
                    box.Paragraph(Format("foodtrucks:bizman_page_route_short_crew", ("count", hands.ToString()), ("limit", limit.ToString())), (Color)colors.red);
                if (!vehicle.HasPermit) box.Paragraph(Text("foodtrucks:bizman_page_route_no_permit"), (Color)colors.red);
                AddStops(box, id, schedule!, day, routeDay, canWrite);
            }
            else box.Paragraph(Text("foodtrucks:bizman_page_route_no_stops"));
            var full = stops.Count >= rules.Fleet.MaxStops;
            if (StopPicker.Ready) box.Button(Text("foodtrucks:bizman_page_add_stop"), () => AddOnMap(id, day), canWrite && !full);
            box.Button(Text("foodtrucks:bizman_page_add_here"), () => AddWhereItStands(id, day), canWrite && !full);
        }

        // The day from the bay where it rents its space, through the day's stops, and back (Peter, 16 October 2026): fixed
        // rows that aren't stops (no ReorderableListItem, so the list's drag leaves them out; the page is rebuilt after a
        // drag, putting them back in place), with the drive (or walk) between each two. The times come from the route day
        // (Core WeekPlan.RouteDayOf), so the page and the crew agree.
        private static void AddStops(BizManParts.Box box, string id, HireSchedule schedule, DayOfWeek day, RouteDay routeDay, bool canWrite)
        {
            var listGo = Object.Instantiate(BizManVehiclePage.listTemplate!, box.Root);
            listGo.name = "FoodTrucks.Stops";
            box.Add(listGo.transform);
            listGo.SetActive(true);
            var reorder = listGo.GetComponent<ReorderableList>();
            var template = listGo.transform.Find(BizManVehiclePage.entryPath);
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            var fleet = DemandData.Rules!.Fleet;
            var mode = Travel.ModeFor(vehicle?.VehicleType);
            var speed = HireRoutes.GameSpeed();
            var home = vehicle == null ? null : HireRoutes.HomeOf(vehicle);
            var homeName = home == null ? null : HomeName(home.Value.SiteId);
            int Minutes(double fromX, double fromZ, double toX, double toZ) => Travel.Between(fleet, fromX, fromZ, toX, toZ, mode, speed);
            var indices = WeekPlan.StopsOf(schedule, day);
            var stops = indices.Select(i => schedule.Stops[i]).ToList();
            var legs = routeDay.Plan.Legs;
            var drives = new List<int>();
            RectTransform first;
            if (home != null)
            {
                var place = home.Value.Place;
                var drive = Minutes(place.X, place.Z, stops[0].X, stops[0].Z);
                var restock = legs.FirstOrDefault(l => l.Kind == LegKind.Restock);
                var leave = legs.FirstOrDefault(l => l.Kind == LegKind.Travel);
                var text = restock.End > restock.Start
                    ? Format("foodtrucks:bizman_week_route_restock", ("commissary", homeName!), ("start", Clock((int)restock.Start)),
                        ("time", Clock((int)restock.End)))
                    : Format("foodtrucks:bizman_page_route_leaves", ("commissary", homeName!), ("time", Clock(leave.End > leave.Start ? (int)leave.Start : stops[0].OpenAt - drive)));
                first = FixedRow(listGo.transform, text, bold: true);
                FixedRow(listGo.transform, TravelText(mode, drive), bold: false);
                drives.Add(drive);
            }
            else first = FixedRow(listGo.transform, Text("foodtrucks:bizman_page_route_no_space"), bold: true);
            // "Opens" and "Closes" on that first line, over the stops' two time dropdowns once the list is laid out.
            Captions(first);
            HireRoutes.Later(() => Guard(() => AlignHeader(first, listGo.transform)));
            for (var k = 0; k < stops.Count; k++)
            {
                if (k > 0)
                {
                    var drive = Minutes(stops[k - 1].X, stops[k - 1].Z, stops[k].X, stops[k].Z);
                    FixedRow(listGo.transform, TravelText(mode, drive), bold: false);
                    drives.Add(drive);
                }
                var entry = Object.Instantiate(template, listGo.transform);
                entry.name = "FoodTrucks.Stop";
                var item = entry.GetComponent<ReorderableListItem>();
                if (item != null) item.list = canWrite ? reorder : null;
                FillStop(entry, id, schedule, indices[k], k, canWrite);
                entry.gameObject.SetActive(true);
            }
            if (home != null)
            {
                var last = stops[stops.Count - 1];
                var back = legs.LastOrDefault(l => l.StopIndex == HireSchedules.HomeIndex);
                if (home.Value.BayStops.Contains(indices[indices.Count - 1]))
                    FixedRow(listGo.transform, Format("foodtrucks:bizman_page_route_ends_in_bay", ("commissary", homeName!)), bold: true);
                else
                {
                    var place = home.Value.Place;
                    var minutes = Minutes(last.X, last.Z, place.X, place.Z);
                    drives.Add(minutes);
                    var arrive = back.End > back.Start ? (int)back.End : last.CloseAt + minutes;
                    FixedRow(listGo.transform, Format(mode == TravelMode.Push ? "foodtrucks:bizman_page_route_walk_back" : "foodtrucks:bizman_page_route_drive_back",
                        ("minutes", minutes.ToString())), bold: false);
                    FixedRow(listGo.transform, Format("foodtrucks:bizman_page_route_back", ("commissary", homeName!), ("time", Clock(arrive))), bold: true);
                    // Back after the 04:00 check: it isn't in the bay then (no refusal; the check sees it on its way).
                    var check = DemandData.Rules?.Commissary.NightCheckMinute ?? 240;
                    if (arrive > 1440 + check)
                    {
                        Log.Info($"Route: {vehicle!.VehicleType} {id} gets back to its bay at {Clock(arrive)} on {day}, after the {Clock(check)} check.");
                        FixedRow(listGo.transform, Format("foodtrucks:bizman_page_route_back_late", ("time", Clock(check))), bold: true);
                    }
                }
            }
            // The list's positions are the day's stops in order.
            if (canWrite) reorder.OnItemReordered += (from, to) => Guard(() =>
            {
                if (from >= 0 && from < indices.Count && to >= 0 && to < indices.Count) MoveStop(id, indices[from], indices[to]);
            });
            reorder.Reinitialize();
            Log.Info($"BizMan: {day}'s route rows for {id}: {(mode == TravelMode.Push ? "walks" : "drives")} {string.Join(", ", drives)} min at game speed {speed:0.##}" +
                     (home == null ? ", no commissary space." : $", from and back to {home.Value.SiteId}."));
        }

        private static string TravelText(TravelMode mode, int minutes) =>
            Format(mode == TravelMode.Push ? "foodtrucks:bizman_page_route_walk" : "foodtrucks:bizman_page_route_drive", ("minutes", minutes.ToString()));

        private static string Clock(int minute) => StaffDialog.Clock(((minute % 1440) + 1440) % 1440);

        private static string HomeName(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            return site == null ? siteId : BizManVehiclePage.ShortName(site);
        }

        // A slim line between the stop cards, in the box's section-label style: grey for a drive with the vehicle icon
        // before it, dark for the commissary at either end. Not a stop: it can't be dragged or removed.
        private static RectTransform FixedRow(Transform list, string text, bool bold)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var unit = BizManParts.SmallLabelHeight();
            var row = Rect(bold ? "FoodTrucks.CommissaryRow" : "FoodTrucks.TravelRow", list);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = unit * 1.2f;
            element.flexibleWidth = 1f;
            var indent = 0f;
            if (!bold)
            {
                var icon = Rect("Icon", row);
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(unit * 0.8f, unit * 0.8f);
                icon.anchoredPosition = new Vector2(unit * 0.5f, 0f);
                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = InstanceBehavior<GlobalReferences>.Instance.vehiclePOIIcon;
                image.preserveAspect = true;
                image.color = colors.darkGrey;
                image.raycastTarget = false;
                indent = unit * 1.6f;
            }
            if (BizManParts.SmallLabel(row, text) is RectTransform label)
            {
                label.name = "FoodTrucks.RowText";
                var labelElement = label.GetComponent<LayoutElement>();
                if (labelElement != null) labelElement.ignoreLayout = true;
                label.anchorMin = Vector2.zero;
                label.anchorMax = Vector2.one;
                label.offsetMin = new Vector2(indent, 0f);
                label.offsetMax = Vector2.zero;
                BizManParts.Colour(label, bold ? (Color)colors.midnight : (Color)colors.darkGrey);
            }
            return row;
        }

        // "Opens" and "Closes" on the first fixed row, in the box's section-label style: until the list is laid out on the
        // right half of the row; AlignHeader then puts each over its dropdown column and ends the row's own text before them.
        private static void Captions(RectTransform row)
        {
            var opens = BizManParts.SmallLabel(row, Text("foodtrucks:bizman_page_opens_label")) as RectTransform;
            var closes = BizManParts.SmallLabel(row, Text("foodtrucks:bizman_page_closes_label")) as RectTransform;
            if (opens == null || closes == null) return;
            foreach (var (label, from, to) in new[] { (opens, 0.5f, 0.75f), (closes, 0.75f, 1f) })
            {
                label.name = label == opens ? "Opens" : "Closes";
                var element = label.GetComponent<LayoutElement>();
                if (element != null) element.ignoreLayout = true;
                label.anchorMin = new Vector2(from, 0f);
                label.anchorMax = new Vector2(to, 1f);
                label.offsetMin = label.offsetMax = Vector2.zero;
            }
            if (row.Find("FoodTrucks.RowText") is RectTransform text) text.anchorMax = new Vector2(0.5f, 1f);
        }

        private static void AlignHeader(RectTransform header, Transform list)
        {
            if (header == null || list == null) return;
            var times = list.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "FoodTrucks.Times");
            if (times == null || times.childCount < 2) return;
            Canvas.ForceUpdateCanvases();
            var rect = header.rect;
            for (var i = 0; i < 2; i++)
            {
                var label = header.Find(i == 0 ? "Opens" : "Closes") as RectTransform;
                var column = times.GetChild(i) as RectTransform;
                if (label == null || column == null) continue;
                var corners = new Vector3[4];
                column.GetWorldCorners(corners);
                var left = header.InverseTransformPoint(corners[0]).x;
                var right = header.InverseTransformPoint(corners[2]).x;
                if (rect.width <= 1f || right <= left) continue;
                label.anchorMin = new Vector2(Mathf.Clamp01((left - rect.xMin) / rect.width), 0f);
                label.anchorMax = new Vector2(Mathf.Clamp01((right - rect.xMin) / rect.width), 1f);
                label.offsetMin = label.offsetMax = Vector2.zero;
                if (i == 0 && header.Find("FoodTrucks.RowText") is RectTransform text) text.anchorMax = new Vector2(label.anchorMin.x, 1f);
            }
        }

        // One stop card: its number on the day and place, the opening and closing time dropdowns, remove.
        private static void FillStop(Transform entry, string id, HireSchedule schedule, int index, int number, bool canWrite)
        {
            var stop = schedule.Stops[index];
            var latest = RouteStops.LatestClose;
            BizManParts.SetText(entry.Find(BizManVehiclePage.entryNamePath), Format("foodtrucks:bizman_page_stop", ("number", (number + 1).ToString()), ("place", RouteStops.Label(stop))));

            // The opening and closing times, side by side: the row's dropdown and a copy, in a row of their own.
            var open = entry.Find(BizManVehiclePage.entryDropdownPath).GetComponent<GameDropdown>();
            var times = Rect("FoodTrucks.Times", ((RectTransform)open.transform).parent);
            var openRect = (RectTransform)open.transform;
            times.SetSiblingIndex(openRect.GetSiblingIndex());
            times.anchorMin = openRect.anchorMin;
            times.anchorMax = openRect.anchorMax;
            times.pivot = openRect.pivot;
            times.anchoredPosition = openRect.anchoredPosition;
            times.sizeDelta = openRect.sizeDelta;
            var rowElement = times.gameObject.AddComponent<LayoutElement>();
            var openElement = open.GetComponent<LayoutElement>();
            rowElement.preferredHeight = openRect.rect.height > 1f ? openRect.rect.height : openElement != null ? openElement.preferredHeight : -1f;
            rowElement.flexibleWidth = 1f;
            var horizontal = times.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 8f;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = true;
            horizontal.childForceExpandHeight = true;
            var close = Object.Instantiate(open.gameObject, times).GetComponent<GameDropdown>();
            open.transform.SetParent(times, false);
            open.transform.SetSiblingIndex(0);
            foreach (var dd in new[] { open, close })
            {
                if (dd.GetComponent<LayoutElement>() == null) dd.gameObject.AddComponent<LayoutElement>();
                foreach (var element in dd.GetComponents<LayoutElement>())
                {
                    element.minWidth = element.preferredWidth = 0f;
                    element.flexibleWidth = 1f;
                }
                Compact(dd.transform);
            }

            // Only times the drive from the stop before allows, as the crew's plan counts it.
            var fleet = DemandData.Rules!.Fleet;
            var mode = Travel.ModeFor(StreetSales.Find(FoodTrucksSession.CurrentState, id)?.VehicleType);
            var speed = HireRoutes.GameSpeed();
            var opens = RouteEditor.OpenTimes(schedule, stop.Day, RouteStops.Step, latest, index,
                from => RouteEditor.TravelMinutes(fleet, from, stop, mode, speed));
            if (!opens.Contains(stop.OpenAt)) opens.Add(stop.OpenAt);
            opens.Sort();
            BizManVehiclePage.SetUp(open, opens.Select(StaffDialog.Clock).ToList(), opens.IndexOf(stop.OpenAt), canWrite,
                picked => Retime(id, index, opens[picked], null));
            var closes = RouteEditor.CloseTimes(schedule, stop.Day, stop.OpenAt, RouteStops.Step, latest, index);
            if (!closes.Contains(stop.CloseAt)) closes.Add(stop.CloseAt);
            closes.Sort();
            BizManVehiclePage.SetUp(close, closes.Select(StaffDialog.Clock).ToList(), closes.IndexOf(stop.CloseAt), canWrite,
                picked => Retime(id, index, stop.OpenAt, closes[picked]));

            var remove = entry.Find(BizManVehiclePage.entryRemovePath).GetComponent<Button>();
            remove.onClick = new Button.ButtonClickedEvent();
            remove.interactable = canWrite;
            var place = RouteStops.Label(stop);
            remove.onClick.AddListener(() => Guard(() =>
            {
                var body = LanguageChangeEventDataHolder.Create("foodtrucks:bizman_page_remove_body",
                    new Dictionary<string, string> { ["number"] = (number + 1).ToString(), ["place"] = place });
                HudConfirm.Show(LanguageChangeEventDataHolder.Create("foodtrucks:bizman_page_remove_header"), body,
                    () => Guard(() => RemoveStop(id, index)), null, allowConfirmationSkip: false, isNegative: true);
            }));
        }

        // A stop's time dropdown without the game's ‹ › step buttons: two of them side by side left the time itself no
        // room (Peter, 16 October 2026: "What are the arrows for?"). The middle box takes the whole header.
        private static void Compact(Transform dropdown)
        {
            var header = dropdown.Find("Header");
            if (header == null) return;
            foreach (var side in new[] { "LeftButton", "RightButton" })
            {
                var button = header.Find(side);
                if (button != null) button.gameObject.SetActive(false);
            }
            if (header.Find("MiddleBox") is RectTransform middle)
            {
                middle.anchorMin = new Vector2(0f, middle.anchorMin.y);
                middle.anchorMax = new Vector2(1f, middle.anchorMax.y);
                middle.offsetMin = new Vector2(0f, middle.offsetMin.y);
                middle.offsetMax = new Vector2(0f, middle.offsetMax.y);
            }
        }

        // ---- Shifts ----

        // The shift grid for the chosen day: hours across, from an hour before the route day to an hour after it (06:00 to
        // 22:00 on a day without a route), hours after midnight belonging to the next weekday; the route day as a band on
        // top; one row per crew member with their shift bars and their week against what they want; who is on shift, red
        // where nobody is; where the vehicle waits closed; Auto schedule for the week.
        private static void Shifts(Transform column, RuleTables rules, VehicleState? vehicle, string id, string kind, DayOfWeek day,
            IReadOnlyList<RouteDay> routeDays, List<string> crew, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var unit = BizManParts.SmallLabelHeight();
            var schedule = vehicle?.Schedule;
            var routeDay = routeDays[WeekPlan.Position(day)];
            var box = new BizManParts.Box(column, Format("foodtrucks:bizman_week_shifts_title", ("day", DayName(day))));
            if (crew.Count == 0)
            {
                box.Paragraph(Text("foodtrucks:bizman_week_no_crew"));
                return;
            }
            var next = WeekPlan.Next(day);
            var mine = (schedule?.CrewShifts ?? new List<CrewShift>()).Where(s => crew.Contains(s.EmployeeId)).ToList();
            var (h0, h1) = HourRange(routeDay, mine.Where(s => s.Day == day), mine.Where(s => s.Day == next));
            var span = h1 - h0;
            var rowHeight = unit * 2.4f;
            var bandHeight = unit * 1.3f;
            const float labelWidth = 0.24f;

            var grid = Rect("FoodTrucks.ShiftGrid", box.Root);
            box.Add(grid);
            var top = 0f;
            RectTransform Line(string name, float height)
            {
                var line = Rect(name, grid);
                line.anchorMin = new Vector2(0f, 1f);
                line.anchorMax = new Vector2(1f, 1f);
                line.pivot = new Vector2(0.5f, 1f);
                line.anchoredPosition = new Vector2(0f, -top);
                line.sizeDelta = new Vector2(0f, height);
                top += height + unit * 0.2f;
                return line;
            }
            RectTransform Track(RectTransform line)
            {
                var track = Rect("Track", line);
                Place(track, labelWidth, 0f, 1f, 1f);
                return track;
            }
            float X(double hour) => (float)((hour - h0) / span);

            // Hour labels.
            var hoursLine = Track(Line("FoodTrucks.Hours", unit));
            for (var h = h0; h < h1; h++)
                if (BizManParts.SmallLabel(hoursLine, $"{h % 24:D2}") is RectTransform label)
                {
                    Place(label, X(h), 0f, X(h + 1), 1f);
                    BizManParts.Colour(label, h >= 24 ? (Color)colors.blue : (Color)colors.darkGrey);
                }

            // The route day: restock, drives, stops (numbered as on the route), the drive back.
            var bandLine = Line("FoodTrucks.Band", bandHeight);
            Caption(bandLine, Text("foodtrucks:bizman_week_band"), colors.darkGrey, labelWidth);
            var band = Track(bandLine);
            var dayStops = WeekPlan.StopsOf(schedule, day);
            foreach (var leg in routeDay.Plan.Legs)
            {
                var a = Math.Max(leg.Start / 60.0, h0);
                var b = Math.Min(leg.End / 60.0, h1);
                if (b <= a) continue;
                var block = Rect("Leg", band);
                Place(block, X(a), 0.1f, X(b), 0.9f);
                var image = block.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.color = leg.Kind == LegKind.Open ? new Color(0.30f, 0.69f, 0.31f) : leg.Kind == LegKind.Restock ? new Color(0.56f, 0.60f, 0.64f)
                    : new Color(0.61f, 0.76f, 0.93f);
                if (leg.Kind == LegKind.Open && BizManParts.SmallLabel(block, (dayStops.IndexOf(leg.StopIndex) + 1).ToString()) is RectTransform number)
                {
                    Place(number, 0f, 0f, 1f, 1f);
                    BizManParts.Colour(number, Color.white);
                    BizManParts.Centre(number);
                }
            }

            // A row per crew member.
            foreach (var employeeId in crew)
            {
                var employee = HireCrew.Find(employeeId);
                var wish = employee == null ? new CrewWish(employeeId) : HireCrew.Wish(employee);
                var week = WeekPlan.WeekHours(schedule, employeeId);
                var line = Line("FoodTrucks.Crew." + employeeId, rowHeight);
                var name = employee == null ? employeeId : HireCrew.Name(employee);
                var labels = Rect("Label", line);
                Place(labels, 0f, 0f, labelWidth - 0.01f, 1f);
                if (BizManParts.SmallLabel(labels, name) is RectTransform nameLabel)
                {
                    Place(nameLabel, 0f, 0.5f, 1f, 1f);
                    BizManParts.Colour(nameLabel, colors.midnight);
                }
                var weekText = Format(wish.MinHours == null && wish.MaxHours == null ? "foodtrucks:bizman_week_hours" : "foodtrucks:bizman_week_hours_wanted",
                    ("hours", week.ToString()), ("min", (wish.MinHours ?? 0).ToString()), ("max", (wish.MaxHours ?? 0).ToString()),
                    ("wage", employee == null ? "-" : CartSelling.Money((long)Math.Round(employee.hourlyWage * 100))),
                    ("satisfaction", employee == null ? "-" : employee.satisfaction.ToString("F0")));
                if (BizManParts.SmallLabel(labels, weekText) is RectTransform weekLabel)
                {
                    Place(weekLabel, 0f, 0f, 1f, 0.5f);
                    BizManParts.Colour(weekLabel, wish.Fulfilled(week) ? (Color)colors.darkGrey : (Color)colors.red);
                }
                var track = Track(line);
                // Hour cells: light inside the route day, grey outside (still open to a shift, paid); a click adds a shift.
                for (var h = h0; h < h1; h++)
                {
                    var cell = Rect("Hour", track);
                    Place(cell, X(h), 0.06f, X(h + 1), 0.94f);
                    var image = cell.gameObject.AddComponent<Image>();
                    var inside = !routeDay.Empty && h * 60 < routeDay.End && (h + 1) * 60 > routeDay.Start;
                    image.color = inside ? new Color(0.95f, 0.96f, 0.97f) : new Color(0.86f, 0.88f, 0.91f);
                    if (h >= 24) image.color *= new Color(0.94f, 0.96f, 1f);
                    var hour = h;
                    if (!canWrite) continue;
                    var button = cell.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => Guard(() => AddShift(id, employeeId, hour >= 24 ? next : day, hour % 24, routeDay)));
                }
                // The member's bars: today's shift, and the next day's early one when the grid runs past midnight.
                foreach (var (shift, offset) in mine.Where(s => s.EmployeeId == employeeId && (s.Day == day || (s.Day == next && h1 > 24)))
                             .Select(s => (s, s.Day == day ? 0 : 24)))
                {
                    var a = Math.Max(shift.Start + offset, h0);
                    var b = Math.Min(shift.End + offset, h1);
                    if (b <= a) continue;
                    Bar(track, shift, offset, a, b, h0, span, employee, canWrite, (start, end) => SetShift(id, employeeId, shift.Day, start - offset, end - offset),
                        () => RemoveShift(id, employeeId, shift.Day), colors, unit);
                }
            }

            // Who is on shift: red where the route day has nobody.
            var gaps = routeDay.Empty ? new List<CoverageGap>() : WeekPlan.Gaps(routeDay, schedule, crew).ToList();
            var cover = Line("FoodTrucks.OnShift", unit);
            Caption(cover, Text("foodtrucks:bizman_week_on_shift"), colors.darkGrey, labelWidth);
            var coverTrack = Track(cover);
            if (!routeDay.Empty)
            {
                var day0 = Rect("RouteDay", coverTrack);
                Place(day0, X(Math.Max(routeDay.Start / 60.0, h0)), 0.35f, X(Math.Min(routeDay.End / 60.0, h1)), 0.65f);
                var image = day0.gameObject.AddComponent<Image>();
                image.color = new Color(0.30f, 0.69f, 0.31f, 0.6f);
                image.raycastTarget = false;
            }
            foreach (var gap in gaps)
            {
                var a = Math.Max(gap.Start / 60.0, h0);
                var b = Math.Min(gap.End / 60.0, h1);
                if (b <= a) continue;
                var block = Rect("Gap", coverTrack);
                Place(block, X(a), 0.1f, X(b), 0.9f);
                var image = block.gameObject.AddComponent<Image>();
                image.color = colors.red;
                image.raycastTarget = false;
            }
            var element = grid.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = top;
            element.flexibleWidth = 1f;

            foreach (var gap in gaps) box.Paragraph(GapText(gap, schedule, day), (Color)colors.red);
            var off = crew.Where(c => HireCrew.Find(c) is EmployeeInstance e && !HireCrew.Wish(e).Fulfilled(WeekPlan.WeekHours(schedule, c))).ToList();
            foreach (var c in off)
            {
                var e = HireCrew.Find(c)!;
                var wish = HireCrew.Wish(e);
                box.Paragraph(Format("foodtrucks:bizman_week_hours_off", ("name", HireCrew.Name(e)), ("hours", WeekPlan.WeekHours(schedule, c).ToString()),
                    ("min", (wish.MinHours ?? 0).ToString()), ("max", (wish.MaxHours ?? 0).ToString())), (Color)colors.red);
            }
            box.Button(Text("foodtrucks:bizman_week_auto_week"), () => Guard(() =>
                HudConfirm.Show(headerKey: "foodtrucks:bizman_week_auto_week", bodyKey: "foodtrucks:bizman_week_auto_week_confirm",
                    onConfirmAction: () => Guard(() => Auto(id, kind, null)), allowConfirmationSkip: true, isNegative: false)), canWrite);
        }

        // The grid's hours: an hour either side of the route day, widened to the shifts shown; 06:00 to 22:00 without either.
        internal static (int From, int To) HourRange(RouteDay routeDay, IEnumerable<CrewShift> today, IEnumerable<CrewShift> nextDay)
        {
            int? from = null, to = null;
            if (!routeDay.Empty)
            {
                from = (int)Math.Floor(routeDay.Start / 60.0) - 1;
                to = (int)Math.Ceiling(routeDay.End / 60.0) + 1;
            }
            foreach (var s in today)
            {
                from = Math.Min(from ?? s.Start, s.Start);
                to = Math.Max(to ?? s.End, s.End);
            }
            // The next day's early shifts show only when the route day runs past midnight.
            if (to > 24)
                foreach (var s in nextDay.Where(s => s.Start < to - 24))
                    to = Math.Max(to.Value, s.End + 24);
            var a = Math.Max(0, from ?? 6);
            var b = Math.Min(30, Math.Max(a + 4, to ?? 22));
            return (a, b);
        }

        private static string GapText(CoverageGap gap, HireSchedule? schedule, DayOfWeek day)
        {
            var stops = WeekPlan.StopsOf(schedule, day);
            var number = (stops.IndexOf(gap.StopIndex) + 1).ToString();
            var key = gap.Kind == LegKind.Restock ? "foodtrucks:bizman_week_gap_restock"
                : gap.Kind == LegKind.Travel && gap.StopIndex == HireSchedules.HomeIndex ? "foodtrucks:bizman_week_gap_back"
                : gap.Kind == LegKind.Travel ? "foodtrucks:bizman_week_gap_drive"
                : "foodtrucks:bizman_week_gap_stop";
            return Format(key, ("from", Clock(gap.Start)), ("to", Clock(gap.End)), ("number", number));
        }

        // A shift bar: a copy of the game's work shift bar (or a plain one) on the member's row, the name and hours on it,
        // in the employee's colour as the game's schedule draws it. Drag the bar to move it, an end to resize it, whole
        // hours, 1 to 12; right-click removes it.
        private static void Bar(RectTransform track, CrewShift shift, int offset, int a, int b, int h0, int span, EmployeeInstance? employee, bool canWrite,
            Action<int, int> moved, System.Action removed, Colors colors, float unit)
        {
            RectTransform bar;
            if (sliderTemplate != null)
            {
                bar = (RectTransform)Object.Instantiate(sliderTemplate, track).transform;
                bar.gameObject.SetActive(true);
            }
            else
            {
                bar = Rect("FoodTrucks.ShiftBar", track);
                bar.gameObject.AddComponent<Image>();
            }
            bar.name = "FoodTrucks.Shift." + shift.Day;
            Place(bar, (float)(a - h0) / span, 0.12f, (float)(b - h0) / span, 0.88f);
            var colour = EmployeeColour(employee, colors);
            var background = (sliderBackgroundPath.Length > 0 ? bar.Find(sliderBackgroundPath) : null)?.GetComponent<Image>() ?? bar.GetComponent<Image>();
            if (background != null)
            {
                background.color = colour;
                background.raycastTarget = true;
            }
            var label = $"{shift.Start:D2}–{shift.End:D2}";
            var hours = Format("foodtrucks:bizman_week_shift_hours", ("hours", (shift.End - shift.Start).ToString()));
            var nameText = sliderNamePath.Length > 0 ? bar.Find(sliderNamePath) : null;
            var hoursText = sliderHoursPath.Length > 0 ? bar.Find(sliderHoursPath) : null;
            if (nameText != null) BizManParts.SetText(nameText, label);
            else if (BizManParts.SmallLabel(bar, label) is RectTransform text)
            {
                Place(text, 0.05f, 0f, 1f, 1f);
                BizManParts.Colour(text, Color.white);
            }
            if (hoursText != null) BizManParts.SetText(hoursText, hours);
            if (!canWrite) return;
            var span0 = span;
            ShiftBarDrag Handle(Transform target, int mode)
            {
                var drag = target.gameObject.AddComponent<ShiftBarDrag>();
                // Within its own weekday's hours on the grid.
                drag.Set(bar, track, mode, a, b, h0, span0, Math.Max(h0, offset), Math.Min(h0 + span0, offset + 24), DemandData.Rules?.Fleet.MaxShiftHours ?? 12,
                    moved, removed);
                return drag;
            }
            Handle(bar, 0);
            foreach (var (path, mode) in new[] { (sliderLeftPath, -1), (sliderRightPath, 1) })
            {
                var end = path.Length > 0 ? bar.Find(path) : null;
                if (end == null)
                {
                    var made = Rect(mode < 0 ? "LeftEnd" : "RightEnd", bar);
                    made.anchorMin = new Vector2(mode < 0 ? 0f : 1f, 0f);
                    made.anchorMax = new Vector2(mode < 0 ? 0f : 1f, 1f);
                    made.pivot = new Vector2(mode < 0 ? 0f : 1f, 0.5f);
                    made.sizeDelta = new Vector2(unit * 0.5f, 0f);
                    var image = made.gameObject.AddComponent<Image>();
                    image.color = new Color(1f, 1f, 1f, 0.25f);
                    end = made;
                }
                else if (end.GetComponent<Graphic>() == null) end.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
                Handle(end, mode);
            }
        }

        // The game's colour for an employee on its schedule (ScheduleHelper.GetEmployeeColor): their primary skill's
        // colour gradient at a point picked by their id.
        private static Color EmployeeColour(EmployeeInstance? employee, Colors colors)
        {
            try
            {
                if (employee != null) return SkillHelper.GetData(employee.GetPrimarySkill()).associatedColorGradient.EvaluateRandom(employee.id.GetHashCode());
            }
            catch (Exception ex) { Log.Error("Week plan: employee colour: " + ex.Message); }
            return colors.blue;
        }

        // ---- Changes ----

        private static VehicleState? Writable(string id)
        {
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return null;
            }
            return StreetSales.Find(FoodTrucksSession.CurrentState, id);
        }

        // Shifts changed: today is planned again, the crew's weekly hours follow, the save is marked, the page refreshed.
        private static void ShiftsChanged(VehicleState vehicle)
        {
            // Every unfinished plan, yesterday's late one too, is planned again with the new shifts.
            HireRunner.ReplanShifts(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
            BizManVehiclePage.RefreshLater();
        }

        private static void SetShift(string id, string employeeId, DayOfWeek day, int start, int end)
        {
            var vehicle = Writable(id);
            var rules = DemandData.Rules;
            if (vehicle == null || rules == null) return;
            var problem = WeekPlan.SetShift(rules.Fleet, vehicle, employeeId, day, start, end);
            if (problem != ShiftProblem.None)
            {
                Log.Info($"Week plan: shift {start:D2}-{end:D2} on {day} for {employeeId} on {id} refused: {problem}.");
                if (problem == ShiftProblem.TooLong) CartSelling.Notify("foodtrucks:notification_shift_too_long", ("max", rules.Fleet.MaxShiftHours.ToString()));
                BizManVehiclePage.RefreshLater();
                return;
            }
            Log.Info($"Week plan: {employeeId} on {id} works {day} {start:D2}:00-{end:D2}:00 (BizMan page).");
            ShiftsChanged(vehicle);
        }

        // A click on an empty hour: a shift from there, up to four hours, to the route day's end where that is sooner.
        private static void AddShift(string id, string employeeId, DayOfWeek day, int hour, RouteDay routeDay)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (vehicle?.Schedule != null && WeekPlan.ShiftOf(vehicle.Schedule, employeeId, day) != null) return;
            var end = Math.Min(24, hour + 4);
            if (!routeDay.Empty && day == routeDay.Day)
            {
                var routeEnd = (int)Math.Ceiling(routeDay.End / 60.0);
                if (routeEnd > hour) end = Math.Min(end, Math.Min(24, routeEnd));
            }
            SetShift(id, employeeId, day, hour, Math.Max(hour + 1, end));
        }

        private static void RemoveShift(string id, string employeeId, DayOfWeek day)
        {
            var vehicle = Writable(id);
            if (vehicle == null || !WeekPlan.RemoveShift(vehicle, employeeId, day)) return;
            Log.Info($"Week plan: {employeeId}'s {day} shift on {id} removed (BizMan page).");
            ShiftsChanged(vehicle);
        }

        // The test bridge's crew auto_schedule: Auto schedule week, without the question.
        internal static void AutoSchedule(string id, string kind) => Auto(id, kind, null);

        // Auto schedule for one day, or the week (`day` null), as Core's WeekPlan.AutoSchedule decides.
        private static void Auto(string id, string kind, DayOfWeek? day)
        {
            var vehicle = Writable(id);
            var rules = DemandData.Rules;
            if (vehicle == null || rules == null || vehicle.Crew.Count == 0) return;
            var home = HireRoutes.HomeOf(vehicle);
            var routeDays = WeekPlan.RouteDays(new HireSchedules(rules.Fleet), vehicle.Schedule, Travel.ModeFor(kind), HireRoutes.GameSpeed(),
                HireRoutes.HomePoint(home), home?.BayStops);
            var wishes = vehicle.Crew.Select(c => HireCrew.Find(c) is EmployeeInstance e ? HireCrew.Wish(e) : new CrewWish(c)).ToList();
            vehicle.Schedule ??= new HireSchedule();
            vehicle.Schedule.CrewShifts = WeekPlan.AutoSchedule(rules.Fleet, routeDays, wishes, HireCrew.Limit(kind), vehicle.Schedule.CrewShifts,
                day == null ? null : new[] { day.Value });
            Log.Info($"Week plan: auto schedule for {id} ({(day == null ? "the week" : day.ToString())}): " +
                     string.Join(", ", vehicle.Schedule.CrewShifts.Select(s => $"{s.EmployeeId} {s.Day} {s.Start:D2}-{s.End:D2}")) + ".");
            ShiftsChanged(vehicle);
        }

        private static void Copy(string id, DayOfWeek day)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            copied = RouteEditor.CopyDay(vehicle?.Schedule, day);
            Log.Info($"Week plan: {day} of {id} copied ({copied.Stops.Count} stops, {copied.Shifts.Count} shifts).");
            BizManVehiclePage.RefreshLater();
        }

        private static void Paste(string id, DayOfWeek day, bool all)
        {
            if (copied == null) return;
            var copy = copied;
            void Apply()
            {
                var vehicle = Writable(id);
                var rules = DemandData.Rules;
                if (vehicle == null || rules == null) return;
                var schedules = new HireSchedules(rules.Fleet);
                // A crew window open at a stop the paste replaces closes first, as for a removed stop (review round 1).
                var targets = all ? WeekPlan.Days.Where(d => d != copy.From).ToList() : new List<DayOfWeek> { day };
                if (RouteEditor.TakesOpenStop(vehicle, targets)) CartSelling.CloseByCrew(vehicle, HireCrew.Now());
                var problem = all ? RouteEditor.PasteToAllDays(schedules, vehicle, copy) : RouteEditor.PasteDay(schedules, vehicle, copy, day);
                if (problem != ScheduleProblem.None)
                {
                    Log.Info($"Week plan: pasting {copy.From} onto {(all ? "every day" : day.ToString())} of {id} refused: {problem}.");
                    CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                    BizManVehiclePage.RefreshLater();
                    return;
                }
                Log.Info($"Week plan: {copy.From} pasted onto {(all ? "every day" : day.ToString())} of {id}.");
                RouteStops.Changed(vehicle, shifts: true);
                BizManVehiclePage.RefreshLater();
            }
            if (!all)
            {
                Guard(Apply);
                return;
            }
            // The game's own question for pasting a day onto all days.
            HudConfirm.Show(bodyKey: "bizman_schedule_paste_to_all_days_confirm", onConfirmAction: () => Guard(Apply), allowConfirmationSkip: true, isNegative: false);
        }

        private static void Clear(string id, DayOfWeek day)
        {
            HudConfirm.Show(bodyKey: "bizman_schedule_clear_schedule_day", onConfirmAction: () => Guard(() =>
            {
                var vehicle = Writable(id);
                if (vehicle == null) return;
                foreach (var index in WeekPlan.StopsOf(vehicle.Schedule, day))
                    if (vehicle.IsOpen && vehicle.HireStop == index) CartSelling.CloseByCrew(vehicle, HireCrew.Now());
                RouteEditor.ClearDay(vehicle, day);
                Log.Info($"Week plan: {day} of {id} cleared.");
                RouteStops.Changed(vehicle, shifts: true);
                BizManVehiclePage.RefreshLater();
            }), allowConfirmationSkip: true, isNegative: true);
        }

        private static void SetClosed(string id, DayOfWeek day, bool closed, RuleTables rules)
        {
            var vehicle = Writable(id);
            if (vehicle == null) return;
            var problem = RouteEditor.SetClosed(new HireSchedules(rules.Fleet), vehicle, day, closed);
            if (problem != ScheduleProblem.None)
            {
                Log.Info($"Week plan: opening {day} of {id} refused: {problem}.");
                CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                BizManVehiclePage.RefreshLater();
                return;
            }
            Log.Info($"Week plan: {id} {(closed ? "closed" : "open")} on {day}.");
            RouteStops.Changed(vehicle);
            BizManVehiclePage.RefreshLater();
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
                Log.Info($"Route: new hours {StaffDialog.Clock(open)}-{(newClose is int c ? StaffDialog.Clock(c) : "?")} for stop {index + 1} of the week of {id} refused: {problem}.");
                CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
            }
            else
            {
                RouteStops.Changed(vehicle);
                Log.Info($"Route: stop {index + 1} of the week of {id} now {StaffDialog.Clock(open)}-{StaffDialog.Clock(newClose!.Value)} (BizMan page).");
            }
            BizManVehiclePage.RefreshLater();
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
                Log.Info($"Route: stop {from + 1} of the week of {id} moved to {to + 1}; the times stay in their slots (BizMan page).");
            }
            else if (problem != ScheduleProblem.None) Log.Info($"Route: moving stop {from + 1} to {to + 1} of {id} refused: {problem}.");
            BizManVehiclePage.RefreshLater();
        }

        private static void RemoveStop(string id, int index)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (vehicle == null) return;
            RouteStops.Remove(vehicle, index);
            BizManVehiclePage.RefreshLater();
        }

        private static void AddOnMap(string id, DayOfWeek day)
        {
            var rules = DemandData.Rules;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (rules != null && WeekPlan.StopsOf(vehicle?.Schedule, day).Count >= rules.Fleet.MaxStops)
            {
                CartSelling.Notify("foodtrucks:notification_route_full", ("max", rules.Fleet.MaxStops.ToString()));
                return;
            }
            StopPicker.Start(id, day);
        }

        // "Add the spot where it stands": the parked vehicle's own place as a stop on `day`, if it could open there now (the
        // same pitch check as Open for business), at the default hours; the page stays open.
        internal static void AddWhereItStands(string id, DayOfWeek day)
        {
            var rules = DemandData.Rules;
            var state = FoodTrucksSession.CurrentState;
            var kind = BizManVehiclePage.KindOf(id);
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            var saved = StreetSales.Find(state, id);
            if (rules != null && WeekPlan.StopsOf(saved?.Schedule, day).Count >= rules.Fleet.MaxStops)
            {
                Log.Info($"Route: the spot where {kind} {id} stands not added: {day}'s route is full.");
                CartSelling.Notify("foodtrucks:notification_route_full", ("max", rules.Fleet.MaxStops.ToString()));
                return;
            }
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
            var controller = cart == null ? null : VehicleHelper.GetVehicleController(cart);
            string? refusal = null, why = null;
            if (cart == null || controller == null || saved?.Transit != null)
            {
                why = cart == null ? "not in the city (with a driver, or gone)" : saved?.Transit != null ? "on the road with its crew" : "not in the world";
                refusal = "foodtrucks:notification_addstop_away";
            }
            else if (SaveGameManager.Current!.ActiveVehicleId == id)
            {
                why = "the player is driving or pushing it";
                refusal = "foodtrucks:notification_addstop_driven";
            }
            else if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                why = "the player is indoors, where the street isn't read";
                refusal = "foodtrucks:notification_addstop_indoors";
            }
            if (refusal != null)
            {
                Log.Info($"Route: the spot where {kind} {id} stands not added: {why}.");
                CartSelling.Say(refusal, kind);
                return;
            }
            var problem = CartSelling.PitchProblem(cart!, controller);
            if (problem != null)
            {
                Log.Info($"Route: the spot where {kind} {id} stands not added: not a pitch ({problem}).");
                CartSelling.Notify(problem);
                return;
            }
            var position = controller!.transform.position;
            var heading = controller.transform.eulerAngles.y;
            Log.Info($"Route: adding the spot where {kind} {id} stands, x {position.x:F1}, z {position.z:F1}, heading {heading:F0}, to {day} (BizMan page).");
            RouteStops.AddWithDefaultTimes(id, day, position, heading, BizManVehiclePage.RefreshLater);
        }

        // ---- Helpers ----

        private static string DayName(DayOfWeek day) => Text("foodtrucks:weekday_" + day.ToString().ToLowerInvariant());

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // A column of boxes in a row (the route beside its map), sharing the row's width by `weight`.
        private static RectTransform Stack(string name, Transform parent, float weight)
        {
            var stack = Rect(name, parent);
            var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = BizManParts.SmallLabelHeight() * 0.5f;
            var element = stack.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = weight;
            element.minWidth = 0f;
            return stack;
        }

        private static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Caption(RectTransform line, string text, Color colour, float width)
        {
            if (BizManParts.SmallLabel(line, text) is RectTransform label)
            {
                Place(label, 0f, 0f, width - 0.01f, 1f);
                BizManParts.Colour(label, colour);
            }
        }

        private static Transform? Part(Transform root, string path) => path.Length == 0 ? null : root.Find(path);

        private static void Show(Transform root, string path, bool on)
        {
            var part = Part(root, path);
            if (part != null) part.gameObject.SetActive(on);
        }

        private static string PathTo(Transform target)
        {
            var parts = new List<string>();
            for (var t = target; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Guard(System.Action action) => BizManVehiclePage.Guard(action);

        private static string Text(string key) => BizManVehiclePage.Text(key);

        private static string Format(string key, params (string Key, string Value)[] values) => BizManVehiclePage.Format(key, values);
    }
}
