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
    // The vehicle page's two tabs and its Week plan tab (Peter, 16 October 2026): a vehicle is planned per weekday like a
    // shop. Across the page: a row of weekday tabs that picks which day's route to edit (today and closed days marked;
    // a day is opened and closed, copied and pasted on the game's own Schedule tab, as for a shop: Peter's playtest), the
    // crew slots, the chosen day's route (the commissary at both ends, drive rows, stop cards; a copy of the logistics
    // plan's reorderable list) beside its map, and the shifts: who is missing from the route day, in red, and a button
    // that opens the game's own Schedule tab for the vehicle (VehicleSchedule). Every change writes the mod's save at
    // once. No instruction text: only real problems, in red.
    internal static class BizManWeekPlan
    {
        // ---- State ----

        // Week plan or Overview, kept while the phone stays open; each vehicle's chosen weekday.
        internal static bool WeekTab { get; private set; }
        private static readonly Dictionary<string, DayOfWeek> chosen = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal);

        // The copied parts and the paths of their pieces.
        private static BizManList? builtFor;
        private static GameObject? tabTemplate;
        private static Color? tabColour;
        // The day's route copied with Copy route (any vehicle's), for Paste route.
        private static DayCopy? copiedRoute;

        internal static void Reset()
        {
            builtFor = null;
            tabTemplate = null;
            tabColour = null;
            copiedRoute = null;
            WeekTab = false;
            chosen.Clear();
        }

        // The weekday shown for the vehicle: the one chosen last, else today.
        internal static DayOfWeek DayFor(string id) =>
            chosen.TryGetValue(id, out var day) ? day : Core.Common.GameDays.Weekday(Math.Max(1, SaveGameManager.Current?.Day ?? 1));

        // ---- The game's parts ----

        // Copies, once per BizMan list, the business page's tab title into the Street food page's template holder. Without
        // it the tabs are plain labels. One log line says which.
        private static void EnsureParts()
        {
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
            var list = bizMan?.list;
            if (list == null || builtFor == list) return;
            var holder = BizManFleet.EnsurePage(list);
            if (holder == null) return;
            builtFor = list;
            tabTemplate = null;
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

            Log.Info($"BizMan: week plan parts copied: {(found.Count == 0 ? "none" : string.Join(", ", found))}" +
                     (missing.Count == 0 ? "; nothing missing." : $"; missing {string.Join("; ", missing)} (plain ones drawn instead)."));
        }

        // ---- Tabs ----

        // Overview and Week plan, at the top of the first column, in the business page's tab style.
        internal static void Tabs(Transform column)
        {
            EnsureParts();
            TabRow(column, "FoodTrucks.Tabs", new[] { (false, "foodtrucks:bizman_week_tab_overview"), (true, "foodtrucks:bizman_week_tab_week") }
                .Select(t => (Text(t.Item2), t.Item1 == WeekTab, (Color?)null, (System.Action)(() =>
                {
                    if (WeekTab == t.Item1) return;
                    WeekTab = t.Item1;
                    Log.Info($"BizMan: vehicle page tab {(t.Item1 ? "Week plan" : "Overview")}.");
                    BizManVehiclePage.RefreshLater();
                }))).ToList());
        }

        // Monday to Sunday, the same tabs: which day's route the tab edits. Today says so; a closed day is greyed.
        private static void DayTabs(Transform column, string id, DayOfWeek chosenDay, HireSchedule? schedule)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var today = Core.Common.GameDays.Weekday(Math.Max(1, SaveGameManager.Current?.Day ?? 1));
            TabRow(column, "FoodTrucks.DayTabs", WeekPlan.Days.Select(day =>
            {
                var name = DayName(day);
                var text = day == today ? Format("foodtrucks:bizman_week_day_today", ("day", name)) : name;
                Color? colour = WeekPlan.IsClosed(schedule, day) && day != chosenDay ? (Color)colors.darkGrey : (Color?)null;
                return (text, day == chosenDay, colour, (System.Action)(() =>
                {
                    chosen[id] = day;
                    BizManVehiclePage.RefreshLater();
                }));
            }).ToList());
        }

        // A row of tabs in the business page's tab style: the chosen one white with a line under it, the others in the
        // game's own tab colour (or `colour`).
        private static void TabRow(Transform column, string name, List<(string Text, bool Selected, Color? Colour, System.Action Click)> tabs)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var unit = BizManParts.SmallLabelHeight();
            var row = Rect(name, column);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = BizManParts.Layout.TabHeight;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = BizManParts.Layout.BoxGap;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            foreach (var (label, selected, colour, click) in tabs)
            {
                Transform tab;
                if (tabTemplate != null)
                {
                    tab = Object.Instantiate(tabTemplate, row).transform;
                    tab.gameObject.SetActive(true);
                    BizManParts.SetText(tab, label);
                }
                else
                {
                    tab = BizManParts.SmallLabel(row, label) ?? Rect("Tab", row);
                    tab.gameObject.AddComponent<Button>();
                }
                tab.name = "FoodTrucks.Tab";
                BizManParts.Colour(tab, selected ? (Color)colors.white : colour ?? tabColour ?? (Color)colors.lightGrey);
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
                var action = click;
                button.onClick.AddListener(() => Guard(action));
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

            DayTabs(column, id, day, schedule);
            Crew(column, state, vehicle, id, kind, canWrite);

            // The day's route beside its map.
            var pair = Rect("FoodTrucks.RouteAndMap", column);
            var pairLayout = pair.gameObject.AddComponent<HorizontalLayoutGroup>();
            pairLayout.spacing = BizManParts.Layout.BoxGap;
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
            // Copy and paste of a day's route, as the game's schedule copies a day (Peter, 16 October 2026).
            box.Button(Text("foodtrucks:bizman_week_copy_route"), () => CopyRoute(id, day), stops.Count > 0);
            var copy = copiedRoute;
            box.Button(copy == null ? Text("foodtrucks:bizman_week_paste_route_none")
                    : Format("foodtrucks:bizman_week_paste_route", ("day", DayName(copy.From))),
                () => PasteRoute(id, day, all: false), canWrite && copy != null && copy.From != day);
            if (copy != null && copy.From == day)
                box.Button(Text("foodtrucks:bizman_week_paste_route_all"), () => PasteRoute(id, day, all: true), canWrite);
        }

        private static void CopyRoute(string id, DayOfWeek day)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            copiedRoute = RouteEditor.CopyDay(vehicle?.Schedule, day);
            Log.Info($"Week plan: {day}'s route of {id} copied ({copiedRoute.Stops.Count} stops).");
            BizManVehiclePage.RefreshLater();
        }

        // The copied stops replace the day's (or every other day's); refused as any route edit when a night would run
        // into the next morning.
        private static void PasteRoute(string id, DayOfWeek day, bool all)
        {
            var copy = copiedRoute;
            if (copy == null) return;
            void Apply()
            {
                var vehicle = Writable(id);
                var rules = DemandData.Rules;
                if (vehicle == null || rules == null) return;
                var schedules = new HireSchedules(rules.Fleet);
                // A crew window at a stop that goes is marked lost (RouteEditor.Remap) and the runner closes it.
                var problem = all ? RouteEditor.PasteRouteToAllDays(schedules, vehicle, copy) : RouteEditor.PasteRoute(schedules, vehicle, copy, day);
                if (problem != ScheduleProblem.None)
                {
                    Log.Info($"Week plan: pasting {copy.From}'s route onto {(all ? "every day" : day.ToString())} of {id} refused: {problem}.");
                    CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                    BizManVehiclePage.RefreshLater();
                    return;
                }
                Log.Info($"Week plan: {copy.From}'s route pasted onto {(all ? "every day" : day.ToString())} of {id}.");
                RouteStops.Changed(vehicle);
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

        // The day from the bay where it rents its space, through the day's stops, and back (Peter, 16 October 2026): the
        // commissary at both ends on the stops' own card, greyed, its times as text, without remove or drag (no
        // ReorderableListItem, so the list's drag leaves it out; the page is rebuilt after a drag, putting it back in
        // place), with the drive (or walk) between each two as a slim row. The times come from the route day
        // (Core WeekPlan.RouteDayOf), so the page and the crew agree.
        private static void AddStops(BizManParts.Box box, string id, HireSchedule schedule, DayOfWeek day, RouteDay routeDay, bool canWrite)
        {
            var listGo = Object.Instantiate(BizManVehiclePage.listTemplate!, box.Root);
            listGo.name = "FoodTrucks.Stops";
            box.Add(listGo.transform);
            listGo.SetActive(true);
            // Cards the list's full width, Gap apart, no padding of the logistics list's own (BizManParts.Layout).
            BizManParts.Layout.Stack(listGo.GetComponent<HorizontalOrVerticalLayoutGroup>(), BizManParts.Layout.Gap);
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
            // "Opens" and "Closes" on a slim line over the cards, over the stops' two time dropdowns once laid out.
            var first = FixedRow(listGo.transform, "", bold: true);
            Captions(first);
            HireRoutes.Later(() => Guard(() => AlignHeader(first, listGo.transform)));
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
                FixedCard(listGo.transform, template, text);
                FixedRow(listGo.transform, TravelText(mode, drive), bold: false);
                drives.Add(drive);
            }
            else FixedCard(listGo.transform, template, Text("foodtrucks:bizman_page_route_no_space"));
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
                BizManParts.NoHover(entry);
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
                    FixedCard(listGo.transform, template, Format("foodtrucks:bizman_page_route_ends_in_bay", ("commissary", homeName!)));
                else
                {
                    var place = home.Value.Place;
                    var minutes = Minutes(last.X, last.Z, place.X, place.Z);
                    drives.Add(minutes);
                    var arrive = back.End > back.Start ? (int)back.End : last.CloseAt + minutes;
                    FixedRow(listGo.transform, Format(mode == TravelMode.Push ? "foodtrucks:bizman_page_route_walk_back" : "foodtrucks:bizman_page_route_drive_back",
                        ("minutes", minutes.ToString())), bold: false);
                    FixedCard(listGo.transform, template, Format("foodtrucks:bizman_page_route_back", ("commissary", homeName!), ("time", Clock(arrive))));
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
            var row = Rect(bold ? "FoodTrucks.CommissaryRow" : "FoodTrucks.TravelRow", list);
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = BizManParts.Layout.SlimHeight;
            element.flexibleWidth = 1f;
            var indent = 0f;
            if (!bold)
            {
                var icon = Rect("Icon", row);
                icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(BizManParts.Layout.IconSize, BizManParts.Layout.IconSize);
                icon.anchoredPosition = Vector2.zero;
                var image = icon.gameObject.AddComponent<Image>();
                image.sprite = InstanceBehavior<GlobalReferences>.Instance.vehiclePOIIcon;
                image.preserveAspect = true;
                image.color = colors.darkGrey;
                image.raycastTarget = false;
                indent = BizManParts.Layout.IconIndent;
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

        // The commissary at either end of the day on a stop card, greyed: its text in the card's name, no time dropdowns,
        // no remove button, and not a list item (it can't be dragged).
        private static void FixedCard(Transform list, Transform template, string text)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var card = Object.Instantiate(template, list);
            card.name = "FoodTrucks.CommissaryCard";
            var item = card.GetComponent<ReorderableListItem>();
            if (item != null) Object.DestroyImmediate(item);
            BizManParts.NoHover(card);
            BizManParts.SetText(card.Find(BizManVehiclePage.entryNamePath), text);
            BizManParts.Colour(card.Find(BizManVehiclePage.entryNamePath), (Color)colors.darkGrey);
            foreach (var path in new[] { BizManVehiclePage.entryDropdownPath, BizManVehiclePage.entryRemovePath })
            {
                var part = card.Find(path);
                if (part != null) part.gameObject.SetActive(false);
            }
            var background = card.GetComponent<Image>();
            if (background != null) background.color = Color.Lerp(background.color, (Color)colors.lightGrey, 0.6f);
            card.gameObject.SetActive(true);
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
            // Two columns of one fixed width at the card's right edge, so every card's times line up under Opens and
            // Closes whatever the time's text (Peter's playtest, 16 October 2026: 15:30 narrow, 18:30 wide).
            horizontal.spacing = BizManParts.Layout.Gap;
            horizontal.childAlignment = TextAnchor.MiddleRight;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = true;
            var close = Object.Instantiate(open.gameObject, times).GetComponent<GameDropdown>();
            open.transform.SetParent(times, false);
            open.transform.SetSiblingIndex(0);
            foreach (var dd in new[] { open, close })
            {
                // The game's Dropdown sets its own LayoutElement's preferredWidth to its widest option a frame after
                // SetOptions (Dropdown.SetPreferredWidth), so each card's times came out a different width. That element
                // drops to priority 0; a second one of ours, at a higher priority, holds the fixed width.
                var own = Traverse.Create(dd).Field("dropdownLayoutElement").GetValue<LayoutElement>();
                LayoutElement? fixedWidth = null;
                foreach (var element in dd.GetComponents<LayoutElement>())
                {
                    if (element == own) element.layoutPriority = 0;
                    else fixedWidth ??= element;
                }
                fixedWidth ??= dd.gameObject.AddComponent<LayoutElement>();
                fixedWidth.minWidth = fixedWidth.preferredWidth = BizManParts.Layout.TimeWidth;
                fixedWidth.flexibleWidth = 0f;
                fixedWidth.layoutPriority = 10;
                foreach (var fitter in dd.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
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

        // The shifts for the chosen day: where nobody is on shift during the route day (red), and the button to the game's
        // own Schedule tab, where the crew's shifts are set.
        private static void Shifts(Transform column, RuleTables rules, VehicleState? vehicle, string id, string kind, DayOfWeek day,
            IReadOnlyList<RouteDay> routeDays, List<string> crew, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var schedule = vehicle?.Schedule;
            var routeDay = routeDays[WeekPlan.Position(day)];
            var box = new BizManParts.Box(column, Format("foodtrucks:bizman_week_shifts_title", ("day", DayName(day))));
            if (crew.Count == 0) box.Paragraph(Text("foodtrucks:bizman_week_no_crew"));
            else if (!routeDay.Empty)
                foreach (var gap in WeekPlan.Gaps(routeDay, schedule, crew)) box.Paragraph(GapText(gap, schedule, day), (Color)colors.red);
            // Offered only while every guard patch of the Schedule tab is active (VehicleSchedule.Ready).
            box.Button(Text("foodtrucks:bizman_week_schedule"), () => Guard(() => VehicleSchedule.Open(id)), canWrite && crew.Count > 0 && VehicleSchedule.Ready);
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

        // The test bridge's crew auto_schedule: Core's auto schedule for the week.
        internal static void AutoSchedule(string id, string kind)
        {
            var vehicle = Writable(id);
            var rules = DemandData.Rules;
            if (vehicle == null || rules == null || vehicle.Crew.Count == 0) return;
            var home = HireRoutes.HomeOf(vehicle);
            var routeDays = WeekPlan.RouteDays(new HireSchedules(rules.Fleet), vehicle.Schedule, Travel.ModeFor(kind), HireRoutes.GameSpeed(),
                HireRoutes.HomePoint(home), home?.BayStops);
            var wishes = vehicle.Crew.Select(c => HireCrew.Find(c) is EmployeeInstance e ? HireCrew.Wish(e) : new CrewWish(c)).ToList();
            vehicle.Schedule ??= new HireSchedule();
            vehicle.Schedule.CrewShifts = WeekPlan.AutoSchedule(rules.Fleet, routeDays, wishes, HireCrew.Limit(kind), vehicle.Schedule.CrewShifts);
            Log.Info($"Week plan: auto schedule for {id}: " +
                     string.Join(", ", vehicle.Schedule.CrewShifts.Select(s => $"{s.EmployeeId} {s.Day} {s.Start:D2}-{s.End:D2} {s.Station}")) + ".");
            HireRunner.ReplanShifts(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
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
            layout.spacing = BizManParts.Layout.BoxGap;
            var element = stack.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = weight;
            element.minWidth = 0f;
            return stack;
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
