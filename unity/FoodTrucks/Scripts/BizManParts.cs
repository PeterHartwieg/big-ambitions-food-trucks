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

        // ---- Shared layout ----

        // One set of measures for every Street food page (the overview's columns, a vehicle's Overview and Week plan), so
        // edges line up down the page and columns across cards (Peter, 16 October 2026: "a bunch of small things are at
        // different positions instead of having clear boundaries"). Everything inside a box starts at Pad from its left
        // edge and ends Pad from its right; nothing adds an offset of its own.
        internal static class Layout
        {
            // Inside a box: left, right, top and bottom.
            internal const int Pad = 16;
            // Between boxes in a column, between columns side by side, and between tabs.
            internal const float BoxGap = 16f;
            // Between the parts of a box, between cards in a list and between controls in a row.
            internal const float Gap = 8f;
            // A label and value row (Box.Row, Box.DropdownRow): the label column ends and the values start here, as a
            // share of the box's inner width; the second value column starts at SecondValue.
            internal const float ValueStart = 0.4f;
            internal const float SecondValue = 0.72f;
            // The space a cell leaves before the next column.
            internal const float CellGap = 8f;
            // A control in a fixed column (a stop's time dropdown).
            internal const float TimeWidth = 200f;
            // A tab row's height and a slim row's (drive rows), in the box's section-label height.
            internal static float TabHeight => SmallLabelHeight() * 1.8f;
            internal static float SlimHeight => SmallLabelHeight() * 1.2f;
            // A slim row's icon and the text after it.
            internal static float IconSize => SmallLabelHeight() * 0.8f;
            internal static float IconIndent => SmallLabelHeight() * 1.2f;

            // A vertical or horizontal stack (a column's content, the route list) with the given gap and no padding.
            internal static void Stack(HorizontalOrVerticalLayoutGroup? group, float spacing)
            {
                if (group == null) return;
                group.padding = new RectOffset(0, 0, 0, 0);
                group.spacing = spacing;
                group.childControlWidth = true;
                group.childForceExpandWidth = true;
            }
        }

        // The game's hover zoom off on a copied card (ButtonEffects on a box or a stop card): Peter, 16 October 2026, no
        // zoom on the page's cards at all.
        internal static void NoHover(Transform root)
        {
            foreach (var effects in root.GetComponents<MonoBehaviour>())
                if (effects != null && effects.GetType().Name == "ButtonEffects") effects.enabled = false;
            root.localScale = Vector3.one;
        }

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
                // Every part spans the box's inner width, Pad in from both edges (the template's title was narrower than
                // its address line and rows, so their right edges differed).
                var layout = root.GetComponent<VerticalLayoutGroup>() ?? root.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(Layout.Pad, Layout.Pad, Layout.Pad, Layout.Pad);
                layout.spacing = Layout.Gap;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                // The warehouse box opens its business page; a vehicle's box does nothing on a click.
                var click = root.GetComponent<Button>();
                if (click != null)
                {
                    click.onClick = new Button.ButtonClickedEvent();
                    click.enabled = false;
                }
                // No hover zoom (Peter, 16 October 2026).
                NoHover(root);
                var background = root.Find(BackgroundPath)?.GetComponent<LayoutElement>();
                if (background != null) background.ignoreLayout = true;
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
                // The label and the two values at the shared column edges, so rows line up across boxes.
                var height = Height(entry);
                var rowElement = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
                if (height > 1f) rowElement.minHeight = rowElement.preferredHeight = height;
                Column(row.Find("SlotNumber"), 0f, Layout.ValueStart);
                Column(row.Find("VehicleName"), Layout.ValueStart, Layout.SecondValue);
                Column(row.Find("DriverStatus"), Layout.SecondValue, 1f);
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
                // Over the value columns, from the shared value edge to the box's inner right edge.
                rect.anchorMin = new Vector2(Layout.ValueStart, 0f);
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

            // A table in the box's inventory list: rows at the inventory row's height with its splitter, cells made from
            // the row's item name (the header's from the section label), each cell at fixed fractions of the row's width
            // (`edges`: the left edge of each column, the last column running to the right edge), so columns line up.
            internal void TableHeader(float[] edges, params string[] cells)
            {
                var row = TableRow(edges, cells, null, null, header: true);
                if (row != null) lastItem = null;
            }

            // A body row; returns the row, its cells named FoodTrucks.Cell0, Cell1, …
            internal Transform? TableRow(float[] edges, string[] cells, Color?[]? colours, Action? click) =>
                TableRow(edges, cells, colours, click, header: false);

            private Transform? TableRow(float[] edges, string[] cells, Color?[]? colours, Action? click, bool header)
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
                var source = header ? template?.Find(SectionPath)?.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault()?.transform
                                    : entry?.Find("ItemName");
                if (entry == null || source == null) return null;
                var height = Height(entry);
                var row = Object.Instantiate(entry.gameObject, items).transform;
                row.name = header ? "FoodTrucks.TableHeader" : "FoodTrucks.TableRow";
                foreach (Transform child in row) child.gameObject.SetActive(false);
                var element = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
                if (height > 1f) element.minHeight = element.preferredHeight = height;
                for (var i = 0; i < cells.Length; i++)
                {
                    var cell = Object.Instantiate(source.gameObject, row).transform;
                    cell.name = "FoodTrucks.Cell" + i;
                    cell.gameObject.SetActive(true);
                    var cellElement = cell.GetComponent<LayoutElement>() ?? cell.gameObject.AddComponent<LayoutElement>();
                    cellElement.ignoreLayout = true;
                    var rect = (RectTransform)cell;
                    rect.anchorMin = new Vector2(edges[i], 0f);
                    rect.anchorMax = new Vector2(i + 1 < edges.Length ? edges[i + 1] : 1f, 1f);
                    rect.pivot = new Vector2(0f, 0.5f);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = new Vector2(i + 1 < edges.Length ? -Layout.CellGap : 0f, 0f);
                    SetText(cell, cells[i]);
                    if (colours != null && i < colours.Length) Colour(cell, colours[i]);
                    OneLine(cell);
                }
                if (!header)
                {
                    var splitter = row.Find("Splitter");
                    if (splitter != null) splitter.gameObject.SetActive(false);
                    if (lastItem != null) Show(lastItem, "Splitter");
                    lastItem = row;
                }
                if (click != null)
                {
                    var button = row.GetComponent<Button>() ?? row.gameObject.AddComponent<Button>();
                    button.onClick = new Button.ButtonClickedEvent();
                    button.onClick.AddListener(() => Guard(click));
                }
                row.gameObject.SetActive(true);
                return row;
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

        // A small caption in the box's section-label style ("Opens" over a stop's time), left-aligned on one line. Null
        // without the template.
        internal static Transform? SmallLabel(Transform parent, string text)
        {
            var source = template?.Find(SectionPath)?.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault()?.transform;
            if (source == null) return null;
            var copy = Object.Instantiate(source.gameObject, parent).transform;
            copy.name = "FoodTrucks.Caption";
            copy.gameObject.SetActive(true);
            SetText(copy, text);
            OneLine(copy);
            var element = copy.GetComponent<LayoutElement>() ?? copy.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = false;
            var height = Height(source);
            element.minHeight = element.preferredHeight = height > 1f ? height : 24f;
            return copy;
        }

        internal static float SmallLabelHeight()
        {
            var source = template?.Find(SectionPath)?.GetComponentsInChildren<TextLocalizationComponent>(true).FirstOrDefault()?.transform;
            var height = source == null ? 0f : Height(source);
            return height > 1f ? height : 24f;
        }

        // Centred text (a route pin's number).
        internal static void Centre(Transform part)
        {
            var graphic = part.GetComponent<Graphic>();
            var alignment = graphic == null ? null : graphic.GetType().GetProperty("alignment");
            if (alignment != null && alignment.CanWrite && alignment.PropertyType.IsEnum && Enum.IsDefined(alignment.PropertyType, "Center"))
                alignment.SetValue(graphic, Enum.Parse(alignment.PropertyType, "Center"));
        }

        // One line, cut with an ellipsis, left-aligned, no margin: a table cell.
        private static void OneLine(Transform part)
        {
            var graphic = part.GetComponent<Graphic>();
            if (graphic == null) return;
            var type = graphic.GetType();
            var wrapping = type.GetProperty("enableWordWrapping");
            if (wrapping != null && wrapping.CanWrite) wrapping.SetValue(graphic, false);
            var overflow = type.GetProperty("overflowMode");
            if (overflow != null && overflow.CanWrite && overflow.PropertyType.IsEnum)
                overflow.SetValue(graphic, Enum.Parse(overflow.PropertyType, "Ellipsis"));
            var alignment = type.GetProperty("alignment");
            if (alignment != null && alignment.CanWrite && alignment.PropertyType.IsEnum && Enum.IsDefined(alignment.PropertyType, "MidlineLeft"))
                alignment.SetValue(graphic, Enum.Parse(alignment.PropertyType, "MidlineLeft"));
            var rich = type.GetProperty("richText");
            if (rich != null && rich.CanWrite) rich.SetValue(graphic, true);
            NoMargin(part);
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

        // A cell placed by anchors between two shared edges (fractions of its row), out of the row's own layout.
        private static void Column(Transform? cell, float from, float to)
        {
            if (!(cell is RectTransform rect)) return;
            var element = cell.GetComponent<LayoutElement>() ?? cell.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
            rect.anchorMin = new Vector2(from, 0f);
            rect.anchorMax = new Vector2(to, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(to < 1f ? -Layout.CellGap : 0f, 0f);
            OneLine(cell);
        }

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
