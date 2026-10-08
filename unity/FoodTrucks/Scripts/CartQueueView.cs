#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Parking.UndergroundParking;
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
    // goes further than ViewMetres, enters a building, skips time, leaves the city or runs the uninstall
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
        private float nextCheck;
        private int spawnedTotal;
        private int mostAtOnce;

        private void OnEnable()
        {
            instance = this;
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

        // The cart closed: everyone in line walks off, then the line goes.
        public static void Closed(string vehicleId)
        {
            if (instance != null && instance.lines.TryGetValue(vehicleId, out var line)) line.Close();
        }

        // Removes every person at once: time skips, the uninstall step, leaving the city.
        public static void ClearAll() => instance?.RemoveAll("cleared");

        private void RemoveAll(string reason)
        {
            foreach (var line in lines.Values) line.Destroy(reason);
            lines.Clear();
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
                var room = MaxPeople - lines.Values.Sum(l => l.Count);
                foreach (var line in lines.Values.ToList())
                {
                    room -= line.Tick(Math.Max(0, room));
                    if (line.Finished)
                    {
                        line.Destroy("closed");
                        lines.Remove(line.VehicleId);
                    }
                }
                var now = lines.Values.Sum(l => l.Count);
                if (now > mostAtOnce) mostAtOnce = now;
            }
            catch (Exception ex)
            {
                Log.Error("Queue view: " + ex);
                RemoveAll("error");
                enabled = false;
            }
        }

        // Starts a line for each open cart near the player and removes the lines of carts out of range.
        private void CheckLines()
        {
            var state = FoodTrucksSession.CurrentState;
            var game = SaveGameManager.Current;
            if (state == null || game == null || BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                if (lines.Count > 0) RemoveAll("player indoors");
                return;
            }
            var player = PlayerHelper.GetPosition();
            foreach (var line in lines.Values.ToList())
            {
                var controller = ControllerOf(game, line.VehicleId);
                var service = state.Vehicles.FirstOrDefault(v => v.VehicleId == line.VehicleId)?.Service;
                if (controller == null || Vector3.Distance(player, controller.transform.position) > KeepMetres)
                {
                    line.Destroy("out of range");
                    lines.Remove(line.VehicleId);
                }
                else if (!line.Closing && (service == null || game.ActiveVehicleId == line.VehicleId)) line.Close();
                else if (!line.Closing) line.Service = service;
            }

            var rules = DemandData.Rules;
            if (rules == null) return;
            foreach (var vehicle in state.Vehicles)
            {
                if (!vehicle.IsOpen || vehicle.Service == null || lines.ContainsKey(vehicle.VehicleId)) continue;
                if (game.ActiveVehicleId == vehicle.VehicleId) continue;
                var controller = ControllerOf(game, vehicle.VehicleId);
                if (controller == null || Vector3.Distance(player, controller.transform.position) > ViewMetres) continue;
                var places = rules.Service.QueueLimit(CartDemand.Kind, rules.Queue.MaxQueue) + 1;
                var line = QueueLine.Create(vehicle.VehicleId, controller.transform, Math.Min(places, MaxPeople), vehicle.Service);
                line.OnSpawned = () => spawnedTotal++;
                lines[vehicle.VehicleId] = line;
            }
        }

        private static VehicleController? ControllerOf(GameInstance game, string vehicleId)
        {
            var cart = game.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            return cart == null ? null : VehicleHelper.GetVehicleController(cart);
        }
    }

    // Marks a line's root object, so a later runtime can find and remove what an earlier one left.
    internal sealed class QueueLineRoot : MonoBehaviour
    {
    }

    // One cart's line: the places along the pavement, the ways in and out, and the people.
    internal sealed class QueueLine
    {
        // Metres from the cart's middle to the window place, sideways, and between people in line.
        private const float WindowOffset = 1.05f;
        private const float Spacing = 0.8f;
        // How long a served customer stands at the window, in game-speed seconds; shorter while others wait to pay.
        private const float PaySeconds = 1.8f;
        private const float HurriedPaySeconds = 0.8f;
        private const float SpawnGapSeconds = 0.6f;
        // Served customers not yet shown are dropped beyond this, so the view never runs far behind Core.
        private const int MaxUnshownServed = 3;
        private const float ClosingSeconds = 40f;

        private readonly List<Vector3> places;
        private readonly Vector3 windowLook;
        private readonly List<Vector3> ends;
        private readonly GameObject root;
        private readonly Dictionary<long, QueuePerson> people = new Dictionary<long, QueuePerson>();
        private readonly List<QueuePerson> leaving = new List<QueuePerson>();
        // Customers Core has served who haven't left the window yet, in the order they were served.
        private readonly List<long> served = new List<long>();
        private bool fresh = true;
        private float nextSpawn;
        private float closedAt;

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
        public ServiceState Service { get; set; }
        public ServiceMood Mood { get; set; } = ServiceMood.Neutral;
        public Dictionary<long, CustomerExit> Exits { get; } = new Dictionary<long, CustomerExit>();
        public bool Closing { get; private set; }
        public Action? OnSpawned { get; set; }
        public int Count => people.Count + leaving.Count;
        public bool Finished => Closing && (Count == 0 || Time.time - closedAt > ClosingSeconds);

        public static QueueLine Create(string vehicleId, Transform cart, int count, ServiceState service)
        {
            var (places, look, ends, detail) = Layout(cart, count);
            var root = new GameObject("FoodTrucks.Queue." + vehicleId);
            root.AddComponent<QueueLineRoot>();
            Log.Info($"Queue view: line at cart {vehicleId}: {detail}.");
            return new QueueLine(vehicleId, root, places, look, ends, service);
        }

        public void Served(long id)
        {
            if (!served.Contains(id)) served.Add(id);
        }

        public void Close()
        {
            if (Closing) return;
            Closing = true;
            closedAt = Time.time;
            served.Clear();
            foreach (var person in people.Values) StartLeaving(person, null);
            people.Clear();
        }

        public void Destroy(string reason)
        {
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
            if (!Closing)
            {
                var order = Order();
                // Anyone no longer in line leaves: gave up, walked on, or the window closed on them.
                foreach (var id in people.Keys.Where(id => !order.Contains(id)).ToList())
                {
                    Exits.TryGetValue(id, out var exit);
                    StartLeaving(people[id], Exits.ContainsKey(id) ? exit : (CustomerExit?)null);
                    people.Remove(id);
                }
                for (var i = 0; i < order.Count; i++)
                {
                    var id = order[i];
                    if (!people.TryGetValue(id, out var person))
                    {
                        if (i >= places.Count || room - spawned <= 0) continue;
                        if (!fresh && Time.time < nextSpawn) continue;
                        person = Spawn(id, i);
                        if (person == null) continue;
                        spawned++;
                    }
                    person.Place = i;
                    if (i < places.Count) person.GoTo(places[i], i == 0 ? windowLook : places[i - 1]);
                }
                fresh = false;
                ServeAtWindow(order);
            }
            foreach (var person in people.Values) person.Tick();
            for (var i = leaving.Count - 1; i >= 0; i--)
            {
                var person = leaving[i];
                if (person == null || person.Tick())
                {
                    if (person != null) Object.Destroy(person.gameObject);
                    leaving.RemoveAt(i);
                }
            }
            foreach (var id in Exits.Keys.Where(id => !people.ContainsKey(id)).ToList()) Exits.Remove(id);
            return spawned;
        }

        // Served customers first (they reached the window before anyone still waiting), then the orders
        // being made, then the queue. Served customers nobody saw arrive are dropped beyond a few.
        private List<long> Order()
        {
            var order = new List<long>();
            var unshown = 0;
            foreach (var id in served.ToList())
            {
                if (!people.ContainsKey(id) && ++unshown > MaxUnshownServed)
                {
                    served.Remove(id);
                    continue;
                }
                order.Add(id);
            }
            foreach (var customer in Service.Window.Concat(Service.Queue))
            {
                if (customer != null && !order.Contains(customer.Id)) order.Add(customer.Id);
            }
            return order;
        }

        // The served customer at the window pays for a moment, then walks off.
        private void ServeAtWindow(List<long> order)
        {
            if (order.Count == 0 || !served.Contains(order[0]) || !people.TryGetValue(order[0], out var person)) return;
            if (!person.Arrived) return;
            if (!person.Paying)
            {
                person.Pay(served.Count > 2 ? HurriedPaySeconds : PaySeconds, Mood);
                return;
            }
            if (!person.DonePaying) return;
            served.Remove(order[0]);
            people.Remove(order[0]);
            StartLeaving(person, CustomerExit.Served);
        }

        private QueuePerson? Spawn(long id, int place)
        {
            nextSpawn = Time.time + SpawnGapSeconds;
            // A new line shows the people already waiting where they stand; later ones walk in.
            var start = fresh || ends.Count == 0 ? places[Math.Min(place, places.Count - 1)] : ends[UnityEngine.Random.Range(0, ends.Count)];
            var person = QueuePerson.Create(root.transform, id, start);
            if (person == null) return null;
            people[id] = person;
            OnSpawned?.Invoke();
            return person;
        }

        private void StartLeaving(QueuePerson person, CustomerExit? exit)
        {
            if (person == null) return;
            var target = ends.Count > 0
                ? ends[UnityEngine.Random.Range(0, ends.Count)]
                : person.transform.position + (person.transform.position - places[0]).normalized * 15f;
            person.Leave(target, exit, Mood);
            leaving.Add(person);
        }

        // The line runs from a window place beside the cart along its length, on whichever side and in
        // whichever direction keeps the most places on walkable pavement, out of parking lanes and off the road.
        private static (List<Vector3> Places, Vector3 Look, List<Vector3> Ends, string Detail) Layout(Transform cart, int count)
        {
            var center = cart.position;
            var forward = Flat(cart.forward);
            var right = Flat(cart.right);
            var filter = QueuePerson.Filter;

            List<Vector3>? best = null;
            var bestScore = int.MinValue;
            var bestSide = 1;
            var bestDirection = 1;
            foreach (var side in new[] { 1, -1 })
            {
                foreach (var direction in new[] { 1, -1 })
                {
                    var placed = new List<Vector3>();
                    var roads = 0;
                    for (var k = 0; k < count; k++)
                    {
                        var wanted = center + right * (side * WindowOffset) + forward * (direction * k * Spacing);
                        if (!NavMesh.SamplePosition(wanted, out var hit, 0.6f, filter) || Flat(hit.position - wanted).magnitude > 0.45f) break;
                        if (Legality.InParkingLane(hit.position)) break;
                        if (Legality.RoadBelow(hit.position)) roads++;
                        placed.Add(hit.position);
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
            var places = best ?? new List<Vector3>();
            if (places.Count == 0) places.Add(center + right * WindowOffset);

            // Ways in and out: points on the pavement a little way along in both directions, reachable from the window.
            var ends = new List<Vector3>();
            var path = new NavMeshPath();
            foreach (var along in new[] { 11f, -9f, 15f, -13f, 19f, -17f })
            {
                var wanted = center + forward * (bestDirection * along) + right * (bestSide * UnityEngine.Random.Range(0.6f, 2.4f));
                if (!NavMesh.SamplePosition(wanted, out var hit, 2.5f, filter)) continue;
                if (!NavMesh.CalculatePath(hit.position, places[0], filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                ends.Add(hit.position);
            }
            for (var attempt = 0; ends.Count < 2 && attempt < 8; attempt++)
            {
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var wanted = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * UnityEngine.Random.Range(9f, 16f);
                if (!NavMesh.SamplePosition(wanted, out var hit, 2.5f, filter)) continue;
                if (!NavMesh.CalculatePath(hit.position, places[0], filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                ends.Add(hit.position);
            }
            var detail = $"{places.Count} of {count} places on the {(bestSide > 0 ? "right" : "left")} side towards the {(bestDirection > 0 ? "front" : "back")}, " +
                         $"{ends.Count} ways in and out";
            return (places, center, ends, detail);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }

    // One customer: a game character walking on the NavMesh, as spike 5 showed (StandingNpc).
    internal sealed class QueuePerson : MonoBehaviour
    {
        private const string CharacterPrefab = "Characters/HumanDefinitionLow";
        private const int PlayerAgentTypeId = 1479372276;
        // Someone who can't reach their place in this time is put there; someone leaving is removed.
        private const float StuckSeconds = 20f;
        private const float LeaveSeconds = 30f;
        private const float TurnDegreesPerSecond = 360f;

        private static bool expressionFailed;

        private ThirdPersonCharacter? character;
        private Vector3 target;
        private Vector3 lookAt;
        private bool walking;
        private bool hasTarget;
        private bool leavingNow;
        private float walkStarted;
        private float payUntil;

        public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = PlayerAgentTypeId, areaMask = NavMesh.AllAreas };

        public long Id { get; private set; }
        public int Place { get; set; }
        public bool Arrived => hasTarget && !walking;
        public bool Paying { get; private set; }
        public bool DonePaying => Paying && Time.time >= payUntil;

        public static QueuePerson? Create(Transform parent, long id, Vector3 start)
        {
            var owner = new GameObject("FoodTrucks.Customer." + id);
            owner.transform.SetParent(parent, false);
            var person = owner.AddComponent<QueuePerson>();
            person.Id = id;
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

        public void Leave(Vector3 to, CustomerExit? exit, ServiceMood mood)
        {
            leavingNow = true;
            // Someone who waited in vain shows it as they go [the game's own emoji, unverified on a spawned character].
            if (exit == CustomerExit.GaveUp) Express(CharacterEmojiName.CommonRedAngry);
            if (character != null) Walk(to);
        }

        // One frame. Returns true when a leaving person is done and can be removed.
        public bool Tick()
        {
            if (character == null) return true;
            var agent = character.navmeshAgent;
            if (walking)
            {
                var reached = agent.isOnNavMesh && !agent.pathPending &&
                              (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.1f);
                var stuck = Time.time - walkStarted > (leavingNow ? LeaveSeconds : StuckSeconds);
                if (leavingNow) return reached || stuck;
                if (stuck && !reached) agent.Warp(target);
                if (reached || stuck) Stop();
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
            if (!agent.isOnNavMesh) return;
            agent.isStopped = false;
            var path = new NavMeshPath();
            if (agent.CalculatePath(to, path) && path.status != NavMeshPathStatus.PathInvalid) agent.SetPath(path);
            else agent.ResetPath();
        }

        private void Stop()
        {
            walking = false;
            if (character == null) return;
            var agent = character.navmeshAgent;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            agent.velocity = Vector3.zero;
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

        private void OnDestroy()
        {
            character = null;
        }
    }
}
