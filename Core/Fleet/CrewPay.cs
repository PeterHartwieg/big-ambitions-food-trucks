using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    public static class CrewPay
    {
        public const int HalfHour = 30;

        public static long HourOf(long minute) => (long)Math.Floor(minute / 60.0);

        public static bool OnShift(VehicleState vehicle, long minute) => OnShiftCrew(vehicle, minute).Count > 0;

        public static List<string> OnShiftCrew(VehicleState vehicle, long minute)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.Crew == null || vehicle.Crew.Count == 0 || vehicle.Transit != null) return new List<string>();
            return WeekPlan.OnShiftCrew(vehicle, minute);
        }

        public static bool PlayerWindow(VehicleState vehicle) =>
            vehicle != null && vehicle.IsOpen && vehicle.HireStop == null && !vehicle.CrewWindowLost && vehicle.Transit == null &&
            vehicle.Crew != null && vehicle.Crew.Count > 0;

        public static bool OpenHourDue(VehicleState vehicle, long minute) =>
            PlayerWindow(vehicle) && minute - HourOf(minute) * 60 < HalfHour;

        public static (List<T> Crew, List<double> Skills) Team<T>(IEnumerable<(T Who, double Skill)> crew, double? playerSkill, int limit,
            bool playerHeld = false)
        {
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            var places = Math.Max(0, limit);
            if (playerHeld && playerSkill.HasValue && places > 0)
            {
                var hands = crew.Select((c, i) => (c, i)).OrderByDescending(x => x.c.Skill).ThenBy(x => x.i)
                    .Take(places - 1).Select(x => x.c).ToList();
                return (hands.Select(h => h.Who).ToList(), hands.Select(h => h.Skill).Append(playerSkill.Value).ToList());
            }
            var everyone = crew.Select(c => (IsCrew: true, c.Who, c.Skill)).ToList();
            if (playerSkill.HasValue) everyone.Add((false, default!, playerSkill.Value));
            var chosen = everyone.Select((p, i) => (p, i)).OrderByDescending(x => x.p.Skill).ThenBy(x => x.p.IsCrew ? 0 : 1).ThenBy(x => x.i)
                .Take(places).Select(x => x.p).ToList();
            return (chosen.Where(p => p.IsCrew).Select(p => p.Who).ToList(), chosen.Select(p => p.Skill).ToList());
        }

        public static double CatchUpLimit(bool inGameTick, long hourBeingProcessedStart, double liveMinute) =>
            inGameTick ? Math.Min(hourBeingProcessedStart, liveMinute) : liveMinute;

        public static int DayOfHour(long hour) => (int)Math.Floor(hour / 24.0) + 1;

        public static void Served(SaveState state, string employeeId, string vehicleId, long minute)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId) || string.IsNullOrEmpty(vehicleId)) return;
            var hour = HourOf(minute);
            if (minute - hour * 60 >= HalfHour) return;
            if (state.CrewServedHour.TryGetValue(employeeId, out var served) && served != null && served.Hour == hour) return;
            state.CrewServedHour[employeeId] = new CrewServed { Hour = hour, VehicleId = vehicleId };
        }

        public static List<(string EmployeeId, string VehicleId, long Hour)> CloseHours(SaveState state, long beforeHour,
            Func<string, string, long, bool> credit, string? vehicleId = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (credit == null) throw new ArgumentNullException(nameof(credit));
            foreach (var id in state.CrewServedHour.Where(p => p.Value == null).Select(p => p.Key).ToList()) state.CrewServedHour.Remove(id);
            var due = state.CrewServedHour.Where(p => p.Value.Hour < beforeHour && (vehicleId == null || p.Value.VehicleId == vehicleId))
                .OrderBy(p => p.Value.Hour).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
            var paid = new List<(string EmployeeId, string VehicleId, long Hour)>();
            foreach (var pair in due)
            {
                var (id, served) = (pair.Key, pair.Value);
                if (!Unpaid(state, id, served.Hour))
                {
                    state.CrewServedHour.Remove(id);
                    continue;
                }
                if (!credit(id, served.VehicleId, served.Hour)) continue;
                TryPay(state, id, served.Hour);
                state.CrewServedHour.Remove(id);
                paid.Add((id, served.VehicleId, served.Hour));
            }
            return paid;
        }

        public static CrewOwed Owed(SaveState state, string employeeId, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var byVehicle = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var onCounters = 0;
            foreach (var entry in state.CrewHours.Where(h => h != null && h.Day == today && h.Hours > 0 &&
                         string.Equals(h.EmployeeId, employeeId, StringComparison.Ordinal)))
            {
                byVehicle.TryGetValue(entry.VehicleId, out var hours);
                byVehicle[entry.VehicleId] = hours + entry.Hours;
                onCounters += entry.Hours;
            }
            long? pending = null;
            if (!string.IsNullOrEmpty(employeeId) && state.CrewServedHour.TryGetValue(employeeId, out var served) && served != null &&
                Unpaid(state, employeeId, served.Hour))
            {
                pending = served.Hour;
                byVehicle.TryGetValue(served.VehicleId, out var hours);
                byVehicle[served.VehicleId] = hours + 1;
            }
            return new CrewOwed(byVehicle.Select(p => (p.Key, p.Value)).ToList(), onCounters, pending);
        }

        public static void Settled(SaveState state, string employeeId, int today, CrewOwed owed)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) return;
            CrewLog.Consume(state, today, employeeId);
            if (owed.PendingHour.HasValue) TryPay(state, employeeId, owed.PendingHour.Value);
            if (state.CrewServedHour.TryGetValue(employeeId, out var served) && (served == null || !Unpaid(state, employeeId, served.Hour)))
                state.CrewServedHour.Remove(employeeId);
        }

        public static bool Unpaid(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return string.IsNullOrEmpty(employeeId) || state.CrewPaidHour == null ||
                   !state.CrewPaidHour.TryGetValue(employeeId, out var last) || last < hour;
        }

        public static bool TryPay(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) throw new ArgumentException("Employee id must not be empty.", nameof(employeeId));
            if (!Unpaid(state, employeeId, hour)) return false;
            state.CrewPaidHour[employeeId] = hour;
            return true;
        }

        public static void GameCounted(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) return;
            if (Unpaid(state, employeeId, hour)) state.CrewPaidHour[employeeId] = hour;
        }

        public static void Prune(SaveState state, long currentHour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            foreach (var id in state.CrewPaidHour.Where(p => p.Value < currentHour - KeepHours).Select(p => p.Key).ToList())
                state.CrewPaidHour.Remove(id);
            foreach (var id in state.CrewServedHour.Where(p => p.Value == null || p.Value.Hour < currentHour - KeepHours).Select(p => p.Key).ToList())
                state.CrewServedHour.Remove(id);
        }

        public const int KeepHours = 24;
    }

    public readonly struct CrewOwed
    {
        public CrewOwed(List<(string VehicleId, int Hours)> byVehicle, int onCounters, long? pendingHour)
        {
            ByVehicle = byVehicle;
            OnCounters = onCounters;
            PendingHour = pendingHour;
        }

        public List<(string VehicleId, int Hours)> ByVehicle { get; }
        public int OnCounters { get; }
        public long? PendingHour { get; }
        public int Hours => ByVehicle?.Sum(v => v.Hours) ?? 0;
    }
}
