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
        // A stop with no free parking space nearby is tried again this many game minutes later.
        private const int RetryMinutes = 10;
        // Parking spaces further than this from the stop don't count as the stop.
        private const float MaxParkingMetres = 60f;
        // Steps per vehicle per tick: enough to close, leave and so on after a load or a time skip.
        private const int MaxSteps = 4;

        private static HireRoutes? instance;
        private static readonly List<Action> later = new List<Action>();
        private HireRunner? runner;
        private float nextTick;
        private bool hidAfterLoad;
        // Refusals already told to the player: vehicle, stop, day.
        private readonly HashSet<(string, int, int)> refusalsShown = new HashSet<(string, int, int)>();

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
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            try
            {
                runner ??= new HireRunner(rules.Fleet);
                if (!hidAfterLoad)
                {
                    hidAfterLoad = true;
                    // Unity destroys at the end of the frame: the routes run from the next tick.
                    if (HideInTransit(state)) return;
                }
                HireCrew.Check(state);
                var now = HireCrew.Now();
                foreach (var vehicle in state.Vehicles.Where(v => (v.Crew != null && v.Crew.Count > 0) || v.Transit != null).ToList())
                    Run(vehicle, now);
            }
            catch (Exception ex)
            {
                Log.Error("Hire routes: " + ex);
                enabled = false;
                CartSelling.Notify("foodtrucks:notification_routes_failed");
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

        // The city load spawns every street vehicle where it was saved, those on their way too.
        private static bool HideInTransit(SaveState state)
        {
            var hidden = false;
            foreach (var vehicle in state.Vehicles.Where(v => v.Transit != null))
            {
                var instance = Instance(vehicle.VehicleId);
                var controller = instance == null ? null : VehicleHelper.GetVehicleController(instance);
                if (controller == null) continue;
                Object.Destroy(controller.gameObject);
                hidden = true;
                Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} is on its way; taken off the map after the load.");
            }
            return hidden;
        }

        private void Run(VehicleState vehicle, long now)
        {
            var instance = Instance(vehicle.VehicleId);
            if (instance == null) return;
            var playerHasIt = SaveGameManager.Current.ActiveVehicleId == vehicle.VehicleId;
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            runner!.EnsurePlan(vehicle, now, position.x, position.z);
            for (var i = 0; i < MaxSteps; i++)
            {
                controller = VehicleHelper.GetVehicleController(instance);
                position = controller != null ? controller.transform.position : (Vector3)instance.position;
                var action = runner.Step(vehicle, now, new VehicleWhere(position.x, position.z, playerHasIt));
                if (action.Kind == HireActionKind.None || !Do(vehicle, instance, action, now)) return;
            }
        }

        // Does one step; false when nothing changed, so the loop stops.
        private bool Do(VehicleState vehicle, VehicleInstance instance, HireAction action, long now)
        {
            var kind = string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;
            switch (action.Kind)
            {
                case HireActionKind.Close:
                    CartSelling.CloseByCrew(vehicle);
                    return !vehicle.IsOpen;
                case HireActionKind.Depart:
                    Depart(vehicle, instance, action, now, kind);
                    return true;
                case HireActionKind.Arrive:
                    return Arrive(vehicle, instance, action.Transit!, now, kind);
                case HireActionKind.Open:
                    var refusal = CartSelling.OpenByCrew(vehicle, action.Stop);
                    if (refusal == null)
                    {
                        CartSelling.Say("foodtrucks:notification_hire_open", kind, ("crew", CrewNames(vehicle)));
                        return true;
                    }
                    if (refusalsShown.Add((vehicle.VehicleId, action.Stop, HireRunner.DayOf(now))))
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

        private static void Depart(VehicleState vehicle, VehicleInstance instance, HireAction action, long now, string kind)
        {
            var controller = VehicleHelper.GetVehicleController(instance);
            if (controller != null)
            {
                TruckHatch.Set(controller, false);
                controller.SavePosition();
                Object.Destroy(controller.gameObject);
            }
            // No parking fees or tickets while it's away (ParkingSimulator skips Undefined).
            instance.parkingState = ParkingState.Undefined;
            HireRunner.Depart(vehicle, action, now);
            FoodTrucksSession.MarkChanged();
            var stop = vehicle.Schedule!.Stops[action.Stop];
            Log.Info($"Route: {kind} {vehicle.VehicleId} leaves for stop {action.Stop + 1} ({stop.SpotKey}), arriving at minute {action.ArrivesAt} ({action.ArrivesAt - now} minutes).");
            CartSelling.Say("foodtrucks:notification_hire_departs", kind, ("crew", CrewNames(vehicle)),
                ("time", StaffDialog.Clock((int)(action.ArrivesAt % 1440))));
        }

        private static bool Arrive(VehicleState vehicle, VehicleInstance instance, TransitState transit, long now, string kind)
        {
            var target = new Vector3((float)transit.ToX, (float)transit.ToY, (float)transit.ToZ);
            Vector3 position;
            Quaternion rotation;
            string? neighbourhood = null;
            if (kind == "truck")
            {
                var spot = FreeParking(target);
                if (spot == null)
                {
                    transit.ArrivesAt = now + RetryMinutes;
                    FoodTrucksSession.MarkChanged();
                    Log.Info($"Route: no free parking space within {MaxParkingMetres} m of {transit.ToSpotKey} for truck {vehicle.VehicleId}; trying again in {RetryMinutes} minutes.");
                    return false;
                }
                (position, rotation, neighbourhood) = spot.Value;
            }
            else
            {
                position = target;
                rotation = Quaternion.Euler(0f, (float)transit.ToHeading, 0f);
            }

            var controller = Place(instance, position, rotation);
            if (controller == null)
            {
                transit.ArrivesAt = now + RetryMinutes;
                Log.Error($"Route: {kind} {vehicle.VehicleId} could not be placed at {position}; trying again in {RetryMinutes} minutes.");
                return false;
            }
            // The hatch has to face the pavement: a lane's rotation can point either way along the kerb.
            if (kind == "truck" && !QueueLine.HasWindowPlace(controller.transform, kind))
            {
                VehicleHelper.TeleportVehicle(controller, position, rotation * Quaternion.Euler(0f, 180f, 0f));
                controller.SavePosition();
            }
            instance.parkingState = ParkingState.Legal;
            if (neighbourhood != null) instance.parkingNeighbourhood = neighbourhood;
            vehicle.Transit = null;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: {kind} {vehicle.VehicleId} arrived at {transit.ToSpotKey}: {controller.transform.position}, {Vector3.Distance(controller.transform.position, target):F0} m from the stop.");
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
                controller.SavePosition();
                return controller;
            }
            // CreateAndSpawnVehicle adds the instance to the list again.
            SaveGameManager.Current.VehicleInstances.Remove(instance);
            try
            {
                controller = VehicleHelper.CreateAndSpawnVehicle(instance, position, rotation);
            }
            finally
            {
                if (!SaveGameManager.Current.VehicleInstances.Contains(instance)) SaveGameManager.Current.VehicleInstances.Add(instance);
            }
            if (controller != null) PrefabFixes.ApplyTo(controller);
            return controller;
        }

        // The nearest free parking space to the stop, as the game's private driver finds one: lanes by distance,
        // no disabled or delivery lanes, a random free slot in the lane that no player vehicle blocks.
        private static (Vector3, Quaternion, string?)? FreeParking(Vector3 target)
        {
            var lanes = Object.FindObjectsByType<ParkingLaneGenerator>(FindObjectsSortMode.None)
                .Where(l => l != null && !l.isHandicapParking && !l.preventUseByPrivateDriver && !l.designatedDeliveryVanSpot && l.chanceOfFreeSpot <= 99)
                .OrderBy(l => (l.GetWorldCenterPosition() - target).sqrMagnitude)
                .Take(12);
            foreach (var lane in lanes)
            {
                if (!lane.TryGetRandomFreeSpotForPlayerVehicle(out var position, out var rotation, 1 << LayerHelper.VehiclesLayerIndex)) continue;
                if (Vector3.Distance(position, target) > MaxParkingMetres) continue;
                return (position, rotation, lane.neighbourhood);
            }
            return null;
        }

        private static string CrewNames(VehicleState vehicle) =>
            string.Join(", ", (vehicle.Crew ?? new List<string>()).Select(HireCrew.Find).Where(e => e != null).Select(e => HireCrew.Name(e!)));

        private static VehicleInstance? Instance(string id) =>
            SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
    }
}
