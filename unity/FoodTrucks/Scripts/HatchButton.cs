#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Localizor.LanguageChangeEvent;
using Player.HUD.ItemInfoOverlays;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // "Back to the hatch" in a vehicle's overlay (click the truck on foot: the game's "Enter vehicle" and "Manage
    // storage" buttons, VehicleOverlay). A copy of the overlay's Enter vehicle button, shown for an open truck while
    // the player isn't at its hatch (TruckWindow.CanGoBack). A truck sells only with someone at its window, so without
    // it every step-out (a time skip, Unstuck, a load) ended solo selling until the window was closed and reopened.
    internal static class HatchButton
    {
        private const string ButtonName = "FoodTrucks.BackToHatch";
        private static readonly FieldInfo? EnterField = AccessTools.Field(typeof(VehicleOverlay), "enterVehicleButton");
        // The truck the overlay showed when the button was last set up.
        private static VehicleController? shownFor;
        private static bool failed;
        private static bool logged;

        // Postfix on VehicleOverlay.UpdateOverlay(EntityController).
        internal static void UpdateOverlayPostfix(VehicleOverlay __instance, EntityController entityController)
        {
            if (failed) return;
            try
            {
                // The game leaves its buttons as they are while a click action is pending; so does this one.
                if (CtaManager.ctaAction != null) return;
                if (!(EnterField?.GetValue(__instance) is Button enter))
                {
                    failed = true;
                    Log.Error("Back to the hatch: VehicleOverlay has no enterVehicleButton; the button isn't offered.");
                    return;
                }
                var button = Find(enter) ?? Make(enter);
                if (button == null) return;
                var controller = entityController as VehicleController;
                var show = controller != null && TruckWindow.CanGoBack(controller.vehicleInstance);
                shownFor = show ? controller : null;
                if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Back to the hatch: " + ex);
            }
        }

        private static Button? Find(Button enter)
        {
            var parent = enter.transform.parent;
            if (parent == null) return null;
            var child = parent.Find(ButtonName);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private static Button? Make(Button enter)
        {
            var button = UnityEngine.Object.Instantiate(enter, enter.transform.parent);
            button.name = ButtonName;
            // A fresh event drops the copied listener (the game's DriveVehicle).
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Click);
            button.interactable = true;
            var label = button.GetComponentInChildren<TextLocalizationComponent>(true);
            if (label != null)
            {
                label.Suffix = string.Empty;
                label.Key = "foodtrucks:overlay_back_to_hatch";
            }
            else Log.Error("Back to the hatch: the copied button has no label; it shows the game's text.");
            button.gameObject.SetActive(false);
            // Placed once, just before Enter vehicle: setting it on every update swapped the two each time.
            button.transform.SetSiblingIndex(enter.transform.GetSiblingIndex());
            if (!logged)
            {
                logged = true;
                Log.Info("Back to the hatch: button added to the vehicle overlay.");
            }
            return button;
        }

        private static void Click()
        {
            try
            {
                var controller = shownFor;
                InstanceBehavior<OverlayManager>.Instance?.HideDetailedOverlay();
                if (controller == null) return;
                TruckWindow.BackToHatch(controller);
            }
            catch (Exception ex) { Log.Error("Back to the hatch: " + ex); }
        }
    }
}
