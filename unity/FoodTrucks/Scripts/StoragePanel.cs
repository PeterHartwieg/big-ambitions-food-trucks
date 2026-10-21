#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using System.Reflection;
using HarmonyLib;
using Localizor;
using Tooltip;
using UI.PlayerHUD;
using UnityEngine.UI;

namespace FoodTrucks
{
    // Storage per piece in the game's own cargo slots (the vehicle's item panel): an equipment slot's amount reads
    // "62/120" and its tooltip "Hot Dog Grill: 62/120 units" with the foods in it; the Extra Storage slot says it
    // doubles every piece; a food slot says which piece holds it, or that none does. Core's Storage decides the numbers.
    internal static class StoragePanel
    {
        public static StorageView? Of(VehicleInstance? vehicle)
        {
            var tables = DemandData.Tables;
            var kind = CargoLimit.VehicleKind(vehicle);
            return tables == null || kind == null ? null : Storage.Of(tables, kind, CargoLimit.Lines(vehicle!));
        }

        // The piece by the name of the game item fitted for it ("Hot Dog Grill", "Industrial Grill"), or its first game item.
        public static string PieceName(VehicleInstance? vehicle, EquipmentInfo piece)
        {
            var item = vehicle?.cargoInstances?.FirstOrDefault(c => c != null && piece.GameItems.Contains(c.itemName))?.itemName
                       ?? piece.GameItems.FirstOrDefault();
            return item == null ? piece.Id : LocalizorManager.GetLocalization(item, null);
        }

        // The foods a piece cooks and holds, "Burger, Chicken Kabob Skewer": those the game has now (a Bakery food only with
        // the Bakery mod). Empty for Extra Storage.
        public static string Foods(EquipmentInfo piece) =>
            string.Join(", ", piece.Products.Where(VehicleTodos.ItemExists).Select(p => LocalizorManager.GetLocalization(p, null)));

        // The name of the piece that cooks and holds a food, or null when no piece does.
        public static string? PieceFor(DemandTables tables, string food)
        {
            var piece = tables.EquipmentForProduct(food);
            return piece == null ? null : PieceName(null, piece);
        }

        // "Hot Dog Grill: 62/120 units", then one line per food in it.
        public static List<string> PoolLines(VehicleInstance vehicle, DemandTables tables, StoragePool pool)
        {
            var lines = new List<string>
            {
                Format("foodtrucks:storage_piece", ("piece", PieceName(vehicle, pool.Piece)), ("load", pool.LoadUnits.ToString()),
                    ("capacity", pool.Capacity.ToString()))
            };
            foreach (var product in pool.Piece.Products)
            {
                if (!pool.Units.TryGetValue(product, out var units) || units <= 0) continue;
                var weight = tables.Product(product)!.StockWeight;
                lines.Add(Format(weight < 1.0 ? "foodtrucks:storage_food_weighted" : "foodtrucks:storage_food",
                    ("food", LocalizorManager.GetLocalization(product, null)), ("count", units.ToString()),
                    ("per", Math.Round(1.0 / weight).ToString("0"))));
            }
            if (pool.Load > pool.Capacity) lines.Add(Format("foodtrucks:storage_over"));
            return lines;
        }

        // One line per piece, for the BizMan fleet card and the log.
        public static string Summary(VehicleInstance vehicle, StorageView view) =>
            string.Join("; ", view.Pools.Select(p => $"{p.EquipmentId} {p.LoadUnits}/{p.Capacity}")) +
            (view.StoragePieces > 0 ? $"; storage x{view.Factor:0.#}" : "") +
            (view.Unheld.Count > 0 ? "; unheld " + string.Join(", ", view.Unheld.Select(u => $"{u.Value} {u.Key}")) : "");

        private static readonly FieldInfo? HolderField = AccessTools.Field(typeof(CargoItemUi), "_currentCargoHolder");
        private static readonly FieldInfo? AmountField = AccessTools.Field(typeof(CargoItemUi), "amountLabel");
        private static readonly FieldInfo? TooltipField = AccessTools.Field(typeof(CargoItemUi), "bundleItemsTooltip");
        private static readonly FieldInfo? PriceField = AccessTools.Field(typeof(CargoItemUi), "priceLabel");

        // The cargo slot's info button, moved from the bottom left to the bottom right on our slots. Its place in the
        // slot is mirrored (same inset from the right edge as it had from the left), so it keeps its size and height.
        private static class InfoButton
        {
            private static bool logged;

            public static void MoveRight(LocalizedListTooltip tooltip)
            {
                if (!(tooltip.transform is UnityEngine.RectTransform rect) || !(rect.parent is UnityEngine.RectTransform parent)) return;
                // A moved button carries the game's placement on itself, so it is never moved twice and goes with its slot.
                var saved = rect.GetComponent<MovedInfoButton>();
                if (saved != null && saved.Moved) return;
                if (saved == null) saved = rect.gameObject.AddComponent<MovedInfoButton>();
                saved.Save(rect);
                // Its left edge's inset in the parent, kept as the right edge's inset.
                // Anchored to the left edge (as on the game's slot), that is its anchored x less its pivot's share, whatever
                // size the slot's layout gives it later; otherwise read from where it stands now.
                var left = rect.anchorMin.x == 0f && rect.anchorMax.x == 0f
                    ? rect.anchoredPosition.x - rect.pivot.x * rect.rect.width
                    : rect.localPosition.x + rect.rect.xMin - parent.rect.xMin;
                rect.anchorMin = new UnityEngine.Vector2(1f, rect.anchorMin.y);
                rect.anchorMax = new UnityEngine.Vector2(1f, rect.anchorMax.y);
                rect.pivot = new UnityEngine.Vector2(1f, rect.pivot.y);
                rect.anchoredPosition = new UnityEngine.Vector2(-left, rect.anchoredPosition.y);
                if (logged) return;
                logged = true;
                Log.Info($"Storage panel: the cargo slots' info button moves to the bottom right on food vehicles ({left:F0} px from the edge, " +
                         $"{rect.rect.width:F0} x {rect.rect.height:F0} px, slot {parent.rect.width:F0} x {parent.rect.height:F0} px), clear of the amount.");
            }

            public static void Restore(LocalizedListTooltip tooltip)
            {
                if (!(tooltip.transform is UnityEngine.RectTransform rect)) return;
                var saved = rect.GetComponent<MovedInfoButton>();
                if (saved != null && saved.Moved) saved.Restore(rect);
            }
        }

        // Postfix on CargoItemUi.SetUp: the vehicle's cargo slot for a piece or a food gets the piece's storage.
        internal static void CargoSlotPostfix(CargoItemUi __instance, CargoItem cargoItem)
        {
            try
            {
                if (HolderField == null || AmountField == null || TooltipField == null)
                {
                    if (!failed) Log.Error("Storage panel: CargoItemUi's fields not found; the cargo slots show no storage.");
                    failed = true;
                    return;
                }
                var amountLabel = AmountField.GetValue(__instance) as Graphic;
                var tooltip = TooltipField.GetValue(__instance) as LocalizedListTooltip;
                // A slot reused for something else gets its info button back where the game put it.
                if (tooltip != null) InfoButton.Restore(tooltip);
                if (!(HolderField.GetValue(__instance) is VehicleInstance vehicle) || cargoItem == null || amountLabel == null || tooltip == null) return;
                var tables = DemandData.Tables;
                var view = Of(vehicle);
                if (tables == null || view == null) return;
                if (cargoItem.cargoInstances.Any(c => c.nestedCargoInstances != null && c.nestedCargoInstances.Count > 0)) return;
                var piece = tables.EquipmentForGameItem(cargoItem.itemName);
                List<string> lines;
                if (piece != null && piece.IsStorage)
                {
                    lines = new List<string> { Format("foodtrucks:storage_extra", ("per", view.PerPiece.ToString())) };
                    if (view.StoragePieces > 1) lines.Add(Format("foodtrucks:storage_extra_more"));
                }
                else if (piece != null)
                {
                    var pool = view.Pools.FirstOrDefault(p => p.EquipmentId == piece.Id);
                    if (pool == null) return;
                    lines = PoolLines(vehicle, tables, pool);
                    if (TextProperty(amountLabel) != null)
                    {
                        SetText(amountLabel, $"{pool.LoadUnits}/{pool.Capacity}");
                        amountLabel.gameObject.SetActive(true);
                    }
                }
                else if (tables.Product(cargoItem.itemName) != null)
                {
                    var pool = view.PoolFor(tables, cargoItem.itemName);
                    lines = pool != null
                        ? PoolLines(vehicle, tables, pool)
                        : new List<string> { Format("foodtrucks:storage_unheld", ("food", LocalizorManager.GetLocalization(cargoItem.itemName, null))) };
                }
                else return;
                // The game shows this info button only on a bundle's slot, where the amount and the "x2" count are
                // hidden, so it sits over them at the slot's bottom left; on our slots both show ("x1", "120/120"). It
                // moves to the bottom right, which a paid slot leaves empty; an unpaid slot shows its price there and
                // gets no button (the tooltip comes back once it is paid).
                if (PriceField?.GetValue(__instance) is UnityEngine.Component price && price.gameObject.activeSelf) return;
                tooltip.list = lines;
                InfoButton.MoveRight(tooltip);
                tooltip.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                if (!failed)
                {
                    failed = true;
                    Log.Error("Storage panel: " + ex);
                }
            }
        }

        // OnSellClick builds its question from the amount label ("Sell (62/120) Hotdog Grill?"). For that moment the label
        // shows what the game would (the slot's item count, shown only above 1); the finalizer puts "62/120" back.
        internal static void SellPrefix(CargoItemUi __instance, CargoItem cargoItem, out (string Text, bool Active)? __state)
        {
            __state = null;
            try
            {
                if (AmountField == null || HolderField == null || cargoItem == null) return;
                if (!(HolderField.GetValue(__instance) is VehicleInstance) || !(AmountField.GetValue(__instance) is Graphic label)) return;
                var text = TextOf(label);
                if (text == null || !text.Contains("/")) return;
                __state = (text, label.gameObject.activeSelf);
                var count = cargoItem.cargoInstances.Sum(c => c.amount);
                SetText(label, count.ToString());
                label.gameObject.SetActive(count > 1);
            }
            catch (Exception ex) { Log.Error("Storage panel: sell label: " + ex); }
        }

        internal static Exception? SellFinalizer(Exception? __exception, CargoItemUi __instance, (string Text, bool Active)? __state)
        {
            try
            {
                if (__state.HasValue && AmountField?.GetValue(__instance) is Graphic label)
                {
                    SetText(label, __state.Value.Text);
                    label.gameObject.SetActive(__state.Value.Active);
                }
            }
            catch (Exception ex) { Log.Error("Storage panel: sell label: " + ex); }
            return __exception;
        }

        private static PropertyInfo? TextProperty(Graphic label)
        {
            var text = label.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            return text != null && text.PropertyType == typeof(string) ? text : null;
        }

        private static string? TextOf(Graphic label) => TextProperty(label)?.GetValue(label) as string;

        private static void SetText(Graphic label, string value) => TextProperty(label)?.SetValue(label, value);

        private static bool failed;

        private static string Format(string key, params (string Key, string Value)[] values)
        {
            var text = LocalizorManager.GetLocalization(key, null);
            foreach (var (k, v) in values) text = text.Replace("{" + k + "}", v);
            return text;
        }
    }

    // On a cargo slot's info button that StoragePanel moved: the game's placement, put back when the slot is reused.
    internal sealed class MovedInfoButton : UnityEngine.MonoBehaviour
    {
        private UnityEngine.Vector2 anchorMin, anchorMax, pivot, position;

        public bool Moved { get; private set; }

        public void Save(UnityEngine.RectTransform rect)
        {
            anchorMin = rect.anchorMin;
            anchorMax = rect.anchorMax;
            pivot = rect.pivot;
            position = rect.anchoredPosition;
            Moved = true;
        }

        public void Restore(UnityEngine.RectTransform rect)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            Moved = false;
        }
    }
}
