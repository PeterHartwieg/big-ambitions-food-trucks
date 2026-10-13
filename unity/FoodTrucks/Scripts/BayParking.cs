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
    //   standing there; a car in the main camera's view is left until it isn't, so none vanishes in view;
    // - a bay resolved while the loading screen is still opaque (Commissary.Attach resolves the pending bays then, when
    //   the street is ready) is swept with no view rule; one resolved later keeps the view rule, and its paint, sign and
    //   props wait until no lane car is left in its zone: a car in view stays until it is out of view, is swept, and
    //   only then the paint appears, so no car vanishes in view and none stands in fresh paint (review rounds 1 and 2
    //   of the mod round; the coordinator's decision, 13 October 2026);
    // - on every load, under the loading screen, every bay is swept with no view rule: the lanes fill on the game's
    //   load event, before the mod's late load event, so the postfix never sees a bay's first cars (live session 2,
    //   13 October 2026: the player was teleported to the Hell's Kitchen door, the car in the paint was in view from
    //   the first look, and stayed until the truck was put on it);
    // - when the player jumps (a teleport, a taxi, the game's fast travel: more than CutMetres in one frame), the bays
    //   near where they landed are swept before that frame is drawn, with no view rule: the picture is new anyway.
    // A car is in a bay when any part of its footprint is (Core BayKeepOut), not only its middle.
    // A car goes back as the game's own cleanup sends it: the lane's onReleaseVehicle first, then the pool.
    // Only the lanes' own parked cars: never a player vehicle (another layer), moving traffic (another layer) or the
    // delivery job's van (DeliveryJobStartController).
    internal static class BayParking
    {
        private const float NearMetres = 150f;
        private const float AwayMetres = 200f;
        // The sweep's box reaches this far beyond the zone; a car it finds counts when its footprint, plus
        // FootprintMarginMetres, reaches into the zone.
        private const float CarMarginMetres = 2.0f;
        private const float FootprintMarginMetres = 0.1f;
        // A player move longer than this in one frame is a cut (no vehicle covers it in a frame).
        private const float CutMetres = 50f;
        private static Vector3? lastPlayer;
        private const float SummarySeconds = 60f;
        private static readonly Collider[] hits = new Collider[64];
        // Sites swept clean since the player last came near; cleared again when they go AwayMetres off.
        private static readonly HashSet<string> clean = new HashSet<string>(StringComparer.Ordinal);
        // Sites swept at least once since the load or since their bay was resolved; one not yet swept is swept whatever
        // the player's distance (0.9.9 swept a newly resolved bay only once the player came within NearMetres).
        private static readonly HashSet<string> swept = new HashSet<string>(StringComparer.Ordinal);
        // Sites whose zone has been found empty of lane cars since the load or the bay's resolution: their paint may be
        // drawn (PaintReady). Unlike `swept`, never undone by a car parked later.
        private static readonly HashSet<string> firstCleared = new HashSet<string>(StringComparer.Ordinal);
        // Sites whose paint was logged as waiting.
        private static readonly HashSet<string> waitLogged = new HashSet<string>(StringComparer.Ordinal);
        // Set by Commissary.Attach while it runs under the game's opaque loading screen.
        internal static bool UnderLoadingScreen;

        // Whether a bay's paint, sign and props may be drawn: its zone was found empty once (BayMarkers asks). After an
        // error here nothing is kept out, so nothing waits either.
        public static bool PaintReady(string siteId) => failed || firstCleared.Contains(siteId);

        // A sweep of a site whose paint may wait: cleared opens it (logged if it waited); cars left in view hold it.
        private static void Cleared(string siteId, int left)
        {
            if (left == 0)
            {
                if (firstCleared.Add(siteId) && waitLogged.Remove(siteId))
                    Log.Info($"Commissary {siteId}: the bay is clear; its paint is drawn.");
            }
            else if (!firstCleared.Contains(siteId) && waitLogged.Add(siteId))
                Log.Info($"Commissary {siteId}: paint waits for {left} parked {(left == 1 ? "car" : "cars")} in view to leave the bay.");
        }
        // Cars kept out per site since the last summary line: (at generation, on a sweep).
        private static readonly Dictionary<string, (int Generated, int Swept)> counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        private static float nextSummary;
        private static bool failed;

        // Every resolved bay point, kept for BayPointSeconds, so the hook on every lane in the city costs a few distance
        // checks.
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
            firstCleared.Clear();
            waitLogged.Clear();
            counts.Clear();
            lastPlayer = null;
            failed = false;
        }

        // A bay was resolved again: sweep it on the next look.
        public static void Forget(string siteId)
        {
            clean.Remove(siteId);
            swept.Remove(siteId);
            firstCleared.Remove(siteId);
            waitLogged.Remove(siteId);
            bayPointsUntil = float.MinValue;
        }

        // On load, from Commissary.Attach (the game's late load event, the loading screen still up): every bay resolved
        // from the save is swept with no view rule. A bay resolved later is swept on its first look.
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
                    if (bays == null || bays.Count == 0) continue;
                    var (gone, left) = Sweep(BayMarkers.ZonesOf(site, (float)radius), inView: false);
                    swept.Add(site.Id);
                    Cleared(site.Id, left);
                    Log.Info($"Commissary {site.Id}: bay swept on load, {gone} parked {(gone == 1 ? "car" : "cars")} kept out" + (left > 0 ? $", {left} left" : "") + ".");
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        // A bay just resolved (Commissary.Search, before BayMarkers can draw it): its first clearance. Under the loading
        // screen with no view rule; later with it, and a car left in view holds the paint back (Cleared, PaintReady).
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
                         (left > 0 ? $", {left} in view left for now" : "") + ".");
                Cleared(site.Id, left);
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary parking: " + ex + " Parked cars are no longer kept out of the bays this session.");
            }
        }

        // Every frame (Commissary.LateUpdate, after the frame's moves and before it is drawn): a jump of the player is a
        // cut, and the bays within NearMetres of where they landed are swept with no view rule.
        public static void WatchCut(CommissaryTables sites, double radius, Vector3 player, bool inside)
        {
            if (failed || Commissary.Uninstalled) return;
            // Indoors nothing is remembered, so stepping out of a building (whose interior may stand elsewhere) starts
            // afresh rather than counting as a jump (review round 1, Opus).
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
                    swept.Add(site.Id);
                    Cleared(site.Id, left);
                    if (left == 0) clean.Add(site.Id);
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

        // Prefix on ParkingLaneGenerator.GenerateParkedVehicles: the lane's cars before it runs.
        public static void GeneratingPrefix(ParkingLaneGenerator __instance, out HashSet<GameObject>? __state)
        {
            __state = null;
            // Most of the city's lanes are far from every bay: nothing is noted for them (review round 6).
            if (failed || __instance == null || !NearAnyBay(__instance.transform.position)) return;
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
                    if (!bays.Any(b => Flat(lane, b.X, b.Z) <= rules.BayRadiusMetres + LaneReachMetres)) continue;
                    var zones = BayMarkers.ZonesOf(site, (float)rules.BayRadiusMetres);
                    if (zones.Count == 0) continue;
                    var released = 0;
                    foreach (Transform child in __instance.transform.Cast<Transform>().ToList())
                    {
                        if (__state.Contains(child.gameObject) || !IsLaneCar(child.gameObject)) continue;
                        if (!zones.Any(z => InZone(z, child.gameObject))) continue;
                        if (OnScreen(child.gameObject))
                        {
                            // Swept again on every look, whatever the distance, until it is out of view.
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
                    // Near, or never swept since the load or the bay's resolution (then whatever the distance).
                    if (inside || clean.Contains(site.Id) || (distance > NearMetres && swept.Contains(site.Id))) continue;
                    var zones = BayMarkers.ZonesOf(site, (float)radius);
                    var (gone, left) = Sweep(zones, inView: true);
                    if (gone > 0) Count(site.Id, 0, gone);
                    // Cars on screen are tried again on the next look, and go as soon as they are out of view.
                    if (left == 0)
                    {
                        swept.Add(site.Id);
                        clean.Add(site.Id);
                    }
                    Cleared(site.Id, left);
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

        // Releases the lane cars standing in the zones (and, with inView, not on screen); returns how many went and how
        // many are left.
        private static (int Swept, int Left) Sweep(List<BayMarkers.BayZone> zones, bool inView)
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
                        if (!InZone(zone, car)) continue;
                        if (inView && OnScreen(car))
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

        // Whether any part of a car's footprint reaches into the zone (Core BayKeepOut). The footprint is its mesh
        // collider's bounds (each parked car has one on its root, ParkingLaneGenerator.GenerateParkedVehicles), else its
        // renderers' bounds, else its middle.
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
                    // World bounds: as wide one way as the other, whatever the car's turn.
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

        // The parked car a collider belongs to: the lane's direct child above it.
        internal static GameObject? CarOf(Collider? hit)
        {
            if (hit == null) return null;
            var t = hit.transform;
            while (t.parent != null && t.parent.GetComponent<ParkingLaneGenerator>() == null) t = t.parent;
            return t.parent != null ? t.gameObject : null;
        }

        // One of the lane's own parked cars: an active direct child of a parking lane, on the ParkedVehicles layer, and
        // not the delivery job's van.
        internal static bool IsLaneCar(GameObject car) =>
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

        internal static bool Release(GameObject car)
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
