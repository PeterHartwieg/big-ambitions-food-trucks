#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The game's own BizMan parts, copied, for the Street food pages (Peter, 15 October 2026: the page must look like the
    // game's). A box is a copy of the warehouse list's `WhiteBox (Template)`: its title (WarehouseName), its address line
    // with the Set destination button, its section labels (VehicleSlots), its three-column rows (VehicleSlotEntry), its
    // item rows (InventoryEntry: name, amount, note, splitter) and its text button (ManageDrivers). Fonts, sizes, colours
    // and spacing are the template's; the page only sets text, colour and clicks. TextMeshPro isn't a compile reference,
    // so text goes through the localization component or the `text` property, and colour through Graphic.
    internal static class BizManParts
    {
        private const string TitlePath = "WarehouseName";
        private const string BackgroundPath = "Background";
        private const string AddressPath = "Address";
        private const string DestinationPath = "Address/SetDestinationButton";
        private const string SectionPath = "VehicleSlots";
        private const string SlotListPath = "VehicleSlotsList";
        private const string SlotEntryName = "VehicleSlotEntry";
        private const string ItemListPath = "InventoryList";
        private const string ItemEntryName = "InventoryEntry";
        private const string ButtonPath = "ManageDrivers";
        private const string ButtonButtonPath = "ManageDrivers/ManageDriversButton";

        private static Transform? template;

        public static bool Ready => template != null;

        // Keeps a copy of the warehouse box under the page's inactive holder. Returns what is missing (empty when all is
        // there); the title is required, the rest degrades.
        internal static List<string> Take(Transform warehouseBox, Transform holder)
        {
            var missing = new List<string>();
            var copy = Object.Instantiate(warehouseBox.gameObject, holder).transform;
            copy.name = "FoodTrucks.BoxTemplate";
            copy.gameObject.SetActive(false);
            BizManFleet.StripLogic(copy);
            foreach (var path in new[] { TitlePath, BackgroundPath, AddressPath, DestinationPath, SectionPath, SlotListPath + "/" + SlotEntryName,
                         ItemListPath + "/" + ItemEntryName, ButtonButtonPath })
                if (copy.Find(path) == null) missing.Add("WhiteBox " + path);
            if (copy.Find(TitlePath) == null)
            {
                Object.Destroy(copy.gameObject);
                return missing;
            }
            // The game fills copies of these rows inside each box; the template keeps only its own row templates.
            foreach (var (list, entry) in new[] { (SlotListPath, SlotEntryName), (ItemListPath, ItemEntryName) })
            {
                var parent = copy.Find(list);
                if (parent == null) continue;
                foreach (Transform child in parent.Cast<Transform>().ToList())
                    if (child.name != entry) Object.DestroyImmediate(child.gameObject);
            }
            if (copy.GetComponent<LayoutGroup>() == null)
                missing.Add("a layout group on the WhiteBox (one added)");
            template = copy;
            return missing;
        }

        internal static void Reset() => template = null;

        // A text button like the warehouse box's Manage drivers, outside a box (the page's Back).
        internal static Button? LooseButton(Transform parent, string caption, Action click)
        {
            var source = template?.Find(ButtonButtonPath);
            if (source == null) return null;
            var copy = Object.Instantiate(source.gameObject, parent);
            copy.name = "FoodTrucks.Button";
            copy.SetActive(true);
            var button = copy.GetComponent<Button>();
            if (button == null) return null;
            Caption(button, caption);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Guard(click));
            return button;
        }

        internal static void Caption(Button button, string caption)
        {
            var label = button.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault();
            if (label != null) SetText(label.transform, caption);
            else SetText(button.GetComponentsInChildren<Graphic>(true).FirstOrDefault(g => HasText(g))?.transform, caption);
        }

        // ---- A box ----

        internal sealed class Box
        {
            private readonly Transform root;
            private Transform? rows, items, lastItem;

            internal Box(Transform parent, string title)
            {
                if (template == null) throw new InvalidOperationException("The box template isn't there.");
                root = Object.Instantiate(template.gameObject, parent).transform;
                root.name = "FoodTrucks.Box";
                if (root.GetComponent<LayoutGroup>() == null)
                {
                    var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
                    layout.childControlWidth = layout.childControlHeight = true;
                    layout.childForceExpandWidth = true;
                    layout.childForceExpandHeight = false;
                }
                // The warehouse box opens its business page; a vehicle's box does nothing on a click.
                var click = root.GetComponent<Button>();
                if (click != null)
                {
                    click.onClick = new Button.ButtonClickedEvent();
                    click.enabled = false;
                }
                // Everything but the title and the white card behind the box starts hidden; the page adds copies of the
                // parts it needs, in order. (The card is the child `Background`: hiding it left the text on BizMan's blur,
                // seen in game 15 October 2026.)
                foreach (Transform child in root)
                    child.gameObject.SetActive(child.name == TitlePath || child.name == BackgroundPath);
                SetText(root.Find(TitlePath), title);
                root.gameObject.SetActive(true);
            }

            internal Transform Root => root;

            // The address line under the title, with the Set destination button when `go` is given.
            internal void Subtitle(string text, Color? colour = null, Action? go = null)
            {
                var line = root.Find(AddressPath);
                if (line == null) return;
                line.gameObject.SetActive(true);
                SetText(line, text);
                Colour(line, colour);
                var button = root.Find(DestinationPath)?.GetComponent<Button>();
                if (button == null) return;
                button.onClick = new Button.ButtonClickedEvent();
                button.gameObject.SetActive(go != null);
                if (go != null) button.onClick.AddListener(() => Guard(go));
            }

            internal void Section(string label)
            {
                Close();
                var part = Part(SectionPath);
                if (part == null) return;
                var text = part.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault();
                SetText(text != null ? text.transform : part, label);
            }

            // A three-column row (the box's vehicle slot row: number, vehicle, driver).
            internal Transform? Row(string first, string second, string third = "", Color? secondColour = null, Color? thirdColour = null)
            {
                items = null;
                if (rows == null)
                {
                    rows = Part(SlotListPath);
                    if (rows == null) return null;
                    Hide(rows, SlotEntryName);
                }
                var entry = rows.Find(SlotEntryName);
                if (entry == null) return null;
                var row = Object.Instantiate(entry.gameObject, rows).transform;
                row.name = "FoodTrucks.Row";
                SetText(row.Find("SlotNumber"), first);
                SetText(row.Find("VehicleName"), second);
                SetText(row.Find("DriverStatus"), third);
                Colour(row.Find("VehicleName"), secondColour);
                Colour(row.Find("DriverStatus"), thirdColour);
                row.gameObject.SetActive(true);
                return row;
            }

            // A row whose value is a game dropdown (a copy of `dropdown`), placed over the row's value columns.
            internal T? DropdownRow<T>(string label, GameObject dropdown) where T : Component
            {
                var row = Row(label, "", "");
                if (row == null) return null;
                Hide(row, "VehicleName");
                Hide(row, "DriverStatus");
                var copy = Object.Instantiate(dropdown, row);
                copy.name = "FoodTrucks.Dropdown";
                copy.SetActive(true);
                var rect = (RectTransform)copy.transform;
                var element = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
                // Over the second and third columns, the label keeping the first (a slot number's width).
                var labelRect = row.Find("SlotNumber") as RectTransform;
                var start = labelRect != null && row is RectTransform rowRect && rowRect.rect.width > 1f
                    ? Mathf.Clamp(labelRect.rect.width / rowRect.rect.width, 0.25f, 0.45f)
                    : 0.35f;
                rect.anchorMin = new Vector2(start, 0f);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                // The row grows to the dropdown's own height when that is taller.
                var source = dropdown.transform as RectTransform;
                var rowElement = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
                var height = Math.Max(Height(row), source != null ? source.rect.height : 0f);
                if (height > 1f) rowElement.minHeight = rowElement.preferredHeight = height;
                return copy.GetComponent<T>();
            }

            // An item row (the box's inventory row: item, amount, days left) and its splitter. A click makes the row a button.
            internal Transform? Item(string name, string amount, string note = "", Color? amountColour = null, Color? noteColour = null, Action? click = null)
            {
                rows = null;
                if (items == null)
                {
                    items = Part(ItemListPath);
                    lastItem = null;
                    if (items == null) return null;
                    Hide(items, ItemEntryName);
                }
                var entry = items.Find(ItemEntryName);
                if (entry == null) return null;
                var row = Object.Instantiate(entry.gameObject, items).transform;
                row.name = "FoodTrucks.Item";
                SetText(row.Find("ItemName"), name);
                SetText(row.Find("Amount"), amount);
                SetText(row.Find("DaysLeft"), note);
                Colour(row.Find("Amount"), amountColour);
                Colour(row.Find("DaysLeft"), noteColour);
                Hide(row, "Splitter");
                if (lastItem != null) Show(lastItem, "Splitter");
                lastItem = row;
                if (click != null)
                {
                    var button = row.GetComponent<Button>() ?? row.gameObject.AddComponent<Button>();
                    button.onClick = new Button.ButtonClickedEvent();
                    button.onClick.AddListener(() => Guard(click));
                }
                row.gameObject.SetActive(true);
                return row;
            }

            internal Button? Button(string caption, Action click, bool interactable = true)
            {
                Close();
                var part = Part(ButtonPath);
                var button = part?.GetComponentInChildren<Button>(true);
                if (button == null) return null;
                Caption(button, caption);
                Widen(button, part!);
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => Guard(click));
                button.interactable = interactable;
                return button;
            }

            // The game's button has a fixed width for "Manage drivers"; a longer caption ("Call Hell's Kitchen Commissary")
            // was cut off in game (15 October 2026). It grows to its caption, up to the row's width.
            private static void Widen(Button button, Transform row)
            {
                if (!(button.transform is RectTransform rect) || !(row is RectTransform rowRect)) return;
                var label = button.GetComponentsInChildren<Graphic>(true).FirstOrDefault(g => HasText(g));
                if (label == null) return;
                var wanted = LayoutUtility.GetPreferredWidth((RectTransform)label.transform) + 48f;
                var room = rowRect.rect.width > 1f ? rowRect.rect.width : wanted;
                var width = Mathf.Min(Mathf.Max(wanted, rect.rect.width), room);
                // Every button on the box's left edge, in line with the title and text: grown from its centre, buttons of
                // different captions stood at different places (seen in game 16 October 2026).
                rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
                rect.anchorMax = new Vector2(0f, rect.anchorMax.y);
                rect.pivot = new Vector2(0f, rect.pivot.y);
                rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }

            // A line of text in the address line's style, wrapping (hints, refusals, the read-only note).
            internal void Paragraph(string text, Color? colour = null)
            {
                Close();
                var part = Part(AddressPath);
                if (part == null) return;
                part.name = "FoodTrucks.Paragraph";
                Hide(part, "SetDestinationButton");
                SetText(part, text);
                Colour(part, colour);
                Wrap(part);
                NoMargin(part);
            }

            // Any other part (the stops list), at the end of the box.
            internal void Add(Transform part)
            {
                Close();
                part.SetParent(root, false);
                part.SetAsLastSibling();
            }

            private void Close()
            {
                rows = null;
                items = null;
                lastItem = null;
            }

            // A fresh copy of one of the template's parts, at the end of the box.
            private Transform? Part(string path)
            {
                var source = template?.Find(path);
                if (source == null) return null;
                var copy = Object.Instantiate(source.gameObject, root).transform;
                copy.name = source.name;
                copy.SetAsLastSibling();
                copy.gameObject.SetActive(true);
                return copy;
            }
        }

        // ---- Text and colour ----

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

        internal static void Colour(Transform? target, Color? colour)
        {
            if (target == null || colour == null) return;
            var graphic = target.GetComponent<Graphic>();
            if (graphic != null) graphic.color = colour.Value;
        }

        private static bool HasText(Graphic graphic)
        {
            var property = graphic.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.PropertyType == typeof(string);
        }

        // TMP word wrapping on and overflow shown, set by reflection; the layout then takes the text's own height.
        private static void Wrap(Transform part)
        {
            var graphic = part.GetComponent<Graphic>();
            if (graphic == null) return;
            var type = graphic.GetType();
            var wrapping = type.GetProperty("enableWordWrapping");
            if (wrapping != null && wrapping.CanWrite) wrapping.SetValue(graphic, true);
            var overflow = type.GetProperty("overflowMode");
            if (overflow != null && overflow.CanWrite && overflow.PropertyType.IsEnum)
                overflow.SetValue(graphic, Enum.Parse(overflow.PropertyType, "Overflow"));
            var element = part.GetComponent<LayoutElement>();
            if (element != null) element.preferredHeight = element.minHeight = -1f;
        }

        // The address line leaves room on its left for the destination button; a paragraph made from it starts at the
        // box's left edge like the title (it stood indented in game, 16 October 2026).
        private static void NoMargin(Transform part)
        {
            var graphic = part.GetComponent<Graphic>();
            var margin = graphic == null ? null : graphic.GetType().GetProperty("margin", BindingFlags.Instance | BindingFlags.Public);
            if (margin != null && margin.CanWrite && margin.PropertyType == typeof(Vector4)) margin.SetValue(graphic, Vector4.zero);
        }

        private static float Height(Transform part) => part is RectTransform rect ? rect.rect.height : 0f;

        private static void Hide(Transform root, string path)
        {
            var part = root.Find(path);
            if (part != null) part.gameObject.SetActive(false);
        }

        private static void Show(Transform root, string path)
        {
            var part = root.Find(path);
            if (part != null) part.gameObject.SetActive(true);
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error("BizMan Street food: " + ex); }
        }
    }
}
