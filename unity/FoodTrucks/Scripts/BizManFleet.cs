#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using EnhancedUI.EnhancedScroller;
using Entities;
using FoodTrucks.Core.Reputation;
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
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The Street food page in BizMan (Peter, 15 October 2026: it must look like the game's own BizMan). The business
    // list's "Street food" line opens it. The page is a copy of the list's own layout (`Layout 50-25-25`) with the game's
    // logic taken out: the overview is a copy of the business table (`BaTable (Businesses)`, its headers and
    // `BusinessCellView` rows), one row per food vehicle; a vehicle's page fills the three columns with copies of the
    // warehouse list's `WhiteBox (Template)` (BizManParts, BizManVehiclePage). While it shows, the list's own parts are
    // hidden; Back and leaving BizMan bring them back. The business-list entry has no building registration; its patches
    // keep the game's address-based business flow away from it.
    internal static class BizManFleet
    {
        private const string EntryId = "foodtrucks:streetfood";
        private const string RowIdPrefix = "foodtrucks:vehicle:";
        private const string LicenceRowId = "foodtrucks:licence";
        private const string BusinessesStatus = "common_businesses";

        private static bool ready, broken;
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        // Cells whose destination button the Street food line hid; a recycled cell gets it back.
        private static readonly HashSet<int> hiddenButtons = new HashSet<int>();
        private static readonly List<GameObject> hiddenListParts = new List<GameObject>();

        // The page: the layout copy, its rows and headers, the table, the vehicle page's columns and the Back button.
        private static Transform? section, holder, table, tableRows, column1;
        private static Transform? row1, row2, row3, header1, header2, header3;
        // The first row's own rectangle and layout sizes, kept while the Week plan tab widens it (Widen).
        private static (Vector2 Min, Vector2 Max, Vector2 OffsetMin, Vector2 OffsetMax)? row1Rect;
        private static (float Preferred, float Flexible)? row1Element;
        private static Transform? content1, content2, content3;
        private static Button? back;
        private static GameObject? cellPrefab;
        private static float cellHeight;
        private static BizManList? builtFor, openList;

        // Called once after patching: the line shows only when every patch that keeps it safe is in place.
        internal static void CheckReady()
        {
            ready = Has(typeof(BusinessScrollerController), "PopulateAllModels") && Has(typeof(BizManFilterToggle), "PassesFilter") &&
                    Has(typeof(BusinessCellView), "SetData") && Has(typeof(BizManList), "OpenBusiness") &&
                    Has(typeof(BizManList), "OnDisable");
            Log.Info(ready
                ? "BizMan: the Street food line and page are on."
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
                CloseSection();
                list?.RemoveModels(IsOurs);
            }
            catch (Exception again) { Log.Error(again); }
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
                var today = SaveGameManager.Current.Day;
                var open = vehicles.Count(v => StreetSales.Find(state, v.Instance.id)?.IsOpen == true);
                var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
                var model = NewModel(EntryId);
                model.BusinessName = Text("foodtrucks:econoview_row");
                model.BusinessType = "foodtrucks:bizman_type";
                model.AvgDailyIncome = FleetReport.AvgDailyProfitCents(state, today) / 100f;
                // The vehicles' to-dos and the licence's, as a business line counts its own.
                model.Alerts = vehicles.Sum(v => VehicleTodos.CountFor(v.Instance.id)) + VehicleTodos.FleetCount();
                model.Satisfaction = 0;
                model.OpenStatus = (LanguageChangeEventDataHolder.Create("foodtrucks:bizman_open_count",
                        new Dictionary<string, string> { ["open"] = open.ToString(), ["total"] = vehicles.Count.ToString() }),
                    (Color)(open > 0 ? colors.green : colors.lightRed));
                // Sorts like the game's own: open businesses before closed ones.
                model.Status = open > 0 ? -1 : -2;
                allModels.Add(model);
            }
            catch (Exception ex) { Fail("list", ex); }
        }

        // A business model without a building: the constructor isn't run, so the list its field initializer would make is set here.
        private static BusinessCellView.BusinessModel NewModel(string id)
        {
            var model = (BusinessCellView.BusinessModel)FormatterServices.GetUninitializedObject(typeof(BusinessCellView.BusinessModel));
            AccessTools.Field(typeof(BusinessCellView.BusinessModel), "statuses").SetValue(model, new List<string> { BusinessesStatus });
            model.Id = id;
            model.BuildingType = string.Empty;
            model.Address = null;
            model.IsRealEstate = false;
            return model;
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
                    if (Traverse.Create(__instance).Field("satisfaction").GetValue() is Component satisfaction) BizManParts.SetText(satisfaction.transform, "-");
                }
                else if (hiddenButtons.Remove(id)) button.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                // The cell is being drawn; the line is dropped from the next load on.
                Fail("cell", ex);
            }
        }

        // A click on the line opens the Street food page instead of a business page, and a second click closes it.
        // Returning false skips the game's handler, which would open BizMan's business page for an address the line
        // doesn't have.
        public static bool OpenPrefix(BizManList __instance, BusinessCellView.BusinessModel businessModel)
        {
            if (!IsOurs(businessModel)) return true;
            try
            {
                if (section != null && section.gameObject.activeSelf) CloseSection();
                else Open(__instance);
            }
            catch (Exception ex) { Fail("page", ex, Traverse.Create(__instance).Field("businessScrollerController").GetValue<BusinessScrollerController>()); }
            return false;
        }

        // Leaving the list closes the page, a vehicle's page with it.
        public static void ListClosedPostfix()
        {
            try { CloseSection(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void ShowSectionView()
        {
            if (section == null || builtFor == null) return;
            if (!section.gameObject.activeSelf)
            {
                hiddenListParts.Clear();
                foreach (Transform part in builtFor.transform)
                {
                    if (part == section || !part.gameObject.activeSelf) continue;
                    hiddenListParts.Add(part.gameObject);
                    part.gameObject.SetActive(false);
                }
            }
            section.gameObject.SetActive(true);
            section.SetAsLastSibling();
        }

        private static void CloseSection()
        {
            BizManVehiclePage.Forget();
            if (section != null) section.gameObject.SetActive(false);
            foreach (var part in hiddenListParts)
                if (part != null) part.SetActive(true);
            hiddenListParts.Clear();
        }

        internal static void Reset()
        {
            CloseSection();
            if (section != null) Object.Destroy(section.gameObject);
            Clear();
            builtFor = openList = null;
            hiddenListParts.Clear();
            hiddenButtons.Clear();
            broken = false;
            logged.Clear();
        }

        private static void Clear()
        {
            section = holder = table = tableRows = column1 = row1 = row2 = row3 = header1 = header2 = header3 = null;
            row1Rect = null;
            row1Element = null;
            content1 = content2 = content3 = null;
            back = null;
            cellPrefab = null;
            BizManParts.Reset();
        }

        // ---- Opening ----

        // The page's inactive holder for templates (the vehicle page keeps its dropdown and stop list there); null if
        // the page can't be built.
        internal static Transform? EnsurePage(BizManList list) => ready && !broken && Ensure(list) ? holder : null;

        // Opens the page on the overview, filled afresh (the vehicle page's Back, and opening a page from elsewhere).
        internal static void ShowSection(BizManList list)
        {
            try { Open(list); }
            catch (Exception ex) { Fail("page", ex, Traverse.Create(list).Field("businessScrollerController").GetValue<BusinessScrollerController>()); }
        }

        private static void Open(BizManList list)
        {
            if (!Ensure(list) || section == null)
            {
                // The page couldn't be built (the log names the missing part): the line goes, as on any other failure.
                Traverse.Create(list).Field("businessScrollerController").GetValue<BusinessScrollerController>()?.RemoveModels(IsOurs);
                CartSelling.Notify("foodtrucks:notification_page_unavailable");
                return;
            }
            openList = list;
            BizManVehiclePage.Forget();
            // Shown before it is filled: the game's cells set their texts through components that wake when active.
            ShowSectionView();
            ShowOverview();
        }

        private static void ShowOverview()
        {
            if (table == null) return;
            Widen(false);
            table.gameObject.SetActive(true);
            if (column1 != null) column1.gameObject.SetActive(false);
            if (row2 != null) row2.gameObject.SetActive(false);
            if (row3 != null) row3.gameObject.SetActive(false);
            SetHeader(header1, Text("foodtrucks:bizman_fleet_header"));
            SetBack(Text("foodtrucks:bizman_fleet_back"), CloseSection);
            Fill();
        }

        // The vehicle page's three columns, emptied, with the vehicle's name over the first and Back to the overview.
        // Null when the page isn't built. A layout without a third column puts the third's boxes in the second.
        // `wide` (the Week plan tab): only the first column shows, across the page's whole width, for the shift grid.
        internal static (Transform First, Transform Second, Transform Third)? ShowVehicleColumns(string title, bool wide = false)
        {
            if (section == null || table == null || content1 == null || content2 == null || column1 == null) return null;
            ShowSectionView();
            table.gameObject.SetActive(false);
            column1.gameObject.SetActive(true);
            if (row2 != null) row2.gameObject.SetActive(!wide);
            if (row3 != null) row3.gameObject.SetActive(!wide);
            Widen(wide);
            SetHeader(header1, title);
            SetHeader(header2, Text(content3 == null ? "foodtrucks:bizman_page_column_both" : "foodtrucks:bizman_page_column_menu"));
            SetHeader(header3, Text("foodtrucks:bizman_page_column_commissary"));
            SetBack(Text("foodtrucks:bizman_page_back"), () =>
            {
                if (openList != null) ShowSection(openList);
            });
            foreach (var content in new[] { content1, content2, content3 })
            {
                if (content == null) continue;
                foreach (Transform child in content.Cast<Transform>().ToList())
                {
                    child.gameObject.SetActive(false);
                    Object.Destroy(child.gameObject);
                }
                // Boxes the column's full width, BoxGap apart (BizManParts.Layout). The content's own padding stays: it
                // keeps the boxes' drop shadows inside the scroll view's mask.
                if (content.GetComponent<HorizontalOrVerticalLayoutGroup>() is { } stack)
                {
                    stack.spacing = BizManParts.Layout.BoxGap;
                    stack.childControlWidth = stack.childForceExpandWidth = true;
                }
                // The content grows downwards from the top: with the copied pivot it grew both ways as boxes went in, and a
                // column taller than the screen opened partway down, its first box's title out of sight (seen in game
                // 15 October 2026). Back to the top once the boxes are laid out.
                if (content is RectTransform rect)
                {
                    rect.anchorMin = new Vector2(rect.anchorMin.x, 1f);
                    rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
                    rect.pivot = new Vector2(rect.pivot.x, 1f);
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0f);
                }
                var scroll = content.GetComponentInParent<ScrollRect>();
                if (scroll != null)
                {
                    scroll.verticalNormalizedPosition = 1f;
                    HireRoutes.Later(() => { if (scroll != null) scroll.verticalNormalizedPosition = 1f; });
                }
            }
            return (content1, content2, content3 ?? content2);
        }

        // The first row across the page (the Week plan tab) or back to its own width. Under a layout group the row's layout
        // element takes the three rows' width; placed by anchors, its right edge goes to the last row's.
        private static void Widen(bool wide)
        {
            if (!(row1 is RectTransform rect)) return;
            var group = rect.parent == null ? null : rect.parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            var element = rect.GetComponent<LayoutElement>();
            row1Rect ??= (rect.anchorMin, rect.anchorMax, rect.offsetMin, rect.offsetMax);
            if (element != null) row1Element ??= (element.preferredWidth, element.flexibleWidth);
            if (!wide)
            {
                var (min, max, offsetMin, offsetMax) = row1Rect.Value;
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;
                if (element != null && row1Element is { } sizes)
                {
                    element.preferredWidth = sizes.Preferred;
                    element.flexibleWidth = sizes.Flexible;
                }
            }
            else if (group != null && group.enabled)
            {
                element ??= rect.gameObject.AddComponent<LayoutElement>();
                row1Element ??= (-1f, 0f);
                var width = new[] { row1, row2, row3 }.OfType<RectTransform>().Sum(r => r.rect.width) + group.spacing * 2f;
                element.preferredWidth = width > 1f ? width : -1f;
                element.flexibleWidth = 1f;
            }
            else if ((row3 ?? row2) is RectTransform last)
            {
                rect.anchorMax = new Vector2(last.anchorMax.x, rect.anchorMax.y);
                rect.offsetMax = new Vector2(last.offsetMax.x, rect.offsetMax.y);
            }
            if (rect.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        private static void SetHeader(Transform? header, string text)
        {
            if (header == null) return;
            var label = header.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault();
            BizManParts.SetText(label != null ? label.transform : header, text);
        }

        private const float BackInset = 30f + BizManParts.Layout.BoxGap;

        private static void SetBack(string caption, Action action)
        {
            if (back == null) return;
            BizManParts.Caption(back, caption);
            back.onClick = new Button.ButtonClickedEvent();
            back.onClick.AddListener(() => Guard(action));
            Fit(back);
        }

        // ---- Building ----

        // Copies BizMan's list layout once per list (the list is rebuilt with each city scene). On a missing part it logs
        // one line naming it and the line is taken out for the session.
        private static bool Ensure(BizManList list)
        {
            if (section != null && builtFor == list) return true;
            CloseSection();
            if (section != null) Object.Destroy(section.gameObject);
            Clear();
            var copied = new List<string>();
            var missing = new List<string>();
            var temp = new GameObject("FoodTrucks.Building", typeof(RectTransform));
            temp.SetActive(false);
            temp.transform.SetParent(list.transform, false);
            try
            {
                var built = Build(list, temp.transform, copied, missing);
                Log.Info($"BizMan: Street food page {(built ? "built" : "not built")} from the game's parts: copied {string.Join(", ", copied)}" +
                         (missing.Count == 0 ? "; nothing missing." : $"; missing {string.Join("; ", missing)}."));
                if (!built)
                {
                    if (section != null) Object.Destroy(section.gameObject);
                    Clear();
                    broken = true;
                    return false;
                }
                builtFor = list;
                return true;
            }
            finally { Object.Destroy(temp); }
        }

        private static bool Build(BizManList list, Transform temp, List<string> copied, List<string> missing)
        {
            var businesses = Traverse.Create(list).Field("businessScrollerController").GetValue<BusinessScrollerController>();
            var warehouses = Traverse.Create(list).Field("warehouseList").GetValue<WarehouseList>();
            var headquarters = Traverse.Create(list).Field("headquartersList").GetValue<HeadquartersList>();
            var warehouseBox = warehouses == null ? null : Traverse.Create(warehouses).Field("warehouseEntry").GetValue<Transform>();
            var hqBox = headquarters == null ? null : Traverse.Create(headquarters).Field("headquartersEntry").GetValue<Transform>();
            if (businesses == null || businesses.prefab == null) { missing.Add("the business table (BaTable (Businesses)) or its BusinessCellView"); return false; }
            if (warehouseBox == null) { missing.Add("the warehouse list's WhiteBox (Template)"); return false; }
            var layout = Top(businesses.transform, list.transform);
            var warehouseScroll = warehouseBox.GetComponentInParent<ScrollRect>(true);
            if (layout == null) { missing.Add("a layout under the list holding the business table"); return false; }
            if (warehouseScroll == null || !warehouseScroll.transform.IsChildOf(layout)) { missing.Add("the warehouse column's scroll view in the list's layout"); return false; }
            var hqScroll = hqBox == null ? null : hqBox.GetComponentInParent<ScrollRect>(true);
            if (hqScroll != null && !hqScroll.transform.IsChildOf(layout)) hqScroll = null;
            var rowOne = Top(businesses.transform, layout);
            var rowTwo = Top(warehouseScroll.transform, layout);
            var rowThree = hqScroll == null ? null : Top(hqScroll.transform, layout);
            if (rowOne == null || rowTwo == null || rowOne == rowTwo) { missing.Add("separate columns for the business table and the warehouses"); return false; }
            if (rowThree == null || rowThree == rowOne || rowThree == rowTwo)
            {
                missing.Add("a third column (the headquarters'): its boxes go in the second");
                rowThree = null;
            }
            var filter = Traverse.Create(list).Field("bizManFilterPanel").GetValue<Component>();

            // Copied under an inactive holder, so none of the copy's components wake before the game's logic is taken out.
            var copy = Object.Instantiate(layout.gameObject, temp).transform;
            copy.name = "FoodTrucks.StreetFoodPage";
            var removed = StripLogic(copy);
            if (filter != null && filter.transform.IsChildOf(layout)) DestroyAt(copy, Path(filter.transform, layout));
            copied.Add($"{layout.name} (logic taken out: {(removed.Count == 0 ? "none" : string.Join(", ", removed))})");

            var tableCopy = copy.Find(Path(businesses.transform, layout)) ?? throw new InvalidOperationException("The copied layout has no business table.");
            var row1 = copy.Find(Path(rowOne, layout))!;
            var wScroll = copy.Find(Path(warehouseScroll.transform, layout))!;
            var r2 = copy.Find(Path(rowTwo, layout))!;
            var hScroll = hqScroll == null || rowThree == null ? null : copy.Find(Path(hqScroll.transform, layout));
            var r3 = rowThree == null ? null : copy.Find(Path(rowThree, layout));
            // Every other part of the layout (a fourth row, the private residences) goes.
            foreach (Transform part in copy.Cast<Transform>().ToList())
                if (part != row1 && part != r2 && part != r3) Object.DestroyImmediate(part.gameObject);

            header1 = KeepOnly(row1, tableCopy);
            header2 = KeepOnly(r2, wScroll);
            header3 = r3 != null && hScroll != null ? KeepOnly(r3, hScroll) : null;
            if (header1 == null) missing.Add("the business table's title");

            // The table: its headers stay, its scroller's rows go, and our rows are laid out in its scroll view.
            var tableScroll = tableCopy.GetComponentInChildren<ScrollRect>(true);
            if (tableScroll == null || tableScroll.content == null) { missing.Add("the business table's scroll view"); return false; }
            tableRows = Empty(tableScroll.content);
            var rowsLayout = tableRows.GetComponent<VerticalLayoutGroup>() ?? tableRows.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.childControlWidth = rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            Fit((RectTransform)tableRows);
            var headers = tableCopy.Find("Headers");
            if (headers == null) missing.Add("the business table's Headers");
            else
            {
                Relabel(headers, "BusinessType", "foodtrucks:bizman_column_type");
                Relabel(headers, "Satisfaction", "foodtrucks:bizman_column_grade");
                copied.Add("BaTable (Businesses) headers");
            }
            cellPrefab = businesses.prefab.gameObject;
            cellHeight = businesses.GetCellViewSize(businesses.scroller, 0);
            if (cellHeight <= 1f) cellHeight = ((RectTransform)cellPrefab.transform).rect.height;
            copied.Add($"BaTable (Businesses) with BusinessCellView rows {cellHeight:F0} high");

            // The columns: the warehouse column's scroll view, emptied; a copy of it stands where the table is for the
            // vehicle page's first column.
            content2 = Empty(Content(wScroll));
            content3 = hScroll == null ? null : Empty(Content(hScroll));
            var first = Object.Instantiate(wScroll.gameObject, tableCopy.parent).transform;
            first.name = "FoodTrucks.Column";
            first.SetSiblingIndex(tableCopy.GetSiblingIndex() + 1);
            Place((RectTransform)first, (RectTransform)tableCopy);
            content1 = Content(first);
            column1 = first;
            copied.Add($"{warehouseScroll.name} as the vehicle page's columns" + (content3 == null ? " (two)" : $" and {hqScroll!.name}"));

            // The templates' holder, inactive, inside the page.
            var holderGo = new GameObject("FoodTrucks.Templates", typeof(RectTransform));
            holderGo.SetActive(false);
            holderGo.transform.SetParent(copy, false);
            holder = holderGo.transform;
            var boxMissing = BizManParts.Take(warehouseBox, holder);
            missing.AddRange(boxMissing);
            if (!BizManParts.Ready) return false;
            copied.Add("WhiteBox (Template) from the warehouse list");

            // Back: a copy of the box's Manage drivers button, at the right of the table's title.
            back = BizManParts.LooseButton(header1 ?? row1, Text("foodtrucks:bizman_fleet_back"), CloseSection);
            if (back == null) missing.Add("the WhiteBox's Manage drivers button (no Back button)");
            else
            {
                var rect = (RectTransform)back.transform;
                var element = back.GetComponent<LayoutElement>() ?? back.gameObject.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
                var corner = header1 == null ? new Vector2(1f, 1f) : new Vector2(1f, 0.5f);
                rect.anchorMin = rect.anchorMax = rect.pivot = corner;
                // The header runs to the screen's edge on a one-column page (a vehicle's Week plan); the button keeps the
                // boxes' right edge: the column's scrollbar strip plus the column's own padding (Peter's playtest).
                rect.anchoredPosition = new Vector2(-BackInset, 0f);
                var source = BizManPartsButtonHeight(warehouseBox);
                var height = header1 is RectTransform h && h.rect.height > 1f ? Mathf.Min(source > 1f ? source : h.rect.height, h.rect.height) : source;
                var tall = height > 1f ? height : Mathf.Max(rect.sizeDelta.y, 1f);
                // A stretched button has no width of its own once anchored to a point; Fit sizes it to its caption.
                rect.sizeDelta = new Vector2(rect.sizeDelta.x > 1f ? rect.sizeDelta.x : tall * 4f, tall);
                copied.Add("Manage drivers button as Back");
            }

            section = copy;
            BizManFleet.row1 = row1;
            row2 = r2;
            row3 = r3;
            table = tableCopy;
            copy.gameObject.SetActive(false);
            copy.SetParent(list.transform, false);
            return true;
        }

        private static float BizManPartsButtonHeight(Transform box) =>
            box.Find("ManageDrivers/ManageDriversButton") is RectTransform rect ? rect.rect.height : 0f;

        // Takes the game's list logic out of a copy: the table's scroller and its cells, the list loaders and filters. What
        // stays is layout, images, text and buttons. Returns the type names taken out.
        internal static SortedSet<string> StripLogic(Transform root)
        {
            var removed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var cell in root.GetComponentsInChildren<EnhancedScrollerCellView>(true))
                if (cell != null) Object.DestroyImmediate(cell.gameObject);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var type = behaviour.GetType();
                var space = type.Namespace ?? "";
                if (behaviour is IEnhancedScrollerDelegate || behaviour is EnhancedScroller ||
                    space.StartsWith("UI.Smartphone.Apps", StringComparison.Ordinal) || space.StartsWith("EnhancedUI", StringComparison.Ordinal))
                {
                    removed.Add(type.Name);
                    Object.DestroyImmediate(behaviour);
                }
            }
            return removed;
        }

        // Keeps `keep` and the row's title and takes every other part of the row out, at each level from the row down to
        // `keep`. The title is the first part at a level that has text and nothing to click or scroll.
        private static Transform? KeepOnly(Transform row, Transform keep)
        {
            Transform? header = null;
            for (var node = keep; node != null && node != row; node = node.parent)
            {
                var parent = node.parent;
                if (parent == null) break;
                foreach (Transform sibling in parent.Cast<Transform>().ToList())
                {
                    if (sibling == node) continue;
                    if (header == null && IsTitle(sibling))
                    {
                        header = sibling;
                        continue;
                    }
                    Object.DestroyImmediate(sibling.gameObject);
                }
            }
            return header;
        }

        private static bool IsTitle(Transform part) =>
            part.GetComponentsInChildren<TextLocalizationComponent>(true).Any() &&
            !part.GetComponentsInChildren<Selectable>(true).Any() && !part.GetComponentsInChildren<ScrollRect>(true).Any();

        private static void Relabel(Transform headers, string column, string key)
        {
            var header = headers.Find(column);
            var label = header == null ? null : header.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault();
            if (label != null) label.SetData(LanguageChangeEventDataHolder.Create(key));
        }

        private static Transform Content(Transform scrollView)
        {
            var scroll = scrollView.GetComponent<ScrollRect>() ?? scrollView.GetComponentInChildren<ScrollRect>(true);
            return scroll != null && scroll.content != null ? scroll.content : scrollView;
        }

        private static Transform Empty(Transform parent)
        {
            foreach (Transform child in parent.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
            return parent;
        }

        // A scroll view's content grows with what is in it, from the top.
        private static void Fit(RectTransform content)
        {
            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
        }

        // The column takes the table's place: its rectangle and, under a layout group, its layout element.
        private static void Place(RectTransform column, RectTransform like)
        {
            column.anchorMin = like.anchorMin;
            column.anchorMax = like.anchorMax;
            column.pivot = like.pivot;
            column.anchoredPosition = like.anchoredPosition;
            column.sizeDelta = like.sizeDelta;
            var own = column.GetComponent<LayoutElement>();
            if (own != null) Object.DestroyImmediate(own);
            var model = like.GetComponent<LayoutElement>();
            if (model == null) return;
            var element = column.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = model.ignoreLayout;
            element.minWidth = model.minWidth;
            element.minHeight = model.minHeight;
            element.preferredWidth = model.preferredWidth;
            element.preferredHeight = model.preferredHeight;
            element.flexibleWidth = model.flexibleWidth;
            element.flexibleHeight = model.flexibleHeight;
        }

        // The Back button as wide as its caption plus a margin of its own height.
        private static void Fit(Button button)
        {
            var rect = (RectTransform)button.transform;
            var text = button.GetComponentsInChildren<Graphic>(true).FirstOrDefault(g => g.GetType().GetProperty("preferredWidth") != null && g is ILayoutElement && g.GetType().Name.Contains("Text"));
            if (text == null) return;
            var width = LayoutUtility.GetPreferredWidth(text.rectTransform);
            if (width > 1f) rect.sizeDelta = new Vector2(width + Mathf.Max(rect.sizeDelta.y, 1f), rect.sizeDelta.y);
        }

        private static Transform? Top(Transform target, Transform root)
        {
            for (var t = target; t != null; t = t.parent)
                if (t.parent == root) return t;
            return null;
        }

        private static string Path(Transform target, Transform root) =>
            PathBelow(target, root) ?? throw new InvalidOperationException($"{target.name} isn't inside {root.name}.");

        private static void DestroyAt(Transform root, string path)
        {
            var part = path.Length == 0 ? null : root.Find(path);
            if (part != null) Object.DestroyImmediate(part.gameObject);
        }

        internal static string? PathBelow(Transform target, Transform root)
        {
            var parts = new List<string>();
            for (var t = target; t != null; t = t.parent)
            {
                if (t == root) return string.Join("/", Enumerable.Reverse(parts));
                parts.Add(t.name);
            }
            return null;
        }

        // ---- The overview ----

        // The overview's rows again while it shows (alerts, status, income), kept where the player scrolled: VehicleTodos
        // calls this after each minute's reconcile.
        internal static void RefreshOverview()
        {
            if (broken || table == null || tableRows == null || !table.gameObject.activeInHierarchy) return;
            try { Fill(keepScroll: true); }
            catch (Exception ex) { Log.Error("BizMan Street food overview refresh: " + ex); }
        }

        private static void Fill(bool keepScroll = false)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || tableRows == null || cellPrefab == null) return;
            foreach (Transform child in tableRows.Cast<Transform>().ToList())
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
            var licence = VehicleTodos.FleetCount();
            if (licence > 0) LicenceRow(licence);
            var names = StreetFoodBooks.VehicleNames();
            var today = SaveGameManager.Current.Day;
            foreach (var vehicle in Vehicles().OrderBy(v => names.TryGetValue(v.Instance.id, out var n) ? n : v.Instance.id, StringComparer.Ordinal))
            {
                var instance = vehicle.Instance;
                var name = names.TryGetValue(instance.id, out var found) ? found : instance.id;
                VehicleRow(state, instance, vehicle.WithDriver, name, today);
            }
            var scroll = tableRows.GetComponentInParent<ScrollRect>();
            if (scroll != null && !keepScroll) scroll.verticalNormalizedPosition = 1f;
        }

        private static BusinessCellView? NewRow(string id, Action click)
        {
            var go = Object.Instantiate(cellPrefab!, tableRows!);
            go.name = "FoodTrucks.Row";
            go.SetActive(true);
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = cellHeight;
            element.flexibleWidth = 1f;
            // As the game's table does for a selectable row: the row is a button.
            var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Guard(click));
            return go.GetComponent<BusinessCellView>();
        }

        private static void VehicleRow(SaveState state, VehicleInstance instance, bool withDriver, string name, int today)
        {
            var id = instance.id;
            var cell = NewRow(RowIdPrefix + id, () =>
            {
                if (openList != null) BizManVehiclePage.Show(openList, id);
            });
            if (cell == null) return;
            var vehicle = StreetSales.Find(state, id);
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var model = NewModel(RowIdPrefix + id);
            model.BusinessName = name;
            model.BusinessType = TypeKey(instance);
            model.AvgDailyIncome = FleetReport.AvgDailyProfitCents(state, id, today) / 100f;
            model.Alerts = VehicleTodos.CountFor(id);
            model.Satisfaction = 100;
            model.OpenStatus = (LanguageChangeEventDataHolder.Create("foodtrucks:bizman_value",
                new Dictionary<string, string> { ["value"] = Where(instance, vehicle, withDriver) }), StatusColour(vehicle, withDriver, colors));
            // The game's own formatting: the name, the type, the income in the short currency format (light red below 0),
            // "N alerts" in light red or a white 0, the status in its colour.
            cell.SetData(model);
            // The status kept inside its column (BizManParts.FitInCell).
            if (cell.status != null)
                BizManParts.FitInCell(cell.status.transform);
            // The satisfaction column holds the health grade: C in light red, as satisfaction under 50%.
            var grade = vehicle?.Health?.Grade ?? Grade.Ungraded;
            if (Traverse.Create(cell).Field("satisfaction").GetValue() is Component satisfaction)
            {
                BizManParts.SetText(satisfaction.transform, grade == Grade.Ungraded ? "-" : grade.ToString());
                BizManParts.Colour(satisfaction.transform, (Color)(grade == Grade.C ? colors.lightRed : colors.white));
            }
            var button = cell.destinationButton;
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                var canGo = !withDriver && vehicle?.Transit == null;
                button.gameObject.SetActive(canGo);
                if (canGo) button.onClick.AddListener(() => Guard(() => GoTo(instance, name)));
            }
        }

        // The vendor licence's to-do (renewal, or run out) as a line of its own; a click opens the City's conversation.
        private static void LicenceRow(int alerts)
        {
            var cell = NewRow(LicenceRowId, () => ModContacts.OpenConversation(CityLicensing.City()));
            if (cell == null) return;
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var model = NewModel(LicenceRowId);
            model.BusinessName = Text("foodtrucks:bizman_licence_row");
            model.BusinessType = "foodtrucks:bizman_type_licence";
            model.Alerts = alerts;
            model.Satisfaction = 100;
            model.OpenStatus = (LanguageChangeEventDataHolder.Create("foodtrucks:bizman_licence_status"), (Color)colors.lightRed);
            cell.SetData(model);
            if (Traverse.Create(cell).Field("avgDailyIncome").GetValue() is Component income) BizManParts.SetText(income.transform, "-");
            if (Traverse.Create(cell).Field("satisfaction").GetValue() is Component satisfaction) BizManParts.SetText(satisfaction.transform, "-");
            if (cell.destinationButton != null)
            {
                cell.destinationButton.onClick.RemoveAllListeners();
                cell.destinationButton.gameObject.SetActive(false);
            }
        }

        internal static string TypeKey(VehicleInstance instance) =>
            FoodTrucksMod.KindOf(instance.vehicleTypeName) == "truck" ? "foodtrucks:bizman_type_truck" : "foodtrucks:bizman_type_cart";

        internal static Color StatusColour(VehicleState? vehicle, bool withDriver, Colors colors) =>
            vehicle?.Transit != null || withDriver ? (Color)colors.white : vehicle?.IsOpen == true ? (Color)colors.green : (Color)colors.lightRed;

        // The GPS to the vehicle. In 1.0 a vehicle's street address names the building or garage it is in and is
        // empty outdoors, so an indoor vehicle gets the game's own address destination (as a business's button
        // does) and an outdoor one a point: where it stands, or where the save has it when it isn't spawned.
        internal static void GoTo(VehicleInstance instance, string name)
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
            Notices.Show(NotificationType.Success, "notification_destination_set", new Dictionary<string, string> { { "address", name } }, 4f);
            InstanceBehavior<UIs>.Instance.fullMenu.Toggle(show: false);
        }

        internal static string Where(VehicleInstance instance, VehicleState? vehicle, bool withDriver)
        {
            if (vehicle?.Transit != null)
                return vehicle.Transit.ToStopIndex is int stop
                    ? Format("foodtrucks:bizman_fleet_where_transit", ("stop", (stop + 1).ToString()))
                    : vehicle.Transit.ToSpotKey == FoodTrucks.Core.Fleet.HireSchedules.HomeSpotKey ? Text("foodtrucks:bizman_fleet_where_home")
                    : Text("foodtrucks:bizman_fleet_where_moving");
            if (withDriver) return Text("foodtrucks:bizman_fleet_where_driver");
            var place = !string.IsNullOrEmpty(instance.streetName) ? instance.Address.ToFormattedString()
                : OutdoorPlace(instance, vehicle)
                  ?? (!string.IsNullOrEmpty(instance.parkingNeighbourhood) ? Text(instance.parkingNeighbourhood) : Text("foodtrucks:bizman_fleet_place_unknown"));
            return Format(vehicle?.IsOpen == true ? "foodtrucks:bizman_fleet_where_open" : "foodtrucks:bizman_fleet_where_closed", ("place", place));
        }

        // Where a vehicle on the street stands, as the player knows the place: the route stop it is at (the one it is
        // open at, else one within the stop radius), a commissary's bay, else the nearest building's neighbourhood. The
        // game sets a vehicle's parkingNeighbourhood only for its own parking, so a cart read "an unknown spot" in the
        // bay and at its stops (the first blind playtest, 17 October 2026). Null when none is known.
        private static string? OutdoorPlace(VehicleInstance instance, VehicleState? vehicle)
        {
            try
            {
                var controller = VehicleHelper.GetVehicleController(instance);
                var position = controller != null ? controller.transform.position : (Vector3)instance.position;
                var stops = vehicle?.Schedule?.Stops;
                if (stops != null && stops.Count > 0)
                {
                    var radius = DemandData.Rules?.Fleet.StopRadiusMetres ?? 25.0;
                    var stop = vehicle!.IsOpen && vehicle.HireStop is int open && open < stops.Count ? stops[open]
                        : stops.Where(st => Distance(position, st) <= radius).OrderBy(st => Distance(position, st)).FirstOrDefault();
                    if (stop != null) return RouteStops.Label(stop);
                }
                var site = Commissary.SiteOf(instance);
                if (site != null) return Commissary.Name(site);
                string? neighbourhood = null;
                var nearest = float.MaxValue;
                foreach (var (building, doors) in World.AllBuildings())
                    foreach (var door in doors)
                    {
                        var d = (door.x - position.x) * (door.x - position.x) + (door.z - position.z) * (door.z - position.z);
                        if (d >= nearest || string.IsNullOrEmpty(building.building?.Neighbourhood)) continue;
                        nearest = d;
                        neighbourhood = building.building!.Neighbourhood;
                    }
                return neighbourhood == null ? null : Text(neighbourhood);
            }
            catch (Exception ex)
            {
                Log.Error("Street food place: " + ex.Message);
                return null;
            }
        }

        private static double Distance(Vector3 position, FoodTrucks.Core.Saves.ScheduleStop stop) =>
            Math.Sqrt((position.x - stop.X) * (position.x - stop.X) + (position.z - stop.Z) * (position.z - stop.Z));

        // The player's food vehicles, those left with the private driver included.
        internal static List<(VehicleInstance Instance, bool WithDriver)> Vehicles()
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

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);

        private static string Format(string key, params (string Key, string Value)[] values) =>
            LocalizorManager.GetLocalization(key, values.ToDictionary(v => v.Key, v => v.Value));
    }
}
