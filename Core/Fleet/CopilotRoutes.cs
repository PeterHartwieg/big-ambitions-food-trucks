using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json;

namespace FoodTrucks.Core.Fleet
{
    // A reviewed day replacement. This never fits times, hires crew or opens a
    // closed weekday. The adapter resolves legal positions before calling it.
    public static class CopilotRoutes
    {
        public static string Digest(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        public static string Revision(VehicleState vehicle) => Digest(JsonConvert.SerializeObject(new {
            vehicle.VehicleId, vehicle.VehicleType, vehicle.Schedule, vehicle.Crew
        }));

        public static ScheduleProblem Preview(VehicleState vehicle, DayOfWeek day, IReadOnlyList<ScheduleStop> stops,
            FleetRules rules, double speed, out HireSchedule candidate)
        {
            candidate = new HireSchedule();
            if ((int)day < 0 || (int)day > 6 || stops == null || stops.Count > rules.MaxStops)
                return ScheduleProblem.TooManyStops;
            var old = vehicle.Schedule ?? new HireSchedule();
            candidate = new HireSchedule {
                Stops = WeekPlan.Ordered((old.Stops ?? new List<ScheduleStop>()).Where(s => s.Day != day)
                    .Concat(stops.Select(s => WeekPlan.CopyStop(s, day)))),
                Closed = old.Closed?.ToList() ?? new List<DayOfWeek>(),
                CrewShifts = old.CrewShifts?.ToList() ?? new List<CrewShift>()
            };
            // Check the player's order before the week is grouped for the mod.
            for (var i = 1; i < stops.Count; i++)
            {
                var a = stops[i - 1]; var b = stops[i];
                if (b.OpenAt < a.CloseAt) return ScheduleProblem.Overlap;
                if (b.OpenAt - a.CloseAt < Travel.Between(rules, a.X, a.Z, b.X, b.Z, Travel.ModeFor(vehicle.VehicleType), speed))
                    return ScheduleProblem.Unreachable;
            }
            return candidate.Stops.Count == 0 ? ScheduleProblem.None : new HireSchedules(rules).Validate(candidate);
        }

        public static ScheduleProblem Apply(VehicleState vehicle, DayOfWeek day, HireSchedule candidate, FleetRules rules)
        {
            var copy = RouteEditor.CopyDay(candidate, day);
            return RouteEditor.PasteRoute(new HireSchedules(rules), vehicle, copy, day);
        }
    }
}
