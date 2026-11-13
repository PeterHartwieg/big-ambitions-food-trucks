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
using UI.Notification;

namespace FoodTrucks
{
    internal static class HireCrew
    {
        internal const string CustomerServiceSkill = "ba:skill_customerservice";

        public static List<EmployeeInstance> Available(SaveState state)
        {
            var taken = new HashSet<string>(state.Vehicles.SelectMany(v => v.Crew ?? new List<string>()), StringComparer.Ordinal);
            return (SaveGameManager.Current?.EmployeeInstances ?? new List<EmployeeInstance>())
                .Where(e => e != null && !e.IsCandidate && !e.isBeingReplaced && !e.IsAssignedToAnyBusiness() && !taken.Contains(e.id))
                .OrderByDescending(Skill)
                .ThenBy(e => e.characterData?.name, StringComparer.Ordinal)
                .ToList();
        }

        public static int Limit(string? kind) =>
            DemandData.Tables?.Vehicle(string.IsNullOrEmpty(kind) ? "cart" : kind!)?.Staff ?? 1;

        public static EmployeeInstance? Find(string id) => EmployeeHelper.GetEmployeeById(id, showError: false);

        public static double Skill(EmployeeInstance employee) => employee.GetSkillValue(CustomerServiceSkill);

        public static double Effective(EmployeeInstance employee) =>
            Core.Rules.ServiceRules.EffectiveSkill(Skill(employee), employee.satisfaction);

        public static string Name(EmployeeInstance employee)
        {
            var name = employee.characterData?.name;
            if (!string.IsNullOrEmpty(name)) knownNames[employee.id] = name!;
            return name ?? employee.id;
        }

        private static readonly Dictionary<string, string> knownNames = new Dictionary<string, string>(StringComparer.Ordinal);

        public static List<EmployeeInstance> OnShift(VehicleState vehicle, long now)
        {
            var ids = new HashSet<string>(CrewPay.OnShiftCrew(vehicle, now), StringComparer.Ordinal);
            return ids.Count == 0 ? new List<EmployeeInstance>() : Present(vehicle).Where(e => ids.Contains(e.id)).ToList();
        }

        public static (List<EmployeeInstance> Crew, List<double> Skills) Team(VehicleState vehicle, long now, double? playerSkill, string? kind = null) =>
            CrewPay.Team(OnShift(vehicle, now).Select(e => (e, Effective(e))), playerSkill, Limit(kind ?? vehicle.VehicleType),
                playerHeld: (kind ?? vehicle.VehicleType) == "truck");

        public static List<EmployeeInstance> Serving(VehicleState vehicle, long now) => Team(vehicle, now, null).Crew;

        public static CrewWish Wish(EmployeeInstance employee)
        {
            foreach (var name in employee.demands ?? new List<string>())
                if (Entities.Employee.JobDemands.JobDemandHelper.GetByName(name) is Entities.Employee.JobDemands.Requirements.HoursWorkingPerWeek hours)
                    return new CrewWish(employee.id, hours.MinHours, hours.MaxHours);
            return new CrewWish(employee.id);
        }

        public static string Names(IEnumerable<EmployeeInstance> employees) => string.Join(", ", employees.Select(Name));

        public static List<EmployeeInstance> Present(VehicleState vehicle) =>
            vehicle.Crew == null ? new List<EmployeeInstance>()
                : vehicle.Crew.Select(Find).Where(e => e != null && !e.isAbsent && e.trainingSession == null && !e.isBeingReplaced).Select(e => e!).ToList();

        public static bool Add(SaveState state, VehicleState vehicle, EmployeeInstance employee)
        {
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
            WeekPlan.DropCrew(vehicle.Schedule, employeeId);
            HireRunner.ReplanShifts(vehicle, Now());
            var employee = Find(employeeId);
            if (employee != null)
            {
                ClearWeeklyWork(employee);
                if (!employee.IsAssignedToAnyBusiness()) employee.AddTodoTask(TodoTaskType.EmployeeUnassigned, invokeQuestAffectingChange: false);
            }
            FoodTrucksSession.MarkChanged();
            Log.Info($"Crew: {employeeId} off {vehicle.VehicleType} {vehicle.VehicleId} ({reason}).");
        }

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
                    vehicle.ReturnPending = false;
                    vehicle.CrewWindowLost = false;
                    continue;
                }
                foreach (var id in vehicle.Crew.ToList())
                {
                    var employee = Find(id);
                    if (employee != null && !employee.IsAssignedToAnyBusiness())
                    {
                        Name(employee);
                        continue;
                    }
                    var name = employee != null ? Name(employee) : knownNames.TryGetValue(id, out var known) ? known : null;
                    Remove(vehicle, id, employee == null ? "no longer employed" : "assigned to a building");
                    var vehicleName = StreetFoodBooks.VehicleNames().TryGetValue(vehicle.VehicleId, out var named)
                        ? named : LocalizorManager.GetLocalization("foodtrucks:noun_" + KindOf(vehicle), null);
                    Notices.Show(NotificationType.Warning, employee == null ? "foodtrucks:notification_crew_gone" : "foodtrucks:notification_crew_reassigned",
                        new Dictionary<string, string>
                        {
                            { "name", name ?? LocalizorManager.GetLocalization("foodtrucks:crew_someone", null) },
                            { "vehiclename", vehicleName },
                            { "vehicle", LocalizorManager.GetLocalization("foodtrucks:noun_" + KindOf(vehicle), null) }
                        }, 10f);
                }
            }
        }

        public static void ReleaseAll()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            foreach (var vehicle in state.Vehicles)
            {
                foreach (var id in (vehicle.Crew ?? new List<string>()).ToList()) Remove(vehicle, id, "uninstall");
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
                vehicle.ReturnPending = false;
                vehicle.CrewWindowLost = false;
            }
            FoodTrucksSession.MarkChanged();
        }

        public static void SetWeeklyWork(VehicleState vehicle)
        {
            foreach (var employee in (vehicle.Crew ?? new List<string>()).Select(Find))
            {
                if (employee == null || employee.IsAssignedToAnyBusiness() || employee.isBeingReplaced) continue;
                var (hours, days) = WeekPlan.WeeklyWork(vehicle.Schedule, employee.id);
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

        public static void CountHour()
        {
            gameCountBefore = null;
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            Check(state);
            var hourStart = HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60);
            CrewLog.Prune(state, TimeHelper.CurrentDay - 2);
            CrewLog.Begin(state, TimeHelper.CurrentDay);
            var hour = CrewPay.HourOf(hourStart);
            CrewPay.Prune(state, hour);
            CloseHours(state, hourStart, TimeHelper.CurrentDay);
            foreach (var vehicle in state.Vehicles)
            {
                if (vehicle.Crew == null || vehicle.Crew.Count == 0) continue;
                SetWeeklyWork(vehicle);
                foreach (var employee in Present(vehicle))
                    if (!employee.IsAssignedToAnyBusiness() && WeekPlan.OnShift(vehicle.Schedule, employee.id, hourStart) &&
                        CrewPay.TryPay(state, employee.id, hour))
                        CountOn(state, vehicle.VehicleId, employee, TimeHelper.CurrentDay);
            }
            gameCountHour = hour;
            gameCountBefore = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var employee in SaveGameManager.Current?.EmployeeInstances ?? new List<EmployeeInstance>())
                if (employee != null && !string.IsNullOrEmpty(employee.id)) gameCountBefore[employee.id] = employee.workedHoursToday;
        }

        private static Dictionary<string, int>? gameCountBefore;
        private static long gameCountHour;

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

        public static void BeforePayroll()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            var midnight = HireSchedules.Absolute(TimeHelper.CurrentDay, 0);
            CartSelling.CatchUpDay(midnight);
            CloseHours(state, midnight, TimeHelper.CurrentDay - 1);
        }

        private static void CloseHours(SaveState state, long boundary, int payrollDay)
        {
            CartSelling.CatchUpTo(boundary);
            Settle(state, CrewPay.HourOf(boundary), payrollDay);
        }

        private static void Settle(SaveState state, long beforeHour, int payrollDay, string? onlyVehicle = null)
        {
            CrewPay.CloseHours(state, beforeHour, (employeeId, vehicleId, hour) =>
            {
                var employee = Find(employeeId);
                if (employee == null) return false;
                if (employee.IsAssignedToAnyBusiness() || employee.isBeingReplaced) return SettleLeaving(employee, "found at the hour's end");
                CountOn(state, vehicleId, employee, payrollDay);
                Log.Info($"Crew: {Name(employee)} worked {hour % 24:D2}:00 on day {CrewPay.DayOfHour(hour)} (open window, {vehicleId}); counted for the game's midnight wage.");
                return true;
            }, onlyVehicle);
            FoodTrucksSession.MarkChanged();
        }

        public static bool SettleLeaving(EmployeeInstance employee, string why)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || employee == null || string.IsNullOrEmpty(employee.id) || SaveGameManager.Current == null)
                return false;
            if (!IsCrew(employee) && !state.CrewServedHour.ContainsKey(employee.id) && !CrewLog.HasHours(state, TimeHelper.CurrentDay, employee.id))
                return true;
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

        public static void SettleAll()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            CartSelling.CatchUpTo(CatchUpLimit());
            Settle(state, long.MaxValue, TimeHelper.CurrentDay);
        }

        internal static void SettleVehicle(string vehicleId)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            CartSelling.CatchUpTo(CatchUpLimit());
            Settle(state, long.MaxValue, TimeHelper.CurrentDay, vehicleId);
        }

        private static void CountOn(SaveState state, string vehicleId, EmployeeInstance employee, int payrollDay)
        {
            employee.workedHoursToday++;
            CrewLog.AddHour(state, payrollDay, employee.id, vehicleId);
            employee.workedHoursThisWeek++;
            WorkedOn(employee, payrollDay);
            FoodTrucksSession.MarkChanged();
        }

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

        internal static int InGameTick;

        private static double CatchUpLimit() =>
            CrewPay.CatchUpLimit(InGameTick > 0, HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60), NowExact());

        public static double NowExact() =>
            HireSchedules.Absolute(TimeHelper.CurrentDay, TimeHelper.CurrentHour * 60) + Math.Min(Math.Max(0f, TimeHelper.CurrentMinute), 59.999f);

        public static long Now() => (long)Math.Floor(NowExact());

        private static string KindOf(VehicleState vehicle) => string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;
    }
}
