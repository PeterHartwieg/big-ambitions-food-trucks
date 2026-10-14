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
    // The game's own parks, for the classifier's `park` area. The game marks each park with a trigger volume
    // (LocationHappinessTrigger) that gives the player the walking-in-the-park happiness boost while inside it, and
    // the skate park with its own [code, 1.0]. A spot near one gets a nearby place of building type parkarea or
    // skatepark (demand.json, classifier.buildingTypes) at the distance to the volume's edge on the ground, 0 inside.
    // Found once per city load, like the buildings. Parks without such a volume come from zones.json.
    internal static class ParkAreas
    {
        public const string Park = "parkarea";
        public const string SkatePark = "skatepark";
        private const string WalkingInThePark = "ba:happinessmodifier_walking_in_the_park";
        private const string InTheSkatePark = "ba:happinessmodifier_in_the_skate_park";

        private static List<(string Type, Collider[] Colliders)>? areas;

        public static void Reset() => areas = null;

        // Nearby parks within `radius` metres of a position, as (building type, distance on the ground).
        public static IEnumerable<(string Type, double Distance)> Near(Vector3 position, double radius)
        {
            foreach (var (type, colliders) in Areas())
            {
                var distance = double.MaxValue;
                foreach (var collider in colliders)
                {
                    // A disabled collider's ClosestPoint returns the position itself (distance 0 anywhere), so a collider
                    // switched off or deactivated since discovery doesn't count.
                    if (!Usable(collider)) continue;
                    distance = Math.Min(distance, GroundDistance(collider, position));
                }
                if (distance <= radius) yield return (type, distance);
            }
        }

        private static bool Usable(Collider? collider) =>
            collider != null && collider.enabled && collider.isTrigger && collider.gameObject.activeInHierarchy;

        // Metres on the ground from a position to a volume: the collider's closest point, height left out. Unity's
        // ClosestPoint works only for box, sphere, capsule and convex mesh colliders; for a concave mesh this falls back
        // to its world bounding box, which reads a spot inside the box's corners as inside the park (a shape the game's
        // park triggers aren't expected to have; the startup line counts them).
        private static double GroundDistance(Collider collider, Vector3 position)
        {
            var concave = collider is MeshCollider mesh && !mesh.convex;
            var closest = concave ? collider.bounds.ClosestPoint(position) : collider.ClosestPoint(position);
            var dx = closest.x - position.x;
            var dz = closest.z - position.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static List<(string Type, Collider[] Colliders)> Areas()
        {
            if (areas != null) return areas;
            var list = new List<(string, Collider[])>();
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
                // One broken trigger doesn't drop the others.
                try
                {
                    if (trigger == null) continue;
                    var config = Traverse.Create(trigger).Field("balanceConfig").GetValue<PlayerActivityBalanceConfig>();
                    var kind = config == null ? null : config.TemporalType;
                    string? type = kind == WalkingInThePark ? Park : kind == InTheSkatePark ? SkatePark : null;
                    if (type == null) continue;
                    if (!trigger.gameObject.activeInHierarchy) { inactive++; continue; }
                    // The trigger's own collider or its children's, active and enabled only.
                    var colliders = trigger.GetComponentsInChildren<Collider>(false).Where(Usable).ToArray();
                    if (colliders.Length == 0) { noCollider++; continue; }
                    concave += colliders.Count(c => c is MeshCollider mesh && !mesh.convex);
                    list.Add((type, colliders));
                    if (type == Park) parks++; else skateParks++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error($"Parks: reading a park area failed, so it is left out: {ex.Message}");
                }
            }
            // Before the city has loaded there is nothing to find; try again on the next read.
            if (list.Count == 0 && InstanceBehavior<CityManager>.Instance == null) return list;
            areas = list;
            Log.Info($"Parks: {parks} park areas and {skateParks} skate park areas found (the game's walking-in-the-park triggers); " +
                     $"skipped {inactive} inactive, {noCollider} without an enabled trigger collider, {failed} that failed; {concave} concave mesh colliders measured by their bounds" +
                     (list.Count == 0 ? "; parks come only from zones.json." : "."));
            return areas;
        }
    }
}
