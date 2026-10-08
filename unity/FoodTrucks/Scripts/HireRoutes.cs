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

        // The earliest game minute a route still has to run from, null when none lags (CartSelling.SoldThrough).
        internal static long? RoutedThrough() =>
            instance == null ? null : instance.routedUntil.Where(p => !instance.broken.Contains(p.Key)).Select(p => (long?)p.Value).Min();

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
            var running = state.Vehicles.Where(v => (v.Crew != null && v.Crew.Count > 0) || v.Transit != null || v.HireStop != null).ToList();
            // A vehicle that sat out ticks (no crew for a while) starts again from now, not from where it stopped.
            foreach (var id in routedUntil.Keys.Where(id => running.All(v => v.VehicleId != id)).ToList()) routedUntil.Remove(id);
            foreach (var vehicle in running)
            {
                if (broken.Contains(vehicle.VehicleId))
                {
                    // A stopped route still brings a vehicle on its way back, as soon as there is room.
                    if (vehicle.Transit != null)
                    {
                        try { Recover(vehicle); }
                        catch (Exception ex) { Log.Error("Hire routes, bringing it back: " + ex); }
                    }
                    continue;
                }
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
        // goes again. Later, one that turns up by the player somewhere other than where it left from (the private
        // driver brought it, say) stays: its trip ends there.
        private void HideInTransit(SaveState state)
        {
            var afterLoad = !loadHidden;
            loadHidden = true;
            foreach (var vehicle in state.Vehicles.Where(v => v.Transit != null))
            {
                var instance = Instance(vehicle.VehicleId);
                var controller = instance == null ? null : VehicleHelper.GetVehicleController(instance);
                if (controller == null) continue;
                // The player got in (it came back with the private driver, say): the trip is over.
                if (SaveGameManager.Current.ActiveVehicleId == vehicle.VehicleId)
                {
                    if (instance!.parkingState == ParkingState.Undefined) instance.parkingState = ParkingState.Legal;
                    vehicle.Transit = null;
                    vehicle.Arrival = null;
                    FoodTrucksSession.MarkChanged();
                    Log.Info($"Route: the player is in {vehicle.VehicleType} {vehicle.VehicleId}; its trip ends here.");
                    continue;
                }
                var from = new Vector3((float)vehicle.Transit!.FromX, (float)vehicle.Transit.FromY, (float)vehicle.Transit.FromZ);
                if (!afterLoad && Vector3.Distance(controller.transform.position, from) > 5f &&
                    Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= FetchedMetres)
                {
                    // Whoever brought it set its parking; it only needs to be counted again if nobody did.
                    if (instance!.parkingState == ParkingState.Undefined) instance.parkingState = ParkingState.Legal;
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
        // The first tick after a load hides whatever the load spawned, without asking how it got there.
        private bool loadHidden;
        // The vehicles' collision hulls as boxes, a little spare (art/README.md): the truck's runs x −1.23…1.26,
        // y 0…2.90, z −3.15…2.65; the cart's body x ±0.48, y 0…0.95, z ±0.70.
        private static readonly Vector3 TruckHalfSize = new Vector3(1.3f, 1.45f, 2.95f);
        private static readonly Vector3 TruckCentre = new Vector3(0f, 1.55f, -0.25f);
        private static readonly Vector3 CartHalfSize = new Vector3(0.5f, 0.5f, 0.75f);
        private static readonly Vector3 CartCentre = new Vector3(0f, 0.55f, 0f);

        // Steps through the minutes since the last tick in order: each moment a leg starts or ends, or a
        // transit is due, gets its own steps, and the crew's window sells up to it first. So a time skip
        // over a whole stop still opens, sells and closes it.
        private void Run(VehicleState vehicle, long now)
        {
            var instance = Instance(vehicle.VehicleId);
            if (instance == null)
            {
                // Away with the private driver, say: when it comes back, the route goes on from then.
                routedUntil.Remove(vehicle.VehicleId);
                return;
            }
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
            // The same starting point as the payroll's preview (HireRunner.PlanFrom).
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var (planX, planZ) = HireRunner.PlanFrom(vehicle, position.x, position.z);
            runner!.EnsurePlan(vehicle, t, planX, planZ, GameSpeed());
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
            // A truck at a catering gig stays there, as one the player holds does.
            var held = SaveGameManager.Current.ActiveVehicleId == vehicle.VehicleId || Catering.Busy(vehicle.VehicleId);
            // Inside a building or an underground garage it isn't on the street to drive off from.
            var indoors = !string.IsNullOrEmpty(instance.streetName);
            if (indoors && indoorsShown.Add(vehicle.VehicleId))
            {
                Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} is in {instance.streetName} {instance.streetNumber}; the crew waits for it on the street.");
                CartSelling.Say("foodtrucks:notification_hire_indoors", KindOf(vehicle));
            }
            if (!indoors) indoorsShown.Remove(vehicle.VehicleId);
            // After a load, nobody knows when the player last used it: as if just now, so nothing drives off at once.
            if (held || !lastHeld.ContainsKey(vehicle.VehicleId)) lastHeld[vehicle.VehicleId] = t;
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

        // A stopped route never leaves its vehicle off the map: one on its way goes back where it left from, or
        // for a truck into a free space near there. Tried every tick until there is room.
        private static void Recover(VehicleState vehicle)
        {
            var transit = vehicle.Transit;
            var instance = Instance(vehicle.VehicleId);
            if (transit == null || instance == null) return;
            var kind = KindOf(vehicle);
            var from = new Vector3((float)transit.FromX, (float)transit.FromY, (float)transit.FromZ);
            var rotation = Quaternion.Euler(0f, (float)transit.FromHeading, 0f);
            var position = from;
            var parking = (ParkingState)transit.FromParkingState;
            string? neighbourhood = null;
            if (Taken(from, rotation, kind))
            {
                if (kind == "truck")
                {
                    var spot = FreeParking(from, WideParkingMetres);
                    if (spot == null) return;
                    (position, rotation, neighbourhood) = spot.Value;
                    parking = ParkingState.Legal;
                }
                else
                {
                    // A cart tries the stop it was heading for instead.
                    var to = new Vector3((float)transit.ToX, (float)transit.ToY, (float)transit.ToZ);
                    var toRotation = Quaternion.Euler(0f, (float)transit.ToHeading, 0f);
                    if (Taken(to, toRotation, kind))
                    {
                        if (recoveryWaitShown.Add(vehicle.VehicleId))
                            Log.Info($"Route: cart {vehicle.VehicleId} waits off the map: both where it left from and its stop are taken.");
                        return;
                    }
                    position = to;
                    rotation = toRotation;
                }
            }
            var controller = Place(instance, position, rotation);
            if (controller == null) return;
            instance.parkingState = parking;
            if (neighbourhood != null) instance.parkingNeighbourhood = neighbourhood;
            vehicle.Transit = null;
            vehicle.Arrival = null;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} brought back to {position} after its route stopped.");
        }

        // Whether another vehicle stands where this one's hull would be.
        private static bool Taken(Vector3 position, Quaternion rotation, string kind) =>
            Physics.CheckBox(position + rotation * (kind == "truck" ? TruckCentre : CartCentre), kind == "truck" ? TruckHalfSize : CartHalfSize,
                rotation, 1 << LayerHelper.VehiclesLayerIndex);

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
            // The game's own removal tells its listeners (multi-storey car parks keep a list of vehicles).
            try { VehicleHelper.onVehicleDestroyed?.Invoke(controller); }
            catch (Exception ex) { Log.Error($"Route: vehicle destroyed event for {controller.vehicleInstance?.id}: {ex}"); }
            // Its save hook would write its old pose over the instance until OnDestroy runs at the frame's end;
            // the same delegate is removed here first (VehicleController.Start adds it, OnDestroy removes it).
            GlobalEvents.onSaveGame -= controller.SavePosition;
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
                    else if (Taken(from, Quaternion.Euler(0f, (float)transit.FromHeading, 0f), kind))
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
                // A vehicle on the cart's spot: it waits, and after a while goes back where it left from.
                if (Taken(position, rotation, kind))
                {
                    var from = new Vector3((float)transit.FromX, (float)transit.FromY, (float)transit.FromZ);
                    var fromRotation = Quaternion.Euler(0f, (float)transit.FromHeading, 0f);
                    if (t - transit.ArrivesAt < 2 * RetryMinutes || Taken(from, fromRotation, kind))
                    {
                        if (t == transit.ArrivesAt) Log.Info($"Route: cart {vehicle.VehicleId}'s spot {transit.ToSpotKey} is taken; it waits.");
                        return false;
                    }
                    position = from;
                    rotation = fromRotation;
                    atStop = false;
                    Log.Info($"Route: cart {vehicle.VehicleId}'s spot {transit.ToSpotKey} stayed taken; it goes back where it left from.");
                }
            }

            var controller = Place(instance, position, rotation);
            if (controller == null)
            {
                Log.Error($"Route: {kind} {vehicle.VehicleId} could not be placed at {position}.");
                return false;
            }
            // A parking space is legal; back on its own old spot, it is what it was.
            instance.parkingState = !atStop && neighbourhood == null ? (ParkingState)transit.FromParkingState : ParkingState.Legal;
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
            // Every lane whose middle is within reach (a lane runs up to about a block, so its middle can be
            // that much further than its nearest slot), nearest first.
            var reach = maxMetres + LaneReachMetres;
            var lanes = laneCache
                .Where(l => l != null && !l.isHandicapParking && !l.preventUseByPrivateDriver && !l.designatedDeliveryVanSpot && l.chanceOfFreeSpot <= 99)
                .Select(l => (Lane: l, Distance: (l.GetWorldCenterPosition() - target).sqrMagnitude))
                .Where(l => l.Distance <= reach * reach)
                .OrderBy(l => l.Distance)
                .Select(l => l.Lane);
            foreach (var lane in lanes)
            {
                if (!lane.TryGetRandomFreeSpotForPlayerVehicle(out var position, out var rotation, 1 << LayerHelper.VehiclesLayerIndex)) continue;
                if (Vector3.Distance(position, target) > maxMetres) continue;
                // The hatch has to face the pavement: a lane's rotation can point either way along the kerb. The
                // game's slot test is sized for a car, so the truck's own hull is checked, the way it will stand.
                if (!HatchFacesPavement(position, rotation)) rotation *= Quaternion.Euler(0f, 180f, 0f);
                if (Taken(position, rotation, "truck")) continue;
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
        private static readonly HashSet<string> recoveryWaitShown = new HashSet<string>();
        private const float LaneReachMetres = 100f;

        public static void Reset()
        {
            lock (later) later.Clear();
            laneCache = null;
            recoveryWaitShown.Clear();
        }

        private static VehicleInstance? Instance(string id) =>
            SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
    }
}
