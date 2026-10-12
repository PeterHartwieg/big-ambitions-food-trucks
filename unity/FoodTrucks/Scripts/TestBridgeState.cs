#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
            };
            if (game == null || scene != "city") return result;
            result["clock"] = TestBridgeCommands.Clock();
            result["money"] = Math.Round(game.Money, 2);
            result["player"] = Player(game);
            var state = FoodTrucksSession.CurrentState;
            if (state == null) return result;
            result["licence"] = new JObject
            {
                ["until_day"] = state.Licence.HasVendorLicence ? state.Licence.LicenceExpiresOnDay : (int?)null,
                ["permits"] = state.Vehicles.Count(v => v.HasPermit),
                ["course_done"] = Try(() => (bool?)EducationHelper.HasCompletedDiploma(DiplomaName.FoodSafetyCourse)),
            };
            var vehicles = new JArray();
            foreach (var instance in game.VehicleInstances.Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null))
            {
                try { vehicles.Add(Vehicle(state, instance)); }
                catch (Exception ex) { vehicles.Add(new JObject { ["id"] = instance.id, ["error"] = ex.Message }); }
            }
            result["vehicles"] = vehicles;
            result["commissary"] = Commissary(state);
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
            return new JObject
            {
                ["x"] = Round(position.x), ["y"] = Round(position.y), ["z"] = Round(position.z), ["heading"] = Round(heading),
                ["in_vehicle"] = string.IsNullOrEmpty(game.ActiveVehicleId) ? null : game.ActiveVehicleId,
                ["at_hatch"] = atHatch,
                ["carrying"] = hands?.itemName,
            };
        }

        internal static JObject Vehicle(SaveState state, VehicleInstance instance)
        {
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName)!;
            var record = StreetSales.Find(state, instance.id);
            var controller = VehicleHelper.GetVehicleController(instance);
            var position = controller != null ? controller.transform.position : (Vector3)instance.position;
            var heading = controller != null ? controller.transform.eulerAngles.y : (float?)null;
            var legality = Legality.Check(position);
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
            var stops = record?.Schedule?.Stops;
            if (stops != null && stops.Count > 0)
            {
                var minuteOfDay = (int)(now % 1440);
                var next = stops.Select((s, i) => (Stop: s, Index: i)).Where(p => p.Stop.CloseAt > minuteOfDay).OrderBy(p => p.Stop.OpenAt).FirstOrDefault();
                route = new JObject
                {
                    ["stops"] = stops.Count,
                    ["next"] = next.Stop == null ? null : $"stop {next.Index + 1}, {Clock(next.Stop.OpenAt)} to {Clock(next.Stop.CloseAt)} at x {next.Stop.X:F1}, z {next.Stop.Z:F1}",
                };
            }

            return new JObject
            {
                ["id"] = instance.id,
                ["kind"] = kind,
                ["name"] = LocalizorManager.GetLocalization(instance.vehicleTypeName, null),
                ["x"] = Round(position.x), ["y"] = Round(position.y), ["z"] = Round(position.z),
                ["heading"] = heading == null ? (double?)null : Round(heading.Value),
                ["legal"] = legality.Legal,
                ["legal_reason"] = legality.Detail,
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
            };
        }

        private static JObject Commissary(SaveState state)
        {
            var c = state.Commissary;
            return new JObject
            {
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

        private static T? Try<T>(Func<T?> read) where T : struct
        {
            try { return read(); }
            catch (Exception) { return null; }
        }
    }
}
