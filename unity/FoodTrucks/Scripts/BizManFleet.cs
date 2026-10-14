#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Entities;
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
    // A dedicated Street food page in BizMan, built from basic UI components. The business-list entry
    // has no building registration; its patches keep the game's address-based business flow away from it.
    // The fleet and vehicle pages share our scrolling content, independent of warehouse cards or loaders.
    internal static class BizManFleet
    {
        private const string EntryId = "foodtrucks:streetfood";
        private const string BusinessesStatus = "common_businesses";

        private static bool ready, broken;
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        // Cells whose destination button the Street food line hid; a recycled cell gets it back.
        private static readonly HashSet<int> hiddenButtons = new HashSet<int>();
        private static Transform? section, content;
        private static BizManList? builtFor;
        private static Font? font;
        private static readonly List<GameObject> hiddenListParts = new List<GameObject>();
        private static readonly Color PageColour = new Color(0.94f, 0.95f, 0.97f, 1f);
        private static readonly Color CardColour = Color.white;

        // Called once after patching: the line shows only when every patch that keeps it safe is in place.
        internal static void CheckReady()
        {
            ready = Has(typeof(BusinessScrollerController), "PopulateAllModels") && Has(typeof(BizManFilterToggle), "PassesFilter") &&
                    Has(typeof(BusinessCellView), "SetData") && Has(typeof(BizManList), "OpenBusiness") &&
                    Has(typeof(BizManList), "OnDisable");
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
                CloseSection();
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

        // A click on the line opens the fleet section instead of a business page, and a second click closes it (the
        // game's warehouse section comes back). Returning false skips the game's handler, which would open BizMan's
        // business page for an address the line doesn't have.
        public static bool OpenPrefix(BizManList __instance, BusinessCellView.BusinessModel businessModel)
        {
            if (!IsOurs(businessModel)) return true;
            try
            {
                if (section != null && section.gameObject.activeSelf)
                {
                    BizManVehiclePage.Close();
                    CloseSection();
                }
                else Open(__instance);
            }
            catch (Exception ex) { Fail("fleet section", ex, Traverse.Create(__instance).Field("businessScrollerController").GetValue<BusinessScrollerController>()); }
            return false;
        }

        // Leaving the list closes the section, and a vehicle's page with it.
        public static void ListClosedPostfix()
        {
            BizManVehiclePage.Close();
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
            if (section != null) section.gameObject.SetActive(false);
            foreach (var part in hiddenListParts)
                if (part != null) part.SetActive(true);
            hiddenListParts.Clear();
        }

        internal static void Reset()
        {
            CloseSection();
            section = content = null;
            builtFor = openList = null;
            hiddenListParts.Clear();
            hiddenButtons.Clear();
            broken = false;
            logged.Clear();
        }

        // ---- The fleet section ----

        private static BizManList? openList;

        // The section's card list, built if it isn't yet (the vehicle page sits in it, in place of the cards); null if
        // it can't be.
        internal static Transform? EnsureCardList(BizManList list) => ready && !broken && Ensure(list) ? content : null;

        // Opens the section, filled afresh and scrolled to (the vehicle page's Back, and opening a page from elsewhere).
        internal static void ShowSection(BizManList list)
        {
            try { Open(list); }
            catch (Exception ex) { Fail("fleet section", ex, Traverse.Create(list).Field("businessScrollerController").GetValue<BusinessScrollerController>()); }
        }

        private static void Open(BizManList list)
        {
            if (!Ensure(list) || section == null || content == null) return;
            openList = list;
            Fill();
            ShowSectionView();
            HireRoutes.Later(() =>
            {
                try { ScrollTo(section); }
                catch (Exception ex) { Log.Error(ex); }
            });
        }

        // The page covers the BizMan list while open; the app's own navigation remains outside it.
        private static bool Ensure(BizManList list)
        {
            if (section != null && content != null && builtFor == list) return true;
            CloseSection();
            if (section != null) UnityEngine.Object.Destroy(section.gameObject);
            font = list.GetComponentsInChildren<Text>(true).Select(t => t.font).FirstOrDefault(f => f != null)
                   ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = Rect("FoodTrucks.StreetFoodPage", list.transform);
            root.gameObject.SetActive(false);
            Stretch(root);
            root.gameObject.AddComponent<Image>().color = PageColour;
            section = root;
            builtFor = list;

            var header = Rect("Header", root);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(16f, -68f);
            header.offsetMax = new Vector2(-16f, -12f);
            var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 16f;
            headerLayout.childControlWidth = headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;
            var back = ActionButton(header, Text("foodtrucks:bizman_fleet_back"), () => { BizManVehiclePage.Close(); CloseSection(); });
            back.GetComponent<LayoutElement>().preferredWidth = 110f;
            var title = Label(header, Text("foodtrucks:bizman_fleet_header"), 26);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var scrollRoot = Rect("FleetScroll", root);
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(12f, 12f);
            scrollRoot.offsetMax = new Vector2(-12f, -80f);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            var viewport = Rect("Viewport", scrollRoot);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(-18f, 0f);
            viewport.gameObject.AddComponent<Image>().color = PageColour;
            viewport.gameObject.AddComponent<RectMask2D>();
            var body = Rect("Content", viewport);
            body.anchorMin = new Vector2(0f, 1f);
            body.anchorMax = Vector2.one;
            body.pivot = new Vector2(0.5f, 1f);
            body.offsetMin = body.offsetMax = Vector2.zero;
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(4, 4, 4, 12);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = body;
            content = body;

            var bar = Rect("Scrollbar", scrollRoot);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = Vector2.one;
            bar.offsetMin = new Vector2(-12f, 0f);
            bar.offsetMax = Vector2.zero;
            bar.gameObject.AddComponent<Image>().color = CardColour;
            var handle = Rect("Handle", bar);
            Stretch(handle);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.55f, 0.60f, 0.65f, 1f);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scroll.verticalScrollbar = scrollbar;
            Log.Info("BizMan: dedicated Street food page built with its own cards and scroll view.");
            return true;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Text Label(Transform parent, string value, int size = 18)
        {
            var rect = Rect("Label", parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = new Color(0.13f, 0.17f, 0.21f, 1f);
            text.text = value;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button ActionButton(Transform parent, string caption, Action action)
        {
            var rect = Rect("Button", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.25f, 0.32f, 0.40f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Guard(action));
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 40f;
            var text = Label(rect, caption);
            Stretch(text.rectTransform);
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            return button;
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

        private static void Fill()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || content == null) return;
            foreach (Transform child in content)
            {
                if (child.name != "FoodTrucks.VehicleCard") continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
            var names = StreetFoodBooks.VehicleNames();
            foreach (var vehicle in Vehicles().OrderBy(v => names.TryGetValue(v.Instance.id, out var n) ? n : v.Instance.id, StringComparer.Ordinal))
            {
                var card = Rect("FoodTrucks.VehicleCard", content);
                card.gameObject.AddComponent<Image>().color = CardColour;
                var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(16, 16, 12, 16);
                layout.spacing = 8f;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                FillCard(card, state, vehicle.Instance, vehicle.WithDriver, names.TryGetValue(vehicle.Instance.id, out var name) ? name : vehicle.Instance.id);
            }
            var scroll = content.GetComponentInParent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private static void FillCard(Transform card, SaveState state, VehicleInstance instance, bool withDriver, string name)
        {
            var vehicle = StreetSales.Find(state, instance.id);
            var today = SaveGameManager.Current.Day;
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;

            ActionButton(card, name, () =>
            {
                if (openList != null) BizManVehiclePage.Show(openList, instance.id);
            });
            Label(card, Where(instance, vehicle, withDriver));
            if (!withDriver && vehicle?.Transit == null)
                ActionButton(card, Text("foodtrucks:bizman_fleet_destination"), () => GoTo(instance, name));

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
            foreach (var (label, first, second, loss) in rows)
            {
                var line = Label(card, label + "   " + first + (string.IsNullOrEmpty(second) ? "" : "   " + second));
                if (loss) line.color = (Color)colors.lightRed;
            }

            // The inventory table: units sold today and units left of each item, most sold first, like the game's
            // sold-amount tooltip.
            var sold = SoldCounts.On(vehicle, today);
            var tables = DemandData.Tables;
            var stock = tables == null ? new Dictionary<string, int>() : CartDemand.StockOf(instance, CartDemand.MenuOf(tables, instance).Menu);
            var items = sold.Select(s => s.Item).Concat(stock.Where(s => s.Value > 0).Select(s => s.Key).OrderBy(i => i, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).ToList();
            var soldOf = sold.ToDictionary(s => s.Item, s => s.Units, StringComparer.Ordinal);
            if (items.Count == 0) Label(card, Text("foodtrucks:bizman_fleet_nothing"));
            foreach (var item in items)
            {
                soldOf.TryGetValue(item, out var units);
                stock.TryGetValue(item, out var left);
                var line = Label(card, Text(item) + "   " + Format("foodtrucks:bizman_fleet_sold", ("count", units.ToString())) +
                                       "   " + Format("foodtrucks:bizman_fleet_left", ("count", left.ToString())));
                if (left == 0) line.color = (Color)colors.lightRed;
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
            Notices.Show(NotificationType.Success, "notification_destination_set", new Dictionary<string, string> { { "address", name } }, 4f);
            InstanceBehavior<UIs>.Instance.fullMenu.Toggle(show: false);
        }

        internal static string Where(VehicleInstance instance, VehicleState? vehicle, bool withDriver)
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
        internal static void SetText(Transform? target, string text)
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
