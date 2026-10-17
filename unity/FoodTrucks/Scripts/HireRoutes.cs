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
        // Moments stepped per vehicle per tick (Run).
        private const int MaxMoments = 512;

        private static HireRoutes? instance;
        private static readonly List<Action> later = new List<Action>();
        private HireRunner? runner;
        private float nextTick;
        // Refusals already told to the player: vehicle, stop, day.
        private readonly HashSet<(string, int, int)> refusalsShown = new HashSet<(string, int, int)>();
        // The game minute each vehicle's route has been run up to. Not saved: after a load it starts now.
        private readonly Dictionary<string, long> routedUntil = new Dictionary<string, long>();
        // Vehicles whose route fails the rules, logged once per problem.
        private readonly Dictionary<string, ScheduleProblem> routeProblems = new Dictionary<string, ScheduleProblem>();
        private readonly HashSet<string> broken = new HashSet<string>();
        private readonly HashSet<string> indoorsShown = new HashSet<string>();
        // The game minute the player last pushed or drove each vehicle.
        private readonly Dictionary<string, long> lastHeld = new Dictionary<string, long>();
        // The crew doesn't drive a vehicle away within this long of the player using it, or with the player this close.
        private const long StayPutMinutes = 60;
        private const float StayPutMetres = 15f;
        // Set by Where: the crew is on shift and would go, but the player is by the vehicle or used it within the hour.
        private bool keptByPlayer;
        // Vehicles the player has really held since the load (lastHeld also starts at the load for every vehicle).
        private readonly HashSet<string> reallyHeld = new HashSet<string>();
        // Vehicles whose crew already said today that they wait for the player to step away: vehicle, day.
        private readonly HashSet<(string, int)> waitShown = new HashSet<(string, int)>();
        // Each truck's last hatch reading, kept while it stands still: the route asks every game minute.
        private static readonly Dictionary<string, (Vector3 Position, Quaternion Rotation, HatchCheck.Result Result)> hatches =
            new Dictionary<string, (Vector3, Quaternion, HatchCheck.Result)>();
        // Trucks already logged as standing the wrong way for a stop: vehicle, stop, day.
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

        // The player has the vehicle (got in, or grabbed the cart): where the crew parked it no longer counts as the
        // crew's placement (HireRunner.ParkedFor), even if it comes back within a few metres, turned round. Only an
        // untouched crew placement keeps that pass. Called on the game's onEnterVehicle, whatever crew or route it has.
        internal static void PlayerTook(VehicleState vehicle)
        {
            if (vehicle.Arrival == null || !FoodTrucksSession.CanWrite) return;
            vehicle.Arrival = null;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: the player took {vehicle.VehicleType} {vehicle.VehicleId}; where the crew parked it no longer counts as at its stop.");
        }

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

        // How far this vehicle's route has run, null when it isn't being run (no crew, stopped, or not started).
        internal static long? RoutedUntil(string vehicleId) =>
            instance != null && !instance.broken.Contains(vehicleId) && instance.routedUntil.TryGetValue(vehicleId, out var until) ? until : (long?)null;

        // Inside the game's midnight processing (CartSelling.CatchUpDay): every running route steps up to `boundary`
        // (midnight) the way its ticks would have, its window selling up to there, so the closing day's crew sales are
        // all made before the day is finalised. The next tick goes on from the boundary.
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

        // Run's steps from `from` up to, not including, `boundary`; the window sells up to the boundary.
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
            var running = state.Vehicles.Where(v => (v.Crew != null && v.Crew.Count > 0) || v.Transit != null || v.HireStop != null || v.CrewWindowLost).ToList();
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
        // y 0…3.10, z −3.15…2.65; the cart's body x ±0.48, y 0…0.95, z ±0.70.
        private static readonly Vector3 TruckHalfSize = new Vector3(1.3f, 1.55f, 2.95f);
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
            // A long skip has many moments (legs, the hours, arrivals): up to MaxMoments a tick; the rest go on next tick
            // from the last one handled, so no moment is skipped and the window sells only up to it.
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
            // Shifts start and end on the hour: an owed drive back waits for one, an open window closes at one's end.
            var hour = (t / 60 + 1) * 60;
            if (vehicle.Crew != null && vehicle.Crew.Count > 0 && hour > t && hour < next) next = hour;
            return next;
        }

        private void StepAt(VehicleState vehicle, VehicleInstance instance, long t)
        {
            CartSelling.AdvanceTo(vehicle, t);
            var controller = VehicleHelper.GetVehicleController(instance);
            // The same starting point as the payroll's preview (HireRunner.PlanFrom).
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var (planX, planZ) = HireRunner.PlanFrom(vehicle, position.x, position.z);
            // A route saved before the travel rule (or with drives longer at today's game speed) is fitted first.
            RouteStops.FitIfNeeded(vehicle, "planning");
            // A route that fails today's rules (saved under older ones) gets no plan: said once in the log. One with a stop
            // it can't reach in time still runs, that stop opening late.
            // The day ends back in the bay where it rents its space (Peter, 16 October 2026).
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
                // Read after planning: the checks below depend on the leg the plan has running.
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

        // A crew on shift that would set off or restock but stays put because of the player (the first blind playtest,
        // 17 October 2026: crews sat in the bay for hours with no word why). Said, and logged, once per vehicle and game
        // day, never while the game sleeps or skips time.
        private void SayWaiting(VehicleState vehicle, long t)
        {
            if (CartSelling.Skipping || !waitShown.Add((vehicle.VehicleId, HireRunner.DayOf(t)))) return;
            var kind = KindOf(vehicle);
            Log.Info($"Route: {kind} {vehicle.VehicleId}: the crew would set off but waits, the player is within {StayPutMetres:F0} m or used it in the last {StayPutMinutes} minutes.");
            var name = StreetFoodBooks.VehicleNames().TryGetValue(vehicle.VehicleId, out var n) ? n : LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null);
            CartSelling.Say("foodtrucks:notification_hire_waits_for_player", kind, ("crew", CrewNames(vehicle)), ("name", name));
        }

        // A crew that turns back from a stop it couldn't take says so (blind run 2, I21: a cart stood closed for three
        // hours with no word why). Never while the game sleeps or skips time.
        private static void SayStopTaken(VehicleState vehicle, TransitState transit, string kind, string key)
        {
            if (CartSelling.Skipping) return;
            var name = StreetFoodBooks.VehicleNames().TryGetValue(vehicle.VehicleId, out var n) ? n : LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null);
            var stops = vehicle.Schedule?.Stops;
            var stop = transit.ToStopIndex is int index && stops != null && index >= 0 && index < stops.Count ? RouteStops.Label(stops[index]) : transit.ToSpotKey;
            CartSelling.Say(key, kind, ("name", name), ("stop", stop));
        }

        // Where the vehicle stands, and whether the crew may touch it or sell there.
        private VehicleWhere Where(VehicleState vehicle, VehicleInstance instance, long t, (ScheduleStop Place, string SiteId, IReadOnlyCollection<int> BayStops)? home)
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
            // After a load, nobody knows when the player last used it: as if just now, so nothing drives off at once.
            if (held || !lastHeld.ContainsKey(vehicle.VehicleId)) lastHeld[vehicle.VehicleId] = t;
            if (held) reallyHeld.Add(vehicle.VehicleId);
            // The game's enter event clears the crew's arrival (PlayerTook, from CartSelling); this catches a vehicle the
            // player already had when the save loaded.
            if (held) PlayerTook(vehicle);
            keptByPlayer = false;
            var canSell = true;
            var hasStock = true;
            var stayPut = false;
            string? restockAt = null;
            string? stockKey = null;
            IReadOnlyCollection<int>? bayStops = null;
            // A leg running, or the drive back owed (VehicleState.ReturnPending): the checks the crew's moves need.
            if (!held && !indoors && !vehicle.IsOpen && vehicle.Transit == null && (HireRunner.Want(vehicle, t) != null || vehicle.ReturnPending))
            {
                canSell = KindOf(vehicle) == "truck" ? instance.parkingState == ParkingState.Legal : Legality.Check(position).Legal;
                // A truck left at a stop with its hatch to the road can't sell there: the crew parks it again,
                // turned the right way (FreeParking), rather than open with the line in the traffic or wait
                // there all day. One the crew parked itself counts as at its stop anyway (HireRunner).
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
                // The player just stepped out (to buy stock, say) or stands right by it: it stays where it is.
                // So does a vehicle with nobody of its crew on shift, or all of them off sick, training or replaced.
                var recentlyHeld = lastHeld.TryGetValue(vehicle.VehicleId, out var heldAt) && t - heldAt < StayPutMinutes;
                var near = !BuildingManager.IsInsideBuilding && Vector3.Distance(PlayerHelper.GetPosition(), position) <= StayPutMetres;
                var nobodyOnShift = !HireCrew.Present(vehicle).Any(e => WeekPlan.OnShift(vehicle.Schedule, e.id, t));
                stayPut = recentlyHeld || near || nobodyOnShift;
                // The hour after a load counts as held too, but the player did nothing: no word about waiting for them then.
                keptByPlayer = (near || (recentlyHeld && reallyHeld.Contains(vehicle.VehicleId))) && !nobodyOnShift;
                // In its own commissary's bay: the crew restocks (or finds what is short) before it sets off, once per leg
                // and again when the commissary's stock of its foods changes (Core CrewRestock.Due).
                restockAt = Commissary.CrewRestockSite(FoodTrucksSession.CurrentState!, instance);
                if (restockAt != null) stockKey = Commissary.CrewStockKey(FoodTrucksSession.CurrentState!, instance, KindOf(vehicle), restockAt);
                // The stops in its own bay: reached only in the bay, and the only ones it may drive to when sold out.
                bayStops = Commissary.CrewBayStops(FoodTrucksSession.CurrentState!, vehicle);
            }
            restockSites[vehicle.VehicleId] = restockAt;
            // A truck at a catering gig at this minute stays there, as one the player holds does (without counting as
            // the player's use, which would keep it an hour longer).
            return new VehicleWhere(position.x, position.z, held || indoors || Catering.BusyAt(vehicle.VehicleId, t), canSell, hasStock, stayPut,
                canRestock: restockAt != null, stockKey: stockKey, bayStops: bayStops, home: HomePoint(home),
                // Someone on shift able to serve now (HireCrew.OnShift leaves out the sick, training and replaced).
                canServe: HireCrew.OnShift(vehicle, t).Count > 0);
        }

        // The bay of the commissary where the vehicle rents its space, which its crew starts each route day in (the
        // restock) and drives back to after the day's last stop (Core HireSchedules.HomeIndex), as the bay's first place;
        // and the route's stops that are in that bay (a day ending at one needs no second drive). Null without a space, or
        // while the bay has no places yet (logged once): the day then starts and ends where the vehicle stands.
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

        // The commissary each vehicle could restock at, as the last Where read it.
        private readonly Dictionary<string, string?> restockSites = new Dictionary<string, string?>();

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
                    if (action.Stop == HireSchedules.HomeIndex && HomeOf(vehicle) == null) return false;
                    Depart(vehicle, instance, action, t, kind);
                    return true;
                case HireActionKind.Arrive:
                    return Arrive(vehicle, instance, action.Transit!, t, kind);
                case HireActionKind.Restock:
                {
                    // Marked done for this leg and this stock first, so a load that throws isn't tried again every minute;
                    // then with the stock left after loading, so only a later change (a delivery) brings another attempt.
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
                        // Route progress goes to the log and the vehicle page, not the notices (Peter, 17 October 2026),
                        // unless the player stands where they see the window open, as for the crew's routine close.
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
            var home = action.Stop == HireSchedules.HomeIndex ? HomeOf(vehicle) : null;
            HireRunner.Depart(vehicle, action, t, position.x, position.y, position.z, heading, home?.Place);
            vehicle.Transit!.FromParkingState = (int)parking;
            FoodTrucksSession.MarkChanged();
            var where = home != null ? $"its bay at {home.Value.SiteId}" : $"stop {action.Stop + 1} ({vehicle.Schedule!.Stops[action.Stop].SpotKey})";
            // Log and vehicle page only: no notice for each leg of each route (Peter, 17 October 2026).
            Log.Info($"Route: {kind} {vehicle.VehicleId} leaves for {where}, arriving at minute {action.ArrivesAt} ({action.ArrivesAt - t} minutes).");
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

        // Street placements (arrival, fallback, recovery, bring-back, FreeParking), as before: another vehicle on the
        // vehicles layer where this one's hull would be (the game's own spawn cleanup releases parked lane cars, as for
        // its own vehicles), plus the player standing in the footprint; never `self`'s own hull (review round 6).
        private static bool Taken(Vector3 position, Quaternion rotation, string kind, VehicleInstance? self = null) =>
            Occupied(position, rotation, kind, self, 1 << LayerHelper.VehiclesLayerIndex, QueryTriggerInteraction.UseGlobal);

        // Bay arrivals only (review rounds 4 to 6): anything a vehicle mustn't be put on, the player's and the mod's
        // vehicles, the lanes' parked cars and AI traffic with their trigger colliders, and the player; never `self`.
        // Bay spawns skip the game's cleanup (SpawnNoCleanup), so nothing in the bay is deleted.
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
            // The player, wherever their collider sits: standing inside the hull's footprint.
            var local = Quaternion.Inverse(rotation) * (PlayerHelper.GetPosition() - centre);
            return Mathf.Abs(local.x) <= half.x + 0.3f && Mathf.Abs(local.z) <= half.z + 0.3f && Mathf.Abs(local.y) <= half.y + 1f;
        }

        // Whether a truck standing there would be refused for its hatch (HatchCheck): such a space is taken
        // the other way round. Anything else stands as the lane has it, as an unclear spot opens anyway.
        private static bool HatchRefused(Vector3 position, Quaternion rotation, TrafficLanes lanes, out HatchCheck.Result result)
        {
            result = HatchCheck.Probe(position, rotation, lanes);
            return !result.Opens;
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
            // A stop in its own commissary's bay: parked in the bay or not at all (review round 3).
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
                    SayStopTaken(vehicle, transit, kind, "foodtrucks:notification_hire_stop_no_parking");
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
            // A parking space is legal; back on its own old spot, it is what it was.
            instance.parkingState = !atStop && neighbourhood == null ? (ParkingState)transit.FromParkingState : ParkingState.Legal;
            if (neighbourhood != null) instance.parkingNeighbourhood = neighbourhood;
            var placed = controller.transform.position;
            // Parked near a bay stop but not in the bay: not arrived, so Core skips the stop until its opening ends and
            // the next bay stop tries again, rather than driving there over and over.
            if (bayFallback) atStop = false;
            HireRunner.Arrived(vehicle, placed.x, placed.z, atStop, t);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Route: {kind} {vehicle.VehicleId} arrived {(atStop ? "at" : "back from")} {transit.ToSpotKey}: {placed}, {Vector3.Distance(placed, target):F0} m from the stop.");
            return true;
        }

        // Arriving at a stop in the bay of the commissary where the vehicle rents its space: it is placed on the bay (its
        // resolved points, then places along the paint) and counts as arrived only if it then stands in the bay by the
        // restock test (Commissary.InBay). Null when the stop isn't such a stop (the ordinary arrival runs). One placement
        // attempt per game minute; while none works it stays on its way, logged once. After 2 × RetryMinutes it parks as
        // at any stop but as not arrived (Core skips the stop until its opening ends), so it is never off the map for good.
        private static bool? BayArrival(VehicleState vehicle, VehicleInstance instance, TransitState transit, long t, string kind, out bool fallback)
        {
            fallback = false;
            var state = FoodTrucksSession.CurrentState;
            var index = transit.ToStopIndex;
            // A bay stop of the route, or the drive back to its bay after the last stop.
            var home = transit.ToSpotKey == HireSchedules.HomeSpotKey;
            if (state == null || (!home && (index == null || !Commissary.CrewBayStops(state, vehicle).Contains(index.Value)))) return null;
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.VehicleId);
            var site = space == null ? null : DemandData.Commissaries?.Site(space.CommissaryId);
            if (site == null) return null;
            var late = t - transit.ArrivesAt;
            var key = (vehicle.VehicleId, transit.ArrivesAt);
            if (bayTried.TryGetValue(vehicle.VehicleId, out var tried) && tried == t) return false;
            bayTried[vehicle.VehicleId] = t;
            // What doesn't depend on the place: the bay resolved and its street side readable. Without it no place can pass
            // InBay this minute, so none is tried (and the 20 minutes still run out to the street fallback).
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
            // At most PlacesPerAttempt places a minute, carrying on from where the last attempt stopped, but enough that the
            // whole set is tried before the 20 minutes are up; a catch-up after a time skip tries all that are left at once.
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
            // Off the map again until a place in the bay frees up, as a vehicle on its way is.
            if (placedAny && VehicleHelper.GetVehicleController(instance) is { } shown) TakeOffMap(shown);
            if (late == 0) Log.Info($"Route: no free place in the bay of {site.Id} for {kind} {vehicle.VehicleId}; it waits on its way and tries each minute.");
            return false;
        }

        private static readonly Dictionary<(string, long), int> bayCovered = new Dictionary<(string, long), int>();
        private static readonly Dictionary<string, long> bayTried = new Dictionary<string, long>();
        private static readonly Dictionary<string, int> bayCursor = new Dictionary<string, int>();
        private const int PlacesPerAttempt = 8;
        private static readonly HashSet<(string, long)> bayGaveUp = new HashSet<(string, long)>();

        // Puts the vehicle at a position: moves its controller if it has one, or spawns it as the game does
        // when the private driver brings a car back. The position is written to the save either way.
        // True while a bay arrival spawns a vehicle: the game's cleanup of blocking cars is skipped (CartParts.DestroyBlockingPrefix).
        internal static bool SpawnNoCleanup;

        // `noCleanup` only for bay arrivals, which checked the place with BayTaken; street placements keep the game's cleanup.
        // The physics scene is synced after every placement, so the next check sees the vehicle where it now stands.
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
                // The game's spawn deletes parked and traffic cars in the way (VehicleHelper.DestroyBlockingVehicles). A bay
                // arrival placed only where BayTaken found nothing, so its spawn deletes nothing (CartParts.DestroyBlockingPrefix).
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
            // A space that reads road by the hatch either way round (a median) is kept only in case no lane offers better.
            (Vector3, Quaternion, string?)? refused = null;
            // The driving lanes for every space tried, read once at any height (the traffic system's search scans the
            // whole city); each probe keeps those within its own reach and height, as its own read would.
            TrafficLanes? traffic = null;
            foreach (var lane in lanes)
            {
                if (!lane.TryGetRandomFreeSpotForPlayerVehicle(out var position, out var rotation, 1 << LayerHelper.VehiclesLayerIndex)) continue;
                if (Vector3.Distance(position, target) > maxMetres) continue;
                // The hatch has to face the pavement: a lane's rotation can point either way along the kerb. The
                // game's slot test is sized for a car, so the truck's own hull is checked, the way it will stand.
                traffic ??= TrafficLanes.Read(target, (float)LaneReach.CoverRadius(maxMetres, HatchCheck.LaneSearchMetres));
                if (HatchRefused(position, rotation, traffic, out var hatch))
                {
                    var turned = rotation * Quaternion.Euler(0f, 180f, 0f);
                    if (HatchRefused(position, turned, traffic, out var turnedHatch))
                    {
                        Log.Info($"Route: the space at {position} has road by the hatch either way round ({hatch}; turned: {turnedHatch}); trying the next lane.");
                        if (refused == null && !Taken(position, rotation, "truck")) refused = (position, rotation, lane.neighbourhood);
                        continue;
                    }
                    rotation = turned;
                    Log.Info($"Route: the space at {position} would put the hatch to the road ({hatch}); the truck takes it the other way round.");
                }
                if (Taken(position, rotation, "truck")) continue;
                return (position, rotation, lane.neighbourhood);
            }
            if (refused != null) Log.Info($"Route: no space near {target} opens; the truck takes the one at {refused.Value.Item1} and the crew says why it can't open.");
            return refused;
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
            hatches.Clear();
        }

        private static VehicleInstance? Instance(string id) =>
            SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
    }
}
