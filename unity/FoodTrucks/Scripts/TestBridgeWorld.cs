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
        // The way out from the door is the walk to the street (the NavMesh path to the nearest driving lane, else to the
        // bay); `View` is the point `viewMetres` along that walk, where a person coming from the street sees the door.
        // Without a path, straight towards the lane or bay; without either, away from the building's middle.
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

        // The point `metres` along a path's corners (its end if shorter).
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
            // The surface the point was put on, in the reply (a Deferred's reply once the move is done).
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

        // The ground surfaces stacked above a point (a street under an elevated road), top down, and the one a teleport
        // lands on (Core GroundPick): with an explicit y the one nearest it (walkable, for the player), else a walkable
        // one nearest the mover's own height. Each ray starts just under the last surface; a ray never hits a box it
        // starts in or a mesh's underside, so it goes on to the next surface down. Live session 4 (13 October 2026): the
        // highest surface put the player on the elevated highway at y 5.06.
        //
        // Walkable, for the player: NavMesh of the player's own agent type and areas within the 3 m the landing allows
        // and within 1 m of the surface's height, and, while the player stands on the NavMesh, a complete walking route
        // from there. Live session 6: the highway deck has NavMesh near it, so a y of 5.06 put the player up there; no
        // footpath leads to it. A vehicle's surfaces are read with any NavMesh, as before.
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
                route = start == null
                    ? "; walking routes not checked (the player isn't on the NavMesh)"
                    : "; walking routes checked from the player's place";
                surfaces = points.Select(p => ((double)p.y, Walkable(p, filter, start))).ToList();
            }
            else surfaces = points.Select(p => ((double)p.y, NavMesh.SamplePosition(p, out var near, 3f, NavMesh.AllAreas) && Mathf.Abs(near.position.y - p.y) < 1f)).ToList();
            var (index, why) = FoodTrucks.Core.Common.GroundPick.Choose(surfaces, referenceY, explicitY, walkableOnly);
            why += route;
            var kind = walkableOnly ? "walkable ground" : "ground";
            if (index < 0) throw new BridgeError($"No {kind} at x {x:F1}, z {z:F1} near y {explicitY:F2}: {why}.");
            return (points[index], why);
        }

        // The player agent's own NavMesh filter (agent type and areas), and the player's place on the NavMesh, or null
        // when they aren't on it (in a vehicle, inside a building, off the mesh).
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

        // ---- What stands where something is moved to ----

        // Something in the way: its description, and the lane's parked car it belongs to (the only kind `clear` removes).
        private sealed class Blocker
        {
            public string What = "";
            public GameObject? LaneCar;
            // The thing itself, so two cars of one model (pooled cars share model names) stay two (review round 6).
            public UnityEngine.Object? Thing;
            // A delivery-job van the game has hidden for now.
            public bool Hidden;
        }

        // The vehicle's own size in its own frame: each solid collider's own box (a box's centre and size, a sphere's or
        // capsule's enclosing box, a mesh's local bounds), carried into the vehicle's frame with its scale (Core Footprint),
        // so the size doesn't grow with the way the vehicle faces. Logged once per vehicle type, to check in game.
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

        // The ground and road surfaces, parking areas and triggers aren't in the way; a kerb isn't either (the test starts
        // 0.3 m up). A trigger on the parked-car or traffic layers still is: the game's own check for cars under a placed
        // vehicle (VehicleHelper.DestroyBlockingVehicles) queries those layers with triggers included.
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
                    found[key] = new Blocker { What = $"{c.transform.root.name}/{c.name} (layer {LayerMask.LayerToName(c.gameObject.layer)})", Thing = key is Component k ? (UnityEngine.Object)k.gameObject : key };
            }
            return found.Values.ToList();
        }

        // What stands in the vehicle's footprint, 0.3 m up to its roof, at a pose on the ground.
        // `vans`: the delivery-job vans, looked up once per command (Vans()); null looks them up here.
        private static List<Blocker> InTheWay(VehicleController controller, Vector3 ground, Quaternion rotation,
            IReadOnlyList<Vehicles.DeliveryDriverJob.DeliveryJobStartController>? vans = null)
        {
            var local = LocalBounds(controller);
            var bottom = local.min.y + 0.3f;
            var top = Mathf.Max(local.max.y, bottom + 0.5f);
            var centre = new Vector3(local.center.x, (bottom + top) / 2f, local.center.z);
            var half = new Vector3(Mathf.Max(0.05f, local.extents.x - 0.1f), (top - bottom) / 2f, Mathf.Max(0.05f, local.extents.z - 0.1f));
            var hits = Physics.OverlapBox(ground + rotation * centre, half, rotation, ~0, QueryTriggerInteraction.Collide);
            var found = Describe(hits, controller.transform);
            // The colliders alone missed a van in a perpendicular stall (live session 4, 13 October 2026, industry-city).
            // So the parked cars round the place are also tested by their footprints (each lane car's mesh bounds, as
            // the game sizes its stalls by), and so is the building's delivery-job van, which the game hides and shows
            // again in its stall whatever stands there; a hidden one has no collider to hit.
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

        // Every delivery-job van in the scene, hidden ones too: a scene-wide search, so once per command.
        private static List<Vehicles.DeliveryDriverJob.DeliveryJobStartController> Vans() =>
            UnityEngine.Object.FindObjectsByType<Vehicles.DeliveryDriverJob.DeliveryJobStartController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(v => v != null).ToList();

        // The lanes' parked cars within `radius` of a point (active children of the parking lanes there) and the
        // delivery-job vans, shown or hidden, with how each is named in a refusal and whether `clear` may send it away.
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

        // A parked car's footprint and height span: its mesh collider's bounds (each lane car has one on its root), else
        // its renderers'.
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
            // Renderers' world bounds: as wide one way as the other, whatever the car's turn.
            var all = renderers[0].bounds;
            foreach (var r in renderers) all.Encapsulate(r.bounds);
            var reach = Mathf.Max(all.extents.x, all.extents.z);
            box = new FoodTrucks.Core.Fleet.FlatBox(all.center.x, all.center.z, 0, 1, reach, reach);
            span = (all.min.y, all.max.y);
            return true;
        }

        // What stands where the player would: a standing person's capsule.
        private static List<Blocker> InTheWay(PlayerController player, Vector3 ground)
        {
            var hits = Physics.OverlapCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 1.6f, 0.2f, ~0, QueryTriggerInteraction.Ignore);
            return Describe(hits, player.transform, sceneryCounts: false);
        }

        // Nothing in the way, or (with clear) only the lanes' parked cars, which go back to their pool as BayParking sends
        // them. Otherwise a refusal naming what stands there.
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

        // A truck to a commissary's bay: the bay's middle, else the free place on its paint nearest the middle, the vehicle
        // lined up with the lane and its hatch to the pavement.
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
            // Nothing free on the paint: the middle, if clear may send its parked cars away.
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

        private static object TeleportPlayer(Vector3 target, float? heading, bool clear)
        {
            var player = PlayerHelper.PlayerController ?? throw new BridgeError("The player isn't there.");
            if (!NavMesh.SamplePosition(target, out var hit, 3f, PlayerFilter(out _)))
                throw new BridgeError($"No walkable ground within 3 m of x {target.x:F1}, z {target.z:F1}.");
            // The NavMesh found must be on the chosen surface, not a level above or below it.
            if (Mathf.Abs(hit.position.y - target.y) > 1f)
                throw new BridgeError($"No walkable ground within 3 m of x {target.x:F1}, y {target.y:F2}, z {target.z:F1} (the nearest is at y {hit.position.y:F2}).");
            var point = hit.position;
            MakeRoom(InTheWay(player, point), clear, $"x {point.x:F1}, z {point.z:F1}");
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

        private static JToken TeleportVehicle(string id, Vector3 target, float? heading, bool clear, List<Blocker>? blockers = null,
            List<Vehicles.DeliveryDriverJob.DeliveryJobStartController>? vans = null)
        {
            vans ??= Vans();
            var vehicle = FoodVehicle(id);
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
                // The move failed: the body's own mode back at once (review round 8).
                if (body != null) RestoreInterpolation(body);
                throw;
            }
            if (body != null)
            {
                var pump = TestBridgePump.Instance;
                if (pump != null) pump.StartCoroutine(InterpolateAfterStep(body));
                else RestoreInterpolation(body);
            }
            // The parking reading for the new place: the game reads it again when a car it drives stands still
            // (CarController.isCheckedForParkingZone), and at once here if the player drives this one, as the panel's
            // live reading that Open checks. A vehicle nobody drives keeps the state the game would store on leaving it.
            var game = SaveGameManager.Current;
            if (controller is CarController car)
            {
                car.isCheckedForParkingZone = false;
                if (game.ActiveVehicleId == id) car.UpdateParkingZone();
                else
                {
                    // As the game stores both on leaving a vehicle (VehicleController.SetParkingData): the state and the
                    // neighbourhood the hourly parking charge is priced by.
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

        // Bodies whose interpolation the bridge turned off, with the mode each had first: a second teleport before the
        // first is restored keeps the first original, not the None the bridge set (review round 8).
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

        // Every body still waiting gets its mode back: the bridge stops (Reset), before its pump and coroutines go.
        internal static void RestoreAllInterpolation()
        {
            foreach (var pair in interpolationOff.ToList())
                if (pair.Key != null) pair.Key.interpolation = pair.Value;
            interpolationOff.Clear();
        }

        // Puts a body's interpolation back once a physics step has run from its new pose (WaitForFixedUpdate waits
        // through a pause).
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

        // ---- vehicle ----

        private static object Vehicle(JObject args)
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
            return WithNotices(() =>
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
            }, notices => VehicleReply(op, id, vehicle, state, notices, why, IsOpen, Cargo, Space, cargoBefore, spaceBefore));
        }

        // Whether the operation happened, read from the state it should have changed (a refusal leaves only its notice).
        private static JObject VehicleReply(string op, string id, VehicleInstance vehicle, FoodTrucks.Core.Saves.SaveState state, List<string> notices,
            string? why, Func<bool> IsOpen, Func<int> Cargo, Func<string?> Space, int cargoBefore, string? spaceBefore)
        {
            var game = SaveGameManager.Current;
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
            var op = Need(args, "op", "order or bay_candidates");
            if (op == "bay_candidates") return BayCandidates(args);
            if (op != "order") throw new BridgeError($"Unknown commissary op '{op}': order, bay_candidates.");
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

        // { "op": "bay_candidates", "site", "radius"? }: legal truck spots round the commissary's search origins, for
        // measuring a bay point by hand. Reads only.
        private const double CandidateRadiusDefault = 60;
        private const double CandidateRadiusMax = 80;

        private static JToken BayCandidates(JObject args)
        {
            var site = Need(args, "site", "the commissary's id");
            // Whole metres: BaySnap caches one grid per radius for the session (review round 1, Opus).
            var radius = Math.Round(Num(args, "radius") ?? CandidateRadiusDefault);
            if (radius < 5 || radius > CandidateRadiusMax) throw new BridgeError($"radius is 5 to {CandidateRadiusMax:F0} metres.");
            (string Status, List<global::FoodTrucks.Commissary.BayCandidateOrigin> Origins) found;
            try { found = global::FoodTrucks.Commissary.BayCandidatesForTesting(site, radius); }
            catch (InvalidOperationException ex) { throw new BridgeError(ex.Message); }
            JToken? Round(double? v) => v == null ? null : (JToken)Math.Round(v.Value, 2);
            return new JObject
            {
                ["site"] = site,
                ["status"] = found.Status,
                ["radius"] = radius,
                ["max_metres"] = BaySnap.MaxMetres,
                ["origins"] = new JArray(found.Origins.Select(o => new JObject
                {
                    ["kind"] = o.Kind,
                    ["x"] = Round(o.X), ["z"] = Round(o.Z),
                    ["door"] = o.Door == null ? null : new JObject { ["x"] = Round(o.Door.Value.x), ["y"] = Round(o.Door.Value.y), ["z"] = Round(o.Door.Value.z) },
                    ["walk_start"] = o.WalkStart,
                    ["parking_colliders"] = o.Colliders,
                    ["truncated"] = o.Truncated,
                    ["candidates"] = new JArray(o.Candidates.Select(c => new JObject
                    {
                        ["x"] = Round(c.X), ["z"] = Round(c.Z), ["heading"] = Round(c.Heading), ["distance"] = Round(c.Distance),
                        ["walk"] = c.Walk, ["walk_m"] = Round(c.WalkMetres), ["straight_m"] = Round(c.StraightMetres),
                        ["automatic"] = c.Automatic, ["rejected"] = c.Rejected,
                    })),
                })),
            };
        }

        // { "op": "assign" | "unassign", "vehicle", "employee_id" }: the BizMan vehicle page's crew dropdown.
        private static object Crew(JObject args)
        {
            var op = Need(args, "op", "assign or unassign");
            var id = Need(args, "vehicle", "the food vehicle's id");
            var employeeId = Need(args, "employee_id", "the employee's id (state.employees)");
            var vehicle = FoodVehicle(id);
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            if (!FoodTrucksSession.CanWrite) throw new BridgeError("The mod's save data is read-only.");
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
            }, notices =>
            {
                var now = FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.Crew ?? new List<string>();
                var done = op == "assign" ? now.Contains(employeeId) : !now.Contains(employeeId);
                return new JObject { ["done"] = done, ["notices"] = new JArray(notices), ["state"] = TestBridgeState.Vehicle(state, vehicle) };
            });
        }

    }
}
