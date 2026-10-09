using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    // Which vehicle each hire worked on, hour by hour, so the day's wage goes to the vehicles worked rather than to
    // the crew a hire is on when the game pays at midnight.
    public static class CrewLog
    {
        // Records one worked hour (the game side counts a hire's paid hours one at a time).
        public static void AddHour(SaveState state, int day, string employeeId, string vehicleId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) throw new ArgumentException("Employee id must not be empty.", nameof(employeeId));
            if (string.IsNullOrEmpty(vehicleId)) throw new ArgumentException("Vehicle id must not be empty.", nameof(vehicleId));
            var entry = state.CrewHours.FirstOrDefault(h => h != null && h.Day == day &&
                string.Equals(h.EmployeeId, employeeId, StringComparison.Ordinal) && string.Equals(h.VehicleId, vehicleId, StringComparison.Ordinal));
            if (entry == null)
                state.CrewHours.Add(new CrewHours { Day = day, EmployeeId = employeeId, VehicleId = vehicleId, Hours = 1 });
            else if (entry.Hours < int.MaxValue)
                entry.Hours++;
        }

        // Notes that hours are logged from now on. Called as hours are counted; the first call sets the marker to the
        // next day, since hours counted earlier today (before this version ran) weren't logged.
        public static void Begin(SaveState state, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.CrewHoursSince ??= today + 1;
        }

        // The vehicles a paid wage belongs to. Days covered by the log go by its hours alone: no logged hours
        // means no vehicle's wage (say, hours worked in one of the player's shops before joining a crew).
        // Days from before the log go to the crew the hire is on at payment (crewVehicleId), if any.
        public static List<(string VehicleId, long Cents)> Attribute(SaveState? state, int day, string employeeId, long paidCents,
            double workedHours, string? crewVehicleId)
        {
            var split = Split(state, day, employeeId, paidCents, workedHours);
            if (split.Count > 0 || state == null || paidCents <= 0) return split;
            if (state.CrewHoursSince.HasValue && day >= state.CrewHoursSince.Value) return split;
            if (!string.IsNullOrEmpty(crewVehicleId)) split.Add((crewVehicleId!, paidCents));
            return split;
        }

        public static bool HasHours(SaveState? state, int day, string employeeId) =>
            state != null && state.CrewHours.Any(h => h != null && h.Day == day && string.Equals(h.EmployeeId, employeeId, StringComparison.Ordinal));

        // Splits a paid wage over the vehicles the hire worked that day, by hours, vehicles in ID order. workedHours is the
        // game's paid hours for the day: when the log holds fewer, only the logged hours' share is split and the rest
        // goes to no vehicle (it stays in the game's row). Leftover cents go to the vehicle with the most hours.
        // Empty when nothing was logged.
        public static List<(string VehicleId, long Cents)> Split(SaveState? state, int day, string employeeId, long paidCents, double workedHours)
        {
            var result = new List<(string VehicleId, long Cents)>();
            if (state == null || paidCents <= 0) return result;
            var logged = state.CrewHours
                .Where(h => h != null && h.Day == day && string.Equals(h.EmployeeId, employeeId, StringComparison.Ordinal) && h.Hours > 0)
                .GroupBy(h => h.VehicleId, StringComparer.Ordinal)
                .Select(g => (VehicleId: g.Key, Hours: g.Sum(h => (long)h.Hours)))
                .OrderBy(v => v.VehicleId, StringComparer.Ordinal)
                .ToList();
            if (logged.Count == 0) return result;
            var loggedHours = logged.Sum(v => v.Hours);
            var basis = Math.Max(loggedHours, workedHours);
            var share = basis <= loggedHours ? paidCents : (long)Math.Round(paidCents * (loggedHours / basis));
            long given = 0;
            foreach (var (vehicleId, hours) in logged)
            {
                var cents = (long)Math.Floor((double)share * hours / loggedHours);
                result.Add((vehicleId, cents));
                given += cents;
            }
            if (given < share)
            {
                var most = result.Select((r, i) => (i, logged[i].Hours)).OrderByDescending(p => p.Hours).ThenBy(p => p.i).First().i;
                result[most] = (result[most].VehicleId, result[most].Cents + share - given);
            }
            return result.Where(r => r.Cents > 0).ToList();
        }

        // Drops a hire's hours for a day once that day's wage is paid.
        public static void Consume(SaveState state, int day, string employeeId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.CrewHours.RemoveAll(h => h == null || (h.Day == day && string.Equals(h.EmployeeId, employeeId, StringComparison.Ordinal)));
        }

        // Drops hours from before a day (wages never paid: a hire let go or the game skipped them).
        public static void Prune(SaveState state, int beforeDay)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.CrewHours.RemoveAll(h => h == null || h.Day < beforeDay);
        }
    }
}
