#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
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
        // A cart's worker stands behind it, opposite the window place.
        private const float CartBehind = 0.85f;
        // Inside the truck, behind the hatch: right of the middle, on the truck's floor (a guess at its height),
        // spread along the window when there are several.
        private static readonly Vector3 TruckPlace = new Vector3(0.45f, 0.45f, -1.10f);
        private const float TruckSpacing = 0.6f;

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
        }

        private static readonly Dictionary<string, List<Worker>> workers = new Dictionary<string, List<Worker>>();
        private static bool failed;

        // Shows exactly these employees at the vehicle's window. windowPlace is where the first customer stands.
        public static void Sync(string vehicleId, Transform vehicle, string kind, Vector3? windowPlace, IReadOnlyList<EmployeeInstance> crew)
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
                for (var i = 0; i < shown.Count; i++) Stand(shown[i], vehicle, kind, windowPlace, i, shown.Count);
                if (shown.Count == 0) workers.Remove(vehicleId);
            }
            catch (Exception ex)
            {
                // Once is enough: the window works without them.
                failed = true;
                Log.Error("Window crew: " + ex + " Crews stay unseen until the next load.");
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

        private static void Stand(Worker worker, Transform vehicle, string kind, Vector3? windowPlace, int index, int count)
        {
            Vector3 position;
            Vector3 facing;
            if (kind == "truck")
            {
                var local = TruckPlace + new Vector3(0f, 0f, (index - (count - 1) / 2f) * TruckSpacing);
                position = vehicle.TransformPoint(local);
                facing = vehicle.right;
            }
            else
            {
                var outward = windowPlace.HasValue ? windowPlace.Value - vehicle.position : vehicle.right;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : vehicle.right;
                position = vehicle.position - outward * CartBehind;
                facing = outward;
            }
            facing.y = 0f;
            worker.Character.ForceToPosition(position);
            if (facing.sqrMagnitude > 1e-4f) worker.Character.ForceToRotation(Quaternion.LookRotation(facing));
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
