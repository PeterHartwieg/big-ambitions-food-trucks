#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
    // The order form inside a commissary's text conversation. It is the game's own phone order form, the one Square
    // Appliances and the other furniture stores show in their conversation (InputTemplate FurnitureDeliverySettings,
    // run by FurnitureDeliveryContractSettings): a delivery address, a delivery time, rows of items with amounts picked
    // from the game's item list beside the phone, the delivery fee and the total. The game lets a contact sell its own
    // list of items (ContractItemsForSaleService.SetItemsForContact). The rest of the form is the store's, so while it
    // shows for a commissary contact, patches make it the commissary's: the address is the commissary, the time is
    // tomorrow at the delivery hour, an amount is a number of boxes (at most Core's MaxBoxesPerOrder), prices are box
    // prices at wholesale, and the fee and total are Core's. Nothing changes for the game's own stores.
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

        // The form now open for a commissary: its contact, its commissary and what it sells (foods first, then machines).
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
            // The form instance this order belongs to: the first commissary form the patches see after Begin. A form
            // from an earlier step of the call (its picker callbacks, say) is someone else's and is left alone.
            public DeliveryContractSettingsBase? Form { get; set; }
            // The entries this form put in the item list beside the phone, kept here rather than read from the form:
            // closing the phone destroys the conversation, the form with it, while the list and its entries stay.
            public List<ItemsListEntry> Entries { get; set; } = new List<ItemsListEntry>();
        }

        private static Open? open;
        private static bool ready;
        private static readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        private static AccessTools.FieldRef<DeliveryContractSettingsBase, UI.Elements.Dropdown>? addressField, slotField;
        // TextMesh Pro labels, read as plain components so this assembly needn't reference TextMesh Pro.
        private static System.Reflection.FieldInfo? feeTextField, totalTextField;
        private static AccessTools.FieldRef<DeliveryContractSettingsBase, Dictionary<string, DeliveryItemEntry>>? entriesField;
        // The item list's own entries (ItemsList._entriesList), emptied at once by Clear; null if the field is gone.
        private static AccessTools.FieldRef<ItemsList, List<ItemsListEntry>>? listEntriesField;

        // The patches that make the store's form the commissary's, by method; the form is used only when all are active.
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

        // Called once after patching: the fields the patches read, and every patch in place.
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

        // Before the conversation shows the form: what it sells, for the game's form to list.
        internal static void Begin(string contactId, string siteId, IReadOnlyList<Line> lines)
        {
            End("a new order form");
            open = new Open(contactId, siteId, lines);
            ContractItemsForSaleService.SetItemsForContact(contactId, lines.Select(l => l.Item));
        }

        // Leaving the form (read back, hung up, the call closed, a new form): the item list beside the phone is the
        // form's, so it is closed and emptied, and its entries' callbacks go with them. The game's own furniture
        // order leaves it open until Esc; for the commissary it would keep changing a form whose order is already
        // read back, and after the call ends adding a row reaches DialogController.current, which is null by then.
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

        // Whether the item list still holds entries this form put there: those kept when the game last filled it for
        // this form and, while the form is still there, the form's own current ones. Another screen that refilled the
        // list since has cleared them: ItemsList.Clear empties its own entry list at once, while the Destroy it calls
        // only takes effect at the frame's end, so the list's entry list is what counts, not whether ours compare null
        // yet. Works after the form itself is gone.
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

        // Every frame (Commissary.Update and LateUpdate): a form whose call has gone (the phone closed, another call) is
        // left; the game's ContactsApp hangs up on closing without calling the entry's OnCancel, so this is the only
        // notice. While the commissary's list is open, its backdrop lets clicks through to the phone.
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

        // After the uninstall step the contacts are gone; so is what they sold.
        internal static void Forget(IEnumerable<string> contactIds)
        {
            End("uninstall step");
            foreach (var id in contactIds) ContractItemsForSaleService.RemoveContact(id);
        }

        internal static Line? Find(string item) => open?.Lines.FirstOrDefault(l => l.Item == item);


        // The form's rows as Core's order requests: an amount is a number of boxes.
        internal static List<CommissaryOrderRequest> Requests(DeliveryContractSettingsBase form)
        {
            var list = new List<CommissaryOrderRequest>();
            foreach (var row in form.itemsToDeliver ?? new List<DeliveryContractSettingsBase.ItemToDeliver>())
            {
                var line = Find(row.itemName);
                if (line == null) throw new InvalidOperationException($"{row.itemName} isn't on the commissary's list.");
                // Food counts towards its weekly limit at the commissary; equipment doesn't.
                list.Add(new CommissaryOrderRequest(line.Item, line.Box, line.UnitCents, Math.Max(0, row.amount),
                    DemandData.Tables?.EquipmentForGameItem(line.Item) == null));
            }
            return list;
        }

        // The commissary's form now open: a furniture form in the commissary's call, and the instance the order is
        // bound to (the first one seen after Begin; the base Start runs SetUpInventoryList before Start's postfix).
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

        // The rows' item, box size and stock, as the item list shows them; the price beside it is the box price.
        // Equipment reads plainly, by the piece ("Hot dog grill (1 here)"); food keeps "Hot dog, box of 500 (… here)" and
        // says how many boxes of it are left under its weekly limit in the delivery's week, one limit for all commissaries
        // together (Core CommissarySupply).
        private static LanguageChangeEventDataHolder Label(Line line, string siteId)
        {
            var piece = DemandData.Tables?.EquipmentForGameItem(line.Item) != null;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules?.Commissary;
            var limited = !piece && state != null && rules != null && !Commissary.LimitsLifted;
            var left = limited ? CommissarySupply.Left(state!.Commissary, rules!, line.Item, TimeHelper.CurrentDay + 1) : 0;
            return LocalizorManager.Localize(piece ? "foodtrucks:commissary_form_piece" : limited ? "foodtrucks:commissary_form_item_left" : "foodtrucks:commissary_form_item", new
            {
                item = LocalizorManager.GetLocalization(line.Item, null),
                box = line.Box.ToString(),
                stock = line.InStock.ToString(),
                left = left.ToString()
            });
        }

        // ---- Patches (each logs Active or Not active at startup through Patches.Patch) ----

        // The address is the commissary and the time tomorrow's delivery, each the only choice; the fee and total are Core's.
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

        // The item list beside the phone: each item with its box size and stock here, at its box price, foods first.
        internal static void InventoryPostfix(DeliveryContractSettingsBase __instance)
        {
            try
            {
                var current = open;
                if (current == null || !IsOurs(__instance)) return;
                var entries = entriesField!(__instance);
                // The game has just filled the list for this form: these entries are the ones to clear on leaving.
                current.Entries = entries.Values.Where(e => e != null && e.ItemsListEntry != null).Select(e => e.ItemsListEntry).ToList();
                var shown = current.Lines.Where(l => entries.TryGetValue(l.Item, out var e) && e != null).Select(l => (Line: l, Entry: entries[l.Item])).ToList();
                // The game lists them by item key; they take the same places in the list, in the catalogue's order.
                var first = shown.Count == 0 ? 0 : shown.Min(s => s.Entry.ItemsListEntry.transform.GetSiblingIndex());
                for (var i = 0; i < shown.Count; i++)
                {
                    var (line, entry) = shown[i];
                    entry.Init(Label(line, current.SiteId), Icon(line.Item), line.BoxCents / 100f);
                    entry.ItemsListEntry.transform.SetSiblingIndex(first + i);
                }
            }
            catch (Exception ex) { Fail("item list", ex); }
        }

        // An equipment item shows the compact model a vehicle actually carries (economy.json's `display`), rendered by the
        // art pipeline as Vehicles/EquipmentArt/Icons/Icon_<display>.png in the mod's bundle, not the game's icon of the
        // full-size counter (Peter, 11 October 2026). Only this list: the game's own shops keep their icons. Without the
        // sprite (art not merged yet) the game's icon shows, logged once per piece.
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

        // A new row: its price is the box price.
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

        // An amount is a number of boxes; one order takes at most Core's maximum, all items together.
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

        // The delivery fee and total as Core prices the rows: one order per item, one flat fee for the delivery, none
        // when another order already arrives at this commissary tomorrow morning (Commissary.QuoteFor).
        private static void ShowTotals(DeliveryContractSettingsBase form)
        {
            var current = open;
            var quote = current == null ? null : Commissary.QuoteFor(current.SiteId, Requests(form));
            if (quote == null) return;
            SetText(feeTextField!.GetValue(form) as Component, CartSelling.Money(quote.FeeCents));
            SetText(totalTextField!.GetValue(form) as Component, CartSelling.Money(quote.TotalCents));
        }

        // The form's labels are TextMesh Pro texts, set through their text property.
        private static void SetText(Component? label, string text)
        {
            if (label == null) return;
            var property = label.GetType().GetProperty("text");
            if (property == null || !property.CanWrite) throw new MissingMemberException(label.GetType().Name, "text");
            property.SetValue(label, text);
        }
    }
}
