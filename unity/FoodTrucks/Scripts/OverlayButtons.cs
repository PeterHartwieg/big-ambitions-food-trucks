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

        private static readonly Spec[] Specs =
        {
            new Spec("FoodTrucks.SellCart", "foodtrucks:overlay_sell_cart",
                c => CartSale.CanOffer(c.vehicleInstance), CartSale.Ask),
            new Spec("FoodTrucks.OpenForBusiness", "foodtrucks:overlay_open",
                c => CartSelling.CanOfferOpen(c.vehicleInstance), CartSelling.OpenHere),
            new Spec("FoodTrucks.WorkCart", "foodtrucks:overlay_work_cart",
                c => CartSelling.CanWorkCart(c.vehicleInstance), CartSelling.WorkCart),
            new Spec("FoodTrucks.BackToHatch", "foodtrucks:overlay_back_to_hatch",
                c => TruckWindow.CanGoBack(c.vehicleInstance), TruckWindow.BackToHatch),
            new Spec("FoodTrucks.CloseWindow", "foodtrucks:overlay_close",
                c => CartSelling.CanOfferClose(c.vehicleInstance), c => CartSelling.CloseByPlayer(c.vehicleInstance.id)),
            new Spec("FoodTrucks.Clean", "foodtrucks:overlay_clean",
                c => CartSelling.CanOfferClean(c.vehicleInstance), c => CartSelling.CleanHere(c.vehicleInstance.id)),
        };

        private static readonly FieldInfo? EnterField = AccessTools.Field(typeof(VehicleOverlay), "enterVehicleButton");
        private static readonly FieldInfo? EnterLabelField = AccessTools.Field(typeof(VehicleOverlay), "enterVehicleField");
        private static readonly FieldInfo? StorageField = AccessTools.Field(typeof(VehicleOverlay), "manageStorageButton");
        private static readonly FieldInfo? AddHandTruckField = AccessTools.Field(typeof(VehicleOverlay), "addHandTruckToStorageButton");
        private static readonly FieldInfo? AddItemsField = AccessTools.Field(typeof(VehicleOverlay), "addItemsToStorageButton");
        private static VehicleController? shownFor;
        private static bool failed;
        private static readonly HashSet<string> logged = new HashSet<string>();

        internal static void CommissaryCartCtaPostfix(EntityController entityController)
        {
            try
            {
                if (CtaManager.ctaKey != "click_to_drive" || PlayerHelper.IsUsingVehicle || PlayerHelper.ItemInstanceInHands != null ||
                    !(entityController is VehicleController controller) || !CartSale.CanOffer(controller.vehicleInstance)) return;
                CtaManager.ctaKey = "click_to_use";
                CtaManager.ctaAction = null;
            }
            catch (Exception ex) { Log.Error("Commissary cart actions: " + ex); }
        }

        internal static void UpdateOverlayPostfix(VehicleOverlay __instance, EntityController entityController)
        {
            if (failed) return;
            try
            {
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
                if (food) NativeOnFootButtons(__instance, enter, controller!);
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

        internal static void CtaPostfix(EntityController entityController, ref ValueTuple<string, Action> __result)
        {
            try
            {
                if (__result.Item1 != "click_to_drive" || failed) return;
                if (!(entityController is VehicleController controller) || controller.vehicleInstance == null) return;
                if (FoodTrucksMod.KindOf(controller.vehicleInstance.vehicleTypeName) == null) return;
                if (PlayerHelper.IsUsingVehicle || PlayerHelper.IsHoldingShoppingBasket || CityMap.IsOpen ||
                    PlayerActivity.PlayerActivityUI.IsPanelOpen) return;
                __result = ("click_to_use", null!);
            }
            catch (Exception ex) { Log.Error("Vehicle click: " + ex); }
        }

        private static void NativeOnFootButtons(VehicleOverlay overlay, Button enter, VehicleController controller)
        {
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || PlayerHelper.ItemInstanceInHands != null) return;
            if (controller.vehicleInstance.cargoInstances.Count > 0) return;
            if (EnterLabelField?.GetValue(overlay) is TextLocalizationComponent label)
                label.Key = CartDemand.KindOf(controller.vehicleInstance) == "cart" ? "use_cart" : "enter_vehicle";
            if (StorageField?.GetValue(overlay) is Button storage) storage.gameObject.SetActive(true);
            if (AddHandTruckField?.GetValue(overlay) is Button addHandTruck) addHandTruck.gameObject.SetActive(false);
            if (AddItemsField?.GetValue(overlay) is Button addItems) addItems.gameObject.SetActive(false);
            enter.gameObject.SetActive(true);
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
