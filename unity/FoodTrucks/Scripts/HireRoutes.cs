#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // Runs each crewed vehicle's route: Core's HireRunner says what the crew should do now, this does it.
    // The game has no NPC that drives a player vehicle (design: Technical answers, row 11), so a vehicle
    // on its way is taken off the map (its controller destroyed, its parking state Undefined so the game
    // charges no parking) and put back when the travel time is up: a truck in the nearest free parking space
    // to the stop, the way the game's private driver returns a car; a cart where the player stood it.
    internal sealed class HireRoutes : MonoBehaviour
    {
        private const float TickSeconds = 0.25f;
        // A stop with no free parking space nearby is tried again for this many game minutes, then wider.
        private const int RetryMinutes = 10;
        // Parking spaces this close count as the stop; after a wait, the crew takes one further out.
        private const float MaxParkingMetres = 60f;
        private const float WideParkingMetres = 200f;
        // Steps per vehicle per tick: enough to close, leave and so on after a load or a time skip.
        private const int MaxSteps = 4;

        private static HireRoutes? instance;
        private static readonly List<Action> later = new List<Action>();
        private HireRunner? runner;
        private float nextTick;
        // Refusals already told to the player: vehicle, stop, day.
        private readonly HashSet<(string, int, int)> refusalsShown = new HashSet<(string, int, int)>();
        // The game minute each vehicle's route has been run up to. Not saved: after a load it starts now.
        private readonly Dictionary<string, long> routedUntil = new Dictionary<string, long>();
        private readonly HashSet<string> broken = new HashSet<string>();
        private readonly HashSet<string> indoorsShown = new HashSet<string>();
        // The game minute the player last pushed or drove each vehicle.
        private readonly Dictionary<string, long> lastHeld = new Dictionary<string, long>();
        // The crew doesn't drive a vehicle away within this long of the player using it, or with the player this close.
        private const long StayPutMinutes = 60;
        private const float StayPutMetres = 15f;

        // Runs an action on the next frame, after the game's list picker has closed its current list.
        public static void Later(Action action)
        {
            lock (later) later.Add(action);
        }

        private void OnEnable() => instance = this;

        private void OnDisable()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            RunLater();
            // During sleep and other skips every frame counts, so each step is at most one frame of game time.
            if (Time.unscaledTime < nextTick && !CartSelling.Skipping) return;
            nextTick = Time.unscaledTime + TickSeconds;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            try
            {
                runner ??= new HireRunner(rules.Fleet);
                // The city load spawns vehicles where they were saved, those on their way too.
                HideInTransit(state);
                HireCrew.Check(state);
            }
            catch (Exception ex)
            {
                Log.Error("Hire routes: " + ex);
                return;
            }
            var now = HireCrew.Now();
            foreach (var vehicle in state.Vehicles.Where(v => (v.Crew != null && v.Crew.Count > 0) || v.Transit != null || v.HireStop != null).ToList())
            {
                if (broken.Contains(vehicle.VehicleId)) continue;
                try { Run(vehicle, now); }
                catch (Exception ex)
                {
                    // One vehicle's route stops; the others go on. A window its crew opened closes.
                    broken.Add(vehicle.VehicleId);
                    Log.Error($"Hire routes: {vehicle.VehicleType} {vehicle.VehicleId} stopped: {ex}");
                    CartSelling.Notify("foodtrucks:notification_routes_failed");
                    try { if (vehicle.HireStop != null) CartSelling.CloseByCrew(vehicle, now); }
                    catch (Exception closeEx) { Log.Error("Hire routes, closing: " + closeEx); }
                }
            }
        }

        private static void RunLater()
        {
            List<Action> due;
            lock (later)
            {
                if (later.Count == 0) return;
                due = later.ToList();
                later.Clear();
            }
            foreach (var action in due)
            {
                try { action(); }
                catch (Exception ex) { Log.Error("Hires: " + ex); }
            }
        }

        // A vehicle on its way has no business on the map: the city load spawns it where it was saved, so it
        // goes again. One that turns up by the player (the private driver brought it, say) stays: its trip ends there.
        private static void HideInTransit(SaveState state)
        {
            foreach (var vehicle in state.Vehicles.Where(v => v.Transit != null))
            {
                var instance = Instance(vehicle.VehicleId);
                var controller = instance == null ? null : VehicleHelper.GetVehicleController(instance);
                if (controller == null) continue;
                if (Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= FetchedMetres)
                {
                    instance!.parkingState = (ParkingState)vehicle.Transit!.FromParkingState;
                    vehicle.Transit = null;
                    vehicle.Arrival = null;
                    FoodTrucksSession.MarkChanged();
                    Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} turned up by the player; its trip ends here.");
                    continue;
                }
                TakeOffMap(controller);
                Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} is on its way; taken off the map again.");
            }
        }

        private const float FetchedMetres = 50f;

        // Steps through the minutes since the last tick in order: each moment a leg starts or ends, or a
        // transit is due, gets its own steps, and the crew's window sells up to it first. So a time skip
        // over a whole stop still opens, sells and closes it.
        private void Run(VehicleState vehicle, long now)
        {
            var instance = Instance(vehicle.VehicleId);
            if (instance == null) return;
            if (!routedUntil.TryGetValue(vehicle.VehicleId, out var from) || from > now) from = now;
            var t = from;
            for (var guard = 0; guard < 64; guard++)
            {
                StepAt(vehicle, instance, t);
                if (t >= now) break;
                t = NextMoment(vehicle, t, now);
            }
            routedUntil[vehicle.VehicleId] = now;
            CartSelling.AdvanceTo(vehicle, HireCrew.NowExact());
        }

        // The next leg boundary or arrival after t, or now.
        private static long NextMoment(VehicleState vehicle, long t, long now)
        {
            var next = now;
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
                foreach (var leg in plan.Legs)
                {
                    if (leg.Start > t && leg.Start < next) next = leg.Start;
                    if (leg.End > t && leg.End < next) next = leg.End;
                }
            // An arrival, and the two later tries for parking.
            if (vehicle.Transit != null)
                foreach (var due in new[] { vehicle.Transit.ArrivesAt, vehicle.Transit.ArrivesAt + RetryMinutes, vehicle.Transit.ArrivesAt + 2 * RetryMinutes })
                    if (due > t && due < next) next = due;
            // A new day gets its plan at midnight.
            var midnight = (t / 1440 + 1) * 1440;
            if (midnight > t && midnight < next) next = midnight;
            return next;
        }

        private void StepAt(VehicleState vehicle, VehicleInstance instance, long t)
        {
            CartSelling.AdvanceTo(vehicle, t);
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            runner!.EnsurePlan(vehicle, t, position.x, position.z, GameSpeed());
            for (var i = 0; i < MaxSteps; i++)
            {
                // Read after planning: the checks below depend on the leg the plan has running.
                var where = Where(vehicle, instance, t);
                var action = runner.Step(vehicle, t, where, GameSpeed());
                if (action.Kind == HireActionKind.None || !Do(vehicle, instance, action, t)) return;
            }
        }

        // Where the vehicle stands, and whether the crew may touch it or sell there.
        private VehicleWhere Where(VehicleState vehicle, VehicleInstance instance, long t)
        {
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var held = SaveGameManager.Current.ActiveVehicleId == vehicle.VehicleId;
            // Inside a building or an underground garage it isn't on the street to drive off from.
            var indoors = !string.IsNullOrEmpty(instance.streetName);
            if (indoors && indoorsShown.Add(vehicle.VehicleId))
            {
                Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} is in {instance.streetName} {instance.streetNumber}; the crew waits for it on the street.");
                CartSelling.Say("foodtrucks:notification_hire_indoors", KindOf(vehicle));
            }
            if (!indoors) indoorsShown.Remove(vehicle.VehicleId);
            if (held) lastHeld[vehicle.VehicleId] = t;
            var canSell = true;
            var hasStock = true;
            var stayPut = false;
            if (!held && !indoors && !vehicle.IsOpen && vehicle.Transit == null && HireRunner.Want(vehicle, t) != null)
            {
                canSell = KindOf(vehicle) == "truck" ? instance.parkingState == ParkingState.Legal : Legality.Check(position).Legal;
                var tables = DemandData.Tables;
                hasStock = tables == null || CartDemand.StockOf(instance, CartDemand.MenuOf(tables, instance).Menu).Count > 0;
                // The player just stepped out (to buy stock, say) or stands right by it: it stays where it is.
                // So does a vehicle whose crew are all off sick, training or being replaced.
                stayPut = (lastHeld.TryGetValue(vehicle.VehicleId, out var heldAt) && t - heldAt < StayPutMinutes) ||
                          (!BuildingManager.IsInsideBuilding && Vector3.Distance(PlayerHelper.GetPosition(), position) <= StayPutMetres) ||
                          HireCrew.OnShift(vehicle, t).Count == 0;
            }
            return new VehicleWhere(position.x, position.z, held || indoors, canSell, hasStock, stayPut);
        }

        // Does one step; false when nothing changed, so the loop stops.
        private bool Do(VehicleState vehicle, VehicleInstance instance, HireAction action, long t)
        {
            var kind = KindOf(vehicle);
            switch (action.Kind)
            {
                case HireActionKind.Close:
                    CartSelling.CloseByCrew(vehicle, t);
                    return !vehicle.IsOpen;
                case HireActionKind.Depart:
                    Depart(vehicle, instance, action, t, kind);
                    return true;
                case HireActionKind.Arrive:
                    return Arrive(vehicle, instance, action.Transit!, t, kind);
                case HireActionKind.Open:
                    var refusal = CartSelling.OpenByCrew(vehicle, action.Stop, t);
                    if (refusal == null)
                    {
                        CartSelling.Say("foodtrucks:notification_hire_open", kind, ("crew", CrewNames(vehicle)));
                        return true;
                    }
                    if (refusalsShown.Add((vehicle.VehicleId, action.Stop, HireRunner.DayOf(t))))
                    {
                        Log.Info($"Route: {kind} {vehicle.VehicleId} can't open at stop {action.Stop + 1}: {refusal}.");
                        var noun = LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null);
                        var reason = LocalizorManager.GetLocalization(refusal, null).Replace("{vehicle}", noun);
                        CartSelling.Say("foodtrucks:notification_hire_refused", kind, ("reason", reason));
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static void Depart(VehicleState vehicle, VehicleInstance instance, HireAction action, long t, string kind)
        {
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var heading = controller != null ? controller.transform.eulerAngles.y : ((Quaternion)instance.rotation).eulerAngles.y;
            if (controller != null)
            {
                TruckHatch.Set(controller, false);
                controller.SavePosition();
                TakeOffMap(controller);
            }
            var parking = instance.parkingState;
            // No parking fees or tickets while it's away (ParkingSimulator skips Undefined).
            instance.parkingState = ParkingState.Undefined;
            HireRunner.Depart(vehicle, action, t, position.x, position.y, position.z, heading);
            vehicle.Transit!.FromParkingState = (int)parking;
            FoodTrucksSession.MarkChanged();
            var stop = vehicle.Schedule!.Stops[action.Stop];
            Log.Info($"Route: {kind} {vehicle.VehicleId} leaves for stop {action.Stop + 1} ({stop.SpotKey}), arriving at minute {action.ArrivesAt} ({action.ArrivesAt - t} minutes).");
            CartSelling.Say("foodtrucks:notification_hire_departs", kind, ("crew", CrewNames(vehicle)),
                ("time", StaffDialog.Clock((int)(action.ArrivesAt % 1440))));
        }

        // Whether a truck standing there would have pavement by its hatch: the queue's own test, on a stand-in.
        private static bool HatchFacesPavement(Vector3 position, Quaternion rotation)
        {
            var probe = new GameObject("FoodTrucks.HatchProbe");
            try
            {
                probe.transform.SetPositionAndRotation(position, rotation);
                return QueueLine.HasWindowPlace(probe.transform, "truck");
            }
            finally { Object.Destroy(probe); }
        }

        // As the game's own vehicle sale does, traffic forgets the colliders before the vehicle goes.
        private static void TakeOffMap(VehicleController controller)
        {
            try
            {
                if (controller.vehicleCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.vehicleCollider);
                if (controller.additionalCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.additionalCollider);
            }
            catch (Exception ex) { Log.Error($"Route: collider event for {controller.vehicleInstance?.id}: {ex}"); }
            // Deactivating unregisters it from the game's vehicle list at once (VehicleController.OnDisable);
            // Destroy only happens at the end of the frame, and an arrival in the same frame must not find it.
            controller.gameObject.SetActive(false);
            Object.Destroy(controller.gameObject);
        }

        // A truck goes into the nearest free parking space near the stop, a cart where the player stood it.
        // With no room near the stop, the truck tries again a little later; after that, wider; and in the
        // end it goes back where it left from, so it is never lost off the map.
        private static bool Arrive(VehicleState vehicle, VehicleInstance instance, TransitState transit, long t, string kind)
        {
            var target = new Vector3((float)transit.ToX, (float)transit.ToY, (float)transit.ToZ);
            Vector3 position;
            Quaternion rotation;
            string? neighbourhood = null;
            var atStop = true;
            if (kind == "truck")
            {
                var late = t - transit.ArrivesAt;
                var spot = FreeParking(target, MaxParkingMetres) ?? (late >= RetryMinutes ? FreeParking(target, WideParkingMetres) : null);
                if (spot == null && late < 2 * RetryMinutes)
                {
                    if (late == 0) Log.Info($"Route: no free parking space near {transit.ToSpotKey} for truck {vehicle.VehicleId}; trying again.");
                    return false;
                }
                if (spot != null) (position, rotation, neighbourhood) = spot.Value;
                else
                {
                    // Back towards where it left from: a free space there, or its own spot if nothing has taken it.
                    var from = new Vector3((float)transit.FromX, (float)transit.FromY, (float)transit.FromZ);
                    var back = FreeParking(from, WideParkingMetres);
                    if (back != null) (position, rotation, neighbourhood) = back.Value;
                    else if (Physics.CheckSphere(from + Vector3.up, 1.5f, 1 << LayerHelper.VehiclesLayerIndex))
                    {
                        // Not even its own spot is free: it stays on its way and tries again each tick.
                        if (late == 2 * RetryMinutes) Log.Info($"Route: truck {vehicle.VehicleId} has nowhere to park near {transit.ToSpotKey} or where it left from; still trying.");
                        return false;
                    }
                    else
                    {
                        position = from;
                        rotation = Quaternion.Euler(0f, (float)transit.FromHeading, 0f);
                    }
                    atStop = false;
                    Log.Info($"Route: no free parking space for truck {vehicle.VehicleId} near {transit.ToSpotKey}; it goes back towards where it left from.");
                }
            }
            else
            {
                position = target;
                rotation = Quaternion.Euler(0f, (float)transit.ToHeading, 0f);
            }

            // The hatch has to face the pavement: a lane's rotation can point either way along the kerb. Decided
            // before the truck is placed, so it is placed once, already turned.
            if (kind == "truck" && atStop && !HatchFacesPavement(position, rotation)) rotation *= Quaternion.Euler(0f, 180f, 0f);
            var controller = Place(instance, position, rotation);
            if (controller == null)
            {
                Log.Error($"Route: {kind} {vehicle.VehicleId} could not be placed at {position}.");
                return false;
            }
            // A parking space is legal; back on its own old spot, it is what it was.
            instance.parkingState = kind == "truck" && !atStop && neighbourhood == null ? (ParkingState)transit.FromParkingState : ParkingState.Legal;
            if (neighbourhood != null) instance.parkingNeighbourhood = neighbourhood;
            var placed = controller.transform.position;
            HireRunner.Arrived(vehicle, placed.x, placed.z, atStop, t);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: {kind} {vehicle.VehicleId} arrived {(atStop ? "at" : "back from")} {transit.ToSpotKey}: {placed}, {Vector3.Distance(placed, target):F0} m from the stop.");
            return true;
        }

        // Puts the vehicle at a position: moves its controller if it has one, or spawns it as the game does
        // when the private driver brings a car back. The position is written to the save either way.
        private static VehicleController? Place(VehicleInstance instance, Vector3 position, Quaternion rotation)
        {
            var controller = VehicleHelper.GetVehicleController(instance);
            if (controller != null)
            {
                VehicleHelper.TeleportVehicle(controller, position, rotation);
                // TeleportVehicle moves the rigidbody; the transform follows on the next physics step, and the
                // save and the hatch check read the transform now.
                controller.transform.SetPositionAndRotation(position, rotation);
                controller.SavePosition();
                return controller;
            }
            // CreateAndSpawnVehicle adds the instance to the end of the list; it goes back to its old place after.
            var list = SaveGameManager.Current.VehicleInstances;
            var index = list.IndexOf(instance);
            list.Remove(instance);
            try
            {
                controller = VehicleHelper.CreateAndSpawnVehicle(instance, position, rotation);
            }
            finally
            {
                list.Remove(instance);
                list.Insert(index >= 0 && index <= list.Count ? index : list.Count, instance);
            }
            if (controller != null) PrefabFixes.ApplyTo(controller);
            return controller;
        }

        // The nearest free parking space to the stop, as the game's private driver finds one: lanes by distance,
        // no disabled or delivery lanes, a random free slot in the lane that no player vehicle blocks.
        private static (Vector3, Quaternion, string?)? FreeParking(Vector3 target, float maxMetres)
        {
            // The lanes belong to the city scene: found once per city load.
            if (laneCache == null || laneCache.Any(l => l == null))
                laneCache = Object.FindObjectsByType<ParkingLaneGenerator>(FindObjectsSortMode.None);
            var lanes = laneCache
                .Where(l => l != null && !l.isHandicapParking && !l.preventUseByPrivateDriver && !l.designatedDeliveryVanSpot && l.chanceOfFreeSpot <= 99)
                .OrderBy(l => (l.GetWorldCenterPosition() - target).sqrMagnitude)
                .Take(12);
            foreach (var lane in lanes)
            {
                if (!lane.TryGetRandomFreeSpotForPlayerVehicle(out var position, out var rotation, 1 << LayerHelper.VehiclesLayerIndex)) continue;
                if (Vector3.Distance(position, target) > maxMetres) continue;
                return (position, rotation, lane.neighbourhood);
            }
            return null;
        }

        private static string CrewNames(VehicleState vehicle) =>
            string.Join(", ", (vehicle.Crew ?? new List<string>()).Select(HireCrew.Find).Where(e => e != null).Select(e => HireCrew.Name(e!)));

        // Game minutes per real second, as the game's taxi times its trips.
        internal static double GameSpeed()
        {
            var speed = PlayerPrefSettings.GameSpeed;
            return speed > 0 ? speed : 1.0;
        }

        private static string KindOf(VehicleState vehicle) => string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;

        private static ParkingLaneGenerator[]? laneCache;

        public static void Reset()
        {
            lock (later) later.Clear();
            laneCache = null;
        }

        private static VehicleInstance? Instance(string id) =>
            SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
    }
}
