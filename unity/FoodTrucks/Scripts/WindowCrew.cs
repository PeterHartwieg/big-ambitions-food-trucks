#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using FoodTrucks.Core.Fleet;
using Helpers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The crew at the window, shown while the queue view shows the vehicle's line: a standing game character
    // per worker, dressed as that employee, the way the game puts a worker behind a station
    // (EmployeeStationController: the same prefab, ForceToPosition, SetAppearance). They stand still and face
    // the customers; the game has no serving animation outside a building. [code, unverified in game]
    internal static class WindowCrew
    {
        private const string CharacterPrefab = "Characters/HumanDefinitionLow";
        // A cart's worker stands behind it straight across from the register, facing the customers (Core CartStance).
        // Inside the truck, behind the hatch: in the aisle, just inside the serving counter (x 0.78) with the
        // cooking line behind them (its counter's front edge at x -0.40), on the compartment's floor (y 0.94,
        // measured on Truck_Body), spread along the window when there are several. The window (y 1.80 to 2.82)
        // shows a standing character from the chest up. The player at the hatch (TruckWindow) stands here too,
        // in the middle place, and the crew move aside (Core WindowPlaces).
        private static readonly Vector3 TruckPlace = new Vector3(0.59f, 0.94f, -1.10f);
        private const float TruckSpacing = 0.6f;
        // With the player in the middle the crew stand closer, so two fit beside them.
        private const float TruckSpacingWithPlayer = 0.45f;
        // Every place stays inside the hatch's opening (z -2.00 to -0.20, art/README.md): its half-width of 0.90 m
        // less a quarter metre for a body at each end.
        private const double HatchHalfWidth = 0.65;
        // Crew who don't fit the opening wait further back in the aisle, past the hatch towards the rear doors
        // (beside the parked window panes and the fridge): one behind the other, the last place shared by any more.
        private static readonly Vector3 TruckIdle = new Vector3(0.59f, 0.94f, -2.25f);
        private const float IdleSpacing = 0.45f;
        private const int IdlePlaces = 2;

        // Where each crew member stands along the hatch; null for one waiting inside.
        private static double?[] TruckPlaces(int crew, bool player) =>
            WindowPlaces.Offsets(crew, player, player ? TruckSpacingWithPlayer : TruckSpacing, HatchHalfWidth).Crew;

        // A place behind the truck's hatch, `along` metres towards the cab from the hatch's middle, facing out.
        internal static (Vector3 Position, Quaternion Rotation) TruckSpot(Transform vehicle, double along)
        {
            var position = vehicle.TransformPoint(TruckPlace + new Vector3(0f, 0f, (float)along));
            var facing = vehicle.right;
            facing.y = 0f;
            return (position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : vehicle.rotation);
        }

        // The player's place at a truck's hatch: the middle one, whoever else is there.
        internal static (Vector3 Position, Quaternion Rotation) PlayerSpot(Transform vehicle) =>
            TruckSpot(vehicle, WindowPlaces.Offsets(0, player: true, TruckSpacingWithPlayer, HatchHalfWidth).Player ?? 0);

        // A waiting place inside the truck, away from the hatch, facing it.
        private static (Vector3 Position, Quaternion Rotation) IdleSpot(Transform vehicle, int index)
        {
            var position = vehicle.TransformPoint(TruckIdle - new Vector3(0f, 0f, Math.Min(index, IdlePlaces - 1) * IdleSpacing));
            var facing = vehicle.right;
            facing.y = 0f;
            return (position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : vehicle.rotation);
        }

        private sealed class Worker
        {
            public Worker(string employeeId, GameObject owner, ThirdPersonCharacter character)
            {
                EmployeeId = employeeId;
                Owner = owner;
                Character = character;
            }

            public string EmployeeId { get; }
            public GameObject Owner { get; }
            public ThirdPersonCharacter Character { get; }
            public Vector3? CartPlace;
            public Vector3 CartWanted;
            public Vector3 CartOrigin;
            public Quaternion CartRotation;
        }

        private static readonly Dictionary<string, List<Worker>> workers = new Dictionary<string, List<Worker>>();
        private static bool failed;

        // Shows exactly these employees at the vehicle's window. side: the side of a cart its queue forms on (1 right,
        // -1 left). serving: those of them who serve (CrewPay.Team); they take the places at the window and the rest
        // wait (inside the truck, beside the cart). Null when all of them do.
        public static void Sync(string vehicleId, Transform vehicle, string kind, int side, IReadOnlyList<EmployeeInstance> crew,
            IReadOnlyCollection<EmployeeInstance>? serving = null)
        {
            if (failed) return;
            try
            {
                if (!workers.TryGetValue(vehicleId, out var shown)) workers[vehicleId] = shown = new List<Worker>();
                var wanted = new HashSet<string>(crew.Select(e => e.id));
                foreach (var worker in shown.Where(w => w.Owner == null || !wanted.Contains(w.EmployeeId)).ToList())
                {
                    if (worker.Owner != null) Object.Destroy(worker.Owner);
                    shown.Remove(worker);
                }
                foreach (var employee in crew.Where(e => shown.All(w => w.EmployeeId != e.id)))
                {
                    var worker = Spawn(vehicleId, employee);
                    if (worker != null) shown.Add(worker);
                }
                var servingIds = serving == null ? null : new HashSet<string>(serving.Select(e => e.id));
                bool Serves(Worker w) => servingIds == null || servingIds.Contains(w.EmployeeId);
                var order = shown.OrderBy(w => Serves(w) ? 0 : 1).ToList();
                var atWindow = order.Count(Serves);
                var places = TruckPlaces(atWindow, TruckWindow.PlayerAt(vehicleId));
                var playerAtCart = kind != "truck" && CartSelling.PlayerAtServingSpot(vehicleId);
                var occupied = new List<Vector3>();
                if (playerAtCart) occupied.Add(PlayerHelper.GetPosition());
                var lanes = kind == "truck" ? null : TrafficLanes.Near(vehicle.position, 30f);
                var idle = 0;
                for (var i = 0; i < order.Count; i++)
                {
                    var place = i < atWindow ? places[i] : null;
                    // A more skilled cart worker can still serve while the player is here (Core's team rule),
                    // but must stand beside them rather than share the player's middle place.
                    Stand(order[i], vehicle, kind, side, place, playerAtCart ? i : place.HasValue ? -1 : idle++,
                        i < atWindow && !playerAtCart, playerAtCart ? PlayerHelper.GetPosition() : (Vector3?)null,
                        lanes, occupied);
                }
                if (shown.Count == 0) workers.Remove(vehicleId);
            }
            catch (Exception ex)
            {
                // Once is enough: the window works without them.
                failed = true;
                Log.Error("Window crew: " + ex + " Crews stay unseen until a save is loaded again.");
                RemoveAll();
            }
        }

        public static void Remove(string vehicleId)
        {
            if (!workers.TryGetValue(vehicleId, out var shown)) return;
            foreach (var worker in shown)
                if (worker.Owner != null) Object.Destroy(worker.Owner);
            workers.Remove(vehicleId);
        }

        public static void RemoveAll()
        {
            foreach (var id in workers.Keys.ToList()) Remove(id);
        }

        // A new save starts with a clean slate, a failure included.
        public static void Reset()
        {
            RemoveAll();
            failed = false;
        }

        private static Worker? Spawn(string vehicleId, EmployeeInstance employee)
        {
            var owner = new GameObject("FoodTrucks.Crew." + vehicleId + "." + employee.id);
            var character = PrefabHelper.CreatePrefab<ThirdPersonCharacter>(CharacterPrefab, owner.transform);
            if (character == null)
            {
                Object.Destroy(owner);
                throw new InvalidOperationException($"Prefab {CharacterPrefab} not found.");
            }
            character.gameObject.SetActive(true);
            character.isPlayer = false;
            var data = employee.characterData;
            // An employee who never stood at a station has no looks yet; the same seed gives the same ones each time.
            if (data?.elements != null && data.elements.Count > 0) character.appearanceSetter.SetAppearance(data);
            else if (data != null) character.appearanceSetter.SetRandomAppearance(data.gender, data.ageInDays, Seed(employee.id));
            else character.appearanceSetter.SetRandomAppearance();
            Log.Info($"Window crew: {HireCrew.Name(employee)} at the {vehicleId} window.");
            return new Worker(employee.id, owner, character);
        }

        // along: the place at the hatch; null with idleIndex for one waiting inside the truck.
        private static void Stand(Worker worker, Transform vehicle, string kind, int side, double? along, int idleIndex, bool serves,
            Vector3? playerPlace, TrafficLanes? lanes, List<Vector3> occupied)
        {
            if (kind == "truck")
            {
                var spot = along.HasValue ? TruckSpot(vehicle, along.Value) : IdleSpot(vehicle, idleIndex);
                worker.Character.ForceToPosition(spot.Position);
                worker.Character.ForceToRotation(spot.Rotation);
                return;
            }
            // In the cart's own frame from its live transform, so a turned, moved or reloaded cart is never read through
            // a stale world-space queue place. One not serving (the player took the place) waits further along the
            // same side, away from the player.
            var workAlong = CartStanceWorld.WorkAlong(vehicle);
            var playerAlong = playerPlace.HasValue ? CartStanceWorld.Along(vehicle, playerPlace.Value) : (double?)null;
            var local = serves || idleIndex < 0 ? CartStance.Server(side, along: workAlong) : CartStance.Waiting(side, idleIndex, playerAlong, along: workAlong);
            var wanted = CartStanceWorld.Point(vehicle, local);
            var radius = worker.Character.navmeshAgent.radius;
            bool Clear(Vector3 p) => occupied.All(other => Vector3.Distance(p, other) >= radius + 0.3f);
            // Reuse a safe fallback while parked instead of running the bounded grid search four times a second.
            // Recheck ground/street and occupied places even when the cart and desired place have not changed.
            var cached = worker.CartPlace.HasValue &&
                (worker.CartWanted - wanted).sqrMagnitude < 0.0001f &&
                (worker.CartOrigin - vehicle.position).sqrMagnitude < 0.0001f &&
                Quaternion.Angle(worker.CartRotation, vehicle.rotation) < 0.01f &&
                Clear(worker.CartPlace.Value) && CartGroundPlacement.IsSafe(worker.CartPlace.Value, lanes!);
            var position = worker.CartPlace ?? wanted;
            var behind = Vector3.Dot(vehicle.position - position, CartStanceWorld.Facing(vehicle, side)) >= 0.5f + radius - 0.01f;
            if (!cached && !CartGroundPlacement.Find(vehicle, side, radius, local,
                Extensions.NavMeshHelper.NpcNavMeshFilter, lanes!, Clear, out position, out _, out behind))
            {
                worker.CartPlace = null;
                worker.Character.gameObject.SetActive(false);
                return; // Never show a crew member at an unchecked point when no safe fallback exists.
            }
            worker.CartPlace = position;
            worker.CartWanted = wanted;
            worker.CartOrigin = vehicle.position;
            worker.CartRotation = vehicle.rotation;
            worker.Character.gameObject.SetActive(true);
            occupied.Add(position);
            var facing = behind && CartStance.FacesAcross(CartStanceWorld.Along(vehicle, position), workAlong)
                ? CartStanceWorld.Facing(vehicle, side)
                : CartStanceWorld.Point(vehicle, CartStance.Window(side, CartStance.WindowOut, workAlong)) - position;
            facing.y = 0f;
            worker.Character.ForceToPosition(position);
            worker.Character.ForceToRotation(Quaternion.LookRotation(facing));
        }

        private static int Seed(string id)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in id) hash = hash * 31 + c;
                return hash;
            }
        }
    }
}
