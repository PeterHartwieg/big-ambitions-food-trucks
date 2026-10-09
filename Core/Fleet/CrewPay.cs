using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    // When a vehicle's crew works, and which of those hours the game pays. The game side counts each paid hour on
    // the game's own counters (HireCrew), so the game's midnight payroll pays it as an unassigned wage.
    //
    // Two kinds of hour are paid. A route hour: the planned shift covers at least half of it, counted as the hour
    // starts (HireCrew.CountHour, as before). An open hour: a window the player opened is paid to the crew who serve
    // it for each hour they serve in the first half of. It is counted as the sales catch up, minute by minute, so a
    // window that sold out or closed before the hour began never earns it. Each hire is paid at most once per hour,
    // whichever comes first and on whichever vehicle, and not at all for an hour the game itself already counted
    // (a shop shift worked at the hour's start before the hire joined a crew).
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

        // Whether serving a window the player opened at this minute (a minute the sales reached with the window still
        // open) earns the hour: the minute is in the hour's first half. Whether the hire has that hour already is the
        // paid-hour marker's to say (Unpaid), never the route's plan.
        public static bool OpenHourDue(VehicleState vehicle, long minute) =>
            PlayerWindow(vehicle) && minute - HourOf(minute) * 60 < HalfHour;

        // Who serves at the window: the best of the crew on shift and the player when at the window, up to the
        // vehicle's staff. Returns the crew among them (they alone are paid for serving) and every skill that serves.
        public static (List<T> Crew, List<double> Skills) Team<T>(IEnumerable<(T Who, double Skill)> crew, double? playerSkill, int limit)
        {
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            var everyone = crew.Select(c => (IsCrew: true, c.Who, c.Skill)).ToList();
            if (playerSkill.HasValue) everyone.Add((false, default!, playerSkill.Value));
            // Ties go to the crew, in their order: the player can step aside.
            var chosen = everyone.Select((p, i) => (p, i)).OrderByDescending(x => x.p.Skill).ThenBy(x => x.p.IsCrew ? 0 : 1).ThenBy(x => x.i)
                .Take(Math.Max(0, limit)).Select(x => x.p).ToList();
            return (chosen.Where(p => p.IsCrew).Select(p => p.Who).ToList(), chosen.Select(p => p.Skill).ToList());
        }

        // Whether the hire's hour is still free: neither the mod nor the game counted it.
        public static bool Unpaid(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return string.IsNullOrEmpty(employeeId) || state.CrewPaidHour == null ||
                   !state.CrewPaidHour.TryGetValue(employeeId, out var last) || last < hour;
        }

        // Marks the hire's hour paid; false (and nothing changes) when it already was. Hours are counted in order,
        // so the last counted hour is all that needs keeping.
        public static bool TryPay(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) throw new ArgumentException("Employee id must not be empty.", nameof(employeeId));
            if (!Unpaid(state, employeeId, hour)) return false;
            state.CrewPaidHour[employeeId] = hour;
            return true;
        }

        // The game counted this hour for the employee itself (a building shift): the mod mustn't count it again.
        public static void GameCounted(SaveState state, string employeeId, long hour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(employeeId)) return;
            if (Unpaid(state, employeeId, hour)) state.CrewPaidHour[employeeId] = hour;
        }

        // An entry before the hour asked about says nothing, so only the last two hours are kept (the sales may still
        // be catching up on the previous one).
        public static void Prune(SaveState state, long currentHour)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            foreach (var id in state.CrewPaidHour.Where(p => p.Value < currentHour - 1).Select(p => p.Key).ToList())
                state.CrewPaidHour.Remove(id);
        }
    }
}
