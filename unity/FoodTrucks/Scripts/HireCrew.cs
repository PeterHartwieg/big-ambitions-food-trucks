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
    // keeps the employee IDs on the vehicle, logs their route hours and the hours they serve a window the player
    // opened, so the game's midnight payroll pays them (as unassigned wages, tax-deductible), sets their weekly hours
    // and days from the route so their job demands can be met, and keeps the "no task" complaint from firing.
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

        // How many work the window at once: the vehicle's staff in economy.json (cart 1, truck 2).
        public static int Limit(string? kind) =>
            DemandData.Tables?.Vehicle(string.IsNullOrEmpty(kind) ? "cart" : kind!)?.Staff ?? 1;

        public static EmployeeInstance? Find(string id) => EmployeeHelper.GetEmployeeById(id, showError: false);

        public static double Skill(EmployeeInstance employee) => employee.GetSkillValue(CustomerServiceSkill);

        // The game's measure for a worker: skill × satisfaction / 100.
        public static double Effective(EmployeeInstance employee) =>
            Core.Rules.ServiceRules.EffectiveSkill(Skill(employee), employee.satisfaction);

        public static string Name(EmployeeInstance employee) => employee.characterData?.name ?? employee.id;

        // The crew on the vehicle's window now, minus anyone off sick or training: at an open window, whoever opened it
        // and wherever (CrewPay.OnShift), and during the route's hours. Someone a headhunter is replacing is on the
        // way out: the game stops paying them, so they don't serve.
        public static List<EmployeeInstance> OnShift(VehicleState vehicle, long now) =>
            CrewPay.OnShift(vehicle, now) ? Present(vehicle) : new List<EmployeeInstance>();

        // Who serves at the window: the best of the crew on shift and the player (their skill, when at the window), up
        // to the vehicle's staff. Only the crew chosen here serve, and only they are paid for serving.
        public static (List<EmployeeInstance> Crew, List<double> Skills) Team(VehicleState vehicle, long now, double? playerSkill, string? kind = null) =>
            CrewPay.Team(OnShift(vehicle, now).Select(e => (e, Effective(e))), playerSkill, Limit(kind ?? vehicle.VehicleType),
                playerHeld: (kind ?? vehicle.VehicleType) == "truck");

        // Who serves at the window with the player away.
        public static List<EmployeeInstance> Serving(VehicleState vehicle, long now) => Team(vehicle, now, null).Crew;

        public static string Names(IEnumerable<EmployeeInstance> employees) => string.Join(", ", employees.Select(Name));

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
        // shift. First the hour that just ended is closed: the sales are caught up to this hour's start and each hire
        // who served a window the player opened in that hour's first half is paid it (CrewPay.CloseHours). Then the
        // hour that is starting counts for each crew member on route shift for at least half of it. The game pays
        // worked hours at midnight, as unassigned wages. [code, unverified in game]
        public static void CountHour()
        {
            gameCountBefore = null;
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
            CrewLog.Begin(state, TimeHelper.CurrentDay);
            var hour = CrewPay.HourOf(hourStart);
            CrewPay.Prune(state, hour);
            CloseHours(state, hourStart, TimeHelper.CurrentDay);
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
                // The same drive back to the bay as the route runs (HireRoutes.HomeOf): paid like any other drive.
                var home = HireRoutes.HomeOf(vehicle);
                if (runner.PlannedShiftMinutes(vehicle, hourStart, hourStart + 60, planX, planZ, HireRoutes.GameSpeed(),
                        HireRoutes.HomePoint(home), home?.EndsThere == true) < CrewPay.HalfHour) continue;
                foreach (var employee in Present(vehicle))
                    if (!employee.IsAssignedToAnyBusiness() && CrewPay.TryPay(state, employee.id, hour))
                        CountOn(state, vehicle.VehicleId, employee, TimeHelper.CurrentDay);
            }
            // What every employee has worked today before the game's own count, to see whose hour the game counts.
            gameCountHour = hour;
            gameCountBefore = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var employee in SaveGameManager.Current?.EmployeeInstances ?? new List<EmployeeInstance>())
                if (employee != null && !string.IsNullOrEmpty(employee.id)) gameCountBefore[employee.id] = employee.workedHoursToday;
        }

        private static Dictionary<string, int>? gameCountBefore;
        private static long gameCountHour;

        // After the game's hourly employee run: an employee whose worked hours went up was counted by the game for
        // this hour (a building shift, EmployeeInstance.RunHourly). That hour is the game's: if they join a crew
        // later in it, serving doesn't count it again. Kept in the save with the mod's own paid hours.
        public static void AfterGameCount()
        {
            var before = gameCountBefore;
            gameCountBefore = null;
            var state = FoodTrucksSession.CurrentState;
            if (before == null || state == null || !FoodTrucksSession.CanWrite) return;
            var changed = false;
            foreach (var employee in SaveGameManager.Current?.EmployeeInstances ?? new List<EmployeeInstance>())
            {
                if (employee == null || !before.TryGetValue(employee.id ?? "", out var worked) || employee.workedHoursToday <= worked) continue;
                if (CrewPay.Unpaid(state, employee.id, gameCountHour)) changed = true;
                CrewPay.GameCounted(state, employee.id, gameCountHour);
            }
            if (changed) FoodTrucksSession.MarkChanged();
        }

        // The crew serving a window the player opened, noted by the sales as they reach each minute with the window
        // open (CartSelling.Tick). Nothing is paid here: the hour is decided when it is over (CloseHours).
        public static void Served(SaveState state, VehicleState vehicle, IEnumerable<EmployeeInstance> serving, long minute)
        {
            if (!FoodTrucksSession.CanWrite || !CrewPay.OpenHourDue(vehicle, minute)) return;
            foreach (var employee in serving)
            {
                if (!CrewPay.Unpaid(state, employee.id, CrewPay.HourOf(minute))) continue;
                CrewPay.Served(state, employee.id, vehicle.VehicleId, minute);
                FoodTrucksSession.MarkChanged();
            }
        }

        // Before the game's midnight payroll (EmployeeHelper.PayDailyWages, in GameManager.NewDay after the day has
        // turned): the sales are caught up to midnight and the day's last hour is closed into the closing day, so its
        // wage is paid with that day's and its hour is logged on it.
        public static void BeforePayroll()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            var midnight = HireSchedules.Absolute(TimeHelper.CurrentDay, 0);
            // Routes and windows sell up to midnight first, so the closing day's sales are all made (CartSelling.CatchUpDay).
            CartSelling.CatchUpDay(midnight);
            CloseHours(state, midnight, TimeHelper.CurrentDay - 1);
        }

        // Catches the sales up to `boundary` (an hour's start) and pays each hire the earlier hours they earned at a
        // window the player opened, onto the counters of payrollDay (the day whose midnight payroll pays them).
        private static void CloseHours(SaveState state, long boundary, int payrollDay)
        {
            CartSelling.CatchUpTo(boundary);
            Settle(state, CrewPay.HourOf(boundary), payrollDay);
        }

        // Credits each earned hour before `beforeHour` onto the game's counters, for hires still unassigned. One whose
        // employment changed without the mod seeing it (SettleLeaving normally runs first) is paid on the spot instead.
        // Only one no longer employed can't be paid; their note waits until it is a day old. An hour the game counted
        // itself is never credited again (CrewPay.Unpaid).
        private static void Settle(SaveState state, long beforeHour, int payrollDay)
        {
            CrewPay.CloseHours(state, beforeHour, (employeeId, vehicleId, hour) =>
            {
                var employee = Find(employeeId);
                if (employee == null) return false;
                if (employee.IsAssignedToAnyBusiness() || employee.isBeingReplaced) return SettleLeaving(employee, "found at the hour's end");
                CountOn(state, vehicleId, employee, payrollDay);
                // The hour goes onto the game's counters; the game's midnight payroll pays it, once, as the unassigned
                // wage (EmployeeInstance.PayWage: workedHoursToday x hourly wage). The line says so, as "paid" read as a
                // second payment beside the game's "Unassigned Daily Wage" (live session 9).
                Log.Info($"Crew: {Name(employee)} worked {hour % 24:D2}:00 on day {CrewPay.DayOfHour(hour)} (open window, {vehicleId}); counted for the game's midnight wage.");
                return true;
            });
            FoodTrucksSession.MarkChanged();
        }

        // Before the game changes a hire's employment (a building or warehouse job, set to be replaced, resigning,
        // fired or retired; the Harmony prefixes in Patches run before the game's own payout): everything the mod owes
        // them is paid on the spot as an unassigned wage at their hourly wage, the hours on today's counters for
        // vehicles and an earned hour not yet decided, the running one included. Each vehicle's share goes into its
        // crew wages. The hours on the counters come off them and the markers are stamped, so neither the game's
        // payout nor the next decision pays them again. False when nothing could be paid (and so nothing changed).
        public static bool SettleLeaving(EmployeeInstance employee, string why)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || employee == null || string.IsNullOrEmpty(employee.id) || SaveGameManager.Current == null)
                return false;
            if (!IsCrew(employee) && !state.CrewServedHour.ContainsKey(employee.id) && !CrewLog.HasHours(state, TimeHelper.CurrentDay, employee.id))
                return true;
            // An hour served so far counts only once the sales have reached it.
            if (IsCrew(employee)) CartSelling.CatchUpTo(CatchUpLimit());
            var today = TimeHelper.CurrentDay;
            var owed = CrewPay.Owed(state, employee.id, today);
            if (owed.Hours == 0)
            {
                CrewPay.Settled(state, employee.id, today, owed);
                return true;
            }
            var shares = owed.ByVehicle.Select(v => (v.VehicleId, Cents: (long)Math.Round(v.Hours * (double)employee.hourlyWage * 100.0))).ToList();
            var cents = shares.Sum(v => v.Cents);
            if (cents > 0)
            {
                var info = new TransactionInfo(UnassignedWage, "ba:transactioncategory_salaryexpenses",
                    new Dictionary<string, string> { { "employee", Name(employee) }, { "businessName", "Unassigned" } });
                info.SetTaxDeductibleName("econoview_row_salaries");
                if (!Payments.PayOut(cents, info, today))
                {
                    Log.Error($"Crew: {Name(employee)}'s {owed.Hours} owed hours ({why}) couldn't be paid; left for later.");
                    return false;
                }
            }
            employee.workedHoursToday = Math.Max(0, employee.workedHoursToday - owed.OnCounters);
            if (owed.PendingHour.HasValue)
            {
                employee.workedHoursThisWeek++;
                WorkedOn(employee, today);
            }
            CrewPay.Settled(state, employee.id, today, owed);
            foreach (var (vehicleId, share) in shares.Where(v => v.Cents > 0))
                if (!SalesLedger.RecordGameCost(state, today, vehicleId, LedgerLine.Wages, share))
                    Log.Info($"Econoview: wage of {CartSelling.Money(share)} for {Name(employee)} on day {today} came after that day's summary; Econoview leaves it out.");
            FoodTrucksSession.MarkChanged();
            Log.Info($"Crew: {Name(employee)} paid {CartSelling.Money(cents)} for {owed.Hours} hours on leaving the crew's pay ({why}): " +
                     string.Join(", ", owed.ByVehicle.Select(v => $"{v.Hours} on {v.VehicleId}")) + ".");
            return true;
        }

        private const string UnassignedWage = "ba:transaction_unassignedwage";

        // The uninstall step, before the crew is released: the sales are caught up to now and every hour the crew has
        // earned so far, the running one included, is credited onto today's counters, so it is paid at midnight even
        // with the mod gone. The paid-hour markers stay, so carrying on playing doesn't pay them again.
        public static void SettleAll()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            CartSelling.CatchUpTo(CatchUpLimit());
            Settle(state, long.MaxValue, TimeHelper.CurrentDay);
        }

        // One paid hour on the game's counters, so payrollDay's midnight payroll pays it, logged with its vehicle for
        // Econoview's crew wages (StreetFoodBooks.WagePostfix). The caller has marked the hour paid (CrewPay).
        private static void CountOn(SaveState state, string vehicleId, EmployeeInstance employee, int payrollDay)
        {
            employee.workedHoursToday++;
            CrewLog.AddHour(state, payrollDay, employee.id, vehicleId);
            employee.workedHoursThisWeek++;
            WorkedOn(employee, payrollDay);
            FoodTrucksSession.MarkChanged();
        }

        // The game's AddWorkedDay counts today; an hour closed at midnight belongs to the day that just ended.
        private static void WorkedOn(EmployeeInstance employee, int day)
        {
            if (day == SaveGameManager.Current.Day)
            {
                employee.AddWorkedDay();
                return;
            }
            if (lastWorkedDay == null) return;
            if ((int)lastWorkedDay.GetValue(employee) == day) return;
            lastWorkedDay.SetValue(employee, day);
            employee.workedDays++;
        }

        private static readonly System.Reflection.FieldInfo? lastWorkedDay =
            typeof(EmployeeInstance).GetField("lastWorkedDay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Inside GameManager.RunMainGameTick (bracketed by Patches), where the hour's and day's events run with the
        // clock's minute already holding the frame's whole advance.
        internal static int InGameTick;

        // How far a settle may catch the sales up: the start of the hour being processed inside the game's tick, the
        // live minute otherwise (CrewPay.CatchUpLimit).
        private static double CatchUpLimit() =>
            CrewPay.CatchUpLimit(InGameTick > 0, HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60), NowExact());

        // Absolute game minutes as Core counts them: (day - 1) × 1440 + minute of the day. Inside the game's
        // hourly loop the minute can still hold hours not yet counted, so it is capped at the hour's end.
        public static double NowExact() =>
            HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60) + Math.Min(Math.Max(0f, TimeHelper.CurrentMinute), 59.999f);

        public static long Now() => (long)Math.Floor(NowExact());

        private static string KindOf(VehicleState vehicle) => string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;
    }
}
