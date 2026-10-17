#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Entities;
using Helpers;
using Localizor;
using Newtonsoft.Json.Linq;
using Player.HUD.ItemInfoOverlays;
using UI;
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
            var caught = Notices.BeginCapture();
            bool handled;
            var placingCancelled = false;
            try
            {
                // The hover sets the call to action before the click, as the mouse does (OnIoLeftClick skips it while an
                // overlay shows over the thing).
                CtaManager.UpdateCta(entity);
                handled = entity.OnIoLeftClick();
                // Moving furniture by hand isn't one of the tester's controls: it follows the real mouse.
                if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode)
                    placingCancelled = Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
            }
            catch
            {
                Notices.EndCapture(caught);
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
                Notices.EndCapture(caught);
                var position = PlayerHelper.GetPosition();
                return new JObject
                {
                    ["clicked"] = name,
                    ["kind"] = entity == null ? null : entity.GetType().Name,
                    ["click"] = string.IsNullOrEmpty(ctaKey) ? null : LocalizorManager.GetLocalization(ctaKey, null),
                    ["handled"] = handled,
                    ["placing_cancelled"] = placingCancelled,
                    ["still_walking"] = walking,
                    ["inside"] = InsideAddress(),
                    ["inside_before"] = insideBefore,
                    ["open"] = new JArray(TestBridgeUi.Open()),
                    ["notices"] = new JArray(caught),
                    ["x"] = TestBridgeState.Round(position.x), ["z"] = TestBridgeState.Round(position.z),
                };
            }, () => Notices.EndCapture(caught));
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

        // { "x", "z", "rotation"? }: the hands panel's Place for a box of furniture (ItemPanelUI.ClickPlace: the bed out of
        // its box), moved to that point of the floor as the mouse moves it there (PlacementSystem's ground step from a ray
        // straight down: the floor hit, blocked areas, the item's grid) and turned to the nearest of its rotation steps
        // (degrees, clockwise), then confirmed when the game finds the place free, as a click does. A refused place puts
        // the furniture back in its box. Only inside a building of the player's own; one mouse-free step, so the
        // per-frame mouse follow never runs in between.
        private static object Place(JObject args)
        {
            var x = Num(args, "x") ?? throw new BridgeError("place needs \"x\" and \"z\": a point on the floor (state's player x and z, or use list's).");
            var z = Num(args, "z") ?? throw new BridgeError("place needs \"x\" and \"z\".");
            var rotation = Num(args, "rotation") ?? 0;
            if (!BuildingManager.CanBuildOnCurrentBuilding) throw new BridgeError("You can place furniture only inside a building you rent.");
            if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode) throw new BridgeError("Something is being placed already.");
            var hands = PlayerHelper.ItemInstanceInHands;
            var cargo = hands?.cargoInstances?.FirstOrDefault();
            if (hands == null || hands.itemName != "ba:itemname_closedcardboardbox" || cargo == null || cargo.ItemCached == null || !cargo.ItemCached.isFurniture)
                throw new BridgeError("You aren't holding a box of furniture (shops pack what you buy in a box).");
            if (!cargo.paid) throw new BridgeError("You haven't paid for it.");
            if (cargo.ItemCached.wallMounted || cargo.ItemCached.stackType != 0)
                throw new BridgeError("That goes on a wall or on other furniture; place covers only furniture that stands on the floor.");
            if (GroundPositionMethod == null || PlacementValidField == null) throw new BridgeError("The game's placement step wasn't found (game update?).");
            var name = LocalizorManager.GetLocalization(cargo.itemName, null);
            var item = cargo.ItemCached;
            var step = item.degreesPerRotation > 0 ? item.degreesPerRotation : 45f;
            var yRotation = (float)(Math.Round(rotation / step) * step % 360 + 360) % 360;
            var caught = Notices.BeginCapture();
            bool placed = false, valid = false, blocked = false, inside = false, stuck = false, started = false;
            Vector3 at = default;
            try
            {
                var panel = InstanceBehavior<UIs>.Instance.playerHUD.itemPanelUI;
                panel.SetItemInstance(hands, spawnIntoPlayerHands: false);
                panel.ClickPlace();
                var placing = BigAmbitions.PlacementSystem.PlacementSystem.CurrentPlaceableItemBeingPlaced as ItemController;
                started = placing != null;
                if (placing != null)
                {
                    placing.ItemInstance.yRotation = yRotation;
                    var euler = placing.transform.eulerAngles;
                    placing.transform.eulerAngles = new Vector3(euler.x, yRotation, euler.z);
                    var top = new Vector3((float)x, PlayerHelper.GetPosition().y + 2f, (float)z);
                    var ray = new Ray(top, Vector3.down);
                    // The floor under the point must be this building's own (its placement grid): the game's camera never
                    // aims the mouse at ground outside the walls, but a point can be anywhere.
                    var grid = BigAmbitions.PlacementSystem.PlacementSystem.currentBuildingGrid;
                    inside = Physics.Raycast(ray, out var floor, 100f, (int)LayerHelper.groundLayerMask) && grid != null && grid.GetGroundGrid(floor.collider) != null;
                    if (inside) GroundPositionMethod.Invoke(null, new object[] { ray });
                    valid = inside && PlacementValidField.GetValue(null) is bool b && b;
                    blocked = BigAmbitions.PlacementSystem.PlacementSystem.placedInBlockedArea;
                    if (inside) at = placing.transform.position;
                    if (valid && !blocked)
                    {
                        placing.ItemInstance.position = at;
                        BigAmbitions.PlacementSystem.PlacementSystem.SaveCurrentItemBeingMovedPosition();
                        Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementMode();
                        placed = true;
                    }
                    else Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
                }
            }
            catch
            {
                if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode) Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
                Notices.EndCapture(caught);
                throw;
            }
            // Never left half placed: whatever the game didn't finish is cancelled, and if even that fails the reply says so.
            if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode)
            {
                Buildings.Indoors.InteriorDesign.PlacementHelper.CancelPlacementModeIfIsActive();
                stuck = BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode;
            }
            Log.Info($"Test bridge: place {cargo.itemName} at x {x:F1}, z {z:F1}, rotation {yRotation:F0}: {(placed ? "placed" : !inside ? "not this building's floor" : blocked ? "blocked area" : "no room")}{(stuck ? ", still placing" : "")}.");
            Notices.EndCapture(caught);
            return new JObject
            {
                ["item"] = name, ["placed"] = placed,
                ["why"] = placed ? null : stuck ? "still being placed: ui escape cancels it"
                    : !started ? "the game didn't start placing it" : !inside ? "that point isn't the floor of this building"
                    : blocked ? "that part of the floor can't take furniture" : "no room there (a wall or another thing is in the way)",
                ["x"] = TestBridgeState.Round(at.x), ["z"] = TestBridgeState.Round(at.z), ["rotation"] = yRotation,
                ["notices"] = new JArray(caught),
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
