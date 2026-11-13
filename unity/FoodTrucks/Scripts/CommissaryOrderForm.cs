#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Extensions;
using FoodTrucks.Core.Commissary;
using HarmonyLib;
using Localizor;
using Localizor.LanguageChangeEvent;
using Services;
using UI;
using UI.Dialog;
using UnityEngine;

namespace FoodTrucks
{
    internal static class CommissaryOrderForm
    {
        internal sealed class Line
        {
            public Line(string item, int box, long unitCents, long inStock)
            {
                Item = item;
                Box = box;
                UnitCents = unitCents;
                InStock = inStock;
            }

            public string Item { get; }
            public int Box { get; }
            public long UnitCents { get; }
            public long InStock { get; }
            public long BoxCents => checked(Box * UnitCents);
        }

        private sealed class Open
        {
            public Open(string contactId, string siteId, IReadOnlyList<Line> lines)
            {
                ContactId = contactId;
                SiteId = siteId;
                Lines = lines;
            }

            public string ContactId { get; }
            public string SiteId { get; }
            public IReadOnlyList<Line> Lines { get; }
            public DeliveryContractSettingsBase? Form { get; set; }
            public List<ItemsListEntry> Entries { get; set; } = new List<ItemsListEntry>();
        }

        private static Open? open;
        private static bool ready;
        private static readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        private static AccessTools.FieldRef<DeliveryContractSettingsBase, UI.Elements.Dropdown>? addressField, slotField;
        private static System.Reflection.FieldInfo? feeTextField, totalTextField;
        private static AccessTools.FieldRef<DeliveryContractSettingsBase, Dictionary<string, DeliveryItemEntry>>? entriesField;
        private static AccessTools.FieldRef<ItemsList, List<ItemsListEntry>>? listEntriesField;

        internal static readonly (Type Type, string Method)[] Needed =
        {
            (typeof(DeliveryContractSettingsBase), "Start"),
            (typeof(DeliveryContractSettingsBase), "SetUpInventoryList"),
            (typeof(DeliveryContractSettingsBase), "AddItemToList"),
            (typeof(DeliveryContractSettingsBase), "UpdateTotalPrice"),
            (typeof(FurnitureDeliveryContractSettings), "get_MaxContractAmount"),
            (typeof(FurnitureDeliveryContractSettings), "UpdateItemsListTitle")
        };

        internal static bool Ready => ready;

        internal static void CheckReady()
        {
            try
            {
                addressField = AccessTools.FieldRefAccess<DeliveryContractSettingsBase, UI.Elements.Dropdown>("addressDropdown");
                slotField = AccessTools.FieldRefAccess<DeliveryContractSettingsBase, UI.Elements.Dropdown>("deliverySlotDropdown");
                feeTextField = AccessTools.Field(typeof(DeliveryContractSettingsBase), "deliveryFeeText") ?? throw new MissingFieldException(nameof(DeliveryContractSettingsBase), "deliveryFeeText");
                totalTextField = AccessTools.Field(typeof(DeliveryContractSettingsBase), "totalPriceText") ?? throw new MissingFieldException(nameof(DeliveryContractSettingsBase), "totalPriceText");
                entriesField = AccessTools.FieldRefAccess<DeliveryContractSettingsBase, Dictionary<string, DeliveryItemEntry>>("_itemsListEntries");
                try { listEntriesField = AccessTools.FieldRefAccess<ItemsList, List<ItemsListEntry>>("_entriesList"); }
                catch (Exception ex)
                {
                    listEntriesField = null;
                    Log.Error("Commissary orders: the item list's entries can't be read; leaving the form checks the entries themselves: " + ex.Message);
                }
                var missing = Needed.Where(n => !Patched(n.Type, n.Method)).Select(n => $"{n.Type.Name}.{n.Method}").ToList();
                ready = missing.Count == 0;
                Log.Info(ready
                    ? "Commissary orders: the order form in the commissary's conversation is on."
                    : "Commissary orders: no order form (patches not active: " + string.Join(", ", missing) + "); the commissary takes no orders.");
            }
            catch (Exception ex)
            {
                ready = false;
                Log.Error("Commissary orders: no order form, the game's form has changed: " + ex.Message);
            }
        }

        private static bool Patched(Type type, string method)
        {
            var original = AccessTools.Method(type, method);
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            return info != null && info.Prefixes.Concat(info.Postfixes).Any(p => p.PatchMethod.DeclaringType == typeof(CommissaryOrderForm));
        }

        internal static void Begin(string contactId, string siteId, IReadOnlyList<Line> lines)
        {
            End("a new order form");
            open = new Open(contactId, siteId, lines);
            ContractItemsForSaleService.SetItemsForContact(contactId, lines.Select(l => l.Item));
        }

        internal static void End(string why)
        {
            var current = open;
            open = null;
            RestoreBackdrop();
            if (current == null) return;
            try
            {
                var list = InstanceBehavior<UIs>.Instance?.itemsList;
                if (list == null) return;
                var shown = list.gameObject.activeSelf;
                if (!OwnsList(current, list))
                {
                    if (shown) Log.Info($"Commissary order at {current.SiteId}: the item list shows another screen's items; left as it is ({why}).");
                    return;
                }
                list.Clear();
                list.Toggle(newState: false);
                Log.Info($"Commissary order at {current.SiteId}: its item list is {(shown ? "closed" : "emptied (it was already closed)")} ({why}).");
            }
            catch (Exception ex) { Fail("closing the item list", ex); }
        }

        private static bool OwnsList(Open current, ItemsList list)
        {
            var mine = new List<ItemsListEntry>(current.Entries);
            var form = current.Form;
            if (form != null && entriesField != null)
                mine.AddRange(entriesField(form).Values.Where(e => e != null && e.ItemsListEntry != null).Select(e => e.ItemsListEntry));
            var listed = listEntriesField?.Invoke(list);
            if (listed != null) return mine.Any(e => e != null && listed.Contains(e));
            return mine.Any(e => e != null && e.gameObject.activeSelf && e.transform.IsChildOf(list.transform));
        }

        // The item list opens over the phone with a full-screen backdrop that takes every click, so the form's Order
        // and Hang up and the phone's Close did nothing until the list was closed with its X or Esc (0.9.7 playtest,
        // step 13: one read-back, after the X; no button reacted while the list was open). While the commissary's list
        // is open its backdrop lets clicks through, so those buttons work and close the list (End). It looks the same;
        // the game's own furniture stores keep theirs.
        private static readonly List<UnityEngine.UI.Graphic> passedThrough = new List<UnityEngine.UI.Graphic>();
        private static bool backdropLogged;

        private static void LetClicksThrough(ItemsList list)
        {
            if (passedThrough.Count > 0) return;
            var root = list.GetComponentInParent<Canvas>()?.rootCanvas?.transform as RectTransform;
            if (root == null) return;
            var screen = Area(root);
            var found = new List<string>();
            foreach (var graphic in list.GetComponentsInChildren<UnityEngine.UI.Graphic>(includeInactive: false))
            {
                if (graphic == null || !graphic.raycastTarget || screen <= 0f || Area(graphic.rectTransform) < 0.8f * screen) continue;
                graphic.raycastTarget = false;
                passedThrough.Add(graphic);
                found.Add(graphic.name);
            }
            if (backdropLogged) return;
            backdropLogged = true;
            Log.Info(found.Count > 0
                ? $"Commissary orders: the item list's backdrop ({string.Join(", ", found)}) lets clicks through to the phone while the commissary's list is open."
                : "Commissary orders: no full-screen backdrop under the item list; if Order stays blocked while the list is open, something else blocks it.");
        }

        private static void RestoreBackdrop()
        {
            foreach (var graphic in passedThrough)
                if (graphic != null) graphic.raycastTarget = true;
            passedThrough.Clear();
        }

        private static float Area(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Abs((corners[2].x - corners[0].x) * (corners[2].y - corners[0].y));
        }

        internal static void Watch()
        {
            var current = open;
            if (current == null)
            {
                if (passedThrough.Count > 0) RestoreBackdrop();
                return;
            }
            var dialog = DialogController.current;
            if (dialog == null || dialog.contact?.id != current.ContactId)
            {
                Log.Info($"Commissary order at {current.SiteId}: the call ended with the order form open.");
                End("the call ended");
                return;
            }
            try
            {
                var list = InstanceBehavior<UIs>.Instance?.itemsList;
                if (list != null && list.gameObject.activeInHierarchy && OwnsList(current, list)) LetClicksThrough(list);
                else if (passedThrough.Count > 0) RestoreBackdrop();
            }
            catch (Exception ex) { Fail("item list backdrop", ex); }
        }

        internal static void Forget(IEnumerable<string> contactIds)
        {
            End("uninstall step");
            foreach (var id in contactIds) ContractItemsForSaleService.RemoveContact(id);
        }

        internal static Line? Find(string item) => open?.Lines.FirstOrDefault(l => l.Item == item);


        internal static List<CommissaryOrderRequest> Requests(DeliveryContractSettingsBase form)
        {
            var list = new List<CommissaryOrderRequest>();
            foreach (var row in form.itemsToDeliver ?? new List<DeliveryContractSettingsBase.ItemToDeliver>())
            {
                var line = Find(row.itemName);
                if (line == null) throw new InvalidOperationException($"{row.itemName} isn't on the commissary's list.");
                list.Add(new CommissaryOrderRequest(line.Item, line.Box, line.UnitCents, Math.Max(0, row.amount),
                    DemandData.Tables?.EquipmentForGameItem(line.Item) == null));
            }
            return list;
        }

        private static bool IsOurs(DeliveryContractSettingsBase form)
        {
            var current = open;
            if (current == null || !(form is FurnitureDeliveryContractSettings) || DialogController.current?.contact?.id != current.ContactId) return false;
            if (current.Form is null) current.Form = form;
            return ReferenceEquals(current.Form, form);
        }

        private static void Fail(string where, Exception ex)
        {
            if (failed.Add(where)) Log.Error($"Commissary order form, {where}: {ex}");
        }

        private static LanguageChangeEventDataHolder Label(Line line, string siteId)
        {
            var tables = DemandData.Tables;
            var equipment = tables?.EquipmentForGameItem(line.Item);
            var piece = equipment != null;
            var foods = equipment == null ? "" : StoragePanel.Foods(equipment);
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules?.Commissary;
            var limited = !piece && state != null && rules != null && !Commissary.LimitsLifted;
            var left = limited ? CommissarySupply.Left(state!.Commissary, rules!, line.Item, TimeHelper.CurrentDay + 1) : 0;
            var item = LocalizorManager.GetLocalization(line.Item, null);
            return LocalizorManager.Localize(piece ? foods.Length > 0 ? "foodtrucks:commissary_form_piece_foods" : "foodtrucks:commissary_form_piece"
                : limited ? "foodtrucks:commissary_form_item_left" : "foodtrucks:commissary_form_item", new
            {
                item,
                foods,
                box = line.Box.ToString(),
                stock = line.InStock.ToString(),
                left = left.ToString()
            });
        }

        internal static void StartPostfix(DeliveryContractSettingsBase __instance)
        {
            try
            {
                var current = open;
                if (current == null || !IsOurs(__instance)) return;
                var site = DemandData.Commissaries?.Site(current.SiteId);
                var rules = DemandData.Rules?.Commissary;
                var address = addressField!(__instance);
                address.onOptionSelected.RemoveAllListeners();
                address.SetOptions(new List<string> { site != null ? Commissary.Name(site) : current.SiteId }, localize: false, selectedOption: 0);
                address.SetInteractable(false);
                var slot = slotField!(__instance);
                slot.onOptionSelected.RemoveAllListeners();
                var minute = rules?.DeliveryMinute ?? 480;
                slot.SetOptions(new List<string> { DeliveryContractSettingsBase.GetDeliverySlotLabel(TimeHelper.CurrentDay + 1, minute / 60) }, localize: false, selectedOption: 0);
                slot.SetInteractable(false);
                ShowTotals(__instance);
            }
            catch (Exception ex) { Fail("start", ex); }
        }

        internal static void InventoryPostfix(DeliveryContractSettingsBase __instance)
        {
            try
            {
                var current = open;
                if (current == null || !IsOurs(__instance)) return;
                var entries = entriesField!(__instance);
                current.Entries = entries.Values.Where(e => e != null && e.ItemsListEntry != null).Select(e => e.ItemsListEntry).ToList();
                var shown = current.Lines.Where(l => entries.TryGetValue(l.Item, out var e) && e != null).Select(l => (Line: l, Entry: entries[l.Item])).ToList();
                var first = shown.Count == 0 ? 0 : shown.Min(s => s.Entry.ItemsListEntry.transform.GetSiblingIndex());
                for (var i = 0; i < shown.Count; i++)
                {
                    var (line, entry) = shown[i];
                    entry.Init(Label(line, current.SiteId), Icon(line.Item), line.BoxCents / 100f);
                    FitName(entry.ItemsListEntry);
                    entry.ItemsListEntry.transform.SetSiblingIndex(first + i);
                }
            }
            catch (Exception ex) { Fail("item list", ex); }
        }

        private static FieldInfo? nameLabelField;

        private static void FitName(ItemsListEntry entry)
        {
            nameLabelField ??= AccessTools.Field(typeof(ItemsListEntry), "nameLabel");
            var label = nameLabelField?.GetValue(entry) as Component;
            var graphic = label == null ? null : label.GetComponent<UnityEngine.UI.Graphic>();
            if (graphic == null)
            {
                if (failed.Add("fit names")) Log.Error("Commissary order form: no name text found on the item list's rows; long names aren't fitted.");
                return;
            }
            var type = graphic.GetType();
            void Set(string property, object value)
            {
                var info = type.GetProperty(property);
                if (info != null && info.CanWrite) info.SetValue(graphic, value);
            }
            Set("enableWordWrapping", true);
            var overflow = type.GetProperty("overflowMode");
            if (overflow != null && overflow.CanWrite && overflow.PropertyType.IsEnum)
                overflow.SetValue(graphic, Enum.Parse(overflow.PropertyType, "Ellipsis"));
            if (type.GetProperty("fontSize")?.GetValue(graphic) is float size && size > 0f)
            {
                Set("fontSizeMax", size);
                Set("fontSizeMin", size * 0.75f);
                Set("enableAutoSizing", true);
            }
        }

        private const string IconFolder = "Assets/Mods/FoodTrucks/Vehicles/EquipmentArt/Icons/";
        private static readonly Dictionary<string, Sprite?> icons = new Dictionary<string, Sprite?>(StringComparer.Ordinal);

        internal static Sprite Icon(string item)
        {
            var display = DemandData.Tables?.EquipmentForGameItem(item)?.Display;
            if (string.IsNullOrEmpty(display)) return ItemHelper.GetIconWithFallback(item);
            if (!icons.TryGetValue(display!, out var sprite))
            {
                sprite = FoodTrucksMod.Bundle?.LoadAsset<Sprite>(IconFolder + "Icon_" + display + ".png");
                icons[display!] = sprite;
                if (sprite == null) Log.Info($"Commissary order form: no icon Icon_{display} in the mod's bundle; {item} shows the game's icon.");
            }
            return sprite != null ? sprite : ItemHelper.GetIconWithFallback(item);
        }

        internal static void AddItemPostfix(DeliveryContractSettingsBase __instance, string itemName)
        {
            try
            {
                if (!IsOurs(__instance)) return;
                var line = Find(itemName);
                var row = __instance.itemsToDeliver?.FirstOrDefault(r => r.itemName == itemName);
                if (line == null || row == null) return;
                row.price = line.BoxCents / 100f;
                if (row.row != null)
                    row.row.GetLanguageChangeEventByName("ItemName").Arguments = new { itemName, itemPrice = CartSelling.Money(line.BoxCents) };
                ShowTotals(__instance);
            }
            catch (Exception ex) { Fail("new row", ex); }
        }

        internal static void TotalPostfix(DeliveryContractSettingsBase __instance)
        {
            try
            {
                if (IsOurs(__instance)) ShowTotals(__instance);
            }
            catch (Exception ex) { Fail("total", ex); }
        }

        internal static void MaxAmountPostfix(DeliveryContractSettingsBase __instance, ref int __result)
        {
            try
            {
                if (IsOurs(__instance)) __result = CommissaryOrdering.MaxBoxesPerOrder;
            }
            catch (Exception ex) { Fail("box limit", ex); }
        }

        internal static void TitlePostfix(DeliveryContractSettingsBase __instance)
        {
            try
            {
                var current = open;
                if (current == null || !IsOurs(__instance)) return;
                var site = DemandData.Commissaries?.Site(current.SiteId);
                var boxes = __instance.itemsToDeliver?.Sum(r => r.amount) ?? 0;
                InstanceBehavior<UIs>.Instance?.itemsList?.SetTitle(string.Format(LocalizorManager.GetLocalization("foodtrucks:commissary_form_title", null),
                    site != null ? Commissary.Name(site) : current.SiteId, boxes, CommissaryOrdering.MaxBoxesPerOrder));
            }
            catch (Exception ex) { Fail("list title", ex); }
        }

        private static void ShowTotals(DeliveryContractSettingsBase form)
        {
            var current = open;
            var quote = current == null ? null : Commissary.QuoteFor(current.SiteId, Requests(form));
            if (quote == null) return;
            SetText(feeTextField!.GetValue(form) as Component, CartSelling.Money(quote.FeeCents));
            SetText(totalTextField!.GetValue(form) as Component, CartSelling.Money(quote.TotalCents));
        }

        private static void SetText(Component? label, string text)
        {
            if (label == null) return;
            var property = label.GetType().GetProperty("text");
            if (property == null || !property.CanWrite) throw new MissingMemberException(label.GetType().Name, "text");
            property.SetValue(label, text);
        }
    }
}
