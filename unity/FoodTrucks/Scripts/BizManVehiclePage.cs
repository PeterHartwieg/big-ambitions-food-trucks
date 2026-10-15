#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using UI;
using UI.Components;
using Parking.UndergroundParking;
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
    // A vehicle's own page in BizMan (Peter, 11 and 15 October 2026), in the three columns of the Street food page
    // (BizManFleet): the vehicle's summary and its crew and route in the first, prices and stock in the second, the
    // commissary space and the partner deal in the third. Each section is a copy of the game's warehouse box (BizManParts);
    // the crew, days and partner choices are copies of the warehouse Drivers tab's dropdown, and the stops a copy of the
    // logistics manager plan's reorderable list. Clicking a vehicle's row on the Street food page opens it; Back goes to the
    // overview. It sits outside BizManBusiness, so the game's business page, its settings and its unsaved-changes check
    // never see it. Every change writes the mod's save data at once, so the phone can close at any point.
    internal static class BizManVehiclePage
    {
        private static bool broken;
        private static BizManList? builtFor;
        private static string? vehicleId;

        // The templates, copies kept inactive in the Street food page's holder, and the paths of their parts.
        private static GameObject? dropdownTemplate, listTemplate;
        private static string entryPath = "", entryNamePath = "", entryRemovePath = "", entryDropdownPath = "";
        private static readonly List<string> entryHiddenPaths = new List<string>();
        // Buttons in the stop row that stay but no longer do anything (1.0: the collapse button is the whole header).
        private static readonly List<string> entryInertPaths = new List<string>();

        public static bool IsOpen => vehicleId != null;

        // ---- Opening ----

        // Opens BizMan on the vehicle's page, from anywhere (the end of a map pick, a price or a stop asked outside the
        // phone, a to-do's click).
        internal static void OpenFor(string id)
        {
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan;
            if (bizMan == null || bizMan.list == null)
            {
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            bizMan.Open();
            // The list wakes over the next frame (BizManList.OnEnable loads its parts one frame on).
            HireRoutes.Later(() => Guard(() =>
            {
                if (!Ensure(bizMan.list))
                {
                    CartSelling.Notify("foodtrucks:notification_page_unavailable");
                    return;
                }
                BizManFleet.ShowSection(bizMan.list);
                Show(bizMan.list, id);
            }));
        }

        // A vehicle's row on the Street food page was clicked.
        public static void Show(BizManList list, string id)
        {
            if (!Ensure(list))
            {
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            vehicleId = id;
            Refresh();
            Log.Info($"BizMan: vehicle page for {KindOf(id)} {id}.");
        }

        // The Street food page went back to its overview or closed: no vehicle page shows.
        public static void Forget() => vehicleId = null;

        // On leaving the city: the page belongs to its scene's BizMan.
        public static void Reset()
        {
            builtFor = null;
            dropdownTemplate = listTemplate = null;
            vehicleId = null;
        }

        // ---- Building ----

        // Builds the templates once per BizMan list, in the Street food page's holder. False (with one error logged) when
        // a part it copies is missing.
        private static bool Ensure(BizManList list)
        {
            var holder = BizManFleet.EnsurePage(list);
            if (holder == null) return false;
            if (builtFor == list && dropdownTemplate != null && listTemplate != null) return true;
            if (broken) return false;
            try
            {
                Build(holder);
                builtFor = list;
                return true;
            }
            catch (Exception ex)
            {
                broken = true;
                Log.Error($"BizMan vehicle page: {ex.Message} Vehicle pages aren't offered; prices, crew and routes can't be changed this session.");
                dropdownTemplate = listTemplate = null;
                return false;
            }
        }

        private static void Build(Transform holder)
        {
            var bizMan = InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan ?? throw Missing("the BizMan app");
            var business = bizMan.business ?? throw Missing("BizMan's business page");

            // The warehouse Drivers tab's row.
            var drivers = business.GetComponentsInChildren<Drivers>(true).FirstOrDefault() ?? throw Missing("the warehouse Drivers tab");
            var station = Need(drivers.driverStationTemplate, "Drivers.driverStationTemplate");
            var dropdown = Field<GameDropdown>(station, "driverDropdown");

            // The logistics manager plan's parts.
            var plans = Need(business.logisticsLogisticsManagersPlanList, "BizManBusiness.logisticsLogisticsManagersPlanList");
            var plan = Need(plans.logisticsManagerPlanUI, "LogisticsManagersPlanList.logisticsManagerPlanUI");
            var destination = Field<LogisticsManagerDestinationUI>(plan, "destinationTemplate");
            var reorderable = Field<ReorderableList>(plan, "destinationsReorderableList");
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

            // Copy the dropdown alone: vehicle fields, captions, backgrounds and warehouse spacing do not belong
            // on a crew/day/partner row. Instantiate remaps the dropdown's internal button and option references.
            dropdownTemplate = Object.Instantiate(dropdown.gameObject, holder);
            dropdownTemplate.name = "FoodTrucks.PageDropdown";
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

            KeepHeight(entry.gameObject, destination.transform);

            Log.Info($"BizMan: vehicle page parts copied: the warehouse Drivers tab's dropdown ({PathTo(station.transform)}) and the logistics plan's " +
                     $"stop list ({PathTo(plan.transform)}; stop rows dragged by {(handle == null ? "the whole row" : handlePath)}; " +
                     $"collapse button {(entryInertPaths.Count > 0 ? "kept, switched off" : "hidden")} at '{collapsePath}'; the copied list's {string.Join(", ", listLayout)}).");
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
            if (vehicleId == null) return;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var id = vehicleId;
            var instance = Instance(id);
            var names = StreetFoodBooks.VehicleNames();
            var name = names.TryGetValue(id, out var n) ? n : id;
            var columns = BizManFleet.ShowVehicleColumns(name);
            if (columns == null) return;
            var (first, second, third) = columns.Value;
            vehicleId = id;
            if (state == null || rules == null || instance == null)
            {
                new BizManParts.Box(first, name).Paragraph(Text("foodtrucks:bizman_page_gone"));
                return;
            }
            HireCrew.Check(state);
            var vehicle = StreetSales.Find(state, id);
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName) ?? "cart";
            var canWrite = FoodTrucksSession.CanWrite;
            var withDriver = IsWithDriver(id);

            Summary(first, state, vehicle, instance, name, withDriver, canWrite);
            CrewAndRoute(first, state, rules, vehicle, id, kind, canWrite);
            Prices(second, id, canWrite);
            Stock(second, instance);
            CommissarySpace(third, state, id, kind);
            Partner(third, state, id, instance, canWrite);
            foreach (var column in new[] { first, second, third })
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)column);
        }

        // The vehicle at a glance: what it is, where and how it is, the game's alert count, its average daily income
        // and health grade, and Set destination.
        private static void Summary(Transform column, SaveState state, VehicleState? vehicle, VehicleInstance instance, string name, bool withDriver, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var box = new BizManParts.Box(column, Text(BizManFleet.TypeKey(instance)));
            var canGo = !withDriver && vehicle?.Transit == null;
            box.Subtitle(BizManFleet.Where(instance, vehicle, withDriver), BizManFleet.StatusColour(vehicle, withDriver, colors),
                canGo ? () => BizManFleet.GoTo(instance, name) : (Action?)null);
            if (!canWrite) box.Paragraph(Text("foodtrucks:bizman_page_readonly"), (Color)colors.lightRed);
            var alerts = VehicleTodos.CountFor(instance.id);
            box.Row(Text("foodtrucks:bizman_page_alerts"),
                alerts == 0 ? "0" : LocalizorManager.GetLocalization("bizman_number_of_alerts", new Dictionary<string, string> { ["amount"] = alerts.ToString() }), "",
                alerts == 0 ? (Color?)null : (Color)colors.lightRed);
            var income = FleetReport.AvgDailyProfitCents(state, instance.id, SaveGameManager.Current.Day) / 100f;
            box.Row(Text("foodtrucks:bizman_page_income"), income.ToShortCurrencyFormat(), "", income < 0f ? (Color)colors.lightRed : (Color?)null);
            var grade = vehicle?.Health?.Grade ?? Grade.Ungraded;
            box.Row(Text("foodtrucks:bizman_column_grade"), grade == Grade.Ungraded ? "-" : grade.ToString(), "", grade == Grade.C ? (Color)colors.lightRed : (Color?)null);
        }

        // Crew: one row per staff slot, and one for each member past it (the truck's staff went from 3 to 2 on 14 October
        // 2026), so they can be taken off. While the crew is over the limit no row offers anyone new: replacing someone
        // would take them off and then refuse the newcomer. Route: days, stops, add.
        private static void CrewAndRoute(Transform column, SaveState state, RuleTables rules, VehicleState? vehicle, string id, string kind, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
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
                var labels = choices.Select(e => e == null ? Text("foodtrucks:bizman_page_unassigned") : CrewOption(e)).ToList();
                var index = slot;
                AddRow(box, Format("foodtrucks:bizman_page_crew_slot", ("number", (slot + 1).ToString())), labels, current != null ? 1 : 0, canWrite,
                    chosen => ChooseCrew(id, index, choices[chosen]?.id));
            }

            var schedule = vehicle?.Schedule;
            var count = schedule?.Stops.Count ?? 0;
            box.Section(Format("foodtrucks:bizman_page_route_header", ("count", count.ToString()), ("max", rules.Fleet.MaxStops.ToString())));
            if (schedule != null)
            {
                var dayKeys = StaffDialog.DayChoices.Select(c => c.Key).ToList();
                var currentDays = StaffDialog.DaysKey(schedule.Days);
                if (!dayKeys.Contains(currentDays)) dayKeys.Add(currentDays);
                AddRow(box, Text("foodtrucks:bizman_page_days"), dayKeys.Select(Text).ToList(), dayKeys.IndexOf(currentDays), canWrite,
                    chosen => ChooseDays(id, dayKeys[chosen]));
                // A route saved under older rules that fails today's: the crew doesn't run it, and the page says why.
                var problem = vehicle == null ? ScheduleProblem.None : new HireRunner(rules.Fleet).RouteProblem(vehicle);
                if (problem != ScheduleProblem.None) box.Paragraph(Text(RouteStops.ProblemKey(problem)), (Color)colors.lightRed);
                if (count > 1) box.Paragraph(Text("foodtrucks:bizman_page_route_hint"));
                AddStops(box, id, schedule, canWrite);
            }
            else box.Paragraph(Text(StopPicker.Ready ? "foodtrucks:bizman_page_route_empty" : "foodtrucks:bizman_page_route_empty_nomap"));
            if (StopPicker.Ready) box.Button(Text("foodtrucks:bizman_page_add_stop"), () => AddOnMap(id), canWrite && count < rules.Fleet.MaxStops);
            box.Button(Text("foodtrucks:bizman_page_add_here"), () => AddWhereItStands(id), canWrite && count < rules.Fleet.MaxStops);
        }

        // Prices: one row per food on the vehicle's menu, priced where it stands now. A click on a row asks for a price.
        private static void Prices(Transform column, string id, bool canWrite)
        {
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_prices_header"));
            var prices = PriceDialog.Lines(id);
            if (prices.Count == 0)
            {
                box.Paragraph(Text("foodtrucks:bizman_page_prices_none"));
                return;
            }
            foreach (var line in prices)
            {
                var item = line.Item;
                var food = line.Name;
                box.Item(Format("foodtrucks:bizman_page_price_name", ("food", food), ("source", line.Source)), line.Price,
                    line.Ceiling.Length == 0 ? "" : Format("foodtrucks:bizman_page_price_ceiling", ("price", line.Ceiling)),
                    click: canWrite ? () => ChangePrice(id, item, food) : (Action?)null);
            }
            box.Paragraph(Text(canWrite ? "foodtrucks:bizman_page_prices_hint" : "foodtrucks:bizman_page_readonly"));
        }

        // Stock, read only: each fitted piece with its load, then each of its foods as "x/capacity", low and empty ones
        // flagged the game's way (a quarter or less in light red, none in red).
        private static void Stock(Transform column, VehicleInstance instance)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_stock_header"));
            var tables = DemandData.Tables;
            var view = StoragePanel.Of(instance);
            if (tables == null || view == null || view.Pools.Count == 0)
            {
                box.Paragraph(Text("foodtrucks:bizman_page_stock_none"));
                return;
            }
            // Only the foods it stocks (on board, or sold on its last days out) that exist in the game: the to-dos' own rule.
            var foods = StockLevels.Stocked(tables, view, StreetSales.Find(FoodTrucksSession.CurrentState, instance.id), VehicleTodos.ItemExists);
            foreach (var pool in view.Pools)
            {
                box.Section(Format("foodtrucks:bizman_page_stock_piece", ("piece", StoragePanel.PieceName(instance, pool.Piece)),
                    ("load", pool.LoadUnits.ToString()), ("capacity", pool.Capacity.ToString())));
                foreach (var food in foods.Where(f => f.EquipmentId == pool.EquipmentId))
                {
                    Color? colour = food.Level == StockLevel.Empty ? (Color)colors.red : food.Level == StockLevel.Low ? (Color)colors.lightRed : (Color?)null;
                    var note = food.Level == StockLevel.Empty ? Text("foodtrucks:bizman_page_stock_empty")
                        : food.Level == StockLevel.Low ? Text("foodtrucks:bizman_page_stock_low") : "";
                    box.Item(Text(food.Item), Format("foodtrucks:bizman_fleet_storage_units", ("load", food.Units.ToString()), ("capacity", food.Capacity.ToString())),
                        note, colour, colour);
                }
            }
            if (view.StoragePieces > 0) box.Section(Text("foodtrucks:bizman_page_stock_extra"));
            if (view.Unheld.Count > 0)
            {
                box.Section(Text("foodtrucks:bizman_page_stock_unheld"));
                foreach (var pair in view.Unheld.OrderBy(p => p.Key, StringComparer.Ordinal)) box.Item(Text(pair.Key), pair.Value.ToString());
            }
        }

        // The commissary space, read only (spaces are rented by phone, under Spaces in the commissary's call), and the
        // way to that call.
        private static void CommissarySpace(Transform column, SaveState state, string id, string kind)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_space_header"));
            var sites = DemandData.Commissaries;
            var rules = DemandData.Rules?.Commissary;
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == id);
            var site = space == null ? null : sites?.Site(space.CommissaryId);
            if (space != null && site != null)
            {
                box.Row(Text("foodtrucks:bizman_page_space_at"), Commissary.Name(site));
                if (rules != null && rules.RentPerMonthCents.TryGetValue(kind, out var rent))
                    box.Row(Text("foodtrucks:bizman_page_space_rent"), Format("foodtrucks:bizman_page_space_rent_value", ("rent", CartSelling.Money(rent))));
                box.Row(Text("foodtrucks:bizman_page_space_since"), Format("foodtrucks:bizman_page_space_since_value", ("day", space.SinceDay.ToString())));
                var siteId = site.Id;
                box.Button(Text("foodtrucks:bizman_page_space_call"), () => Call(siteId));
                return;
            }
            box.Paragraph(Format("foodtrucks:bizman_page_space_none", ("fine", CartSelling.Money(rules?.OvernightFineCents ?? 0))), (Color)colors.lightRed);
            var known = state.Commissary.Unlocked.Select(u => sites?.Site(u)).Where(s => s != null).Select(s => s!).ToList();
            if (known.Count == 0) box.Paragraph(Text("foodtrucks:bizman_page_space_visit"));
            foreach (var other in known)
            {
                var otherId = other.Id;
                box.Button(Format("foodtrucks:bizman_page_space_call_named", ("commissary", Commissary.Name(other))), () => Call(otherId));
            }
        }

        private static void Call(string siteId)
        {
            Log.Info($"BizMan: vehicle page opens the conversation with commissary {siteId}.");
            ModContacts.OpenConversation(Commissary.ContactFor(siteId));
        }

        // Partner venue: No partner, the current one, and the player's own venues within reach of where the vehicle stands.
        private static void Partner(Transform column, SaveState state, string id, VehicleInstance instance, bool canWrite)
        {
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_partner_header"));
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
            AddRow(box, Text("foodtrucks:bizman_page_partner"), labels, current != null ? 1 : 0, canWrite && venues.Count > 1,
                chosen => ChoosePartner(id, venues[chosen]));
            box.Paragraph(Text(venues.Count > 1 ? "foodtrucks:bizman_page_partner_hint" : "foodtrucks:bizman_page_partner_none_near"));
        }

        private static void AddStops(BizManParts.Box box, string id, HireSchedule schedule, bool canWrite)
        {
            var listGo = Object.Instantiate(listTemplate!, box.Root);
            listGo.name = "FoodTrucks.Stops";
            box.Add(listGo.transform);
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
            BizManParts.SetText(entry.Find(entryNamePath), Format("foodtrucks:bizman_page_stop", ("number", (index + 1).ToString()), ("place", RouteStops.Label(stop))));

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

        // ---- Parts ----

        // A box row with a copy of the game's dropdown as its value.
        private static void AddRow(BizManParts.Box box, string title, List<string> options, int selected, bool interactable, Action<int> chosen)
        {
            var dropdown = box.DropdownRow<GameDropdown>(title, dropdownTemplate!);
            if (dropdown == null) return;
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

        // The game's text prompt, as the old Prices button had it. It shows outside the phone, like the map pick's time
        // pickers: the phone closes for it and opens again on this page once it is answered or closed.
        private static void ChangePrice(string id, string item, string name)
        {
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            Log.Info($"Prices: changing {item} on {KindOf(id)} {id} (BizMan page).");
            InstanceBehavior<UIs>.Instance?.fullMenu?.Toggle(show: false);
            HireRoutes.Later(() => Guard(() => PriceDialog.AskPrice(id, item, name, () => HireRoutes.Later(() => OpenFor(id)))));
        }

        // "Add the spot where it stands": the parked vehicle's own place as a stop, if it could open there now (the same
        // pitch check as Open for business), with the opening hours asked as after a map pick; then back to this page.
        internal static void AddWhereItStands(string id)
        {
            var rules = DemandData.Rules;
            var state = FoodTrucksSession.CurrentState;
            var kind = KindOf(id);
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            var saved = StreetSales.Find(state, id);
            if (rules != null && saved?.Schedule != null && saved.Schedule.Stops.Count >= rules.Fleet.MaxStops)
            {
                Log.Info($"Route: the spot where {kind} {id} stands not added: the route is full.");
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
            Log.Info($"Route: adding the spot where {kind} {id} stands, x {position.x:F1}, z {position.z:F1}, heading {heading:F0} (BizMan page).");
            // Default hours; the stop's row has the time dropdowns, so the page stays open.
            RouteStops.AddWithDefaultTimes(id, position, heading, RefreshLater);
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
