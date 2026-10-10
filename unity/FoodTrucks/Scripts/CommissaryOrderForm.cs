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
        }

        private static Open? open;
        private static bool ready;
        private static readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        private static AccessTools.FieldRef<DeliveryContractSettingsBase, UI.Elements.Dropdown>? addressField, slotField;
        // TextMesh Pro labels, read as plain components so this assembly needn't reference TextMesh Pro.
        private static System.Reflection.FieldInfo? feeTextField, totalTextField;
        private static AccessTools.FieldRef<DeliveryContractSettingsBase, Dictionary<string, DeliveryItemEntry>>? entriesField;

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
            End();
            open = new Open(contactId, siteId, lines);
            ContractItemsForSaleService.SetItemsForContact(contactId, lines.Select(l => l.Item));
        }

        // Leaving the form (read back, hung up, the call closed, a new form): the item list beside the phone is the
        // form's, so it is closed and emptied, and its entries' callbacks go with them. The game's own furniture
        // order leaves it open until Esc; for the commissary it would keep changing a form whose order is already
        // read back, and after the call ends adding a row reaches DialogController.current, which is null by then.
        internal static void End()
        {
            var current = open;
            open = null;
            if (current?.Form == null) return;
            try
            {
                var list = InstanceBehavior<UIs>.Instance?.itemsList;
                if (list == null || !OwnsList(current.Form, list)) return;
                list.Clear();
                list.Toggle(newState: false);
            }
            catch (Exception ex) { Fail("closing the item list", ex); }
        }

        // Whether the item list still holds this form's entries (another screen may have filled it since).
        private static bool OwnsList(DeliveryContractSettingsBase form, ItemsList list)
        {
            var entries = entriesField?.Invoke(form);
            return entries != null && entries.Values.Any(e => e != null && e.ItemsListEntry != null && e.ItemsListEntry.transform.IsChildOf(list.transform));
        }

        // Every frame (Commissary.Update): a form whose call has gone (the phone closed, another call) is left. The
        // game's ContactsApp hangs up on closing without calling the entry's OnCancel, so this is the only notice.
        internal static void Watch()
        {
            var current = open;
            if (current == null) return;
            var dialog = DialogController.current;
            if (dialog != null && dialog.contact?.id == current.ContactId) return;
            Log.Info($"Commissary order at {current.SiteId}: the call ended with the order form open; its item list is closed.");
            End();
        }

        // After the uninstall step the contacts are gone; so is what they sold.
        internal static void Forget(IEnumerable<string> contactIds)
        {
            End();
            foreach (var id in contactIds) ContractItemsForSaleService.RemoveContact(id);
        }

        internal static Line? Find(string item) => open?.Lines.FirstOrDefault(l => l.Item == item);

        // Whether this is the form the open order belongs to (the read-back reads it).
        internal static bool IsCurrent(DeliveryContractSettingsBase? form) => form != null && open?.Form is { } bound && ReferenceEquals(bound, form);

        // The form's rows as Core's order requests: an amount is a number of boxes.
        internal static List<CommissaryOrderRequest> Requests(DeliveryContractSettingsBase form)
        {
            var list = new List<CommissaryOrderRequest>();
            foreach (var row in form.itemsToDeliver ?? new List<DeliveryContractSettingsBase.ItemToDeliver>())
            {
                var line = Find(row.itemName);
                if (line == null) throw new InvalidOperationException($"{row.itemName} isn't on the commissary's list.");
                list.Add(new CommissaryOrderRequest(line.Item, line.Box, line.UnitCents, Math.Max(0, row.amount)));
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
        private static LanguageChangeEventDataHolder Label(Line line) =>
            LocalizorManager.Localize("foodtrucks:commissary_form_item", new
            {
                item = LocalizorManager.GetLocalization(line.Item, null),
                box = line.Box.ToString(),
                stock = line.InStock.ToString()
            });

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
                var shown = current.Lines.Where(l => entries.TryGetValue(l.Item, out var e) && e != null).Select(l => (Line: l, Entry: entries[l.Item])).ToList();
                // The game lists them by item key; they take the same places in the list, in the catalogue's order.
                var first = shown.Count == 0 ? 0 : shown.Min(s => s.Entry.ItemsListEntry.transform.GetSiblingIndex());
                for (var i = 0; i < shown.Count; i++)
                {
                    var (line, entry) = shown[i];
                    entry.Init(Label(line), ItemHelper.GetIconWithFallback(line.Item), line.BoxCents / 100f);
                    entry.ItemsListEntry.transform.SetSiblingIndex(first + i);
                }
            }
            catch (Exception ex) { Fail("item list", ex); }
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

        // The delivery fee and total as Core prices the rows (one order, and one fee, per item).
        private static void ShowTotals(DeliveryContractSettingsBase form)
        {
            var rules = DemandData.Rules?.Commissary;
            if (rules == null) return;
            var quote = CommissaryOrdering.Quote(rules, Requests(form));
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
