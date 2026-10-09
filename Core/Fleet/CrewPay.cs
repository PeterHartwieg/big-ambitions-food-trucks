using System;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    // When a vehicle's crew works, and which of those hours the game pays. The game side counts each paid hour on
    // the game's own counters (HireCrew.CountHour), so the game's midnight payroll pays it as an unassigned wage.
    //
    // Two kinds of hour are paid. A route hour: the planned shift covers at least half of it, counted as the hour
    // starts (as before). An open hour: a window the player opened, which the crew serves at whatever place or time,
    // is paid for each hour the crew serves in the first half of: open as the hour starts, or opened (or joined)
    // before its half. Each hire is paid at most once per hour, whichever comes first and on whichever vehicle.
    public static class CrewPay
    {
        public const int HalfHour = 30;

        // The game hour a minute falls in: (day - 1) × 24 + hour.
        public static long HourOf(long minute) => (long)Math.Floor(minute / 60.0);

        // Whether the crew is at the vehicle's window at that minute (before anyone's absence): at a window that is
        // open, whoever opened it, or during the route's hours. Not while it is on its way somewhere.
        public static bool OnShift(VehicleState vehicle, long minute)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.Crew == null || vehicle.Crew.Count == 0 || vehicle.Transit != null) return false;
            return vehicle.IsOpen || HireRunner.Want(vehicle, minute) != null;
        }

        // A window the player opened: the route leaves it alone and doesn't pay for it.
        public static bool PlayerWindow(VehicleState vehicle) =>
            vehicle != null && vehicle.IsOpen && vehicle.HireStop == null && vehicle.Transit == null &&
            vehicle.Crew != null && vehicle.Crew.Count > 0;

        // Whether serving at a window the player opened earns the crew the hour of this minute: it is in the hour's
        // first half and the route doesn't pay that hour already. routeMinutes is the route's shift minutes in that
        // hour (HireRunner.ShiftMinutes or PlannedShiftMinutes).
        public static bool OpenHourDue(VehicleState vehicle, long minute, long routeMinutes) =>
            PlayerWindow(vehicle) && minute - HourOf(minute) * 60 < HalfHour && routeMinutes < HalfHour;

        // Whether the hire's hour is still unpaid.
        public static bool Unpaid(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return string.IsNullOrEmpty(employeeId) || state.CrewPaidHour == null ||
                   !state.CrewPaidHour.TryGetValue(employeeId, out var last) || last < hour;
        }

        // Marks the hire's hour paid; false (and nothing changes) when it already was. Hours are counted in order,
        // so the last paid hour is all that needs keeping.
        public static bool TryPay(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) throw new ArgumentException("Employee id must not be empty.", nameof(employeeId));
            if (!Unpaid(state, employeeId, hour)) return false;
            state.CrewPaidHour[employeeId] = hour;
            return true;
        }

        // Forgets hires whose last paid hour is long past (let go, or off the crew).
        public static void Prune(SaveState state, long beforeHour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            foreach (var id in state.CrewPaidHour.Where(p => p.Value < beforeHour).Select(p => p.Key).ToList())
                state.CrewPaidHour.Remove(id);
        }
    }
}
