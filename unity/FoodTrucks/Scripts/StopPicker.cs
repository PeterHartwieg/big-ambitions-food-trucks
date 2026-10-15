#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using Streets;
using UI;
using UI.InGameUI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // Adding a route stop from BizMan by picking a building on the city map (Peter, 11 October 2026). The vehicle
    // page's "Add stop on the map" opens the map in a pick mode, much as the game's taxi mode does: a click on a
    // building shows "Add as a stop" in the building card (a copy of its Set destination button, shown by a postfix
    // on BuildingResume.SetButtons). The stop then snaps to the nearest kerbside place within 40 m of the entrance
    // where a truck parks legally by the game's wheel test with its hatch to the pavement (Core StopSnap, BaySnap's
    // search, HatchCheck), or for a cart to the pavement just past that kerb. A marker shows the spot and the player
    // confirms; the times are asked after the map closes. Closing the map ends the mode.
    internal static class StopPicker
    {
        private static readonly Color MarkerColour = new Color(0.95f, 0.70f, 0.15f);
        private static bool ready;
        private static string? vehicleId;
        private static Button? addButton;
        private static GameObject? markerTarget;
        private static PointOfInterest? marker;
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);

        public static bool Active => vehicleId != null;

        // Called once after patching: the pick mode needs its button in the building card.
        internal static void CheckReady()
        {
            var original = AccessTools.Method(typeof(BuildingResume), "SetButtons");
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            ready = info != null && info.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(StopPicker));
            Log.Info(ready ? "Map pick: on (\"Add as a stop\" in the building card)." : "Map pick: off (a patch it needs isn't active); stops are added with the vehicle page's \"Add the spot where it stands\" only.");
        }

        public static bool Ready => ready;

        // Opens the city map to pick a stop for the vehicle. False when the map can't be used now.
        public static bool Start(string id)
        {
            var map = InstanceBehavior<CityManager>.Instance?.cityMap;
            if (!ready || map == null || !CityMap.CanOpenMap())
            {
                CartSelling.Notify("foodtrucks:notification_pick_unavailable");
                return false;
            }
            GlobalEvents.onCityMapToggle = (Action<bool>)Delegate.Remove(GlobalEvents.onCityMapToggle, (Action<bool>)OnMapToggle);
            GlobalEvents.onCityMapToggle = (Action<bool>)Delegate.Combine(GlobalEvents.onCityMapToggle, (Action<bool>)OnMapToggle);
            RemoveMarker();
            vehicleId = id;
            var kind = KindOf(id);
            Log.Info($"Map pick: started for {kind} {id}.");
            InstanceBehavior<UIs>.Instance.fullMenu.Toggle(show: false);
            HireRoutes.Later(() =>
            {
                try
                {
                    if (!CityMap.IsOpen) map.Toggle();
                    if (!CityMap.IsOpen)
                    {
                        End("the map didn't open");
                        CartSelling.Notify("foodtrucks:notification_pick_unavailable");
                        return;
                    }
                    CartSelling.Say("foodtrucks:notification_pick_start", kind);
                }
                catch (Exception ex)
                {
                    Log.Error("Map pick, opening the map: " + ex);
                    End("error");
                }
            });
            return true;
        }

        private static void OnMapToggle(bool open)
        {
            try { if (!open && Active) End("the map closed"); }
            catch (Exception ex) { Log.Error("Map pick: " + ex); }
        }

        private static void End(string why)
        {
            if (vehicleId != null) Log.Info($"Map pick: ended for {vehicleId} ({why}).");
            vehicleId = null;
            RemoveMarker();
            if (addButton != null) addButton.gameObject.SetActive(false);
        }

        // Harmony, after BuildingResume.SetButtons: in pick mode a building's card offers "Add as a stop" in place of
        // Set destination and the BizMan button. Otherwise the copy stays hidden and the game's buttons are its own.
        public static void SetButtonsPostfix(BuildingResume __instance)
        {
            try
            {
                var show = Active && CityMap.IsOpen && __instance.CityBuildingController != null && __instance.CityBuildingController.building != null;
                if (!show)
                {
                    if (addButton != null) addButton.gameObject.SetActive(false);
                    return;
                }
                if (!Ensure(__instance)) return;
                __instance.setDestinationButton.gameObject.SetActive(false);
                __instance.bizManButton.gameObject.SetActive(false);
                addButton!.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                if (logged.Add("buttons")) Log.Error("Map pick, building card buttons: " + ex);
            }
        }

        // The building card's Set destination button, copied once per card (the card belongs to the city scene).
        private static bool Ensure(BuildingResume resume)
        {
            if (addButton != null && addButton.transform.parent == resume.setDestinationButton.transform.parent) return true;
            var template = resume.setDestinationButton;
            if (template == null)
            {
                Fail("the building card has no Set destination button to copy");
                return false;
            }
            var button = Object.Instantiate(template, template.transform.parent);
            button.name = "FoodTrucks.AddStopButton";
            // A fresh event drops the prefab's own listener (BuildingResume.SetDestination).
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() =>
            {
                try { Pick(resume); }
                catch (Exception ex) { Log.Error("Map pick: " + ex); }
            });
            var label = button.GetComponentInChildren<TextLocalizationComponent>(true);
            if (label == null)
            {
                Object.Destroy(button.gameObject);
                Fail("the Set destination button has no label to change");
                return false;
            }
            label.SetData(LanguageChangeEventDataHolder.Create("foodtrucks:map_pick_button"));
            button.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            addButton = button;
            Log.Info("Map pick: \"Add as a stop\" copied from the building card's Set destination button.");
            return true;
        }

        private static void Fail(string why)
        {
            Log.Error($"Map pick: {why}; the pick mode ends.");
            CartSelling.Notify("foodtrucks:notification_pick_unavailable");
            End("no button");
        }

        // A building picked: find the spot, show it and ask.
        private static void Pick(BuildingResume resume)
        {
            var id = vehicleId;
            var building = resume.CityBuildingController;
            if (id == null || building == null || building.building == null) return;
            var kind = KindOf(id);
            var doors = building.entranceDoors;
            var door = doors != null && doors.Length > 0 && doors[0] != null && doors[0].doorTransform != null ? doors[0].doorTransform.position : building.transform.position;
            var address = building.building.Address?.ToFormattedString() ?? "?";
            var start = Time.realtimeSinceStartup;
            var spot = Snap(kind, door, out var refusal, out var how);
            var ms = (Time.realtimeSinceStartup - start) * 1000f;
            if (spot == null)
            {
                Log.Info($"Map pick: no {kind} spot by {address} (entrance x {door.x:F1}, z {door.z:F1}): {how} ({ms:F0} ms).");
                CartSelling.Notify(refusal, ("address", address), ("max", StopSnap.MaxMetres.ToString("F0")));
                return;
            }
            var (position, heading, distance) = spot.Value;
            Log.Info($"Map pick: {kind} stop by {address} at x {position.x:F1}, y {position.y:F1}, z {position.z:F1}, heading {heading:F0}, " +
                     $"{distance:F1} m from the entrance; {how} ({ms:F0} ms).");
            ShowMarker(position);
            resume.ToggleOptions(show: false);
            var body = LanguageChangeEventDataHolder.Create(kind == "truck" ? "foodtrucks:map_pick_confirm_truck" : "foodtrucks:map_pick_confirm_cart",
                new Dictionary<string, string> { ["address"] = address, ["distance"] = distance.ToString("F0") });
            HudConfirm.Show(LanguageChangeEventDataHolder.Create("foodtrucks:map_pick_confirm_header"), body,
                () => Guard(() => Accept(id, position, heading)), () => Guard(RemoveMarker), allowConfirmationSkip: false);
        }

        private static void Accept(string id, Vector3 position, float heading)
        {
            // A confirm answered after the map closed or the pick ended (or moved on to another vehicle) adds nothing.
            if (vehicleId != id || !CityMap.IsOpen)
            {
                Log.Info($"Map pick: the confirm for {id} came after the pick {(vehicleId == null ? "ended" : "moved on")}{(CityMap.IsOpen ? "" : " (map closed)")}; nothing added.");
                RemoveMarker();
                return;
            }
            End("stop picked");
            InstanceBehavior<CityManager>.Instance?.cityMap?.Close();
            // Back to the vehicle's BizMan page whether a stop went in or the times were cancelled.
            void Reopen() => HireRoutes.Later(() => BizManVehiclePage.OpenFor(id));
            HireRoutes.Later(() => RouteStops.AskTimesAndAdd(id, position, heading, Reopen, Reopen));
        }

        // ---- The spot ----

        // Where a stop by this entrance goes: position, heading and metres from the entrance; null with the refusal's
        // message key. `how` says what was found, for the log.
        private static (Vector3 Position, float Heading, float Distance)? Snap(string kind, Vector3 door, out string refusal, out string how)
        {
            refusal = kind == "truck" ? "foodtrucks:notification_pick_none_truck" : "foodtrucks:notification_pick_none_cart";
            var probe = new Legality.ParkingProbe(door, (float)StopSnap.MaxMetres + 5f);
            var traffic = TrafficLanes.Read(door, (float)LaneReach.CoverRadius(StopSnap.MaxMetres, HatchCheck.LaneSearchMetres));
            var detail = $"{probe.Count} parking colliders{(probe.Truncated ? " (truncated)" : "")}, {traffic.Describe()}";
            if (probe.Count == 0 && !traffic.Known)
            {
                refusal = "foodtrucks:notification_pick_not_loaded";
                how = $"neither parking lanes nor traffic lanes within reach ({detail})";
                return null;
            }

            (Vector3, float)? cartSpot = null;
            string reading = "";
            var found = StopSnap.Nearest(door.x, door.z, probe.Legal, probe.Axes, Legality.TruckFootprint(), (x, z, ax, az) =>
            {
                var at = new Vector3((float)x, GroundY((float)x, (float)z, door.y), (float)z);
                var along = Quaternion.LookRotation(new Vector3((float)ax, 0f, (float)az), Vector3.up);
                var forward = HatchCheck.Probe(at, along, traffic);
                if (kind == "truck")
                {
                    var reverse = HatchCheck.Probe(at, along * Quaternion.Euler(0f, 180f, 0f), traffic);
                    var choice = StopSnap.ChooseHeading(forward.Verdict, reverse.Verdict);
                    if (choice == HeadingChoice.None) return null;
                    reading = choice == HeadingChoice.Along ? forward.ToString() : reverse.ToString();
                    return choice == HeadingChoice.Along ? StopSnap.HeadingDegrees(ax, az) : StopSnap.HeadingDegrees(-ax, -az);
                }
                // A cart: on the pavement side, just past the kerb.
                var side = StopSnap.PavementSide(forward.Hatch, forward.Opposite);
                if (side == 0) return null;
                var pavement = side > 0 ? forward.Hatch.Pavement!.Value : forward.Opposite.Pavement!.Value;
                var right = along * Vector3.right;
                foreach (var offset in StopSnap.CartOffsets(pavement))
                {
                    var wanted = at + right * (side * (float)offset);
                    if (!CartCanStand(wanted, traffic, out var standing)) continue;
                    cartSpot = (standing, (float)StopSnap.HeadingDegrees(ax, az));
                    reading = $"pavement on the {(side > 0 ? "right" : "left")} {pavement:F1} m out, cart {offset:F2} m out";
                    return StopSnap.HeadingDegrees(ax, az);
                }
                return null;
            });

            if (kind == "truck")
            {
                if (found == null)
                {
                    how = $"no legal place with the hatch to the pavement within {StopSnap.MaxMetres:F0} m ({detail})";
                    return null;
                }
                var position = new Vector3((float)found.X, GroundY((float)found.X, (float)found.Z, door.y), (float)found.Z);
                how = $"legal parking, {reading}; {detail}";
                return (position, (float)found.Heading, Flat(position, door));
            }
            if (found != null && cartSpot != null)
            {
                how = $"by a parking lane, {reading}; {detail}";
                return (cartSpot.Value.Item1, cartSpot.Value.Item2, Flat(cartSpot.Value.Item1, door));
            }
            // No parking lane to go by: the kerb of the nearest driving lane, towards the entrance.
            var kerb = KerbByLane(door, traffic);
            if (kerb != null)
            {
                how = $"by the nearest driving lane (no parking lane fits); {detail}";
                return (kerb.Value.Position, kerb.Value.Heading, Flat(kerb.Value.Position, door));
            }
            how = $"no pavement by a kerb within {StopSnap.MaxMetres:F0} m ({detail})";
            return null;
        }

        // From the nearest driving lane's centre towards the entrance, the first pavement a cart may stand on, a step in.
        private static (Vector3 Position, float Heading)? KerbByLane(Vector3 door, TrafficLanes traffic)
        {
            var near = traffic.Nearest(door);
            if (near == null) return null;
            var (point, direction) = near.Value;
            if (Flat(point, door) > StopSnap.MaxMetres) return null;
            var outward = new Vector3(-direction.z, 0f, direction.x);
            if (Vector3.Dot(door - point, outward) < 0f) outward = -outward;
            for (var s = 2f; s <= 15f; s += 0.25f)
            {
                var wanted = point + outward * s;
                wanted.y = GroundY(wanted.x, wanted.z, door.y);
                if (!CartCanStand(wanted, traffic, out _)) continue;
                var inward = wanted + outward * 0.75f;
                var spot = CartCanStand(inward, traffic, out var standing) ? standing : wanted;
                return (spot, (float)StopSnap.HeadingDegrees(direction.x, direction.z));
            }
            return null;
        }

        // Pavement by the hatch rule's own reading of the point, and a legal pitch by the cart's siting rules.
        private static bool CartCanStand(Vector3 wanted, TrafficLanes traffic, out Vector3 standing)
        {
            standing = wanted;
            var walkable = NavMesh.SamplePosition(wanted + Vector3.up * 0.3f, out var hit, 0.6f, QueuePerson.Filter) &&
                           new Vector2(hit.position.x - wanted.x, hit.position.z - wanted.z).magnitude <= 0.45f;
            if (!walkable) return false;
            standing = hit.position;
            var lanes = traffic.Within(standing, HatchCheck.LaneSearchMetres);
            var ground = HatchSide.Classify(lanes.Distance(standing), Legality.RoadBelow(standing), Legality.InParkingLane(standing),
                Legality.NearCrosswalk(standing, 0.5f), walkable);
            return ground == Ground.Pavement && Legality.Check(standing).Legal;
        }

        // The ground's height at a point: the walkable NavMesh nearby, else the height given.
        private static float GroundY(float x, float z, float near)
        {
            var at = new Vector3(x, near, z);
            return NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas) ? hit.position.y : near;
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ---- The marker ----

        private static void ShowMarker(Vector3 position)
        {
            RemoveMarker();
            var map = InstanceBehavior<CityManager>.Instance?.cityMap;
            var references = InstanceBehavior<GlobalReferences>.Instance;
            if (map == null || references == null) return;
            markerTarget = new GameObject("FoodTrucks.StopPickMarker");
            markerTarget.transform.position = position;
            marker = map.AddPoi(markerTarget.transform, references.vehiclePOIIcon, MarkerColour, LocalizorManager.GetLocalization("foodtrucks:map_pick_marker", null));
            marker.SetHidden(false);
        }

        private static void RemoveMarker()
        {
            // Destroying the marker takes it off the map's list (PointOfInterest.OnDestroy).
            if (marker != null) Object.Destroy(marker.gameObject);
            if (markerTarget != null) Object.Destroy(markerTarget);
            marker = null;
            markerTarget = null;
        }

        // On leaving the city: the button and marker belong to its scene.
        public static void Reset()
        {
            vehicleId = null;
            RemoveMarker();
            addButton = null;
        }

        private static string KindOf(string id)
        {
            var game = SaveGameManager.Current;
            var instance = game?.VehicleInstances.FirstOrDefault(v => v.id == id) ?? game?.privateDriverVehicleInstances?.FirstOrDefault(v => v.id == id);
            return FoodTrucksMod.KindOf(instance?.vehicleTypeName) ?? "cart";
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error("Map pick: " + ex); }
        }
    }
}
