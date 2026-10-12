#nullable enable
using System;
using System.Collections;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Helpers;
using Newtonsoft.Json.Linq;
using UI;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    // The bridge's teleport and vehicle commands, and the places both they and shoot resolve: a commissary's bay and
    // door, a vehicle's hatch side.
    internal static partial class TestBridgeCommands
    {
        // ---- Places ----

        // The food vehicle with this id, or a refusal naming the ones there are.
        internal static VehicleInstance FoodVehicle(string id)
        {
            var game = SaveGameManager.Current;
            var vehicle = game.VehicleInstances.FirstOrDefault(v => v != null && v.id == id && FoodTrucksMod.KindOf(v.vehicleTypeName) != null);
            if (vehicle != null) return vehicle;
            var ids = game.VehicleInstances.Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null).Select(v => v.id).ToList();
            throw new BridgeError($"No food cart or truck with id '{id}'. Food vehicles: {(ids.Count == 0 ? "none" : string.Join(", ", ids))}.");
        }

        internal static VehicleController Spawned(VehicleInstance vehicle)
        {
            if (!string.IsNullOrEmpty(vehicle.streetName)) throw new BridgeError($"Vehicle {vehicle.id} is inside a building ({vehicle.streetName}).");
            var controller = VehicleHelper.GetVehicleController(vehicle);
            if (controller == null) throw new BridgeError($"Vehicle {vehicle.id} isn't on the map now (too far from the player, or in a garage).");
            return controller;
        }

        internal static CommissarySite Site(string id)
        {
            var sites = DemandData.Commissaries ?? throw new BridgeError("The commissaries aren't loaded (data/commissaries.json).");
            return sites.Site(id) ?? throw new BridgeError($"No commissary '{id}'. Commissaries: {string.Join(", ", sites.Sites.Select(s => s.Id))}.");
        }

        // The ground (the game's Ground layer) under a point, from high above.
        internal static Vector3 Ground(float x, float z)
        {
            if (Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out var hit, 1000f, LayerHelper.groundLayerMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            throw new BridgeError($"No ground found at x {x:F1}, z {z:F1}.");
        }

        // A commissary's bay: its first resolved point on the ground, the paint's direction, and the side its pavement is on
        // (1 right of that direction, -1 left). The direction is the one a truck faces with its hatch (right) to the pavement,
        // by the mod's own hatch check; the paint's own direction when neither reads as pavement.
        internal static (Vector3 Point, Vector3 Along, int PavementSide) Bay(string siteId)
        {
            var site = Site(siteId);
            var points = global::FoodTrucks.Commissary.ResolvedBays(site);
            if (points == null) throw new BridgeError($"The bay of {siteId} isn't resolved yet (the city's parking lanes load after the city); try again in a few seconds.");
            if (points.Count == 0) throw new BridgeError($"{siteId} has no bay: the search found no legal parking near it.");
            var p = points[0];
            var point = Ground((float)p.X, (float)p.Z);
            var along = new Vector3((float)p.AlongX, 0f, (float)p.AlongZ);
            if (along.sqrMagnitude < 1e-6f) along = Vector3.forward;
            along.Normalize();
            if (HatchCheck.Probe(point, Quaternion.LookRotation(along)).Opens) return (point, along, 1);
            if (HatchCheck.Probe(point, Quaternion.LookRotation(-along)).Opens) return (point, -along, 1);
            return (point, along, 1);
        }

        // The building's entrance nearest a point (the bay), or null when the building isn't in this city.
        internal static Vector3? EntranceNear(CommissarySite site, float x, float z)
        {
            var doors = World.EntrancesOf(site.Address);
            if (doors == null || doors.Length == 0) return null;
            return doors.OrderBy(d => (d.x - x) * (d.x - x) + (d.z - z) * (d.z - z)).First();
        }

        // A commissary's door and the way out of the building from it: the entrance nearest its resolved bay (the one
        // state.commissary.bays names), else the building's first entrance.
        internal static (Vector3 Door, Vector3 Out) Door(string siteId)
        {
            var site = Site(siteId);
            var doors = World.EntrancesOf(site.Address);
            if (doors == null || doors.Length == 0) throw new BridgeError($"The building of {siteId} ({site.Address}) isn't in this city.");
            var bay = global::FoodTrucks.Commissary.ResolvedBays(site);
            var door = bay != null && bay.Count > 0 ? EntranceNear(site, (float)bay[0].X, (float)bay[0].Z)!.Value : doors[0];
            var building = World.AllBuildings().FirstOrDefault(b => b.Doors.Contains(door)).Controller;
            var outward = building != null ? door - building.transform.position : Vector3.forward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            return (door, outward.normalized);
        }

        // A vehicle's hatch (a truck's serving window, a cart's window side) and the outward direction there.
        internal static (Vector3 Hatch, Vector3 Out, Vector3 Forward) HatchOf(VehicleInstance vehicle, Transform transform)
        {
            var forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            if (FoodTrucksMod.KindOf(vehicle.vehicleTypeName) == "truck")
            {
                // The hatch is on the truck's right (art/README.md); the player's place behind it is its middle.
                var right = transform.right;
                right.y = 0f;
                return (WindowCrew.PlayerSpot(transform).Position, right.normalized, forward);
            }
            var side = CartQueueView.QueueSide(vehicle.id) ?? 1;
            var outward = transform.right * side;
            outward.y = 0f;
            return (transform.position, outward.normalized, forward);
        }

        private static float Heading(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        // ---- teleport ----

        private const float TeleportSeconds = 30f;

        private static object Teleport(JObject args)
        {
            var who = Need(args, "who", "\"player\" or a food vehicle's id");
            var place = Str(args, "place");
            var heading = (float?)Num(args, "heading");
            Vector3 target;
            if (place != null) (target, heading) = Place(place, who, heading);
            else
            {
                var x = Num(args, "x") ?? throw new BridgeError("teleport needs \"x\" and \"z\", or \"place\".");
                var z = Num(args, "z") ?? throw new BridgeError("teleport needs \"x\" and \"z\", or \"place\".");
                target = Ground((float)x, (float)z);
            }
            return who == "player" ? TeleportPlayer(target, heading) : TeleportVehicle(who, target, heading);
        }

        private static (Vector3, float?) Place(string place, string who, float? heading)
        {
            var colon = place.IndexOf(':');
            var kind = colon < 0 ? place : place.Substring(0, colon);
            var id = colon < 0 ? "" : place.Substring(colon + 1);
            switch (kind)
            {
                case "bay":
                {
                    var bay = Bay(id);
                    return (bay.Point, heading ?? Heading(bay.Along));
                }
                case "commissary":
                {
                    if (who != "player") throw new BridgeError("commissary: places the player at the door; a vehicle goes to bay:<site>.");
                    var (door, outward) = Door(id);
                    // A step out from the door, facing it.
                    return (door + outward * 1.5f, heading ?? Heading(-outward));
                }
                case "vehicle":
                {
                    if (who != "player") throw new BridgeError("vehicle: places the player beside a vehicle.");
                    var vehicle = FoodVehicle(id);
                    var controller = Spawned(vehicle);
                    var (hatch, outward, _) = HatchOf(vehicle, controller.transform);
                    var flat = controller.transform.position;
                    var along = hatch - flat;
                    // On the pavement beside the hatch: the hatch's place along the vehicle, 2 m out from its side.
                    var halfWidth = FoodTrucksMod.KindOf(vehicle.vehicleTypeName) == "truck" ? 1.1f : 0.6f;
                    var point = flat + Vector3.Project(along, controller.transform.forward) + outward * (halfWidth + 1.5f);
                    return (point, heading ?? Heading(-outward));
                }
                default:
                    throw new BridgeError($"Unknown place '{place}': use bay:<site>, commissary:<site> or vehicle:<id>.");
            }
        }

        private static object TeleportPlayer(Vector3 target, float? heading)
        {
            var player = PlayerHelper.PlayerController ?? throw new BridgeError("The player isn't there.");
            if (!NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas))
                throw new BridgeError($"No walkable ground within 3 m of x {target.x:F1}, z {target.z:F1}.");
            var point = hit.position;
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
            var game = SaveGameManager.Current;
            // A walk the game is running (enter, Back to the hatch) would carry on from the new place: dropped first.
            player.ResetNavigation();
            foreach (var v in game.VehicleInstances)
                if (v != null && TruckWindow.PlayerAt(v.id)) TruckWindow.StepOutNow("moved by the test bridge");
            var exited = false;
            if (!string.IsNullOrEmpty(game.ActiveVehicleId))
            {
                VehicleHelper.GetCurrentVehicleBase()?.ExitVehicle();
                exited = true;
            }
            var done = false;
            IEnumerator Move()
            {
                // The game warps the player out of a car a frame after the seat is left (CarController.ExitVehicle).
                if (exited)
                {
                    yield return null;
                    yield return null;
                }
                yield return GameManager.SetPlayerPosition(point);
                if (heading != null) player.transform.rotation = Quaternion.Euler(0f, heading.Value, 0f);
                done = true;
            }
            pump.StartCoroutine(Move());
            Log.Info($"Test bridge: moving the player to x {point.x:F1}, y {point.y:F1}, z {point.z:F1}.");
            var deadline = UnityEngine.Time.realtimeSinceStartup + TeleportSeconds;
            return new Deferred(() =>
            {
                if (!done)
                {
                    if (UnityEngine.Time.realtimeSinceStartup > deadline) throw new BridgeError($"The move didn't finish within {TeleportSeconds:F0} seconds.");
                    return null;
                }
                var now = player.transform.position;
                if (Vector3.Distance(now, point) > 3f)
                    throw new BridgeError($"The game didn't move the player: they are at x {now.x:F1}, z {now.z:F1}.");
                return Pose(now, player.transform.eulerAngles.y);
            });
        }

        private static JToken TeleportVehicle(string id, Vector3 target, float? heading)
        {
            var vehicle = FoodVehicle(id);
            var controller = Spawned(vehicle);
            var record = FoodTrucks.Core.Saves.StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (record?.IsOpen == true) throw new BridgeError($"Vehicle {id} is open for business: close it first (vehicle close).");
            var rotation = Quaternion.Euler(0f, heading ?? controller.transform.eulerAngles.y, 0f);
            VehicleHelper.TeleportVehicleToGround(controller, target, rotation);
            // The game moves the rigidbody; the transform only follows at the next physics step, which a paused game
            // doesn't take. Move it too, so the reply and the saved position are the new place.
            var body = controller.GetComponent<Rigidbody>();
            if (body != null) controller.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            controller.SavePosition();
            var t = controller.transform;
            Log.Info($"Test bridge: moved {vehicle.vehicleTypeName} {id} to x {t.position.x:F1}, z {t.position.z:F1}, heading {t.eulerAngles.y:F0}.");
            return Pose(t.position, t.eulerAngles.y);
        }

        private static JObject Pose(Vector3 p, float heading) => new JObject
        {
            ["x"] = TestBridgeState.Round(p.x), ["y"] = TestBridgeState.Round(p.y), ["z"] = TestBridgeState.Round(p.z),
            ["heading"] = TestBridgeState.Round(heading),
        };

        // ---- vehicle ----

        private static JToken Vehicle(JObject args)
        {
            var id = Need(args, "id", "the food vehicle's id");
            var op = Need(args, "op", "open, close, step_out, back_to_hatch, enter, exit, restock or rent_bay");
            var vehicle = FoodVehicle(id);
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            var game = SaveGameManager.Current;
            var panel = InstanceBehavior<UIs>.Instance?.playerHUD?.itemPanelUI;
            bool IsOpen() => FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.IsOpen == true;
            int Cargo() => vehicle.cargoInstances.Where(c => c != null).Sum(c => c.amount);
            string? Space() => state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == id)?.CommissaryId;
            var cargoBefore = Cargo();
            // Why the mod's own panel refused, when it says so without a notice (the Commissary button's choices).
            string? why = null;
            var spaceBefore = Space();
            void InIt(string what)
            {
                if (game.ActiveVehicleId != id) throw new BridgeError($"{what} is on the vehicle's panel, shown while the player drives the truck or pushes the cart: vehicle enter first.");
            }
            var notices = Notices.Capture(() =>
            {
                switch (op)
                {
                    case "open":
                        InIt("Open here");
                        if (panel == null) throw new BridgeError("The vehicle panel isn't there.");
                        // Open parks through the game's Park button, which does nothing while the game is paused.
                        if (Paused() == true) throw new BridgeError("Open parks with the game's Park button, which doesn't work while paused: unpause first (time).");
                        CartSelling.OpenHere(panel);
                        break;
                    case "close":
                        if (TruckWindow.PanelClose(id)) break;
                        if (FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.IsOpen != true) throw new BridgeError($"Vehicle {id} isn't open.");
                        CartSelling.CloseByPlayer(id);
                        break;
                    case "step_out":
                        if (!TruckWindow.PanelStepOut(id)) throw new BridgeError($"The player isn't at the hatch of {id}.");
                        break;
                    case "back_to_hatch":
                        if (!TruckWindow.CanGoBack(vehicle))
                            throw new BridgeError("Back to the hatch isn't offered now: it needs an open truck and the player on foot, empty-handed and not at a hatch.");
                        TruckWindow.BackToHatch(Spawned(vehicle));
                        break;
                    case "enter":
                        if (game.ActiveVehicleId == id) throw new BridgeError($"The player is in {id} already.");
                        if (!string.IsNullOrEmpty(game.ActiveVehicleId)) throw new BridgeError($"The player is in another vehicle ({game.ActiveVehicleId}): vehicle exit first.");
                        // The vehicle overlay's Enter vehicle button: walks there first when further than the game's reach.
                        Spawned(vehicle).DriveVehicle();
                        break;
                    case "exit":
                        InIt("Park");
                        if (panel == null) throw new BridgeError("The vehicle panel isn't there.");
                        panel.ClickPark();
                        if (game.ActiveVehicleId == id)
                            throw new BridgeError("The game's Park button refused: paused, no parking here, or the panel isn't showing.");
                        break;
                    case "restock":
                        InIt("Commissary");
                        why = global::FoodTrucks.Commissary.RunPanelOption(vehicle, "restock", "nostock");
                        break;
                    case "rent_bay":
                        InIt("Commissary");
                        why = global::FoodTrucks.Commissary.RunPanelOption(vehicle, "rent", "move");
                        break;
                    default:
                        throw new BridgeError($"Unknown op '{op}': open, close, step_out, back_to_hatch, enter, exit, restock or rent_bay.");
                }
            });
            // Whether the operation happened, read from the state it should have changed (a refusal leaves only its notice).
            bool done;
            switch (op)
            {
                case "open": done = IsOpen(); break;
                case "close": done = !IsOpen(); break;
                case "step_out": done = !TruckWindow.PlayerAt(id); break;
                // The walk to the rear door has started, or the player is placed again.
                case "back_to_hatch": done = !TruckWindow.CanGoBack(vehicle); break;
                // In it, or walking to it.
                case "enter": done = game.ActiveVehicleId == id || (PlayerHelper.PlayerController?.hasOnGoalReachedAction ?? false); break;
                case "exit": done = game.ActiveVehicleId != id; break;
                case "restock": done = Cargo() != cargoBefore; break;
                case "rent_bay": done = Space() != null && Space() != spaceBefore; break;
                default: done = false; break;
            }
            Log.Info($"Test bridge: vehicle {id} {op}: {(done ? "done" : "not done")}.");
            return new JObject { ["done"] = done, ["why"] = done ? null : why, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) };
        }

        // ---- commissary, crew ----

        // { "op": "order", "site", "items": [ { "item", "boxes" } ] }: the phone form's confirm without the call.
        private static JToken CommissaryCommand(JObject args)
        {
            var op = Need(args, "op", "order");
            if (op != "order") throw new BridgeError($"Unknown commissary op '{op}': order.");
            var site = Need(args, "site", "the commissary's id");
            if (!(args["items"] is JArray rows) || rows.Count == 0) throw new BridgeError("order needs \"items\": [ { \"item\", \"boxes\" } ].");
            var items = rows.OfType<JObject>().Select(r => (Item: Need(r, "item", "an item key or name"), Boxes: (int)(Num(r, "boxes") ?? throw new BridgeError("Each item needs \"boxes\".")))).ToList();
            var (orders, refusal) = global::FoodTrucks.Commissary.OrderForTesting(site, items);
            if (refusal != null) throw new BridgeError(refusal);
            return new JObject
            {
                ["orders"] = new JArray(orders.Select(o => new JObject
                {
                    ["n"] = o.Id, ["site"] = o.CommissaryId, ["item"] = o.ItemName, ["units"] = o.Units,
                    ["goods"] = o.GoodsCents / 100.0, ["fee"] = o.FeeCents / 100.0, ["total"] = o.TotalCents / 100.0, ["due_day"] = o.DeliverDay,
                })),
            };
        }

        // { "op": "assign" | "unassign", "vehicle", "employee_id" }: the BizMan vehicle page's crew dropdown.
        private static JToken Crew(JObject args)
        {
            var op = Need(args, "op", "assign or unassign");
            var id = Need(args, "vehicle", "the food vehicle's id");
            var employeeId = Need(args, "employee_id", "the employee's id (state.employees)");
            var vehicle = FoodVehicle(id);
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            if (!FoodTrucksSession.CanWrite) throw new BridgeError("The mod's save data is read-only.");
            var record = FoodTrucks.Core.Saves.StreetSales.Find(state, id);
            var crew = record?.Crew ?? new System.Collections.Generic.List<string>();
            var notices = Notices.Capture(() =>
            {
                switch (op)
                {
                    case "assign":
                        if (crew.Contains(employeeId)) throw new BridgeError($"{employeeId} is on {id} already.");
                        if (!HireCrew.Available(state).Any(e => e.id == employeeId))
                            throw new BridgeError($"{employeeId} isn't an unassigned hire (see state.employees).");
                        var kind = FoodTrucksMod.KindOf(vehicle.vehicleTypeName);
                        if (crew.Count >= HireCrew.Limit(kind)) throw new BridgeError($"{id} has its full crew ({crew.Count}).");
                        BizManVehiclePage.ChooseCrew(id, crew.Count, employeeId);
                        break;
                    case "unassign":
                        var slot = crew.IndexOf(employeeId);
                        if (slot < 0) throw new BridgeError($"{employeeId} isn't on {id}.");
                        BizManVehiclePage.ChooseCrew(id, slot, null);
                        break;
                    default:
                        throw new BridgeError($"Unknown crew op '{op}': assign or unassign.");
                }
            });
            var now = FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.Crew ?? new System.Collections.Generic.List<string>();
            var done = op == "assign" ? now.Contains(employeeId) : !now.Contains(employeeId);
            return new JObject { ["done"] = done, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) };
        }

    }
}
