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
    internal static class WindowCrew
    {
        private const string CharacterPrefab = "Characters/HumanDefinitionLow";
        private static readonly Vector3 TruckPlace = new Vector3(0.59f, 0.94f, -1.10f);
        private const float TruckSpacing = 0.6f;
        private const float TruckSpacingWithPlayer = 0.45f;
        private const double HatchHalfWidth = 0.65;
        private static readonly Vector3 TruckIdle = new Vector3(0.59f, 0.94f, -2.25f);
        private const float IdleSpacing = 0.45f;
        private const int IdlePlaces = 2;

        private static double?[] TruckPlaces(int crew, bool player) =>
            WindowPlaces.Offsets(crew, player, player ? TruckSpacingWithPlayer : TruckSpacing, HatchHalfWidth).Crew;

        internal static (Vector3 Position, Quaternion Rotation) TruckSpot(Transform vehicle, double along)
        {
            var position = vehicle.TransformPoint(TruckPlace + new Vector3(0f, 0f, (float)along));
            var facing = vehicle.right;
            facing.y = 0f;
            return (position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : vehicle.rotation);
        }

        internal static (Vector3 Position, Quaternion Rotation) PlayerSpot(Transform vehicle) =>
            TruckSpot(vehicle, WindowPlaces.Offsets(0, player: true, TruckSpacingWithPlayer, HatchHalfWidth).Player ?? 0);

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
                    Stand(order[i], vehicle, kind, side, place, playerAtCart ? i : place.HasValue ? -1 : idle++,
                        i < atWindow && !playerAtCart, playerAtCart ? PlayerHelper.GetPosition() : (Vector3?)null,
                        lanes, occupied);
                }
                if (shown.Count == 0) workers.Remove(vehicleId);
            }
            catch (Exception ex)
            {
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
            if (data?.elements != null && data.elements.Count > 0) character.appearanceSetter.SetAppearance(data);
            else if (data != null) character.appearanceSetter.SetRandomAppearance(data.gender, data.ageInDays, Seed(employee.id));
            else character.appearanceSetter.SetRandomAppearance();
            Log.Info($"Window crew: {HireCrew.Name(employee)} at the {vehicleId} window.");
            return new Worker(employee.id, owner, character);
        }

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
            var workAlong = CartStanceWorld.WorkAlong(vehicle);
            var playerAlong = playerPlace.HasValue ? CartStanceWorld.Along(vehicle, playerPlace.Value) : (double?)null;
            var local = serves || idleIndex < 0 ? CartStance.Server(side, along: workAlong) : CartStance.Waiting(side, idleIndex, playerAlong, along: workAlong);
            var wanted = CartStanceWorld.Point(vehicle, local);
            var radius = worker.Character.navmeshAgent.radius;
            bool Clear(Vector3 p) => occupied.All(other => Vector3.Distance(p, other) >= radius + 0.3f);
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
                return;
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
