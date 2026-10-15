#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Localizor.LanguageChangeEvent;
using Player.HUD.ItemInfoOverlays;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // The food vehicle's own buttons in its on-foot menu (click the vehicle on foot: the game's "Enter vehicle" and
    // "Manage storage" buttons, VehicleOverlay). Copies of the overlay's Enter vehicle button, each shown only when it
    // applies (Peter, 15 October 2026: the vehicle panel holds only cargo):
    // - "Open for business": a closed food vehicle, the player on foot (CartSelling.OpenHere checks the rest).
    // - "Back to the hatch": an open truck while the player isn't at its hatch (TruckWindow.CanGoBack). A truck sells
    //   only with someone at its window, so without it every step-out (a time skip, Unstuck, a load) ended solo selling
    //   until the window was closed and reopened.
    // - "Close": an open vehicle while the player isn't at its hatch, so a cart can close without being moved.
    // - "Clean": in the bay of the commissary where the vehicle rents its space (CartSelling.CleanHere).
    internal static class OverlayButtons
    {
        private sealed class Spec
        {
            public Spec(string name, string labelKey, Func<VehicleController, bool> show, Action<VehicleController> click)
            {
                Name = name;
                LabelKey = labelKey;
                Show = show;
                Click = click;
            }

            public string Name { get; }
            public string LabelKey { get; }
            public Func<VehicleController, bool> Show { get; }
            public Action<VehicleController> Click { get; }
        }

        // In the order they stand, just before Enter vehicle.
        private static readonly Spec[] Specs =
        {
            new Spec("FoodTrucks.OpenForBusiness", "foodtrucks:overlay_open",
                c => CartSelling.CanOfferOpen(c.vehicleInstance), CartSelling.OpenHere),
            new Spec("FoodTrucks.BackToHatch", "foodtrucks:overlay_back_to_hatch",
                c => TruckWindow.CanGoBack(c.vehicleInstance), TruckWindow.BackToHatch),
            new Spec("FoodTrucks.CloseWindow", "foodtrucks:overlay_close",
                c => CartSelling.CanOfferClose(c.vehicleInstance), c => CartSelling.CloseByPlayer(c.vehicleInstance.id)),
            new Spec("FoodTrucks.Clean", "foodtrucks:overlay_clean",
                c => CartSelling.CanOfferClean(c.vehicleInstance), c => CartSelling.CleanHere(c.vehicleInstance.id)),
        };

        private static readonly FieldInfo? EnterField = AccessTools.Field(typeof(VehicleOverlay), "enterVehicleButton");
        // The vehicle the overlay showed when the buttons were last set up.
        private static VehicleController? shownFor;
        private static bool failed;
        private static readonly HashSet<string> logged = new HashSet<string>();

        // Postfix on VehicleOverlay.UpdateOverlay(EntityController).
        internal static void UpdateOverlayPostfix(VehicleOverlay __instance, EntityController entityController)
        {
            if (failed) return;
            try
            {
                // The game leaves its buttons as they are while a click action is pending; so do these.
                if (CtaManager.ctaAction != null) return;
                if (!(EnterField?.GetValue(__instance) is Button enter))
                {
                    failed = true;
                    Log.Error("Vehicle overlay buttons: VehicleOverlay has no enterVehicleButton; Open for business, Back to the hatch, Close and Clean aren't offered.");
                    return;
                }
                var controller = entityController as VehicleController;
                var food = controller != null && controller.vehicleInstance != null && FoodTrucksMod.KindOf(controller.vehicleInstance.vehicleTypeName) != null;
                shownFor = food ? controller : null;
                foreach (var spec in Specs)
                {
                    var button = Find(enter, spec.Name) ?? Make(enter, spec);
                    if (button == null) continue;
                    bool show;
                    try { show = food && spec.Show(controller!); }
                    catch (Exception ex)
                    {
                        show = false;
                        Log.Error($"{spec.Name}: {ex}");
                    }
                    if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Vehicle overlay buttons: " + ex);
            }
        }

        private static Button? Find(Button enter, string name)
        {
            var parent = enter.transform.parent;
            if (parent == null) return null;
            var child = parent.Find(name);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private static Button? Make(Button enter, Spec spec)
        {
            var button = UnityEngine.Object.Instantiate(enter, enter.transform.parent);
            button.name = spec.Name;
            // A fresh event drops the copied listener (the game's DriveVehicle).
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Click(spec));
            button.interactable = true;
            var label = button.GetComponentInChildren<TextLocalizationComponent>(true);
            if (label != null)
            {
                label.Suffix = string.Empty;
                label.Key = spec.LabelKey;
            }
            else Log.Error($"{spec.Name}: the copied button has no label; it shows the game's text.");
            button.gameObject.SetActive(false);
            // Placed once, just before Enter vehicle: setting it on every update swapped them each time. Made in the
            // order of Specs, each lands after the ones made before it.
            button.transform.SetSiblingIndex(enter.transform.GetSiblingIndex());
            if (logged.Add(spec.Name)) Log.Info($"Vehicle overlay: {spec.Name} button added.");
            return button;
        }

        private static void Click(Spec spec)
        {
            try
            {
                var controller = shownFor;
                InstanceBehavior<OverlayManager>.Instance?.HideDetailedOverlay();
                if (controller == null || controller.vehicleInstance == null)
                {
                    Log.Info($"{spec.Name}: clicked with no food vehicle in the overlay; nothing done.");
                    return;
                }
                Log.Info($"{spec.Name}: clicked for {controller.vehicleInstance.id}.");
                spec.Click(controller);
            }
            catch (Exception ex) { Log.Error($"{spec.Name}: {ex}"); }
        }
    }
}
