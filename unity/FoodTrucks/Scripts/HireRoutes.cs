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
    internal sealed class HireRoutes : MonoBehaviour
    {
        private const float TickSeconds = 0.25f;
        private const int RetryMinutes = 10;
        private const float MaxParkingMetres = 60f;
        private const float WideParkingMetres = 200f;
        private const int MaxSteps = 4;
        private const int MaxMoments = 512;

        private static HireRoutes? instance;
        private static readonly List<Action> later = new List<Action>();
        private HireRunner? runner;
        private float nextTick;
        private readonly HashSet<(string, int, int)> refusalsShown = new HashSet<(string, int, int)>();
        private readonly Dictionary<string, long> routedUntil = new Dictionary<string, long>();
        private readonly Dictionary<string, ScheduleProblem> routeProblems = new Dictionary<string, ScheduleProblem>();
        private readonly HashSet<string> broken = new HashSet<string>();
        private readonly HashSet<string> indoorsShown = new HashSet<string>();
        private readonly Dictionary<string, long> lastHeld = new Dictionary<string, long>();
        private const long StayPutMinutes = 60;
        private const float StayPutMetres = 15f;
        private bool keptByPlayer;
        private readonly HashSet<string> reallyHeld = new HashSet<string>();
        private readonly HashSet<(string, int)> waitShown = new HashSet<(string, int)>();
        private static readonly Dictionary<string, (Vector3 Position, Quaternion Rotation, HatchCheck.Result Result)> hatches =
            new Dictionary<string, (Vector3, Quaternion, HatchCheck.Result)>();
        private readonly HashSet<(string, int, int)> hatchShown = new HashSet<(string, int, int)>();

        // Whether a truck's hatch faces the pavement where it stands (HatchCheck), read again once it moves. A reading
        // without traffic data (the traffic system not ready yet after a load, say) isn't kept: it is the NavMesh
        // alone, which reads the street as pavement, so it is read again until the lanes are there.
        internal static HatchCheck.Result Hatch(string vehicleId, Transform truck)
        {
            if (hatches.TryGetValue(vehicleId, out var last) && Vector3.Distance(last.Position, truck.position) < 0.3f &&
                Quaternion.Angle(last.Rotation, truck.rotation) < 3f)
                return last.Result;
            var result = HatchCheck.Probe(truck);
            if (result.LanesKnown) hatches[vehicleId] = (truck.position, truck.rotation, result);
            else hatches.Remove(vehicleId);
            return result;
        }

        internal static void PlayerTook(VehicleState vehicle)
        {
            if (vehicle.Arrival == null || !FoodTrucksSession.CanWrite) return;
            vehicle.Arrival = null;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: the player took {vehicle.VehicleType} {vehicle.VehicleId}; where the crew parked it no longer counts as at its stop.");
        }

        public static void Later(Action action)
        {
            lock (later) later.Add(action);
        }

        private void OnEnable() => instance = this;

        internal static long? RoutedThrough() =>
            instance == null ? null : instance.routedUntil.Where(p => !instance.broken.Contains(p.Key)).Select(p => (long?)p.Value).Min();

        private void OnDisable()
        {
            if (instance == this) instance = null;
        }

        internal static long? RoutedUntil(string vehicleId) =>
            instance != null && !instance.broken.Contains(vehicleId) && instance.routedUntil.TryGetValue(vehicleId, out var until) ? until : (long?)null;

        internal static void CatchUpTo(long boundary)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (self == null || state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            self.runner ??= new HireRunner(rules.Fleet);
            foreach (var vehicle in state.Vehicles.ToList())
            {
                if (self.broken.Contains(vehicle.VehicleId) || !self.routedUntil.TryGetValue(vehicle.VehicleId, out var from) || from >= boundary) continue;
                try { self.RunTo(vehicle, from, boundary); }
                catch (Exception ex)
                {
                    self.broken.Add(vehicle.VehicleId);
                    Log.Error($"Hire routes: {vehicle.VehicleType} {vehicle.VehicleId} stopped at midnight: {ex}");
                    CartSelling.Notify("foodtrucks:notification_routes_failed");
                    try { if (vehicle.HireStop != null) CartSelling.CloseByCrew(vehicle, from); }
                    catch (Exception closeEx) { Log.Error("Hire routes, closing: " + closeEx); }
                }
            }
        }

        private void RunTo(VehicleState vehicle, long from, long boundary)
        {
            var instance = Instance(vehicle.VehicleId);
            if (instance == null) return;
            var t = from;
            for (var guard = 0; guard < MaxMoments; guard++)
            {
                StepAt(vehicle, instance, t);
                var next = NextMoment(vehicle, t, boundary);
                if (next >= boundary) break;
                t = next;
            }
            CartSelling.AdvanceTo(vehicle, boundary);
            routedUntil[vehicle.VehicleId] = boundary;
            Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} caught up to minute {boundary} for the day's close.");
        }

        private void Update()
        {
            RunLater();
            if (Time.unscaledTime < nextTick && !CartSelling.Skipping) return;
            nextTick = Time.unscaledTime + TickSeconds;
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            try
            {
                runner ??= new HireRunner(rules.Fleet);
                HideInTransit(state);
                HireCrew.Check(state);
            }
            catch (Exception ex)
            {
                Log.Error("Hire routes: " + ex);
                return;
            }
            var now = HireCrew.Now();
            var running = state.Vehicles.Where(v => (v.Crew != null && v.Crew.Count > 0) || v.Transit != null || v.HireStop != null || v.CrewWindowLost).ToList();
            foreach (var id in routedUntil.Keys.Where(id => running.All(v => v.VehicleId != id)).ToList()) routedUntil.Remove(id);
            foreach (var vehicle in running)
            {
                if (broken.Contains(vehicle.VehicleId))
                {
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
        private bool loadHidden;
        private static readonly Vector3 TruckHalfSize = new Vector3(1.3f, 1.55f, 2.95f);
        private static readonly Vector3 TruckCentre = new Vector3(0f, 1.55f, -0.25f);
        private static readonly Vector3 CartHalfSize = new Vector3(0.5f, 0.5f, 0.75f);
        private static readonly Vector3 CartCentre = new Vector3(0f, 0.55f, 0f);

        private void Run(VehicleState vehicle, long now)
        {
            if (CrewDriveProbe.Owns(vehicle.VehicleId)) return;
            var instance = Instance(vehicle.VehicleId);
            if (instance == null)
            {
                routedUntil.Remove(vehicle.VehicleId);
                return;
            }
            if (!routedUntil.TryGetValue(vehicle.VehicleId, out var from) || from > now) from = now;
            var t = from;
            for (var guard = 0; guard < MaxMoments; guard++)
            {
                StepAt(vehicle, instance, t);
                if (t >= now) break;
                t = NextMoment(vehicle, t, now);
            }
            if (t < now)
            {
                routedUntil[vehicle.VehicleId] = t;
                return;
            }
            routedUntil[vehicle.VehicleId] = now;
            CartSelling.AdvanceTo(vehicle, HireCrew.NowExact());
        }

        private static long NextMoment(VehicleState vehicle, long t, long now)
        {
            var next = now;
            foreach (var plan in vehicle.Shifts ?? new List<ShiftPlan>())
                foreach (var leg in plan.Legs)
                {
                    if (leg.Start > t && leg.Start < next) next = leg.Start;
                    if (leg.End > t && leg.End < next) next = leg.End;
                }
            if (vehicle.Transit != null)
                foreach (var due in new[] { vehicle.Transit.ArrivesAt, vehicle.Transit.ArrivesAt + RetryMinutes, vehicle.Transit.ArrivesAt + 2 * RetryMinutes })
                    if (due > t && due < next) next = due;
            var midnight = (t / 1440 + 1) * 1440;
            if (midnight > t && midnight < next) next = midnight;
            var hour = (t / 60 + 1) * 60;
            if (vehicle.Crew != null && vehicle.Crew.Count > 0 && hour > t && hour < next) next = hour;
            return next;
        }

        private void StepAt(VehicleState vehicle, VehicleInstance instance, long t)
        {
            CartSelling.AdvanceTo(vehicle, t);
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var (planX, planZ) = HireRunner.PlanFrom(vehicle, position.x, position.z);
            RouteStops.FitIfNeeded(vehicle, "planning");
            var home = HomeOf(vehicle);
            var problem = runner!.RouteProblem(vehicle, GameSpeed(), HomePoint(home), home?.BayStops);
            if (problem == ScheduleProblem.None) routeProblems.Remove(vehicle.VehicleId);
            else if (!routeProblems.TryGetValue(vehicle.VehicleId, out var known) || known != problem)
            {
                routeProblems[vehicle.VehicleId] = problem;
                Log.Info(problem == ScheduleProblem.Unreachable
                    ? $"Route: {vehicle.VehicleType} {vehicle.VehicleId}: a stop opens before the drive from the one before can end; it opens late, on arrival."
                    : problem == ScheduleProblem.Uncovered
                        ? $"Route: {vehicle.VehicleType} {vehicle.VehicleId}: part of a route day has nobody on shift; the vehicle waits closed where it is until a shift starts."
                        : $"Route: {vehicle.VehicleType} {vehicle.VehicleId} has no plan: its route fails the rules ({problem}); the crew waits until the route is changed on the BizMan page.");
            }
            runner.EnsurePlan(vehicle, t, planX, planZ, GameSpeed(), HomePoint(home), home?.BayStops);
            for (var i = 0; i < MaxSteps; i++)
            {
                var where = Where(vehicle, instance, t, home);
                var action = runner.Step(vehicle, t, where, GameSpeed());
                if (action.Kind == HireActionKind.None)
                {
                    if (keptByPlayer && runner.WouldMove(vehicle, t, where.WithStayPut(false), GameSpeed())) SayWaiting(vehicle, t);
                    return;
                }
                if (!Do(vehicle, instance, action, t)) return;
            }
        }

        private void SayWaiting(VehicleState vehicle, long t)
        {
            if (CartSelling.Skipping || !waitShown.Add((vehicle.VehicleId, HireRunner.DayOf(t)))) return;
            var kind = KindOf(vehicle);
            Log.Info($"Route: {kind} {vehicle.VehicleId}: the crew would set off but waits, the player is within {StayPutMetres:F0} m or used it in the last {StayPutMinutes} minutes.");
            var name = StreetFoodBooks.VehicleNames().TryGetValue(vehicle.VehicleId, out var n) ? n : LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null);
            CartSelling.Say("foodtrucks:notification_hire_waits_for_player", kind, ("crew", CrewNames(vehicle)), ("name", name));
        }

        private static void SayStopTaken(VehicleState vehicle, TransitState transit, string kind, string key)
        {
            if (CartSelling.Skipping) return;
            var name = StreetFoodBooks.VehicleNames().TryGetValue(vehicle.VehicleId, out var n) ? n : LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null);
            var stops = vehicle.Schedule?.Stops;
            var stop = transit.ToStopIndex is int index && stops != null && index >= 0 && index < stops.Count ? RouteStops.Label(stops[index]) : transit.ToSpotKey;
            CartSelling.Say(key, kind, ("name", name), ("stop", stop));
        }

        private VehicleWhere Where(VehicleState vehicle, VehicleInstance instance, long t, (ScheduleStop Place, string SiteId, IReadOnlyCollection<int> BayStops)? home)
        {
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var held = SaveGameManager.Current.ActiveVehicleId == vehicle.VehicleId;
            var indoors = !string.IsNullOrEmpty(instance.streetName);
            if (indoors && indoorsShown.Add(vehicle.VehicleId))
            {
                Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} is in {instance.streetName} {instance.streetNumber}; the crew waits for it on the street.");
                CartSelling.Say("foodtrucks:notification_hire_indoors", KindOf(vehicle));
            }
            if (!indoors) indoorsShown.Remove(vehicle.VehicleId);
            if (held || !lastHeld.ContainsKey(vehicle.VehicleId)) lastHeld[vehicle.VehicleId] = t;
            if (held) reallyHeld.Add(vehicle.VehicleId);
            if (held) PlayerTook(vehicle);
            keptByPlayer = false;
            var canSell = true;
            var hasStock = true;
            var stayPut = false;
            string? restockAt = null;
            string? stockKey = null;
            IReadOnlyCollection<int>? bayStops = null;
            if (!held && !indoors && !vehicle.IsOpen && vehicle.Transit == null && (HireRunner.Want(vehicle, t) != null || vehicle.ReturnPending))
            {
                canSell = KindOf(vehicle) == "truck" ? instance.parkingState == ParkingState.Legal : Legality.Check(position).Legal;
                if (canSell && KindOf(vehicle) == "truck" && controller != null)
                {
                    var hatch = Hatch(vehicle.VehicleId, controller.transform);
                    if (hatch.Verdict == HatchVerdict.WrongWay)
                    {
                        canSell = false;
                        var want = HireRunner.Want(vehicle, t);
                        if (want != null && hatchShown.Add((vehicle.VehicleId, want.StopIndex, HireRunner.DayOf(t))))
                            Log.Info($"Route: truck {vehicle.VehicleId} at {position} stands with its hatch to the road for stop {want.StopIndex + 1} " +
                                     $"({hatch}); the crew parks it again unless it's theirs to leave.");
                    }
                }
                var tables = DemandData.Tables;
                hasStock = tables == null || CartDemand.StockOf(instance, CartDemand.MenuOf(tables, instance).Menu).Count > 0;
                var recentlyHeld = lastHeld.TryGetValue(vehicle.VehicleId, out var heldAt) && t - heldAt < StayPutMinutes;
                var near = !BuildingManager.IsInsideBuilding && Vector3.Distance(PlayerHelper.GetPosition(), position) <= StayPutMetres;
                var nobodyOnShift = !HireCrew.Present(vehicle).Any(e => WeekPlan.OnShift(vehicle.Schedule, e.id, t));
                stayPut = recentlyHeld || near || nobodyOnShift;
                keptByPlayer = (near || (recentlyHeld && reallyHeld.Contains(vehicle.VehicleId))) && !nobodyOnShift;
                restockAt = Commissary.CrewRestockSite(FoodTrucksSession.CurrentState!, instance);
                if (restockAt != null) stockKey = Commissary.CrewStockKey(FoodTrucksSession.CurrentState!, instance, KindOf(vehicle), restockAt);
                bayStops = Commissary.CrewBayStops(FoodTrucksSession.CurrentState!, vehicle);
            }
            restockSites[vehicle.VehicleId] = restockAt;
            return new VehicleWhere(position.x, position.z, held || indoors || Catering.BusyAt(vehicle.VehicleId, t), canSell, hasStock, stayPut,
                canRestock: restockAt != null, stockKey: stockKey, bayStops: bayStops, home: HomePoint(home),
                canServe: HireCrew.OnShift(vehicle, t).Count > 0);
        }

        internal static (ScheduleStop Place, string SiteId, IReadOnlyCollection<int> BayStops)? HomeOf(VehicleState vehicle)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || Commissary.Uninstalled) return null;
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.VehicleId);
            var site = space == null ? null : DemandData.Commissaries?.Site(space.CommissaryId);
            if (site == null) return null;
            var places = Commissary.BayPlaces(site, KindOf(vehicle) == "truck" ? TruckHalfSize.z : CartHalfSize.z);
            if (places.Count == 0)
            {
                if (homeMissing.Add(vehicle.VehicleId))
                    Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId}: the bay of {site.Id} has no places yet; its day ends at its last stop until it has.");
                return null;
            }
            if (homeMissing.Remove(vehicle.VehicleId)) Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} drives back to the bay of {site.Id} after its last stop.");
            var (position, rotation) = places[0];
            var bayStops = Commissary.CrewBayStops(state, vehicle);
            var place = new ScheduleStop
            {
                SpotKey = HireSchedules.HomeSpotKey, X = position.x, Y = position.y, Z = position.z, Heading = rotation.eulerAngles.y
            };
            return (place, site.Id, bayStops);
        }

        internal static (double X, double Z)? HomePoint((ScheduleStop Place, string SiteId, IReadOnlyCollection<int> BayStops)? home) =>
            home == null ? ((double, double)?)null : (home.Value.Place.X, home.Value.Place.Z);

        private static readonly HashSet<string> homeMissing = new HashSet<string>();

        private readonly Dictionary<string, string?> restockSites = new Dictionary<string, string?>();

        private bool Do(VehicleState vehicle, VehicleInstance instance, HireAction action, long t)
        {
            var kind = KindOf(vehicle);
            switch (action.Kind)
            {
                case HireActionKind.Close:
                    CartSelling.CloseByCrew(vehicle, t);
                    return !vehicle.IsOpen;
                case HireActionKind.Depart:
                    if (action.Stop == HireSchedules.HomeIndex && HomeOf(vehicle) == null) return false;
                    Depart(vehicle, instance, action, t, kind);
                    return true;
                case HireActionKind.Arrive:
                    return Arrive(vehicle, instance, action.Transit!, t, kind);
                case HireActionKind.Restock:
                {
                    var state = FoodTrucksSession.CurrentState!;
                    restockSites.TryGetValue(vehicle.VehicleId, out var site);
                    HireRunner.Restocked(vehicle, t, site == null ? null : Commissary.CrewStockKey(state, instance, kind, site));
                    FoodTrucksSession.MarkChanged();
                    if (site == null) return true;
                    Commissary.CrewRestock(state, vehicle, instance, kind, site, t);
                    HireRunner.Restocked(vehicle, t, Commissary.CrewStockKey(state, instance, kind, site));
                    return true;
                }
                case HireActionKind.Open:
                    var refusal = CartSelling.OpenByCrew(vehicle, action.Stop, t);
                    if (refusal == null)
                    {
                        Log.Info($"Route: {kind} {vehicle.VehicleId}: the crew opened at stop {action.Stop + 1}.");
                        if (!CartSelling.Skipping && CartSelling.PlayerNear(instance))
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
            if (CrewDriveProbe.Owns(vehicle.VehicleId)) return;
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
            instance.parkingState = ParkingState.Undefined;
            var home = action.Stop == HireSchedules.HomeIndex ? HomeOf(vehicle) : null;
            HireRunner.Depart(vehicle, action, t, position.x, position.y, position.z, heading, home?.Place);
            vehicle.Transit!.FromParkingState = (int)parking;
            FoodTrucksSession.MarkChanged();
            var where = home != null ? $"its bay at {home.Value.SiteId}" : $"stop {action.Stop + 1} ({vehicle.Schedule!.Stops[action.Stop].SpotKey})";
            Log.Info($"Route: {kind} {vehicle.VehicleId} leaves for {where}, arriving at minute {action.ArrivesAt} ({action.ArrivesAt - t} minutes).");
        }

        private static void Recover(VehicleState vehicle)
        {
            if (CrewDriveProbe.Owns(vehicle.VehicleId)) return;
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
                    var to = new Vector3((float)transit.ToX, (float)transit.ToY, (float)transit.ToZ);
                    var toRotation = Quaternion.Euler(0f, (float)transit.ToHeading, 0f);
                    if (Taken(to, toRotation, kind))
                    {
                        if (recoveryWaitShown.Add(vehicle.VehicleId))
                            Log.Info($"Route: {kind} {vehicle.VehicleId} waits off the map: both where it left from and its stop are taken.");
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

        private static bool Taken(Vector3 position, Quaternion rotation, string kind, VehicleInstance? self = null) =>
            Occupied(position, rotation, kind, self, 1 << LayerHelper.VehiclesLayerIndex, QueryTriggerInteraction.UseGlobal);

        private static bool BayTaken(Vector3 position, Quaternion rotation, string kind, VehicleInstance self) =>
            Occupied(position, rotation, kind, self,
                (1 << LayerHelper.VehiclesLayerIndex) | (1 << LayerHelper.ParkedVehiclesLayerIndex) | (1 << LayerHelper.AiVehiclesLayerIndex) |
                (1 << LayerHelper.PlayerVehiclesLayerIndex) | (1 << LayerHelper.PlayerLayerIndex), QueryTriggerInteraction.Collide);

        private static bool Occupied(Vector3 position, Quaternion rotation, string kind, VehicleInstance? self, int mask, QueryTriggerInteraction triggers)
        {
            var centre = position + rotation * (kind == "truck" ? TruckCentre : CartCentre);
            var half = kind == "truck" ? TruckHalfSize : CartHalfSize;
            var own = self == null ? null : VehicleHelper.GetVehicleController(self)?.transform;
            foreach (var hit in Physics.OverlapBox(centre, half, rotation, mask, triggers))
                if (hit != null && (own == null || !hit.transform.IsChildOf(own))) return true;
            var local = Quaternion.Inverse(rotation) * (PlayerHelper.GetPosition() - centre);
            return Mathf.Abs(local.x) <= half.x + 0.3f && Mathf.Abs(local.z) <= half.z + 0.3f && Mathf.Abs(local.y) <= half.y + 1f;
        }

        private static bool HatchRefused(Vector3 position, Quaternion rotation, TrafficLanes lanes, out HatchCheck.Result result)
        {
            result = HatchCheck.Probe(position, rotation, lanes);
            return !result.Opens;
        }

        private static void TakeOffMap(VehicleController controller)
        {
            if (CrewDriveProbe.Owns(controller)) return;
            try
            {
                if (controller.vehicleCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.vehicleCollider);
                if (controller.additionalCollider != null) GleyTrafficSystem.Manager.TriggerColliderRemovedEvent(controller.additionalCollider);
            }
            catch (Exception ex) { Log.Error($"Route: collider event for {controller.vehicleInstance?.id}: {ex}"); }
            try { VehicleHelper.onVehicleDestroyed?.Invoke(controller); }
            catch (Exception ex) { Log.Error($"Route: vehicle destroyed event for {controller.vehicleInstance?.id}: {ex}"); }
            // Its save hook would write its old pose over the instance until OnDestroy runs at the frame's end;
            // the same delegate is removed here first (VehicleController.Start adds it, OnDestroy removes it).
            GlobalEvents.onSaveGame -= controller.SavePosition;
            controller.gameObject.SetActive(false);
            Object.Destroy(controller.gameObject);
        }

        private static bool Arrive(VehicleState vehicle, VehicleInstance instance, TransitState transit, long t, string kind)
        {
            var target = new Vector3((float)transit.ToX, (float)transit.ToY, (float)transit.ToZ);
            Vector3 position;
            Quaternion rotation;
            string? neighbourhood = null;
            var atStop = true;
            var bayArrival = BayArrival(vehicle, instance, transit, t, kind, out var bayFallback);
            if (bayArrival != null) return bayArrival.Value;
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
                    var from = new Vector3((float)transit.FromX, (float)transit.FromY, (float)transit.FromZ);
                    var back = FreeParking(from, WideParkingMetres);
                    if (back != null) (position, rotation, neighbourhood) = back.Value;
                    else if (Taken(from, Quaternion.Euler(0f, (float)transit.FromHeading, 0f), kind))
                    {
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
                    SayStopTaken(vehicle, transit, kind, "foodtrucks:notification_hire_stop_no_parking");
                }
            }
            else
            {
                position = target;
                rotation = Quaternion.Euler(0f, (float)transit.ToHeading, 0f);
                if (Taken(position, rotation, kind))
                {
                    var from = new Vector3((float)transit.FromX, (float)transit.FromY, (float)transit.FromZ);
                    var fromRotation = Quaternion.Euler(0f, (float)transit.FromHeading, 0f);
                    if (t - transit.ArrivesAt < 2 * RetryMinutes || Taken(from, fromRotation, kind))
                    {
                        if (t == transit.ArrivesAt) Log.Info($"Route: {kind} {vehicle.VehicleId}'s spot {transit.ToSpotKey} is taken; it waits.");
                        return false;
                    }
                    position = from;
                    rotation = fromRotation;
                    atStop = false;
                    Log.Info($"Route: {kind} {vehicle.VehicleId}'s spot {transit.ToSpotKey} stayed taken; it goes back where it left from.");
                    SayStopTaken(vehicle, transit, kind, "foodtrucks:notification_hire_stop_taken");
                }
            }

            var controller = Place(instance, position, rotation);
            if (controller == null)
            {
                Log.Error($"Route: {kind} {vehicle.VehicleId} could not be placed at {position}.");
                return false;
            }
            instance.parkingState = !atStop && neighbourhood == null ? (ParkingState)transit.FromParkingState : ParkingState.Legal;
            if (neighbourhood != null) instance.parkingNeighbourhood = neighbourhood;
            var placed = controller.transform.position;
            if (bayFallback) atStop = false;
            HireRunner.Arrived(vehicle, placed.x, placed.z, atStop, t);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: {kind} {vehicle.VehicleId} arrived {(atStop ? "at" : "back from")} {transit.ToSpotKey}: {placed}, {Vector3.Distance(placed, target):F0} m from the stop.");
            return true;
        }

        private static bool? BayArrival(VehicleState vehicle, VehicleInstance instance, TransitState transit, long t, string kind, out bool fallback)
        {
            fallback = false;
            var state = FoodTrucksSession.CurrentState;
            var index = transit.ToStopIndex;
            var home = transit.ToSpotKey == HireSchedules.HomeSpotKey;
            if (state == null || (!home && (index == null || !Commissary.CrewBayStops(state, vehicle).Contains(index.Value)))) return null;
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.VehicleId);
            var site = space == null ? null : DemandData.Commissaries?.Site(space.CommissaryId);
            if (site == null) return null;
            var late = t - transit.ArrivesAt;
            var key = (vehicle.VehicleId, transit.ArrivesAt);
            if (bayTried.TryGetValue(vehicle.VehicleId, out var tried) && tried == t) return false;
            bayTried[vehicle.VehicleId] = t;
            var ready = Commissary.BayReady(site, out var notReady);
            var places = ready ? Commissary.BayPlaces(site, kind == "truck" ? TruckHalfSize.z : CartHalfSize.z) : new List<(Vector3, Quaternion)>();
            bayCovered.TryGetValue(key, out var covered);
            if (late >= 2 * RetryMinutes && covered >= places.Count)
            {
                fallback = true;
                if (bayGaveUp.Add(key))
                    Log.Info($"Route: {kind} {vehicle.VehicleId} found no place in the bay of {site.Id} for {RetryMinutes * 2} minutes; it parks nearby, not at the stop, and waits for its next bay stop.");
                return null;
            }
            if (!ready)
            {
                if (late == 0) Log.Info($"Route: the bay of {site.Id} can't take {kind} {vehicle.VehicleId} yet ({notReady}); it waits on its way.");
                return false;
            }
            var minutesLeft = 2 * RetryMinutes - late;
            var left = places.Count - covered;
            var take = minutesLeft <= 1 ? left : Math.Max(PlacesPerAttempt, (int)Math.Ceiling(left / (double)minutesLeft));
            bayCursor.TryGetValue(vehicle.VehicleId, out var cursor);
            if (cursor >= places.Count) cursor = 0;
            var batch = places.Skip(cursor).Take(take).ToList();
            if (batch.Count < take) batch.AddRange(places.Take(Math.Min(cursor, take - batch.Count)));
            bayCursor[vehicle.VehicleId] = (cursor + batch.Count) % Math.Max(1, places.Count);
            bayCovered[key] = covered + batch.Count;
            var placedAny = false;
            foreach (var (position, rotation) in batch)
            {
                if (BayTaken(position, rotation, kind, instance)) continue;
                var placed = Place(instance, position, rotation, noCleanup: true);
                if (placed == null) continue;
                placedAny = true;
                instance.parkingState = ParkingState.Legal;
                if (!Commissary.InBay(instance, site, out var why))
                {
                    if (late == 0) Log.Info($"Route: {kind} {vehicle.VehicleId} at {position} isn't in the bay of {site.Id} ({why}); trying the next place.");
                    continue;
                }
                HireRunner.Arrived(vehicle, placed.transform.position.x, placed.transform.position.z, atStop: true, t);
                bayTried.Remove(vehicle.VehicleId);
                bayCursor.Remove(vehicle.VehicleId);
                bayCovered.Remove(key);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Route: {kind} {vehicle.VehicleId} arrived in the bay of {site.Id}: {placed.transform.position}.");
                return true;
            }
            if (placedAny && VehicleHelper.GetVehicleController(instance) is { } shown) TakeOffMap(shown);
            if (late == 0) Log.Info($"Route: no free place in the bay of {site.Id} for {kind} {vehicle.VehicleId}; it waits on its way and tries each minute.");
            return false;
        }

        private static readonly Dictionary<(string, long), int> bayCovered = new Dictionary<(string, long), int>();
        private static readonly Dictionary<string, long> bayTried = new Dictionary<string, long>();
        private static readonly Dictionary<string, int> bayCursor = new Dictionary<string, int>();
        private const int PlacesPerAttempt = 8;
        private static readonly HashSet<(string, long)> bayGaveUp = new HashSet<(string, long)>();

        internal static bool SpawnNoCleanup;

        private static VehicleController? Place(VehicleInstance instance, Vector3 position, Quaternion rotation, bool noCleanup = false)
        {
            var placed = PlaceUnsynced(instance, position, rotation, noCleanup);
            Physics.SyncTransforms();
            return placed;
        }

        private static VehicleController? PlaceUnsynced(VehicleInstance instance, Vector3 position, Quaternion rotation, bool noCleanup)
        {
            var controller = VehicleHelper.GetVehicleController(instance);
            if (controller != null)
            {
                VehicleHelper.TeleportVehicle(controller, position, rotation);
                controller.transform.SetPositionAndRotation(position, rotation);
                controller.SavePosition();
                return controller;
            }
            var list = SaveGameManager.Current.VehicleInstances;
            var index = list.IndexOf(instance);
            list.Remove(instance);
            try
            {
                SpawnNoCleanup = noCleanup;
                controller = VehicleHelper.CreateAndSpawnVehicle(instance, position, rotation);
            }
            finally
            {
                SpawnNoCleanup = false;
                list.Remove(instance);
                list.Insert(index >= 0 && index <= list.Count ? index : list.Count, instance);
            }
            if (controller != null) PrefabFixes.ApplyTo(controller);
            return controller;
        }

        internal static (Vector3, Quaternion, string?)? FreeParkingNear(Vector3 target, float maxMetres, VehicleInstance self) => FreeParking(target, maxMetres, self);

        private static (Vector3, Quaternion, string?)? FreeParking(Vector3 target, float maxMetres, VehicleInstance? self = null)
        {
            if (laneCache == null || laneCache.Any(l => l == null))
                laneCache = Object.FindObjectsByType<ParkingLaneGenerator>(FindObjectsSortMode.None);
            var reach = maxMetres + LaneReachMetres;
            var lanes = laneCache
                .Where(l => l != null && !l.isHandicapParking && !l.preventUseByPrivateDriver && !l.designatedDeliveryVanSpot && l.chanceOfFreeSpot <= 99)
                .Select(l => (Lane: l, Distance: (l.GetWorldCenterPosition() - target).sqrMagnitude))
                .Where(l => l.Distance <= reach * reach)
                .OrderBy(l => l.Distance)
                .Select(l => l.Lane);
            (Vector3, Quaternion, string?)? refused = null;
            TrafficLanes? traffic = null;
            foreach (var lane in lanes)
            {
                if (!lane.TryGetRandomFreeSpotForPlayerVehicle(out var position, out var rotation, 1 << LayerHelper.VehiclesLayerIndex)) continue;
                if (Vector3.Distance(position, target) > maxMetres) continue;
                traffic ??= TrafficLanes.Read(target, (float)LaneReach.CoverRadius(maxMetres, HatchCheck.LaneSearchMetres));
                if (HatchRefused(position, rotation, traffic, out var hatch))
                {
                    var turned = rotation * Quaternion.Euler(0f, 180f, 0f);
                    if (HatchRefused(position, turned, traffic, out var turnedHatch))
                    {
                        Log.Info($"Route: the space at {position} has road by the hatch either way round ({hatch}; turned: {turnedHatch}); trying the next lane.");
                        if (refused == null && !Taken(position, rotation, "truck", self)) refused = (position, rotation, lane.neighbourhood);
                        continue;
                    }
                    rotation = turned;
                    Log.Info($"Route: the space at {position} would put the hatch to the road ({hatch}); the truck takes it the other way round.");
                }
                if (Taken(position, rotation, "truck", self)) continue;
                return (position, rotation, lane.neighbourhood);
            }
            if (refused != null) Log.Info($"Route: no space near {target} opens; the truck takes the one at {refused.Value.Item1} and the crew says why it can't open.");
            return refused;
        }

        private static string CrewNames(VehicleState vehicle) =>
            string.Join(", ", (vehicle.Crew ?? new List<string>()).Select(HireCrew.Find).Where(e => e != null).Select(e => HireCrew.Name(e!)));

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
            hatches.Clear();
        }

        private static VehicleInstance? Instance(string id) =>
            SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
    }
}
