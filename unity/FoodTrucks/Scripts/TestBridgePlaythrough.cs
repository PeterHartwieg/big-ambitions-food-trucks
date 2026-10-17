#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Streets;
using UnityEngine;
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

        // ---- places ----

        // { "radius"?: 150 }: the buildings round the player as the map shows them: address, street, building type,
        // the business there (name and type, whether it is the player's) and the mod's commissaries. No demand
        // figures. Nearest first, at most 40. Reads only.
        private static JToken PlacesCommand(JObject args)
        {
            var radius = Math.Max(20, Math.Min(500, Num(args, "radius") ?? 150));
            var at = PlayerHelper.GetPosition();
            var registrations = World.Registrations();
            var sites = DemandData.Commissaries?.Sites ?? new List<FoodTrucks.Core.Commissary.CommissarySite>();
            var list = new List<(double Distance, JObject Entry)>();
            foreach (var (controller, doors) in World.AllBuildings())
            {
                var address = controller.building?.Address;
                if (address == null) continue;
                var distance = doors.Min(d => Math.Sqrt((d.x - at.x) * (d.x - at.x) + (d.z - at.z) * (d.z - at.z)));
                if (distance > radius) continue;
                var key = World.AddressKey(address.streetName, address.streetNumber);
                registrations.TryGetValue((address.streetName, address.streetNumber), out var registration);
                var hasBusiness = registration != null && registration.businessTypeName != "ba:businesstype_empty";
                var site = sites.FirstOrDefault(s => s.Address == key);
                list.Add((distance, new JObject
                {
                    ["address"] = key, ["name"] = address.ToFormattedString(),
                    ["building_type"] = Localizor.LocalizorManager.GetLocalization(controller.building!.BuildingType, null),
                    ["business"] = hasBusiness ? registration!.BusinessName : null,
                    ["business_type"] = hasBusiness ? Localizor.LocalizorManager.GetLocalization(registration!.businessTypeName, null) : null,
                    ["yours"] = registration?.RentedByPlayer ?? false,
                    ["commissary"] = site == null ? null : new JObject { ["site"] = site.Id, ["name"] = Localizor.LocalizorManager.GetLocalization(site.Name, null) },
                    ["distance"] = Math.Round(distance),
                }));
            }
            return new JObject
            {
                ["radius"] = radius,
                ["places"] = new JArray(list.OrderBy(p => p.Distance).Take(40).Select(p => p.Entry)),
            };
        }

        // ---- map ----

        // The city map's highlights as data, filtered the way a player filters the map (docs/ai-playtest-plan.md, "The map
        // for the tester"). { "op": "filters" }: the filters there are, each with how many buildings it lights now.
        // { "filter", "limit"?: N (default 30, at most 100) }: the buildings that filter lights, nearest the player first.
        // Filters, as the game's map and the mod's Street food line have them:
        //   for_rent, for_rent:<building type>  buildings on the market (AvailableForRent), all or of one type
        //   business:<business type>             active businesses of one type (ba:businesstype_…)
        //   for_sale, yours                      buildings for sale; buildings you rent or own
        //   offices, factories, nightlife, shows Street food's building filters (Core StreetFoodMap)
        //   pitches, commissaries                Street food's premium spots and coming events; the commissaries
        // Reads only. The game's own map isn't opened or changed.
        private static JToken MapCommand(JObject args)
        {
            var game = SaveGameManager.Current;
            var tables = DemandData.Tables ?? throw new BridgeError("The mod's tables aren't loaded.");
            var classifier = new FoodTrucks.Core.Demand.AreaClassifier(tables);
            var registrations = World.Registrations();
            var forSale = new HashSet<string>((game.buildingsForSale ?? new List<BuildingForSale>()).Where(b => b?.address != null)
                .Select(b => World.AddressKey(b.address.streetName, b.address.streetNumber)), StringComparer.Ordinal);
            var owned = new HashSet<string>((game.realEstate ?? new List<Entities.RealEstate>()).Where(r => r?.address != null)
                .Select(r => World.AddressKey(r.address.streetName, r.address.streetNumber)), StringComparer.Ordinal);
            var pitches = MapFilters.PitchAddresses();
            var sites = DemandData.Commissaries?.Sites ?? new List<FoodTrucks.Core.Commissary.CommissarySite>();
            var commissaries = new HashSet<string>(sites.Select(s => s.Address), StringComparer.Ordinal);
            var at = PlayerHelper.GetPosition();

            // Every filter that lights a building, for one building.
            IEnumerable<string> Lit(string key, string? buildingType, BuildingRegistration? registration)
            {
                var business = registration?.businessTypeName;
                var hasBusiness = !string.IsNullOrEmpty(business) && business != "ba:businesstype_empty";
                if (registration?.AvailableForRent == true)
                {
                    yield return "for_rent";
                    if (!string.IsNullOrEmpty(buildingType)) yield return "for_rent:" + buildingType;
                }
                if (hasBusiness) yield return "business:" + business;
                if (forSale.Contains(key) && !owned.Contains(key)) yield return "for_sale";
                if (owned.Contains(key) || registration?.RentedByPlayer == true) yield return "yours";
                foreach (var f in FoodTrucks.Core.Demand.StreetFoodMap.BuildingFilters)
                    if (FoodTrucks.Core.Demand.StreetFoodMap.Highlights(classifier, f, buildingType, business)) yield return f;
                if (pitches.Contains(key)) yield return "pitches";
                if (commissaries.Contains(key)) yield return "commissaries";
            }

            var buildings = new List<(string Key, CityBuildingController Controller, Vector3 Door, double Distance, BuildingRegistration? Registration, List<string> Lit)>();
            foreach (var (controller, doors) in World.AllBuildings())
            {
                var address = controller.building?.Address;
                if (address == null || doors.Length == 0) continue;
                var key = World.AddressKey(address.streetName, address.streetNumber);
                registrations.TryGetValue((address.streetName, address.streetNumber), out var registration);
                var door = doors.OrderBy(d => (d - at).sqrMagnitude).First();
                buildings.Add((key, controller, door, Math.Sqrt((door.x - at.x) * (door.x - at.x) + (door.z - at.z) * (door.z - at.z)),
                    registration, Lit(key, controller.building!.BuildingType, registration).ToList()));
            }

            if (Str(args, "op") == "filters")
            {
                string Label(string id)
                {
                    var colon = id.IndexOf(':');
                    if (colon < 0) return id;
                    var name = Localizor.LocalizorManager.GetLocalization(id.Substring(colon + 1), null);
                    return (id.StartsWith("for_rent:", StringComparison.Ordinal) ? "for rent: " : "") + name;
                }
                var counts = buildings.SelectMany(b => b.Lit).GroupBy(f => f, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => new JObject { ["filter"] = g.Key, ["label"] = Label(g.Key), ["buildings"] = g.Count() });
                return new JObject { ["filters"] = new JArray(counts) };
            }

            var filter = Need(args, "filter", "a map filter (op \"filters\" lists them)");
            var limit = (int)Math.Max(1, Math.Min(100, Num(args, "limit") ?? 30));
            var lit = buildings.Where(b => b.Lit.Contains(filter)).OrderBy(b => b.Distance).ToList();
            var list = new JArray();
            foreach (var b in lit.Take(limit))
            {
                var registration = b.Registration;
                var business = registration?.businessTypeName;
                var hasBusiness = !string.IsNullOrEmpty(business) && business != "ba:businesstype_empty";
                var site = sites.FirstOrDefault(s => s.Address == b.Key);
                list.Add(new JObject
                {
                    ["address"] = b.Key,
                    ["name"] = b.Controller.building!.Address.ToFormattedString(),
                    ["distance"] = Math.Round(b.Distance),
                    ["x"] = TestBridgeState.Round(b.Door.x), ["z"] = TestBridgeState.Round(b.Door.z),
                    ["building_type"] = Localizor.LocalizorManager.GetLocalization(b.Controller.building.BuildingType, null),
                    ["size"] = string.IsNullOrEmpty(b.Controller.building.BuildingSize) ? null : Localizor.LocalizorManager.GetLocalization(b.Controller.building.BuildingSize, null),
                    ["business"] = hasBusiness ? registration!.BusinessName : null,
                    ["business_type"] = hasBusiness ? Localizor.LocalizorManager.GetLocalization(business!, null) : null,
                    ["for_rent"] = registration?.AvailableForRent ?? false,
                    ["rent_per_day"] = registration?.AvailableForRent == true ? Math.Round(registration.RentPerDay, 2) : (double?)null,
                    ["neighbourhood"] = string.IsNullOrEmpty(b.Controller.building.Neighbourhood) ? null
                        : Localizor.LocalizorManager.GetLocalization(b.Controller.building.Neighbourhood, null),
                    ["commissary"] = site == null ? null : new JObject { ["site"] = site.Id, ["name"] = Localizor.LocalizorManager.GetLocalization(site.Name, null) },
                });
            }
            return new JObject { ["filter"] = filter, ["count"] = lit.Count, ["buildings"] = list };
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
