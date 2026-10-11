#nullable enable
using System;
using System.Collections.Generic;
using Extensions;
using Helpers;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // The cart's and truck's own buttons in the item panel while the player pushes the cart or drives the
    // truck: "Open here", "Prices", "Hires", "Add stop here", "Clean", and "Commissary" while the vehicle stands in a commissary's
    // bay. Copies of the panel's Sleep button with their own click handlers.
    internal static class CartButton
    {
        private static readonly List<Button> buttons = new List<Button>();
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
            foreach (var button in buttons)
                if (button != null) button.gameObject.SetActive(true);
            SetCommissary();
        }

        public static void Hide()
        {
            onFoodVehicle = false;
            foreach (var button in buttons)
                if (button != null) button.gameObject.SetActive(false);
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
            // The panel is rebuilt with each city scene; Unity's null check sees the old copies as gone.
            if (buttons.Count > 0 && buttons.TrueForAll(b => b != null) && commissaryButton != null) return;
            foreach (var old in buttons)
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            if (commissaryButton != null) UnityEngine.Object.Destroy(commissaryButton.gameObject);
            buttons.Clear();
            commissaryButton = null;
            var template = panel.sleepButton;
            if (template == null)
            {
                Log.Error("Cart buttons: the item panel has no Sleep button to copy.");
                return;
            }
            var after = panel.parkButton.transform.GetSiblingIndex();
            buttons.Add(Add(template, "FoodTrucks.OpenButton", "foodtrucks:itempanel_open", () => CartSelling.OpenHere(panel), after + 1));
            buttons.Add(Add(template, "FoodTrucks.PricesButton", "foodtrucks:itempanel_prices", PriceDialog.Show, after + 2));
            // Hires opens the vehicle's page in BizMan (crew, route, partner), or the old dialog if that page can't be built.
            buttons.Add(Add(template, "FoodTrucks.HiresButton", "foodtrucks:itempanel_hires", () => BizManVehiclePage.OpenFromPanel(panel), after + 3));
            // The exact spot the vehicle stands on as a stop; the BizMan page adds stops by picking a building on the map.
            buttons.Add(Add(template, "FoodTrucks.AddStopButton", "foodtrucks:itempanel_addstop", () => AddStopHere(panel), after + 4));
            buttons.Add(Add(template, "FoodTrucks.CleanButton", "foodtrucks:itempanel_clean", CartSelling.CleanHere, after + 5));
            commissaryButton = Add(template, "FoodTrucks.CommissaryButton", "foodtrucks:itempanel_commissary", () => Commissary.ShowPanel(panel), after + 6);
            Log.Info("Cart buttons added to the item panel.");
        }

        private static void AddStopHere(ItemPanelUI panel)
        {
            var vehicle = VehicleHelper.GetCurrentVehicle();
            if (vehicle != null) StaffDialog.AddStopHere(panel, vehicle.id);
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
