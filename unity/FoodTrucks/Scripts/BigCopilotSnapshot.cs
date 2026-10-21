#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    // Versioned integration called by Big Copilot Link on the main thread.
    // Capture is read-only. Route writes require a separate reviewed quote.
    public static class BigCopilotSnapshot
    {
        private static bool Ready(GameInstance instance) =>
            FoodTrucksSession.IsAttachedTo(instance) && FoodTrucksSession.CanWrite;

        public static string? CaptureV1(GameInstance instance) => Ready(instance)
            ? new SaveCodec().Serialize(FoodTrucksSession.CurrentState!) : null;

        public static bool IsCrewV1(GameInstance instance, string employeeId)
        {
            if (!Ready(instance)) throw new InvalidOperationException("Street Food session is unavailable");
            return FoodTrucksSession.CurrentState!.Vehicles.Any(v => v.Crew.Contains(employeeId));
        }

        public static string? CaptureFactsV1(GameInstance instance)
        {
            if (!Ready(instance)) return null;
            return JsonConvert.SerializeObject(new {
                status = "fresh", gameSpeed = HireRoutes.GameSpeed(),
                routeRevisions = FoodTrucksSession.CurrentState!.Vehicles.ToDictionary(v => v.VehicleId, CopilotRoutes.Revision)
            });
        }

        private static string Refuse(string reason, bool changed = false) => JsonConvert.SerializeObject(new {
            error = changed ? "changed" : "refused", reason, rows = Array.Empty<object>()
        });
        private static bool Number(JToken? token) => token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            && !double.IsNaN((double)token) && !double.IsInfinity((double)token) && Math.Abs((double)token) <= 1000000;

        public static string RouteV1(GameInstance instance, string request, bool dryRun)
        {
            if (!Ready(instance) || DemandData.Rules == null) return Refuse("unavailable");
            JObject body;
            try { body = JObject.Parse(request); }
            catch (JsonException) { return Refuse("invalid_route"); }
            var id = body.Value<string>("vehicleId");
            var vehicle = FoodTrucksSession.CurrentState!.Vehicles.FirstOrDefault(v => v.VehicleId == id);
            var physical = instance.VehicleInstances?.Concat(instance.privateDriverVehicleInstances ?? new List<VehicleInstance>())
                .FirstOrDefault(v => v.id == id && FoodTrucksMod.IsModVehicle(v.vehicleTypeName));
            if (vehicle == null || physical == null) return Refuse("changed", true);
            var revision = CopilotRoutes.Revision(vehicle);
            if (body.Value<string>("revision") != revision) return Refuse("changed", true);
            // An active crew keeps its current stop and journey. Review again when parked and closed.
            if (vehicle.IsOpen || vehicle.Transit != null) return Refuse("vehicle_busy");
            var dayToken = body["weekday"];
            var input = body["stops"] as JArray;
            var rules = DemandData.Rules.Fleet;
            if (dayToken?.Type != JTokenType.Integer || (int)dayToken < 0 || (int)dayToken > 6 || input == null || input.Count > rules.MaxStops)
                return Refuse("invalid_route");
            var day = (DayOfWeek)(((int)dayToken + 1) % 7);
            var stops = new List<ScheduleStop>();
            var kind = FoodTrucksMod.KindOf(physical.vehicleTypeName) ?? "cart";
            foreach (var token in input)
            {
                if (!(token is JObject row) || !Number(row["x"]) || !Number(row["z"]) ||
                    row["open"]?.Type != JTokenType.Integer || row["close"]?.Type != JTokenType.Integer ||
                    (long)row["open"]! < 0 || (long)row["open"]! >= 1440 || (long)row["close"]! > rules.LatestCloseMinute ||
                    (long)row["close"]! <= (long)row["open"]!) return Refuse("invalid_route");
                var x = (double)row["x"]!; var z = (double)row["z"]!;
                // Keep an existing exact place, including its ground height and heading.
                var old = vehicle.Schedule?.Stops.FirstOrDefault(s => Math.Abs(s.X - x) < .0001 && Math.Abs(s.Z - z) < .0001);
                Vector3 at; float heading;
                if (old != null) { at = new Vector3((float)old.X, (float)old.Y, (float)old.Z); heading = (float)old.Heading; }
                else {
                    var found = StopPicker.Snap(kind, new Vector3((float)x, 0, (float)z), out _, out _);
                    if (found == null) return Refuse("no_position");
                    at = found.Value.Position; heading = found.Value.Heading;
                }
                stops.Add(new ScheduleStop { Day = day, SpotKey = StaffDialog.SpotKey(at.x, at.z),
                    X = at.x, Y = at.y, Z = at.z, Heading = heading, OpenAt = (int)row["open"]!, CloseAt = (int)row["close"]! });
            }
            var speed = HireRoutes.GameSpeed();
            var previewVehicle = new VehicleState { VehicleId = vehicle.VehicleId, VehicleType = kind,
                Schedule = vehicle.Schedule, Crew = vehicle.Crew };
            var problem = CopilotRoutes.Preview(previewVehicle, day, stops, rules, speed, out var candidate);
            if (problem != ScheduleProblem.None) return Refuse(problem == ScheduleProblem.Unreachable ? "travel_gap" : "invalid_route");
            var crew = HireCrew.Present(vehicle).Select(e => e.id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
            var home = HireRoutes.HomeOf(vehicle);
            var routeDay = WeekPlan.RouteDayOf(new HireSchedules(rules), candidate, day, Travel.ModeFor(kind), speed,
                HireRoutes.HomePoint(home), home?.BayStops);
            var gaps = WeekPlan.Gaps(routeDay, candidate, crew);
            var warnings = new List<object>();
            if (gaps.Count > 0) warnings.Add(new { code = "crew_gap" });
            if (WeekPlan.IsClosed(candidate, day)) warnings.Add(new { code = "day_closed" });
            var before = (vehicle.Schedule?.Stops ?? new List<ScheduleStop>()).Where(s => s.Day == day).Select(Wire).ToArray();
            var after = candidate.Stops.Where(s => s.Day == day).Select(Wire).ToArray();
            var quote = CopilotRoutes.Digest(JsonConvert.SerializeObject(new { revision, day, after, crew, home, speed, rules }));
            if (!dryRun && body.Value<string>("quote") != quote) return Refuse("changed", true);
            var changed = JsonConvert.SerializeObject(before) != JsonConvert.SerializeObject(after);
            if (!dryRun && changed)
            {
                problem = CopilotRoutes.Apply(vehicle, day, candidate, rules);
                if (problem != ScheduleProblem.None) return Refuse("changed", true);
                // No RouteStops.Changed: that path fits times automatically. Reviewed times stay exact.
                // The route has changed. A follow-up failure must not answer as
                // though nothing happened and invite a blind retry.
                try { HireRunner.Replan(vehicle, HireCrew.Now()); }
                catch (Exception ex) { Log.Error("Big Copilot route replan: " + ex); }
                try { HireCrew.SetWeeklyWork(vehicle); }
                catch (Exception ex) { Log.Error("Big Copilot crew hours refresh: " + ex); }
                try { FoodTrucksSession.MarkChanged(); }
                catch (Exception ex) { Log.Error("Big Copilot route change marker: " + ex); }
            }
            return JsonConvert.SerializeObject(new { ok = true, kind = "streetFoodRoute", dryRun, quote, warnings,
                rows = new[] { new { vehicleId = id, weekday = (int)dayToken, before, after, changed } } });
        }

        private static object Wire(ScheduleStop stop) => new {
            x = stop.X, z = stop.Z, y = stop.Y, heading = stop.Heading, open = stop.OpenAt, close = stop.CloseAt
        };
    }
}
