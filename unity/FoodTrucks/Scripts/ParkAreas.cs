#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using PlayerActivity;
using UnityEngine;

namespace FoodTrucks
{
    internal static class ParkAreas
    {
        public const string Park = "parkarea";
        public const string SkatePark = "skatepark";
        private const string WalkingInThePark = "ba:happinessmodifier_walking_in_the_park";
        private const string InTheSkatePark = "ba:happinessmodifier_in_the_skate_park";

        private static List<(string Type, string Name, Collider[] Colliders)>? areas;
        private static List<(string Name, string? Type, string Reason)> skipped = new List<(string, string?, string)>();

        public static void Reset() => areas = null;

        internal static (List<(string Type, string Name, Collider[] Colliders)> Kept, List<(string Name, string? Type, string Reason)> Skipped) ForTesting() =>
            (Areas(), skipped);

        public static IEnumerable<(string Type, double Distance)> Near(Vector3 position, double radius)
        {
            foreach (var (type, _, colliders) in Areas())
            {
                var distance = DistanceTo(colliders, position);
                if (distance <= radius) yield return (type, distance);
            }
        }

        internal static double DistanceTo(Collider[] colliders, Vector3 position)
        {
            var distance = double.MaxValue;
            foreach (var collider in colliders)
            {
                if (!Usable(collider)) continue;
                distance = Math.Min(distance, GroundDistance(collider, position));
            }
            return distance;
        }

        internal static bool Usable(Collider? collider) =>
            collider != null && collider.enabled && collider.isTrigger && collider.gameObject.activeInHierarchy;

        private static double GroundDistance(Collider collider, Vector3 position)
        {
            var concave = collider is MeshCollider mesh && !mesh.convex;
            var closest = concave ? collider.bounds.ClosestPoint(position) : collider.ClosestPoint(position);
            var dx = closest.x - position.x;
            var dz = closest.z - position.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static List<(string Type, string Name, Collider[] Colliders)> Areas()
        {
            if (areas != null) return areas;
            var list = new List<(string, string, Collider[])>();
            var left = new List<(string, string?, string)>();
            int parks = 0, skateParks = 0, inactive = 0, noCollider = 0, concave = 0, failed = 0;
            LocationHappinessTrigger[] triggers;
            try { triggers = UnityEngine.Object.FindObjectsOfType<LocationHappinessTrigger>(true); }
            catch (Exception ex)
            {
                Log.Error($"Parks: finding the game's park areas failed, so parks come only from zones.json: {ex.Message}");
                triggers = Array.Empty<LocationHappinessTrigger>();
            }
            foreach (var trigger in triggers)
            {
                try
                {
                    if (trigger == null) continue;
                    var config = Traverse.Create(trigger).Field("balanceConfig").GetValue<PlayerActivityBalanceConfig>();
                    var kind = config == null ? null : config.TemporalType;
                    string? type = kind == WalkingInThePark ? Park : kind == InTheSkatePark ? SkatePark : null;
                    if (type == null) continue;
                    var name = trigger.gameObject.name;
                    if (!trigger.gameObject.activeInHierarchy) { inactive++; left.Add((name, type, "inactive")); continue; }
                    var colliders = trigger.GetComponentsInChildren<Collider>(false).Where(Usable).ToArray();
                    if (colliders.Length == 0) { noCollider++; left.Add((name, type, "no enabled trigger collider")); continue; }
                    concave += colliders.Count(c => c is MeshCollider mesh && !mesh.convex);
                    list.Add((type, name, colliders));
                    if (type == Park) parks++; else skateParks++;
                }
                catch (Exception ex)
                {
                    failed++;
                    left.Add((SafeName(trigger), null, "failed: " + ex.Message));
                    Log.Error($"Parks: reading a park area failed, so it is left out: {ex.Message}");
                }
            }
            if (list.Count == 0 && InstanceBehavior<CityManager>.Instance == null) return list;
            areas = list;
            skipped = left;
            Log.Info($"Parks: {parks} park areas and {skateParks} skate park areas found (the game's walking-in-the-park triggers); " +
                     $"skipped {inactive} inactive, {noCollider} without an enabled trigger collider, {failed} that failed; {concave} concave mesh colliders measured by their bounds" +
                     (list.Count == 0 ? "; parks come only from zones.json." : "."));
            return areas;
        }

        private static string SafeName(LocationHappinessTrigger? trigger)
        {
            try { return trigger == null ? "(destroyed)" : trigger.gameObject.name; }
            catch (Exception) { return "(unknown)"; }
        }
    }
}
