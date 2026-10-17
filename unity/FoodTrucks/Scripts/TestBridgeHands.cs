#nullable enable
using System;
using System.Linq;
using BigAmbitions.Items;
using BigAmbitions.Tags;
using Helpers;
using Localizor;
using Newtonsoft.Json.Linq;
using UI;
using UI.PlayerHUD;
using UI.Purchase;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // Player controls only. No cargo/state writes, spawns, forced entry or Harmony patches.
    internal static partial class TestBridgeCommands
    {
        private static void RequireCarryControl()
        {
            RequireFreeHands("carrying controls");
            if (TestBridgeUi.Open().Count > 0 || PurchaseUI.IsPanelOpen || SubwaySystem.IsRiding || GameManager.IsAnyMiniGameActive())
                throw new BridgeError("Not now: close what is open first (ui escape).");
            var manager = InstanceBehavior<BuildingManager>.Instance;
            if (manager != null && (manager.enteringBuilding || manager.exitingBuilding))
                throw new BridgeError("Wait until the building's door has finished.");
            if (PlayerHelper.PlayerController == null || PlayerHelper.PlayerController.VehicleNavigationDisabled)
                throw new BridgeError("The player can't use carrying controls during this activity.");
        }

        private static bool PickupContainer(ItemController item) => item != null && item.ItemInstance != null &&
            item.ItemInstance.ItemCached != null &&
            (item.playerItemPurchaserSettings == null || !item.playerItemPurchaserSettings.enabled) &&
            (item.ItemInstance.ItemCached.HasTag(TagRef.Itemtag.isbox) || item.ItemInstance.ItemCached.HasTag(TagRef.Itemtag.isbag));

        // Reuse the real click and its walk, merging, capacity, registration and destruction handling.
        private static object Pickup(JObject args)
        {
            RequireCarryControl();
            var id = Num(args, "id") ?? throw new BridgeError("pickup needs id from the last use list.");
            if (id != Math.Truncate(id) || id < 1 || id > lastUse.Count || !(lastUse[(int)id - 1] is ItemController item) || !PickupContainer(item))
                throw new BridgeError("That isn't a placed box or bag in the last use list; list again.");
            return UseClick(args);
        }

        // Only actual vanilla hand trucks/flatbeds. Food carts keep their existing vehicle controls.
        private static bool OrdinaryHandTruck(HandTruck truck) => truck != null && truck.vehicleInstance != null &&
            FoodTrucksMod.KindOf(truck.vehicleInstance.vehicleTypeName) == null;

        internal static JObject? PushingHandTruck()
        {
            var truck = PlayerHelper.IsUsingVehicle ? VehicleHelper.GetCurrentVehicleBase() as HandTruck : null;
            return OrdinaryHandTruck(truck!) ? HandTruckEntry(truck!) : null;
        }

        private static JObject HandTruckEntry(HandTruck truck) => new JObject
        {
            ["id"] = truck.vehicleInstance.id,
            ["name"] = LocalizorManager.GetLocalization(truck.vehicleInstance.vehicleTypeName, null),
            ["pushing"] = SaveGameManager.Current.ActiveVehicleId == truck.vehicleInstance.id,
            ["distance"] = Math.Round(Flat(truck.transform.position, PlayerHelper.GetPosition()), 1),
            // These are the boxes and quantities shown in the cargo panel, never purchase costs or tuning.
            ["contents"] = new JArray(truck.vehicleInstance.cargoInstances.Where(c => c != null && c.amount > 0).Select(c => new JObject
            {
                ["key"] = c.itemName, ["item"] = LocalizorManager.GetLocalization(c.itemName, null),
                ["amount"] = c.amount, ["paid"] = c.paid,
            })),
        };

        private static object HandTruckCommand(JObject args)
        {
            var op = Str(args, "op") ?? "list";
            if (op == "list")
            {
                RequireCity("handtruck list");
                return new JObject { ["handtrucks"] = new JArray(NearHandTrucks().Select(HandTruckEntry)) };
            }
            RequireFtCity("handtruck " + op);
            if (op != "grab" && op != "release" && op != "load" && op != "take")
                throw new BridgeError($"Unknown handtruck op '{op}': list, grab, release, load, take.");
            RequireCarryControl();
            if (Paused() == true) throw new BridgeError("The game is paused: set a speed before using a hand truck.");
            var active = SaveGameManager.Current.ActiveVehicleId;
            var id = Str(args, "id") ?? (op == "release" ? active : null);
            if (string.IsNullOrEmpty(id)) throw new BridgeError("handtruck needs id from handtruck list.");
            var truck = NearHandTrucks().FirstOrDefault(t => t.vehicleInstance.id == id)
                ?? throw new BridgeError("That hand truck isn't near you on this floor; list again.");
            if (!string.IsNullOrEmpty(active) && active != id) throw new BridgeError("Let go of your current vehicle first.");
            if (op == "release" && active != id) throw new BridgeError("You aren't pushing that hand truck.");
            if ((op == "grab" || op == "load") && (PlayerHelper.IsHoldingAMop || PlayerHelper.IsHoldingShoppingBasket))
                throw new BridgeError("Return the mop or shopping basket before using a hand truck.");
            if (op == "grab" && active == id) throw new BridgeError("You're already pushing that hand truck.");
            if (op == "load" && !PlayerHelper.IsHoldingItem) throw new BridgeError("Your hands are empty.");
            // Bag conversion into storage drops the contents' paid flags; refuse before the game can convert it.
            if (op == "load" || op == "grab")
            {
                var unpaid = PlayerHelper.ItemInstanceInHands?.cargoInstances?.FirstOrDefault(c => c != null && c.amount > 0 && !c.paid);
                if (unpaid != null) throw new BridgeError($"Pay for it first: {LocalizorManager.GetLocalization(unpaid.itemName, null)}.");
            }
            if (op == "take" && PlayerHelper.IsHoldingItem) throw new BridgeError("Empty your hands before taking a box.");
            var wanted = op == "take" ? Need(args, "item", "a cargo item key or name from handtruck list") : null;
            var cargo = op == "take" ? truck.vehicleInstance.cargoInstances.FirstOrDefault(c => c.amount > 0 &&
                (c.itemName == wanted || string.Equals(LocalizorManager.GetLocalization(c.itemName, null), wanted, StringComparison.OrdinalIgnoreCase))) : null;
            if (op == "take" && cargo == null) throw new BridgeError("That item isn't on the hand truck; list again.");
            if (cargo != null && !string.IsNullOrEmpty(cargo.ItemCached?.vehicleType))
                throw new BridgeError("That cargo deploys a vehicle rather than a carried box; use its own cargo button.");

            var player = PlayerHelper.PlayerController;
            var ui = InstanceBehavior<UIs>.Instance.playerHUD;
            var caught = Notices.BeginCapture();
            var started = UnityEngine.Time.realtimeSinceStartup;
            var grabbingAfterLoad = op == "grab" && PlayerHelper.IsHoldingItem;
            var takeClicked = false;
            try
            {
                if (op == "release") ui.itemPanelUI.ClickPark();
                else if (op == "load" || grabbingAfterLoad) truck.MoveAndAddHeldItemToStorage();
                else if (op == "grab") truck.DriveVehicle();
                else if (active != id) truck.MoveAndManageStorage();
                else ui.manageCargoUI.Close(); // A panel cargo click takes a box and parks; an open storage view transfers instead.
            }
            catch { Notices.EndCapture(caught); throw; }
            return new Deferred(() =>
            {
                var elapsed = UnityEngine.Time.realtimeSinceStartup - started;
                if (elapsed < 0.5f || (player.hasOnGoalReachedAction && elapsed < UseSeconds)) return null;
                if (truck == null) throw new BridgeError("The hand truck is gone; list again.");
                if (grabbingAfterLoad)
                {
                    grabbingAfterLoad = false;
                    if (!PlayerHelper.IsHoldingItem && elapsed < UseSeconds)
                    {
                        truck.DriveVehicle();
                        return null;
                    }
                }
                if (op == "take" && !takeClicked && elapsed < UseSeconds)
                {
                    takeClicked = true;
                    // Invoke the visible row's own listener, preserving sealed cargo, exit and transfer behavior.
                    var root = active == id ? (Component)ui.itemPanelUI : ui.manageCargoUI;
                    var holderField = HarmonyLib.AccessTools.Field(typeof(CargoItemUi), "_currentCargoHolder");
                    var buttonField = HarmonyLib.AccessTools.Field(typeof(CargoItemUi), "itemButton");
                    var row = root.GetComponentsInChildren<CargoItemUi>().FirstOrDefault(r => r.gameObject.activeInHierarchy &&
                        r.gameObject.name == cargo!.itemName && ReferenceEquals(holderField?.GetValue(r), truck.vehicleInstance));
                    var button = row == null ? null : buttonField?.GetValue(row) as Button;
                    if (button == null || !button.isActiveAndEnabled || !button.interactable)
                        throw new BridgeError("The game's cargo button isn't available; close other panels and list again.");
                    button.onClick.Invoke();
                    return null;
                }
                Notices.EndCapture(caught);
                var pushing = SaveGameManager.Current.ActiveVehicleId == id;
                var done = op == "release" ? !pushing : op == "grab" ? pushing : op == "take" ? PlayerHelper.IsHoldingItem : !PlayerHelper.IsHoldingItem;
                var why = done ? null : player.hasOnGoalReachedAction ? "still walking; inspect look before repeating"
                    : caught.LastOrDefault() ?? ((op == "load" || op == "grab") && PlayerHelper.IsHoldingItem
                        ? "the hand truck couldn't take everything; remaining cargo stays in your hands"
                        : op == "release" ? "the game's Park button refused here"
                        : op == "grab" ? "the game couldn't reach or enter that hand truck"
                        : "the game didn't put a box in your hands; inspect the cargo before repeating");
                Log.Info($"Test bridge: handtruck {id} {op}: {(done ? "done" : why)}.");
                return new JObject
                {
                    ["done"] = done, ["why"] = why,
                    ["handtruck"] = HandTruckEntry(truck), ["hands"] = Hands(), ["notices"] = new JArray(caught),
                };
            }, () => Notices.EndCapture(caught));
        }

        private static System.Collections.Generic.IEnumerable<HandTruck> NearHandTrucks()
        {
            var from = PlayerHelper.GetPosition();
            return Object.FindObjectsOfType<HandTruck>().Where(t => OrdinaryHandTruck(t) && t.isActiveAndEnabled && t.visible && t.primaryInteractionEnabled &&
                Math.Abs(t.transform.position.y - from.y) <= 4 && Flat(t.transform.position, from) <= UseDefaultRadius)
                .OrderBy(t => Flat(t.transform.position, from));
        }
    }
}
