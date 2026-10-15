#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using Streets;
using UI.InteriorDesigner;
using UnityEngine;

namespace FoodTrucks
{
    // Adding and changing a route's stops, shared by the BizMan vehicle page ("Add the spot where it stands", its stop
    // rows) and the map pick. Core's RouteEditor decides; this asks the times, writes the save and tells the crew.
    internal static class RouteStops
    {
        // Opening hours are picked in half hours.
        internal const int Step = 30;

        internal static int LatestClose => DemandData.Rules?.Fleet.LatestCloseMinute ?? 27 * 60;

        // Asks when the crew opens and closes at a new stop, with the game's list picker, offering only times that fit
        // between the route's other stops; then adds it. `added` runs after a stop went in, `cancelled` when the player
        // closes either picker without choosing.
        public static void AskTimesAndAdd(string vehicleId, Vector3 position, float heading, Action? added = null, Action? cancelled = null)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            var rules = DemandData.Rules;
            if (rules != null && vehicle?.Schedule != null && vehicle.Schedule.Stops.Count >= rules.Fleet.MaxStops)
            {
                CartSelling.Notify("foodtrucks:notification_route_full", ("max", rules.Fleet.MaxStops.ToString()));
                cancelled?.Invoke();
                return;
            }
            var opens = RouteEditor.OpenTimes(vehicle?.Schedule, Step, LatestClose);
            if (opens.Count == 0)
            {
                CartSelling.Notify("foodtrucks:notification_route_no_time");
                cancelled?.Invoke();
                return;
            }
            var times = opens.ToDictionary(StaffDialog.Clock, m => m);
            var answered = false;
            DropdownSelector.Show(times.Keys.ToList(), Guard(chosen =>
            {
                answered = true;
                if (times.TryGetValue(chosen, out var open)) HireRoutes.Later(() => AskClose(vehicleId, position, heading, open, added, cancelled));
                else cancelled?.Invoke();
            }), "foodtrucks:hires_open_header", "foodtrucks:hires_open_body");
            if (cancelled != null) WhenClosedUnanswered(() => answered, cancelled);
        }

        private static void AskClose(string vehicleId, Vector3 position, float heading, int open, Action? added, Action? cancelled)
        {
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            var closes = RouteEditor.CloseTimes(vehicle?.Schedule, open, Step, LatestClose);
            var times = closes.ToDictionary(StaffDialog.Clock, m => m);
            var answered = false;
            DropdownSelector.Show(times.Keys.ToList(), Guard(chosen =>
            {
                answered = true;
                if (times.TryGetValue(chosen, out var close) && Add(vehicleId, position, heading, open, close)) added?.Invoke();
                else cancelled?.Invoke();
            }), "foodtrucks:hires_close_header", "foodtrucks:hires_close_body");
            if (cancelled != null) WhenClosedUnanswered(() => answered, cancelled);
        }

        // The game's list picker has no cancel callback (DropdownSelectorUI.OnCancel only drops the confirm): checked each
        // frame, a picker that closed without an answer runs `cancelled`.
        private static void WhenClosedUnanswered(Func<bool> answered, Action cancelled)
        {
            HireRoutes.Later(() =>
            {
                if (answered()) return;
                if (DropdownSelector.isOpen)
                {
                    WhenClosedUnanswered(answered, cancelled);
                    return;
                }
                cancelled();
            });
        }

        public static bool Add(string vehicleId, Vector3 position, float heading, int open, int close)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return false;
            var vehicle = StreetSales.GetOrAdd(state, vehicleId);
            if (string.IsNullOrEmpty(vehicle.VehicleType))
                vehicle.VehicleType = FoodTrucksMod.KindOf(SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicleId)?.vehicleTypeName) ?? "cart";
            var x = position.x;
            var z = position.z;
            var stop = new ScheduleStop { SpotKey = StaffDialog.SpotKey(x, z), X = x, Y = position.y, Z = z, Heading = heading, OpenAt = open, CloseAt = close };
            var problem = RouteEditor.AddStop(new HireSchedules(rules.Fleet), vehicle, stop);
            if (problem != ScheduleProblem.None)
            {
                Log.Info($"Route: stop {StaffDialog.Clock(open)}-{StaffDialog.Clock(close)} refused for {vehicleId}: {problem}.");
                CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                return false;
            }
            var schedule = vehicle.Schedule!;
            Changed(vehicle);
            Log.Info($"Route: stop {stop.SpotKey} at x {x:F1}, z {z:F1}, heading {heading:F0} {StaffDialog.Clock(open)}-{StaffDialog.Clock(close)} " +
                     $"added to {vehicle.VehicleType} {vehicleId}; {schedule.Stops.Count} stops.");
            CartSelling.Notify("foodtrucks:notification_route_added", ("open", StaffDialog.Clock(open)), ("close", StaffDialog.Clock(close)),
                ("count", schedule.Stops.Count.ToString()));
            return true;
        }

        // Removes a stop; the crew closes a window open there first.
        public static void Remove(VehicleState vehicle, int index)
        {
            if (vehicle.Schedule == null || index < 0 || index >= vehicle.Schedule.Stops.Count || !FoodTrucksSession.CanWrite) return;
            if (vehicle.IsOpen && vehicle.HireStop == index) CartSelling.CloseByCrew(vehicle, HireCrew.Now());
            RouteEditor.RemoveStop(vehicle, index);
            Changed(vehicle);
            Log.Info($"Route: stop {index + 1} removed from {vehicle.VehicleType} {vehicle.VehicleId}.");
        }

        // The route changed: plan today again, the crew's weekly hours follow, the save is marked.
        public static void Changed(VehicleState vehicle)
        {
            HireRunner.Replan(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
        }

        // A stop's name for lists: the premium spot it is at, else the nearest building's address and the
        // neighbourhood, else its pitch key.
        public static string Label(ScheduleStop stop)
        {
            var position = new Vector3((float)stop.X, (float)stop.Y, (float)stop.Z);
            try
            {
                var premium = CityPitches.PremiumAt(position);
                if (premium != null) return CityPitches.Text(premium.Name);
                var address = NearestAddress(position);
                var tables = DemandData.Tables;
                var neighbourhood = tables == null ? null : World.ReadSpot(tables, new Vector3((float)stop.X, 0f, (float)stop.Z)).Neighbourhood;
                var area = string.IsNullOrEmpty(neighbourhood) ? null : LocalizorManager.GetLocalization(neighbourhood, null);
                if (address != null) return area == null ? address : $"{address}, {area}";
                if (area != null) return area;
            }
            catch (Exception ex) { Log.Error("Route stop name: " + ex.Message); }
            return stop.SpotKey;
        }

        // The formatted address of the building whose entrance is nearest, within 60 m.
        public static string? NearestAddress(Vector3 position)
        {
            CityBuildingController? best = null;
            var bestDistance = 60f * 60f;
            foreach (var (controller, doors) in World.AllBuildings())
            {
                foreach (var door in doors)
                {
                    var d = (door.x - position.x) * (door.x - position.x) + (door.z - position.z) * (door.z - position.z);
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    best = controller;
                }
            }
            return best?.building?.Address?.ToFormattedString();
        }

        // What the BizMan page says about a route that fails today's rules (HireRunner.RouteProblem).
        internal static string ProblemKey(ScheduleProblem problem) =>
            problem == ScheduleProblem.Overlap ? "foodtrucks:bizman_page_route_invalid_overlap"
            : problem == ScheduleProblem.BadTimes ? "foodtrucks:bizman_page_route_invalid_times"
            : "foodtrucks:bizman_page_route_invalid";

        // The game's list picker calls back without a guard: nothing may throw out of a choice.
        private static Action<string> Guard(Action<string> choice) => chosen =>
        {
            try { choice(chosen); }
            catch (Exception ex) { Log.Error("Route: " + ex); }
        };
    }
}
