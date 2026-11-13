#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    internal static class BayParking
    {
        private const float NearMetres = 150f;
        private const float AwayMetres = 200f;
        private const float CarMarginMetres = 2.0f;
        private const float FootprintMarginMetres = 0.1f;
        private const float CutMetres = 50f;
        private static Vector3? lastPlayer;
        private const float SummarySeconds = 60f;
        private static readonly Collider[] hits = new Collider[64];
        private static readonly HashSet<string> clean = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> swept = new HashSet<string>(StringComparer.Ordinal);
        // Cars whose release threw (review round 4, Opus): logged once, then left alone by every sweep, counted neither
        // as kept out nor as left, so the bay doesn't stay unclean for good and its paint goes on round such a car.
        private static readonly HashSet<GameObject> unreleasable = new HashSet<GameObject>();
        private static readonly HashSet<string> waitLogged = new HashSet<string>(StringComparer.Ordinal);
        internal static bool UnderLoadingScreen;

        // Right before a bay's first markers are made (BayMarkers.Update; review round 3, Astra and Sol: a clearance
        // found earlier could be stale by the time the player came near): its zone is swept now, with the view rule
        // unless under the loading screen, and the markers are made only if no lane car is left in it. A car in view
        // holds the paint back until it is out of view and swept. Drawn paint never asks again, so it doesn't flicker.
        // After an error here nothing is kept out, so nothing waits either.
        // Per bay point (review round 4, Sol and Opus): a point without markers is checked before its own are made,
        // even when another point of the same commissary is drawn. Only this point's zone is known to be clear, so the
        // site isn't marked swept or clean here (review round 5, Astra and Sol: another point's car would be skipped).
        public static bool ClearBeforePaint(CommissarySite site, BayMarkers.BayZone? zone)
        {
            if (failed || Commissary.Uninstalled || zone == null) return true;
            try
            {
                var (gone, left) = Sweep(new List<BayMarkers.BayZone> { zone }, inView: !UnderLoadingScreen);
                if (gone > 0) Count(site.Id, 0, gone);
                if (left == 0)
                {
                    if (waitLogged.Remove(site.Id)) Log.Info($"Commissary {site.Id}: the bay is clear; its paint is drawn.");
                    return true;
                }
                if (waitLogged.Add(site.Id))
                    Log.Info($"Commissary {site.Id}: paint waits for {left} parked {(left == 1 ? "car" : "cars")} in view to leave the bay.");
                return false;
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
                return true;
            }
        }
        private static readonly Dictionary<string, (int Generated, int Swept)> counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        private static float nextSummary;
        private static bool failed;

        private const float BayPointSeconds = 1f;
        private const float LaneReachMetres = 120f;
        private static List<(double X, double Z)> bayPoints = new List<(double X, double Z)>();
        private static float bayPointsUntil = float.MinValue;

        private static bool NearAnyBay(Vector3 lane)
        {
            var now = Time.unscaledTime;
            if (now >= bayPointsUntil)
            {
                bayPointsUntil = now + BayPointSeconds;
                bayPoints = DemandData.Commissaries?.Sites.SelectMany(s => Commissary.ResolvedBays(s) ?? new List<CommissaryBayPoint>())
                    .Select(b => (b.X, b.Z)).ToList() ?? new List<(double X, double Z)>();
            }
            var reach = (DemandData.Rules?.Commissary?.BayRadiusMetres ?? 15) + LaneReachMetres;
            return bayPoints.Any(b => Flat(lane, b.X, b.Z) <= reach);
        }

        public static void Reset()
        {
            bayPointsUntil = float.MinValue;
            clean.Clear();
            swept.Clear();
            waitLogged.Clear();
            unreleasable.Clear();
            counts.Clear();
            lastPlayer = null;
            failed = false;
        }

        public static void Forget(string siteId)
        {
            clean.Remove(siteId);
            swept.Remove(siteId);
            waitLogged.Remove(siteId);
            bayPointsUntil = float.MinValue;
        }

        public static void SweepOnLoad(CommissaryTables sites, double radius)
        {
            if (failed || Commissary.Uninstalled) return;
            try
            {
                if (!Legality.ParkingSceneReady())
                {
                    Log.Info("Commissary bays: the parking lanes aren't ready on load; the bays from the save are swept on the first look instead (with the view rule, their paint waiting until they are clear).");
                    return;
                }
                foreach (var site in sites.Sites)
                {
                    var bays = Commissary.ResolvedBays(site);
                    if (bays == null || bays.Count == 0 || swept.Contains(site.Id)) continue;
                    var (gone, left) = Sweep(BayMarkers.ZonesOf(site, (float)radius), inView: false);
                    if (left == 0) swept.Add(site.Id);
                    Log.Info($"Commissary {site.Id}: bay swept on load, {gone} parked {(gone == 1 ? "car" : "cars")} kept out" + (left > 0 ? $", {left} left" : "") + ".");
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        public static void SweepNewBay(CommissarySite site, double radius)
        {
            Forget(site.Id);
            if (failed || Commissary.Uninstalled) return;
            try
            {
                var bays = Commissary.ResolvedBays(site);
                if (bays == null || bays.Count == 0) return;
                var cover = UnderLoadingScreen;
                var (gone, left) = Sweep(BayMarkers.ZonesOf(site, (float)radius), inView: !cover);
                if (left == 0) swept.Add(site.Id);
                Log.Info($"Commissary {site.Id}: new bay swept {(cover ? "under the loading screen" : "with the view rule")}, {gone} parked {(gone == 1 ? "car" : "cars")} kept out" +
                         (left > 0 ? $", {left} left for now" : "") + ".");
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        public static void WatchCut(CommissaryTables sites, double radius, Vector3 player, bool inside)
        {
            if (failed || Commissary.Uninstalled) return;
            if (inside)
            {
                lastPlayer = null;
                return;
            }
            var last = lastPlayer;
            lastPlayer = player;
            if (last == null || Flat(player, last.Value.x, last.Value.z) <= CutMetres) return;
            try
            {
                foreach (var site in sites.Sites)
                {
                    var bays = Commissary.ResolvedBays(site);
                    if (bays == null || bays.Count == 0 || bays.Min(b => Flat(player, b.X, b.Z)) > NearMetres) continue;
                    var (gone, left) = Sweep(BayMarkers.ZonesOf(site, (float)radius), inView: false);
                    if (left == 0)
                    {
                        swept.Add(site.Id);
                        clean.Add(site.Id);
                    }
                    else clean.Remove(site.Id);
                    if (gone > 0) Count(site.Id, 0, gone);
                    Log.Info($"Commissary {site.Id}: the player jumped {Flat(player, last.Value.x, last.Value.z):F0} m to within {NearMetres:F0} m of the bay; " +
                             $"swept before the frame was drawn, {gone} parked {(gone == 1 ? "car" : "cars")} kept out.");
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        public static void GeneratingPrefix(ParkingLaneGenerator __instance, out HashSet<GameObject>? __state)
        {
            __state = null;
            if (failed || __instance == null || !NearAnyBay(__instance.transform.position)) return;
            try { __state = new HashSet<GameObject>(__instance.transform.Cast<Transform>().Select(t => t.gameObject)); }
            catch (Exception) { __state = null; }
        }

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
                    if (!bays.Any(b => Flat(lane, b.X, b.Z) <= rules.BayRadiusMetres + LaneReachMetres)) continue;
                    var zones = BayMarkers.ZonesOf(site, (float)rules.BayRadiusMetres);
                    if (zones.Count == 0) continue;
                    var released = 0;
                    foreach (Transform child in __instance.transform.Cast<Transform>().ToList())
                    {
                        if (__state.Contains(child.gameObject) || !IsLaneCar(child.gameObject)) continue;
                        unreleasable.Remove(child.gameObject);
                        if (!zones.Any(z => InZone(z, child.gameObject))) continue;
                        if (OnScreen(child.gameObject))
                        {
                            clean.Remove(site.Id);
                            swept.Remove(site.Id);
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
                    if (inside || clean.Contains(site.Id) || (distance > NearMetres && swept.Contains(site.Id))) continue;
                    var zones = BayMarkers.ZonesOf(site, (float)radius);
                    var (gone, left) = Sweep(zones, inView: true);
                    if (gone > 0) Count(site.Id, 0, gone);
                    if (left == 0)
                    {
                        swept.Add(site.Id);
                        clean.Add(site.Id);
                    }
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

        private static (int Swept, int Left) Sweep(List<BayMarkers.BayZone> zones, bool inView)
        {
            var swept = 0;
            var left = 0;
            var mask = 1 << LayerHelper.ParkedVehiclesLayerIndex;
            IEnumerable<GameObject> Near(BayMarkers.BayZone zone)
            {
                var found = new List<GameObject>();
                foreach (var (from, to) in zone.Spans)
                {
                    var middle = zone.Foot + zone.Along * ((from + to) / 2f);
                    var half = new Vector3(zone.Width / 2f + CarMarginMetres, 4f, (to - from) / 2f + CarMarginMetres);
                    var count = Physics.OverlapBoxNonAlloc(middle, half, hits, Quaternion.LookRotation(zone.Along, Vector3.up), mask, QueryTriggerInteraction.Collide);
                    for (var i = 0; i < count; i++)
                    {
                        var car = CarOf(hits[i]);
                        if (car != null && IsLaneCar(car)) found.Add(car);
                    }
                }
                return found;
            }
            foreach (var car in BayKeepOut.CarsInZones(zones, Near, (zone, car) => InZone(zone, car)))
            {
                if (unreleasable.Contains(car)) continue;
                if (inView && OnScreen(car)) left++;
                else if (Release(car)) swept++;
            }
            return (swept, left);
        }

        internal static bool InZone(BayMarkers.BayZone zone, GameObject car)
        {
            var t = car.transform;
            Vector3 middle;
            float halfLength, halfWidth;
            var mesh = car.GetComponent<MeshCollider>()?.sharedMesh;
            if (mesh != null)
            {
                var b = mesh.bounds;
                var scale = t.lossyScale;
                middle = t.TransformPoint(b.center);
                halfLength = b.extents.z * Mathf.Abs(scale.z);
                halfWidth = b.extents.x * Mathf.Abs(scale.x);
            }
            else
            {
                var renderers = car.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    foreach (var r in renderers) b.Encapsulate(r.bounds);
                    middle = b.center;
                    halfLength = halfWidth = Mathf.Max(b.extents.x, b.extents.z);
                }
                else
                {
                    middle = t.position;
                    halfLength = halfWidth = 0f;
                }
            }
            var d = middle - zone.Foot;
            var across = zone.Across;
            var forward = t.forward;
            return BayKeepOut.Overlaps(zone.Spans.Select(sp => ((double)sp.From, (double)sp.To)), zone.Width,
                Vector3.Dot(d, zone.Along), Vector3.Dot(d, across), Vector3.Dot(forward, zone.Along), Vector3.Dot(forward, across),
                halfLength, halfWidth, FootprintMarginMetres);
        }

        internal static GameObject? CarOf(Collider? hit)
        {
            if (hit == null) return null;
            var t = hit.transform;
            while (t.parent != null && t.parent.GetComponent<ParkingLaneGenerator>() == null) t = t.parent;
            return t.parent != null ? t.gameObject : null;
        }

        internal static bool IsLaneCar(GameObject car) =>
            car.activeInHierarchy && car.layer == LayerHelper.ParkedVehiclesLayerIndex &&
            car.transform.parent != null && car.transform.parent.GetComponent<ParkingLaneGenerator>() != null &&
            car.GetComponent<Vehicles.DeliveryDriverJob.DeliveryJobStartController>() == null;

        private static bool OnScreen(GameObject car)
        {
            var camera = GameManager.GetMainCamera();
            if (camera == null) return false;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return car.GetComponentsInChildren<Renderer>().Any(r => r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds));
        }

        internal static bool Release(GameObject car)
        {
            try
            {
                car.transform.parent?.GetComponent<ParkingLaneGenerator>()?.onReleaseVehicle?.Invoke(car);
                ParkingSimulator.ReleaseParkedVehicle(car);
                unreleasable.Remove(car);
                return true;
            }
            catch (Exception ex)
            {
                if (unreleasable.Add(car))
                    Log.Error($"Commissary parking: {car.name} couldn't go back to its pool ({ex.Message}); left where it is, and passed over from now on.");
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
