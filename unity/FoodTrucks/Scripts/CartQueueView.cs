#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Parking.UndergroundParking;
using UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The queue at an open cart's window, shown as people on the pavement. Core decides who waits, who is
    // served and who gives up (SalesSimulator); this only shows it, and only within ViewMetres of the player,
    // the range in which the game keeps street people of its own. Further away nothing is spawned and the
    // same Core tick goes on unseen, so the view never changes a sale.
    //
    // People walk in from the pavement a little way along, take their place in line, step up as it moves,
    // stand at the window for a moment when served and walk off. Core can serve someone before they reach
    // the window (an in-game minute is a second at normal speed); they still walk up, pay and leave.
    // Everyone is removed when the cart closes or is grabbed (they walk off), and at once when the player
    // goes further than KeepMetres, enters a building, skips time, leaves the city or runs the uninstall
    // step. At most MaxPeople exist at a time, across all carts.
    internal sealed class CartQueueView : MonoBehaviour
    {
        private const float ViewMetres = 100f;
        // A little further than ViewMetres before a visible line goes, so walking along its edge doesn't flicker.
        private const float KeepMetres = 110f;
        private const int MaxPeople = 16;
        private const float CheckSeconds = 0.25f;

        private static CartQueueView? instance;
        private readonly Dictionary<string, QueueLine> lines = new Dictionary<string, QueueLine>();
        // Lines of carts that closed, while their people walk off; a cart reopened meanwhile gets a new line.
        private readonly List<QueueLine> departing = new List<QueueLine>();
        private float nextCheck;
        private int spawnedTotal;
        private int mostAtOnce;

        // Set from the time machine's start event to its end event: no lines while time skips.
        public static bool Suspended { get; set; }

        private void OnEnable()
        {
            instance = this;
            // A skip that ended while no runtime listened must not hide the queue for good.
            Suspended = false;
            // A runtime from an earlier load (or a mod reload) may have left people behind.
            foreach (var old in FindObjectsOfType<QueueLineRoot>())
            {
                if (old != null) Destroy(old.gameObject);
            }
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            RemoveAll("view disabled");
            if (instance == this) instance = null;
            if (spawnedTotal > 0) Log.Info($"Queue view: {spawnedTotal} people shown this session, {mostAtOnce} at most at once.");
        }

        private void OnSceneChanged(Scene from, Scene to) => RemoveAll("scene changed");

        // After each Core tick: who was served (in order) and why the others left.
        public static void Record(string vehicleId, SalesResult result, ServiceMood mood)
        {
            if (instance == null || !instance.lines.TryGetValue(vehicleId, out var line)) return;
            line.Mood = mood;
            foreach (var sale in result.Sales)
            {
                if (!sale.AddOn) line.Served(sale.CustomerId);
            }
            foreach (var (id, exit) in result.Left)
            {
                if (id != 0) line.Exits[id] = exit;
            }
        }

        // True while the cart's queue is on screen: CartSelling then holds each sale until the customer
        // pays at the window (Paid).
        public static bool Showing(string vehicleId) =>
            instance != null && instance.enabled && instance.lines.TryGetValue(vehicleId, out var line) && line.HasPlaces;

        // The cart closed: everyone in line walks off, then the line goes.
        public static void Closed(string vehicleId) => instance?.Retire(vehicleId);

        // Removes every person at once: time skips, the uninstall step, leaving the city.
        public static void ClearAll() => instance?.RemoveAll("cleared");

        private void Retire(string vehicleId)
        {
            if (!lines.TryGetValue(vehicleId, out var line)) return;
            lines.Remove(vehicleId);
            line.Close();
            departing.Add(line);
        }

        private void RemoveAll(string reason)
        {
            foreach (var line in lines.Values.Concat(departing)) line.Destroy(reason);
            lines.Clear();
            departing.Clear();
        }

        private void Update()
        {
            try
            {
                if (Time.unscaledTime >= nextCheck)
                {
                    nextCheck = Time.unscaledTime + CheckSeconds;
                    CheckLines();
                }
                var room = MaxPeople - lines.Values.Concat(departing).Sum(l => l.Count);
                foreach (var line in lines.Values) room -= line.Tick(Math.Max(0, room));
                for (var i = departing.Count - 1; i >= 0; i--)
                {
                    departing[i].Tick(0);
                    if (!departing[i].Finished) continue;
                    departing[i].Destroy("closed");
                    departing.RemoveAt(i);
                }
                var now = lines.Values.Concat(departing).Sum(l => l.Count);
                if (now > mostAtOnce) mostAtOnce = now;
            }
            catch (Exception ex)
            {
                Log.Error("Queue view: " + ex + " The queue stays hidden until the next load; sales go on.");
                RemoveAll("error");
                enabled = false;
            }
        }

        // Starts a line for each open cart near the player and removes the lines of carts out of range.
        private void CheckLines()
        {
            var state = FoodTrucksSession.CurrentState;
            var game = SaveGameManager.Current;
            var timeMachine = InstanceBehavior<UIs>.Instance?.timeMachine;
            if (state == null || game == null || Suspended || (timeMachine != null && timeMachine.isRunning) ||
                BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                if (lines.Count + departing.Count > 0) RemoveAll(Suspended ? "time skip" : "player away");
                return;
            }
            var player = PlayerHelper.GetPosition();
            foreach (var line in lines.Values.ToList())
            {
                var controller = ControllerOf(game, line.VehicleId);
                var vehicle = state.Vehicles.FirstOrDefault(v => v.VehicleId == line.VehicleId);
                if (controller == null || Vector3.Distance(player, controller.transform.position) > KeepMetres)
                {
                    line.Destroy("out of range");
                    lines.Remove(line.VehicleId);
                }
                else if (vehicle == null || !vehicle.IsOpen || vehicle.Service == null || game.ActiveVehicleId == line.VehicleId)
                    Retire(line.VehicleId);
                // A cart closed and opened again between two checks has a new queue.
                else if (!ReferenceEquals(line.Service, vehicle.Service))
                {
                    Retire(line.VehicleId);
                }
            }
            foreach (var line in departing.ToList())
            {
                var controller = ControllerOf(game, line.VehicleId);
                if (controller != null && Vector3.Distance(player, controller.transform.position) <= KeepMetres) continue;
                line.Destroy("out of range");
                departing.Remove(line);
            }

            var rules = DemandData.Rules;
            if (rules == null) return;
            foreach (var vehicle in state.Vehicles)
            {
                if (!vehicle.IsOpen || vehicle.Service == null || lines.ContainsKey(vehicle.VehicleId)) continue;
                if (game.ActiveVehicleId == vehicle.VehicleId) continue;
                var controller = ControllerOf(game, vehicle.VehicleId);
                if (controller == null || Vector3.Distance(player, controller.transform.position) > ViewMetres) continue;
                var kind = FoodTrucksMod.KindOf(controller.vehicleInstance?.vehicleTypeName) ?? "cart";
                var limit = rules.Service.QueueLimit(kind, rules.Queue.MaxQueue);
                var line = QueueLine.Create(vehicle.VehicleId, controller.transform, limit, vehicle.Service, WindowShape.For(kind));
                line.OnSpawned = () => spawnedTotal++;
                var id = vehicle.VehicleId;
                line.Paid = customer => CartSelling.Paid(id, customer);
                lines[vehicle.VehicleId] = line;
            }
        }

        private static VehicleController? ControllerOf(GameInstance game, string vehicleId)
        {
            var cart = game.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            return cart == null ? null : VehicleHelper.GetVehicleController(cart);
        }
    }

    // Where a vehicle's window is, relative to its middle: Sideways metres to the window place, Along metres
    // forward of the middle, on which sides. A cart serves from either side; the truck's hatch is on its right
    // (art/README.md: opening at x 1.10, centre z -1.10), so its line is always on the right.
    internal sealed class WindowShape
    {
        private WindowShape(float sideways, float reach, float along, int[] sides, bool pavementOnly)
        {
            PavementOnly = pavementOnly;
            Sideways = sideways;
            Reach = reach;
            Along = along;
            Sides = sides;
        }

        public float Sideways { get; }
        // How far out a place may be found when the nearest one isn't pavement: a truck in a wide parking
        // space has its hatch side over the lane, and the line starts at the kerb instead.
        public float Reach { get; }
        public float Along { get; }
        // No place on the road layer or on a crosswalk: a truck's hatch side may face the street, a cart's
        // two sides are always on the pavement it stands on. The road layer read "no" at every pavement spot
        // logged so far; whether it covers streets is unverified.
        public bool PavementOnly { get; }
        public int[] Sides { get; }

        private static readonly WindowShape Cart = new WindowShape(1.05f, 1.05f, 0f, new[] { 1, -1 }, pavementOnly: false);
        private static readonly WindowShape Truck = new WindowShape(1.80f, 3.40f, -1.10f, new[] { 1 }, pavementOnly: true);

        public static WindowShape For(string kind) => kind == "truck" ? Truck : Cart;
    }

    // Marks a line's root object, so a later runtime can find and remove what an earlier one left.
    internal sealed class QueueLineRoot : MonoBehaviour
    {
    }

    // One cart's line: the places along the pavement, the ways in and out, and the people.
    internal sealed class QueueLine
    {
        // Metres between people in line.
        private const float Spacing = 0.8f;
        // How long a served customer stands at the window in all, in game-speed seconds (an in-game minute is
        // a second at normal speed): taking the order, paying, picking it up. Shorter while others wait to pay.
        // Time already spent standing there while the order was made counts, but paying takes at least MinPaySeconds.
        private const float WindowSeconds = 4.5f;
        private const float HurriedWindowSeconds = 2.5f;
        private const float MinPaySeconds = 1.5f;
        private const float HurriedMinPaySeconds = 1f;
        private const float SpawnGapSeconds = 0.6f;
        // Served customers not yet shown are dropped beyond this, so the view never runs far behind Core.
        private const int MaxUnshownServed = 3;
        // Served customers still in line beyond this walk off without stopping at the window.
        private const int MaxShownServed = 5;
        // Places beyond the queue limit, for served customers still on their way to the window.
        private const int ExtraPlaces = 3;
        private const float ClosingSeconds = 40f;
        private const int MaxSpawnFailures = 3;
        private const float SpawnRetrySeconds = 15f;

        private readonly List<Vector3> places;
        private readonly Vector3 windowLook;
        private readonly List<Vector3> ends;
        private readonly GameObject root;
        private readonly Dictionary<long, QueuePerson> people = new Dictionary<long, QueuePerson>();
        private readonly List<QueuePerson> leaving = new List<QueuePerson>();
        // Customers Core has served who haven't left the window yet, in the order they were served.
        private readonly List<long> served = new List<long>();
        // Served customers whose sale has been paid (they reached the window).
        private readonly HashSet<long> payingStarted = new HashSet<long>();
        private bool fresh = true;
        private float nextSpawn;
        private float closedAt;
        private int spawnFailures;
        // After repeated spawn failures, nobody is spawned until then, a new line's first batch included.
        private float blockedUntil;

        private QueueLine(string vehicleId, GameObject root, List<Vector3> places, Vector3 windowLook, List<Vector3> ends, ServiceState service)
        {
            VehicleId = vehicleId;
            this.root = root;
            this.places = places;
            this.windowLook = windowLook;
            this.ends = ends;
            Service = service;
        }

        public string VehicleId { get; }
        public ServiceState Service { get; }
        public ServiceMood Mood { get; set; } = ServiceMood.Neutral;
        public Dictionary<long, CustomerExit> Exits { get; } = new Dictionary<long, CustomerExit>();
        public Action? OnSpawned { get; set; }
        // Called once per served customer: when they start paying at the window, or when the view lets them
        // go unseen (dropped, removed, closed), so CartSelling never holds a sale for good.
        public Action<long>? Paid { get; set; }
        public bool HasPlaces => places.Count > 0;
        public int Count => people.Count + leaving.Count;
        public bool Finished => Count == 0 || Time.time - closedAt > ClosingSeconds;

        // Places for the window, the queue limit and a few served customers on their way.
        public static QueueLine Create(string vehicleId, Transform cart, int queueLimit, ServiceState service, WindowShape shape)
        {
            var (places, look, ends, detail) = Layout(cart, queueLimit + 1 + ExtraPlaces, shape);
            var root = new GameObject("FoodTrucks.Queue." + vehicleId);
            root.AddComponent<QueueLineRoot>();
            Log.Info($"Queue view: line at cart {vehicleId}: {detail}.");
            return new QueueLine(vehicleId, root, places, look, ends, service);
        }

        private void ReleaseServed()
        {
            var ids = served.ToList();
            served.Clear();
            foreach (var id in ids) Release(id);
        }

        // A served customer leaves the view without paying at the window: their sale is paid now.
        private void Release(long id)
        {
            payingStarted.Remove(id);
            Paid?.Invoke(id);
        }

        public void Served(long id)
        {
            // A line with no places shows nobody, so it keeps no history either.
            if (places.Count > 0 && !served.Contains(id)) served.Add(id);
        }

        public void Close()
        {
            closedAt = Time.time;
            ReleaseServed();
            foreach (var person in people.Values) StartLeaving(person, null);
            people.Clear();
        }

        public void Destroy(string reason)
        {
            ReleaseServed();
            var count = Count;
            people.Clear();
            leaving.Clear();
            if (root != null) Object.Destroy(root);
            if (count > 0) Log.Info($"Queue view: cart {VehicleId} line removed with {count} people ({reason}).");
        }

        // One frame. Spawns at most `room` people; returns how many it spawned.
        public int Tick(int room)
        {
            var spawned = 0;
            if (places.Count > 0 && people.Count + room > 0)
            {
                var order = Order();
                var inLine = new HashSet<long>(order);
                // Anyone no longer in line leaves: gave up, walked on, or the window closed on them.
                foreach (var id in people.Keys.Where(id => !inLine.Contains(id)).ToList())
                {
                    StartLeaving(people[id], Exits.TryGetValue(id, out var exit) ? exit : (CustomerExit?)null);
                    people.Remove(id);
                }
                for (var i = 0; i < order.Count && i < places.Count; i++)
                {
                    var id = order[i];
                    if (!people.TryGetValue(id, out var person))
                    {
                        if (room - spawned <= 0 || Time.time < blockedUntil || (!fresh && Time.time < nextSpawn)) continue;
                        person = Spawn(id, i);
                        if (person == null) continue;
                        spawned++;
                    }
                    person.GoTo(places[i], i == 0 ? windowLook : places[i - 1]);
                }
                fresh = false;
                ServeAtWindow(order);
            }

            // People whose character the game removed are dropped, so nobody waits behind them.
            foreach (var pair in people.ToList())
            {
                if (pair.Value != null && !pair.Value.Tick()) continue;
                if (pair.Value != null) Object.Destroy(pair.Value.gameObject);
                people.Remove(pair.Key);
                if (served.Remove(pair.Key) && !payingStarted.Contains(pair.Key)) Release(pair.Key);
                payingStarted.Remove(pair.Key);
            }
            for (var i = leaving.Count - 1; i >= 0; i--)
            {
                var person = leaving[i];
                if (person != null && !person.Tick()) continue;
                if (person != null) Object.Destroy(person.gameObject);
                leaving.RemoveAt(i);
            }
            if (Exits.Count > 0)
            {
                foreach (var id in Exits.Keys.Where(id => !people.ContainsKey(id)).ToList()) Exits.Remove(id);
            }
            return spawned;
        }

        // Served customers first (they reached the window before anyone still waiting), then the orders
        // being made, then the queue. Served customers nobody saw arrive are dropped beyond a few, and
        // served ones still in line beyond a few walk off.
        private List<long> Order()
        {
            var order = new List<long>();
            var unshown = 0;
            var shown = 0;
            foreach (var id in served.ToList())
            {
                if (people.TryGetValue(id, out var person))
                {
                    if (++shown > MaxShownServed && !person.Paying)
                    {
                        served.Remove(id);
                        people.Remove(id);
                        Release(id);
                        StartLeaving(person, CustomerExit.Served);
                        continue;
                    }
                }
                else if (++unshown > MaxUnshownServed)
                {
                    served.Remove(id);
                    Release(id);
                    continue;
                }
                order.Add(id);
            }
            var seen = new HashSet<long>(order);
            foreach (var customer in Service.Window.Concat(Service.Queue))
            {
                if (customer != null && seen.Add(customer.Id)) order.Add(customer.Id);
            }
            return order;
        }

        // The served customer at the window pays for a moment, then walks off. With others waiting to pay,
        // the next one in line pays where they stand, so a fast window doesn't leave a crowd of paid customers.
        private void ServeAtWindow(List<long> order)
        {
            var windows = served.Count > 2 ? 2 : 1;
            for (var i = 0; i < windows && i < order.Count; i++)
            {
                var id = order[i];
                if (!served.Contains(id) || !people.TryGetValue(id, out var person) || !person.Arrived) continue;
                if (!person.Paying)
                {
                    var hurried = served.Count > 2;
                    var seconds = Math.Max(hurried ? HurriedMinPaySeconds : MinPaySeconds,
                        (hurried ? HurriedWindowSeconds : WindowSeconds) - person.SecondsStanding);
                    person.Pay(seconds, Mood);
                    // The money and the sale message come now, with the customer at the window.
                    payingStarted.Add(id);
                    Paid?.Invoke(id);
                    continue;
                }
                if (!person.DonePaying) continue;
                served.Remove(id);
                payingStarted.Remove(id);
                people.Remove(id);
                StartLeaving(person, CustomerExit.Served);
            }
        }

        private QueuePerson? Spawn(long id, int place)
        {
            nextSpawn = Time.time + SpawnGapSeconds;
            // A new line shows the people already waiting where they stand; later ones walk in.
            var start = fresh || ends.Count == 0 ? places[place] : ends[UnityEngine.Random.Range(0, ends.Count)];
            var person = QueuePerson.Create(root.transform, id, start);
            if (person == null)
            {
                // A missing prefab or NavMesh won't fix itself: wait a while instead of filling the log.
                if (++spawnFailures >= MaxSpawnFailures)
                {
                    blockedUntil = Time.time + SpawnRetrySeconds;
                    spawnFailures = 0;
                    Log.Error($"Queue view: cart {VehicleId}: customers could not be created; trying again in {SpawnRetrySeconds:F0} s.");
                }
                return null;
            }
            spawnFailures = 0;
            people[id] = person;
            OnSpawned?.Invoke();
            return person;
        }

        private void StartLeaving(QueuePerson person, CustomerExit? exit)
        {
            if (person == null) return;
            var target = ends.Count > 0 ? ends[UnityEngine.Random.Range(0, ends.Count)] : AwayFrom(person.Position);
            person.Leave(target, exit);
            leaving.Add(person);
        }

        // Without ways out: a point on the NavMesh about 15 m from the window, past the person.
        private Vector3 AwayFrom(Vector3 position)
        {
            var away = position - windowLook;
            away.y = 0f;
            var wanted = position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 15f;
            return NavMesh.SamplePosition(wanted, out var hit, 4f, QueuePerson.Filter) ? hit.position : wanted;
        }

        // The line runs from a window place beside the vehicle along its length, on whichever of the window's
        // sides and in whichever direction keeps the most places on walkable pavement, out of parking lanes and
        // off the road. No walkable window place at all: the line has no places and shows nobody.
        private static (List<Vector3> Places, Vector3 Look, List<Vector3> Ends, string Detail) Layout(Transform cart, int count, WindowShape shape)
        {
            var forward = Direction(cart.forward);
            var right = Direction(cart.right);
            var center = cart.position + forward * shape.Along;
            var filter = QueuePerson.Filter;

            var best = new List<Vector3>();
            var bestScore = int.MinValue;
            var bestSide = 1;
            var bestDirection = 1;
            foreach (var side in shape.Sides)
            {
                foreach (var direction in new[] { 1, -1 })
                {
                    var placed = new List<Vector3>();
                    var roads = 0;
                    for (var k = 0; k < count; k++)
                    {
                        var place = PlaceBeside(center, right * side, forward * (direction * k * Spacing), shape, filter);
                        if (place == null) break;
                        if (Legality.RoadBelow(place.Value)) roads++;
                        placed.Add(place.Value);
                    }
                    var score = placed.Count * 10 - roads * 3;
                    if (score > bestScore)
                    {
                        best = placed;
                        bestScore = score;
                        bestSide = side;
                        bestDirection = direction;
                    }
                }
            }
            if (best.Count == 0) return (best, center, new List<Vector3>(), $"no walkable place beside the window at {center}; nobody shown");

            // Ways in and out: points on the pavement a little way along in both directions, reachable from the window.
            var ends = new List<Vector3>();
            var path = new NavMeshPath();
            foreach (var along in new[] { 11f, -9f, 15f, -13f, 19f, -17f })
            {
                var wanted = center + forward * (bestDirection * along) + right * (bestSide * UnityEngine.Random.Range(shape.Sideways - 0.45f, shape.Reach + 1.35f));
                if (!NavMesh.SamplePosition(wanted, out var hit, 2.5f, filter)) continue;
                if (shape.PavementOnly && (Legality.InParkingLane(hit.position) || Legality.RoadBelow(hit.position) || Legality.NearCrosswalk(hit.position, 0.5f))) continue;
                if (!NavMesh.CalculatePath(hit.position, best[0], filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                ends.Add(hit.position);
            }
            for (var attempt = 0; ends.Count < 2 && attempt < 8; attempt++)
            {
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var wanted = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * UnityEngine.Random.Range(9f, 16f);
                if (!NavMesh.SamplePosition(wanted, out var hit, 2.5f, filter)) continue;
                if (shape.PavementOnly && (Legality.InParkingLane(hit.position) || Legality.RoadBelow(hit.position) || Legality.NearCrosswalk(hit.position, 0.5f))) continue;
                if (!NavMesh.CalculatePath(hit.position, best[0], filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                ends.Add(hit.position);
            }
            var detail = $"{best.Count} of {count} places on the {(bestSide > 0 ? "right" : "left")} side towards the {(bestDirection > 0 ? "front" : "back")}, " +
                         $"{ends.Count} ways in and out";
            return (best, center, ends, detail);
        }

        // The nearest walkable place out of parking lanes beside the window, from Sideways out to Reach.
        private static Vector3? PlaceBeside(Vector3 center, Vector3 outward, Vector3 along, WindowShape shape, NavMeshQueryFilter filter)
        {
            for (var sideways = shape.Sideways; sideways <= shape.Reach + 0.01f; sideways += 0.4f)
            {
                var wanted = center + outward * sideways + along;
                if (!NavMesh.SamplePosition(wanted, out var hit, 0.6f, filter) || FlatDistance(hit.position, wanted) > 0.45f) continue;
                if (Legality.InParkingLane(hit.position)) continue;
                if (shape.PavementOnly && (Legality.RoadBelow(hit.position) || Legality.NearCrosswalk(hit.position, 0.5f))) continue;
                return hit.position;
            }
            return null;
        }

        // Whether the vehicle's window has pavement beside it, for refusing a truck parked with its hatch to the road.
        public static bool HasWindowPlace(Transform vehicle, string kind)
        {
            var shape = WindowShape.For(kind);
            var forward = Direction(vehicle.forward);
            var right = Direction(vehicle.right);
            var center = vehicle.position + forward * shape.Along;
            return shape.Sides.Any(side => PlaceBeside(center, right * side, Vector3.zero, shape, QueuePerson.Filter) != null);
        }

        private static Vector3 Direction(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    // One customer: a game character walking on the NavMesh, as spike 5 showed.
    internal sealed class QueuePerson : MonoBehaviour
    {
        private const string CharacterPrefab = "Characters/HumanDefinitionLow";
        private const int PlayerAgentTypeId = 1479372276;
        // Someone who can't reach their place in this time is put there; someone leaving is removed.
        private const float StuckSeconds = 20f;
        private const float LeaveSeconds = 30f;
        // How close to a place counts as being there.
        private const float ArriveMetres = 0.35f;
        private const float TurnDegreesPerSecond = 360f;

        private static bool expressionFailed;
        private static bool agentLogged;

        private ThirdPersonCharacter? character;
        private Vector3 target;
        private Vector3 lookAt;
        private bool walking;
        private bool hasTarget;
        private bool leavingNow;
        // No complete path to the target: a person in line is put there, a leaving one removed.
        private bool noPath;
        private float walkStarted;
        private float payUntil;
        private float standingSince;

        public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = PlayerAgentTypeId, areaMask = NavMesh.AllAreas };

        public bool Arrived => hasTarget && !walking;
        // Where the character stands; the owner object itself stays at the world origin.
        public Vector3 Position => character != null ? character.transform.position : transform.position;
        public bool Paying { get; private set; }
        // How long they have stood at their current place.
        public float SecondsStanding => walking || !hasTarget ? 0f : Time.time - standingSince;
        public bool DonePaying => Paying && Time.time >= payUntil;

        public static QueuePerson? Create(Transform parent, long id, Vector3 start)
        {
            var owner = new GameObject("FoodTrucks.Customer." + id);
            owner.transform.SetParent(parent, false);
            var person = owner.AddComponent<QueuePerson>();
            try
            {
                if (person.Initialize(start)) return person;
            }
            catch (Exception ex)
            {
                Log.Error("Queue view: customer not created: " + ex);
            }
            Destroy(owner);
            return null;
        }

        private bool Initialize(Vector3 start)
        {
            character = PrefabHelper.CreatePrefab<ThirdPersonCharacter>(CharacterPrefab, transform);
            if (character == null) throw new InvalidOperationException($"Prefab {CharacterPrefab} not found.");
            character.navmeshAgent.agentTypeID = PlayerAgentTypeId;
            character.gameObject.SetActive(true);
            character.isPlayer = false;
            character.appearanceSetter.SetRandomAppearance();
            var agent = character.navmeshAgent;
            if (!NavMesh.SamplePosition(start, out var hit, 2f, Filter) || !agent.Warp(hit.position))
            {
                Log.Error($"Queue view: no NavMesh near {start}.");
                return false;
            }
            character.SetWalkingSpeed(ThirdPersonCharacter.WalkingSpeed.Walk);
            if (!agentLogged)
            {
                agentLogged = true;
                Log.Info($"Queue view: customer agent speed {agent.speed:F2} m/s, stopping distance {agent.stoppingDistance:F2} m, radius {agent.radius:F2} m.");
            }
            return true;
        }

        // Walks to a place in line and faces `look` once there. Does nothing if already heading there.
        public void GoTo(Vector3 place, Vector3 look)
        {
            if (leavingNow || character == null) return;
            lookAt = look;
            if (hasTarget && (place - target).sqrMagnitude < 0.01f) return;
            Walk(place);
        }

        public void Pay(float seconds, ServiceMood mood)
        {
            Paying = true;
            payUntil = Time.time + seconds;
            if (mood == ServiceMood.Pleased) Express(CharacterEmojiName.CustomerGoodService);
            else if (mood == ServiceMood.Complaining) Express(CharacterEmojiName.CustomerBadService);
        }

        public void Leave(Vector3 to, CustomerExit? exit)
        {
            leavingNow = true;
            // Someone who waited in vain shows it as they go [the game's own emoji, unverified on a spawned character].
            if (exit == CustomerExit.GaveUp) Express(CharacterEmojiName.CommonRedAngry);
            if (character != null) Walk(to);
        }

        // One frame. Returns true when the person is done: a leaving one has gone, or the character was removed.
        public bool Tick()
        {
            if (character == null) return true;
            var agent = character.navmeshAgent;
            if (agent == null || !agent.isActiveAndEnabled) return true;
            if (walking)
            {
                var near = FlatDistance(character.transform.position, target) <= ArriveMetres;
                var reached = near || (agent.isOnNavMesh && !agent.pathPending && agent.hasPath &&
                                       agent.pathStatus == NavMeshPathStatus.PathComplete &&
                                       agent.remainingDistance <= agent.stoppingDistance + 0.1f && FlatDistance(character.transform.position, target) <= 1f);
                var stuck = noPath || Time.time - walkStarted > (leavingNow ? LeaveSeconds : StuckSeconds);
                if (leavingNow) return reached || stuck;
                if (reached) Stop();
                else if (stuck)
                {
                    // Put them in their place rather than leave them standing somewhere else.
                    if (!agent.Warp(target)) return true;
                    Stop();
                }
                return false;
            }
            if (leavingNow) return true;
            // Standing in line: turn to face the person ahead, or the cart at the window.
            var facing = lookAt - character.transform.position;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f)
            {
                var wanted = Quaternion.LookRotation(facing);
                character.transform.rotation = Quaternion.RotateTowards(character.transform.rotation, wanted, TurnDegreesPerSecond * Time.deltaTime);
            }
            return false;
        }

        private void Walk(Vector3 to)
        {
            if (character == null) return;
            var agent = character.navmeshAgent;
            target = to;
            hasTarget = true;
            walking = true;
            noPath = false;
            walkStarted = Time.time;
            if (!agent.isOnNavMesh)
            {
                noPath = true;
                return;
            }
            agent.isStopped = false;
            var path = new NavMeshPath();
            if (agent.CalculatePath(to, path) && path.status == NavMeshPathStatus.PathComplete) agent.SetPath(path);
            else
            {
                agent.ResetPath();
                noPath = true;
            }
        }

        private void Stop()
        {
            walking = false;
            standingSince = Time.time;
            if (character == null) return;
            var agent = character.navmeshAgent;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            character.LookTarget = Vector3.zero;
            character.Move(Vector3.zero);
        }

        private void Express(CharacterEmojiName emoji)
        {
            if (character == null || expressionFailed) return;
            try { StartCoroutine(character.ShowExpression(emoji, 2f)); }
            catch (Exception ex)
            {
                // Once is enough: the queue works without them.
                expressionFailed = true;
                Log.Error("Queue view: customer expressions unavailable: " + ex.Message);
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private void OnDestroy()
        {
            character = null;
        }
    }
}
