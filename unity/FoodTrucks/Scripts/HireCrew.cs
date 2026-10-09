#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Entities;
using Enums;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;

namespace FoodTrucks
{
    // The game's own employees as a vehicle's crew (Peter, 8 October 2026). A crew member stays unassigned
    // in the game: no building, so the game's schedules, stations and sick days leave them alone. The mod
    // keeps the employee IDs on the vehicle, logs their shift hours so the game's midnight payroll pays them
    // (as unassigned wages, tax-deductible), sets their weekly hours and days from the route so their job
    // demands can be met, and keeps the "no task" complaint from firing while they have a route.
    internal static class HireCrew
    {
        internal const string CustomerServiceSkill = "ba:skill_customerservice";

        // Employees the player can put on a vehicle: hired, not assigned to a building, not on another vehicle.
        public static List<EmployeeInstance> Available(SaveState state)
        {
            var taken = new HashSet<string>(state.Vehicles.SelectMany(v => v.Crew ?? new List<string>()), StringComparer.Ordinal);
            return (SaveGameManager.Current?.EmployeeInstances ?? new List<EmployeeInstance>())
                .Where(e => e != null && !e.IsCandidate && !e.isBeingReplaced && !e.IsAssignedToAnyBusiness() && !taken.Contains(e.id))
                .OrderByDescending(Skill)
                .ThenBy(e => e.characterData?.name, StringComparer.Ordinal)
                .ToList();
        }

        // How many work the window at once: the vehicle's staff in economy.json (cart 1, truck 3).
        public static int Limit(string? kind) =>
            DemandData.Tables?.Vehicle(string.IsNullOrEmpty(kind) ? "cart" : kind!)?.Staff ?? 1;

        public static EmployeeInstance? Find(string id) => EmployeeHelper.GetEmployeeById(id, showError: false);

        public static double Skill(EmployeeInstance employee) => employee.GetSkillValue(CustomerServiceSkill);

        // The game's measure for a worker: skill × satisfaction / 100.
        public static double Effective(EmployeeInstance employee) =>
            Core.Rules.ServiceRules.EffectiveSkill(Skill(employee), employee.satisfaction);

        public static string Name(EmployeeInstance employee) => employee.characterData?.name ?? employee.id;

        // The crew on the vehicle's window now: those whose shift is running, minus anyone off sick or training.
        public static List<EmployeeInstance> OnShift(VehicleState vehicle, long now)
        {
            if (vehicle.Crew == null || vehicle.Crew.Count == 0 || vehicle.Transit != null || HireRunner.Want(vehicle, now) == null)
                return new List<EmployeeInstance>();
            // Someone a headhunter is replacing is on the way out: the game stops paying them, so they don't serve.
            return vehicle.Crew.Select(Find).Where(e => e != null && !e.isAbsent && e.trainingSession == null && !e.isBeingReplaced).Select(e => e!).ToList();
        }

        // The crew who could work now, shift or not: not off sick, training or being replaced (catering gigs).
        public static List<EmployeeInstance> Present(VehicleState vehicle) =>
            vehicle.Crew == null ? new List<EmployeeInstance>()
                : vehicle.Crew.Select(Find).Where(e => e != null && !e.isAbsent && e.trainingSession == null && !e.isBeingReplaced).Select(e => e!).ToList();

        public static bool Add(SaveState state, VehicleState vehicle, EmployeeInstance employee)
        {
            if (vehicle.Crew.Count >= Limit(vehicle.VehicleType)) return false;
            if (state.Vehicles.Any(v => v.Crew != null && v.Crew.Contains(employee.id))) return false;
            vehicle.Crew.Add(employee.id);
            SetWeeklyWork(vehicle);
            ClearUnassignedTask(employee);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Crew: {Name(employee)} ({employee.id}, customer service {Skill(employee):F0}, satisfaction {employee.satisfaction:F0}, " +
                     $"${employee.hourlyWage:F2} an hour) on {vehicle.VehicleType} {vehicle.VehicleId}.");
            return true;
        }

        public static void Remove(VehicleState vehicle, string employeeId, string reason)
        {
            if (vehicle.Crew == null || !vehicle.Crew.Remove(employeeId)) return;
            var employee = Find(employeeId);
            if (employee != null)
            {
                ClearWeeklyWork(employee);
                // Back to the game's own "unassigned" reminder.
                if (!employee.IsAssignedToAnyBusiness()) employee.AddTodoTask(TodoTaskType.EmployeeUnassigned, invokeQuestAffectingChange: false);
            }
            FoodTrucksSession.MarkChanged();
            Log.Info($"Crew: {employeeId} off {vehicle.VehicleType} {vehicle.VehicleId} ({reason}).");
        }

        // Fired, resigned, retired, poached or put to work in a building by the player: off the vehicle.
        // A vehicle that is gone (sold, seized, removed) lets its whole crew go and ends its route.
        public static void Check(SaveState state)
        {
            var game = SaveGameManager.Current;
            foreach (var vehicle in state.Vehicles)
            {
                if (vehicle.Crew == null || vehicle.Crew.Count == 0) continue;
                if (game != null && !game.VehicleInstances.Any(v => v.id == vehicle.VehicleId) &&
                    (game.privateDriverVehicleInstances == null || !game.privateDriverVehicleInstances.Any(v => v.id == vehicle.VehicleId)))
                {
                    foreach (var id in vehicle.Crew.ToList()) Remove(vehicle, id, "the vehicle is gone");
                    vehicle.Schedule = null;
                    vehicle.Shifts.Clear();
                    vehicle.Transit = null;
                    vehicle.Arrival = null;
                    vehicle.HireStop = null;
                    continue;
                }
                foreach (var id in vehicle.Crew.ToList())
                {
                    var employee = Find(id);
                    if (employee != null && !employee.IsAssignedToAnyBusiness()) continue;
                    var name = employee != null ? Name(employee) : null;
                    Remove(vehicle, id, employee == null ? "no longer employed" : "assigned to a building");
                    CartSelling.Say(employee == null ? "foodtrucks:notification_crew_gone" : "foodtrucks:notification_crew_reassigned",
                        KindOf(vehicle), ("name", name ?? LocalizorManager.GetLocalization("foodtrucks:crew_someone", null)));
                }
            }
        }

        // The uninstall step: every crew member back to a plain unassigned employee, routes and transits ended.
        public static void ReleaseAll()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            foreach (var vehicle in state.Vehicles)
            {
                foreach (var id in (vehicle.Crew ?? new List<string>()).ToList()) Remove(vehicle, id, "uninstall");
                // A vehicle on its way goes back where it left from, so the game spawns it there if it stays.
                var transit = vehicle.Transit;
                var instance = transit == null ? null : SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
                if (transit != null && instance != null)
                {
                    instance.position = new UnityEngine.Vector3((float)transit.FromX, (float)transit.FromY, (float)transit.FromZ);
                    instance.rotation = UnityEngine.Quaternion.Euler(0f, (float)transit.FromHeading, 0f);
                    instance.parkingState = (ParkingState)transit.FromParkingState;
                }
                vehicle.Schedule = null;
                vehicle.Shifts?.Clear();
                vehicle.Transit = null;
                vehicle.Arrival = null;
                vehicle.Skipped = null;
                vehicle.HireStop = null;
            }
            FoodTrucksSession.MarkChanged();
        }

        // The route's hours and days stand in for a building's work shifts in the game's job demands.
        public static void SetWeeklyWork(VehicleState vehicle)
        {
            var (hours, days) = HireRunner.WeeklyWork(vehicle.Schedule);
            foreach (var employee in (vehicle.Crew ?? new List<string>()).Select(Find))
            {
                // A building job's hours are the game's to set.
                if (employee == null || employee.IsAssignedToAnyBusiness() || employee.isBeingReplaced) continue;
                employee.assignedWeeklyHours = hours;
                var list = (IList)employee.assignedWeeklyDays;
                list.Clear();
                var type = employee.assignedWeeklyDays.GetType().GetGenericArguments()[0];
                foreach (var day in days)
                {
                    try { list.Add(Enum.Parse(type, day.ToString())); }
                    catch (ArgumentException) { Log.Error($"Crew: the game has no weekday called {day}."); }
                }
            }
        }

        // A crew member isn't idle: the game's "unassigned employee" to-do goes, from the list and the save.
        private static void ClearUnassignedTask(EmployeeInstance employee)
        {
            var tasks = SaveGameManager.Current.TodoTasks.FindAll(t => t != null && t.type == TodoTaskType.EmployeeUnassigned && t.employeeId == employee.id);
            if (tasks.Count == 0) return;
            InstanceBehavior<UI.UIs>.Instance?.tasksUI?.InstantlyCompleteListOfTasks(tasks);
            SaveGameManager.Current.TodoTasks.RemoveAll(t => tasks.Contains(t));
        }

        private static void ClearWeeklyWork(EmployeeInstance employee)
        {
            if (employee.IsAssignedToAnyBusiness()) return;
            employee.assignedWeeklyHours = 0;
            employee.assignedWeeklyDays.Clear();
        }

        public static bool IsCrew(EmployeeInstance? employee)
        {
            var state = FoodTrucksSession.CurrentState;
            return employee != null && state != null && state.Vehicles.Any(v => v.Crew != null && v.Crew.Contains(employee.id));
        }

        // Before the game's hourly employee run, which counts the hour that is starting for anyone on a work
        // shift: the same hour counts for each crew member on shift for at least half of it. The game pays
        // worked hours at midnight, as unassigned wages. [code, unverified in game]
        public static void CountHour()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            // Anyone who left or took a building job since the last check isn't counted here.
            Check(state);
            // The hour that is starting, as the game counts it: its own minute may still hold later hours.
            var hourStart = HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60);
            // Counted from the plans without changing them: making and dropping plans is the routes' job, which may
            // still be catching up on the minutes before this hour. A day not planned yet is previewed.
            var rules = DemandData.Rules;
            var runner = rules == null ? null : new HireRunner(rules.Fleet);
            // Hours whose wage was never paid (a hire let go before midnight) don't stay in the save.
            CrewLog.Prune(state, TimeHelper.CurrentDay - 2);
            foreach (var vehicle in state.Vehicles)
            {
                if (vehicle.Crew == null || vehicle.Crew.Count == 0 || runner == null) continue;
                // With the private driver, it waits in the game's own list, at its saved position.
                var instance = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId)
                               ?? SaveGameManager.Current?.privateDriverVehicleInstances?.FirstOrDefault(v => v.id == vehicle.VehicleId);
                var controller = instance == null ? null : VehicleHelper.GetVehicleController(instance);
                var standing = controller != null ? controller.transform.position
                    : instance != null ? (UnityEngine.Vector3)instance.position : UnityEngine.Vector3.zero;
                var (planX, planZ) = HireRunner.PlanFrom(vehicle, standing.x, standing.z);
                // The game clears these only when a building's shifts change, so they should hold; once an hour is cheap.
                SetWeeklyWork(vehicle);
                if (runner.PlannedShiftMinutes(vehicle, hourStart, hourStart + 60, planX, planZ, HireRoutes.GameSpeed()) < 30) continue;
                foreach (var employee in vehicle.Crew.Select(Find))
                {
                    if (employee == null || employee.isAbsent || employee.trainingSession != null || employee.isBeingReplaced || employee.IsAssignedToAnyBusiness()) continue;
                    employee.workedHoursToday++;
                    // Which vehicle this paid hour was on, for Econoview's crew wages (StreetFoodBooks.WagePostfix).
                    CrewLog.AddHour(state, TimeHelper.CurrentDay, employee.id, vehicle.VehicleId);
                    employee.workedHoursThisWeek++;
                    employee.AddWorkedDay();
                }
            }
        }

        // Absolute game minutes as Core counts them: (day - 1) × 1440 + minute of the day. Inside the game's
        // hourly loop the minute can still hold hours not yet counted, so it is capped at the hour's end.
        public static double NowExact() =>
            HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60) + Math.Min(Math.Max(0f, TimeHelper.CurrentMinute), 59.999f);

        public static long Now() => (long)Math.Floor(NowExact());

        private static string KindOf(VehicleState vehicle) => string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;
    }
}
