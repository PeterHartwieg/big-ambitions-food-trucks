#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using BigAmbitions.Tags;
using Entities;
using Helpers;
using Localizor;
using Newtonsoft.Json.Linq;
using Player.HUD.ItemInfoOverlays;
using UI;
using UI.Notification;
using UI.Smartphone;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The player's own life, for the blind tester (docs/test-bridge.md, "The player's life"): clicking things in the world
    // (`use`, the game's left click on an object: a bed, a shop's shelf or till, a taxi, a school's door, a building), the
    // taxi's ride to a building (`taxi`), the phone (`phone`, `call`) and eating from a bag in hands (`eat`). Each runs the
    // game's own code with its checks, prices and time; nothing is placed, granted or paid by the bridge.
    internal static partial class TestBridgeCommands
    {
        // ---- use: a click on a thing in the world ----

        private const float UseDefaultRadius = 20f;
        private const float UseMaxRadius = 80f;
        private const int UseMaxEntries = 40;
        // How long a click's walk over to the thing may take (real seconds) before the reply says where the player is.
        private const float UseSeconds = 120f;

        // The things of the last use list, for a click by id.
        private static List<EntityController> lastUse = new List<EntityController>();

        private static object Use(JObject args)
        {
            var op = Str(args, "op") ?? "list";
            switch (op)
            {
                case "list":
                    RequireCity("use list");
                    return UseList(args);
                case "click":
                    RequireFtCity("use " + op);
                    return UseClick(args);
                default:
                    // No right click: on the player's own furniture it starts moving it, which follows the real mouse
                    // (review round 1). Placing a bought piece is place's.
                    throw new BridgeError($"Unknown use op '{op}': list or click.");
            }
        }

        // Where the thing stands for the player: a building at its nearest entrance, anything else where it is.
        private static Vector3 UsePoint(EntityController entity, Vector3 from)
        {
            if (entity is CityBuildingController building && building.entranceDoors != null && building.entranceDoors.Length > 0)
                return building.entranceDoors.Where(d => d?.doorTransform != null).Select(d => d.doorTransform.position)
                    .DefaultIfEmpty(building.transform.position).OrderBy(p => (p - from).sqrMagnitude).First();
            return entity.transform.position;
        }

        // Whether a left click on it does anything a player would see: a call to action, a detailed overlay, something
        // to buy, a building or a vehicle. Walls and decoration are left out.
        private static bool Clickable(EntityController entity)
        {
            if (entity == null || !entity.isActiveAndEnabled || !entity.visible || !entity.primaryInteractionEnabled) return false;
            if (entity is CityBuildingController) return true;
            if (entity is ItemController item && item.playerItemPurchaserSettings != null && item.playerItemPurchaserSettings.enabled) return true;
            if (entity.detailedOverlayType != 0) return true;
            return CtaManager.HasCta(entity);
        }

        private static JToken UseList(JObject args)
        {
            var radius = (float)Math.Max(3, Math.Min(UseMaxRadius, Num(args, "radius") ?? UseDefaultRadius));
            var from = PlayerHelper.GetPosition();
            var found = new List<(EntityController Entity, Vector3 At, float Distance)>();
            foreach (var entity in Object.FindObjectsOfType<EntityController>())
            {
                if (entity == null) continue;
                var at = UsePoint(entity, from);
                // Another floor of the building isn't in reach.
                if (Math.Abs(at.y - from.y) > 4f && !(entity is CityBuildingController)) continue;
                var distance = (float)Flat(at, from);
                if (distance <= radius && Clickable(entity)) found.Add((entity, at, distance));
            }
            var near = found.OrderBy(f => f.Distance).Take(UseMaxEntries).ToList();
            lastUse = near.Select(n => n.Entity).ToList();
            var things = new JArray();
            for (var i = 0; i < near.Count; i++) things.Add(UseEntry(i + 1, near[i].Entity, near[i].At, near[i].Distance));
            return new JObject
            {
                ["radius"] = radius,
                ["inside"] = InsideAddress(),
                ["things"] = things,
            };
        }

        private static JObject UseEntry(int id, EntityController entity, Vector3 at, float distance)
        {
            var (ctaKey, hasAction) = Cta(entity);
            var entry = new JObject
            {
                ["id"] = id,
                ["kind"] = entity.GetType().Name,
                ["name"] = ThingName(entity),
                ["click"] = string.IsNullOrEmpty(ctaKey) ? null : LocalizorManager.GetLocalization(ctaKey, null),
                ["click_key"] = string.IsNullOrEmpty(ctaKey) ? null : ctaKey,
                ["click_action"] = hasAction,
                ["x"] = TestBridgeState.Round(at.x), ["z"] = TestBridgeState.Round(at.z),
                ["distance"] = Math.Round(distance, 1),
            };
            if (entity is CityBuildingController building && building.building != null)
            {
                var address = building.building.Address;
                entry["address"] = address == null ? null : World.AddressKey(address.streetName, address.streetNumber);
                var registration = building.buildingRegistration;
                entry["business"] = string.IsNullOrEmpty(registration?.BusinessName) ? null : registration!.BusinessName;
                entry["for_rent"] = registration?.AvailableForRent ?? false;
                entry["yours"] = registration?.RentedByPlayer ?? false;
            }
            if (entity is ItemController item && item.playerItemPurchaserSettings != null && item.playerItemPurchaserSettings.enabled)
            {
                // What it sells; the price is on the shop's own label and at the till.
                var sells = item.playerItemPurchaserSettings;
                entry["sells"] = string.IsNullOrEmpty(sells.itemName) ? null : LocalizorManager.GetLocalization(sells.itemName, null);
                entry["sells_key"] = sells.itemName;
            }
            return entry;
        }

        // The game's call to action over the thing, as hovering it shows ("Click to sleep"), and whether the click runs it.
        // CtaManager's state is the hover's, so it is cleared again.
        private static (string? Key, bool HasAction) Cta(EntityController entity)
        {
            try
            {
                CtaManager.UpdateCta(entity);
                return (CtaManager.ctaKey, CtaManager.ctaAction != null);
            }
            catch (Exception) { return (null, false); }
            finally { CtaManager.Clear(); }
        }

        private static string ThingName(EntityController entity)
        {
            if (entity is CityBuildingController building)
            {
                var name = building.buildingRegistration?.BusinessName;
                if (!string.IsNullOrEmpty(name)) return name!;
                var type = building.building?.BuildingType;
                return string.IsNullOrEmpty(type) ? "building" : LocalizorManager.GetLocalization(type, null);
            }
            if (entity is ItemController item && !string.IsNullOrEmpty(item.itemName)) return LocalizorManager.GetLocalization(item.itemName, null);
            return entity.gameObject.name;
        }

        // Notices.BeginCapture only sees the mod's Notices.Show wrapper. A world click must also report vanilla
        // refusals. The game's event carries exactly the key/data that NotificationsUI localizes for the screen.
        private sealed class UseNoticeCapture : IDisposable
        {
            internal readonly List<string> Messages = new List<string>();
            internal string? LastError { get; private set; }
            internal UseNoticeCapture() { Notifications.onShow += Capture; }
            private void Capture(NotificationType type, string key, Dictionary<string, string> data, float seconds,
                string duplicateIdentifier, Action onClick, bool sound, bool track)
            {
                var message = Notices.Text(key, data);
                Messages.Add(message);
                if (type == NotificationType.Error) LastError = message;
            }
            public void Dispose() { Notifications.onShow -= Capture; }
        }

        // An id click has no screen position. CityBuildingController.OnIoLeftClick tests the *desktop* pointer's
        // IsPointerOverGameObject, so a pointer left over the hands HUD can swallow a perfectly valid door click.
        // Use the native entity navigation and virtual Interact instead. Interact still decides access, opening
        // hours, DLC, goals, installation, entrance fees and the door id; never call BuildingManager.EnterBuilding.
        private static bool UseBuildingClick(CityBuildingController building, PlayerController player, out string? why)
        {
            why = null;
            if (FullMenu.IsOpen || CityMap.IsOpen)
                why = "Close the phone or map before clicking a building in the world (ui escape).";
            else if (player.NavigationDisabled && !PlayerHelper.IsUsingVehicle)
                why = "The player can't interact now" + Blockers(player) + ".";
            else if (GameManager.IsAnyMiniGameActive())
                why = "Finish the minigame before clicking a building.";
            else if (!player.IsOnNavmesh())
                // In 1.0 IsOnNavmesh means on foot or using a spawnInPlayerObject vehicle (e.g. a hand truck).
                why = "Leave the motor vehicle before walking to a building entrance.";
            if (why != null) return false;

            building.UpdateNavMeshTargets();
            if (PlayerHelper.IsWithinPlayerDistance(building)) return building.Interact();
            var target = building.GetClosestNavMeshTargetPosition(PlayerHelper.GetPosition());
            // Ask the game to emit its own cannot-reach notice too (the mouse's WalkOverAndInteract is silent here).
            if (!player.ExistsRoute(target, showErrorNotification: true, entityController: building))
            {
                why = target == Vector3.zero ? "The game found no reachable interaction point for this entrance."
                    : "The game found no complete walking route to this entrance.";
                return false;
            }
            building.MoveTowardsEntity(() => { if (building != null) building.Interact(); });
            if (player.hasOnGoalReachedAction) return true;
            why = "The game didn't start the entrance walk" + Blockers(player) + ".";
            return false;
        }

        private static object UseClick(JObject args)
        {
            var id = Num(args, "id") ?? throw new BridgeError("use click needs \"id\" from the last use list.");
            var index = (int)id - 1;
            if (index < 0 || index >= lastUse.Count) throw new BridgeError($"No thing {id} in the last use list; list again.");
            var entity = lastUse[index];
            if (entity == null || !Clickable(entity)) throw new BridgeError($"Thing {id} is gone or can't be clicked now; list again.");
            if (Paused() == true) throw new BridgeError("The game is paused: the walk over takes real time. Set a speed first.");
            RequireFreeHands("a click");
            // What the mouse's click handling waits for too (MouseController): the interior designer, the subway, placing.
            if (UI.InteriorDesigner.InteriorDesignerUI.IsOpen || SubwaySystem.IsRiding || BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode)
                throw new BridgeError("Not now: close what is open first (ui escape).");
            var name = ThingName(entity);
            var (ctaKey, _) = Cta(entity);
            var player = PlayerHelper.PlayerController ?? throw new BridgeError("The player isn't there.");
            var insideBefore = InsideAddress();
            var capture = new UseNoticeCapture();
            var caught = capture.Messages;
            bool handled;
            string? why = null;
            var placingCancelled = false;
            try
            {
                // The hover sets the call to action before the click, as the mouse does (OnIoLeftClick skips it while an
                // overlay shows over the thing).
                CtaManager.UpdateCta(entity);
                handled = entity is CityBuildingController building ? UseBuildingClick(building, player, out why) : entity.OnIoLeftClick();
                if (!handled && why == null)
                    why = player.NavigationDisabled ? "The player can't interact now" + Blockers(player) + "."
                        : "The game's click handler declined this interaction without giving a reason; inspect look and ui list.";
                // Moving furniture by hand isn't one of the tester's controls: it follows the real mouse.
                if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode)
                    placingCancelled = Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
            }
            catch
            {
                capture.Dispose();
                throw;
            }
            Log.Info($"Test bridge: clicked {entity.GetType().Name} \"{name}\" ({(handled ? "handled" : "not handled")}{(placingCancelled ? ", placing cancelled" : "")}).");
            var started = UnityEngine.Time.realtimeSinceStartup;
            var deadline = started + UseSeconds;
            return new Deferred(() =>
            {
                var now = UnityEngine.Time.realtimeSinceStartup;
                // A moment for the game's reaction (a panel, a notice), then until the walk over has ended and any
                // building's door has finished loading.
                if (now - started < 0.5f) return null;
                var walking = player.hasOnGoalReachedAction;
                var entering = InstanceBehavior<BuildingManager>.Instance != null && InstanceBehavior<BuildingManager>.Instance.enteringBuilding;
                if ((walking || entering || TaxiSystem.IsTraveling) && now < deadline) return null;
                capture.Dispose();
                var position = PlayerHelper.GetPosition();
                var inside = InsideAddress();
                var open = TestBridgeUi.Open();
                var resultWhy = why;
                if (walking) resultWhy = "Still walking; inspect look before repeating the click.";
                else if (entering) resultWhy = "The building is still loading; wait and inspect look before repeating the click.";
                else if (entity is CityBuildingController && inside == insideBefore && open.Count == 0)
                    resultWhy = capture.LastError ?? resultWhy ?? "The building interaction ended without entering or opening a screen; inspect look before repeating.";
                else if (!handled) resultWhy = capture.LastError ?? resultWhy;
                Log.Info($"Test bridge: use result for {name}: handled {handled}, inside {inside ?? "outside"}, holding {PlayerHelper.ItemInstanceInHands?.itemName ?? "nothing"}: {resultWhy ?? "done"}.");
                return new JObject
                {
                    ["clicked"] = name,
                    ["kind"] = entity == null ? null : entity.GetType().Name,
                    ["click"] = string.IsNullOrEmpty(ctaKey) ? null : LocalizorManager.GetLocalization(ctaKey, null),
                    ["handled"] = handled,
                    ["why"] = resultWhy,
                    ["placing_cancelled"] = placingCancelled,
                    ["still_walking"] = walking,
                    ["still_entering"] = entering,
                    ["inside"] = inside,
                    ["inside_before"] = insideBefore,
                    ["open"] = new JArray(open),
                    ["notices"] = new JArray(caught),
                    ["x"] = TestBridgeState.Round(position.x), ["z"] = TestBridgeState.Round(position.z),
                };
            }, () => capture.Dispose());
        }

        // The building the player is inside, by address, or null outside.
        internal static string? InsideAddress()
        {
            if (!BuildingManager.IsInsideBuilding) return null;
            var registration = InstanceBehavior<BuildingManager>.Instance?.buildingRegistration;
            var address = registration?.Address;
            return address == null ? "a building" : World.AddressKey(address.streetName, address.streetNumber);
        }

        // ---- taxi: the ride to a building, once a taxi waits ----

        // How long a ride may take in real seconds (the game skips its minutes fast).
        private const float TaxiSeconds = 120f;

        // { "address" }: the destination picked on the taxi's map, as the building card's travel button does
        // (BuildingResume.TaxiTravel → TaxiSystem.TravelTo): the game charges the fare and skips the ride's minutes, then
        // sets the player down at the building. A taxi must be waiting: one clicked in the street (use click) opens the
        // map in taxi mode. Without it, nothing is charged and nothing moves.
        private static object Taxi(JObject args)
        {
            var address = Need(args, "address", "the building's address, ba:street_<name>#<number>");
            if (!AddressForm.IsMatch(address)) throw new BridgeError($"address takes a building's address as the game writes it, ba:street_<name>#<number>; got '{address}'.");
            if (TaxiSystem.IsTraveling) throw new BridgeError("You're already riding.");
            var cityMap = InstanceBehavior<CityManager>.Instance?.cityMap;
            if (cityMap == null || !CityMap.IsOpen || !cityMap.IsTaxiMode)
                throw new BridgeError("No taxi is waiting for you: click a taxi in the street first (use list shows the ones near you; use click gets in), then say where to.");
            var target = World.BuildingAt(address) ?? throw new BridgeError($"no building at {address} in this city");
            var game = SaveGameManager.Current;
            var moneyBefore = game.Money;
            var minuteBefore = NowMinutes(game);
            var caught = Notices.BeginCapture();
            try { InstanceBehavior<TaxiSystem>.Instance.TravelTo(target); }
            catch
            {
                Notices.EndCapture(caught);
                throw;
            }
            var riding = TaxiSystem.IsTraveling;
            Log.Info($"Test bridge: taxi to {address}: {(riding ? "riding" : "refused")}.");
            var started = UnityEngine.Time.realtimeSinceStartup;
            return new Deferred(() =>
            {
                var now = UnityEngine.Time.realtimeSinceStartup;
                if (riding && TaxiSystem.IsTraveling && now - started < TaxiSeconds) return null;
                if (now - started < 0.3f) return null;
                Notices.EndCapture(caught);
                var position = PlayerHelper.GetPosition();
                return new JObject
                {
                    ["done"] = riding && !TaxiSystem.IsTraveling,
                    ["address"] = address,
                    ["fare"] = Math.Round(moneyBefore - game.Money, 2),
                    ["minutes"] = Math.Round(NowMinutes(game) - minuteBefore),
                    ["money"] = Math.Round(game.Money, 2),
                    ["notices"] = new JArray(caught),
                    ["x"] = TestBridgeState.Round(position.x), ["z"] = TestBridgeState.Round(position.z),
                };
            }, () => Notices.EndCapture(caught));
        }

        // ---- phone and calls ----

        private static readonly Dictionary<string, AppName> PhoneApps = new Dictionary<string, AppName>(StringComparer.OrdinalIgnoreCase)
        {
            ["persona"] = AppName.Persona, ["contacts"] = AppName.Contacts, ["employees"] = AppName.MyEmployees,
            ["bizman"] = AppName.BizMan, ["econoview"] = AppName.EconoView,
            ["market"] = AppName.MarketInsider, ["rivals"] = AppName.Rivals,
            // Not VoogleMaps: the game opens the map through SmartphoneUI.OpenApp, not the full menu (review round 1);
            // the tester reads the map with the map command, and a taxi opens it in taxi mode.
        };

        // { "app" }: the phone opened on one of its full-menu apps, as its key and app buttons do.
        private static object Phone(JObject args)
        {
            var name = Need(args, "app", string.Join(", ", PhoneApps.Keys));
            if (!PhoneApps.TryGetValue(name, out var app)) throw new BridgeError($"No phone app '{name}': {string.Join(", ", PhoneApps.Keys)}.");
            RequireFreeHands("the phone");
            Log.Info($"Test bridge: phone opened on {app}.");
            return WithNotices(() => InstanceBehavior<UIs>.Instance.fullMenu.ShowApp(app), notices => new JObject { ["app"] = name, ["open"] = FullMenu.IsOpen, ["notices"] = new JArray(notices) });
        }

        private static void RequireFreeHands(string what)
        {
            if (TaxiSystem.IsTraveling) throw new BridgeError($"You're in a taxi: {what} waits until you're there.");
            var machine = InstanceBehavior<UIs>.Instance?.timeMachine;
            if (machine != null && machine.isRunning) throw new BridgeError($"Time is skipping (sleep, a course, a ride): {what} waits until it ends.");
        }

        // { "op": "list" } or { "contact": "<id or name>" }: the phone's contacts, or a call to one (the conversation opened and its
        // call button pressed, ContactsApp.OpenAndStartCall). The call's options are then the screen's buttons (ui list).
        private static object Call(JObject args)
        {
            var contacts = (SaveGameManager.Current?.Contacts ?? new List<Contact>()).Where(c => c != null && !string.IsNullOrEmpty(c.id)).ToList();
            if (Str(args, "op") == "list")
            {
                RequireCity("call list");
                return new JObject
                {
                    ["contacts"] = new JArray(contacts.Select(c => new JObject
                    {
                        ["id"] = c.id, ["name"] = ContactName(c), ["category"] = c.category.ToString(), ["unread"] = c.HasUnreadMessages,
                    })),
                };
            }
            RequireFtCity("call");
            var wanted = Need(args, "contact", "a contact's id or name (call list shows them)");
            var matches = contacts.Where(c => string.Equals(c.id, wanted, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0) matches = contacts.Where(c => string.Equals(ContactName(c), wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) throw new BridgeError($"No contact '{wanted}' in your phone (call list shows them).");
            if (matches.Count > 1) throw new BridgeError($"{matches.Count} contacts are called '{wanted}': use the id ({string.Join(", ", matches.Select(c => c.id))}).");
            RequireFreeHands("a call");
            var talking = InstanceBehavior<UIs>.Instance?.playerHUD?.dialogUI;
            if (DialogController.current != null || (talking != null && talking.isPanelOpen))
                throw new BridgeError("You're in a call or a conversation already: finish it first (its own buttons, or ui escape).");
            var contact = matches[0];
            Log.Info($"Test bridge: calling {contact.id}.");
            // The conversation opened, and a frame later its call button pressed only if the game shows it usable
            // (ContactsApp.StartCall would press it regardless: a contact with no call button would call the last one).
            return WithNotices(() => ModContacts.OpenAndCall(contact), notices => new JObject
            {
                ["contact"] = contact.id, ["name"] = ContactName(contact),
                ["calling"] = DialogController.current != null && DialogController.current.contact == contact,
                ["open"] = new JArray(TestBridgeUi.Open()),
                ["notices"] = new JArray(notices),
            });
        }

        private static string ContactName(Contact contact)
        {
            var text = LocalizorManager.GetLocalization(contact.id, null);
            return string.IsNullOrEmpty(text) || text!.StartsWith("#", StringComparison.Ordinal) ? contact.id : text;
        }

        // ---- eat: from the paper bag in hands ----

        // { "item"? }: eats one of a food in the bag the player holds, as the hands panel's action button does
        // (CargoItemUi: paid, not sealed, a bag, a consumable; Item.TryToConsume, then one fewer in the bag). The first
        // food in the bag when no item is named.
        private static object Eat(JObject args)
        {
            RequireFreeHands("eating");
            var hands = PlayerHelper.ItemInstanceInHands;
            if (hands == null) throw new BridgeError("Your hands are empty: food you buy comes in a bag to eat from.");
            if (hands.ItemCached == null || !hands.ItemCached.HasTag(BigAmbitions.Tags.TagRef.Itemtag.isbag))
                throw new BridgeError($"You're holding {LocalizorManager.GetLocalization(hands.itemName, null)}, not a bag: only food in a paper bag is eaten from the hands.");
            var wanted = Str(args, "item");
            var foods = (hands.cargoInstances ?? new List<CargoInstance>()).Where(c => c != null && c.amount > 0 && c.paid && !c.IsSealed && c.ItemCached != null && c.ItemCached.isConsumable).ToList();
            var cargo = wanted == null ? foods.FirstOrDefault()
                : foods.FirstOrDefault(c => c.itemName == wanted || string.Equals(LocalizorManager.GetLocalization(c.itemName, null), wanted, StringComparison.OrdinalIgnoreCase));
            if (cargo == null)
                throw new BridgeError(foods.Count == 0 ? "Nothing in the bag can be eaten (unpaid, sealed or not food)."
                    : $"No '{wanted}' in the bag: {string.Join(", ", foods.Select(c => LocalizorManager.GetLocalization(c.itemName, null)))}.");
            var game = SaveGameManager.Current;
            var (hunger, energy) = (game.Hunger, game.Energy);
            var item = cargo.ItemCached;
            var name = LocalizorManager.GetLocalization(cargo.itemName, null);
            return WithNotices(() =>
            {
                if (item.TryToConsume()) hands.ReduceFromCargo(cargo, 1);
            }, notices =>
            {
                Log.Info($"Test bridge: ate {cargo.itemName}: hunger {hunger:F1} to {game.Hunger:F1}, energy {energy:F1} to {game.Energy:F1}.");
                return new JObject
                {
                    ["item"] = name, ["done"] = game.Hunger > hunger,
                    ["hunger"] = Math.Round(game.Hunger, 1), ["energy"] = Math.Round(game.Energy, 1),
                    ["notices"] = new JArray(notices),
                };
            });
        }

        // ---- place: furniture from the box in hands, set down in the player's own building ----

        private static readonly System.Reflection.MethodInfo? GroundPositionMethod =
            HarmonyLib.AccessTools.Method(typeof(BigAmbitions.PlacementSystem.PlacementSystem), "UpdateGroundItemPosition");
        private static readonly System.Reflection.FieldInfo? PlacementValidField =
            HarmonyLib.AccessTools.Field(typeof(BigAmbitions.PlacementSystem.PlacementSystem), "IsNewItemPlacementValid");

        // { "square" } or { "x", "z" }, with "rotation", "move", "check": the hands panel's Place for a box of furniture (ItemPanelUI.ClickPlace: the bed out of
        // its box), moved to that point of the floor as the mouse moves it there (PlacementSystem's ground step from a ray
        // straight down: the floor hit, blocked areas, the item's grid) and turned to the nearest of its rotation steps
        // (degrees, clockwise), then confirmed when the game finds the place free, as a click does. A refused place puts
        // the furniture back in its box. Only inside a building of the player's own; one mouse-free step, so the
        // per-frame mouse follow never runs in between.
        private static object Place(JObject args)
            => PlaceHeld(args, false);

        // Uses the same floor/grid/collision checks as furniture placement, but keeps the container closed.
        private static object Drop(JObject args)
        {
            RequireCarryControl();
            if (!PlayerHelper.IsHoldingItem) throw new BridgeError("Your hands are empty.");
            if (PlayerHelper.IsHoldingAMop)
                return WithNotices(() => CleaningStationController.ReturnMopToStation(), notices => new JObject
                {
                    ["item"] = "mop", ["placed"] = !PlayerHelper.IsHoldingAMop,
                    ["why"] = PlayerHelper.IsHoldingAMop ? "the game couldn't return the mop to its station" : null,
                    ["notices"] = new JArray(notices),
                });
            var at = PlayerHelper.GetPosition();
            var point = (JObject)args.DeepClone();
            if ((Num(args, "x") == null) != (Num(args, "z") == null)) throw new BridgeError("drop takes both x and z, or neither.");
            point["x"] = Num(args, "x") ?? at.x;
            point["z"] = Num(args, "z") ?? at.z;
            return PlaceHeld(point, true);
        }

        private static object PlaceHeld(JObject args, bool keepContainer)
        {
            if (!BuildingManager.CanBuildOnCurrentBuilding) throw new BridgeError(keepContainer
                ? "The game allows putting items down only inside a building you can edit; outside, load a hand truck instead."
                : "You can place furniture only inside a building you rent.");
            var plan = CurrentFloorPlan();
            var square = Str(args, "square");
            if (square != null && (args["x"] != null || args["z"] != null)) throw new BridgeError("place takes a square or x and z, not both.");
            var requested = square != null ? plan.Resolve(square) : new Vector3(
                (float)(Num(args, "x") ?? throw new BridgeError("place needs a square, or x and z.")), plan.Y,
                (float)(Num(args, "z") ?? throw new BridgeError("place needs both x and z.")));
            var x = requested.x;
            var z = requested.z;
            if (!keepContainer && args["check"] != null && args["check"]!.Type != JTokenType.Boolean)
                throw new BridgeError("place check must be true or false.");
            var check = !keepContainer && (Bool(args, "check") ?? false);
            var rotation = Num(args, "rotation") ?? 0;
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) || double.IsNaN(rotation) || double.IsInfinity(rotation))
                throw new BridgeError("Placement coordinates and rotation must be finite.");
            if (NavTargetsField == null) throw new BridgeError("The game's use targets weren't found (game update?).");
            if (PlacementRotationInput()) throw new BridgeError("Release the placement rotation controls before trying again.");
            if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode) throw new BridgeError("Something is being placed already.");
            if (GroundPositionMethod == null || PlacementValidField == null) throw new BridgeError("The game's placement step wasn't found (game update?).");
            // "move": furniture already standing in this building (the nearest whose name contains it) is picked up and set
            // down again through the game's own move (PlacementHelper.StartPlacementMode); a refused move puts it back.
            var moveName = keepContainer ? null : Str(args, "move");
            ItemInstance? hands = null;
            ItemController? moving = null;
            Item item;
            string itemName;
            if (moveName != null)
            {
                var me = PlayerHelper.GetPosition();
                moving = InstanceBehavior<BuildingManager>.Instance.allItemControllers
                    .Where(c => c != null && c.ItemInstance?.ItemCached != null && c.ItemInstance.ItemCached.isFurniture && OnPlayerFloor(plan, c) &&
                                (c.itemName.IndexOf(moveName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 LocalizorManager.GetLocalization(c.itemName, null).IndexOf(moveName, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(c => Vector3.Distance(c.transform.position, me)).FirstOrDefault()
                    ?? throw new BridgeError($"No furniture called '{moveName}' in this building.");
                item = moving.ItemInstance.ItemCached;
                itemName = moving.itemName;
            }
            else
            {
                hands = PlayerHelper.ItemInstanceInHands;
                var cargo = hands?.cargoInstances?.FirstOrDefault();
                if (!keepContainer && (hands == null || hands.itemName != "ba:itemname_closedcardboardbox" || cargo == null || cargo.ItemCached == null || !cargo.ItemCached.isFurniture))
                    throw new BridgeError("You aren't holding a box of furniture (shops pack what you buy in a box). To move furniture that stands here, add --move NAME.");
                if (keepContainer && (hands == null || hands.ItemCached == null || PlayerHelper.IsHoldingShoppingBasket ||
                    !(hands.ItemCached.HasTag(TagRef.Itemtag.isbox) || (hands.ItemCached.HasTag(TagRef.Itemtag.isbag) && hands.cargoInstances.Count > 0))))
                    throw new BridgeError("The game offers no put-down action for this item; return it or load it into storage.");
                if (!keepContainer && !cargo!.paid) throw new BridgeError("You haven't paid for it.");
                item = keepContainer ? hands!.ItemCached : cargo!.ItemCached;
                itemName = keepContainer ? hands!.itemName : cargo!.itemName;
            }
            if (item.wallMounted || item.stackType != 0 || item.snapToCeiling)
                throw new BridgeError("That goes on a wall, ceiling or other furniture; place covers only furniture that stands on the floor.");
            var name = LocalizorManager.GetLocalization(itemName, null);
            var step = item.degreesPerRotation > 0 ? item.degreesPerRotation : 45f;
            var yRotation = (float)(Math.Round(rotation / step) * step % 360 + 360) % 360;
            // Snapshot what native cancellation must conserve, including children and attachment relationships.
            var before = PlacementSnapshot();
            var oldSnap = BigAmbitions.PlacementSystem.PlacementSystem.snapFreePlacement;
            var caught = Notices.BeginCapture();
            bool placed = false, valid = false, blocked = false, inside = false, stuck = false, started = false, resolved = false;
            bool? reachable = null;
            string? blocker = null, placedId = null;
            var use = new JArray();
            var navBefore = new Dictionary<Transform, Vector3>();
            var floorSeen = "";
            Vector3 at = requested;
            try
            {
                if (moving != null) Buildings.Indoors.InteriorDesign.PlacementHelper.StartPlacementMode(moving, warpCursor: false);
                else
                {
                    var panel = InstanceBehavior<UIs>.Instance.playerHUD.itemPanelUI;
                    panel.SetItemInstance(hands, spawnIntoPlayerHands: false);
                    if (keepContainer) panel.ClickPlaceBox();
                    else panel.ClickPlace();
                }
                var placing = BigAmbitions.PlacementSystem.PlacementSystem.CurrentPlaceableItemBeingPlaced as ItemController;
                started = placing != null;
                if (placing != null)
                {
                    placing.ItemInstance.yRotation = yRotation;
                    var euler = placing.transform.eulerAngles;
                    placing.transform.eulerAngles = new Vector3(euler.x, yRotation, euler.z);
                    placedId = placing.ItemInstance.id;
                    BigAmbitions.PlacementSystem.PlacementSystem.snapFreePlacement = false;
                    Physics.SyncTransforms();
                    var top = new Vector3(x, plan.Y + .35f, z);
                    var ray = new Ray(top, Vector3.down);
                    inside = plan.Hit(requested, out var floor);
                    floorSeen = inside ? $"{floor.collider.name} (y {floor.point.y:F2})" : "no floor on the player's level";
                    if (inside) GroundPositionMethod.Invoke(null, new object[] { ray });
                    blocked = inside && BigAmbitions.PlacementSystem.PlacementSystem.placedInBlockedArea;
                    // A blocked surface returns before the native updater moves the ghost. Don't report its old pose
                    // or old use targets as the candidate's resolved position/reachability.
                    at = requested;
                    if (inside && !blocked)
                    {
                        at = placing.transform.position;
                        resolved = plan.Hit(at, out _) && Math.Abs(at.y - plan.Y) < .35f;
                        inside = resolved;
                    }
                    yRotation = placing.transform.eulerAngles.y;
                    valid = resolved && PlacementValidField.GetValue(null) is bool b && b;
                    if (blocked) blocker = floor.collider.name;
                    if (!valid && !blocked && inside)
                    {
                        var overlap = BigAmbitions.PlacementSystem.PlacementValidation.CurrentOverlappingCollider;
                        if (overlap != null)
                        {
                            var other = overlap.GetComponentInParent<ItemController>();
                            blocker = other != null ? LocalizorManager.GetLocalization(other.itemName, null) : overlap.name;
                        }
                    }
                    if (resolved)
                    {
                        // Preview needs refreshed candidate targets too; restore their caches after cancellation.
                        foreach (var member in placing.GetComponentsInChildren<EntityController>(true))
                            foreach (var t in (NavTargetsField?.GetValue(member) as Transform[]) ?? Array.Empty<Transform>())
                                if (t != null && !navBefore.ContainsKey(t)) navBefore[t] = t.localPosition;
                        placing.UpdateNavMeshTargets();
                        use = UseSquares(plan, placing);
                        reachable = placing.GetClosestNavMeshTargetPosition(PlayerHelper.GetPosition()) != Vector3.zero;
                    }
                    if (valid && !blocked && !check)
                    {
                        placing.ItemInstance.position = placing.transform.position;
                        placing.OnItemPositionUpdated();
                        BigAmbitions.PlacementSystem.PlacementSystem.SaveCurrentItemBeingMovedPosition();
                        Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementMode();
                        placed = true;
                        placing.UpdateNavMeshTargets();
                        use = UseSquares(plan, placing);
                        reachable = placing.GetClosestNavMeshTargetPosition(PlayerHelper.GetPosition()) != Vector3.zero;
                    }
                    else Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
                }
            }
            finally
            {
                try
                {
                    if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode)
                        Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
                    stuck = BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode;
                    if (!placed)
                        foreach (var target in navBefore) if (target.Key != null) target.Key.localPosition = target.Value;
                    Physics.SyncTransforms();
                }
                finally
                {
                    BigAmbitions.PlacementSystem.PlacementSystem.snapFreePlacement = oldSnap;
                    Notices.EndCapture(caught);
                }
            }
            var restored = !placed && !stuck && JToken.DeepEquals(before, PlacementSnapshot());
            Log.Info($"Test bridge: {(keepContainer ? "drop" : moving != null ? "place (move)" : "place")} {itemName} at x {x:F1}, z {z:F1}, rotation {yRotation:F0}: {(placed ? "placed" : check && valid && restored ? "fits (check, cancelled)" : !inside ? "not this building's floor" : blocked ? "blocked area" : "no room")}{(placed && reachable == false ? ", unreachable" : "")}{(stuck ? ", still placing" : "")}; floor {floorSeen}.");
            return new JObject
            {
                ["item"] = name, ["placed"] = placed, ["moved"] = moving != null, ["reachable"] = reachable, ["still_placing"] = stuck,
                ["checked"] = check, ["fits"] = inside && valid && !blocked, ["blocked_by"] = blocker,
                ["restored"] = placed ? (bool?)null : restored, ["id"] = placed ? placedId : moving?.ItemInstance.id,
                ["square"] = plan.Square(at), ["position_resolved"] = resolved, ["requested_square"] = square?.ToUpperInvariant(),
                ["floor"] = plan.Floor + 1, ["use"] = use,
                ["why"] = placed || (check && valid && !blocked && restored) ? null : stuck ? "still being placed: ui escape cancels it"
                    : !restored ? "native cancellation did not restore the original item/cargo state"
                    : !started ? "the game didn't start placing it" : !inside ? "that point isn't the floor of this building"
                    : blocked ? "that part of the floor can't take furniture" : "no room there (a wall or another thing is in the way)",
                ["x"] = TestBridgeState.Round(at.x), ["y"] = TestBridgeState.Round(at.y), ["z"] = TestBridgeState.Round(at.z), ["rotation"] = yRotation,
                ["notices"] = new JArray(caught),
            };
        }

        private static readonly System.Reflection.FieldInfo? NavTargetsField =
            HarmonyLib.AccessTools.Field(typeof(EntityController), "navMeshTargets");

        // { "radius"? }: read only. For each item near the player, its walk-to points as the game's reach check sees them
        // (EntityController.GetClosestNavMeshTargetPosition): the wall test, the path status and how far the path ends short.
        private static object NavProbe(JObject args)
        {
            var radius = (float)(Num(args, "radius") ?? 6);
            var me = PlayerHelper.GetPosition();
            var onMesh = UnityEngine.AI.NavMesh.SamplePosition(me, out var meHit, 2f, -1);
            var items = new JArray();
            foreach (var item in InstanceBehavior<BuildingManager>.Instance.allItemControllers)
            {
                if (item == null || Vector3.Distance(item.transform.position, me) > radius) continue;
                var targets = new JArray();
                foreach (var t in (NavTargetsField?.GetValue(item) as Transform[]) ?? new Transform[0])
                {
                    if (t == null) continue;
                    var wall = PathHelper.IsThereAWallBetweenTargetAndEntity(t.position, item.transform.position + item.transform.forward * 0.1f);
                    var path = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(me, t.position, PlayerController.navMeshQueryFilter, path);
                    var sampled = UnityEngine.AI.NavMesh.SamplePosition(t.position, out var tHit, 3f, -1);
                    targets.Add(new JObject
                    {
                        ["x"] = TestBridgeState.Round(t.position.x), ["y"] = TestBridgeState.Round(t.position.y), ["z"] = TestBridgeState.Round(t.position.z),
                        ["wall_between"] = wall, ["path"] = path.status.ToString(), ["corners"] = path.corners.Length,
                        ["end_gap"] = path.corners.Length > 0 ? Math.Round(Vector3.Distance(path.corners[path.corners.Length - 1], t.position), 2) : (double?)null,
                        ["navmesh_gap"] = sampled ? Math.Round(Vector3.Distance(tHit.position, t.position), 2) : (double?)null,
                    });
                }
                var closest = item.GetClosestNavMeshTargetPosition(me);
                items.Add(new JObject
                {
                    ["item"] = item.itemName, ["x"] = TestBridgeState.Round(item.transform.position.x), ["y"] = TestBridgeState.Round(item.transform.position.y),
                    ["z"] = TestBridgeState.Round(item.transform.position.z), ["rotation"] = TestBridgeState.Round(item.transform.eulerAngles.y),
                    ["reachable"] = closest != Vector3.zero, ["targets"] = targets,
                });
            }
            return new JObject
            {
                ["player"] = new JObject { ["x"] = TestBridgeState.Round(me.x), ["y"] = TestBridgeState.Round(me.y), ["z"] = TestBridgeState.Round(me.z),
                    ["on_navmesh"] = onMesh, ["navmesh_gap"] = onMesh ? Math.Round(Vector3.Distance(meHit.position, me), 2) : (double?)null },
                ["items"] = items,
            };
        }

        // ---- What the player has: hands, home ----

        // What the player holds: the item and its contents (a bag's food, a box's goods).
        internal static JObject? Hands()
        {
            var hands = PlayerHelper.ItemInstanceInHands;
            if (hands == null) return null;
            return new JObject
            {
                ["item"] = LocalizorManager.GetLocalization(hands.itemName, null),
                ["key"] = hands.itemName,
                ["contents"] = new JArray((hands.cargoInstances ?? new List<CargoInstance>()).Where(c => c != null && c.amount > 0).Select(c => new JObject
                {
                    ["item"] = LocalizorManager.GetLocalization(c.itemName, null), ["key"] = c.itemName, ["amount"] = c.amount, ["paid"] = c.paid,
                    ["food"] = c.ItemCached != null && c.ItemCached.isConsumable,
                })),
            };
        }

        // The homes the player rents (residential buildings), by address: where a bed of their own is.
        internal static JArray Homes()
        {
            var homes = new JArray();
            foreach (var registration in World.Registrations().Values)
            {
                if (registration == null || !registration.RentedByPlayer || registration.GetBuildingType() != "ba:buildingtype_residential") continue;
                var address = registration.Address;
                if (address != null) homes.Add(World.AddressKey(address.streetName, address.streetNumber));
            }
            return homes;
        }
    }
}
