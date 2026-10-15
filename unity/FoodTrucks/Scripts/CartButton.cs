#nullable enable
using System;
using Extensions;
using Helpers;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // The cart's and truck's own button in the item panel while the player pushes the cart or drives the truck:
    // "Commissary", while the vehicle stands in a commissary's bay. A copy of the panel's Sleep button with its own click
    // handler. The panel holds only cargo (Peter, 15 October 2026): opening, closing and cleaning are in the vehicle's
    // on-foot menu (OverlayButtons), prices, crew and route stops on its BizMan page (BizManVehiclePage).
    internal static class CartButton
    {
        private static Button? commissaryButton;
        // The panel shows a food vehicle now; the Commissary button shows only then (Commissary checks the bay).
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

        // Called twice a second with whether the player's food vehicle stands in a commissary's bay.
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
            // The panel is rebuilt with each city scene; Unity's null check sees the old copy as gone.
            if (commissaryButton != null) return;
            commissaryButton = null;
            var template = panel.sleepButton;
            if (template == null)
            {
                Log.Error("Cart buttons: the item panel has no Sleep button to copy.");
                return;
            }
            var after = panel.parkButton.transform.GetSiblingIndex();
            commissaryButton = Add(template, "FoodTrucks.CommissaryButton", "foodtrucks:itempanel_commissary", () => Commissary.ShowPanel(panel), after + 6);
            Log.Info("Cart buttons added to the item panel.");
        }

        private static Button Add(Button template, string name, string labelKey, Action onClick, int siblingIndex)
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
            return button;
        }
    }
}
