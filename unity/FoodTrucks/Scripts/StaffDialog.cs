#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using UI.InteriorDesigner;
using UI.ItemPanel;

namespace FoodTrucks
{
    // The "Hires" button on the cart's and truck's panel: the crew and the route, with the game's list picker
    // (the one the Prices button uses). Take on or let go of a worker, add the spot the vehicle stands on as a
    // stop with its opening hours, remove a stop, pick the days the route runs.
    internal static class StaffDialog
    {
        private const int MaxCandidatesShown = 8;
        // Opening hours are picked in half hours.
        private const int Step = 30;

        public static void Show(ItemPanelUI panel)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            var cart = VehicleHelper.GetCurrentVehicle();
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            if (state == null || rules == null || cart == null || kind == null) return;
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (DropdownSelector.onShow == null)
            {
                Log.Error("Hires: the game's list picker isn't available in this scene.");
                CartSelling.Notify("foodtrucks:notification_prices_unavailable");
                return;
            }
            HireCrew.Check(state);
            var vehicle = StreetSales.GetOrAdd(state, cart.id);
            if (string.IsNullOrEmpty(vehicle.VehicleType)) vehicle.VehicleType = kind;
            var id = cart.id;
            var options = new List<(string Label, Action Act)>();

            foreach (var employee in vehicle.Crew.Select(HireCrew.Find).Where(e => e != null).Select(e => e!))
            {
                var employeeId = employee.id;
                var name = HireCrew.Name(employee);
                options.Add((Format("foodtrucks:hires_option_letgo", name, Skill(employee), Wage(employee.hourlyWage)), () =>
                {
                    HireCrew.Remove(vehicle, employeeId, "let go by the player");
                    CartSelling.Say("foodtrucks:notification_crew_letgo", kind, ("name", name));
                }));
            }
            var limit = rules.Fleet.CrewLimit(kind);
            if (vehicle.Crew.Count < limit)
            {
                foreach (var employee in HireCrew.Available(state).Take(MaxCandidatesShown))
                {
                    var hire = employee;
                    options.Add((Format("foodtrucks:hires_option_takeon", HireCrew.Name(employee), Skill(employee),
                        employee.satisfaction.ToString("F0"), Wage(employee.hourlyWage)), () =>
                    {
                        if (HireCrew.Add(state, vehicle, hire))
                            CartSelling.Say("foodtrucks:notification_crew_added", kind, ("name", HireCrew.Name(hire)));
                    }));
                }
            }

            var schedule = vehicle.Schedule;
            if (schedule == null || schedule.Stops.Count < rules.Fleet.MaxStops)
                options.Add((Text("foodtrucks:hires_option_addstop"), () => AddStopHere(panel, id)));
            if (schedule != null)
            {
                for (var i = 0; i < schedule.Stops.Count; i++)
                {
                    var stop = schedule.Stops[i];
                    var index = i;
                    options.Add((Format("foodtrucks:hires_option_removestop", (i + 1).ToString(), Clock(stop.OpenAt), Clock(stop.CloseAt), Place(stop)), () =>
                    {
                        if (vehicle.Schedule == null || index >= vehicle.Schedule.Stops.Count) return;
                        vehicle.Schedule.Stops.RemoveAt(index);
                        if (vehicle.Schedule.Stops.Count == 0) vehicle.Schedule = null;
                        RouteChanged(vehicle);
                        Log.Info($"Route: stop {index + 1} removed from {kind} {id}.");
                    }));
                }
                options.Add((Format("foodtrucks:hires_option_days", Text(DaysKey(schedule.Days))), () => HireRoutes.Later(() => AskDays(id))));
            }

            var labels = options.Select(o => o.Label).ToList();
            DropdownSelector.Show(labels, chosen =>
            {
                var match = options.FirstOrDefault(o => o.Label == chosen);
                try { match.Act?.Invoke(); }
                catch (Exception ex) { Log.Error("Hires: " + ex); }
            }, "foodtrucks:hires_header", vehicle.Crew.Count == 0 ? "foodtrucks:hires_body_nocrew" : "foodtrucks:hires_body");
        }

        // The stop is where the vehicle stands, if the vehicle could open here now.
        private static void AddStopHere(ItemPanelUI panel, string vehicleId)
        {
            var cart = VehicleHelper.GetCurrentVehicle();
            var controller = VehicleHelper.GetCurrentVehicleBase();
            if (cart == null || cart.id != vehicleId || controller == null) return;
            var problem = CartSelling.PitchProblem(panel, cart);
            if (problem != null)
            {
                CartSelling.Notify(problem);
                return;
            }
            var position = controller.transform.position;
            var heading = controller.transform.eulerAngles.y;
            HireRoutes.Later(() => AskOpen(vehicleId, position, heading));
        }

        private static void AskOpen(string vehicleId, UnityEngine.Vector3 position, float heading)
        {
            var times = new Dictionary<string, int>();
            for (var minute = 0; minute < 24 * 60; minute += Step) times[Clock(minute)] = minute;
            DropdownSelector.Show(times.Keys.ToList(), chosen =>
            {
                if (times.TryGetValue(chosen, out var open)) HireRoutes.Later(() => AskClose(vehicleId, position, heading, open));
            }, "foodtrucks:hires_open_header", "foodtrucks:hires_open_body");
        }

        private static void AskClose(string vehicleId, UnityEngine.Vector3 position, float heading, int open)
        {
            var latest = DemandData.Rules?.Fleet.LatestCloseMinute ?? 27 * 60;
            var times = new Dictionary<string, int>();
            for (var minute = open + Step; minute <= latest; minute += Step) times[Clock(minute)] = minute;
            DropdownSelector.Show(times.Keys.ToList(), chosen =>
            {
                if (times.TryGetValue(chosen, out var close)) AddStop(vehicleId, position, heading, open, close);
            }, "foodtrucks:hires_close_header", "foodtrucks:hires_close_body");
        }

        private static void AddStop(string vehicleId, UnityEngine.Vector3 position, float heading, int open, int close)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var vehicle = StreetSales.GetOrAdd(state, vehicleId);
            var schedule = vehicle.Schedule ?? new HireSchedule();
            var x = position.x;
            var z = position.z;
            var stop = new ScheduleStop { SpotKey = SpotKey(x, z), X = x, Y = position.y, Z = z, Heading = heading, OpenAt = open, CloseAt = close };
            var problem = RouteEditor.AddStop(new HireSchedules(rules.Fleet), schedule, stop);
            if (problem != ScheduleProblem.None)
            {
                Log.Info($"Route: stop {Clock(open)}-{Clock(close)} refused for {vehicleId}: {problem}.");
                CartSelling.Notify(problem == ScheduleProblem.Overlap ? "foodtrucks:notification_route_overlap" : "foodtrucks:notification_route_refused");
                return;
            }
            vehicle.Schedule = schedule;
            RouteChanged(vehicle);
            Log.Info($"Route: stop {SpotKey(x, z)} {Clock(open)}-{Clock(close)} added to {vehicle.VehicleType} {vehicleId}; {schedule.Stops.Count} stops.");
            CartSelling.Notify("foodtrucks:notification_route_added", ("open", Clock(open)), ("close", Clock(close)), ("count", schedule.Stops.Count.ToString()));
        }

        private static readonly (string Key, DayOfWeek[] Days)[] DayChoices =
        {
            ("foodtrucks:hires_days_every", Array.Empty<DayOfWeek>()),
            ("foodtrucks:hires_days_weekdays", new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }),
            ("foodtrucks:hires_days_monsat", new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }),
            ("foodtrucks:hires_days_weekends", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }),
        };

        private static void AskDays(string vehicleId)
        {
            var options = DayChoices.ToDictionary(c => Text(c.Key), c => c.Days);
            DropdownSelector.Show(options.Keys.ToList(), chosen =>
            {
                var state = FoodTrucksSession.CurrentState;
                var vehicle = StreetSales.Find(state, vehicleId);
                if (vehicle?.Schedule == null || !options.TryGetValue(chosen, out var days) || !FoodTrucksSession.CanWrite) return;
                vehicle.Schedule.Days = days.ToList();
                RouteChanged(vehicle);
                Log.Info($"Route: {vehicleId} runs {(days.Length == 0 ? "every day" : string.Join(", ", days))}.");
            }, "foodtrucks:hires_days_header", null!);
        }

        private static string DaysKey(List<DayOfWeek>? days)
        {
            var set = new HashSet<DayOfWeek>(days ?? new List<DayOfWeek>());
            foreach (var (key, choice) in DayChoices)
                if (set.SetEquals(choice)) return key;
            return "foodtrucks:hires_days_custom";
        }

        private static void RouteChanged(VehicleState vehicle)
        {
            HireRunner.Replan(vehicle);
            HireCrew.SetWeeklyWork(vehicle);
            FoodTrucksSession.MarkChanged();
        }

        // Regulars and logs know a stop by its 10 m square.
        internal static string SpotKey(float x, float z) => $"{Math.Round(x / 10.0) * 10:F0}:{Math.Round(z / 10.0) * 10:F0}";

        // 13:30, and 01:00 for a late stop that runs past midnight.
        internal static string Clock(int minute) => $"{minute / 60 % 24:D2}:{minute % 60:D2}";

        private static string Place(ScheduleStop stop)
        {
            var tables = DemandData.Tables;
            var neighbourhood = tables == null ? null : World.ReadSpot(tables, new UnityEngine.Vector3((float)stop.X, 0f, (float)stop.Z)).Neighbourhood;
            return string.IsNullOrEmpty(neighbourhood) ? stop.SpotKey : LocalizorManager.GetLocalization(neighbourhood, null);
        }

        private static string Skill(Entities.EmployeeInstance employee) => HireCrew.Skill(employee).ToString("F0");

        private static string Wage(float hourly) => CartSelling.Money((long)Math.Round(hourly * 100));

        private static string Format(string key, params object[] values) => string.Format(Text(key), values);

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);
    }
}
