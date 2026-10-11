#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // No parked cars in a commissary bay (Peter, 11 October 2026: a player driving back at night always finds room).
    // The game fills each parking lane with parked cars from its pools (ParkingLaneGenerator.GenerateParkedVehicles, on
    // load, every game hour for a business's lane and at the daily regeneration), only beyond 80 m of the player unless
    // forced [code, 1.0]. Each car is a child of its lane, on the ParkedVehicles layer, and goes back with
    // ParkingSimulator.ReleaseParkedVehicle, as the mod's carts have cleared them since 0.9.2 (CartParts). Here:
    // - a prefix on GenerateParkedVehicles notes the lane's cars, and the postfix releases only the cars that call
    //   added inside any commissary's bay zone (BayMarkers' zone: the painted stretches), for every commissary whether
    //   rented or not; a car already there (the game may skip a lane within 80 m of the player) is left to the sweep
    //   (review round 5);
    // - once the bay is resolved, and each time the player comes within NearMetres, the zone is swept for cars already
    //   standing there; a car in the main camera's view is left until it isn't, so none vanishes in view.
    // A car goes back as the game's own cleanup sends it: the lane's onReleaseVehicle first, then the pool.
    // Only the lanes' own parked cars: never a player vehicle (another layer), moving traffic (another layer) or the
    // delivery job's van (DeliveryJobStartController).
    internal static class BayParking
    {
        private const float NearMetres = 150f;
        private const float AwayMetres = 200f;
        // A car counts as in the zone when its middle is within the zone plus this (a car half in the bay is out too).
        private const float CarMarginMetres = 2.0f;
        private const float SummarySeconds = 60f;
        private static readonly Collider[] hits = new Collider[64];
        // Sites swept clean since the player last came near; cleared again when they go AwayMetres off.
        private static readonly HashSet<string> clean = new HashSet<string>(StringComparer.Ordinal);
        // Cars kept out per site since the last summary line: (at generation, on a sweep).
        private static readonly Dictionary<string, (int Generated, int Swept)> counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        private static float nextSummary;
        private static bool failed;

        public static void Reset()
        {
            clean.Clear();
            counts.Clear();
            failed = false;
        }

        // A bay was resolved again: sweep it on the next look.
        public static void Forget(string siteId) => clean.Remove(siteId);

        // Prefix on ParkingLaneGenerator.GenerateParkedVehicles: the lane's cars before it runs.
        public static void GeneratingPrefix(ParkingLaneGenerator __instance, out HashSet<GameObject>? __state)
        {
            __state = null;
            if (failed || __instance == null) return;
            try { __state = new HashSet<GameObject>(__instance.transform.Cast<Transform>().Select(t => t.gameObject)); }
            catch (Exception) { __state = null; }
        }

        // Postfix on ParkingLaneGenerator.GenerateParkedVehicles: the cars this call added inside a bay go back, unless
        // on screen (then the bay is swept again later).
        public static void GeneratedPostfix(ParkingLaneGenerator __instance, HashSet<GameObject>? __state)
        {
            if (failed || __instance == null || __state == null || Commissary.Uninstalled) return;
            try
            {
                var sites = DemandData.Commissaries;
                var rules = DemandData.Rules?.Commissary;
                if (sites == null || rules == null) return;
                var lane = __instance.transform.position;
                foreach (var site in sites.Sites)
                {
                    var bays = Commissary.ResolvedBays(site);
                    if (bays == null || bays.Count == 0) continue;
                    // A lane runs up to about a block; only lanes near the bay can reach into it.
                    if (!bays.Any(b => Flat(lane, b.X, b.Z) <= rules.BayRadiusMetres + 120f)) continue;
                    var zones = BayMarkers.ZonesOf(site, (float)rules.BayRadiusMetres);
                    if (zones.Count == 0) continue;
                    var released = 0;
                    foreach (Transform child in __instance.transform.Cast<Transform>().ToList())
                    {
                        if (__state.Contains(child.gameObject) || !IsLaneCar(child.gameObject)) continue;
                        if (!zones.Any(z => z.Contains(child.position, CarMarginMetres))) continue;
                        if (OnScreen(child.gameObject))
                        {
                            clean.Remove(site.Id);
                            continue;
                        }
                        if (Release(child.gameObject)) released++;
                    }
                    if (released > 0) Count(site.Id, released, 0);
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        // Twice a second from Commissary.Look: a bay the player comes near (or that was just resolved) is swept.
        public static void Update(CommissaryTables sites, double radius, Vector3 player, bool inside)
        {
            if (failed) return;
            try
            {
                foreach (var site in sites.Sites)
                {
                    var bays = Commissary.ResolvedBays(site);
                    if (bays == null || bays.Count == 0) continue;
                    var distance = bays.Min(b => Flat(player, b.X, b.Z));
                    if (distance > AwayMetres) clean.Remove(site.Id);
                    if (inside || distance > NearMetres || clean.Contains(site.Id)) continue;
                    var zones = BayMarkers.ZonesOf(site, (float)radius);
                    var (swept, left) = Sweep(zones);
                    if (swept > 0) Count(site.Id, 0, swept);
                    // Cars on screen are tried again on the next look.
                    if (left == 0) clean.Add(site.Id);
                }
                if (Time.unscaledTime >= nextSummary && counts.Count > 0)
                {
                    nextSummary = Time.unscaledTime + SummarySeconds;
                    foreach (var pair in counts)
                        Log.Info($"Commissary {pair.Key}: kept {pair.Value.Generated + pair.Value.Swept} parked cars out of the bay " +
                                 $"({pair.Value.Generated} as the lanes parked them, {pair.Value.Swept} already standing there).");
                    counts.Clear();
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        // Releases the lane cars standing in the zones and not on screen; returns how many went and how many are left.
        private static (int Swept, int Left) Sweep(List<BayMarkers.BayZone> zones)
        {
            var swept = 0;
            var left = 0;
            var seen = new HashSet<GameObject>();
            var mask = 1 << LayerHelper.ParkedVehiclesLayerIndex;
            foreach (var zone in zones)
            {
                foreach (var (from, to) in zone.Spans)
                {
                    var middle = zone.Foot + zone.Along * ((from + to) / 2f);
                    var half = new Vector3(zone.Width / 2f + CarMarginMetres, 4f, (to - from) / 2f + CarMarginMetres);
                    var count = Physics.OverlapBoxNonAlloc(middle, half, hits, Quaternion.LookRotation(zone.Along, Vector3.up), mask, QueryTriggerInteraction.Collide);
                    for (var i = 0; i < count; i++)
                    {
                        var car = CarOf(hits[i]);
                        if (car == null || !seen.Add(car) || !IsLaneCar(car)) continue;
                        if (!zone.Contains(car.transform.position, CarMarginMetres)) continue;
                        if (OnScreen(car))
                        {
                            left++;
                            continue;
                        }
                        if (Release(car)) swept++;
                    }
                }
            }
            return (swept, left);
        }

        // The parked car a collider belongs to: the lane's direct child above it.
        private static GameObject? CarOf(Collider? hit)
        {
            if (hit == null) return null;
            var t = hit.transform;
            while (t.parent != null && t.parent.GetComponent<ParkingLaneGenerator>() == null) t = t.parent;
            return t.parent != null ? t.gameObject : null;
        }

        // One of the lane's own parked cars: an active direct child of a parking lane, on the ParkedVehicles layer, and
        // not the delivery job's van.
        private static bool IsLaneCar(GameObject car) =>
            car.activeInHierarchy && car.layer == LayerHelper.ParkedVehiclesLayerIndex &&
            car.transform.parent != null && car.transform.parent.GetComponent<ParkingLaneGenerator>() != null &&
            car.GetComponent<Vehicles.DeliveryDriverJob.DeliveryJobStartController>() == null;

        // In the main camera's view (its frustum; review round 5: Renderer.isVisible is true for shadow passes too, so a
        // car near the player was never cleared). No main camera: nothing is in view.
        private static bool OnScreen(GameObject car)
        {
            var camera = GameManager.GetMainCamera();
            if (camera == null) return false;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return car.GetComponentsInChildren<Renderer>().Any(r => r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds));
        }

        private static bool Release(GameObject car)
        {
            try
            {
                // As CleanupParkedVehicles does: the lane is told first (a multi-floor garage drops the car's renderers
                // from its hiding lists), then the car goes back to its pool.
                car.transform.parent?.GetComponent<ParkingLaneGenerator>()?.onReleaseVehicle?.Invoke(car);
                ParkingSimulator.ReleaseParkedVehicle(car);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Commissary parking: {car.name} couldn't go back to its pool ({ex.Message}); left where it is.");
                return false;
            }
        }

        private static void Count(string siteId, int generated, int swept)
        {
            counts.TryGetValue(siteId, out var c);
            counts[siteId] = (c.Generated + generated, c.Swept + swept);
        }

        private static float Flat(Vector3 a, double x, double z) => new Vector2(a.x - (float)x, a.z - (float)z).magnitude;
    }
}
