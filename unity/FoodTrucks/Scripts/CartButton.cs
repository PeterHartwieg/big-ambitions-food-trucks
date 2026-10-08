#nullable enable
using System;
using Extensions;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // The "Open here" button in the item panel while the player pushes a food cart.
    // A copy of the panel's Sleep button with its own click handler, shown only for the cart.
    internal static class CartButton
    {
        private const string LabelKey = "foodtrucks:itempanel_open";
        private static Button? button;

        public static void OnSetVehicle(ItemPanelUI panel, VehicleController? vehicle)
        {
            var isCart = vehicle != null && vehicle.vehicleInstance != null &&
                         vehicle.vehicleInstance.vehicleTypeName == FoodTrucksMod.CartTypeName;
            if (!isCart)
            {
                Hide();
                return;
            }
            var created = Ensure(panel);
            if (created != null) created.gameObject.SetActive(true);
        }

        public static void Hide()
        {
            if (button != null) button.gameObject.SetActive(false);
        }

        private static Button? Ensure(ItemPanelUI panel)
        {
            if (button != null) return button;
            var template = panel.sleepButton;
            if (template == null)
            {
                Log.Error("Open button: the item panel has no Sleep button to copy.");
                return null;
            }
            button = UnityEngine.Object.Instantiate(template, template.transform.parent);
            button.name = "FoodTrucks.OpenButton";
            // A fresh event drops the listeners wired in the prefab (ClickSleep); RemoveAllListeners would not.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => CartSelling.OpenHere(panel));
            button.interactable = true;
            var label = button.transform.GetLanguageChangeEventByName("Label");
            if (label != null)
            {
                label.Suffix = string.Empty;
                label.Key = LabelKey;
            }
            else Log.Error("Open button: no Label child; the button shows the Sleep text.");
            button.transform.SetSiblingIndex(panel.parkButton.transform.GetSiblingIndex() + 1);
            Log.Info("Open button added to the item panel.");
            return button;
        }
    }
}
