#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;

namespace FoodTrucks
{
    // The bridge's commands for the AI playthrough (docs/ai-playtest-plan.md, tools/playthrough): the mod's day totals
    // as data, and routes set by address in place of the map pick.
    internal static partial class TestBridgeCommands
    {
        // ---- day_summary ----

        // { "day"?: N }: Core's DaySummary for that day (default today), the same per-vehicle ledger Econoview and the
        // fleet page read, with each vehicle's cargo now. Amounts in dollars. Reads only.
        private static JToken DaySummaryCommand(JObject args)
        {
            var game = SaveGameManager.Current;
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            var today = game.Day;
            var asked = Num(args, "day");
            var day = asked == null ? today : (int)asked.Value;
            if (asked != null && (asked.Value != Math.Floor(asked.Value) || day < 1 || day > today))
                throw new BridgeError($"day is a whole day number from 1 to today ({today}).");
            var report = DaySummary.For(state, day, today);
            double D(long cents) => cents / 100.0;
            var vehicles = new JArray();
            foreach (var v in report.Vehicles)
            {
                var instance = game.VehicleInstances.FirstOrDefault(i => i != null && i.id == v.VehicleId);
                vehicles.Add(new JObject
                {
                    ["id"] = v.VehicleId,
                    ["kind"] = string.IsNullOrEmpty(v.VehicleType) ? FoodTrucksMod.KindOf(instance?.vehicleTypeName) : v.VehicleType,
                    ["sales"] = D(v.SalesCents), ["catering"] = D(v.CateringCents), ["stock_cost"] = D(v.StockCostCents),
                    ["wages"] = D(v.WagesCents), ["parking"] = D(v.ParkingCents), ["permit"] = D(v.PermitCents),
                    ["commissary_rent"] = D(v.CommissaryRentCents), ["overnight_fines"] = D(v.OvernightFineCents),
                    ["profit"] = D(v.ProfitCents),
                    ["sold"] = v.Sold == null ? null : new JArray(v.Sold.Select(s => new JObject { ["item"] = s.Item, ["units"] = s.Units })),
                    ["stops_served"] = v.StopsServed, ["minutes_open"] = v.MinutesOpen,
                    ["stock_now"] = instance == null ? null : new JArray(instance.cargoInstances
                        .Where(c => c != null && c.amount > 0 && FoodTrucksMod.KindOf(instance.vehicleTypeName) != null)
                        .GroupBy(c => c.itemName)
                        .Select(g => new JObject { ["key"] = g.Key, ["amount"] = g.Sum(c => c.amount) })),
                });
            }
            return new JObject
            {
                ["day"] = day, ["today"] = today, ["in_summary"] = report.InSummary,
                ["fleet"] = new JObject
                {
                    ["revenue"] = D(report.RevenueCents), ["stock_cost"] = D(report.StockCostCents), ["licence"] = D(report.LicenceCents),
                    ["event_places"] = D(report.EventPlacesCents), ["delivery_fees"] = D(report.DeliveryFeesCents), ["profit"] = D(report.ProfitCents),
                },
                ["fees_paid_at"] = new JArray(report.FeesPaidAt),
                ["vehicles"] = vehicles,
            };
        }

        // ---- route ----

        // { "op": "add_stop", "vehicle", "address", "days"?: ["monday", …] | "all", "open"?: "HH:MM", "close"?: "HH:MM" }: the
        // map pick's spot by that building (StopPicker.Snap from its first entrance) added to each day, at the given hours or
        // the default ones (RouteStops.AddWithDefaultTimes, as the map pick). { "op": "paste_day", "vehicle", "from", "to":
        // [days] | "all" }: the Week plan tab's copy and paste of a day's route.
        private static object RouteCommand(JObject args)
        {
            var op = Need(args, "op", "add_stop or paste_day");
            var id = Need(args, "vehicle", "the food vehicle's id");
            var instance = FoodVehicle(id);
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            if (!FoodTrucksSession.CanWrite) throw new BridgeError("The mod's save data is read-only.");
            var rules = DemandData.Rules ?? throw new BridgeError("The mod's rules aren't loaded.");
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName) ?? "cart";
            int Stops(DayOfWeek d) => WeekPlan.StopsOf(StreetSales.Find(state, id)?.Schedule, d).Count;
            switch (op)
            {
                case "add_stop":
                {
                    var address = Need(args, "address", "a building's address, ba:street_<name>#<number>");
                    if (!AddressForm.IsMatch(address))
                        throw new BridgeError($"\"address\" is a building's address as the game writes it, ba:street_<name>#<number>; got '{address}'.");
                    var days = Days(args["days"], "days");
                    var open = ClockArg(args, "open");
                    var close = ClockArg(args, "close");
                    if ((open == null) != (close == null)) throw new BridgeError("Give both \"open\" and \"close\", or neither for the default hours.");
                    if (open != null && close!.Value <= open.Value) close += 1440;
                    var doors = World.EntrancesOf(address) ?? throw new BridgeError($"no building at {address} in this city");
                    if (doors.Length == 0) throw new BridgeError($"the building at {address} has no entrance");
                    var spot = StopPicker.Snap(kind, doors[0], out _, out var how)
                               ?? throw new BridgeError($"No {kind} spot by {address}: {how}.");
                    var (position, heading, distance) = spot;
                    var added = new JArray();
                    return WithNotices(() =>
                    {
                        foreach (var day in days)
                        {
                            var before = Stops(day);
                            if (open != null) RouteStops.Add(id, day, position, heading, open.Value, close!.Value);
                            else RouteStops.AddWithDefaultTimes(id, day, position, heading);
                            added.Add(new JObject { ["day"] = day.ToString().ToLowerInvariant(), ["added"] = Stops(day) > before, ["stops"] = Stops(day) });
                        }
                        Log.Info($"Test bridge: route add_stop {id} by {address} at x {position.x:F1}, z {position.z:F1}: " +
                                 $"{added.Count(a => (bool)a["added"]!)} of {days.Count} days.");
                    }, notices => new JObject
                    {
                        ["done"] = added.All(a => (bool)a["added"]!), ["address"] = address,
                        ["x"] = TestBridgeState.Round(position.x), ["z"] = TestBridgeState.Round(position.z),
                        ["heading"] = TestBridgeState.Round(heading), ["distance"] = TestBridgeState.Round(distance),
                        ["days"] = added, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, instance),
                    });
                }
                case "paste_day":
                {
                    var from = Days(args["from"], "from", single: true)[0];
                    var to = Days(args["to"], "to");
                    var vehicle = StreetSales.Find(state, id) ?? throw new BridgeError($"{id} has no route to copy.");
                    var problem = ScheduleProblem.None;
                    return WithNotices(() =>
                    {
                        var copy = RouteEditor.CopyDay(vehicle.Schedule, from);
                        var schedules = new HireSchedules(rules.Fleet);
                        foreach (var day in to.Where(d => d != from))
                        {
                            problem = RouteEditor.PasteRoute(schedules, vehicle, copy, day);
                            if (problem != ScheduleProblem.None) break;
                        }
                        if (problem == ScheduleProblem.None) RouteStops.Changed(vehicle);
                        Log.Info($"Test bridge: route paste_day {id}: {from} onto {string.Join(", ", to)}: {problem}.");
                    }, notices => new JObject
                    {
                        ["done"] = problem == ScheduleProblem.None, ["problem"] = problem == ScheduleProblem.None ? null : problem.ToString(),
                        ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, instance),
                    });
                }
                default:
                    throw new BridgeError($"Unknown route op '{op}': add_stop, paste_day.");
            }
        }

        private static List<DayOfWeek> Days(JToken? token, string key, bool single = false)
        {
            if (!single && (token == null || (token.Type == JTokenType.String && (string?)token == "all"))) return WeekPlan.Days.ToList();
            var names = token?.Type == JTokenType.String ? new[] { (string)token! }
                : token is JArray list ? list.Select(t => t.Type == JTokenType.String ? (string)t! : "").ToArray() : Array.Empty<string>();
            if (names.Length == 0 || (single && names.Length != 1))
                throw new BridgeError(single ? $"\"{key}\" is one weekday, monday to sunday." : $"\"{key}\" is \"all\" or a list of weekdays, monday to sunday.");
            var days = new List<DayOfWeek>();
            foreach (var name in names)
            {
                if (!Enum.TryParse<DayOfWeek>(name, true, out var day) || int.TryParse(name, out _))
                    throw new BridgeError($"Unknown day '{name}': monday to sunday.");
                if (!days.Contains(day)) days.Add(day);
            }
            return days;
        }

        // "HH:MM" as minutes from midnight, or null when the key is left out.
        private static int? ClockArg(JObject args, string key)
        {
            var text = Str(args, key);
            if (text == null) return null;
            var parts = text.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h < 0 || h > 23 || m < 0 || m > 59)
                throw new BridgeError($"\"{key}\" must be \"HH:MM\".");
            return h * 60 + m;
        }
    }
}
