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
    internal sealed class CartQueueView : MonoBehaviour
    {
        private const float ViewMetres = 100f;
        private const float KeepMetres = 110f;
        private const int MaxPeople = 16;
        private const float CheckSeconds = 0.25f;

        private static CartQueueView? instance;
        private readonly Dictionary<string, QueueLine> lines = new Dictionary<string, QueueLine>();
        private readonly Dictionary<string, (ServiceState Service, int Side)> lastSides = new Dictionary<string, (ServiceState, int)>();
        private readonly List<QueueLine> departing = new List<QueueLine>();
        private float nextCheck;
        private int spawnedTotal;
        private int mostAtOnce;

        public static bool Suspended { get; set; }

        private void OnEnable()
        {
            instance = this;
            Suspended = false;
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

        public static bool Showing(string vehicleId) =>
            instance != null && instance.enabled && instance.lines.TryGetValue(vehicleId, out var line) && line.HasPlaces;

        public static int? QueueSide(string vehicleId)
        {
            var self = instance;
            if (self == null) return null;
            if (self.lines.TryGetValue(vehicleId, out var line) && line.Side != 0) return line.Side;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            return vehicle?.IsOpen == true && self.lastSides.TryGetValue(vehicleId, out var last) &&
                ReferenceEquals(last.Service, vehicle.Service) ? last.Side : (int?)null;
        }

        internal static (int Side, Vector3? Window)? PrepareServing(VehicleController controller)
        {
            var self = instance;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, controller.vehicleInstance.id);
            if (self == null || !self.enabled || Suspended || vehicle?.IsOpen != true || vehicle.Service == null ||
                DemandData.Rules == null) return null;
            var line = self.EnsureLine(vehicle, controller);
            var side = QueueSide(vehicle.VehicleId);
            return side.HasValue ? (side.Value, line.WindowPlace) : ((int, Vector3?)?)null;
        }

        public static IReadOnlyList<Vector3>? PlacesOf(string vehicleId) =>
            instance != null && instance.lines.TryGetValue(vehicleId, out var line) && line.HasPlaces ? line.Places : null;

        public static void Closed(string vehicleId) => instance?.Retire(vehicleId);

        public static void Recheck()
        {
            if (instance != null) instance.nextCheck = 0f;
        }

        public static void ClearAll() => instance?.RemoveAll("cleared");

        private void Retire(string vehicleId)
        {
            lastSides.Remove(vehicleId);
            WindowCrew.Remove(vehicleId);
            if (!lines.TryGetValue(vehicleId, out var line)) return;
            lines.Remove(vehicleId);
            line.Close();
            departing.Add(line);
        }

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
                else if (!ReferenceEquals(line.Service, vehicle.Service))
                {
                    Retire(line.VehicleId);
                }
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

            var now = HireCrew.Now();
            foreach (var line in lines.Values)
            {
                var vehicle = state.Vehicles.FirstOrDefault(v => v.VehicleId == line.VehicleId);
                var controller = ControllerOf(game, line.VehicleId);
                if (vehicle == null || controller == null) continue;
                var kind = FoodTrucksMod.KindOf(controller.vehicleInstance?.vehicleTypeName) ?? "cart";
                WindowCrew.Sync(line.VehicleId, controller.transform, kind, QueueSide(line.VehicleId) ?? 1, HireCrew.OnShift(vehicle, now), CartSelling.ServingNow(vehicle, kind));
            }
        }

        private QueueLine EnsureLine(VehicleState vehicle, VehicleController controller)
        {
            if (lines.TryGetValue(vehicle.VehicleId, out var existing))
            {
                if (ReferenceEquals(existing.Service, vehicle.Service))
                {
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
            if (line.Side != 0) lastSides[id] = (vehicle.Service!, line.Side);
            VehicleDisplays.QueueChanged();
            return line;
        }

        private static VehicleController? ControllerOf(GameInstance game, string vehicleId)
        {
            var cart = game.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            return cart == null ? null : VehicleHelper.GetVehicleController(cart);
        }
    }

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
        public float Reach { get; }
        public float Along { get; }
        public bool Outward { get; }
        public int[] Sides { get; }
        public float Spacing { get; }
        public bool PreferBack { get; }
        public float Tolerance { get; }
        public bool Strict { get; }
        public const float MinSeparation = 0.9f;
        public const float TruckHalfWidth = 1.10f;

        private static readonly WindowShape Cart = new WindowShape((float)CartStance.WindowOut, (float)CartStance.WindowOut, (float)CartStance.CounterAlong, new[] { 1, -1 }, spacing: 0.8f, preferBack: false,
            tolerance: 0.45f, strict: false, outward: true);
        private static readonly WindowShape Truck = new WindowShape(2.00f, 3.40f, -1.10f, new[] { 1 }, spacing: 1.0f, preferBack: true,
            tolerance: 0.2f, strict: true, outward: false);

        public static WindowShape For(string kind) => kind == "truck" ? Truck : Cart;
    }

    internal sealed class QueueLineRoot : MonoBehaviour
    {
    }

    internal sealed class QueueLine
    {
        private const float PickUpSeconds = 1f;
        private const float SpawnGapSeconds = 0.6f;
        private const int MaxShownServed = 5;
        private const int MaxShownApproaching = 4;
        private const float DefaultWalkMetresPerSecond = 1.4f;
        private const int ExtraPlaces = 3;
        private const float ClosingSeconds = 40f;
        private const int MaxSpawnFailures = 3;
        private const float SpawnRetrySeconds = 15f;

        private readonly List<Vector3> places;
        private readonly Vector3 windowLook;
        private readonly List<Vector3> ends;
        private readonly TrafficLanes? lanes;
        private readonly HashSet<long> unseen = new HashSet<long>();
        private readonly GameObject root;
        private readonly Dictionary<long, QueuePerson> people = new Dictionary<long, QueuePerson>();
        private readonly List<QueuePerson> leaving = new List<QueuePerson>();
        private readonly List<long> served = new List<long>();
        private bool fresh = true;
        private float nextSpawn;
        private float closedAt;
        private int spawnFailures;
        private float blockedUntil;

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
        public string Kind => kind ??= KindOf(VehicleId);
        private string? kind;

        private static string KindOf(string vehicleId) =>
            FoodTrucksMod.KindOf(SaveGameManager.Current?.VehicleInstances?.FirstOrDefault(v => v != null && v.id == vehicleId)?.vehicleTypeName) ?? "vehicle";
        public ServiceState Service { get; }
        public ServiceMood Mood { get; set; } = ServiceMood.Neutral;
        public Dictionary<long, CustomerExit> Exits { get; } = new Dictionary<long, CustomerExit>();
        public Action? OnSpawned { get; set; }
        public Action<long>? Paid { get; set; }
        public bool HasPlaces => places.Count > 0;
        public IReadOnlyList<Vector3> Places => places;
        public int Side { get; private set; }
        public Vector3? WindowPlace => places.Count > 0 ? places[0] : (Vector3?)null;
        public int Count => people.Count + leaving.Count;
        public bool Finished => Count == 0 || Time.time - closedAt > ClosingSeconds;

        public static QueueLine Create(string vehicleId, Transform cart, int queueLimit, ServiceState service, WindowShape shape)
        {
            var (places, look, ends, detail, side, lanes) = Layout(cart, queueLimit + 1 + ExtraPlaces, shape);
            var root = new GameObject("FoodTrucks.Queue." + vehicleId);
            root.AddComponent<QueueLineRoot>();
            Log.Info($"Queue view: line at {KindOf(vehicleId)} {vehicleId}: {detail}.");
            return new QueueLine(vehicleId, root, places, look, ends, service, lanes)
            {
                Side = side, laidAt = cart.position, laidRotation = cart.rotation, checksMoves = shape == WindowShape.For("cart"), laidAlong = shape == WindowShape.For("cart") ? CartStanceWorld.WorkAlong(cart) : 0,
            };
        }

        public bool MovedFrom(Transform cart) => checksMoves &&
            (Vector3.Distance(cart.position, laidAt) > MovedMetres || Quaternion.Angle(cart.rotation, laidRotation) > TurnedDegrees ||
             Math.Abs(CartStanceWorld.WorkAlong(cart) - laidAlong) > 0.01);

        private Vector3 laidAt;
        private Quaternion laidRotation;
        private bool checksMoves;
        private double laidAlong;

        private void ReleaseServed()
        {
            var ids = served.ToList();
            served.Clear();
            foreach (var id in ids) Release(id);
        }

        private void Release(long id) => Paid?.Invoke(id);

        public void Served(long id)
        {
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

        public int Tick(int room)
        {
            var spawned = 0;
            if (places.Count > 0)
            {
                var order = Order(out var lineCount);
                var inLine = new HashSet<long>(order);
                unseen.RemoveWhere(id => !inLine.Contains(id));
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
                        if (i >= lineCount && i - lineCount >= MaxShownApproaching) continue;
                        person = Spawn(id, i, i >= lineCount ? WalkSecondsLeft(id) : (float?)null);
                        if (person == null) continue;
                        spawned++;
                    }
                    person.GoTo(places[i], i == 0 ? windowLook : places[i - 1]);
                }
                fresh = false;
                FinishAtWindow(order);
            }

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

        private List<long> Order(out int lineCount)
        {
            var order = new List<long>();
            var shown = 0;
            foreach (var id in served.ToList())
            {
                if (!people.TryGetValue(id, out var person))
                {
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

        private float? WalkSecondsLeft(long id)
        {
            var customer = Service.Approaching.FirstOrDefault(c => c != null && c.Id == id);
            return customer == null ? (float?)null : Math.Max(0f, customer.JoinsAtMinute - Service.Minute);
        }

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

        private QueuePerson? Spawn(long id, int place, float? walkSeconds)
        {
            nextSpawn = Time.time + SpawnGapSeconds;
            Vector3 start;
            if (ends.Count == 0) start = places[place];
            else if (walkSeconds != null) start = EndFor(walkSeconds.Value, places[place]);
            else if (fresh) start = places[place];
            else start = ends[UnityEngine.Random.Range(0, ends.Count)];
            var person = QueuePerson.Create(root.transform, id, start, lanes);
            if (person == null)
            {
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

        private Vector3? AwayFrom(Vector3 position)
        {
            var away = position - windowLook;
            away.y = 0f;
            var wanted = position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 15f;
            return lanes == null ? null : WayIn(wanted, position, QueuePerson.PedestrianFilter, lanes, new NavMeshPath(), new Rejections());
        }

        private static (List<Vector3> Places, Vector3 Look, List<Vector3> Ends, string Detail, int Side, TrafficLanes Lanes) Layout(Transform cart, int count, WindowShape shape)
        {
            var forward = Direction(cart.forward);
            var right = Direction(cart.right);
            var workAlong = shape == WindowShape.For("cart") ? CartStanceWorld.WorkAlong(cart) : shape.Along;
            var center = cart.position + forward * (float)workAlong;
            var filter = QueuePerson.PedestrianFilter;
            var rejected = new Rejections();
            var lanes = TrafficLanes.Near(center, 30f);

            var sides = shape.Sides.Where(side =>
            {
                if (shape != WindowShape.For("cart") || CartGroundPlacement.Find(cart, side, 0.3f,
                    CartStance.Server(side, along: workAlong), filter, lanes, _ => true, out _, out _, out _)) return true;
                rejected.Add("no pavement behind counter");
                return false;
            }).ToArray();

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
                    var place = PlaceAt(wanted, shape, filter, lanes, rejected, window: k == 0);
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
            foreach (var side in sides)
            {
                for (var lineOut = shape.Sideways; lineOut <= shape.Reach + 0.01f; lineOut += 0.2f)
                    foreach (var direction in new[] { 1, -1 })
                        Try(side, direction, lineOut, straightOut: false);
            }
            if (shape.Outward)
                foreach (var side in sides)
                    Try(side, 1, shape.Sideways, straightOut: true);
            if (best.Count == 0) return (best, center, new List<Vector3>(), $"no walkable place beside the window at {center}; nobody shown{rejected}", 0, lanes);

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

        private static string Geometry(List<Vector3> places, Transform cart, Vector3 outward, WindowShape shape)
        {
            var outs = places.Select(p => Vector3.Dot(p - cart.position, outward)).ToList();
            var gaps = Enumerable.Range(1, Math.Max(0, places.Count - 1)).Select(i => FlatDistance(places[i - 1], places[i])).ToList();
            var text = $"{outs.Min():F2}…{outs.Max():F2} m out";
            if (shape.Strict) text += $" ({outs.Min() - WindowShape.TruckHalfWidth:F2} m clear of the side at the least)";
            return text + (gaps.Count == 0 ? "" : $", {gaps.Min():F2}…{gaps.Max():F2} m apart");
        }

        private sealed class Rejections
        {
            private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
            public void Add(string test) => counts[test] = counts.TryGetValue(test, out var count) ? count + 1 : 1;
            public override string ToString() => counts.Count == 0 ? "" :
                "; candidates rejected by " + string.Join(", ", counts.Select(p => $"{p.Key}: {p.Value}"));
        }

        private static Vector3? PlaceAt(Vector3 wanted, WindowShape shape, NavMeshQueryFilter filter, TrafficLanes lanes, Rejections rejected, bool window)
        {
            if (!NavMesh.SamplePosition(wanted, out var hit, 0.6f, filter))
            { rejected.Add("NPC NavMesh (agent 0)"); return null; }
            if (FlatDistance(hit.position, wanted) > (window && !shape.Strict ? 0.1f : shape.Tolerance))
            { rejected.Add("placement tolerance on NPC NavMesh"); return null; }
            if (OnStreet(hit.position, lanes) is { } test)
            { rejected.Add(test); return null; }
            return hit.position;
        }

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

        internal static string? OnStreet(Vector3 position, TrafficLanes lanes)
        {
            if (Legality.InParkingLane(position)) return "parking lane";
            if (Legality.NearCrosswalk(position, 0.5f)) return "Crosswalk trigger";
            if (Legality.RoadBelow(position)) return "Roads layer";
            if (lanes.Distance(position) <= HatchSide.RoadsideMetres) return "traffic lane / roadside strip";
            return null;
        }

        internal static bool CrossesRoad(Vector3[] corners, TrafficLanes lanes, out string test)
        {
            test = "empty NPC path";
            if (corners.Length == 0) return true;
            if (corners.Length == 1) return CrossesRoad(corners[0], corners[0], lanes, out test);
            for (var i = 1; i < corners.Length; i++)
                if (CrossesRoad(corners[i - 1], corners[i], lanes, out test)) return true;
            return false;
        }

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

    internal sealed class QueuePerson : MonoBehaviour
    {
        private const string CharacterPrefab = "Characters/HumanDefinitionLow";
        private const int PlayerAgentTypeId = 1479372276;
        private const float StuckSeconds = 20f;
        private const float LeaveSeconds = 30f;
        private const float ArriveMetres = 0.5f;
        private const float StoppingMetres = 0.3f;
        // Someone near their place who gets no closer for this long is circling it (avoidance with the people around,
        // or a place a hair off the agent's NavMesh). Within ArriveMetres they have already arrived; farther off they
        // are put on their place once nobody else stands on it, and keep walking until then. Stopping where they are
        // up to 0.9 m off (0.9.8) left them on the next place back, since places are about 0.8 m apart.
        private const float CircleSeconds = 1.5f;
        private const float CircleNearMetres = 1.5f;
        internal const float BlockedSeconds = 10f;
        private const float TurnDegreesPerSecond = 360f;

        private static bool expressionFailed;
        private static bool agentLogged;
        private static readonly HashSet<QueuePerson> live = new HashSet<QueuePerson>();

        private ThirdPersonCharacter? character;
        private Vector3 target;
        private Vector3 lookAt;
        private bool walking;
        private bool hasTarget;
        private bool leavingNow;
        private TrafficLanes? lanes;
        private float walkStarted;
        private float standingSince;
        private float bestDistance;
        private float closerSince;

        public int Walks { get; private set; }
        public (float Miss, bool Warped)? Circled { get; set; }

        public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = PlayerAgentTypeId, areaMask = NavMesh.AllAreas };
        public static NavMeshQueryFilter PedestrianFilter => Extensions.NavMeshHelper.NpcNavMeshFilter;
        public static float? WalkMetresPerSecond { get; private set; }

        public bool Arrived => hasTarget && !walking;
        public bool UnsafeWalk { get; private set; }
        public string UnsafeWalkTest { get; private set; } = "NPC NavMesh (agent 0)";
        public bool Blocked { get; private set; }
        private float blockedSince = -1f;
        public Vector3 Position => character != null ? character.transform.position : transform.position;
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

        public void GoTo(Vector3 place, Vector3 look)
        {
            if (leavingNow || character == null) return;
            lookAt = look;
            if (hasTarget && (place - target).sqrMagnitude < 0.01f) return;
            Walk(place);
        }

        public void ShowMood(ServiceMood mood)
        {
            if (mood == ServiceMood.Pleased) Express(CharacterEmojiName.CustomerGoodService);
            else if (mood == ServiceMood.Complaining) Express(CharacterEmojiName.CustomerBadService);
        }

        public void Leave(Vector3 to, CustomerExit? exit)
        {
            leavingNow = true;
            if (exit == CustomerExit.GaveUp) Express(CharacterEmojiName.CommonRedAngry);
            if (character != null) Walk(to);
        }

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
                    var warped = agent.Warp(target);
                    Circled = (distance, warped);
                    Stop();
                }
                else if (stuck)
                {
                    if (!agent.Warp(target)) return true;
                    Stop();
                }
                return false;
            }
            if (leavingNow) return true;
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
                expressionFailed = true;
                Log.Error("Queue view: customer expressions unavailable: " + ex.Message);
            }
        }

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
