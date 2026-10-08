#nullable enable
using System;
using System.Collections.Generic;
using Extensions;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // The cart's own buttons in the item panel while the player pushes it: "Open here" and "Prices".
    // Copies of the panel's Sleep button with their own click handlers, shown only for the cart.
    internal static class CartButton
    {
        private static readonly List<Button> buttons = new List<Button>();

        public static void OnSetVehicle(ItemPanelUI panel, VehicleController? vehicle)
        {
            var isCart = vehicle != null && vehicle.vehicleInstance != null &&
                         vehicle.vehicleInstance.vehicleTypeName == FoodTrucksMod.CartTypeName;
            if (!isCart)
            {
                Hide();
                return;
            }
            Ensure(panel);
            foreach (var button in buttons)
                if (button != null) button.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            foreach (var button in buttons)
                if (button != null) button.gameObject.SetActive(false);
        }

        private static void Ensure(ItemPanelUI panel)
        {
            // The panel is rebuilt with each city scene; Unity's null check sees the old copies as gone.
            if (buttons.Count > 0 && buttons.TrueForAll(b => b != null)) return;
            foreach (var old in buttons)
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            buttons.Clear();
            var template = panel.sleepButton;
            if (template == null)
            {
                Log.Error("Cart buttons: the item panel has no Sleep button to copy.");
                return;
            }
            var after = panel.parkButton.transform.GetSiblingIndex();
            Add(template, "FoodTrucks.OpenButton", "foodtrucks:itempanel_open", () => CartSelling.OpenHere(panel), after + 1);
            Add(template, "FoodTrucks.PricesButton", "foodtrucks:itempanel_prices", PriceDialog.Show, after + 2);
            Log.Info("Cart buttons added to the item panel.");
        }

        private static void Add(Button template, string name, string labelKey, Action onClick, int siblingIndex)
        {
            var button = UnityEngine.Object.Instantiate(template, template.transform.parent);
            button.name = name;
            // A fresh event drops the listeners wired in the prefab (ClickSleep); RemoveAllListeners would not.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() =>
            {
                try { onClick(); }
                catch (Exception ex) { Log.Error($"{name}: {ex}"); }
            });
            button.interactable = true;
            var label = button.transform.GetLanguageChangeEventByName("Label");
            if (label != null)
            {
                label.Suffix = string.Empty;
                label.Key = labelKey;
            }
            else Log.Error($"{name}: no Label child; the button shows the Sleep text.");
            button.transform.SetSiblingIndex(siblingIndex);
            buttons.Add(button);
        }
    }
}
