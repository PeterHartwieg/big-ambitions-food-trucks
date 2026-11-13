#nullable enable
using System;
using Extensions;
using Helpers;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    internal static class CartButton
    {
        private static Button? commissaryButton;
        private static bool onFoodVehicle;
        private static bool atBay;

        public static void OnSetVehicle(ItemPanelUI panel, VehicleController? vehicle)
        {
            var isFoodVehicle = vehicle != null && vehicle.vehicleInstance != null &&
                                FoodTrucksMod.KindOf(vehicle.vehicleInstance.vehicleTypeName) != null;
            if (!isFoodVehicle)
            {
                Hide();
                return;
            }
            Ensure(panel);
            onFoodVehicle = true;
            SetCommissary();
        }

        public static void Hide()
        {
            onFoodVehicle = false;
            SetCommissary();
        }

        public static void ShowCommissary(bool inBay)
        {
            if (atBay == inBay) return;
            atBay = inBay;
            SetCommissary();
        }

        private static void SetCommissary()
        {
            if (commissaryButton != null) commissaryButton.gameObject.SetActive(onFoodVehicle && atBay);
        }

        private static void Ensure(ItemPanelUI panel)
        {
            if (commissaryButton != null) return;
            commissaryButton = null;
            var template = panel.sleepButton;
            if (template == null)
            {
                Log.Error("Cart buttons: the item panel has no Sleep button to copy.");
                return;
            }
            var after = panel.parkButton.transform.GetSiblingIndex();
            commissaryButton = Add(template, "FoodTrucks.CommissaryButton", "foodtrucks:itempanel_commissary_stock", Commissary.ShowStock, after + 1);
            Log.Info("Cart buttons added to the item panel.");
        }

        private static Button Add(Button template, string name, string labelKey, Action onClick, int siblingIndex)
        {
            var button = UnityEngine.Object.Instantiate(template, template.transform.parent);
            button.name = name;
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
            return button;
        }
    }
}
