#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using Streets;
using UnityEngine;

namespace FoodTrucks
{
    // Adding and changing a route's stops, shared by the BizMan vehicle page ("Add the spot where it stands", its stop
    // rows) and the map pick. Core's RouteEditor decides; this picks default times, writes the save and tells the crew.
    internal static class RouteStops
    {
        // Opening hours are picked in half hours.
        internal const int Step = 30;

        internal static int LatestClose => DemandData.Rules?.Fleet.LatestCloseMinute ?? 27 * 60;

        // A new stop's default hours: from 11:00 (or the first free time after it, else the first free time of the day)
        // for up to three hours, within the gaps the route's other stops leave. The stop goes in at once and its times are
        // changed with the dropdowns on its row of the vehicle page (Peter, 15 October 2026: the list pickers that asked
        // for the times took the player out of the page).
        internal const int DefaultOpen = 11 * 60;
        internal const int DefaultLength = 3 * 60;

        // Adds a stop to `day` (the weekday chosen on the Week plan tab) at the default hours. `done` runs after either
        // outcome, so the caller goes back to the page.
        public static void AddWithDefaultTimes(string vehicleId, DayOfWeek day, Vector3 position, float heading, Action? done = null)
        {
            try
            {
                var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
                var rules = DemandData.Rules;
                if (rules != null && WeekPlan.StopsOf(vehicle?.Schedule, day).Count >= rules.Fleet.MaxStops)
                {
                    CartSelling.Notify("foodtrucks:notification_route_full", ("max", rules.Fleet.MaxStops.ToString()));
                    return;
                }
                var opens = RouteEditor.OpenTimes(vehicle?.Schedule, day, Step, LatestClose);
                if (opens.Count == 0)
                {
                    CartSelling.Notify("foodtrucks:notification_route_no_time");
                    return;
                }
                var open = opens.Where(m => m >= DefaultOpen).DefaultIfEmpty(opens[0]).First();
                var closes = RouteEditor.CloseTimes(vehicle?.Schedule, day, open, Step, LatestClose);
                if (closes.Count == 0)
                {
                    CartSelling.Notify("foodtrucks:notification_route_no_time");
                    return;
                }
                var close = closes.Where(m => m <= open + DefaultLength).DefaultIfEmpty(closes[0]).Max();
                Add(vehicleId, day, position, heading, open, close);
            }
            catch (Exception ex) { Log.Error("Route stop: " + ex); }
            finally { done?.Invoke(); }
        }

        public static bool Add(string vehicleId, DayOfWeek day, Vector3 position, float heading, int open, int close)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return false;
            var vehicle = StreetSales.GetOrAdd(state, vehicleId);
            if (string.IsNullOrEmpty(vehicle.VehicleType))
                vehicle.VehicleType = FoodTrucksMod.KindOf(SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicleId)?.vehicleTypeName) ?? "cart";
            var x = position.x;
            var z = position.z;
            var stop = new ScheduleStop { Day = day, SpotKey = StaffDialog.SpotKey(x, z), X = x, Y = position.y, Z = z, Heading = heading, OpenAt = open, CloseAt = close };
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
                     $"added to {vehicle.VehicleType} {vehicleId} on {day}; {WeekPlan.StopsOf(schedule, day).Count} stops that day.");
            CartSelling.Notify("foodtrucks:notification_route_added", ("open", StaffDialog.Clock(open)), ("close", StaffDialog.Clock(close)),
                ("count", Notices.Counted(WeekPlan.StopsOf(schedule, day).Count, "foodtrucks:count_stops")));
            return true;
        }

        // Removes a stop; the crew closes a window open there first.
        public static void Remove(VehicleState vehicle, int index)
        {
            if (vehicle.Schedule == null || index < 0 || index >= vehicle.Schedule.Stops.Count || !FoodTrucksSession.CanWrite) return;
            if (vehicle.IsOpen && vehicle.HireStop == index) CartSelling.CloseByCrew(vehicle, HireCrew.Now());
            RouteEditor.RemoveStop(vehicle, index);
            Changed(vehicle);
            Log.Info($"Route: stop {index + 1} of the week removed from {vehicle.VehicleType} {vehicle.VehicleId}.");
        }

        // The route changed: each stop opens no earlier than the drive from the one before allows (Core
        // RouteEditor.FitTravel, said in one notice), today is planned again, the crew's weekly hours follow, the save is marked.
        // `shifts`: the edit changed shifts too (a paste, a cleared day), so yesterday's late plan is planned again as well.
        public static void Changed(VehicleState vehicle, bool shifts = false)
        {
            FitTravel(vehicle);
            if (shifts) HireRunner.ReplanShifts(vehicle, HireCrew.Now());
            else HireRunner.Replan(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
        }

        // A route saved before the travel rule, or whose drives grew with the game speed, is fitted too, without an edit:
        // when its page opens and as the crew plans (HireRoutes). Only a route that changes is written, said in the same
        // notice; one that can't be fitted is only logged, once per vehicle and problem stop.
        internal static void FitIfNeeded(VehicleState vehicle, string why)
        {
            if (vehicle?.Schedule == null || vehicle.Schedule.Stops.Count < 2 || !FoodTrucksSession.CanWrite) return;
            if (!FitTravel(vehicle, quiet: true)) return;
            Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId} fitted to its travel times ({why}).");
            HireRunner.Replan(vehicle, HireCrew.Now());
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
        }

        private static readonly HashSet<(string, int)> stuckShown = new HashSet<(string, int)>();

        // True when stops moved.
        private static bool FitTravel(VehicleState vehicle, bool quiet = false)
        {
            var rules = DemandData.Rules;
            if (rules == null || vehicle.Schedule == null) return false;
            var walk = Travel.ModeFor(vehicle.VehicleType) == TravelMode.Push;
            var fit = RouteEditor.FitTravel(new HireSchedules(rules.Fleet), rules.Fleet, vehicle.Schedule, Travel.ModeFor(vehicle.VehicleType),
                HireRoutes.GameSpeed(), Step, LatestClose);
            if (fit.Stuck is int stuck)
            {
                if (quiet && !stuckShown.Add((vehicle.VehicleId, stuck))) return false;
                var stuckNumber = Number(vehicle.Schedule, stuck);
                Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId}: {vehicle.Schedule.Stops[stuck].Day}'s stop {stuckNumber} can't be moved late enough for the {(walk ? "walk" : "drive")} from stop {stuckNumber - 1} ({fit.Problem}); the times stay.");
                if (!quiet) CartSelling.Notify("foodtrucks:notification_route_travel_stuck", ("number", stuckNumber.ToString()), ("from", (stuckNumber - 1).ToString()));
                return false;
            }
            if (fit.Moved.Count == 0) return false;
            var lines = fit.Moved.Select(m => LocalizorManager.GetLocalization(walk ? "foodtrucks:route_moved_walk" : "foodtrucks:route_moved_drive",
                new Dictionary<string, string>
                {
                    ["number"] = Number(vehicle.Schedule, m.Index).ToString(), ["open"] = StaffDialog.Clock(m.NewOpen),
                    ["from"] = (Number(vehicle.Schedule, m.Index) - 1).ToString(),
                    ["minutes"] = m.DriveMinutes.ToString()
                })).ToList();
            Log.Info($"Route: {vehicle.VehicleType} {vehicle.VehicleId}: {string.Join("; ", fit.Moved.Select(m => $"{vehicle.Schedule.Stops[m.Index].Day}'s stop {Number(vehicle.Schedule, m.Index)} {StaffDialog.Clock(m.OldOpen)}-{StaffDialog.Clock(m.OldClose)} now {StaffDialog.Clock(m.NewOpen)}-{StaffDialog.Clock(m.NewClose)} ({m.DriveMinutes} min from stop {m.Index})"))}.");
            CartSelling.Notify("foodtrucks:notification_route_travel_moved", ("stops", string.Join(". ", lines)));
            return true;
        }

        // A stop's number on its weekday's route, from 1.
        internal static int Number(HireSchedule? schedule, int index)
        {
            var stop = schedule?.Stops?.ElementAtOrDefault(index);
            return stop == null ? index + 1 : WeekPlan.StopsOf(schedule, stop.Day).IndexOf(index) + 1;
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
            : problem == ScheduleProblem.Unreachable ? "foodtrucks:bizman_page_route_invalid_travel"
            : problem == ScheduleProblem.BadTimes ? "foodtrucks:bizman_page_route_invalid_times"
            : problem == ScheduleProblem.Uncovered ? "foodtrucks:bizman_page_route_uncovered"
            : "foodtrucks:bizman_page_route_invalid";
    }
}
