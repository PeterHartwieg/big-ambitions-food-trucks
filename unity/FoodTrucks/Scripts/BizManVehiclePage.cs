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
using GameInput = UI.Components.InputField;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // A vehicle's own page in BizMan (Peter, 11 and 15 October 2026), in the three columns of the Street food page
    // (BizManFleet), with two tabs (Peter, 16 October 2026): Overview has the vehicle's summary and menu in the first
    // column, alerts and the commissary space in the second, the partner deal in the third; Week plan (BizManWeekPlan)
    // has the crew, the day's route and the shifts across the page. Each section is a copy of the game's warehouse box (BizManParts);
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
        internal static GameObject? dropdownTemplate, listTemplate;
        // A copy of the game's product price field (InventoryProductCellView.retailPrice); null when it isn't there.
        private static GameObject? priceFieldTemplate;
        private static bool priceFieldLogged;
        // The page dropdowns' light box colour (read from the copied dropdown), the price fields' background.
        private static Color? fieldGrey;
        internal static string entryPath = "", entryNamePath = "", entryRemovePath = "", entryDropdownPath = "";
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
            dropdownTemplate = listTemplate = priceFieldTemplate = null;
            priceFieldLogged = false;
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
                dropdownTemplate = listTemplate = priceFieldTemplate = null;
            priceFieldLogged = false;
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
            // The dropdown's light box, for the price fields: its middle box, else its header's background, else its
            // lightest opaque image.
            var grey = dropdownTemplate.transform.Find("Header/MiddleBox")?.GetComponent<Image>()
                       ?? dropdownTemplate.transform.Find("Header/Background")?.GetComponent<Image>()
                       ?? dropdownTemplate.GetComponentsInChildren<Image>(true).Where(i => i.color.a > 0.9f)
                           .OrderByDescending(i => i.color.r + i.color.g + i.color.b).FirstOrDefault();
            fieldGrey = grey != null ? grey.color : (Color?)null;
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
            var priceField = PriceFieldTemplate(business, holder);

            Log.Info($"BizMan: vehicle page parts copied: the warehouse Drivers tab's dropdown ({PathTo(station.transform)}) and the logistics plan's " +
                     $"stop list ({PathTo(plan.transform)}; stop rows dragged by {(handle == null ? "the whole row" : handlePath)}; " +
                     $"collapse button {(entryInertPaths.Count > 0 ? "kept, switched off" : "hidden")} at '{collapsePath}'; the copied list's {string.Join(", ", listLayout)}); " +
                     $"price field: {priceField}; field colour {(fieldGrey?.ToString() ?? "not found, a light grey")}.");
        }

        // The game's product price field (the inventory pricing row's retailPrice), copied alone. Optional: without it the
        // Menu box's prices are changed with the text prompt. Returns what the log says about it.
        private static string PriceFieldTemplate(BizManBusiness business, Transform holder)
        {
            priceFieldTemplate = null;
            var row = business.bizManInventoryPricing?.inventoryProductsScrollerController?.prefab;
            var field = row == null ? null : row.retailPrice;
            if (field == null) return "missing (InventoryProductCellView.retailPrice), prices by text prompt";
            var copy = Object.Instantiate(field.gameObject, holder);
            copy.name = "FoodTrucks.PriceField";
            var copied = copy.GetComponent<GameInput>();
            var input = copied == null ? null : Traverse.Create(copied).Field("tmpInputField").GetValue<Component>();
            var placeholder = copied == null ? null : Traverse.Create(copied).Field("placeholder").GetValue<Component>();
            // Its references must be its own, or typing on this page would change the game's row.
            if (input == null || !input.transform.IsChildOf(copy.transform) || (placeholder != null && !placeholder.transform.IsChildOf(copy.transform)))
            {
                Object.Destroy(copy);
                return "its input isn't inside it, prices by text prompt";
            }
            foreach (var button in copy.GetComponentsInChildren<Button>(true)) button.gameObject.SetActive(false);
            priceFieldTemplate = copy;
            return $"copied from {PathTo(field.transform)}";
        }

        // A part with no layout of its own keeps the height the game gave it, and fills the page's width.
        private static void KeepHeight(GameObject copy, Transform original)
        {
            var element = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
            if (element.preferredHeight <= 0f && copy.GetComponent<LayoutGroup>() == null && original is RectTransform rect && rect.rect.height > 1f)
                element.preferredHeight = rect.rect.height;
        }

        // ---- Filling ----

        internal static void RefreshLater() => HireRoutes.Later(() => Guard(Refresh));

        private static void Refresh()
        {
            if (vehicleId == null) return;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var id = vehicleId;
            var instance = Instance(id);
            var names = StreetFoodBooks.VehicleNames();
            var name = names.TryGetValue(id, out var n) ? n : id;
            var week = BizManWeekPlan.WeekTab;
            var columns = BizManFleet.ShowVehicleColumns(name, wide: week);
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
            // A route saved before the travel rule is fitted as its page opens (the crew's planning does it too).
            if (vehicle != null) RouteStops.FitIfNeeded(vehicle, "page opened");
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName) ?? "cart";
            var canWrite = FoodTrucksSession.CanWrite;
            var withDriver = IsWithDriver(id);

            BizManWeekPlan.Tabs(first);
            if (week)
            {
                BizManWeekPlan.Fill(first, state, rules, vehicle, instance, id, kind, canWrite);
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)first);
                return;
            }
            Summary(first, state, vehicle, instance, name, withDriver, canWrite);
            Menu(first, id, vehicle, instance, canWrite);
            Alerts(second, id);
            CommissarySpace(second, state, id, kind);
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
            if (!canWrite) box.Paragraph(Text("foodtrucks:bizman_page_readonly"), (Color)colors.red);
            var alerts = VehicleTodos.CountFor(instance.id);
            box.Row(Text("foodtrucks:bizman_page_alerts"),
                alerts == 0 ? "0" : LocalizorManager.GetLocalization("bizman_number_of_alerts", new Dictionary<string, string> { ["amount"] = alerts.ToString() }), "",
                alerts == 0 ? (Color?)null : (Color)colors.lightRed);
            var income = FleetReport.AvgDailyProfitCents(state, instance.id, SaveGameManager.Current.Day) / 100f;
            box.Row(Text("foodtrucks:bizman_page_income"), income.ToShortCurrencyFormat(), "", income < 0f ? (Color)colors.lightRed : (Color?)null);
            var grade = vehicle?.Health?.Grade ?? Grade.Ungraded;
            box.Row(Text("foodtrucks:bizman_column_grade"), grade == Grade.Ungraded ? "-" : grade.ToString(), "", grade == Grade.C ? (Color)colors.lightRed : (Color?)null);
        }

        // The vehicle's to-dos, as a store's page lists its alerts: each one's text in the game's alert colour (red for
        // what stops sales or costs money; the game's yellow reads orange on the white box, as its own table turns yellow
        // text orange on a light row), "No alerts" when there are none.
        private static void Alerts(Transform column, string id)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_alerts_header"));
            var alerts = VehicleTodos.For(id);
            if (alerts.Count == 0)
            {
                box.Paragraph(Text("foodtrucks:bizman_page_alerts_none"));
                return;
            }
            foreach (var (text, priority) in alerts)
                box.Paragraph(text, priority >= 2 ? (Color)colors.red : priority == 1 ? (Color)colors.orange : (Color?)null);
        }

        // The table's columns: Food, Price, Market price, On board, status (left edges as fractions of the row).
        private static readonly float[] MenuColumns = { 0f, 0.36f, 0.54f, 0.68f, 0.84f };

        // Menu: one row per food the vehicle stocks (StockLevels.Stocked, the to-dos' own rule), priced the way the game's
        // inventory pricing shows a shop's product (InventoryProductCellView): the price now, typed into a copy of the
        // game's price field (yellow above the market price, red at twice the item's default price; empty is the automatic
        // price, shown greyed), the market price where the vehicle stands, what is on board of what its pieces hold, and low
        // or empty stock in red. Without the game's field a click on the row asks with the text prompt. Then the fitted
        // equipment on one line.
        private static void Menu(Transform column, string id, VehicleState? vehicle, VehicleInstance instance, bool canWrite)
        {
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_menu_header"));
            var tables = DemandData.Tables;
            var view = StoragePanel.Of(instance);
            if (tables == null || view == null || view.Pools.Count == 0)
            {
                box.Paragraph(Text("foodtrucks:bizman_page_stock_none"));
                return;
            }
            var drawn = VehiclePageDrawings.Equipment(box, instance, view, tables);
            var foods = StockLevels.Stocked(tables, view, vehicle, VehicleTodos.ItemExists);
            var prices = PriceDialog.Lines(id).ToDictionary(l => l.Item, StringComparer.Ordinal);
            box.TableHeader(MenuColumns, Text("foodtrucks:bizman_page_menu_food"), Text("foodtrucks:bizman_page_menu_price"),
                Text("foodtrucks:bizman_page_menu_market"), Text("foodtrucks:bizman_page_menu_onboard"), "");
            if (foods.Count == 0) box.Paragraph(Text("foodtrucks:bizman_page_menu_empty"));
            foreach (var food in foods)
            {
                var item = food.Item;
                var name = Text(item);
                var priced = prices.TryGetValue(item, out var line);
                var status = food.Level == StockLevel.Empty ? Text("foodtrucks:bizman_page_stock_empty")
                    : food.Level == StockLevel.Low ? Text("foodtrucks:bizman_page_stock_low") : "";
                var useField = priced && canWrite && priceFieldTemplate != null;
                var cells = new[]
                {
                    name,
                    !priced ? "-" : useField ? "" : CartSelling.Money(line.PriceCents),
                    priced ? CartSelling.Money(line.MarketCents) : "-",
                    Format("foodtrucks:bizman_page_menu_stock_value", ("load", food.Units.ToString()), ("capacity", food.Capacity.ToString())),
                    status,
                };
                var colours = new Color?[] { null, priced ? PriceColour(line, colors) : null, null, null, status.Length > 0 ? (Color)colors.red : (Color?)null };
                var row = box.TableRow(MenuColumns, cells, colours,
                    priced && canWrite && !useField ? () => ChangePrice(id, item, name) : (Action?)null);
                if (row != null && useField) PriceField(row, id, line);
            }
            if (!drawn) box.Paragraph(Format("foodtrucks:bizman_page_menu_equipment", ("pieces", Equipment(view, instance))));
        }

        // A price's colour, as InventoryProductCellView.UpdateAmountBackground: red at the red price, yellow above the
        // yellow one (orange on the white box's text), else none.
        private static Color? PriceColour(PriceDialog.Line line, Colors colors) =>
            line.RedCents > 0 && line.PriceCents >= line.RedCents ? (Color)colors.red
            : line.YellowCents > 0 && line.PriceCents > line.YellowCents ? (Color)colors.orange
            : (Color?)null;

        // "Dumpling Steamer, Griddle ×2, Extra Storage (doubles storage)".
        private static string Equipment(StorageView view, VehicleInstance instance)
        {
            var pieces = view.Pools.Select(p => StoragePanel.PieceName(instance, p.Piece) + (p.Pieces > 1 ? " ×" + p.Pieces : "")).ToList();
            if (view.StoragePieces > 0) pieces.Add(Text("foodtrucks:bizman_page_menu_storage"));
            return string.Join(", ", pieces);
        }

        // A copy of the game's product price field over the row's price column: the set price, or empty with the automatic
        // price greyed in it; the game's background colours; a new price is written when the field is left.
        private static void PriceField(Transform row, string id, PriceDialog.Line line)
        {
            var cell = row.Find("FoodTrucks.Cell1");
            if (cell == null || priceFieldTemplate == null) return;
            var copy = Object.Instantiate(priceFieldTemplate, row);
            copy.name = "FoodTrucks.PriceField";
            var rect = (RectTransform)copy.transform;
            var element = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            var cellRect = (RectTransform)cell;
            rect.anchorMin = cellRect.anchorMin;
            rect.anchorMax = cellRect.anchorMax;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(0f, 2f);
            rect.offsetMax = new Vector2(-BizManParts.Layout.CellGap, -2f);
            copy.SetActive(true);
            var field = copy.GetComponent<GameInput>();
            if (field == null) return;
            var culture = CultureHelper.CultureInfo;
            // The price it charges now, always, as the game's row does (retailPrice.SetText(price "N2", notify: false)):
            // a set price in the field's own text colour, the automatic one in grey.
            field.SetText((line.PriceCents / 100f).ToString("N2", culture), notify: false);
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var tmp = Traverse.Create(field).Field("tmpInputField").GetValue<Selectable>();
            if (tmp != null) tmp.interactable = true;
            var text = tmp == null ? null : Traverse.Create(tmp).Property("textComponent").GetValue() as Graphic;
            var hint = tmp == null ? null : Traverse.Create(tmp).Property("placeholder").GetValue() as Graphic;
            if (text != null && text.transform.IsChildOf(copy.transform))
            {
                // The game's field sits on a dark table with white text; on the white box its text takes the box's dark text
                // colour, grey for an automatic price.
                text.color = line.Custom ? (Color)colors.midnight : (Color)colors.darkGrey;
            }
            // The currency sign before the price was white, as on the game's dark table: on the grey field it takes the
            // price's colour (Peter's playtest, 16 October 2026: a white $ on white).
            foreach (var graphic in copy.GetComponentsInChildren<Graphic>(true))
                if (graphic.name == "Prefix") graphic.color = text != null ? text.color : (Color)colors.midnight;
            var background = copy.GetComponent<Image>();
            if (!priceFieldLogged)
            {
                priceFieldLogged = true;
                Log.Info($"BizMan: price field for {line.Item}: text '{field.GetRawValue()}' ({(line.Custom ? "set" : "automatic")}), " +
                         $"input {(tmp == null ? "missing" : tmp.interactable ? "interactable" : "not interactable")}, " +
                         $"text component {(text == null ? "missing" : text.transform.IsChildOf(copy.transform) ? $"inside, colour {text.color}" : "OUTSIDE the copy")}, " +
                         $"placeholder {(hint == null ? "none" : hint.transform.IsChildOf(copy.transform) ? "inside" : "OUTSIDE the copy")}, " +
                         $"background {(background == null ? "none" : background.color.ToString())}.");
            }
            if (background != null)
            {
                // The game's field is a dark translucent block made for its dark table (RGBA 0, 0, 0, 0.65 in game); on the
                // white box it takes the light grey of the page's dropdowns, tinted yellow or red as the game tints its own.
                Color32 tint = BizManParts.Layout.FieldGrey;
                if (line.RedCents > 0 && line.PriceCents >= line.RedCents) tint = colors.red;
                else if (line.YellowCents > 0 && line.PriceCents > line.YellowCents) tint = colors.yellow;
                tint.a = 255;
                background.color = tint;
            }
            var input = Traverse.Create(field).Field("tmpInputField").GetValue<Selectable>();
            var endEdit = input == null ? null : Traverse.Create(input).Property("onEndEdit").GetValue() as UnityEngine.Events.UnityEvent<string>;
            if (endEdit == null)
            {
                Log.Error("BizMan vehicle page: the copied price field has no end-edit event; prices go through the text prompt.");
                priceFieldTemplate = null;
                return;
            }
            var item = line.Item;
            var name = line.Name;
            var was = line.Custom ? line.PriceCents : (long?)null;
            var shown = line.PriceCents;
            endEdit.AddListener(_ => Guard(() =>
            {
                var raw = field.GetRawValue()?.Trim() ?? "";
                if (raw.Length == 0)
                {
                    if (was == null) return;
                    PriceDialog.SetPrice(id, item, name, "");
                }
                else
                {
                    if (!float.TryParse(raw, System.Globalization.NumberStyles.Number, culture, out var dollars))
                    {
                        CartSelling.Notify("foodtrucks:notification_price_invalid");
                        RefreshLater();
                        return;
                    }
                    var cents = (long)Math.Round(dollars * 100.0);
                    // The shown price left as it was: nothing to set (an automatic price stays automatic).
                    if (cents == shown) return;
                    PriceDialog.SetPrice(id, item, name, (cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                }
                RefreshLater();
            }));
        }

        // The commissary space, read only (spaces are rented by phone, under Spaces in the commissary's call), and the
        // way to that call.
        private static void CommissarySpace(Transform column, SaveState state, string id, string kind)
        {
            var box = new BizManParts.Box(column, Text("foodtrucks:bizman_page_space_header"));
            var sites = DemandData.Commissaries;
            var rules = DemandData.Rules?.Commissary;
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == id);
            var site = space == null ? null : sites?.Site(space.CommissaryId);
            if (space != null && site != null)
            {
                box.Row(Text("foodtrucks:bizman_page_space_at"), Commissary.Name(site));
                // Per day, as Commissary charges it (CommissaryRules.DailyRentCents: the month's rent over a year's days).
                if (rules != null && rules.RentPerMonthCents.ContainsKey(kind))
                    box.Row(Text("foodtrucks:bizman_page_space_rent"), Format("foodtrucks:bizman_page_space_rent_value", ("rent", CartSelling.Money(rules.DailyRentCents(kind)))));
                box.Row(Text("foodtrucks:bizman_page_space_since"), Format("foodtrucks:bizman_page_space_since_value", ("day", space.SinceDay.ToString())));
                var siteId = site.Id;
                box.Button(Text("foodtrucks:bizman_page_space_call"), () => Call(siteId));
                return;
            }
            // The missing space is a to-do (its fine there); here the commissaries it could call (Peter, 16 October 2026: no
            // lines explaining the page).
            var known = state.Commissary.Unlocked.Select(u => sites?.Site(u)).Where(s => s != null).Select(s => s!).ToList();
            box.Paragraph(Text("foodtrucks:bizman_page_space_none"));
            foreach (var other in known)
            {
                var otherId = other.Id;
                box.Button(Format("foodtrucks:bizman_page_space_call_named", ("commissary", ShortName(other))), () => Call(otherId));
            }
        }

        // "Hell's Kitchen" for "Hell's Kitchen Commissary": the buttons sit under the Commissary space title.
        internal static string ShortName(FoodTrucks.Core.Commissary.CommissarySite site)
        {
            var name = Commissary.Name(site);
            var word = Text("foodtrucks:bizman_page_space_word");
            return name.EndsWith(" " + word, StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - word.Length - 1) : name;
        }

        private static void Call(string siteId)
        {
            Log.Info($"BizMan: vehicle page calls commissary {siteId}.");
            ModContacts.OpenAndCall(Commissary.ContactFor(siteId));
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
        }

        // ---- Parts ----

        // A box row with a copy of the game's dropdown as its value.
        internal static void AddRow(BizManParts.Box box, string title, List<string> options, int selected, bool interactable, Action<int> chosen)
        {
            var dropdown = box.DropdownRow<GameDropdown>(title, dropdownTemplate!);
            if (dropdown == null) return;
            SetUp(dropdown, options, selected, interactable, chosen);
        }

        internal static void SetUp(GameDropdown dropdown, List<string> options, int selected, bool interactable, Action<int> chosen)
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

        internal static string CrewOption(EmployeeInstance employee) =>
            Format("foodtrucks:bizman_page_crew_option", ("name", HireCrew.Name(employee)), ("skill", HireCrew.Skill(employee).ToString("F0")),
                ("satisfaction", employee.satisfaction.ToString("F0")), ("wage", CartSelling.Money((long)Math.Round(employee.hourlyWage * 100))));

        internal static VehicleInstance? Instance(string id)
        {
            var game = SaveGameManager.Current;
            return game?.VehicleInstances.FirstOrDefault(v => v.id == id) ?? game?.privateDriverVehicleInstances?.FirstOrDefault(v => v.id == id);
        }

        private static bool IsWithDriver(string id) =>
            SaveGameManager.Current?.VehicleInstances.All(v => v.id != id) == true &&
            SaveGameManager.Current.privateDriverVehicleInstances?.Any(v => v.id == id) == true;

        internal static string KindOf(string id) => FoodTrucksMod.KindOf(Instance(id)?.vehicleTypeName) ?? "cart";

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

        internal static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error("BizMan vehicle page: " + ex); }
        }

        internal static string Text(string key) => LocalizorManager.GetLocalization(key, null);

        internal static string Format(string key, params (string Key, string Value)[] values) =>
            LocalizorManager.GetLocalization(key, values.ToDictionary(v => v.Key, v => v.Value));
    }
}
