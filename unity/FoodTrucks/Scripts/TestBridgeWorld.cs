#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Helpers;
using Newtonsoft.Json.Linq;
using UI;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    internal static partial class TestBridgeCommands
    {
        internal static VehicleInstance DrivenVehicle(string id)
        {
            var game = SaveGameManager.Current;
            var vehicle = game.VehicleInstances.FirstOrDefault(v => v != null && v.id == id);
            if (vehicle != null && (FoodTrucksMod.KindOf(vehicle.vehicleTypeName) != null || game.ActiveVehicleId == id)) return vehicle;
            if (vehicle != null) throw new BridgeError($"You're not in {id}: get in first (use it, \"Click to drive\").");
            var driven = InstanceBehavior<GameManager>.Instance?.selectedVehicle?.vehicleInstance;
            if (driven != null && driven.id == id) return driven;
            return FoodVehicle(id);
        }

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

        internal static Vector3 Ground(float x, float z)
        {
            if (Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out var hit, 1000f, LayerHelper.groundLayerMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            throw new BridgeError($"No ground found at x {x:F1}, z {z:F1}.");
        }

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
            if (site.InLot) return (point, along, 1);
            if (HatchCheck.Probe(point, Quaternion.LookRotation(along)).Opens) return (point, along, 1);
            if (HatchCheck.Probe(point, Quaternion.LookRotation(-along)).Opens) return (point, -along, 1);
            return (point, along, 1);
        }

        internal static Vector3? EntranceNear(CommissarySite site, float x, float z)
        {
            var doors = World.EntrancesOf(site.Address);
            if (doors == null || doors.Length == 0) return null;
            return doors.OrderBy(d => (d.x - x) * (d.x - x) + (d.z - z) * (d.z - z)).First();
        }

        internal static (Vector3 Door, Vector3 Out) Door(string siteId)
        {
            var (door, outward, _) = DoorView(siteId, 1.5f);
            return (door, outward);
        }

        internal static (Vector3 Door, Vector3 Out, Vector3 View) DoorView(string siteId, float viewMetres)
        {
            var site = Site(siteId);
            var doors = World.EntrancesOf(site.Address);
            if (doors == null || doors.Length == 0) throw new BridgeError($"The building of {siteId} ({site.Address}) isn't in this city.");
            var bay = global::FoodTrucks.Commissary.ResolvedBays(site);
            var door = bay != null && bay.Count > 0 ? EntranceNear(site, (float)bay[0].X, (float)bay[0].Z)!.Value : doors[0];
            Vector3? street = TrafficLanes.Near(door, 40f).Nearest(door)?.Point;
            if (street == null && bay != null && bay.Count > 0) street = new Vector3((float)bay[0].X, door.y, (float)bay[0].Z);
            if (street != null && NavMesh.SamplePosition(door, out var from, 3f, NavMesh.AllAreas) && NavMesh.SamplePosition(street.Value, out var to, 4f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.corners.Length >= 2)
                {
                    var view = Along(path.corners, viewMetres);
                    var first = Along(path.corners, Mathf.Min(1.5f, viewMetres)) - door;
                    first.y = 0f;
                    if (first.sqrMagnitude > 1e-4f) return (door, first.normalized, view);
                }
            }
            Vector3 outward;
            if (street != null) outward = street.Value - door;
            else
            {
                var building = World.AllBuildings().FirstOrDefault(b => b.Doors.Contains(door)).Controller;
                outward = building != null ? door - building.transform.position : Vector3.forward;
            }
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            outward.Normalize();
            return (door, outward, door + outward * viewMetres);
        }

        private static Vector3 Along(Vector3[] corners, float metres)
        {
            for (var i = 1; i < corners.Length; i++)
            {
                var leg = Vector3.Distance(corners[i - 1], corners[i]);
                if (metres <= leg) return Vector3.Lerp(corners[i - 1], corners[i], leg > 0f ? metres / leg : 0f);
                metres -= leg;
            }
            return corners[corners.Length - 1];
        }

        internal static (Vector3 Hatch, Vector3 Out, Vector3 Forward) HatchOf(VehicleInstance vehicle, Transform transform)
        {
            var forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            if (FoodTrucksMod.KindOf(vehicle.vehicleTypeName) == "truck")
            {
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

        private const float TeleportSeconds = 30f;

        private static object Teleport(JObject args)
        {
            var who = Need(args, "who", "\"player\" or a food vehicle's id");
            if (who == DrivingVehicle) throw new BridgeError($"{who} is on a drive: wait for it (drive status).");
            var place = Str(args, "place");
            var heading = (float?)Num(args, "heading");
            var clear = Bool(args, "clear") == true;
            Vector3 target;
            if (place != null && who != "player" && place.StartsWith("bay:", StringComparison.Ordinal))
                return TeleportToBay(who, place.Substring(4), heading, clear);
            string? surface = null;
            if (place != null) (target, heading) = Place(place, who, heading);
            else
            {
                var x = Num(args, "x") ?? throw new BridgeError("teleport needs \"x\" and \"z\", or \"place\".");
                var z = Num(args, "z") ?? throw new BridgeError("teleport needs \"x\" and \"z\", or \"place\".");
                var y = (float?)Num(args, "y");
                var from = who == "player"
                    ? (PlayerHelper.PlayerController ?? throw new BridgeError("The player isn't there.")).transform.position.y
                    : Spawned(FoodVehicle(who)).transform.position.y;
                (target, surface) = GroundPicked((float)x, (float)z, from, y, walkableOnly: who == "player");
                Log.Info($"Test bridge: teleport to x {x:F1}, z {z:F1}: y {target.y:F2}, {surface}.");
            }
            var reply = who == "player" ? TeleportPlayer(target, heading, clear) : TeleportVehicle(who, target, heading, clear);
            if (surface == null) return reply;
            if (reply is JObject pose)
            {
                pose["surface"] = surface;
                return pose;
            }
            if (reply is Deferred later)
                return new Deferred(() =>
                {
                    var result = later.Poll();
                    if (result is JObject done) done["surface"] = surface;
                    return result;
                }, later.OnStop);
            return reply;
        }

        internal static (Vector3 Point, string Why) GroundPicked(float x, float z, float referenceY, float? explicitY, bool walkableOnly = false)
        {
            var points = new List<Vector3>();
            var top = 500f;
            for (var i = 0; i < 8; i++)
            {
                if (!Physics.Raycast(new Vector3(x, top, z), Vector3.down, out var hit, top + 500f, LayerHelper.groundLayerMask, QueryTriggerInteraction.Ignore)) break;
                if (points.Count == 0 || points[points.Count - 1].y - hit.point.y > 0.5f) points.Add(hit.point);
                top = hit.point.y - 0.5f;
            }
            if (points.Count == 0)
            {
                if (explicitY != null && !walkableOnly) return (new Vector3(x, explicitY.Value, z), $"no ground under the point; the y asked for ({explicitY.Value:F2})");
                throw new BridgeError($"No ground found at x {x:F1}, z {z:F1}.");
            }
            List<(double, bool)> surfaces;
            var route = "";
            if (walkableOnly)
            {
                var filter = PlayerFilter(out var start);
                if (start != null && new Vector2(start.Value.x - x, start.Value.z - z).magnitude > RouteMetres)
                {
                    route = $"; walking routes not checked (more than {RouteMetres:F0} m from the player)";
                    start = null;
                }
                else route = start == null
                    ? "; walking routes not checked (the player isn't on the NavMesh)"
                    : "; walking routes checked from the player's place";
                surfaces = points.Select(p => ((double)p.y, Walkable(p, filter, start))).ToList();
            }
            else surfaces = points.Select(p => ((double)p.y, NavMesh.SamplePosition(p, out var near, 3f, NavMesh.AllAreas) && Mathf.Abs(near.position.y - p.y) < 1f)).ToList();
            var (index, why) = FoodTrucks.Core.Common.GroundPick.Choose(surfaces, referenceY, explicitY, walkableOnly);
            why += route;
            var kind = walkableOnly ? "walkable ground" : "ground";
            if (index < 0)
                throw new BridgeError(explicitY != null
                    ? $"No {kind} at x {x:F1}, z {z:F1} near y {explicitY:F2}: {why}."
                    : $"No {kind} at x {x:F1}, z {z:F1}: {why}.");
            return (points[index], why);
        }

        private const float RouteMetres = 100f;

        internal static NavMeshQueryFilter PlayerFilter(out Vector3? start)
        {
            start = null;
            var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            var controller = PlayerHelper.PlayerController;
            var agent = controller != null && controller.Character != null ? controller.Character.navmeshAgent : null;
            if (agent != null)
            {
                filter.agentTypeID = agent.agentTypeID;
                filter.areaMask = agent.areaMask;
            }
            var game = SaveGameManager.Current;
            if (controller != null && game != null && string.IsNullOrEmpty(game.ActiveVehicleId) && !BuildingManager.IsInsideBuilding &&
                NavMesh.SamplePosition(controller.transform.position, out var here, 1f, filter))
                start = here.position;
            return filter;
        }

        private static bool Walkable(Vector3 surface, NavMeshQueryFilter filter, Vector3? start)
        {
            if (!NavMesh.SamplePosition(surface, out var near, 3f, filter) || Mathf.Abs(near.position.y - surface.y) >= 1f) return false;
            if (start == null) return true;
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(start.Value, near.position, filter, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        private sealed class Blocker
        {
            public string What = "";
            public GameObject? LaneCar;
            public UnityEngine.Object? Thing;
            public bool Hidden;
            public bool Traffic;
        }

        private static readonly HashSet<string> footprintLogged = new HashSet<string>(StringComparer.Ordinal);

        private static Bounds LocalBounds(VehicleController controller)
        {
            var toVehicle = controller.transform.worldToLocalMatrix;
            var parts = new List<(double[] Matrix, FoodTrucks.Core.Fleet.LocalBox Box)>();
            foreach (var c in controller.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                Vector3 centre, size;
                switch (c)
                {
                    case BoxCollider box:
                        centre = box.center;
                        size = box.size;
                        break;
                    case SphereCollider sphere:
                        centre = sphere.center;
                        size = Vector3.one * sphere.radius * 2f;
                        break;
                    case CapsuleCollider capsule:
                        centre = capsule.center;
                        size = Vector3.one * capsule.radius * 2f;
                        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        centre = mesh.sharedMesh.bounds.center;
                        size = mesh.sharedMesh.bounds.size;
                        break;
                    default:
                        continue;
                }
                var m = toVehicle * c.transform.localToWorldMatrix;
                parts.Add((new double[] { m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23 },
                    new FoodTrucks.Core.Fleet.LocalBox((centre.x, centre.y, centre.z), (size.x, size.y, size.z))));
            }
            var found = FoodTrucks.Core.Fleet.Footprint.Box(parts);
            var bounds = found == null
                ? new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(2.2f, 2.4f, 6f))
                : new Bounds(new Vector3((float)(found.Value.Min.X + found.Value.Max.X) / 2f, (float)(found.Value.Min.Y + found.Value.Max.Y) / 2f, (float)(found.Value.Min.Z + found.Value.Max.Z) / 2f),
                    new Vector3((float)(found.Value.Max.X - found.Value.Min.X), (float)(found.Value.Max.Y - found.Value.Min.Y), (float)(found.Value.Max.Z - found.Value.Min.Z)));
            var type = controller.vehicleInstance?.vehicleTypeName ?? controller.name;
            if (footprintLogged.Add(type))
                Log.Info($"Test bridge: footprint of {type}: {bounds.size.x:F2} m across, {bounds.size.z:F2} m long, {bounds.size.y:F2} m high, " +
                         $"centre {bounds.center.x:F2}, {bounds.center.y:F2}, {bounds.center.z:F2} ({(found == null ? "no colliders; the default" : parts.Count + " colliders")}).");
            return bounds;
        }

        private static bool Ignored(Collider c)
        {
            var layer = c.gameObject.layer;
            if (layer == LayerHelper.ParkedVehiclesLayerIndex || layer == LayerHelper.AiVehiclesLayerIndex) return false;
            return c.isTrigger || ((1 << layer) & (LayerHelper.groundLayerMask.value | LayerHelper.parkingAreaLayerMask.value)) != 0;
        }

        // `sceneryCounts` false (the player): a static collider (no Rigidbody, no NavMeshObstacle) doesn't count, since
        // the landing point is on the NavMesh, which is already built round anything a person can't walk through. Live
        // session 6: BridgeMiddleRoadColliders (layer StreetProps), which keep traffic out of the middle of the road
        // under the bridge, refused a player at x -1515, z -1150 where the NavMesh runs. Vehicles, people, bodies and
        // carving obstacles (the mod's bay props) still count.
        private static List<Blocker> Describe(IEnumerable<Collider> hits, Transform? self, bool sceneryCounts = true)
        {
            var found = new Dictionary<UnityEngine.Object, Blocker>();
            foreach (var c in hits)
            {
                if (c == null || Ignored(c) || (self != null && c.transform.IsChildOf(self))) continue;
                var car = BayParking.CarOf(c);
                if (car != null && BayParking.IsLaneCar(car))
                {
                    if (!found.ContainsKey(car)) found[car] = new Blocker { What = $"a parked car ({car.name})", LaneCar = car, Thing = car };
                    continue;
                }
                var vehicle = c.GetComponentInParent<VehicleController>();
                if (vehicle != null)
                {
                    if (!found.ContainsKey(vehicle)) found[vehicle] = new Blocker { What = $"the player's {vehicle.vehicleInstance?.vehicleTypeName} {vehicle.vehicleInstance?.id}", Thing = vehicle.gameObject };
                    continue;
                }
                var player = c.GetComponentInParent<PlayerController>();
                if (player != null)
                {
                    if (!found.ContainsKey(player)) found[player] = new Blocker { What = "the player", Thing = player.gameObject };
                    continue;
                }
                var van = c.GetComponentInParent<Vehicles.DeliveryDriverJob.DeliveryJobStartController>();
                if (van != null)
                {
                    if (!found.ContainsKey(van)) found[van] = new Blocker { What = $"the building's delivery-job van ({van.name})", Thing = van.gameObject };
                    continue;
                }
                var person = c.GetComponentInParent<ThirdPersonCharacter>();
                if (person != null)
                {
                    if (!found.ContainsKey(person)) found[person] = new Blocker { What = $"a person ({person.name})", Thing = person.gameObject };
                    continue;
                }
                if (!sceneryCounts && c.attachedRigidbody == null && c.GetComponentInParent<NavMeshObstacle>() == null) continue;
                var key = (UnityEngine.Object?)c.attachedRigidbody ?? c.transform.root;
                if (!found.ContainsKey(key))
                    found[key] = new Blocker
                    {
                        What = $"{c.transform.root.name}/{c.name} (layer {LayerMask.LayerToName(c.gameObject.layer)})", Thing = key is Component k ? (UnityEngine.Object)k.gameObject : key,
                        Traffic = c.gameObject.layer == LayerHelper.AiVehiclesLayerIndex && c.GetComponentInParent<GleyTrafficSystem.VehicleComponent>() != null,
                    };
            }
            return found.Values.ToList();
        }

        private static List<Blocker> InTheWay(VehicleController controller, Vector3 ground, Quaternion rotation,
            IReadOnlyList<Vehicles.DeliveryDriverJob.DeliveryJobStartController>? vans = null)
        {
            var local = LocalBounds(controller);
            var bottom = Mathf.Min(local.min.y, 0f) + 0.3f;
            var top = Mathf.Max(local.max.y, bottom + 0.5f);
            var centre = new Vector3(local.center.x, (bottom + top) / 2f, local.center.z);
            var half = new Vector3(Mathf.Max(0.05f, local.extents.x - 0.1f), (top - bottom) / 2f, Mathf.Max(0.05f, local.extents.z - 0.1f));
            var hits = Physics.OverlapBox(ground + rotation * centre, half, rotation, ~0, QueryTriggerInteraction.Collide);
            var found = Describe(hits, controller.transform);
            var middle = ground + rotation * new Vector3(local.center.x, 0f, local.center.z);
            var forward = rotation * Vector3.forward;
            var mine = new FoodTrucks.Core.Fleet.FlatBox(middle.x, middle.z, forward.x, forward.z, local.extents.x, local.extents.z);
            var mySpan = ((double)(ground.y + bottom), (double)(ground.y + top));
            foreach (var (car, what, laneCar, hidden) in CarsNear(middle, 15f, vans ?? Vans()))
            {
                if (found.Any(b => b.Thing == car) || !CarFootprint(car, out var theirs, out var theirSpan)) continue;
                if (FoodTrucks.Core.Fleet.Footprint.Overlap(mine, mySpan, theirs, theirSpan, -0.05))
                    found.Add(new Blocker { What = what, LaneCar = laneCar ? car : null, Thing = car, Hidden = hidden });
            }
            return found;
        }

        private static List<Vehicles.DeliveryDriverJob.DeliveryJobStartController> Vans() =>
            UnityEngine.Object.FindObjectsByType<Vehicles.DeliveryDriverJob.DeliveryJobStartController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(v => v != null).ToList();

        private static IEnumerable<(GameObject Car, string What, bool LaneCar, bool Hidden)> CarsNear(Vector3 point, float radius,
            IReadOnlyList<Vehicles.DeliveryDriverJob.DeliveryJobStartController> vans)
        {
            var lanes = new HashSet<ParkingLaneGenerator>();
            foreach (var c in Physics.OverlapSphere(point, radius, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide))
            {
                var lane = c == null ? null : c.GetComponentInParent<ParkingLaneGenerator>();
                if (lane != null) lanes.Add(lane);
            }
            foreach (var lane in lanes)
                foreach (Transform child in lane.transform)
                    if (BayParking.IsLaneCar(child.gameObject) && Flat(child.position - point) <= radius + 5f)
                        yield return (child.gameObject, $"a parked car ({child.name})", true, false);
            foreach (var van in vans)
                if (van != null && Flat(van.transform.position - point) <= radius + 5f)
                    yield return (van.gameObject, van.gameObject.activeInHierarchy
                        ? $"the building's delivery-job van ({van.name})"
                        : $"the building's delivery-job van ({van.name}, hidden now; the game shows it again in its stall)", false, !van.gameObject.activeInHierarchy);
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private static bool CarFootprint(GameObject car, out FoodTrucks.Core.Fleet.FlatBox box, out (double Bottom, double Top) span)
        {
            box = default;
            span = default;
            var t = car.transform;
            var forward = t.forward;
            if (new Vector2(forward.x, forward.z).sqrMagnitude < 1e-6f) return false;
            var mesh = car.GetComponent<MeshCollider>()?.sharedMesh;
            if (mesh != null)
            {
                var b = mesh.bounds;
                var scale = t.lossyScale;
                var middle = t.TransformPoint(b.center);
                box = new FoodTrucks.Core.Fleet.FlatBox(middle.x, middle.z, forward.x, forward.z, b.extents.x * Mathf.Abs(scale.x), b.extents.z * Mathf.Abs(scale.z));
                var high = b.extents.y * Mathf.Abs(scale.y);
                span = (middle.y - high, middle.y + high);
                return true;
            }
            var renderers = car.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            var all = renderers[0].bounds;
            foreach (var r in renderers) all.Encapsulate(r.bounds);
            var reach = Mathf.Max(all.extents.x, all.extents.z);
            box = new FoodTrucks.Core.Fleet.FlatBox(all.center.x, all.center.z, 0, 1, reach, reach);
            span = (all.min.y, all.max.y);
            return true;
        }

        private static List<Blocker> InTheWay(PlayerController player, Vector3 ground)
        {
            var hits = Physics.OverlapCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 1.6f, 0.2f, ~0, QueryTriggerInteraction.Ignore);
            return Describe(hits, player.transform, sceneryCounts: false);
        }

        private static void MakeRoom(List<Blocker> blockers, bool clear, string where)
        {
            if (blockers.Count == 0) return;
            if (clear && blockers.All(b => b.LaneCar != null))
            {
                foreach (var b in blockers)
                    if (!BayParking.Release(b.LaneCar!)) throw new BridgeError($"{b.What} at {where} couldn't be removed.");
                Physics.SyncTransforms();
                Log.Info($"Test bridge: cleared {string.Join(", ", blockers.Select(b => b.What))} at {where}.");
                return;
            }
            var names = string.Join(", ", blockers.Select(b => b.What));
            var hint = blockers.All(b => b.LaneCar != null) ? " Add \"clear\": true to send the parked cars away." : "";
            throw new BridgeError($"Something stands at {where}: {names}.{hint}");
        }

        private static JToken TeleportToBay(string id, string siteId, float? heading, bool clear)
        {
            var vehicle = FoodVehicle(id);
            var controller = Spawned(vehicle);
            var bay = Bay(siteId);
            var rotation = Quaternion.Euler(0f, heading ?? Heading(bay.Along), 0f);
            var halfLength = LocalBounds(controller).extents.z;
            var places = new List<Vector3> { bay.Point };
            var zones = BayMarkers.ZonesOf(Site(siteId), (float)(DemandData.Rules?.Commissary?.BayRadiusMetres ?? 15));
            foreach (var zone in zones)
                foreach (var (from, to) in zone.Spans)
                    for (var t = from + halfLength; t <= to - halfLength + 1e-3f; t += 0.5f)
                    {
                        var p = zone.Foot + zone.Along * t;
                        places.Add(Ground(p.x, p.z));
                    }
            var ordered = places.Distinct().OrderBy(p => (p - bay.Point).sqrMagnitude).ToList();
            var vans = Vans();
            List<Blocker>? atMiddle = null;
            foreach (var place in ordered)
            {
                var blockers = InTheWay(controller, place, rotation, vans);
                atMiddle ??= blockers;
                if (blockers.Count == 0) return TeleportVehicle(id, place, rotation.eulerAngles.y, clear, vans: vans);
            }
            return TeleportVehicle(id, bay.Point, rotation.eulerAngles.y, clear, atMiddle, vans);
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
                    return (door + outward * 1.5f, heading ?? Heading(-outward));
                }
                case "vehicle":
                {
                    if (who != "player") throw new BridgeError("vehicle: places the player beside a vehicle.");
                    var any = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == id);
                    if (any != null && FoodTrucksMod.KindOf(any.vehicleTypeName) == null)
                    {
                        var car = Spawned(any);
                        var side = car.transform.position - car.transform.right * 2f;
                        return (NavMesh.SamplePosition(side, out var beside, 3f, PlayerFilter(out _)) ? beside.position : side, null);
                    }
                    var vehicle = FoodVehicle(id);
                    var controller = Spawned(vehicle);
                    var (hatch, outward, _) = HatchOf(vehicle, controller.transform);
                    var flat = controller.transform.position;
                    var along = hatch - flat;
                    var halfWidth = FoodTrucksMod.KindOf(vehicle.vehicleTypeName) == "truck" ? 1.1f : 0.6f;
                    var point = flat + Vector3.Project(along, controller.transform.forward) + outward * (halfWidth + 1.5f);
                    return (point, heading ?? Heading(-outward));
                }
                case "address":
                {
                    if (who != "player") throw new BridgeError("address: places the player at a building's entrance.");
                    if (!AddressForm.IsMatch(id))
                        throw new BridgeError($"address: takes a building's address as the game writes it, ba:street_<name>#<number>; got '{id}'.");
                    var doors = World.EntrancesOf(id) ?? throw new BridgeError($"no building at {id} in this city");
                    var building = World.BuildingAt(id);
                    var door = doors[0];
                    var outward = building == null ? Vector3.forward : Vector3.ProjectOnPlane(door - building.transform.position, Vector3.up);
                    outward = outward.sqrMagnitude < 0.01f ? Vector3.forward : outward.normalized;
                    return (door + outward * 1.5f, heading ?? Heading(-outward));
                }
                case "exit":
                {
                    if (who != "player") throw new BridgeError("exit: walks the player out of a building.");
                    if (!BuildingManager.IsInsideBuilding) throw new BridgeError("You're not inside a building.");
                    var from = PlayerHelper.GetPosition();
                    var exits = UnityEngine.Object.FindObjectsOfType<ExitZoneDespawner>()
                        .Where(e => e != null && e.isActiveAndEnabled && !e.isParkingExit && e.GetComponent<Collider>() != null)
                        .Select(e => e.GetComponent<Collider>().bounds.center)
                        .OrderBy(p => (p - from).sqrMagnitude).ToList();
                    if (exits.Count == 0) throw new BridgeError("No way out found in this building.");
                    return (exits[0], heading);
                }
                default:
                    throw new BridgeError($"Unknown place '{place}': use bay:<site>, commissary:<site>, vehicle:<id>, address:<address> or exit.");
            }
        }

        private static string Blockers(PlayerController player)
        {
            try
            {
                var set = HarmonyLib.Traverse.Create(player).Field("_activeNavigationBlockers").GetValue() as System.Collections.IEnumerable;
                var names = set == null ? new List<string>() : set.Cast<object>().Select(b => b.ToString()).ToList();
                return names.Count == 0 ? "; no navigation blocker is set" : $"; navigation blocked by {string.Join(", ", names)}";
            }
            catch (Exception ex) { return $"; blockers unreadable ({ex.Message})"; }
        }

        private const float WalkSeconds = 600f;
        private const float ArrivedMetres = 1.5f;
        private const float StoppedSeconds = 3f;

        private static object Walk(JObject args)
        {
            var player = PlayerHelper.PlayerController ?? throw new BridgeError("The player isn't there.");
            if (Paused() == true) throw new BridgeError("The game is paused: walking takes real time. Set a speed first.");
            var game = SaveGameManager.Current;
            var holding = string.IsNullOrEmpty(game.ActiveVehicleId) ? null : game.ActiveVehicleId;
            var selected = InstanceBehavior<GameManager>.Instance.selectedVehicle;
            if (holding != null && (selected == null || selected.vehicleType == null || !selected.vehicleType.spawnInPlayerObject))
                throw new BridgeError("You're in a vehicle you drive: get out first (vehicle ID exit), or push a cart instead.");
            if (TruckWindow.PlayerAt(holding ?? "") || game.VehicleInstances.Any(v => v != null && TruckWindow.PlayerAt(v.id)))
                throw new BridgeError("You're at a truck's hatch: step out first (vehicle ID exit).");
            var place = Str(args, "place");
            Vector3 target;
            if (place != null) (target, _) = Place(place, "player", null);
            else
            {
                var x = Num(args, "x") ?? throw new BridgeError("walk needs \"x\" and \"z\", or \"place\".");
                var z = Num(args, "z") ?? throw new BridgeError("walk needs \"x\" and \"z\", or \"place\".");
                target = new Vector3((float)x, player.transform.position.y, (float)z);
            }
            var agent = player.Character != null ? player.Character.navmeshAgent : null;
            if (agent == null || !agent.enabled) throw new BridgeError("The player can't walk now (navigation is off: sitting, working or in a menu).");
            var filter = PlayerFilter(out _);
            if (!NavMesh.SamplePosition(player.transform.position, out var from, 2f, filter))
                throw new BridgeError("You aren't standing on walkable ground.");
            if (!NavMesh.SamplePosition(target, out var to, 3f, filter) && !NavMesh.SamplePosition(new Vector3(target.x, from.position.y, target.z), out to, 3f, filter))
                throw new BridgeError($"No walkable ground within 3 m of x {target.x:F1}, z {target.z:F1}.");
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from.position, to.position, filter, path) || path.status == NavMeshPathStatus.PathInvalid)
                throw new BridgeError($"No walking route to x {to.position.x:F1}, z {to.position.z:F1} from here.");
            var metres = 0f;
            for (var i = 1; i < path.corners.Length; i++) metres += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            var complete = path.status == NavMeshPathStatus.PathComplete;
            var goal = to.position;
            var stops = PavementRoute(from.position, goal);
            var stop = 0;
            var waitedSeconds = 0f;
            var crossings = 0;
            float? waitingSince = null;
            string? lastTraffic = null;
            player.SetNewDestination(stops != null ? stops[0] : goal, showParticleEffect: true);
            if (!agent.hasPath && !agent.pathPending && Flat(player.transform.position, goal) > ArrivedMetres)
                throw new BridgeError("The game didn't start the walk (a menu or the map may be open, or navigation is blocked here)" +
                                      $"{Blockers(player)}.");
            Log.Info($"Test bridge: walking to x {goal.x:F1}, z {goal.z:F1} ({metres:F0} m{(complete ? "" : ", part of the way")}" +
                     $"{(holding != null ? ", pushing " + holding : "")}" +
                     $"{(stops != null ? $", on the pavement: {stops.Count} legs" : ", straight: no pavement route")}).");
            var deadline = UnityEngine.Time.realtimeSinceStartup + WalkSeconds;
            var insideBefore = InsideAddress();
            var last = player.transform.position;
            var stillSince = UnityEngine.Time.realtimeSinceStartup;
            JObject Reply(bool arrived)
            {
                var now = player.transform.position;
                var reply = Pose(now, player.transform.eulerAngles.y);
                reply["arrived"] = arrived;
                reply["from_goal"] = Math.Round(Flat(now, goal), 2);
                reply["goal"] = new JObject { ["x"] = TestBridgeState.Round(goal.x), ["z"] = TestBridgeState.Round(goal.z) };
                reply["path"] = complete ? "complete" : "partial";
                reply["metres"] = Math.Round(metres);
                reply["holding"] = holding;
                reply["inside"] = InsideAddress();
                reply["route"] = stops != null ? "pavement" : "straight";
                reply["crossings"] = crossings;
                reply["waited_for_traffic_s"] = Math.Round(waitedSeconds);
                return reply;
            }
            return new Deferred(() =>
            {
                if (insideBefore != null && InsideAddress() != insideBefore)
                {
                    if (InstanceBehavior<BuildingManager>.Instance != null && InstanceBehavior<BuildingManager>.Instance.enteringBuilding) return null;
                    var outside = Reply(true);
                    outside["left"] = insideBefore;
                    return outside;
                }
                var now = player.transform.position;
                if (Flat(now, goal) <= ArrivedMetres) return Reply(true);
                if (stops != null && stop < stops.Count - 1 && Flat(now, stops[stop]) <= LegMetres)
                {
                    var crossing = CrossesRoadway(stops[stop], stops[stop + 1]);
                    var clock = UnityEngine.Time.realtimeSinceStartup;
                    if (crossing && waitingSince == null) waitingSince = clock;
                    if (crossing && MustWait(stops[stop], stops[stop + 1], clock - waitingSince!.Value, out var nearest) && clock - waitingSince < MaxTrafficWaitSeconds)
                    {
                        lastTraffic = nearest;
                        stillSince = clock;
                        return null;
                    }
                    if (waitingSince != null)
                    {
                        var waited = clock - waitingSince.Value;
                        waitedSeconds += waited; crossings++; waitingSince = null;
                        Log.Info($"Test bridge: walk crosses from x {stops[stop].x:F1}, z {stops[stop].z:F1} to x {stops[stop + 1].x:F1}, z {stops[stop + 1].z:F1}" +
                                 $" after waiting {waited:F0} s{(waited >= MaxTrafficWaitSeconds ? " (gave up waiting)" : "")}{(lastTraffic != null ? "; last car seen: " + lastTraffic : "")}.");
                        lastTraffic = null;
                    }
                    stop++;
                    player.SetNewDestination(stops[stop], showParticleEffect: false);
                    stillSince = clock;
                }
                if (Vector3.Distance(now, last) > 0.05f)
                {
                    last = now;
                    stillSince = UnityEngine.Time.realtimeSinceStartup;
                }
                if (UnityEngine.Time.realtimeSinceStartup - stillSince > StoppedSeconds && !agent.pathPending)
                {
                    Log.Info($"Test bridge: walk stopped at x {now.x:F1}, z {now.z:F1}, {Flat(now, goal):F0} m short" +
                             (stops != null ? $", on leg {stop + 1} of {stops.Count} towards x {stops[stop].x:F1}, z {stops[stop].z:F1}" +
                                              $" ({Flat(now, stops[stop]):F1} m off; the player's own path there: {agent.pathStatus})" : "") + ".");
                    return Reply(false);
                }
                if (UnityEngine.Time.realtimeSinceStartup > deadline) return Reply(false);
                return null;
            });
        }

        private const float LegMetres = 0.8f;
        private const float TrafficNearMetres = 8f;
        private const float TrafficMetres = 25f;
        private const float MaxTrafficWaitSeconds = 60f;

        private static List<Vector3>? PavementRoute(Vector3 from, Vector3 goal)
        {
            var filter = Extensions.NavMeshHelper.NpcNavMeshFilter;
            if (!NavMesh.SamplePosition(from, out var start, 3f, filter) || !NavMesh.SamplePosition(goal, out var end, 3f, filter)) return null;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, end.position, filter, path) || path.status == NavMeshPathStatus.PathInvalid || path.corners.Length == 0)
                return null;
            if (Flat(path.corners[path.corners.Length - 1], goal) > 3f) return null;
            var stops = path.corners.Skip(1).ToList();
            stops.Add(goal);
            return stops;
        }

        private static bool CrossesRoadway(Vector3 a, Vector3 b)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt((float)Flat(a, b) / 0.5f));
            for (var i = 0; i <= steps; i++)
            {
                var point = Vector3.Lerp(a, b, i / (float)steps);
                if (Legality.RoadBelow(point) || Legality.NearCrosswalk(point, 0.3f)) return true;
            }
            return false;
        }

        private const float LightWaitSeconds = 30f;

        private static bool MustWait(Vector3 a, Vector3 b, float waitedSoFar, out string? why)
        {
            var light = CrossingLight(a, b);
            if (light != null)
            {
                // A light that hasn't turned in LightWaitSeconds is one the game isn't cycling here (attempt 7: four waits
                // of an hour with the cars' light never red); then a gap in the moving traffic is enough.
                if (!light.activeSelf && waitedSoFar < LightWaitSeconds) { why = "the lights are green for the cars"; return true; }
                return TrafficComing(a, b, out why, nearOnly: true);
            }
            return TrafficComing(a, b, out why, nearOnly: false);
        }

        private static GameObject? CrossingLight(Vector3 a, Vector3 b)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt((float)Flat(a, b) / 1f));
            for (var i = 0; i <= steps; i++)
            {
                var point = Vector3.Lerp(a, b, i / (float)steps);
                foreach (var hit in Physics.OverlapSphere(point + Vector3.up * 0.5f, 1f, ~0, QueryTriggerInteraction.Collide))
                {
                    if (hit == null || !hit.CompareTag("Crosswalk")) continue;
                    var link = hit.GetComponent<CrosswalkToTrafficLightLink>();
                    if (link != null && link.redTrafficLight != null) return link.redTrafficLight;
                }
            }
            return null;
        }

        private static bool TrafficComing(Vector3 a, Vector3 b, out string? nearest, bool nearOnly)
        {
            nearest = null;
            var mask = LayerMask.GetMask("AiVehicles");
            if (mask == 0) return false;
            var middle = Vector3.Lerp(a, b, 0.5f);
            var reach = TrafficMetres + Flat(a, b) / 2f;
            var found = false;
            var closest = float.MaxValue;
            foreach (var hit in Physics.OverlapSphere(middle, (float)reach, mask, QueryTriggerInteraction.Ignore))
            {
                var body = hit != null ? hit.attachedRigidbody : null;
                if (body == null || body.isKinematic) continue;
                var at = body.worldCenterOfMass;
                var distance = Mathf.Min((float)Flat(at, a), Mathf.Min((float)Flat(at, b), (float)Flat(at, middle)));
                var velocity = new Vector3(body.velocity.x, 0f, body.velocity.z);
                var speed = velocity.magnitude;
                var towards = middle - at; towards.y = 0f;
                var closing = towards.sqrMagnitude > 0.01f && Vector3.Dot(velocity, towards.normalized) > 0.5f;
                if (speed >= 1f && (distance <= TrafficNearMetres || (!nearOnly && distance <= TrafficMetres && closing)))
                {
                    found = true;
                    if (distance < closest) { closest = distance; nearest = $"{distance:F0} m away at {speed * 3.6f:F0} km/h"; }
                }
            }
            return found;
        }

        private static object TeleportPlayer(Vector3 target, float? heading, bool clear)
        {
            var player = PlayerHelper.PlayerController ?? throw new BridgeError("The player isn't there.");
            if (!NavMesh.SamplePosition(target, out var hit, 3f, PlayerFilter(out _)))
                throw new BridgeError($"No walkable ground within 3 m of x {target.x:F1}, z {target.z:F1}.");
            if (Mathf.Abs(hit.position.y - target.y) > 1f)
                throw new BridgeError($"No walkable ground within 3 m of x {target.x:F1}, y {target.y:F2}, z {target.z:F1} (the nearest is at y {hit.position.y:F2}).");
            var point = hit.position;
            MakeRoom(InTheWay(player, point), clear, $"x {point.x:F1}, z {point.z:F1}");
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
            var game = SaveGameManager.Current;
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

        private static JToken TeleportVehicle(string id, Vector3 target, float? heading, bool clear, List<Blocker>? blockers = null,
            List<Vehicles.DeliveryDriverJob.DeliveryJobStartController>? vans = null)
        {
#if FOODTRUCKS_CREW_DRIVING_DEV
            if (CrewDriveProbe.Owns(id)) throw new BridgeError("Truck belongs to the crew driving probe; stop its generation before teleporting.");
#endif
            vans ??= Vans();
            var vehicle = DrivenVehicle(id);
            var controller = Spawned(vehicle);
            var record = FoodTrucks.Core.Saves.StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (record?.IsOpen == true) throw new BridgeError($"Vehicle {id} is open for business: close it first (vehicle close).");
            var rotation = Quaternion.Euler(0f, heading ?? controller.transform.eulerAngles.y, 0f);
            MakeRoom(blockers ?? InTheWay(controller, target, rotation, vans), clear, $"x {target.x:F1}, z {target.z:F1}");
            // NWH's Vehicle.Awake sets the body to interpolate. With no physics step (a paused clock) interpolation keeps
            // drawing the vehicle between its last two physics poses, so a truck that had been driven showed, and state
            // read, a point 15 to 90% of the way from its old place to the new one until the clock ran (live session 5,
            // found by the mod agent). Interpolation is off for the move and comes back after the next physics step.
            var body = controller.GetComponent<Rigidbody>();
            if (body != null) InterpolationOff(body);
            try
            {
                VehicleHelper.TeleportVehicleToGround(controller, target, rotation);
                // The game moves the rigidbody; the transform only follows at the next physics step, which a paused game
                // doesn't take. Move it too, so the reply and the saved position are the new place.
                if (body != null) controller.transform.SetPositionAndRotation(body.position, body.rotation);
                Physics.SyncTransforms();
            }
            catch
            {
                if (body != null) RestoreInterpolation(body);
                throw;
            }
            if (body != null)
            {
                var pump = TestBridgePump.Instance;
                if (pump != null) pump.StartCoroutine(InterpolateAfterStep(body));
                else RestoreInterpolation(body);
            }
            var game = SaveGameManager.Current;
            if (controller is CarController car)
            {
                car.isCheckedForParkingZone = false;
                if (game.ActiveVehicleId == id) car.UpdateParkingZone();
                else
                {
                    var legal = Legality.GameParkingReading(controller.transform, out var neighbourhood);
                    vehicle.parkingState = legal ? ParkingState.Legal : ParkingState.Illegal;
                    vehicle.parkingNeighbourhood = neighbourhood;
                }
            }
            controller.SavePosition();
            var t = controller.transform;
            Log.Info($"Test bridge: moved {vehicle.vehicleTypeName} {id} to x {t.position.x:F1}, z {t.position.z:F1}, heading {t.eulerAngles.y:F0}.");
            var reply = Pose(t.position, t.eulerAngles.y);
            // Tested again where the game put it (on the road under the point, which may differ from the ground probed).
            // A hidden delivery-job van there is a warning, not a fault: nothing stands there now, but the game shows the van
            // in its stall again later (review round 6). It is listed as "…, hidden now; …" and logged as Info with
            // "(hidden)"; anything else standing there is an ERROR.
            var after = InTheWay(controller, t.position, t.rotation, vans);
            reply["in_the_way_after"] = new JArray(after.Select(b => b.What));
            var shown = after.Where(b => !b.Hidden).Select(b => b.What).ToList();
            var hidden = after.Where(b => b.Hidden).Select(b => b.What).ToList();
            if (shown.Count > 0)
                Log.Error($"Test bridge: {vehicle.vehicleTypeName} {id} stands in something after the move: {string.Join(", ", shown)}.");
            if (hidden.Count > 0)
                Log.Info($"Test bridge: {vehicle.vehicleTypeName} {id} stands where a hidden delivery-job van has its stall (hidden): {string.Join(", ", hidden)}.");
            return reply;
        }

        private static readonly Dictionary<Rigidbody, RigidbodyInterpolation> interpolationOff = new Dictionary<Rigidbody, RigidbodyInterpolation>();

        private static void InterpolationOff(Rigidbody body)
        {
            if (!interpolationOff.ContainsKey(body)) interpolationOff[body] = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
        }

        private static void RestoreInterpolation(Rigidbody body)
        {
            if (!interpolationOff.TryGetValue(body, out var mode)) return;
            interpolationOff.Remove(body);
            if (body != null) body.interpolation = mode;
        }

        internal static void RestoreAllInterpolation()
        {
            foreach (var pair in interpolationOff.ToList())
                if (pair.Key != null) pair.Key.interpolation = pair.Value;
            interpolationOff.Clear();
        }

        private static IEnumerator InterpolateAfterStep(Rigidbody body)
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            RestoreInterpolation(body);
        }

        private static JObject Pose(Vector3 p, float heading) => new JObject
        {
            ["x"] = TestBridgeState.Round(p.x), ["y"] = TestBridgeState.Round(p.y), ["z"] = TestBridgeState.Round(p.z),
            ["heading"] = TestBridgeState.Round(heading),
        };

        private static object Vehicle(JObject args)
        {
            var op = Need(args, "op", "open, close, fast_forward, skip_work, stop_work, work_window, step_out, back_to_hatch, enter, exit, restock, transfer, rent_bay or buy");
            if (op == "buy") return Buy(args);
            var id = Need(args, "id", "the food vehicle's id");
#if FOODTRUCKS_CREW_DRIVING_DEV
            if (op != "enter" && CrewDriveProbe.Owns(id)) throw new BridgeError("Truck belongs to the crew driving probe; stop it first.");
#endif
            if (id == DrivingVehicle) throw new BridgeError($"{id} is on a drive: wait for it (drive status).");
            var vehicle = (op == "enter" || op == "exit") && SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == id) is { } any
                ? any : op == "exit" ? DrivenVehicle(id) : FoodVehicle(id);
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            var game = SaveGameManager.Current;
            var panel = InstanceBehavior<UIs>.Instance?.playerHUD?.itemPanelUI;
            bool IsOpen() => FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.IsOpen == true;
            int Cargo() => vehicle.cargoInstances.Where(c => c != null).Sum(c => c.amount);
            string? Space() => state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == id)?.CommissaryId;
            var cargoBefore = Cargo();
            string? why = null;
            var workAccepted = false;
            var spaceBefore = Space();
            void InIt(string what)
            {
                if (game.ActiveVehicleId != id) throw new BridgeError($"{what} is on the vehicle's panel, shown while the player drives the truck or pushes the cart: vehicle enter first.");
            }
            return WithNotices(() =>
            {
                switch (op)
                {
                    case "open":
                        if (!string.IsNullOrEmpty(game.ActiveVehicleId))
                            throw new BridgeError($"Open for business is in the parked vehicle's on-foot menu: vehicle exit first (the player is in {game.ActiveVehicleId}).");
                        if (FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.IsOpen == true) throw new BridgeError($"Vehicle {id} is open already.");
                        CartSelling.OpenHere(Spawned(vehicle));
                        break;
                    case "close":
                        if (TruckWindow.PanelClose(id)) break;
                        if (FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.IsOpen != true) throw new BridgeError($"Vehicle {id} isn't open.");
                        CartSelling.CloseByPlayer(id);
                        break;
                    case "fast_forward":
                    case "skip_work":
                    {
                        var duration = Num(args, "minutes");
                        if (op == "skip_work" && (duration == null || duration != Math.Floor(duration.Value) || duration < 5 || duration > 720))
                            throw new BridgeError("minutes must be a whole number from 5 to 720.");
                        if (args["enabled"] != null && args["enabled"]!.Type != JTokenType.Boolean) throw new BridgeError("enabled must be true or false.");
                        ServingWork.Control(id, op, duration == null ? (int?)null : (int)duration.Value, (bool?)args["enabled"] ?? true);
                        workAccepted = true;
                        break;
                    }
                    case "work_window":
                        if (!CartSelling.CanWorkCart(vehicle)) throw new BridgeError("Work the window needs an open cart and the player free on foot.");
                        CartSelling.WorkCart(Spawned(vehicle));
                        workAccepted = true;
                        break;
                    case "stop_work":
                        if (ServingWork.Current?.VehicleId != id) throw new BridgeError("The player is not working this window.");
                        ServingWork.Current.Finish();
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
                        var car = Spawned(vehicle);
                        if (Vector3.Distance(PlayerHelper.GetPosition(), car.transform.position) <= TestBridgeCommands.CarReachMetres) car.EnterVehicle();
                        else car.DriveVehicle();
                        break;
                    case "exit":
                        InIt("Park");
                        if (panel == null) throw new BridgeError("The vehicle panel isn't there.");
                        panel.ClickPark();
                        if (game.ActiveVehicleId == id)
                            throw new BridgeError("The game's Park button refused: paused, no parking here, or the panel isn't showing.");
                        break;
                    case "restock":
                        InIt("Commissary stock");
                        why = global::FoodTrucks.Commissary.FillUpForTesting(vehicle);
                        break;
                    case "transfer":
                    {
                        InIt("Commissary stock");
                        var direction = Need(args, "direction", "take or return");
                        var item = Need(args, "item", "an item key or name");
                        var units = Num(args, "units");
                        if (units != null && units.Value < 1)
                            throw new BridgeError("units is a whole number from 1, or left out for as many as fit (take) or all on board (return).");
                        why = global::FoodTrucks.Commissary.TransferForTesting(vehicle, direction, item, units == null ? (int?)null : (int)Math.Min(int.MaxValue, units.Value));
                        break;
                    }
                    case "rent_bay":
                        why = global::FoodTrucks.Commissary.RentBayForTesting(vehicle);
                        break;
                    default:
                        throw new BridgeError($"Unknown op '{op}': open, close, fast_forward, skip_work, stop_work, work_window, step_out, back_to_hatch, enter, exit, restock, transfer, rent_bay or buy.");
                }
            }, notices => VehicleReply(op, id, vehicle, state, notices, why, IsOpen, Cargo, Space, cargoBefore, spaceBefore, workAccepted));
        }

        private static object Buy(JObject args)
        {
            var kind = Need(args, "kind", "truck or cart");
            var typeName = kind == "truck" ? FoodTrucksMod.TruckTypeName : kind == "cart" ? FoodTrucksMod.CartTypeName
                : throw new BridgeError($"Unknown kind '{kind}': truck or cart.");
            var type = Vehicles.VehicleTypes.VehicleTypeHelper.GetVehicleType(typeName) ?? throw new BridgeError($"Vehicle type {typeName} is not registered.");
            var cents = (long)Math.Round(type.price * 100.0);
            var game = SaveGameManager.Current;
            if (!string.IsNullOrEmpty(game.ActiveVehicleId)) throw new BridgeError($"The player is in {game.ActiveVehicleId}: vehicle exit first (the vehicle spawns beside the player on foot).");
            if (game.Money * 100.0 < cents)
                throw new BridgeError($"Not enough money for the {kind}: it costs {CartSelling.Money(cents)} and the player has {CartSelling.Money((long)Math.Floor(game.Money * 100.0))}.");
            VehicleInstance? bought = null;
            var paid = false;
            return WithNotices(() =>
            {
                bought = DebugActions.SpawnVehicle(typeName, announce: false);
                if (bought == null) return;
                var info = new TransactionInfo("ba:transaction_vehiclebought", new Dictionary<string, string> { { "vehicleName", typeName } });
                if (type.taxDeductible) info.SetTaxDeductibleName("tax_vehicle");
                paid = Payments.TryPayOut(cents, info, showNotification: false);
                if (!paid) Log.Error($"Test bridge: vehicle buy: {kind} {bought.id} spawned but the payment of {CartSelling.Money(cents)} was refused.");
                else CartSelling.Say("foodtrucks:notification_vehicle_bought", kind, ("price", CartSelling.Money(cents)));
            }, notices =>
            {
                Log.Info(bought == null
                    ? $"Test bridge: vehicle buy {kind}: not spawned, nothing charged."
                    : $"Test bridge: vehicle buy {kind}: {bought.id} for {CartSelling.Money(cents)}{(paid ? "" : " (payment refused)")}.");
                var state = FoodTrucksSession.CurrentState;
                return new JObject
                {
                    ["done"] = bought != null && paid, ["id"] = bought?.id, ["kind"] = kind, ["price"] = cents / 100.0,
                    ["money"] = Math.Round(game.Money, 2), ["notices"] = new JArray(notices),
                    ["state"] = bought == null || state == null ? null : TestBridgeState.Vehicle(state, bought),
                };
            });
        }

        private static JObject VehicleReply(string op, string id, VehicleInstance vehicle, FoodTrucks.Core.Saves.SaveState state, List<string> notices,
            string? why, Func<bool> IsOpen, Func<int> Cargo, Func<string?> Space, int cargoBefore, string? spaceBefore, bool workAccepted = false)
        {
            var game = SaveGameManager.Current;
            bool done;
            switch (op)
            {
                case "work_window": done = workAccepted; break;
                case "fast_forward": done = workAccepted; break;
                case "skip_work": done = workAccepted; break;
                case "stop_work": done = ServingWork.VehicleId != id; break;
                case "open": done = IsOpen(); break;
                case "close": done = !IsOpen(); break;
                case "step_out": done = !TruckWindow.PlayerAt(id); break;
                case "back_to_hatch": done = !TruckWindow.CanGoBack(vehicle); break;
                case "enter": done = game.ActiveVehicleId == id || (PlayerHelper.PlayerController?.hasOnGoalReachedAction ?? false); break;
                case "exit": done = game.ActiveVehicleId != id; break;
                case "restock":
                case "transfer": done = Cargo() != cargoBefore; break;
                case "rent_bay": done = Space() != null && Space() != spaceBefore; break;
                default: done = false; break;
            }
            Log.Info($"Test bridge: vehicle {id} {op}: {(done ? "done" : "not done")}.");
            return new JObject { ["done"] = done, ["why"] = done ? null : why, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) };
        }

        private static JToken CommissaryCommand(JObject args)
        {
            var op = Need(args, "op", "order or bay_candidates");
            if (op == "bay_candidates") return BayCandidates(args);
            if (op != "order") throw new BridgeError($"Unknown commissary op '{op}': order, bay_candidates.");
            var site = Need(args, "site", "the commissary's id");
            if (!(args["items"] is JArray rows) || rows.Count == 0) throw new BridgeError("order needs \"items\": [ { \"item\", \"boxes\" } ].");
            var items = rows.OfType<JObject>().Select(r => (Item: Need(r, "item", "an item key or name"), Boxes: (int)(Num(r, "boxes") ?? throw new BridgeError("Each item needs \"boxes\".")))).ToList();
            var (orders, refusal, feeCents) = global::FoodTrucks.Commissary.OrderForTesting(site, items);
            if (refusal != null) throw new BridgeError(refusal);
            return new JObject
            {
                ["orders"] = new JArray(orders.Select((o, i) => new JObject
                {
                    ["n"] = o.Id, ["site"] = o.CommissaryId, ["item"] = o.ItemName, ["units"] = o.Units,
                    ["goods"] = o.GoodsCents / 100.0, ["fee"] = (i == 0 ? feeCents : 0) / 100.0, ["total"] = (o.GoodsCents + (i == 0 ? feeCents : 0)) / 100.0, ["due_day"] = o.DeliverDay,
                })),
                ["delivery_fee"] = feeCents / 100.0,
            };
        }

        private const double CandidateRadiusDefault = 60;
        private const double CandidateRadiusMax = 80;
        private static readonly System.Text.RegularExpressions.Regex AddressForm = new System.Text.RegularExpressions.Regex(@"^ba:street_[a-z0-9]+#[0-9]+$");

        private static JToken BayCandidates(JObject args)
        {
            var site = Str(args, "site");
            var address = Str(args, "address");
            if ((site == null) == (address == null))
                throw new BridgeError("bay_candidates needs \"site\" (a commissary's id) or \"address\" (any building, as ba:street_…#N), not both.");
            if (address != null && !AddressForm.IsMatch(address))
                throw new BridgeError($"\"address\" is a building's address as the game writes it, ba:street_<name>#<number>; got '{address}'.");
            var radius = Math.Round(Num(args, "radius") ?? CandidateRadiusDefault);
            if (radius < 5 || radius > CandidateRadiusMax) throw new BridgeError($"radius is 5 to {CandidateRadiusMax:F0} metres.");
            (string? Status, List<global::FoodTrucks.Commissary.BayCandidateOrigin> Origins) found;
            try { found = global::FoodTrucks.Commissary.BayCandidatesForTesting(site, address, radius); }
            catch (InvalidOperationException ex) { throw new BridgeError(ex.Message); }
            JToken? Round(double? v) => v == null ? null : (JToken)Math.Round(v.Value, 2);
            return new JObject
            {
                ["site"] = site,
                ["address"] = address,
                ["status"] = found.Status,
                ["radius"] = radius,
                ["max_metres"] = BaySnap.MaxMetres,
                ["origins"] = new JArray(found.Origins.Select(o => new JObject
                {
                    ["kind"] = o.Kind,
                    ["x"] = Round(o.X), ["z"] = Round(o.Z),
                    ["door"] = o.Door == null ? null : new JObject { ["x"] = Round(o.Door.Value.x), ["y"] = Round(o.Door.Value.y), ["z"] = Round(o.Door.Value.z) },
                    ["walk_start"] = o.WalkStart,
                    ["same_side"] = o.SameSide,
                    ["parking_colliders"] = o.Colliders,
                    ["truncated"] = o.Truncated,
                    ["candidates"] = new JArray(o.Candidates.Select(c => new JObject
                    {
                        ["x"] = Round(c.X), ["z"] = Round(c.Z), ["heading"] = Round(c.Heading), ["distance"] = Round(c.Distance),
                        ["walk"] = c.Walk, ["walk_m"] = Round(c.WalkMetres), ["straight_m"] = Round(c.StraightMetres),
                        ["same_side"] = c.SameSide, ["automatic"] = c.Automatic, ["rejected"] = c.Rejected,
                    })),
                })),
            };
        }

        private static object Crew(JObject args)
        {
            var op = Need(args, "op", "assign, unassign, schedule or auto_schedule");
            var id = Need(args, "vehicle", "the food vehicle's id");
            var vehicle = FoodVehicle(id);
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            if (!FoodTrucksSession.CanWrite) throw new BridgeError("The mod's save data is read-only.");
            if (op == "auto_schedule")
                return WithNotices(() => BizManWeekPlan.AutoSchedule(id, FoodTrucksMod.KindOf(vehicle.vehicleTypeName) ?? "cart"), notices =>
                {
                    var shifts = FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.Schedule?.CrewShifts?.Count ?? 0;
                    return new JObject { ["done"] = shifts > 0, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) };
                });
            var employeeId = Need(args, "employee_id", "the employee's id (state.employees)");
            if (op == "schedule")
            {
                var dayText = Need(args, "day", "a weekday, monday to sunday");
                if (!Enum.TryParse<DayOfWeek>(dayText, true, out var day)) throw new BridgeError($"Unknown day '{dayText}': monday to sunday.");
                var start = (int)(TestBridgeCommands.Num(args, "start") ?? throw new BridgeError("Missing \"start\": the starting hour, 0 to 23."));
                var end = (int)(TestBridgeCommands.Num(args, "end") ?? throw new BridgeError("Missing \"end\": the ending hour, 1 to 24 (the start removes)."));
                var station = TestBridgeCommands.Str(args, "station");
                if (station != null && station != "serving1" && station != "serving2")
                    throw new BridgeError($"Unknown station '{station}': serving1 or serving2.");
                return WithNotices(() =>
                {
                    var problem = VehicleSchedule.SetForBridge(id, employeeId, day, start, end, station);
                    if (problem != FoodTrucks.Core.Fleet.ShiftProblem.None) throw new BridgeError($"Shift refused: {problem}.");
                }, notices => new JObject { ["done"] = true, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) });
            }
            var record = FoodTrucks.Core.Saves.StreetSales.Find(state, id);
            var crew = record?.Crew ?? new System.Collections.Generic.List<string>();
            return WithNotices(() =>
            {
                switch (op)
                {
                    case "assign":
                        if (crew.Contains(employeeId)) throw new BridgeError($"{employeeId} is on {id} already.");
                        if (!HireCrew.Available(state).Any(e => e.id == employeeId))
                            throw new BridgeError($"{employeeId} isn't an unassigned hire (see state.employees).");
                        BizManVehiclePage.ChooseCrew(id, crew.Count, employeeId);
                        break;
                    case "unassign":
                        var slot = crew.IndexOf(employeeId);
                        if (slot < 0) throw new BridgeError($"{employeeId} isn't on {id}.");
                        BizManVehiclePage.ChooseCrew(id, slot, null);
                        break;
                    default:
                        throw new BridgeError($"Unknown crew op '{op}': assign, unassign, schedule or auto_schedule.");
                }
            }, notices =>
            {
                var now = FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.Crew ?? new List<string>();
                var done = op == "assign" ? now.Contains(employeeId) : !now.Contains(employeeId);
                return new JObject { ["done"] = done, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) };
            });
        }

    }
}
