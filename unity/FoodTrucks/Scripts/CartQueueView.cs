#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using HarmonyLib;
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
    // People appear on the pavement a little way along when Core says they turn up, and walk towards the
    // back of the line while Core has them walking up (ServiceState.Approaching). They get a place in line
    // only when Core lets them join the queue, step up as it moves, stand at the window while their order
    // is made, and pay and walk off when Core completes it. An in-game minute is a second at normal speed,
    // so Core's walk-in and service times match what the view shows; someone who lags behind Core still
    // walks up, picks up and pays before leaving.
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
        // turns from the window to leave (Paid).
        public static bool Showing(string vehicleId) =>
            instance != null && instance.enabled && instance.lines.TryGetValue(vehicleId, out var line) && line.HasPlaces;

        // The side of the vehicle its open line forms on (1 right, -1 left), or null while no line is laid out
        // (the vehicle is closed, far from the player, or has no walkable place beside it).
        public static int? QueueSide(string vehicleId) =>
            instance != null && instance.lines.TryGetValue(vehicleId, out var line) && line.Side != 0 ? line.Side : (int?)null;

        // Lay out this opening's line before placing its server. Waiting for CheckLines would let the player
        // take the right-hand serving place before the queue chose the left. No customers are spawned here.
        internal static (int Side, Vector3? Window)? PrepareServing(VehicleController controller)
        {
            var self = instance;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, controller.vehicleInstance.id);
            if (self == null || !self.enabled || Suspended || vehicle?.IsOpen != true || vehicle.Service == null ||
                DemandData.Rules == null) return null;
            var line = self.EnsureLine(vehicle, controller);
            return line.Side == 0 ? null : (line.Side, line.WindowPlace);
        }

        // Where the open line's people stand, the window first, or null while no line is laid out (for the test bridge).
        public static IReadOnlyList<Vector3>? PlacesOf(string vehicleId) =>
            instance != null && instance.lines.TryGetValue(vehicleId, out var line) && line.HasPlaces ? line.Places : null;

        // The cart closed: everyone in line walks off, then the line goes.
        public static void Closed(string vehicleId) => instance?.Retire(vehicleId);

        // Looks again at the next frame: the player went to or left a truck's hatch, so the crew move.
        public static void Recheck()
        {
            if (instance != null) instance.nextCheck = 0f;
        }

        // Removes every person at once: time skips, the uninstall step, leaving the city.
        public static void ClearAll() => instance?.RemoveAll("cleared");

        private void Retire(string vehicleId)
        {
            WindowCrew.Remove(vehicleId);
            if (!lines.TryGetValue(vehicleId, out var line)) return;
            lines.Remove(vehicleId);
            line.Close();
            departing.Add(line);
        }

        // A moved cart's line goes at once rather than walking off: its customers are still in Core's queue, and
        // the new line shows them again at its own places, so a walking-off copy would show each of them twice.
        private void Relay(QueueLine line)
        {
            Log.Info($"Queue view: cart {line.VehicleId} moved or turned since its line was laid out; laying it out again.");
            line.Destroy("cart moved");
            WindowCrew.Remove(line.VehicleId);
            lines.Remove(line.VehicleId);
        }

        private void RemoveAll(string reason)
        {
            foreach (var line in lines.Values.Concat(departing)) line.Destroy(reason);
            WindowCrew.RemoveAll();
            lines.Clear();
            departing.Clear();
        }

        private void Update()
        {
            try
            {
                if (Time.unscaledTime >= nextCheck || ServingWork.Accelerated)
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
            // The player is gone while the game quits; there is nothing to check against then.
            if (PlayerHelper.PlayerController == null) return;
            var player = PlayerHelper.GetPosition();
            foreach (var line in lines.Values.ToList())
            {
                var controller = ControllerOf(game, line.VehicleId);
                var vehicle = state.Vehicles.FirstOrDefault(v => v.VehicleId == line.VehicleId);
                if (controller == null || Vector3.Distance(player, controller.transform.position) > KeepMetres)
                {
                    line.Destroy("out of range");
                    WindowCrew.Remove(line.VehicleId);
                    lines.Remove(line.VehicleId);
                }
                else if (vehicle == null || !vehicle.IsOpen || vehicle.Service == null || game.ActiveVehicleId == line.VehicleId)
                    Retire(line.VehicleId);
                // A cart closed and opened again between two checks has a new queue.
                else if (!ReferenceEquals(line.Service, vehicle.Service))
                {
                    Retire(line.VehicleId);
                }
                // A cart that stands somewhere else than where its line was laid out gets a new line below, on its
                // live position and heading, so its register, server and first customer agree again. Not under the
                // player's serving stance, which ends by itself once the cart moves (CartServing) and must not see
                // the queue change sides beneath it.
                else if (line.MovedFrom(controller.transform) && !CartSelling.PlayerAtServingSpot(line.VehicleId))
                    Relay(line);
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
                EnsureLine(vehicle, controller);
            }

            // The crew on shift stands at each window in view.
            var now = HireCrew.Now();
            foreach (var line in lines.Values)
            {
                var vehicle = state.Vehicles.FirstOrDefault(v => v.VehicleId == line.VehicleId);
                var controller = ControllerOf(game, line.VehicleId);
                if (vehicle == null || controller == null) continue;
                var kind = FoodTrucksMod.KindOf(controller.vehicleInstance?.vehicleTypeName) ?? "cart";
                WindowCrew.Sync(line.VehicleId, controller.transform, kind, line.Side, HireCrew.OnShift(vehicle, now), CartSelling.ServingNow(vehicle, kind));
            }
        }

        private QueueLine EnsureLine(VehicleState vehicle, VehicleController controller)
        {
            if (lines.TryGetValue(vehicle.VehicleId, out var existing))
            {
                if (ReferenceEquals(existing.Service, vehicle.Service))
                {
                    // The stance guard is defensive: CartServing.Open releases the stance before it asks for the line.
                    if (!existing.MovedFrom(controller.transform) || CartSelling.PlayerAtServingSpot(vehicle.VehicleId)) return existing;
                    Relay(existing);
                }
                else Retire(vehicle.VehicleId);
            }
            var rules = DemandData.Rules!;
            var kind = FoodTrucksMod.KindOf(controller.vehicleInstance?.vehicleTypeName) ?? "cart";
            var limit = rules.Service.QueueLimit(kind, rules.Queue.MaxQueue);
            var line = QueueLine.Create(vehicle.VehicleId, controller.transform, limit, vehicle.Service!, WindowShape.For(kind));
            line.OnSpawned = () => spawnedTotal++;
            var id = vehicle.VehicleId;
            line.Paid = customer => CartSelling.Paid(id, customer);
            lines[id] = line;
            return line;
        }

        private static VehicleController? ControllerOf(GameInstance game, string vehicleId)
        {
            var cart = game.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            return cart == null ? null : VehicleHelper.GetVehicleController(cart);
        }
    }

    // Where a vehicle's window is, relative to its middle: Sideways metres to the window place, Along metres
    // forward of the middle, on which sides. A cart serves from either side, level with its cash register on the
    // counter's front corner (Core CartStance, z 0.615); the truck's hatch is on its right (art/README.md: opening
    // at x 1.10, centre z -1.10), so its line is always on the right.
    internal sealed class WindowShape
    {
        private WindowShape(float sideways, float reach, float along, int[] sides, float spacing, bool preferBack,
            float tolerance, bool strict, bool outward)
        {
            Outward = outward;
            Tolerance = tolerance;
            Strict = strict;
            Spacing = spacing;
            PreferBack = preferBack;
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
        // The line may also run straight out from the window, away from the vehicle, instead of along its length:
        // a cart pushed in at right angles to the kerb has its length across the pavement, so a line along it would
        // run into the street (blind run 2, Peter). Whichever keeps more places on the pavement wins, along on a tie.
        public bool Outward { get; }
        public int[] Sides { get; }
        // Metres between people in line.
        public float Spacing { get; }
        // Between two directions that place as many people, the one towards the vehicle's back.
        public bool PreferBack { get; }
        // How far the NavMesh point found may be from the place wanted.
        public float Tolerance { get; }
        // Each place is checked where it actually landed (review round 4): at least Sideways out from the middle, and
        // at least MinSeparation of Spacing from the place before; the line keeps one distance out, or is cut short.
        public bool Strict { get; }
        public const float MinSeparation = 0.9f;
        // The truck body's half width (art/README.md, NavMeshObstacle extents), for the logged clearance.
        public const float TruckHalfWidth = 1.10f;

        // The truck (0.9.9; agent run of 11 October 2026: "a loose group, bunched close against the truck's side", two
        // customers circling their places). Its body is 1.10 m either side of its middle (art/README.md, NavMeshObstacle),
        // so 0.9.8's places 1.80 m out stood 0.7 m from the side, 0.8 m apart with 0.3 m agents, and each place took
        // the nearest pavement on its own, so the line zigzagged between 1.8 and 3.4 m out. Now people stand a step
        // apart (1.0 m) in one straight line 2.0 m out or further (Layout tries each distance out for the whole line and
        // keeps the one that places most), and the line runs towards the truck's back, where the hatch (1.10 m behind
        // the middle) is 2 m from the end of the body rather than 3.8 m, so most of it stands along open pavement. Each
        // place is checked where the NavMesh put it (within 0.2 m of the wanted point, still 2.0 m out at least, 0.9 m
        // or more from the one before), so the clearance and spacing hold as built.
        private static readonly WindowShape Cart = new WindowShape((float)CartStance.WindowOut, (float)CartStance.WindowOut, (float)CartStance.RegisterAlong, new[] { 1, -1 }, spacing: 0.8f, preferBack: false,
            tolerance: 0.45f, strict: false, outward: true);
        private static readonly WindowShape Truck = new WindowShape(2.00f, 3.40f, -1.10f, new[] { 1 }, spacing: 1.0f, preferBack: true,
            tolerance: 0.2f, strict: true, outward: false);

        public static WindowShape For(string kind) => kind == "truck" ? Truck : Cart;
    }

    // Marks a line's root object, so a later runtime can find and remove what an earlier one left.
    internal sealed class QueueLineRoot : MonoBehaviour
    {
    }

    // One cart's line: the places along the pavement, the ways in and out, and the people.
    internal sealed class QueueLine
    {
        // A served customer stands at the window at least this long, in game-speed seconds, before paying and
        // leaving: Core's service time already kept them there while the order was made (an in-game minute is
        // a second at normal speed), so this only matters for someone who got there after Core finished.
        private const float PickUpSeconds = 1f;
        private const float SpawnGapSeconds = 0.6f;
        // Served customers still in line beyond this pay and walk off without stopping at the window.
        private const int MaxShownServed = 5;
        // Customers walking up who are shown at once, the soonest to reach the line first.
        private const int MaxShownApproaching = 4;
        // Assumed walking pace before the first customer's NavMesh agent reports its own (unverified in game).
        private const float DefaultWalkMetresPerSecond = 1.4f;
        // Places beyond the queue limit: served customers picking up, and customers walking up behind the line.
        private const int ExtraPlaces = 3;
        private const float ClosingSeconds = 40f;
        private const int MaxSpawnFailures = 3;
        private const float SpawnRetrySeconds = 15f;

        private readonly List<Vector3> places;
        private readonly Vector3 windowLook;
        private readonly List<Vector3> ends;
        // The driving lanes near the line, so every walk a customer starts is checked against them (QueuePerson.Walk).
        private readonly TrafficLanes? lanes;
        // Customers taken out of view because their next walk is unsafe: not shown again.
        private readonly HashSet<long> unseen = new HashSet<long>();
        private readonly GameObject root;
        private readonly Dictionary<long, QueuePerson> people = new Dictionary<long, QueuePerson>();
        private readonly List<QueuePerson> leaving = new List<QueuePerson>();
        // Customers Core has served who haven't left the window yet, in the order they were served. Each has a
        // sale CartSelling holds until they leave (Release).
        private readonly List<long> served = new List<long>();
        private bool fresh = true;
        private float nextSpawn;
        private float closedAt;
        private int spawnFailures;
        // After repeated spawn failures, nobody is spawned until then, a new line's first batch included.
        private float blockedUntil;

        // How far a cart may stand from where its line was laid out before the line is laid out again: the same
        // limits that end the player's serving stance (CartServing).
        private const float MovedMetres = 0.1f;
        private const float TurnedDegrees = 1f;

        private QueueLine(string vehicleId, GameObject root, List<Vector3> places, Vector3 windowLook, List<Vector3> ends, ServiceState service, TrafficLanes? lanes)
        {
            VehicleId = vehicleId;
            this.lanes = lanes;
            this.root = root;
            this.places = places;
            this.windowLook = windowLook;
            this.ends = ends;
            Service = service;
        }

        public string VehicleId { get; }
        // "truck" or "cart", for the log (live session 5: a truck's line was logged as a cart's).
        public string Kind => kind ??= KindOf(VehicleId);
        private string? kind;

        private static string KindOf(string vehicleId) =>
            FoodTrucksMod.KindOf(SaveGameManager.Current?.VehicleInstances?.FirstOrDefault(v => v != null && v.id == vehicleId)?.vehicleTypeName) ?? "vehicle";
        public ServiceState Service { get; }
        public ServiceMood Mood { get; set; } = ServiceMood.Neutral;
        public Dictionary<long, CustomerExit> Exits { get; } = new Dictionary<long, CustomerExit>();
        public Action? OnSpawned { get; set; }
        // Called once per served customer: when they pay and turn from the window to leave, or when the view
        // lets them go unseen (dropped, removed, closed), so CartSelling never holds a sale for good.
        public Action<long>? Paid { get; set; }
        public bool HasPlaces => places.Count > 0;
        public IReadOnlyList<Vector3> Places => places;
        // The vehicle's side the line forms on: 1 right, -1 left, 0 with no places.
        public int Side { get; private set; }
        // Where the first customer stands, for the crew behind the window.
        public Vector3? WindowPlace => places.Count > 0 ? places[0] : (Vector3?)null;
        public int Count => people.Count + leaving.Count;
        public bool Finished => Count == 0 || Time.time - closedAt > ClosingSeconds;

        // Places for the window, the queue limit and a few more behind them.
        public static QueueLine Create(string vehicleId, Transform cart, int queueLimit, ServiceState service, WindowShape shape)
        {
            var (places, look, ends, detail, side, lanes) = Layout(cart, queueLimit + 1 + ExtraPlaces, shape);
            var root = new GameObject("FoodTrucks.Queue." + vehicleId);
            root.AddComponent<QueueLineRoot>();
            Log.Info($"Queue view: line at {KindOf(vehicleId)} {vehicleId}: {detail}.");
            return new QueueLine(vehicleId, root, places, look, ends, service, lanes)
            {
                Side = side, laidAt = cart.position, laidRotation = cart.rotation, checksMoves = shape == WindowShape.For("cart"),
            };
        }

        // Only a cart's line: a truck only moves by being driven, which retires its line already, and its body may
        // settle on its suspension after parking.
        public bool MovedFrom(Transform cart) => checksMoves &&
            (Vector3.Distance(cart.position, laidAt) > MovedMetres || Quaternion.Angle(cart.rotation, laidRotation) > TurnedDegrees);

        private Vector3 laidAt;
        private Quaternion laidRotation;
        private bool checksMoves;

        private void ReleaseServed()
        {
            var ids = served.ToList();
            served.Clear();
            foreach (var id in ids) Release(id);
        }

        // A served customer's sale is paid: as they leave the window, or as the view lets them go unseen.
        private void Release(long id) => Paid?.Invoke(id);

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
            if (count > 0 || WalkSummary().Length > 0) Log.Info($"Queue view: {Kind} {VehicleId} line removed with {count} people ({reason}){WalkSummary()}.");
        }

        // One frame. Spawns at most `room` people; returns how many it spawned.
        public int Tick(int room)
        {
            var spawned = 0;
            // Runs even with no room to spawn, so a served customer nobody sees is paid at once (Order).
            if (places.Count > 0)
            {
                var order = Order(out var lineCount);
                var inLine = new HashSet<long>(order);
                unseen.RemoveWhere(id => !inLine.Contains(id));
                // Anyone no longer in line or walking up leaves: gave up, found the queue full, walked on, or
                // the window closed on them.
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
                        if (unseen.Contains(id)) continue;
                        if (room - spawned <= 0 || Time.time < blockedUntil || (!fresh && Time.time < nextSpawn)) continue;
                        // Only the first few walking up are shown; the rest appear as they get closer.
                        if (i >= lineCount && i - lineCount >= MaxShownApproaching) continue;
                        person = Spawn(id, i, i >= lineCount ? WalkSecondsLeft(id) : (float?)null);
                        if (person == null) continue;
                        spawned++;
                    }
                    // Someone walking up heads for the place behind the line; it becomes theirs if nobody
                    // reaches the line first.
                    person.GoTo(places[i], i == 0 ? windowLook : places[i - 1]);
                }
                fresh = false;
                FinishAtWindow(order);
            }

            // People whose character the game removed are dropped, so nobody waits behind them; so are those whose next
            // walk would cross a driving lane, and they aren't shown again.
            foreach (var pair in people.ToList())
            {
                if (pair.Value != null) NoteWalking(pair.Key, pair.Value);
                if (pair.Value != null && !pair.Value.Tick()) continue;
                if (pair.Value != null && pair.Value.UnsafeWalk)
                {
                    unseen.Add(pair.Key);
                    Log.Info($"Queue view: {Kind} {VehicleId}: a customer's walk rejected by {pair.Value.UnsafeWalkTest}; taken out of view.");
                }
                else if (pair.Value != null && pair.Value.Blocked)
                {
                    // Two people each standing on the other's place never get free: the one who waited too long goes
                    // from view, so the other can take their place. Their sale (if served) is paid below.
                    unseen.Add(pair.Key);
                    Log.Info($"Queue view: {Kind} {VehicleId}: customer {pair.Key} waited {QueuePerson.BlockedSeconds:F0} s for someone to clear their place; taken out of view.");
                }
                if (pair.Value != null) Object.Destroy(pair.Value.gameObject);
                people.Remove(pair.Key);
                if (served.Remove(pair.Key)) Release(pair.Key);
            }
            for (var i = leaving.Count - 1; i >= 0; i--)
            {
                var person = leaving[i];
                if (person != null && !person.Tick()) continue;
                if (person != null && person.UnsafeWalk)
                    Log.Info($"Queue view: {Kind} {VehicleId}: a customer's way out rejected by {person.UnsafeWalkTest}; taken out of view.");
                if (person != null) Object.Destroy(person.gameObject);
                leaving.RemoveAt(i);
            }
            if (Exits.Count > 0)
            {
                foreach (var id in Exits.Keys.Where(id => !people.ContainsKey(id)).ToList()) Exits.Remove(id);
            }
            return spawned;
        }

        // Spinning on the pavement (0.9.7 playtest) left no trace in the log. A customer who circled their place is
        // logged the first time in a line, with how far off they stopped; one walking to new targets more often than
        // a line ever needs (a place each time the line moves up, plus walking up and leaving) is logged once.
        private const int ManyWalks = 16;
        private int circled, circledWarped, manyWalksLogged;
        private float worstMiss;
        private int mostWalks;

        private void NoteWalking(long id, QueuePerson person)
        {
            if (person.Walks > mostWalks) mostWalks = person.Walks;
            if (person.Walks == ManyWalks + 1 && manyWalksLogged++ < 3)
                Log.Info($"Queue view: {Kind} {VehicleId}: customer {id} has walked to {person.Walks} targets; the line keeps moving them (re-pathing).");
            if (!(person.Circled is { } circle)) return;
            person.Circled = null;
            circled++;
            if (circle.Warped) circledWarped++;
            if (circle.Miss > worstMiss) worstMiss = circle.Miss;
            if (circled == 1)
                Log.Info($"Queue view: {Kind} {VehicleId}: a customer circled their place {circle.Miss:F2} m off without getting closer; " +
                         $"{(circle.Warped ? "put there" : "stopped where they were (their place is off the NavMesh)")}.");
        }

        private string WalkSummary() =>
            circled == 0 && mostWalks <= ManyWalks ? "" :
                $"; {circled} stopped after circling their place ({circledWarped} put there, worst {worstMiss:F2} m off), at most {mostWalks} walks by one customer";

        // Served customers first (they reached the window before anyone still waiting), then the orders
        // being made, then the queue; lineCount says how many those are. After them, the customers Core has
        // walking up, the soonest to reach the line first. A served customer nobody saw is paid at once, and
        // served ones still in line beyond a few pay and walk off.
        private List<long> Order(out int lineCount)
        {
            var order = new List<long>();
            var shown = 0;
            foreach (var id in served.ToList())
            {
                if (!people.TryGetValue(id, out var person))
                {
                    // Nobody to walk up and pay: the sale is paid now.
                    served.Remove(id);
                    Release(id);
                    continue;
                }
                if (++shown > MaxShownServed)
                {
                    served.Remove(id);
                    people.Remove(id);
                    Release(id);
                    StartLeaving(person, CustomerExit.Served);
                    continue;
                }
                order.Add(id);
            }
            var seen = new HashSet<long>(order);
            foreach (var customer in Service.Window.Concat(Service.Queue))
            {
                if (customer != null && seen.Add(customer.Id)) order.Add(customer.Id);
            }
            lineCount = order.Count;
            foreach (var customer in Service.Approaching.Where(c => c != null).OrderBy(c => c.JoinsAtMinute).ThenBy(c => c.Id))
            {
                if (seen.Add(customer.Id)) order.Add(customer.Id);
            }
            return order;
        }

        // Game minutes until Core lets this customer join the queue, which are seconds at normal speed.
        private float? WalkSecondsLeft(long id)
        {
            var customer = Service.Approaching.FirstOrDefault(c => c != null && c.Id == id);
            return customer == null ? (float?)null : Math.Max(0f, customer.JoinsAtMinute - Service.Minute);
        }

        // A served customer at the window, whose order Core has completed, pays and walks off: the money and
        // the sale message come now (Paid), as they turn to leave. Served customers always stand first in line,
        // so the second place is a second worker's window or the next served customer. With a backlog of
        // served customers nobody waits out the pick-up, so a fast window doesn't leave a crowd behind it.
        private void FinishAtWindow(List<long> order)
        {
            var hurried = served.Count > 2;
            for (var i = 0; i < 2 && i < order.Count; i++)
            {
                var id = order[i];
                if (!served.Contains(id) || !people.TryGetValue(id, out var person) || !person.Arrived) continue;
                if (!hurried && person.SecondsStanding < PickUpSeconds) continue;
                served.Remove(id);
                people.Remove(id);
                person.ShowMood(Mood);
                Release(id);
                StartLeaving(person, CustomerExit.Served);
            }
        }

        // `walkSeconds`: for someone walking up, how long Core has them walking, so they start about that far
        // out and reach the line close to when Core lets them join it.
        private QueuePerson? Spawn(long id, int place, float? walkSeconds)
        {
            nextSpawn = Time.time + SpawnGapSeconds;
            // A new line shows the people already waiting where they stand; later ones, and everyone walking
            // up, walk in.
            Vector3 start;
            if (ends.Count == 0) start = places[place];
            else if (walkSeconds != null) start = EndFor(walkSeconds.Value, places[place]);
            else if (fresh) start = places[place];
            else start = ends[UnityEngine.Random.Range(0, ends.Count)];
            var person = QueuePerson.Create(root.transform, id, start, lanes);
            if (person == null)
            {
                // A missing prefab or NavMesh won't fix itself: wait a while instead of filling the log.
                if (++spawnFailures >= MaxSpawnFailures)
                {
                    blockedUntil = Time.time + SpawnRetrySeconds;
                    spawnFailures = 0;
                    Log.Error($"Queue view: {Kind} {VehicleId}: customers could not be created; trying again in {SpawnRetrySeconds:F0} s.");
                }
                return null;
            }
            spawnFailures = 0;
            people[id] = person;
            OnSpawned?.Invoke();
            return person;
        }

        // The way in whose distance to the place best matches the walk Core has left for this customer.
        private Vector3 EndFor(float walkSeconds, Vector3 place)
        {
            var wanted = walkSeconds * (QueuePerson.WalkMetresPerSecond ?? DefaultWalkMetresPerSecond);
            var best = ends[0];
            var bestMiss = float.MaxValue;
            foreach (var end in ends)
            {
                var miss = Math.Abs(Vector3.Distance(end, place) - wanted);
                if (miss >= bestMiss) continue;
                best = end;
                bestMiss = miss;
            }
            return best;
        }

        private void StartLeaving(QueuePerson person, CustomerExit? exit)
        {
            if (person == null) return;
            var target = ends.Count > 0 ? ends[UnityEngine.Random.Range(0, ends.Count)] : AwayFrom(person.Position);
            if (target == null)
            {
                Object.Destroy(person.gameObject);
                return;
            }
            person.Leave(target.Value, exit);
            leaving.Add(person);
        }

        // Without ways out, try the same pavement/path checks; never fall back to an unchecked point in the street.
        private Vector3? AwayFrom(Vector3 position)
        {
            var away = position - windowLook;
            away.y = 0f;
            var wanted = position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 15f;
            return lanes == null ? null : WayIn(wanted, position, QueuePerson.PedestrianFilter, lanes, new NavMeshPath(), new Rejections());
        }

        // The line runs from a window place beside the vehicle along its length (a cart: or straight out from it,
        // WindowShape.Outward), on whichever of the window's sides and in whichever direction keeps the most places
        // on walkable pavement, out of parking lanes and off the road. No walkable window place at all: the line has
        // no places and shows nobody.
        private static (List<Vector3> Places, Vector3 Look, List<Vector3> Ends, string Detail, int Side, TrafficLanes Lanes) Layout(Transform cart, int count, WindowShape shape)
        {
            var forward = Direction(cart.forward);
            var right = Direction(cart.right);
            var center = cart.position + forward * shape.Along;
            var filter = QueuePerson.PedestrianFilter;
            var rejected = new Rejections();
            // The NPC mesh is the positive pedestrian-ground test. Lane/parking/bridge checks remain extra exclusions,
            // not evidence of pavement: the player mesh and gaps between traffic lanes also cover carriageways.
            var lanes = TrafficLanes.Near(center, 30f);

            var best = new List<Vector3>();
            var bestScore = int.MinValue;
            var bestSide = 1;
            var bestDirection = 1;
            var bestOut = 0f;
            var bestOutward = false;
            void Try(int side, int direction, float lineOut, bool straightOut)
            {
                var outward = right * side;
                var placed = new List<Vector3>();
                for (var k = 0; k < count; k++)
                {
                    var wanted = straightOut
                        ? center + outward * (lineOut + k * shape.Spacing)
                        : center + outward * lineOut + forward * (direction * k * shape.Spacing);
                    var place = PlaceAt(wanted, shape, filter, lanes, rejected);
                    if (place == null) break;
                    if (shape.Strict)
                    {
                        if (Vector3.Dot(place.Value - cart.position, outward) < shape.Sideways - 0.01f)
                        { rejected.Add("vehicle clearance"); break; }
                        if (placed.Count > 0 && FlatDistance(placed[placed.Count - 1], place.Value) < WindowShape.MinSeparation)
                        { rejected.Add("place spacing"); break; }
                    }
                    if (placed.Count > 0 && CrossesRoad(placed[placed.Count - 1], place.Value, lanes, out var test))
                    { rejected.Add(test); break; }
                    placed.Add(place.Value);
                }
                var score = placed.Count * 10 + (shape.PreferBack && direction < 0 ? 1 : 0);
                if (score > bestScore)
                {
                    best = placed;
                    bestScore = score;
                    bestSide = side;
                    bestDirection = direction;
                    bestOut = lineOut;
                    bestOutward = straightOut;
                }
            }
            foreach (var side in shape.Sides)
            {
                // One distance out for the whole line, tried from Sideways to Reach: the one that places most people wins,
                // the nearest on a tie. A place that isn't walkable pavement at that distance (or, strict, lands too close
                // to the truck or to the place before) ends the line there.
                for (var lineOut = shape.Sideways; lineOut <= shape.Reach + 0.01f; lineOut += 0.2f)
                    foreach (var direction in new[] { 1, -1 })
                        Try(side, direction, lineOut, straightOut: false);
            }
            if (shape.Outward)
                foreach (var side in shape.Sides)
                    Try(side, 1, shape.Sideways, straightOut: true);
            if (best.Count == 0) return (best, center, new List<Vector3>(), $"no walkable place beside the window at {center}; nobody shown{rejected}", 0, lanes);

            // Ways in and out: points on the pavement a little way along the line's direction both ways, reachable from
            // the window. A path that crosses a driving lane (from the far pavement, or round an obstacle through the
            // street) isn't used.
            var lineAxis = bestOutward ? right * bestSide : forward * bestDirection;
            var crossAxis = bestOutward ? forward : right * bestSide;
            var ends = new List<Vector3>();
            var path = new NavMeshPath();
            foreach (var along in new[] { 11f, -9f, 15f, -13f, 19f, -17f })
            {
                var wanted = center + lineAxis * along + crossAxis * UnityEngine.Random.Range(shape.Sideways - 0.45f, shape.Reach + 1.35f);
                if (WayIn(wanted, best[0], filter, lanes, path, rejected) is { } way) ends.Add(way);
            }
            for (var attempt = 0; ends.Count < 2 && attempt < 8; attempt++)
            {
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // A truck's fallback ways stay on the hatch side's half too.
                if (shape.Strict && Vector3.Dot(offset, right * bestSide) < 0f) offset -= 2f * Vector3.Dot(offset, right * bestSide) * (right * bestSide);
                var wanted = center + offset * UnityEngine.Random.Range(9f, 16f);
                if (WayIn(wanted, best[0], filter, lanes, path, rejected) is { } way) ends.Add(way);
            }
            var shapeText = bestOutward
                ? $"straight out from the {(bestSide > 0 ? "right" : "left")} side"
                : $"on the {(bestSide > 0 ? "right" : "left")} side towards the {(bestDirection > 0 ? "front" : "back")}";
            var detail = $"{best.Count} of {count} places {shapeText}, " +
                         $"{shape.Spacing:F1} m apart as wanted, {bestOut:F1} m out from the middle; as placed: {Geometry(best, cart, right * bestSide, shape)}, " +
                         $"{ends.Count} ways in and out{rejected}";
            return (best, center, ends, detail, bestSide, lanes);
        }

        // The line as placed: how far out (from the vehicle's middle, and clear of a truck's side), and how far apart.
        private static string Geometry(List<Vector3> places, Transform cart, Vector3 outward, WindowShape shape)
        {
            var outs = places.Select(p => Vector3.Dot(p - cart.position, outward)).ToList();
            var gaps = Enumerable.Range(1, Math.Max(0, places.Count - 1)).Select(i => FlatDistance(places[i - 1], places[i])).ToList();
            var text = $"{outs.Min():F2}…{outs.Max():F2} m out";
            if (shape.Strict) text += $" ({outs.Min() - WindowShape.TruckHalfWidth:F2} m clear of the side at the least)";
            return text + (gaps.Count == 0 ? "" : $", {gaps.Min():F2}…{gaps.Max():F2} m apart");
        }

        // Counts rejected candidate places and ways across all layouts, including layouts that did not win.
        private sealed class Rejections
        {
            private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
            public void Add(string test) => counts[test] = counts.TryGetValue(test, out var count) ? count + 1 : 1;
            public override string ToString() => counts.Count == 0 ? "" :
                "; candidates rejected by " + string.Join(", ", counts.Select(p => $"{p.Key}: {p.Value}"));
        }

        private static Vector3? PlaceAt(Vector3 wanted, WindowShape shape, NavMeshQueryFilter filter, TrafficLanes lanes, Rejections rejected)
        {
            if (!NavMesh.SamplePosition(wanted, out var hit, 0.6f, filter))
            { rejected.Add("NPC NavMesh (agent 0)"); return null; }
            if (FlatDistance(hit.position, wanted) > shape.Tolerance)
            { rejected.Add("placement tolerance on NPC NavMesh"); return null; }
            if (OnStreet(hit.position, lanes) is { } test)
            { rejected.Add(test); return null; }
            return hit.position;
        }

        // An endpoint on pedestrian ground and a complete NPC path. The actual walk is checked again by the agent,
        // in both directions and to every place, since a path to the window need not be the path to the line's tail.
        private static Vector3? WayIn(Vector3 wanted, Vector3 first, NavMeshQueryFilter filter, TrafficLanes lanes, NavMeshPath path, Rejections rejected)
        {
            if (!NavMesh.SamplePosition(wanted, out var hit, 2.5f, filter))
            { rejected.Add("NPC NavMesh (agent 0)"); return null; }
            if (OnStreet(hit.position, lanes) is { } test)
            { rejected.Add(test); return null; }
            if (!NavMesh.CalculatePath(hit.position, first, filter, path) || path.status != NavMeshPathStatus.PathComplete)
            { rejected.Add("complete NPC path"); return null; }
            if (CrossesRoad(path.corners, lanes, out var pathTest))
            { rejected.Add(pathTest); return null; }
            return hit.position;
        }

        // Extra exclusions for carts and trucks alike. Never infer pavement from the absence of these signals.
        // Crosswalk triggers are the game's pedestrian crossing objects (PedestrianMovementHandler.OnTriggerEnter).
        // Queues use the same pavement throughout; they do not send customers across a crossing or wait on one.
        internal static string? OnStreet(Vector3 position, TrafficLanes lanes)
        {
            if (Legality.InParkingLane(position)) return "parking lane";
            if (Legality.NearCrosswalk(position, 0.5f)) return "Crosswalk trigger";
            if (Legality.RoadBelow(position)) return "Roads layer";
            if (lanes.Distance(position) <= HatchSide.RoadsideMetres) return "traffic lane / roadside strip";
            return null;
        }

        // Empty paths are unproven, not safe. Every segment must stay on the NPC mesh and off the street.
        internal static bool CrossesRoad(Vector3[] corners, TrafficLanes lanes, out string test)
        {
            test = "empty NPC path";
            if (corners.Length == 0) return true;
            if (corners.Length == 1) return CrossesRoad(corners[0], corners[0], lanes, out test);
            for (var i = 1; i < corners.Length; i++)
                if (CrossesRoad(corners[i - 1], corners[i], lanes, out test)) return true;
            return false;
        }

        // Raycast on the pedestrian mesh catches gaps between places, even with no traffic data. Unlike sampling,
        // it cannot step over a narrow road/mesh boundary. The extra collider/lane exclusions are sampled too.
        private static bool CrossesRoad(Vector3 a, Vector3 b, TrafficLanes lanes, out string test)
        {
            test = "NPC NavMesh (agent 0) boundary";
            if (NavMesh.Raycast(a, b, out _, QueuePerson.PedestrianFilter)) return true;
            var steps = Mathf.Max(1, Mathf.CeilToInt(FlatDistance(a, b) / 0.5f));
            for (var i = 0; i <= steps; i++)
            {
                var point = Vector3.Lerp(a, b, i / (float)steps);
                if (OnStreet(point, lanes) is { } reason) { test = reason; return true; }
            }
            return false;
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
        // How close to a place counts as being there: the game's own MoveToPosition default for its characters.
        private const float ArriveMetres = 0.5f;
        // The agent stops this far short of its place (the prefab's 0.10 m is about a third of its 0.30 m radius).
        private const float StoppingMetres = 0.3f;
        // Someone near their place who gets no closer for this long is circling it (avoidance with the people around,
        // or a place a hair off the agent's NavMesh). Within ArriveMetres they have already arrived; farther off they
        // are put on their place once nobody else stands on it, and keep walking until then. Stopping where they are
        // up to 0.9 m off (0.9.8) left them on the next place back, since places are about 0.8 m apart.
        private const float CircleSeconds = 1.5f;
        private const float CircleNearMetres = 1.5f;
        // Waiting longer than this for someone to clear their place, the person is taken out of view (QueueLine):
        // two customers each near the other's place would otherwise wait for good (review round 2).
        internal const float BlockedSeconds = 10f;
        private const float TurnDegreesPerSecond = 360f;

        private static bool expressionFailed;
        private static bool agentLogged;
        // Every customer on screen, for checking that a place is free before putting someone on it.
        private static readonly HashSet<QueuePerson> live = new HashSet<QueuePerson>();

        private ThirdPersonCharacter? character;
        private Vector3 target;
        private Vector3 lookAt;
        private bool walking;
        private bool hasTarget;
        private bool leavingNow;
        // The driving lanes every walk is checked against (null only for a person made outside a line).
        private TrafficLanes? lanes;
        private float walkStarted;
        private float standingSince;
        // The closest this walk has come to its target, and when it last got closer.
        private float bestDistance;
        private float closerSince;

        // Walks started since the person appeared (each new target, re-paths included).
        public int Walks { get; private set; }
        // Set when a walk ended by circling: how far from the place they stopped, and whether they were put there.
        public (float Miss, bool Warped)? Circled { get; set; }

        // Existing hatch/stop probes use the player mesh. Keep those readings unchanged.
        public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = PlayerAgentTypeId, areaMask = NavMesh.AllAreas };
        // 1.0: PedestrianBuildingPositionProvider.TryGetSampleTarget samples NavMeshHelper.NpcNavMeshFilter;
        // Extensions.NavMeshHelper (HGExtensions) defines it as agent 0, all areas. The player uses 1479372276.
        // These are different baked meshes, not different area names. Use the game's pedestrian mesh for the line
        // AND its agents; a player agent can walk straight across roads even when its targets are on pavement.
        public static NavMeshQueryFilter PedestrianFilter => Extensions.NavMeshHelper.NpcNavMeshFilter;
        // The walking pace of the first customer's NavMesh agent; null until one exists.
        public static float? WalkMetresPerSecond { get; private set; }

        public bool Arrived => hasTarget && !walking;
        // A walk failed the pedestrian-ground/path checks: the line takes this customer out of view.
        public bool UnsafeWalk { get; private set; }
        public string UnsafeWalkTest { get; private set; } = "NPC NavMesh (agent 0)";
        // Waited BlockedSeconds for their place to be free: the line takes them out of view.
        public bool Blocked { get; private set; }
        // When they started waiting for their place to be free, or negative.
        private float blockedSince = -1f;
        // Where the character stands; the owner object itself stays at the world origin.
        public Vector3 Position => character != null ? character.transform.position : transform.position;
        // How long they have stood at their current place.
        public float SecondsStanding => walking || !hasTarget ? 0f : Time.time - standingSince;

        public static QueuePerson? Create(Transform parent, long id, Vector3 start, TrafficLanes? lanes = null)
        {
            var owner = new GameObject("FoodTrucks.Customer." + id);
            owner.transform.SetParent(parent, false);
            var person = owner.AddComponent<QueuePerson>();
            person.lanes = lanes;
            live.Add(person);
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
            character.navmeshAgent.agentTypeID = PedestrianFilter.agentTypeID;
            character.navmeshAgent.areaMask = PedestrianFilter.areaMask;
            character.gameObject.SetActive(true);
            character.isPlayer = false;
            character.appearanceSetter.SetRandomAppearance();
            var agent = character.navmeshAgent;
            if (!NavMesh.SamplePosition(start, out var hit, 0.2f, PedestrianFilter) ||
                (lanes != null && QueueLine.OnStreet(hit.position, lanes) != null) || !agent.Warp(hit.position))
            {
                Log.Info($"Queue view: customer start rejected by NPC NavMesh / pavement check near {start}.");
                return false;
            }
            character.SetWalkingSpeed(ThirdPersonCharacter.WalkingSpeed.Walk);
            var prefabStopping = agent.stoppingDistance;
            agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, StoppingMetres);
            agent.autoBraking = true;
            // Keep the checked path. A new obstacle makes the view retry/drop the walk rather than silently
            // re-pathing through a crossing that was not checked.
            agent.autoRepath = false;
            if (!agentLogged)
            {
                agentLogged = true;
                Log.Info($"Queue view: customer agent speed {agent.speed:F2} m/s, stopping distance {prefabStopping:F2} m (now {agent.stoppingDistance:F2}), radius {agent.radius:F2} m, " +
                         $"acceleration {agent.acceleration:F1}, angular speed {agent.angularSpeed:F0}, avoidance {agent.obstacleAvoidanceType}; arrives within {ArriveMetres:F2} m; " +
                         $"NavMesh agent {agent.agentTypeID}, area mask {agent.areaMask}.");
                if (agent.speed > 0.1f) WalkMetresPerSecond = agent.speed;
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

        // The game's good or bad service emoji as they pay, when the server's skill calls for one.
        public void ShowMood(ServiceMood mood)
        {
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
            if (character == null || UnsafeWalk || Blocked) return true;
            var agent = character.navmeshAgent;
            if (agent == null || !agent.isActiveAndEnabled) return true;
            if (walking)
            {
                var distance = FlatDistance(character.transform.position, target);
                if (distance < bestDistance - 0.05f)
                {
                    bestDistance = distance;
                    closerSince = Time.time;
                }
                var near = distance <= ArriveMetres;
                var reached = near || (agent.isOnNavMesh && !agent.pathPending && agent.hasPath &&
                                       agent.pathStatus == NavMeshPathStatus.PathComplete &&
                                       agent.remainingDistance <= agent.stoppingDistance + 0.1f && distance <= 1f);
                // Close to the target and no closer for a while: circling it (avoidance, or a target the agent can't
                // stand on exactly). This is what showed as people spinning on the pavement (0.9.7 playtest).
                // A walk round an obstacle can stay this close in a straight line, so the path left must be short too.
                var circling = distance <= CircleNearMetres && Time.time - closerSince > CircleSeconds &&
                               (!agent.hasPath || agent.remainingDistance <= CircleNearMetres + 0.5f);
                var stuck = Time.time - walkStarted > (leavingNow ? LeaveSeconds : StuckSeconds);
                if (leavingNow) return reached || stuck || circling;
                if (!((circling || stuck) && !reached)) blockedSince = -1f;
                if (reached) Stop();
                else if ((circling || stuck) && !PlaceFree(agent.radius))
                {
                    // Someone else still stands there (usually the person before them leaving it): keep walking, and try
                    // again next frame, for at most BlockedSeconds.
                    if (blockedSince < 0f) blockedSince = Time.time;
                    else if (Time.time - blockedSince > BlockedSeconds)
                    {
                        Blocked = true;
                        return true;
                    }
                    return false;
                }
                else if (circling)
                {
                    // Put on their place; a place the agent can't be put on (off its NavMesh) leaves them where they are.
                    var warped = agent.Warp(target);
                    Circled = (distance, warped);
                    Stop();
                }
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
            walkStarted = Time.time;
            closerSince = Time.time;
            blockedSince = -1f;
            bestDistance = FlatDistance(character.transform.position, to);
            Walks++;
            if (!agent.isOnNavMesh)
            {
                UnsafeWalk = true;
                return;
            }
            agent.isStopped = false;
            agent.updateRotation = true;
            var path = new NavMeshPath();
            if (agent.CalculatePath(to, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                // Check the NPC agent's actual path, arriving, moving up and leaving alike.
                if (lanes != null && QueueLine.CrossesRoad(path.corners, lanes, out var test))
                {
                    agent.ResetPath();
                    UnsafeWalkTest = test;
                    UnsafeWalk = true;
                    return;
                }
                if (!agent.SetPath(path))
                {
                    UnsafeWalkTest = "NPC agent SetPath";
                    UnsafeWalk = true;
                }
            }
            else
            {
                agent.ResetPath();
                UnsafeWalkTest = "complete NPC agent path";
                UnsafeWalk = true;
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
            // Standing, only Tick turns them (towards the person ahead or the window); the agent doesn't. This holds
            // because isStopped is set above: the game's ThirdPersonCharacter.HandleNpcMovement sets updateRotation back
            // to true every frame unless the agent is stopped (off the NavMesh it skips that path altogether). Keep
            // isStopped = true here, or standing customers turn with the agent again.
            agent.updateRotation = false;
            character.LookTarget = Vector3.zero;
            character.Move(Vector3.zero);
        }

        // 0.9.8 threw a NullReferenceException inside the game's ShowExpression for every customer who gave up (23 in
        // the 11 October 2026 log, from Leave): an NPC's emoji runs on the character's serialized
        // npcExpressionsSingleCoroutineStarterStopper, which the HumanDefinitionLow prefab leaves empty (the game's own
        // NPCs that show emojis get theirs elsewhere). The exception is thrown inside the coroutine, where Unity logs it,
        // so the catch below never saw it. The stopper is now added and set before the first emoji; without it, none.
        private static readonly AccessTools.FieldRef<ThirdPersonCharacter, SingleCoroutineStarterStopper>? stopperField = StopperField();
        private static bool stopperLogged;

        private static AccessTools.FieldRef<ThirdPersonCharacter, SingleCoroutineStarterStopper>? StopperField()
        {
            try { return AccessTools.FieldRefAccess<ThirdPersonCharacter, SingleCoroutineStarterStopper>("npcExpressionsSingleCoroutineStarterStopper"); }
            catch (Exception ex)
            {
                Log.Error("Queue view: the characters' emoji runner field isn't there (" + ex.Message + "); no customer emojis.");
                return null;
            }
        }

        private bool ReadyToExpress()
        {
            if (character == null || character.head == null || stopperField == null) return false;
            if (stopperField(character) != null) return true;
            var stopper = character.gameObject.GetComponent<SingleCoroutineStarterStopper>() ?? character.gameObject.AddComponent<SingleCoroutineStarterStopper>();
            stopperField(character) = stopper;
            if (!stopperLogged)
            {
                stopperLogged = true;
                Log.Info("Queue view: customers' emoji runner added (the character prefab has none).");
            }
            return stopperField(character) != null;
        }

        private void Express(CharacterEmojiName emoji)
        {
            if (character == null || expressionFailed || !character.gameObject.activeInHierarchy) return;
            try
            {
                if (!ReadyToExpress())
                {
                    expressionFailed = true;
                    Log.Error("Queue view: customer expressions unavailable: the character has no emoji runner or head.");
                    return;
                }
                StartCoroutine(character.ShowExpression(emoji, 2f));
            }
            catch (Exception ex)
            {
                // Once is enough: the queue works without them.
                expressionFailed = true;
                Log.Error("Queue view: customer expressions unavailable: " + ex.Message);
            }
        }

        // Nobody else's body overlaps someone standing on the target.
        private bool PlaceFree(float radius)
        {
            foreach (var other in live)
            {
                if (other == this || other == null || other.character == null) continue;
                if (FlatDistance(other.character.transform.position, target) < radius * 2f) return false;
            }
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private void OnDestroy()
        {
            live.Remove(this);
            character = null;
        }
    }
}
