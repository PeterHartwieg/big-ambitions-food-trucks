#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    // The bridge's `state` (docs/test-bridge.md, "State"): read from the mod's state and the game's, changing nothing.
    // A field the bridge can't fill is null, never left out.
    internal static class TestBridgeState
    {
        internal static JObject Read()
        {
            var game = SaveGameManager.Current;
            var scene = TestBridgeCommands.Scene();
            var result = new JObject
            {
                ["save"] = game?.SaveGameName,
                ["scene"] = scene,
                ["clock"] = null,
                ["money"] = null,
                ["player"] = null,
                ["licence"] = null,
                ["vehicles"] = new JArray(),
                ["commissary"] = null,
                ["ledger_today"] = null,
                ["notices_recent"] = new JArray(Notices.Recent()),
                ["load"] = TestBridgeCommands.LoadJson(),
                ["transactions_today"] = new JArray(),
                ["employees"] = new JArray(),
                ["game_camera"] = null,
                ["view_blocked"] = new JArray(),
            };
            if (game == null || scene != "city") return result;
            result["clock"] = TestBridgeCommands.Clock();
            var main = Camera.main;
            if (main != null)
            {
                var a = main.transform.eulerAngles;
                result["game_camera"] = new JObject
                {
                    ["x"] = Round(main.transform.position.x), ["y"] = Round(main.transform.position.y), ["z"] = Round(main.transform.position.z),
                    ["yaw"] = Round(a.y), ["pitch"] = Round(a.x > 180f ? a.x - 360f : a.x),
                };
            }
            result["view_blocked"] = TestBridgeUncover.ViewBlocked();
            result["money"] = Math.Round(game.Money, 2);
            result["player"] = Player(game);
            result["transactions_today"] = Transactions(game);
            var state = FoodTrucksSession.CurrentState;
            if (state == null) return result;
            result["licence"] = new JObject
            {
                ["until_day"] = state.Licence.HasVendorLicence ? state.Licence.LicenceExpiresOnDay : (int?)null,
                ["permits"] = state.Vehicles.Count(v => v.HasPermit),
                ["course_done"] = Try(() => (bool?)EducationHelper.HasCompletedDiploma(DiplomaName.FoodSafetyCourse)),
                ["todos"] = Todos(null),
            };
            var vehicles = new JArray();
            foreach (var instance in game.VehicleInstances.Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null))
            {
                try { vehicles.Add(Vehicle(state, instance)); }
                catch (Exception ex) { vehicles.Add(new JObject { ["id"] = instance.id, ["error"] = ex.Message }); }
            }
            result["vehicles"] = vehicles;
            result["commissary"] = Commissary(state);
            result["employees"] = new JArray(HireCrew.Available(state).Select(e => new JObject
            {
                ["id"] = e.id, ["name"] = HireCrew.Name(e), ["skill"] = Math.Round(HireCrew.Skill(e), 1),
                ["satisfaction"] = Math.Round(e.satisfaction, 1), ["wage"] = Math.Round(e.hourlyWage, 2),
            }));
            var today = state.DailySales.FirstOrDefault(d => d.Day == game.Day);
            result["ledger_today"] = new JObject
            {
                ["street_food"] = (today?.RevenueCents ?? 0) / 100.0,
                ["crew_waiting"] = state.CrewTakings.Sum(t => t.Cents) / 100.0,
            };
            return result;
        }

        private static JObject Player(GameInstance game)
        {
            var controller = PlayerHelper.PlayerController;
            var position = PlayerHelper.GetPosition();
            var heading = controller != null ? controller.transform.eulerAngles.y : 0f;
            string? atHatch = null;
            foreach (var v in game.VehicleInstances)
                if (v != null && TruckWindow.PlayerAt(v.id)) atHatch = v.id;
            var hands = PlayerHelper.ItemInstanceInHands;
            string activity;
            if (!string.IsNullOrEmpty(game.ActiveVehicleId)) activity = VehicleHelper.GetCurrentVehicleBase() is HandTruck ? "pushing" : "driving";
            else activity = TruckWindow.Phase() ?? (controller != null && controller.hasOnGoalReachedAction ? "walking" : "on_foot");
            return new JObject
            {
                ["activity"] = activity,
                ["clock_held_by"] = TestBridgeCommands.ClockHeldBy(),
                ["x"] = Round(position.x), ["y"] = Round(position.y), ["z"] = Round(position.z), ["heading"] = Round(heading),
                ["in_vehicle"] = string.IsNullOrEmpty(game.ActiveVehicleId) ? null : game.ActiveVehicleId,
                ["at_hatch"] = atHatch,
                ["carrying"] = hands?.itemName,
                ["hands"] = TestBridgeCommands.Hands(),
                ["handtruck"] = TestBridgeCommands.PushingHandTruck(),
                ["inside"] = TestBridgeCommands.InsideAddress(),
                ["homes"] = TestBridgeCommands.Homes(),
                ["riding"] = TaxiSystem.IsTraveling,
                ["needs"] = TestBridgeCommands.Needs(game),
            };
        }

        internal static JObject Vehicle(SaveState state, VehicleInstance instance)
        {
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName)!;
            var record = StreetSales.Find(state, instance.id);
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var heading = controller != null ? controller.transform.eulerAngles.y : (float?)null;
            // A truck parks: the game's own wheel test, as its panel's LEGAL/ILLEGAL. A cart stands on the pavement: the
            // mod's pavement rule.
            bool legal;
            string legalReason;
            var info = InstanceBehavior<UI.UIs>.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
            if (kind == "truck" && SaveGameManager.Current.ActiveVehicleId == instance.id && info != null && info.isActiveAndEnabled)
            {
                // The player drives it: the panel's live reading, which the game saves as the truck's parking state when it
                // parks (the state Open for business checks, CartSelling.PitchProblem).
                legal = info.currentParkingState == ParkingState.Legal;
                legalReason = $"panel's live reading: {info.currentParkingState}";
            }
            else if (kind == "truck")
            {
                legal = Legality.VehicleLegallyParked(instance, out var how);
                legalReason = $"wheel parking test: {(legal ? "legal" : "illegal")}, {how}";
            }
            else
            {
                var rule = Legality.Check(position);
                legal = rule.Legal;
                legalReason = "pavement rule: " + rule.Detail;
            }
            bool? toPavement = null;
            string? hatchSide = null;
            if (kind == "truck")
            {
                // The truck's hatch is on its right (art/README.md); HatchCheck reads whether that side has the pavement.
                hatchSide = "right";
                if (controller != null) toPavement = HireRoutes.Hatch(instance.id, controller.transform).Opens;
            }
            else
            {
                var side = CartQueueView.QueueSide(instance.id);
                hatchSide = side == null ? null : side > 0 ? "right" : "left";
            }
            var open = record?.IsOpen == true;
            var now = HireCrew.Now();
            string serving = "nobody";
            if (open && CartSelling.PlayerServesNow(instance)) serving = "player";
            else if (open && record != null && HireCrew.Serving(record, now).Count > 0) serving = "crew";

            var tables = DemandData.Tables;
            var pieces = new JArray();
            var view = StoragePanel.Of(instance);
            if (view != null)
                foreach (var pool in view.Pools)
                    pieces.Add(new JObject { ["item"] = pool.EquipmentId, ["units"] = pool.LoadUnits, ["cap"] = pool.Capacity });
            var stock = new JArray();
            foreach (var group in instance.cargoInstances.Where(c => c != null && tables?.EquipmentForGameItem(c.itemName) == null).GroupBy(c => c.itemName))
                stock.Add(new JObject
                {
                    ["item"] = LocalizorManager.GetLocalization(group.Key, null),
                    ["key"] = group.Key,
                    ["amount"] = group.Sum(c => c.amount),
                    // The price set on this vehicle's page; null while it sells at the automatic price.
                    ["set_price"] = record?.CustomPriceCents != null && record.CustomPriceCents.TryGetValue(group.Key, out var setCents)
                        ? (JToken)(setCents / 100.0) : JValue.CreateNull(),
                });

            JObject? queue = null;
            if (record?.Service != null)
            {
                var waiting = record.Service.Window.Count + record.Service.Queue.Count;
                var places = new JArray();
                var shown = CartQueueView.PlacesOf(instance.id);
                if (shown != null)
                    for (var i = 0; i < shown.Count; i++)
                        places.Add(new JObject { ["x"] = Round(shown[i].x), ["z"] = Round(shown[i].z), ["occupied"] = i < waiting });
                queue = new JObject { ["length"] = waiting, ["places"] = places };
            }

            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == instance.id);
            var inBay = global::FoodTrucks.Commissary.SiteOf(instance);
            JObject? commissary = space == null && inBay == null ? null : new JObject
            {
                ["site"] = space?.CommissaryId,
                ["in_bay"] = inBay != null,
                ["bay_site"] = inBay?.Id,
            };

            JObject? route = null;
            var schedule = record?.Schedule;
            if (schedule != null && (schedule.Stops.Count > 0 || schedule.CrewShifts.Count > 0))
            {
                var minuteOfDay = (int)(now % 1440);
                var today = FoodTrucks.Core.Common.GameDays.Weekday(Math.Max(1, HireRunner.DayOf(now)));
                var todays = WeekPlan.StopsOf(schedule, today);
                var next = todays.Select((i, k) => (Stop: schedule.Stops[i], Number: k + 1)).Where(p => p.Stop.CloseAt > minuteOfDay)
                    .OrderBy(p => p.Stop.OpenAt).FirstOrDefault();
                route = new JObject
                {
                    ["stops"] = todays.Count,
                    ["next"] = next.Stop == null ? null : $"stop {next.Number}, {Clock(next.Stop.OpenAt)} to {Clock(next.Stop.CloseAt)} at x {next.Stop.X:F1}, z {next.Stop.Z:F1}",
                    ["week"] = WeekOf(record!),
                    ["shifts"] = ShiftsOf(record!),
                    ["problem"] = DemandData.Rules == null ? null : Snake(new HireRunner(DemandData.Rules.Fleet).RouteProblem(record!, HireRoutes.GameSpeed(),
                        HireRoutes.HomePoint(HireRoutes.HomeOf(record!)), HireRoutes.HomeOf(record!)?.BayStops).ToString()),
                };
            }

            return new JObject
            {
                ["id"] = instance.id,
                ["kind"] = kind,
                ["name"] = LocalizorManager.GetLocalization(instance.vehicleTypeName, null),
                ["x"] = Round(position.x), ["y"] = Round(position.y), ["z"] = Round(position.z),
                ["heading"] = heading == null ? (double?)null : Round(heading.Value),
                ["legal"] = legal,
                ["legal_reason"] = legalReason,
                ["hatch_side"] = hatchSide,
                ["hatch_to_pavement"] = toPavement,
                ["open"] = open,
                ["serving"] = serving,
                ["crew"] = new JArray((record?.Crew ?? new List<string>()).Select(HireCrew.Find).Where(e => e != null).Select(e => HireCrew.Name(e!))),
                ["pieces"] = pieces,
                ["stock"] = stock,
                ["queue"] = queue,
                ["commissary"] = commissary,
                ["route"] = route,
                ["parking"] = Parking(instance),
                ["todos"] = Todos(instance.id),
            };
        }

        // The week plan, Monday first: each weekday's closed mark and stops, its route day (restock to back in the bay, as
        // minutes from that day's midnight, past 1440 for a late route) and the parts of it with nobody on shift.
        private static JArray WeekOf(VehicleState vehicle)
        {
            var schedule = vehicle.Schedule!;
            var rules = DemandData.Rules;
            var home = HireRoutes.HomeOf(vehicle);
            var days = rules == null ? null : WeekPlan.RouteDays(new HireSchedules(rules.Fleet), schedule, Travel.ModeFor(vehicle.VehicleType),
                HireRoutes.GameSpeed(), HireRoutes.HomePoint(home), home?.BayStops);
            var list = new JArray();
            foreach (var day in WeekPlan.Days)
            {
                var routeDay = days?[WeekPlan.Position(day)];
                list.Add(new JObject
                {
                    ["day"] = day.ToString().ToLowerInvariant(),
                    ["closed"] = WeekPlan.IsClosed(schedule, day),
                    ["stops"] = new JArray(WeekPlan.StopsOf(schedule, day).Select(i => schedule.Stops[i]).Select(s => new JObject
                    {
                        ["open"] = Clock(s.OpenAt), ["close"] = Clock(s.CloseAt), ["x"] = Math.Round(s.X, 2), ["z"] = Math.Round(s.Z, 2),
                    })),
                    ["route_day"] = routeDay == null || routeDay.Empty ? null : new JObject { ["start"] = routeDay.Start, ["end"] = routeDay.End },
                    ["gaps"] = routeDay == null || routeDay.Empty ? new JArray()
                        : new JArray(WeekPlan.Gaps(routeDay, schedule, vehicle.Crew).Select(g => new JObject { ["start"] = g.Start, ["end"] = g.End })),
                });
            }
            return list;
        }

        // Each crew member's shifts and week: name, id, the week's hours and the range they want (null: no demand).
        private static JArray ShiftsOf(VehicleState vehicle) =>
            new JArray((vehicle.Crew ?? new List<string>()).Select(id =>
            {
                var employee = HireCrew.Find(id);
                var wish = employee == null ? new CrewWish(id) : HireCrew.Wish(employee);
                return new JObject
                {
                    ["employee"] = employee == null ? id : HireCrew.Name(employee),
                    ["id"] = id,
                    ["week_hours"] = WeekPlan.WeekHours(vehicle.Schedule, id),
                    ["wants_min"] = wish.MinHours,
                    ["wants_max"] = wish.MaxHours,
                    ["shifts"] = new JArray(vehicle.Schedule!.CrewShifts.Where(s => s.EmployeeId == id).Select(s => new JObject
                    {
                        ["day"] = s.Day.ToString().ToLowerInvariant(), ["start"] = s.Start, ["end"] = s.End, ["station"] = s.Station ?? "serving1",
                    })),
                };
            }));

        private static string Snake(string name) =>
            string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

        // The phone's to-dos the mod made for this vehicle (null: the fleet's, the vendor licence), as they stand after the
        // last minute's reconcile: kind and food.
        private static JArray Todos(string? vehicleId) =>
            new JArray(VehicleTodos.ListFor(vehicleId).Select(t => new JObject { ["kind"] = t.Kind, ["item"] = t.Item, ["text"] = t.Text }));

        // The game's transactions of today and yesterday, oldest first: label as the bank app shows it, amount, day, time.
        private static JArray Transactions(GameInstance game)
        {
            var list = new JArray();
            foreach (var t in game.Transactions)
            {
                if (t == null || t.timestamp.Day < game.Day - 1) continue;
                list.Add(new JObject
                {
                    ["label"] = Notices.Text(t.transactionType ?? "", t.transactionData),
                    ["type"] = t.transactionType,
                    ["amount"] = Math.Round(t.amount, 2),
                    // The game's balance after it (floored to whole dollars), which tells two equal transactions apart.
                    ["balance"] = Math.Round(t.balance, 2),
                    ["day"] = t.timestamp.Day,
                    ["time"] = $"{t.timestamp.Hour:D2}:{(int)t.timestamp.Minute:D2}",
                });
            }
            return list;
        }

        // Each commissary's bay: "resolved" with its point, "failed" when the search found no legal parking, "pending"
        // until the city's parking lanes let it search. `entrance` is the building's entrance nearest the bay.
        private static JArray Bays()
        {
            var list = new JArray();
            var sites = DemandData.Commissaries;
            if (sites == null) return list;
            foreach (var site in sites.Sites)
            {
                var points = global::FoodTrucks.Commissary.ResolvedBays(site);
                var entry = new JObject
                {
                    ["site"] = site.Id,
                    ["status"] = points == null ? "pending" : points.Count == 0 ? "failed" : "resolved",
                    ["point"] = null,
                    ["entrance"] = null,
                };
                if (points != null && points.Count > 0)
                {
                    entry["point"] = new JObject { ["x"] = Math.Round(points[0].X, 2), ["z"] = Math.Round(points[0].Z, 2), ["along_x"] = Math.Round(points[0].AlongX, 3), ["along_z"] = Math.Round(points[0].AlongZ, 3) };
                    var door = TestBridgeCommands.EntranceNear(site, (float)points[0].X, (float)points[0].Z);
                    if (door != null) entry["entrance"] = new JObject { ["x"] = Round(door.Value.x), ["z"] = Round(door.Value.z) };
                }
                list.Add(entry);
            }
            return list;
        }

        private static JObject Commissary(SaveState state)
        {
            var c = state.Commissary;
            return new JObject
            {
                ["bays"] = Bays(),
                ["spaces"] = new JArray(c.Spaces.Select(s => new JObject { ["site"] = s.CommissaryId, ["vehicle"] = s.VehicleId })),
                ["orders"] = new JArray(c.Orders.Select(o => new JObject
                {
                    ["n"] = o.Id,
                    ["site"] = o.CommissaryId,
                    ["items"] = $"{o.Units} {LocalizorManager.GetLocalization(o.ItemName, null)}",
                    ["item"] = o.ItemName,
                    ["units"] = o.Units,
                    ["total"] = o.TotalCents / 100.0,
                    ["due_day"] = o.DeliverDay,
                })),
                ["stock"] = new JArray(c.Stock.SelectMany(p => p.Value.GroupBy(l => l.ItemName).Select(g => new JObject
                {
                    ["site"] = p.Key,
                    ["item"] = LocalizorManager.GetLocalization(g.Key, null),
                    ["key"] = g.Key,
                    ["amount"] = g.Sum(l => l.Units),
                }))),
                ["unpaid"] = new JObject
                {
                    ["rent"] = c.Charges.Sum(x => x.Cents) / 100.0,
                    ["fines"] = c.Fines.Sum(x => x.Cents) / 100.0,
                },
            };
        }

        private static string Clock(int minuteOfDay) => $"{minuteOfDay / 60 % 24:D2}:{minuteOfDay % 60:D2}";

        internal static double Round(float value) => Math.Round(value, 2);

        // What the game's hourly parking charge reads for a vehicle nobody drives: its stored parking state, the
        // neighbourhood it was left in and that neighbourhood's hourly price (ParkingSimulator.RunHourly: legal parking
        // adds the price to unpaidParkingAmount every hour, $25 in a parking garage; illegal parking draws tickets).
        private static JObject Parking(VehicleInstance instance)
        {
            double? price = null;
            try
            {
                price = instance.streetName == "ba:street_parking" ? 25.0
                    : string.IsNullOrEmpty(instance.parkingNeighbourhood) ? (double?)null
                    : Math.Round(NeighborhoodHelper.GetData(instance.parkingNeighbourhood).parkingPrice, 2);
            }
            catch (Exception) { price = null; }
            return new JObject
            {
                ["state"] = instance.parkingState.ToString(),
                ["neighbourhood"] = instance.parkingNeighbourhood,
                ["hourly_price"] = price,
                ["unpaid"] = Math.Round(instance.unpaidParkingAmount, 2),
            };
        }

        private static T? Try<T>(Func<T?> read) where T : struct
        {
            try { return read(); }
            catch (Exception) { return null; }
        }
    }
}
