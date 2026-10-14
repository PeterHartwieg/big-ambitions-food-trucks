#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Dialogs;
using Entities;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UI;
using UI.InteriorDesigner;
using UI.ItemPanel;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

namespace FoodTrucks
{
    // Commissaries (design: Commissary and new foods). Garages from data/commissaries.json where the player rents a
    // space per vehicle in person (the Commissary button at the bay), orders stock by phone for delivery at 08:00,
    // restocks at the bay, and pays rent daily; at 04:00 the City fines a food vehicle that isn't in its bay.
    // Core's Commissaries decides; this finds the bays, moves the money and the cargo, and sends the texts.
    internal sealed class Commissary : MonoBehaviour
    {
        // Each commissary's phone contact is this plus its ID; en.json names it.
        public const string ContactPrefix = "foodtrucks-commissary-";
        private const string ContactDescription = "foodtrucks:commissary_description";
        private const CallDialogType DialogType = (CallDialogType)9103;
        // Rent and orders are tax-deductible under this label; overnight fines are not.
        private const string ExpenseLabel = "foodtrucks:transaction_commissary_label";
        // The player standing this much beyond the bay radius has visited the commissary.
        private const float VisitExtraMetres = 15f;
        private const float CheckSeconds = 0.5f;

        private static bool attached;
        // Set by the uninstall step for the rest of this session: nothing runs, nothing unlocks, no contact comes back.
        private static bool uninstalled;
        internal static bool Uninstalled => uninstalled;
        private long lastMinute = -1;
        private float nextCheck;
        // Vehicles told once per load that their space is at a commissary data/commissaries.json no longer has.
        private readonly HashSet<string> unknownSiteShown = new HashSet<string>();

        private static CommissaryRules? Rules => DemandData.Rules?.Commissary;
        private static CommissaryTables? Sites => DemandData.Commissaries;

        public static void Attach()
        {
            Detach();
            uninstalled = false;
            BayMarkers.Reset();
            BayParking.Reset();
            resolveAt.Clear();
            resolveCursor = 0;
            notReadyLogged.Clear();
            errorLogged.Clear();
            searchedThisLoad.Clear();
            doorLanes.Clear();
            lastSide.Clear();
            Legality.ResetLanes();
            var state = FoodTrucksSession.CurrentState;
            var sites = Sites;
            if (sites == null || Rules == null)
            {
                Log.Info("Commissary: not active (commissaries.json or rules not loaded).");
                return;
            }
            CallDialogFactory.RegisterDialog(DialogType, () => new CommissaryDialog());
            attached = true;
            var found = sites.Sites.Count(s => Origins(s) != null);
            Log.Info($"Commissary: {found} of {sites.Sites.Count} garages found in the city" +
                     (found < sites.Sites.Count ? " (missing: " + string.Join(", ", sites.Sites.Where(s => Origins(s) == null).Select(s => s.Address)) + ")" : "") + ".");
            foreach (var site in sites.Sites)
            {
                var origin = Origins(site)?.FirstOrDefault();
                var resolved = ResolvedBays(site);
                if (origin != null)
                    Log.Info($"Commissary {site.Id} ({site.Address}): bay searched from x {origin.Value.X:F1}, z {origin.Value.Z:F1}{(site.Bay != null ? " (set in data)" : " (nearest entrance)")}; " +
                             (resolved == null ? "not resolved yet." : resolved.Count == 0 ? "no bay in the save; searched again after load." :
                                 "resolved at " + string.Join("; ", resolved.Select(p => $"x {p.X:F1}, z {p.Z:F1}")) + " (from the save)."));
                LogBusinessAt(site);
            }
            // The lanes filled the bays with parked cars on the game's load event, before this one: they go now, under
            // the loading screen. Bays still to be searched are searched first, while the screen hides any car their
            // sweep removes (a bay resolved later keeps the view rule and its paint waits; BayParking).
            BayParking.UnderLoadingScreen = true;
            try
            {
                if (state != null && Legality.ParkingSceneReady())
                    foreach (var site in sites.Sites)
                        if (SearchDue(site)) Resolve(site, "on load", force: true);
                BayParking.SweepOnLoad(sites, Rules!.BayRadiusMetres);
            }
            catch (Exception ex) { Log.Error("Commissary, bays on load: " + ex); }
            finally { BayParking.UnderLoadingScreen = false; }
            if (state == null) return;
            var commissary = state.Commissary;
            // The game doesn't save a contact's call button, so it is set again after every load.
            foreach (var id in commissary.Unlocked)
                if (sites.Site(id) != null) ContactFor(id);
            Log.Info($"Commissary state: unlocked {Join(commissary.Unlocked)}; spaces {Join(commissary.Spaces.Select(s => $"{s.Kind} {World.Short(s.VehicleId)} at {s.CommissaryId}"))}; " +
                     $"{commissary.Orders.Count} orders on the way (paid on delivery), {commissary.Stock.Values.Sum(l => l.Sum(x => (long)x.Units))} units in stock, " +
                     $"{commissary.StandingOrders.Count} standing orders ({string.Join("; ", commissary.StandingOrders.Select(o => $"{o.Id} {o.Repeat} at {o.CommissaryId}, next day {o.NextDay}"))}), " +
                     $"{commissary.Charges.Count} rent charges and {commissary.Fines.Count} fines unpaid; night checked through day {commissary.NightCheckedThroughDay?.ToString() ?? "(not started)"}.");
        }

        public static void Detach() => attached = false;

        // A new game gives random AI shops to retail and office units and a rival's factory to a few random warehouses
        // (CityGenerator.PopulateBuildings, SetupRivalFactories); homes never get one. The garages are picked to avoid
        // that, but a rival factory can still land in one, so the log says when a garage's building has a business.
        private static void LogBusinessAt(CommissarySite site)
        {
            try
            {
                var (street, number) = CommissaryTables.SplitAddress(site.Address);
                var registration = SaveGameManager.Current?.BuildingRegistrations.FirstOrDefault(r => r != null && r.StreetName == street && r.StreetNumber == number);
                var business = registration?.businessTypeName;
                if (string.IsNullOrEmpty(business) || business == "ba:businesstype_empty") return;
                Log.Info($"Commissary {site.Id} ({site.Address}): the building has a business in this game ({business}" +
                         $"{(string.IsNullOrEmpty(registration!.BusinessName) ? "" : ", " + registration.BusinessName)}{(registration.RentedByPlayer ? ", yours" : "")}); the bay works the same.");
            }
            catch (Exception ex) { Log.Error("Commissary, building check: " + ex.Message); }
        }

        private static string Join(IEnumerable<string> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        // Where the bay search starts: the point set in data, else the building's entrances. Null while the city has no
        // such building.
        internal static List<(double X, double Z)>? Origins(CommissarySite site)
        {
            if (site.Bay != null) return new List<(double X, double Z)> { site.Bay.Value };
            return World.EntrancesOf(site.Address)?.Select(d => ((double)d.x, (double)d.z)).ToList();
        }

        // ---- Bays (review round 4, the coordinator's design) ----
        // Peter (11 October 2026): "The purple area is not on a parking spot". Each commissary's bay is resolved once
        // per city: from each origin, the nearest place within BaySnap.MaxMetres where a food truck stands legally parked
        // by the game's own wheel test, centred across its lane (Core BaySnap). The result goes into the save
        // (CommissaryState.Bays), with the origins and the rule version it was searched with, so a load decides from the
        // same points without searching. A vehicle is in the bay when the game's wheel test calls it legally parked and
        // its middle is within the bay radius of a resolved point (Core CommissaryBays.InBay); the paint, Ctrl+F3, the
        // Commissary button, the visit unlock and the 04:00 check all ask InBay or the resolved points. An unresolved
        // commissary decides nothing: no unlock, no button, no paint, and the 04:00 check waits for it.

        // The resolved bay points, or null while not resolved. Empty when the search found no legal parking.
        internal static IReadOnlyList<CommissaryBayPoint>? ResolvedBays(CommissarySite site)
        {
            var state = FoodTrucksSession.CurrentState;
            var origins = Origins(site);
            if (state == null || origins == null || origins.Count == 0) return null;
            return CommissaryBays.Current(state.Commissary, site.Id, origins)?.Points;
        }

        private static IEnumerable<(double X, double Z)>? ResolvedPoints(CommissarySite site) => ResolvedBays(site)?.Select(p => (p.X, p.Z));

        // Two bays closer than this (entrances side by side) are one.
        private const double SameBayMetres = 3;
        // The search reads the parking colliders this far round its origin.
        private const float ProbeMetres = (float)BaySnap.MaxMetres + 8f;
        // A bay that couldn't be resolved (the city's parking lanes not loaded) is tried again after this long.
        private const float ResolveRetrySeconds = 5f;
        private static readonly Dictionary<string, float> resolveAt = new Dictionary<string, float>(StringComparer.Ordinal);
        // Messages logged once per load, keyed by commissary and reason (review round 9: one key per commissary hid a
        // second reason). Readiness notes and errors are kept apart.
        private static readonly HashSet<string> notReadyLogged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> errorLogged = new HashSet<string>(StringComparer.Ordinal);

        private static bool Once(HashSet<string> logged, string siteId, string reason) => logged.Add(siteId + "|" + reason);

        // Commissaries whose bay was searched (to the end) since this load: a stored "no bay" is searched again once per
        // load (Core CommissaryBays.SearchDue; live session 2: a failed search was final for good).
        private static readonly HashSet<string> searchedThisLoad = new HashSet<string>(StringComparer.Ordinal);

        private static bool SearchDue(CommissarySite site)
        {
            var state = FoodTrucksSession.CurrentState;
            var origins = Origins(site);
            if (state == null || origins == null || origins.Count == 0) return false;
            return CommissaryBays.SearchDue(CommissaryBays.Current(state.Commissary, site.Id, origins), searchedThisLoad.Contains(site.Id));
        }

        // Resolves one commissary's bay if it isn't yet: true when it is resolved (now or before). `force` skips the
        // retry wait (the 04:00 check). A stored "no bay" counts as resolved while it waits for its search again.
        private static bool Resolve(CommissarySite site, string why, bool force)
        {
            var state = FoodTrucksSession.CurrentState;
            var origins = Origins(site);
            if (state == null || origins == null || origins.Count == 0) return false;
            var current = CommissaryBays.Current(state.Commissary, site.Id, origins);
            if (!CommissaryBays.SearchDue(current, searchedThisLoad.Contains(site.Id))) return true;
            var stored = current != null;
            if (!FoodTrucksSession.CanWrite) return stored;
            if (!force && resolveAt.TryGetValue(site.Id, out var at) && Time.unscaledTime < at) return stored;
            resolveAt[site.Id] = Time.unscaledTime + ResolveRetrySeconds;
            return Search(state, site, origins, stored ? why + ", searched again: the save had no bay" : why) || stored;
        }

        // The search itself: true when its result was stored, false when the street wasn't ready for it.
        private static bool Search(SaveState state, CommissarySite site, List<(double X, double Z)> origins, string why)
        {
            if (!Legality.ParkingSceneReady())
            {
                if (Once(notReadyLogged, site.Id, "parking lanes"))
                    Log.Info($"Commissary {site.Id}: bay not resolved yet ({why}): the city's parking lanes aren't loaded; tried again every {ResolveRetrySeconds:F0} s.");
                return false;
            }
            var doors = World.EntrancesOf(site.Address);
            var found = new List<(BaySnapResult Bay, (double X, double Z) From, (double X, double Z) Door)>();
            var start = Time.realtimeSinceStartup;
            var colliders = 0;
            // What each origin came to, for the one line a commissary without a bay gets.
            var tried = new List<string>();
            foreach (var origin in origins)
            {
                var label = $"{(site.Bay != null ? "the point set in data" : "the entrance")} at x {origin.X:F1}, z {origin.Z:F1}";
                var door = doors == null || doors.Length == 0 ? (Vector3?)null : doors.OrderBy(d => Flat(d, origin)).First();
                var centre = new Vector3((float)origin.X, door?.y ?? PlayerHelper.GetPosition().y, (float)origin.Z);
                var probe = new Legality.ParkingProbe(centre, ProbeMetres);
                if (probe.Truncated) Log.Error($"Commissary {site.Id}: more than 512 parking colliders round x {origin.X:F1}, z {origin.Z:F1}; some were left out of the search.");
                colliders += probe.Count;
                if (probe.Count == 0)
                {
                    tried.Add($"{label}: no parking lanes within {ProbeMetres:F0} m");
                    continue;
                }
                // The side rule (Peter, 13 October 2026; Core BaySide): a place counts only when the straight walk from
                // the door to it crosses no driving lane, so the bay is on the door's side of the street. From a point set
                // in data the door is the entrance nearest it, and the point itself must pass, else it isn't used.
                if (door == null)
                {
                    tried.Add($"{label}: the building has no entrance to check the street's side from");
                    continue;
                }
                var side = SideLanes(door.Value, origin, BaySnap.MaxMetres);
                if (side.Lanes == null)
                {
                    if (side.Why.Length > 0)
                    {
                        if (Once(errorLogged, site.Id, "traffic read"))
                            Log.Error($"Commissary {site.Id}: bay not resolved yet ({why}): the traffic lanes couldn't be read ({side.Why}); tried again every {ResolveRetrySeconds:F0} s.");
                    }
                    else if (Once(notReadyLogged, site.Id, "traffic"))
                        Log.Info($"Commissary {site.Id}: bay not resolved yet ({why}): the traffic system isn't ready to check the door's street; tried again every {ResolveRetrySeconds:F0} s.");
                    return false;
                }
                var sideLanes = side.Lanes;
                var doorX = (double)door.Value.x;
                var doorZ = (double)door.Value.z;
                // No driving lane at all round the door: every street has lanes, so the read missed them (an entrance well
                // above or below the street, review round 1, Opus). The side can't be checked, so nothing is resolved now
                // and the search is tried again, as while the parking lanes load.
                if (sideLanes.Count == 0)
                {
                    if (Once(errorLogged, site.Id, "no driving lanes"))
                        Log.Error($"Commissary {site.Id}: bay not resolved yet ({why}): no driving lanes found round the door at x {doorX:F1}, " +
                                  $"y {door.Value.y:F1}, z {doorZ:F1} ({side.Why}), so the street's side can't be checked; tried again every {ResolveRetrySeconds:F0} s.");
                    return false;
                }
                if (site.Bay != null && BaySide.FirstCrossing(doorX, doorZ, origin.X, origin.Z, sideLanes) is { } measuredCrossing)
                {
                    var text = $"the measured bay in data/commissaries.json at x {origin.X:F1}, z {origin.Z:F1} is across the road from the door " +
                               $"at x {doorX:F1}, z {doorZ:F1} (the walk crosses a driving lane at x {measuredCrossing.X:F1}, z {measuredCrossing.Z:F1}), so it isn't used";
                    if (Once(errorLogged, site.Id, "measured across")) Log.Error($"Commissary {site.Id} ({site.Address}): {text}.");
                    tried.Add($"{label}: {text}");
                    continue;
                }
                var across = 0;
                Func<double, double, bool> reach = (x, z) =>
                {
                    if (BaySide.SameSide(doorX, doorZ, x, z, sideLanes)) return true;
                    across++;
                    return false;
                };
                // From an entrance, a place the door's pedestrians can't reach within a short walk (the street behind the
                // building, Core WalkRule) is passed over for good; a point set in data is trusted for its walk. The walk
                // starts at the door's NavMesh point, which must itself have a short walk to the street; if it hasn't,
                // nothing is guessed (review round 8: a replacement start near the door could be behind the building):
                // that origin gives no bay and asks for a measured point.
                var tooLong = 0;
                var noRoute = 0;
                if (site.Bay == null)
                {
                    var from = WalkFrom(site.Id, door.Value);
                    if (from.Kind == WalkStart.NotReady)
                    {
                        // The traffic system isn't running yet, so the door can't be checked: not resolved, tried again.
                        if (from.Why.Length > 0)
                        {
                            if (Once(errorLogged, site.Id, "traffic read"))
                                Log.Error($"Commissary {site.Id}: bay not resolved yet ({why}): the traffic lanes couldn't be read ({from.Why}); tried again every {ResolveRetrySeconds:F0} s.");
                        }
                        else if (Once(notReadyLogged, site.Id, "traffic"))
                            Log.Info($"Commissary {site.Id}: bay not resolved yet ({why}): the traffic system isn't ready to check the door's street; tried again every {ResolveRetrySeconds:F0} s.");
                        return false;
                    }
                    if (from.Kind == WalkStart.CutOff)
                    {
                        tried.Add($"{label}: the door at x {door.Value.x:F1}, z {door.Value.z:F1} has no short walk to the street ({from.Why})");
                        continue;
                    }
                    if (from.Kind != WalkStart.Unknown)
                    {
                        var walkStart = from.Point;
                        var sideOnly = reach;
                        // The side first: it is cheaper than a NavMesh path.
                        reach = (x, z) =>
                        {
                            if (!sideOnly(x, z)) return false;
                            var verdict = Walk(walkStart, x, z);
                            if (verdict == WalkVerdict.TooLong) tooLong++;
                            if (verdict == WalkVerdict.NoRoute) noRoute++;
                            return WalkRule.Accepts(verdict);
                        };
                    }
                }
                // A measured bay with a heading is nose-in in a lot: the point as measured when legal facing that way, else
                // the nearest legal place facing that way (Core BaySnap.Measured).
                var bay = site.Bay != null
                    ? BaySnap.Measured(origin.X, origin.Z, site.BayHeading, probe.Legal, probe.Axes, Legality.TruckFootprint(), BaySnap.MaxMetres, reach)
                    : BaySnap.Nearest(origin.X, origin.Z, probe.Legal, probe.Axes, Legality.TruckFootprint(), BaySnap.MaxMetres, reach);
                if (site.InLot && bay != null && bay.Distance > 0)
                    Log.Error($"Commissary {site.Id}: the measured nose-in bay at x {origin.X:F1}, z {origin.Z:F1}, heading {site.BayHeading:F0}, isn't legal by the wheel test; " +
                              $"the nearest legal place facing that way is x {bay.X:F1}, z {bay.Z:F1}, {bay.Distance:F1} m off. Correct the bay in data/commissaries.json.");
                if (site.InLot && bay == null)
                    tried.Add($"{label}: no legal place facing heading {site.BayHeading:F0} (nose-in) within {BaySnap.MaxMetres:F0} m: the stalls may be too shallow for the truck's wheels");
                else if (bay == null)
                    tried.Add(across + tooLong + noRoute > 0
                        ? $"{label}: no legal parking within {BaySnap.MaxMetres:F0} m on the door's side of the street with a short walk from the door " +
                          $"({across} across the road, {tooLong} too long a walk, {noRoute} with no route)"
                        : $"{label}: no legal parking for a truck within {BaySnap.MaxMetres:F0} m ({probe.Count} parking colliders read)");
                if (bay != null && !found.Any(f => BaySnap.Distance(f.Bay.X, f.Bay.Z, bay.X, bay.Z) < SameBayMetres))
                    found.Add((bay, origin, (doorX, doorZ)));
            }
            // No parking collider at all round any origin: more likely the street isn't there yet than a commissary
            // without parking, so nothing is stored and it is tried again (review round 5). "No bay" is stored only
            // after a search over colliders that were there.
            if (colliders == 0)
            {
                if (Once(notReadyLogged, site.Id, "no colliders"))
                    Log.Info($"Commissary {site.Id}: bay not resolved yet ({why}): no parking lanes round it yet; tried again every {ResolveRetrySeconds:F0} s.");
                return false;
            }
            CommissaryBays.Store(state.Commissary, site.Id, origins, found.Select(f => (f.Bay, ((double X, double Z)?)f.Door)));
            searchedThisLoad.Add(site.Id);
            FoodTrucksSession.MarkChanged();
            BayMarkers.Forget(site.Id);
            // The new bay's first clearance, with no view rule, before its paint can be drawn (the markers are made in a
            // later part of this look or a later look, BayMarkers.Update), so a car seen from the first frame can't stay in
            // a bay that appears round it (review round 1, Sol and Astra).
            BayParking.SweepNewBay(site, Rules!.BayRadiusMetres);
            var ms = (Time.realtimeSinceStartup - start) * 1000f;
            foreach (var (bay, from, _) in found)
                Log.Info($"Commissary {site.Id}: bay on legal parking at x {bay.X:F1}, z {bay.Z:F1}, {bay.Distance:F1} m from x {from.X:F1}, z {from.Z:F1} " +
                         $"({(site.Bay != null ? "set in data" : "entrance")}), {(site.InLot ? "nose-in in a lot, facing" : "the lane along")} {Math.Atan2(bay.AlongX, bay.AlongZ) * 180 / Math.PI:F0} degrees; " +
                         $"resolved {why} in {ms:F0} ms and kept in the save.");
            // One line per load for a commissary still without a bay, with what each origin came to.
            if (found.Count == 0)
                Log.Error($"Commissary {site.Id}: no bay this load (searched {why} in {ms:F0} ms): {string.Join("; ", tried)}. " +
                          "No vehicle is ever in its bay, no paint is drawn and its rented vehicles aren't fined at 04:00. " +
                          "Set a measured bay point in data/commissaries.json (the test bridge's commissary bay_candidates lists legal truck spots up to 80 m out).");
            return true;
        }

        // Extra reach for the lanes read round a door (Core BaySide): a lane segment that crosses a walk is read when
        // either of its waypoints is within the read, and the traffic system's waypoints can stand some way apart.
        private const float SideSlackMetres = 40f;

        // The driving lanes' centre lines round a door, far enough out to cover the walk to any place within reach of
        // origin. Null while the traffic system isn't running (Why empty) or its lanes can't be read (Why says why); a
        // read that finds no lane at all is an empty list, and every place is then on the door's side.
        private static (List<((double X, double Z) A, (double X, double Z) B)>? Lanes, string Why) SideLanes(Vector3 door, (double X, double Z) origin, double reach)
        {
            if (!GleyTrafficSystem.TrafficManager.HasInstance || !GleyTrafficSystem.TrafficManager.IsInitialized) return (null, "");
            var lanes = TrafficLanes.Near(door, Flat(door, origin) + (float)reach + SideSlackMetres);
            if (lanes.Unavailable) return (null, lanes.Describe());
            return (lanes.FlatSegments(), lanes.Describe());
        }

        // The driving lanes round each bay point's entrance, read once per load for "in the bay" (Core
        // CommissaryBays.OnDoorSide); a read that failed or found none is tried again after ResolveRetrySeconds.
        private static readonly Dictionary<(double, double), (float Radius, List<((double X, double Z) A, (double X, double Z) B)>? Lanes, float RetryAt)> doorLanes =
            new Dictionary<(double, double), (float, List<((double X, double Z) A, (double X, double Z) B)>?, float)>();

        private static IReadOnlyList<((double X, double Z) A, (double X, double Z) B)>? LanesFor(CommissaryBayPoint point, float radius)
        {
            if (point.DoorX is not { } x || point.DoorZ is not { } z) return null;
            var key = (x, z);
            var door = new Vector3((float)x, 0f, (float)z);
            var need = Flat(door, (point.X, point.Z)) + radius + SideSlackMetres;
            if (doorLanes.TryGetValue(key, out var known))
            {
                if (known.Lanes != null && known.Lanes.Count > 0 && known.Radius >= need) return known.Lanes;
                if (known.Lanes == null || known.Lanes.Count == 0)
                    if (Time.unscaledTime < known.RetryAt) return null;
            }
            // The read's height filter needs the door's height: the ground under the stored entrance.
            var doors = Sites?.Sites.Select(s => World.EntrancesOf(s.Address)).Where(d => d != null).SelectMany(d => d!)
                .Where(d => Math.Abs(d.x - x) < 0.01 && Math.Abs(d.z - z) < 0.01).ToList();
            if (doors == null || doors.Count == 0) return null;
            door.y = doors[0].y;
            var side = SideLanes(door, (point.X, point.Z), radius);
            doorLanes[key] = (need, side.Lanes, Time.unscaledTime + ResolveRetrySeconds);
            return side.Lanes is { Count: > 0 } ? side.Lanes : null;
        }

        // The last side each vehicle was seen on at a commissary, kept while the side can't be checked: the answer stands
        // for that vehicle at that spot only (review round 1, Opus: keep the last known answer or refuse).
        private static readonly Dictionary<(string Site, string Vehicle), (Vector3 At, bool OnSide)> lastSide =
            new Dictionary<(string, string), (Vector3, bool)>();

        private enum WalkStart
        {
            // The door's own NavMesh point has a short walk to the street.
            Door,
            // The door isn't on the NavMesh at all: nothing is known about walks from it.
            Unknown,
            // The door's point has no short walk to the street, or the street can't be found: no bay from it.
            CutOff,
            // The traffic system isn't running yet: the door can't be checked now.
            NotReady,
        }

        // Where the walk check starts for a door: its NavMesh point, if that has a short walk (Core WalkRule) to the
        // street, the NavMesh by the nearest driving lane within 40 m.
        private static (WalkStart Kind, Vector3 Point, string Why) WalkFrom(string siteId, Vector3 door)
        {
            try
            {
                const int areas = UnityEngine.AI.NavMesh.AllAreas;
                if (!UnityEngine.AI.NavMesh.SamplePosition(door, out var doorHit, 3f, areas)) return (WalkStart.Unknown, door, "");
                if (!GleyTrafficSystem.TrafficManager.HasInstance || !GleyTrafficSystem.TrafficManager.IsInitialized) return (WalkStart.NotReady, door, "");
                var lanes = TrafficLanes.Near(door, 40f);
                // A read that failed isn't "no street near": not ready, tried again, never saved (review round 9).
                if (lanes.Unavailable) return (WalkStart.NotReady, door, lanes.Describe());
                var lane = lanes.Nearest(door);
                if (lane == null) return (WalkStart.CutOff, doorHit.position, "no driving lane within 40 m: " + lanes.Describe());
                if (!UnityEngine.AI.NavMesh.SamplePosition(lane.Value.Point, out var streetHit, 3f, areas))
                    return (WalkStart.CutOff, doorHit.position, $"no NavMesh by the driving lane at x {lane.Value.Point.x:F1}, z {lane.Value.Point.z:F1}");
                var path = new UnityEngine.AI.NavMeshPath();
                var complete = UnityEngine.AI.NavMesh.CalculatePath(doorHit.position, streetHit.position, areas, path) &&
                               path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                var length = 0f;
                for (var i = 1; complete && i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                var verdict = WalkRule.Classify(true, true, complete, length, Vector3.Distance(doorHit.position, streetHit.position));
                if (verdict == WalkVerdict.Short) return (WalkStart.Door, doorHit.position, "");
                return (WalkStart.CutOff, doorHit.position, verdict == WalkVerdict.TooLong
                    ? $"the walk to the street at x {streetHit.position.x:F1}, z {streetHit.position.z:F1} is {length:F0} m"
                    : $"no route to the street at x {streetHit.position.x:F1}, z {streetHit.position.z:F1}");
            }
            catch (Exception ex)
            {
                if (Once(errorLogged, siteId, "walk check: " + ex.GetType().Name)) Log.Error($"Commissary {siteId}: the door's walk check failed ({ex.Message}); tried again later.");
                return (WalkStart.NotReady, door, "");
            }
        }

        // The walk from a start on the NavMesh to the pavement nearest a place (Core WalkRule).
        private static WalkVerdict Walk(Vector3 from, double x, double z) => WalkDetail(from, x, z).Verdict;

        // The walk with its lengths: the NavMesh path (0 when there is none) and the straight line.
        private static (WalkVerdict Verdict, float Path, float Straight) WalkDetail(Vector3 from, double x, double z)
        {
            try
            {
                var place = new Vector3((float)x, from.y, (float)z);
                if (!UnityEngine.AI.NavMesh.SamplePosition(place, out var to, 4f, UnityEngine.AI.NavMesh.AllAreas))
                    return (WalkRule.Classify(true, false, false, 0, 0), 0f, 0f);
                var path = new UnityEngine.AI.NavMeshPath();
                var complete = UnityEngine.AI.NavMesh.CalculatePath(from, to.position, UnityEngine.AI.NavMesh.AllAreas, path) &&
                               path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                var length = 0f;
                for (var i = 1; complete && i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                var straight = Vector3.Distance(from, to.position);
                return (WalkRule.Classify(true, true, complete, length, straight), length, straight);
            }
            catch (Exception) { return (WalkVerdict.Unknown, 0f, 0f); }
        }

        // Twice a second from Look: at most one unresolved commissary per call, so searches spread across frames. They
        // are taken in turn, and one whose retry isn't due is passed over, so a commissary that waits doesn't hold up
        // the others (review round 6; Core ResolveOrder).
        private static int resolveCursor;

        private static void ResolveOne(CommissaryTables sites)
        {
            var list = sites.Sites;
            var now = Time.unscaledTime;
            var next = ResolveOrder.Next(list.Count, resolveCursor, i =>
                SearchDue(list[i]) && !(resolveAt.TryGetValue(list[i].Id, out var at) && now < at));
            if (next < 0) return;
            resolveCursor = next + 1;
            Resolve(list[next], "after load", force: false);
        }

        private static float Flat(Vector3 a, (double X, double Z) b) => new Vector2(a.x - (float)b.X, a.z - (float)b.Z).magnitude;

        private static bool CityReady(CommissaryTables sites) => sites.Sites.Any(s => Origins(s) != null);

        // The commissary whose resolved bay a position is within (radius plus extraMetres) of: the visit unlock.
        internal static CommissarySite? SiteNear(Vector3 position, float extraMetres)
        {
            var sites = Sites;
            var rules = Rules;
            if (sites == null || rules == null) return null;
            return sites.At(ResolvedPoints, position.x, position.z, rules.BayRadiusMetres + extraMetres);
        }

        // The commissary whose bay the vehicle is in (legally parked, its middle within the radius), or null.
        internal static CommissarySite? SiteOf(VehicleInstance vehicle) => Sites?.Sites.FirstOrDefault(s => InBay(vehicle, s, out _));

        // Hourly parking uses the same rented space and bay test as 04:00. A missing or unresolved bay grants no parking
        // exemption; the overnight rule's NoBay grace doesn't mean the vehicle is standing in a rented bay.
        internal static CommissarySite? PublicParkingBay(VehicleInstance vehicle)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (state == null || !FoodTrucksSession.CanWrite || StreetSales.Find(state, vehicle.id)?.Transit != null) return null;
                var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
                var site = space == null ? null : Sites?.Site(space.CommissaryId);
                if (site == null || ResolvedBays(site) is not { Count: > 0 }) return null;
                var bayId = InBay(vehicle, site, out _) ? site.Id : null;
                return Commissaries.PublicParkingCovered(state.Commissary, vehicle.id, FoodTrucksMod.KindOf(vehicle.vehicleTypeName), bayId)
                    ? site : null;
            }
            catch (Exception ex) { Log.Error(ex); return null; }
        }

        // A commissary searched with no legal parking found: it has no bay, and its vehicles aren't fined.
        private static bool NoBay(CommissarySite site) => ResolvedBays(site) is { Count: 0 };

        private static Vector3 PositionOf(VehicleInstance vehicle)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            return controller != null ? controller.transform.position : (Vector3)vehicle.position;
        }

        // Parked in the open (not in a building or garage), its middle within the radius of the commissary's resolved
        // bay, and (a truck) legally parked by the game's test. A cart counts by its middle alone (Peter, review round 5).
        internal static bool InBay(VehicleInstance vehicle, CommissarySite site, out string why) => InBayAnswer(vehicle, site, out why) == true;

        // InBay with a third answer: null when the vehicle is within the radius but its street side can't be read yet
        // (no driving lanes read round the door and no answer kept for it there). The 04:00 check waits on that rather
        // than fine (review round 2, Astra and Sol); everything else takes it as "not in the bay".
        internal static bool? InBayAnswer(VehicleInstance vehicle, CommissarySite site, out string why)
        {
            var rules = Rules;
            var points = ResolvedBays(site);
            if (rules == null || points == null) { why = "bay not resolved"; return false; }
            if (!string.IsNullOrEmpty(vehicle.streetName)) { why = "inside a building"; return false; }
            var position = PositionOf(vehicle);
            if (!CommissaryBays.WithinRadius(points, position.x, position.z, rules.BayRadiusMetres)) { why = "outside the radius"; return false; }
            // On the door's side of the street (Core CommissaryBays.OnDoorSide): the opposite kerb is within the radius too.
            var onSide = CommissaryBays.OnDoorSide(points, position.x, position.z, rules.BayRadiusMetres, p => LanesFor(p, (float)rules.BayRadiusMetres));
            var sideKey = (site.Id, vehicle.id ?? "");
            if (onSide != null) lastSide[sideKey] = (position, onSide.Value);
            else if (lastSide.TryGetValue(sideKey, out var last) && Flat(last.At, (position.x, position.z)) < 0.5f) onSide = last.OnSide;
            if (onSide == null) { why = "the street's side can't be checked yet (no driving lanes read round the door)"; return null; }
            if (onSide == false) { why = "across the road from the door"; return false; }
            if (!CommissaryBays.RequiresLegalParking(FoodTrucksMod.KindOf(vehicle.vehicleTypeName)))
            {
                why = "a cart within the radius";
                return true;
            }
            var legal = Legality.VehicleLegallyParked(vehicle, out var how);
            why = legal ? "legally parked (" + how + ")" : "not legally parked (" + how + ")";
            return CommissaryBays.InBay(points, position.x, position.z, rules.BayRadiusMetres, legal);
        }

        internal static string Name(CommissarySite site) => LocalizorManager.GetLocalization(site.Name, null);

        private static string NameOf(string? id)
        {
            var site = Sites?.Site(id);
            return site == null ? id ?? "" : Name(site);
        }

        // The commissary's contact, with its call button. Created (as a new contact, with the game's notice) on the first visit.
        internal static Contact ContactFor(string siteId)
        {
            var contact = Contact.EnsurePermanentContact(ContactPrefix + siteId, ContactCategoryName.General, ContactDescription);
            contact.callDialogTypeOverride = DialogType;
            return contact;
        }

        // The first visit: the commissary's contact appears, as the game's own contacts do, and it writes.
        internal static bool Unlock(SaveState state, CommissarySite site, string how)
        {
            if (uninstalled) return false;
            if (!FoodTrucksSession.CanWrite || !Commissaries.Unlock(state.Commissary, site.Id)) return false;
            FoodTrucksSession.MarkChanged();
            var contact = Contact.GetContact(ContactPrefix + site.Id, ContactCategoryName.General, ContactDescription, null, hasWelcomeMessages: false, skipNewNotification: false);
            contact.callDialogTypeOverride = DialogType;
            try { Contact.ShowAddedContactNotifications(); }
            catch (Exception ex) { Log.Error("Commissary, new contact notice: " + ex); }
            var rules = Rules!;
            Notices.SendText(contact, "foodtrucks:message_commissary_welcome", new Dictionary<string, string>
            {
                { "commissary", Name(site) },
                { "cart", CartSelling.Money(rules.RentPerMonthCents.TryGetValue("cart", out var cart) ? cart : 0) },
                { "truck", CartSelling.Money(rules.RentPerMonthCents.TryGetValue("truck", out var truck) ? truck : 0) }
            });
            Log.Info($"Commissary {site.Id}: unlocked on day {SaveGameManager.Current.Day} ({how}); its contact added.");
            return true;
        }

        // The order form's call can end with the phone closed, which tells no one: its item list is closed then. Checked
        // in Update and again in LateUpdate, so a phone closed anywhere in a frame (input, UI events, other scripts'
        // Update) is seen before the next frame's clicks reach the list.
        // While the game quits the player is destroyed before this component, and PlayerHelper.GetPosition throws
        // (six errors after "Quitting game..." on 13 October 2026).
        private static bool NoPlayer => InstanceBehavior<GameManager>.Instance?.playerController == null;

        private void LateUpdate()
        {
            CommissaryOrderForm.Watch();
            // A jump of the player (teleport, taxi, fast travel) sweeps the bays near where they land before the frame
            // is drawn (BayParking).
            var sites = Sites;
            var rules = Rules;
            if (!attached || uninstalled || sites == null || rules == null || FoodTrucksSession.CurrentState == null || NoPlayer) return;
            try
            {
                var inside = BuildingManager.IsInsideBuilding || Parking.UndergroundParking.UndergroundParkingManager.IsInsideParking;
                BayParking.WatchCut(sites, rules.BayRadiusMetres, PlayerHelper.GetPosition(), inside);
            }
            catch (Exception ex) { Log.Error("Commissary, parked cars after a jump: " + ex); }
        }

        private void Update()
        {
            CommissaryOrderForm.Watch();
            StartPendingCall();
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var sites = Sites;
            if (!attached || uninstalled || state == null || rules == null || sites == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null || NoPlayer) return;
            if (!CityReady(sites)) return;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckSeconds;
                try { Look(state); }
                catch (Exception ex) { Log.Error("Commissary, visit check: " + ex); }
            }
            var now = HireCrew.Now();
            if (now == lastMinute) return;
            lastMinute = now;
            try { Step(state, rules, sites, now); }
            catch (Exception ex) { Log.Error("Commissary: " + ex); }
        }

        // Twice a second: the player at a bay unlocks it; the panel's Commissary button shows while the vehicle is in a bay.
        private static void Look(SaveState state)
        {
            if (Sites != null) ResolveOne(Sites);
            if (!BuildingManager.IsInsideBuilding)
            {
                var visit = SiteNear(PlayerHelper.GetPosition(), VisitExtraMetres);
                if (visit != null) Unlock(state, visit, "visited");
            }
            var vehicle = VehicleHelper.GetCurrentVehicle();
            var inFoodVehicle = vehicle != null && FoodTrucksMod.KindOf(vehicle.vehicleTypeName) != null;
            var atBay = inFoodVehicle && SiteOf(vehicle!) != null;
            CartButton.ShowCommissary(atBay);
            var sites = Sites;
            var rules = Rules;
            if (sites != null && rules != null)
            {
                var inside = BuildingManager.IsInsideBuilding || Parking.UndergroundParking.UndergroundParkingManager.IsInsideParking;
                BayMarkers.Update(sites, rules.BayRadiusMetres, PlayerHelper.GetPosition(), inFoodVehicle, inside);
                BayParking.Update(sites, rules.BayRadiusMetres, PlayerHelper.GetPosition(), inside);
            }
        }

        // Once a game minute (or once after a skip): unpaid charges tried again, spaces of vehicles gone given up,
        // rent charged, orders delivered and paid, and the 04:00 check.
        private void Step(SaveState state, CommissaryRules rules, CommissaryTables sites, long now)
        {
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            PayPending(state, rules, showNotification: false);
            PruneSpaces(state, rules);
            ChargeRent(state, rules);
            BookStanding(state, rules, day, minute);
            Deliver(state, rules, day, minute);
            NightCheck(state, rules, sites, now, day, minute);
        }

        // The game's own setting that lifts its wholesale and import limits lifts the commissary's weekly food limits too
        // [code, 1.0: DeliveryHelper.AreWholesaleAndImportLimitsDisabled reads gameVariables.disableWholesaleAndImportLimits].
        internal static bool LimitsLifted => SaveGameManager.Current?.gameVariables?.disableWholesaleAndImportLimits == true;

        // Today's catalogue prices, as the phone order form has them: what a standing order books at.
        private static Func<string, CommissaryPrice?> TodaysPrices()
        {
            Dictionary<string, (int Box, long Cents)>? prices = null;
            return item =>
            {
                prices ??= Catalogue(false).Concat(Catalogue(true)).GroupBy(c => c.Item, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => (g.First().Box, g.First().UnitCents), StringComparer.Ordinal);
                return prices.TryGetValue(item, out var p) ? new CommissaryPrice(p.Box, p.Cents) : (CommissaryPrice?)null;
            };
        }

        // Standing orders due by now book today's delivery at today's prices (Core CommissaryStandingOrders.Book), before
        // Deliver pays for them in the same step. What a food's weekly limit left out, items the commissary no longer
        // sells and a day without a space there are texted by the commissary; the standing order stays.
        internal static void BookStanding(SaveState state, CommissaryRules rules, int day, int minute)
        {
            if (state.Commissary.StandingOrders.Count == 0) return;
            var runs = CommissaryStandingOrders.Book(state.Commissary, rules, day, minute, TodaysPrices(), LimitsLifted);
            if (runs.Count == 0) return;
            FoodTrucksSession.MarkChanged();
            foreach (var run in runs) TellRun(state, run);
        }

        // One standing order's booking: a log line, and the commissary's texts. "No space" is texted once until it has a
        // space again, an item no longer sold once until it sells again (Core TellNoSpace, TellNotSold); a limit cut each
        // time it happens.
        private static void TellRun(SaveState state, CommissaryStandingBooking run)
        {
            var standing = run.Standing;
            var site = standing.CommissaryId;
            if (run.Missed > 0)
                Log.Info($"Commissary standing order {standing.Id} at {site}: {run.Missed} delivery days passed without a booking run; skipped, next day {standing.NextDay}.");
            if (run.Orders.Count == 0 && run.Cut.Count == 0 && run.NotSold.Count == 0 && !run.NoSpace) return;
            Log.Info($"Commissary standing order {standing.Id} ({standing.Repeat}) at {site}: day {run.Day} " +
                     (run.NoSpace ? "not booked: no vehicle has a space there" : $"booked {(run.Orders.Count == 0 ? "nothing" : string.Join(", ", run.Orders.Select(o => $"order {o.Id} {o.Units} {o.ItemName}")))}") +
                     (run.Cut.Count == 0 ? "" : $"; weekly limit cut {string.Join(", ", run.Cut.Select(c => $"{c.ItemName} {c.Booked}/{c.Wanted} boxes"))}") +
                     (run.NotSold.Count == 0 ? "" : $"; not sold any more: {string.Join(", ", run.NotSold)}") + $"; next day {standing.NextDay}.");
            if (!state.Commissary.Unlocked.Contains(site)) return;
            var contact = ContactFor(site);
            if (run.TellNoSpace)
                Notices.SendText(contact, "foodtrucks:message_commissary_standing_nospace", new Dictionary<string, string> { { "items", StandingItems(standing) } });
            if (run.Cut.Count > 0)
                Notices.SendText(contact, "foodtrucks:message_commissary_standing_cut", new Dictionary<string, string>
                {
                    { "cut", string.Join(", ", run.Cut.Select(c => string.Format(LocalizorManager.GetLocalization("foodtrucks:commissary_standing_cut_line", null),
                        LocalizorManager.GetLocalization(c.ItemName, null), c.Booked, c.Wanted))) },
                    { "weekday", CityPitches.Weekday(CommissarySupply.WeekStart(run.Day) + 7) }
                });
            if (run.TellNotSold.Count > 0)
                Notices.SendText(contact, "foodtrucks:message_commissary_standing_notsold", new Dictionary<string, string>
                {
                    { "items", string.Join(", ", run.TellNotSold.Select(i => LocalizorManager.GetLocalization(i, null))) }
                });
        }

        // "2 boxes of Hot dog, Griddle": a standing order's lines.
        internal static string StandingItems(CommissaryStandingOrder standing) =>
            string.Join(", ", standing.Lines.Select(l => string.Format(LocalizorManager.GetLocalization(
                l.Capped ? (l.Boxes == 1 ? "foodtrucks:commissary_standing_line_one" : "foodtrucks:commissary_standing_line") : "foodtrucks:commissary_standing_piece", null),
                l.Boxes, LocalizorManager.GetLocalization(l.ItemName, null))));

        // "every day", "every Thursday".
        internal static string StandingRepeat(CommissaryStandingOrder standing) =>
            standing.Repeat == CommissaryRepeat.Weekly
                ? LocalizorManager.GetLocalization("foodtrucks:commissary_repeat_weekly", null).Replace("{weekday}", CityPitches.Weekday(standing.NextDay))
                : LocalizorManager.GetLocalization("foodtrucks:commissary_repeat_daily", null);

        // Debts the game refused earlier (not enough money), tried again: rent, then fines. Orders don't wait here: they
        // are paid on delivery, and one the game refuses then is cancelled (Deliver).
        private static void PayPending(SaveState state, CommissaryRules rules, bool showNotification)
        {
            var commissary = state.Commissary;
            var today = SaveGameManager.Current.Day;
            foreach (var charge in commissary.Charges.ToList())
            {
                if (!Payments.TryPayOut(charge.Cents, RentInfo(charge.CommissaryId), showNotification)) continue;
                commissary.Charges.Remove(charge);
                SalesLedger.RecordCommissaryRent(state, today, charge.VehicleId, charge.Cents);
                Log.Info($"Commissary rent: {World.Short(charge.VehicleId)} at {charge.CommissaryId} for day {charge.Day}, {CartSelling.Money(charge.Cents)} paid on a retry.");
                FoodTrucksSession.MarkChanged();
            }
            foreach (var fine in commissary.Fines.ToList())
            {
                if (!Payments.TryPayOut(fine.Cents, FineInfo(), showNotification)) continue;
                commissary.Fines.Remove(fine);
                SalesLedger.RecordOvernightFine(state, today, fine.VehicleId, fine.Cents);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Overnight fine: {World.Short(fine.VehicleId)} for the night of day {fine.Day}, {CartSelling.Money(fine.Cents)} paid on a retry.");
            }
        }

        // The player's food vehicles, by ID: on the map and those with the private driver.
        private static Dictionary<string, VehicleInstance> Fleet()
        {
            var game = SaveGameManager.Current;
            var fleet = new Dictionary<string, VehicleInstance>(StringComparer.Ordinal);
            foreach (var vehicle in game.VehicleInstances.Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>()))
                if (vehicle != null && !string.IsNullOrEmpty(vehicle.id) && FoodTrucksMod.KindOf(vehicle.vehicleTypeName) != null && !fleet.ContainsKey(vehicle.id))
                    fleet[vehicle.id] = vehicle;
            return fleet;
        }

        // A space whose vehicle the player no longer has (sold, removed) is given up after its rent is settled.
        private static void PruneSpaces(SaveState state, CommissaryRules rules)
        {
            var commissary = state.Commissary;
            if (commissary.Spaces.Count == 0) return;
            var fleet = Fleet();
            var gone = commissary.Spaces.Where(s => !fleet.ContainsKey(s.VehicleId)).Select(s => s.VehicleId).ToList();
            if (gone.Count == 0) return;
            ChargeRent(state, rules);
            foreach (var id in gone)
            {
                Commissaries.Release(commissary, id);
                Log.Info($"Commissary: the space of {World.Short(id)} given up, the vehicle is gone.");
            }
            FoodTrucksSession.MarkChanged();
        }

        // Rent for every day not charged yet, today's included; a refused payment waits as a charge.
        private static void ChargeRent(SaveState state, CommissaryRules rules)
        {
            var today = SaveGameManager.Current.Day;
            if (today < 1) return;
            var charges = Commissaries.RentDue(state.Commissary, rules, today);
            if (charges.Count == 0) return;
            FoodTrucksSession.MarkChanged();
            foreach (var charge in charges)
            {
                if (charge.Cents <= 0) continue;
                if (Payments.TryPayOut(charge.Cents, RentInfo(charge.CommissaryId), showNotification: false))
                {
                    SalesLedger.RecordCommissaryRent(state, today, charge.VehicleId, charge.Cents);
                    Log.Info($"Commissary rent: {World.Short(charge.VehicleId)} at {charge.CommissaryId} for day {charge.Day}, {CartSelling.Money(charge.Cents)} paid.");
                    continue;
                }
                state.Commissary.Charges.Add(charge);
                Log.Info($"Commissary rent: {World.Short(charge.VehicleId)} at {charge.CommissaryId} for day {charge.Day}, {CartSelling.Money(charge.Cents)} unpaid (not enough money); tried again.");
                if (state.Commissary.Unlocked.Contains(charge.CommissaryId))
                    Notices.SendText(ContactFor(charge.CommissaryId), "foodtrucks:message_commissary_rent_unpaid", new Dictionary<string, string>
                    {
                        { "rent", CartSelling.Money(charge.Cents) },
                        { "day", charge.Day.ToString() },
                        { "vehicle", VehicleName(state, charge.VehicleId) }
                    });
            }
        }

        // Orders due by now arrive and are paid, goods and delivery fee in one payment under the commissary's tax label, as
        // the game's wholesale and furniture deliveries are paid when they arrive [code, 1.0: BusinessHelper.
        // DeliverWholesaleContract, FurnitureDeliveryHelper.PayDelivery]. A payment the game refuses (not enough money)
        // cancels that order and nothing arrives; the commissary texts why, as the game's stores do with their
        // "not enough funds" message, and the order isn't tried again.
        internal static void Deliver(SaveState state, CommissaryRules rules, int day, int minute)
        {
            if (state.Commissary.Orders.Count == 0) return;
            var today = SaveGameManager.Current.Day;
            var run = Commissaries.Deliver(state.Commissary, rules, day, minute, order =>
            {
                // Payments.TryPayOut says whether the game booked the payment even when it throws. Nothing to pay is paid.
                var paid = order.TotalCents <= 0 || Payments.TryPayOut(order.TotalCents, OrderInfo(order.CommissaryId), showNotification: false);
                FoodTrucksSession.MarkChanged();
                if (!paid) return false;
                try { SalesLedger.RecordDeliveryFees(state, today, order.FeeCents); }
                catch (Exception ex) { Log.Error($"Commissary order {order.Id}: paid, but its delivery fee wasn't recorded for Econoview: {ex}"); }
                return true;
            });
            foreach (var order in run.Delivered)
            {
                Log.Info($"Commissary delivery: order {order.Id}, {order.Units} {order.ItemName} at {order.CommissaryId} (ordered day {order.OrderedDay}, due day {order.DeliverDay}), " +
                         $"paid {CartSelling.Money(order.TotalCents)} on delivery (goods {CartSelling.Money(order.GoodsCents)}, fee {CartSelling.Money(order.FeeCents)}).");
                if (state.Commissary.Unlocked.Contains(order.CommissaryId))
                    Notices.SendText(ContactFor(order.CommissaryId), "foodtrucks:message_commissary_delivered", new Dictionary<string, string>
                    {
                        { "units", order.Units.ToString() },
                        { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                        { "commissary", NameOf(order.CommissaryId) },
                        { "total", CartSelling.Money(order.TotalCents) }
                    });
            }
            foreach (var order in run.Refused)
            {
                var standing = order.StandingOrderId != null;
                Log.Info($"Commissary delivery: order {order.Id}, {order.Units} {order.ItemName} at {order.CommissaryId} not delivered: " +
                         $"the payment of {CartSelling.Money(order.TotalCents)} was refused (not enough money); the order is cancelled" +
                         (standing ? $", standing order {order.StandingOrderId} stays." : "."));
                // A standing order's delivery: only that one is cancelled; the standing order stays (its own text says so).
                if (state.Commissary.Unlocked.Contains(order.CommissaryId))
                    Notices.SendText(ContactFor(order.CommissaryId), standing ? "foodtrucks:message_commissary_standing_unpaid" : "foodtrucks:message_commissary_delivery_unpaid", new Dictionary<string, string>
                    {
                        { "units", order.Units.ToString() },
                        { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                        { "total", CartSelling.Money(order.TotalCents) }
                    });
            }
        }

        // The 04:00 check (design: Overnight rule). Core keeps the day it checked through, so a load or a skip catches up.
        private void NightCheck(SaveState state, CommissaryRules rules, CommissaryTables sites, long now, int day, int minute)
        {
            var commissary = state.Commissary;
            var fleet = Fleet();
            if (commissary.NightCheckedThroughDay == null)
            {
                // The first check this save sees starts tracking from now: a night that has passed before the rule
                // existed isn't fined (Core would check this morning's 04:00 after 04:00).
                Commissaries.NightCheck(commissary, rules, day, minute, Enumerable.Empty<CommissaryVehicle>());
                FoodTrucksSession.MarkChanged();
                Log.Info($"Overnight check: starts on day {day} at {CityPitches.Clock(minute)}, checked through day {commissary.NightCheckedThroughDay}; {fleet.Count} food vehicles.");
                if (fleet.Count > 0)
                    Notices.SendText(CityLicensing.City(), "foodtrucks:message_city_overnight_rule", new Dictionary<string, string>
                    {
                        { "fine", CartSelling.Money(rules.OvernightFineCents) },
                        { "time", CityPitches.Clock(rules.NightCheckMinute) }
                    });
                return;
            }
            // Nothing to check until 04:00 has passed since the last check.
            var latest = minute >= rules.NightCheckMinute ? day : day - 1;
            if (latest <= commissary.NightCheckedThroughDay.Value) return;
            // Every commissary with a rented space is resolved first, wherever the player is; one that can't be yet (the
            // city's parking lanes not loaded) holds the check until it is, rather than deciding from another point.
            var waiting = commissary.Spaces.Select(sp => sites.Site(sp.CommissaryId)).Where(site => site != null).Distinct()
                .Where(site => !Resolve(site!, "for the 04:00 check", force: true)).Select(site => site!.Id).ToList();
            if (waiting.Count > 0)
            {
                if (nightWaitLogged != latest)
                {
                    nightWaitLogged = latest;
                    Log.Info($"Overnight check through day {latest} waits: the bay of {string.Join(", ", waiting)} isn't resolved yet (parking lanes not loaded).");
                }
                return;
            }
            var unresolvedOther = sites.Sites.Where(site => ResolvedBays(site) == null).Select(site => site.Id).ToList();
            var flags = Flags(state, sites, fleet, now);
            // A vehicle in its bay's radius whose street side can't be read yet (no lanes read round the door, as just
            // after a load) holds the check too, never fined on an unknown (Core Commissaries.Undecided).
            var undecided = Commissaries.Undecided(commissary, flags);
            if (undecided.Count > 0)
            {
                if (nightSideWaitLogged != latest)
                {
                    nightSideWaitLogged = latest;
                    Log.Info($"Overnight check through day {latest} waits: the street side of the bay can't be read yet for {string.Join(", ", undecided.Select(id => World.Short(id)))} " +
                             "(no driving lanes read round the door); tried again each minute.");
                }
                return;
            }
            var fines = Commissaries.NightCheck(commissary, rules, day, minute, flags);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Overnight check through day {commissary.NightCheckedThroughDay}: " +
                     (flags.Count == 0 ? "no food vehicles" : string.Join("; ", flags.Select(Describe))) + $"; {fines.Count} fines" +
                     (unresolvedOther.Count > 0 ? $" (bays not resolved, no space rented there: {string.Join(", ", unresolvedOther)})" : "") + ".");
            foreach (var fine in fines)
            {
                var paid = fine.Cents <= 0 || Payments.TryPayOut(fine.Cents, FineInfo(), showNotification: false);
                if (paid) SalesLedger.RecordOvernightFine(state, SaveGameManager.Current.Day, fine.VehicleId, fine.Cents);
                else commissary.Fines.Add(fine);
                Log.Info($"Overnight fine: {World.Short(fine.VehicleId)} out on the night of day {fine.Day}, {CartSelling.Money(fine.Cents)} {(paid ? "paid" : "unpaid (not enough money); tried again")}.");
                // The text says what was wrong (live session 4: a truck legally in another commissary's bay, with no space
                // anywhere, read "wasn't in a commissary bay"): no space at all, or not in its own commissary's bay.
                var space = commissary.Spaces.FirstOrDefault(sp => sp.VehicleId == fine.VehicleId);
                // New keys for the new texts: the game keeps a sent message's key and parameters and words it again from
                // them, so the old keys keep their old text for fines already in the phone (review round 4, Astra and Sol).
                var key = space == null ? "foodtrucks:message_city_overnight_fine_nospace" : "foodtrucks:message_city_overnight_fine_ownbay";
                Notices.SendText(CityLicensing.City(), paid ? key : key + "_unpaid", new Dictionary<string, string>
                {
                    { "commissary", space == null ? "" : NameOf(space.CommissaryId) },
                    { "vehicle", VehicleName(state, fine.VehicleId) },
                    { "day", fine.Day.ToString() },
                    { "fine", CartSelling.Money(fine.Cents) },
                    { "time", CityPitches.Clock(rules.NightCheckMinute) }
                });
            }
        }

        private int nightWaitLogged = -1;
        private int nightSideWaitLogged = -1;

        private static string Describe(CommissaryVehicle v) =>
            $"{World.Short(v.VehicleId)} {(v.AtOwnCommissaryBay ? "in its bay" : "out")}{(v.PlayerInside ? ", player inside" : "")}{(v.OnLateStop ? ", late stop" : "")}{(v.InTransit ? ", in transit" : "")}{(v.AtPlayerProperty ? ", at your own building" : "")}";

        // The addresses ("street#number") of buildings the player rents (a business, warehouse or home: the game's
        // RentedByPlayer) or owns (its real estate list).
        private static HashSet<string> PlayerProperty()
        {
            var game = SaveGameManager.Current;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var registration in game.BuildingRegistrations)
            {
                var address = registration?.Address;
                if (registration != null && registration.RentedByPlayer && address != null && !string.IsNullOrEmpty(address.streetName))
                    set.Add(World.AddressKey(address.streetName, address.streetNumber));
            }
            foreach (var estate in game.realEstate ?? new List<RealEstate>())
                if (estate?.address != null && !string.IsNullOrEmpty(estate.address.streetName))
                    set.Add(World.AddressKey(estate.address.streetName, estate.address.streetNumber));
            return set;
        }

        // Peter (9 October 2026): a vehicle at the player's own building isn't fined. The game records a vehicle inside a
        // building (a warehouse's vehicle slot, a garage, any interior) with that building's address, so that counts;
        // the game has no parking lot of a building, so outside it counts within the bay radius of one of its entrances.
        private static bool AtProperty(VehicleInstance vehicle, HashSet<string> property)
        {
            if (property.Count == 0) return false;
            if (!string.IsNullOrEmpty(vehicle.streetName)) return property.Contains(World.AddressKey(vehicle.streetName, vehicle.streetNumber));
            var rules = Rules;
            if (rules == null) return false;
            var position = PositionOf(vehicle);
            return property.Any(address => World.DistanceTo(address, position.x, position.z) <= rules.BayRadiusMetres);
        }

        // Each food vehicle's standing for the night check.
        private List<CommissaryVehicle> Flags(SaveState state, CommissaryTables sites, Dictionary<string, VehicleInstance> fleet, long now)
        {
            var game = SaveGameManager.Current;
            var withDriver = new HashSet<string>((game.privateDriverVehicleInstances ?? new List<VehicleInstance>()).Where(v => v != null).Select(v => v.id), StringComparer.Ordinal);
            var property = PlayerProperty();
            var list = new List<CommissaryVehicle>();
            foreach (var vehicle in fleet.Values.OrderBy(v => v.id, StringComparer.Ordinal))
            {
                var saved = StreetSales.Find(state, vehicle.id);
                var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
                var site = space == null ? null : sites.Site(space.CommissaryId);
                if (space != null && site == null && unknownSiteShown.Add(vehicle.id))
                    Log.Error($"Commissary: {World.Short(vehicle.id)} has its space at '{space.CommissaryId}', which commissaries.json doesn't have; it counts as out overnight.");
                bool? inBay = site == null || withDriver.Contains(vehicle.id) ? false : NoBay(site) ? true : InBayAnswer(vehicle, site, out _);
                list.Add(new CommissaryVehicle
                {
                    VehicleId = vehicle.id,
                    AtOwnCommissaryBay = inBay == true,
                    BaySideUnknown = inBay == null,
                    PlayerInside = game.ActiveVehicleId == vehicle.id || TruckWindow.PlayerAt(vehicle.id),
                    // Only a window the crew has open at a stop right now: a route that sold out and closed early is out like any other.
                    OnLateStop = saved != null && saved.IsOpen && saved.HireStop != null,
                    InTransit = withDriver.Contains(vehicle.id) || saved?.Transit != null,
                    AtPlayerProperty = !withDriver.Contains(vehicle.id) && AtProperty(vehicle, property)
                });
            }
            return list;
        }

        // "Food cart", "Food truck 2": as Econoview names them.
        private static string VehicleName(SaveState state, string vehicleId)
        {
            if (StreetFoodBooks.VehicleNames().TryGetValue(vehicleId, out var name)) return name;
            var kind = StreetSales.Find(state, vehicleId)?.VehicleType;
            return LocalizorManager.GetLocalization(kind == "truck" ? FoodTrucksMod.TruckTypeName : FoodTrucksMod.CartTypeName, null);
        }

        private static TransactionInfo RentInfo(string siteId)
        {
            var info = new TransactionInfo("foodtrucks:transaction_commissary_rent", new Dictionary<string, string> { { "commissary", NameOf(siteId) } });
            info.SetTaxDeductibleName(ExpenseLabel);
            return info;
        }

        internal static TransactionInfo OrderInfo(string siteId)
        {
            var info = new TransactionInfo("foodtrucks:transaction_commissary_order", new Dictionary<string, string> { { "commissary", NameOf(siteId) } });
            info.SetTaxDeductibleName(ExpenseLabel);
            return info;
        }

        // A fine is no business expense: no tax label.
        private static TransactionInfo FineInfo() => new TransactionInfo("foodtrucks:transaction_overnight_fine", new Dictionary<string, string>());

        // ---- The Commissary button on the vehicle's panel ----

        public static void ShowPanel(ItemPanelUI panel)
        {
            var options = PanelOptions(VehicleHelper.GetCurrentVehicle(), forPicker: true);
            if (options == null) return;
            var labels = options.Select(o => o.Label).ToList();
            DropdownSelector.Show(labels, chosen =>
            {
                try { options.FirstOrDefault(o => o.Label == chosen).Act?.Invoke(); }
                catch (Exception ex) { Log.Error("Commissary button: " + ex); }
            }, "foodtrucks:commissary_header", "foodtrucks:commissary_body");
        }

        // The test bridge's restock and rent_bay: the panel's checks and messages, then the first of its choices named in
        // `ops` ("rent", "move", "release", "restock", "nostock", "order"). Null when it ran one; otherwise why not.
        internal static string? RunPanelOption(VehicleInstance cart, params string[] ops)
        {
            var options = PanelOptions(cart, forPicker: false);
            if (options == null) return "the Commissary button refused (see the notice)";
            var choice = options.FirstOrDefault(o => ops.Contains(o.Op));
            if (choice.Act == null) return $"the Commissary button offers {string.Join(", ", options.Select(o => o.Op))} here, not {string.Join(" or ", ops)}";
            choice.Act();
            return null;
        }

        // The Commissary button's choices for the vehicle, after its checks (each refusal shows its notice); null when
        // it refused. forPicker: the game's list picker must be there to show them.
        private static List<(string Op, string Label, Action Act)>? PanelOptions(VehicleInstance? cart, bool forPicker)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            if (state == null || rules == null || cart == null || kind == null || uninstalled) return null;
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return null;
            }
            var site = SiteOf(cart);
            if (site == null)
            {
                // Within a bay's radius and on its door's side but not legally parked: say so, rather than "not here".
                var near = Sites?.Sites.FirstOrDefault(s => !InBay(cart, s, out var whyNot) && whyNot.StartsWith("not legally parked", StringComparison.Ordinal));
                CartSelling.Say(near != null ? "foodtrucks:notification_commissary_park_legally" : "foodtrucks:notification_commissary_not_here", kind);
                return null;
            }
            if (forPicker && DropdownSelector.onShow == null)
            {
                Log.Error("Commissary: the game's list picker isn't available in this scene.");
                CartSelling.Notify("foodtrucks:notification_prices_unavailable");
                return null;
            }
            Unlock(state, site, "vehicle at the bay");
            var id = cart.id;
            var siteId = site.Id;
            var options = new List<(string Op, string Label, Action Act)>();
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == id);
            var rent = CartSelling.Money(rules.RentPerMonthCents.TryGetValue(kind, out var monthly) ? monthly : 0);
            if (space == null)
                options.Add(("rent", Format("foodtrucks:commissary_option_rent", rent, Name(site)), () => Rent(id, kind, siteId)));
            else if (space.CommissaryId != siteId)
                options.Add(("move", Format("foodtrucks:commissary_option_move", NameOf(space.CommissaryId), Name(site), rent), () => Rent(id, kind, siteId)));
            else
                options.Add(("release", Format("foodtrucks:commissary_option_release", Name(site)), () => Release(id, kind, siteId)));
            var units = state.Commissary.Stock.TryGetValue(siteId, out var lines) ? lines.Sum(l => (long)l.Units) : 0;
            options.Add(units > 0
                ? ("restock", Format("foodtrucks:commissary_option_restock", units.ToString()), () => Restock(id, kind, siteId))
                : ("nostock", Format("foodtrucks:commissary_option_nostock", Name(site)), () => CartSelling.Say("foodtrucks:notification_commissary_nostock", kind, ("commissary", Name(site)))));
            if (state.Commissary.Spaces.Any(s => s.CommissaryId == siteId))
                options.Add(("order", Format("foodtrucks:commissary_option_order", Name(site)), () => CallToOrder(siteId)));
            return options;
        }

        // Renting a space here, or moving the vehicle's space here; its first day's rent is charged at once.
        private static void Rent(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var day = SaveGameManager.Current.Day;
            var before = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicleId)?.CommissaryId;
            if (!rules.RentPerMonthCents.TryGetValue(kind, out var monthly))
            {
                Log.Error($"Commissary: no rent for {kind} in rules.json.");
                return;
            }
            // Days not charged yet go to the old place first.
            ChargeRent(state, rules);
            Commissaries.Rent(state.Commissary, rules, vehicleId, kind, siteId, day);
            FoodTrucksSession.MarkChanged();
            ChargeRent(state, rules);
            if (before == null)
            {
                Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} rented a space on day {day}, {CartSelling.Money(monthly)} a month ({CartSelling.Money(rules.DailyRentCents(kind))} a day).");
                CartSelling.Say("foodtrucks:notification_commissary_rented", kind, ("commissary", NameOf(siteId)), ("rent", CartSelling.Money(monthly)));
            }
            else
            {
                Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} moved its space here from {before} on day {day}.");
                CartSelling.Say("foodtrucks:notification_commissary_moved", kind, ("commissary", NameOf(siteId)), ("from", NameOf(before)));
            }
        }

        private static void Release(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            // Core wants the rent settled before a space goes.
            ChargeRent(state, rules);
            if (!Commissaries.Release(state.Commissary, vehicleId)) return;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} gave up its space on day {SaveGameManager.Current.Day}.");
            CartSelling.Say("foodtrucks:notification_commissary_released", kind, ("commissary", NameOf(siteId)));
        }

        // What one restock loaded: the units per item, the foods nothing on board holds, the units the cargo refused and
        // kept here, and the units left in stock here.
        private sealed class Loaded
        {
            public Dictionary<string, int> Items { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
            public IReadOnlyList<string> NotHeld { get; set; } = Array.Empty<string>();
            public long Back { get; set; }
            public long Left { get; set; }
        }

        // Loads the commissary's stock into the vehicle (storage per piece; plan, "Storage per piece"). Equipment first,
        // the spare pieces that fit (an Extra Storage only if the vehicle has none), so a piece fitted here already holds food. Then Core's
        // Storage.Restock plans the food: each fitted piece is filled from its foods, a shared piece split evenly by
        // weight and topped up from the other food. Foods with no fitted piece stay here. Whatever doesn't go in goes
        // back to the commissary's stock in its place. The Restock button and the crew (CrewRestock) both load this way;
        // the crew loads food only (fitPieces false): fitting equipment is the player's choice.
        private static Loaded LoadFromStock(SaveState state, FoodTrucks.Core.Demand.DemandTables tables, VehicleInstance vehicle, string kind, string siteId,
            bool fitPieces = true)
        {
            var result = new Loaded();
            // Only spare pieces that fit (Core's Storage.FitPieces): never a second Extra Storage, never past the slots,
            // so a Restock shows no refusal for a piece it chose to leave here.
            var pieces = fitPieces ? Storage.FitPieces(tables, kind, CargoLimit.Lines(vehicle), InStock(state, siteId)) : new Dictionary<string, int>();
            if (pieces.Count > 0) result.Back += Load(state, vehicle, siteId, pieces, result.Items);
            var foods = InStock(state, siteId).Where(p => tables.EquipmentForGameItem(p.Key) == null)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var plan = Storage.Restock(tables, kind, CargoLimit.Lines(vehicle), foods);
            if (plan.Load.Count > 0) result.Back += Load(state, vehicle, siteId, plan.Load, result.Items);
            result.NotHeld = plan.NotHeld;
            FoodTrucksSession.MarkChanged();
            if (result.Items.Count > 0 || result.Back > 0)
            {
                try { GameEvent.Invoke("ba:gameevent_itemcargochanged"); }
                catch (Exception ex) { Log.Error("Commissary restock, cargo event: " + ex); }
            }
            result.Left = state.Commissary.Stock.TryGetValue(siteId, out var rest) ? rest.Sum(x => (long)x.Units) : 0;
            return result;
        }

        private static void Restock(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Tables;
            if (state == null || tables == null || !FoodTrucksSession.CanWrite) return;
            var vehicle = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == vehicleId);
            if (vehicle == null || !state.Commissary.Stock.TryGetValue(siteId, out var stock) || stock.Count == 0) return;

            var result = LoadFromStock(state, tables, vehicle, kind, siteId);
            var loaded = result.Items;
            var left = result.Left;
            state.Commissary.Stock.TryGetValue(siteId, out var rest);
            var view = Storage.Of(tables, kind, CargoLimit.Lines(vehicle));
            Log.Info($"Commissary restock: {kind} {World.Short(vehicleId)} at {siteId} loaded {(loaded.Count == 0 ? "nothing" : string.Join(", ", loaded.Select(p => $"{p.Value} {p.Key}")))}; " +
                     (result.NotHeld.Count == 0 ? "" : $"no piece for {string.Join(", ", result.NotHeld)}; ") + $"{result.Back} units refused by the cargo and kept; " +
                     $"{left} units left in stock; storage {(view == null ? "?" : StoragePanel.Summary(vehicle, view))}.");
            // Fitted pieces are named first ("fitted Griddle"), then the foods with their counts.
            var fitted = loaded.Where(p => tables.EquipmentForGameItem(p.Key) != null)
                .Select(p => (p.Value > 1 ? p.Value + " " : "") + LocalizorManager.GetLocalization(p.Key, null)).ToList();
            var foodText = string.Join(", ", FoodParts(tables, vehicle, view, loaded, rest));
            var text = fitted.Count == 0 ? foodText
                : LocalizorManager.GetLocalization("foodtrucks:commissary_restock_fitted", null).Replace("{pieces}", string.Join(", ", fitted))
                  + (foodText.Length > 0 ? "; " + foodText : "");
            var notHeld = string.Join(", ", result.NotHeld.Select(i => LocalizorManager.GetLocalization(i, null)));
            // Foods that stayed here because their piece is full, "Tacos: the Griddle is full (160/160)": each full piece
            // none of whose foods went in this time (a piece that took some says so in the loaded list). 0.9.8 named only
            // the foods nothing held and left a full Griddle unexplained (agent run, 11 October 2026).
            var fullParts = view == null ? new List<string>()
                : view.Pools.Where(p => Capped(tables, p, rest) && !p.Piece.Products.Any(loaded.ContainsKey)).Select(p => PieceFull(vehicle, p, rest)).ToList();
            var full = fullParts.Count == 0 ? "" : " " + string.Join("; ", fullParts) + ".";
            if (fullParts.Count > 0) Log.Info($"Commissary restock: full pieces:{full}");
            if (loaded.Count == 0 && result.NotHeld.Count == 0 && fullParts.Count > 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_full_why", kind, ("full", full), ("left", left.ToString()));
            else if (loaded.Count == 0 && result.NotHeld.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_full", kind, ("left", left.ToString()));
            else if (loaded.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_notheld", kind, ("notheld", notHeld), ("full", full), ("left", left.ToString()));
            else if (result.NotHeld.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restocked", kind, ("loaded", text), ("full", full), ("left", left.ToString()));
            else
                CartSelling.Say("foodtrucks:notification_commissary_restocked_notheld", kind, ("loaded", text), ("notheld", notHeld), ("full", full), ("left", left.ToString()));
        }

        // ---- Crew restock (plan, "Commissary supply and crew restock") ----

        // The commissary a crewed vehicle can restock at where it stands: the one where it rents its space, when it is in
        // that bay (the Restock button's test). Whether anything there would load doesn't matter: the restock step also
        // finds what the commissary was short of and texts it (review round 1, Astra and Sol). Null otherwise.
        internal static string? CrewRestockSite(SaveState state, VehicleInstance vehicle)
        {
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
            var site = space == null ? null : Sites?.Site(space.CommissaryId);
            if (!attached || uninstalled || DemandData.Tables == null || site == null) return null;
            return InBay(vehicle, site, out _) ? site.Id : null;
        }

        // The route's stops in the bay of the commissary where the vehicle rents its space (within the bay radius of a
        // resolved bay point, on the door's side): such a stop is reached only in the bay, and a crew with nothing to sell
        // may still drive to it to restock (Core VehicleWhere.BayStops, review rounds 2 and 3 of the balance pass). An
        // unknown street side counts as not in it and isn't cached, so it is read again next time. Cached per vehicle
        // while its commissary, bay and stops stay the same.
        internal static IReadOnlyCollection<int> CrewBayStops(SaveState state, VehicleState vehicle)
        {
            var stops = vehicle.Schedule?.Stops;
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.VehicleId);
            var site = space == null ? null : Sites?.Site(space.CommissaryId);
            var rules = Rules;
            var points = site == null ? null : ResolvedBays(site);
            if (!attached || uninstalled || stops == null || rules == null || points == null || site == null) return Array.Empty<int>();
            var key = site.Id + "|" + string.Join(";", points.Select(p => $"{p.X:F1},{p.Z:F1}")) + "|" + string.Join(";", stops.Select(st => $"{st.X:F1},{st.Z:F1}"));
            if (bayStopCache.TryGetValue(vehicle.VehicleId, out var cached) && cached.Key == key) return cached.Stops;
            var result = new List<int>();
            var known = true;
            for (var i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (!CommissaryBays.WithinRadius(points, stop.X, stop.Z, rules.BayRadiusMetres)) continue;
                var side = CommissaryBays.OnDoorSide(points, stop.X, stop.Z, rules.BayRadiusMetres, p => LanesFor(p, (float)rules.BayRadiusMetres));
                if (side == null) known = false;
                if (side == true) result.Add(i);
            }
            if (known) bayStopCache[vehicle.VehicleId] = (key, result);
            return result;
        }

        private static readonly Dictionary<string, (string Key, IReadOnlyCollection<int> Stops)> bayStopCache = new Dictionary<string, (string, IReadOnlyCollection<int>)>();

        // Where a crew parks a vehicle arriving at a stop in its own bay: the site's resolved bay points, each lined up
        // with its lane and the hatch to the pavement (a nose-in bay in a lot as measured), then places along the bay's
        // paint nearest the first point, as the test bridge's bay teleport does. The caller tries them in order, takes
        // the first free one and keeps it only if the vehicle then stands in the bay by InBay. Every place along the paint
        // is listed (every metre), not a first few, so a bay with a truck in its middle still offers its free ends; each
        // zone's facing is probed once.
        internal static List<(Vector3 Position, Quaternion Rotation)> BayPlaces(CommissarySite site, float halfLength)
        {
            var points0 = ResolvedBays(site);
            var cacheKey = site.Id + "|" + halfLength.ToString("F2") + "|" + (points0 == null ? "" : string.Join(";", points0.Select(p => $"{p.X:F1},{p.Z:F1}")));
            if (bayPlaceCache.TryGetValue(site.Id, out var cachedPlaces) && cachedPlaces.Key == cacheKey) return cachedPlaces.Places;
            var built = BuildBayPlaces(site, halfLength);
            if (built.Count > 0) bayPlaceCache[site.Id] = (cacheKey, built);
            return built;
        }

        private static readonly Dictionary<string, (string Key, List<(Vector3, Quaternion)> Places)> bayPlaceCache =
            new Dictionary<string, (string, List<(Vector3, Quaternion)>)>();

        // The part of the in-bay test that doesn't depend on where the vehicle stands: the bay resolved, and the street's
        // side readable at its first point (InBayAnswer gives no answer while the driving lanes round the door aren't read).
        internal static bool BayReady(CommissarySite site, out string why)
        {
            var rules = Rules;
            var points = ResolvedBays(site);
            if (rules == null || points == null || points.Count == 0) { why = "bay not resolved"; return false; }
            var p = points[0];
            if (CommissaryBays.OnDoorSide(points, p.X, p.Z, rules.BayRadiusMetres, q => LanesFor(q, (float)rules.BayRadiusMetres)) == null)
            {
                why = "the street's side can't be checked yet";
                return false;
            }
            why = "";
            return true;
        }

        private static List<(Vector3 Position, Quaternion Rotation)> BuildBayPlaces(CommissarySite site, float halfLength)
        {
            var result = new List<(Vector3, Quaternion)>();
            var points = ResolvedBays(site);
            var rules = Rules;
            if (points == null || points.Count == 0 || rules == null) return result;
            Quaternion Facing(Vector3 at, Vector3 along)
            {
                if (along.sqrMagnitude < 1e-6f) along = Vector3.forward;
                along.Normalize();
                if (site.InLot) return Quaternion.LookRotation(along);
                if (HatchCheck.Probe(at, Quaternion.LookRotation(along)).Opens) return Quaternion.LookRotation(along);
                if (HatchCheck.Probe(at, Quaternion.LookRotation(-along)).Opens) return Quaternion.LookRotation(-along);
                return Quaternion.LookRotation(along);
            }
            Vector3? GroundAt(float x, float z) =>
                Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out var hit, 1000f, LayerHelper.groundLayerMask, QueryTriggerInteraction.Ignore)
                    ? hit.point : (Vector3?)null;
            foreach (var p in points)
            {
                var at = GroundAt((float)p.X, (float)p.Z);
                if (at != null) result.Add((at.Value, Facing(at.Value, new Vector3((float)p.AlongX, 0f, (float)p.AlongZ))));
            }
            var first = result.Count > 0 ? result[0].Item1 : new Vector3((float)points[0].X, 0f, (float)points[0].Z);
            var along = new List<(Vector3, Quaternion)>();
            foreach (var zone in BayMarkers.ZonesOf(site, (float)rules.BayRadiusMetres))
            {
                Quaternion? facing = null;
                foreach (var (from, to) in zone.Spans)
                    for (var t = from + halfLength; t <= to - halfLength + 1e-3f; t += 1f)
                    {
                        var foot = zone.Foot + zone.Along * t;
                        var at = GroundAt(foot.x, foot.z);
                        if (at == null) continue;
                        facing ??= Facing(at.Value, zone.Along);
                        along.Add((at.Value, facing.Value));
                    }
            }
            result.AddRange(along.OrderBy(a => (a.Item1 - first).sqrMagnitude));
            return result;
        }

        // The commissary's stock of the vehicle's foods, as Core's key for "did it change since the crew last tried".
        internal static string? CrewStockKey(SaveState state, VehicleInstance vehicle, string kind, string siteId)
        {
            var tables = DemandData.Tables;
            return tables == null ? null : FoodTrucks.Core.Fleet.CrewRestock.StockKey(tables, kind, CargoLimit.Lines(vehicle), InStock(state, siteId));
        }

        // The crew loads food with the Restock button's code before it sets off (HireRoutes, on Core's Restock step),
        // never spare equipment. The log says what went in; when a fitted piece still has room and the commissary has
        // none of its foods (an empty commissary included), the commissary texts what was missing, once per vehicle and
        // game day (Core CrewRestock).
        internal static void CrewRestock(SaveState state, VehicleState saved, VehicleInstance vehicle, string kind, string siteId, long now)
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var result = LoadFromStock(state, tables, vehicle, kind, siteId, fitPieces: false);
            var shortOf = FoodTrucks.Core.Fleet.CrewRestock.Short(tables, kind, CargoLimit.Lines(vehicle), InStock(state, siteId));
            var view = Storage.Of(tables, kind, CargoLimit.Lines(vehicle));
            Log.Info($"Crew restock: {kind} {World.Short(vehicle.id)} at {siteId} loaded " +
                     $"{(result.Items.Count == 0 ? "nothing" : string.Join(", ", result.Items.Select(p => $"{p.Value} {p.Key}")))}; " +
                     (result.NotHeld.Count == 0 ? "" : $"no piece for {string.Join(", ", result.NotHeld)}; ") +
                     (shortOf.Count == 0 ? "" : $"short of {string.Join(", ", shortOf)}; ") +
                     $"{result.Back} units refused by the cargo and kept; {result.Left} units left in stock; storage {(view == null ? "?" : StoragePanel.Summary(vehicle, view))}.");
            if (shortOf.Count == 0 || !FoodTrucks.Core.Fleet.CrewRestock.TellShort(saved, (int)(now / 1440) + 1)) return;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Crew restock: {World.Short(vehicle.id)} short at {siteId}; the commissary texts it (once today).");
            if (state.Commissary.Unlocked.Contains(siteId))
                Notices.SendText(ContactFor(siteId), "foodtrucks:message_commissary_crew_short", new Dictionary<string, string>
                {
                    { "vehicle", VehicleName(state, vehicle.id) },
                    { "items", string.Join(", ", shortOf.Select(i => LocalizorManager.GetLocalization(i, null))) },
                    { "loaded", result.Items.Count == 0 ? LocalizorManager.GetLocalization("foodtrucks:commissary_crew_loaded_nothing", null)
                        : string.Join(", ", result.Items.Select(p => $"{p.Value} {LocalizorManager.GetLocalization(p.Key, null)}")) }
                });
        }

        // "Tacos, Chopped Cheese: the Griddle is full (160/160)": the piece's foods still in stock here.
        private static string PieceFull(VehicleInstance vehicle, StoragePool pool, List<CommissaryStockLine>? stock)
        {
            var foods = pool.Piece.Products.Where(p => stock != null && stock.Any(l => l.ItemName == p && l.Units > 0))
                .Select(p => LocalizorManager.GetLocalization(p, null));
            return LocalizorManager.GetLocalization("foodtrucks:commissary_restock_piece_full", null)
                .Replace("{foods}", string.Join(", ", foods)).Replace("{piece}", StoragePanel.PieceName(vehicle, pool.Piece))
                .Replace("{load}", pool.LoadUnits.ToString()).Replace("{capacity}", pool.Capacity.ToString());
        }

        // The foods loaded, "80 Hotdog (the Hotdog Grill holds 80)": a food whose piece is now full while more of its
        // foods stay in stock says why the load stopped there; for a shared piece the note follows the last of its foods.
        private static List<string> FoodParts(FoodTrucks.Core.Demand.DemandTables tables, VehicleInstance vehicle, StorageView? view,
            Dictionary<string, int> loaded, List<CommissaryStockLine>? stock)
        {
            var foods = loaded.Where(p => tables.EquipmentForGameItem(p.Key) == null).ToList();
            var parts = new List<string>();
            for (var i = 0; i < foods.Count; i++)
            {
                var text = $"{foods[i].Value} {LocalizorManager.GetLocalization(foods[i].Key, null)}";
                var pool = view?.PoolFor(tables, foods[i].Key);
                var lastOfPool = pool != null && !foods.Skip(i + 1).Any(f => view!.PoolFor(tables, f.Key)?.EquipmentId == pool.EquipmentId);
                if (pool != null && lastOfPool && Capped(tables, pool, stock)) text += " " + CapNote(vehicle, pool);
                parts.Add(text);
            }
            return parts;
        }

        // "(the Hotdog Grill holds 80)", or "(2 Hotdog Grills hold 160)".
        private static string CapNote(VehicleInstance vehicle, StoragePool pool) =>
            LocalizorManager.GetLocalization(pool.Pieces > 1 ? "foodtrucks:commissary_restock_capped_many" : "foodtrucks:commissary_restock_capped", null)
                .Replace("{piece}", StoragePanel.PieceName(vehicle, pool.Piece)).Replace("{count}", pool.Pieces.ToString())
                .Replace("{capacity}", pool.Capacity.ToString());

        // A piece is full for its foods still in stock here: not even one more of the lightest of them fits.
        private static bool Capped(FoodTrucks.Core.Demand.DemandTables tables, StoragePool pool, List<CommissaryStockLine>? stock)
        {
            if (stock == null) return false;
            var waiting = pool.Piece.Products.Where(p => stock.Any(l => l.ItemName == p && l.Units > 0)).ToList();
            if (waiting.Count == 0) return false;
            var lightest = waiting.Min(p => tables.Product(p)?.StockWeight ?? 1.0);
            return pool.Room + 1e-9 < lightest;
        }

        // What this commissary holds of each item.
        private static Dictionary<string, int> InStock(SaveState state, string siteId)
        {
            var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!state.Commissary.Stock.TryGetValue(siteId, out var stock)) return wanted;
            foreach (var line in stock) wanted[line.ItemName] = (int)Math.Min(int.MaxValue, (wanted.TryGetValue(line.ItemName, out var w) ? w : 0) + (long)line.Units);
            return wanted;
        }

        // Takes the wanted units out of stock, oldest first, loads them through TryToAddToCargo (so CargoLimit still has the
        // last word) and returns what didn't go in to its batch. Returns the units returned.
        private static long Load(SaveState state, VehicleInstance vehicle, string siteId, IReadOnlyDictionary<string, int> wanted,
            Dictionary<string, int> loaded)
        {
            var taken = Commissaries.Restock(state.Commissary, siteId, wanted);
            var back = new List<CommissaryStockLine>();
            foreach (var line in taken)
            {
                var moved = 0;
                if (ItemsGetter.GetByName(line.ItemName, suppressError: true) != null)
                {
                    var before = vehicle.GetAmountByItemName(line.ItemName);
                    try { vehicle.TryToAddToCargo(new CargoInstance(line.ItemName, line.Units, line.UnitPriceCents / 100f, paid: true)); }
                    catch (Exception ex) { Log.Error($"Commissary restock: loading {line.ItemName} threw: {ex}"); }
                    moved = Math.Max(0, Math.Min(line.Units, vehicle.GetAmountByItemName(line.ItemName) - before));
                }
                else Log.Error($"Commissary restock: {line.ItemName} isn't a game item (its mod removed?); it stays in stock.");
                if (moved > 0) loaded[line.ItemName] = (loaded.TryGetValue(line.ItemName, out var l) ? l : 0) + moved;
                if (moved < line.Units)
                    back.Add(new CommissaryStockLine
                    {
                        ItemName = line.ItemName, Units = line.Units - moved, UnitPriceCents = line.UnitPriceCents, BatchId = line.BatchId,
                        ExtensionData = line.ExtensionData, OrderMetadata = line.OrderMetadata
                    });
            }
            if (back.Count > 0) Commissaries.ReturnToStock(state.Commissary, siteId, back);
            return back.Sum(b => (long)b.Units);
        }

        private static string Format(string key, params string[] values) => string.Format(LocalizorManager.GetLocalization(key, null), values);

        // ---- Ordering (the commissary's phone contact) ----

        // What a commissary sells: economy.json's foods and the game items its equipment maps to, where the game has the
        // item (a mod's items only while it is installed) and a price. Foods first, then machines, each by name.
        internal static List<(string Item, int Box, long UnitCents)> Catalogue(bool machines)
        {
            var tables = DemandData.Tables;
            var list = new List<(string, int, long)>();
            if (tables == null) return list;
            var names = machines
                ? tables.EquipmentIds.Select(tables.Equipment).Where(e => e != null).SelectMany(e => e!.GameItems)
                : tables.ProductNames;
            foreach (var name in names.Distinct(StringComparer.Ordinal))
            {
                var item = ItemsGetter.GetByName(name, suppressError: true);
                if (item == null) continue;
                var price = item.GetWholesalePrice();
                // Furniture has no wholesale price: its shop price, with the difficulty's market multiplier as GetWholesalePrice applies to foods.
                if (price <= 0f) price = item.DefaultMarketPrice * (SaveGameManager.Current?.gameVariables.marketPriceMultiplier ?? 1f);
                var cents = (long)Math.Round(price * 100.0);
                if (cents <= 0) continue;
                // Equipment is sold by the piece, whatever box the game packs it in; food by the game's whole box.
                list.Add((name, machines ? 1 : Math.Max(1, item.boxSize), cents));
            }
            return list.OrderBy(i => LocalizorManager.GetLocalization(i.Item1, null), StringComparer.CurrentCulture).ToList();
        }

        // What the commissary's order form lists: the catalogue with what this commissary holds of each item.
        internal static List<CommissaryOrderForm.Line> FormLines(SaveState state, string siteId)
        {
            var stock = state.Commissary.Stock.TryGetValue(siteId, out var lines) ? lines : new List<CommissaryStockLine>();
            return Catalogue(false).Concat(Catalogue(true))
                .Select(c => new CommissaryOrderForm.Line(c.Item, c.Box, c.UnitCents, stock.Where(l => l.ItemName == c.Item).Sum(l => (long)l.Units)))
                .ToList();
        }

        // The bay's "Order stock": the phone opens on the commissary's conversation and calls it, so ordering happens
        // in one place, the conversation's order form.
        //
        // Not ContactsApp.OpenAndStartCall: it presses CALL one frame after opening the app, but the app shows the
        // conversation in a coroutine (ShowContactConversationIEnumerator) that waits a frame and then lays out the
        // history. When the call wins that race (the phone opened from the world, as here), the call's first entry, with
        // its Hang up and Order stock buttons, is placed before the history is laid out and ends up out of sight, and
        // the call can't be continued or hung up [code, 1.0; 0.9.6 playtest]. So the phone opens on the conversation
        // (the game's own OpenAppWithContact path through the app's OnEnable), and the call starts through the CALL
        // button's own path (ContactsApp.StartCall) once the conversation panel is up with this contact.
        private static void CallToOrder(string siteId)
        {
            HireRoutes.Later(() =>
            {
                try
                {
                    var contact = ContactFor(siteId);
                    Log.Info($"Commissary {siteId}: the bay's Order stock opens its conversation.");
                    SaveGameManager.Current.PlayerDefaults.contactsLastName = contact.id;
                    SaveGameManager.Current.PlayerDefaults.contactsLastCategoryName = contact.category;
                    // FullMenu.SelectApp turns every app off and the chosen one back on, so the app's OnEnable runs and
                    // opens contactsLastName even when Contacts was already showing; its OnDisable on the way cancels
                    // a call already running there. With unsaved BizMan settings ShowApp only asks first: Contacts
                    // isn't showing, so the pending call gives up quietly (below) and the player presses CALL after answering.
                    InstanceBehavior<UIs>.Instance.fullMenu.ShowApp(AppName.Contacts);
                    pendingCall = (siteId, contact, Time.frameCount, Time.unscaledTime + PendingCallSeconds);
                }
                catch (Exception ex)
                {
                    pendingCall = null;
                    Log.Error("Commissary order, opening the phone: " + ex);
                    CartSelling.Notify("foodtrucks:notification_commissary_call", ("commissary", NameOf(siteId)));
                }
            });
        }

        private const float PendingCallSeconds = 5f;
        private static (string SiteId, Contact Contact, int Frame, float Until)? pendingCall;
        // The frame in which the bay's "Order stock" last pressed CALL, for the hang-up log line.
        internal static int BayCallFrame = -1;
        private static readonly System.Reflection.FieldInfo? ConversationPanelField =
            typeof(ContactsApp).GetField("conversationPanel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Starts the bay's call once the phone shows the commissary's conversation: the panel is up with this contact,
        // which the app's coroutine sets in the same step that lays out the history, and a frame has passed since.
        private static void StartPendingCall()
        {
            if (pendingCall == null) return;
            var (siteId, contact, frame, until) = pendingCall.Value;
            try
            {
                var app = InstanceBehavior<UIs>.Instance?.fullMenu?.contactsApp;
                var panel = app == null ? null : ConversationPanelField?.GetValue(app) as GameObject;
                if (app != null && app.isActiveAndEnabled && app.selectedContact?.id == contact.id && panel != null && panel.activeSelf &&
                    Time.frameCount > frame + 1)
                {
                    pendingCall = null;
                    // Rare: switching apps cancelled any call in Contacts, but one may have started since.
                    if (DialogController.current != null)
                    {
                        Log.Info($"Commissary {siteId}: a call is already on; the bay's Order stock leaves the conversation open.");
                        return;
                    }
                    BayCallFrame = Time.frameCount;
                    app.StartCall();
                    Log.Info($"Commissary {siteId}: the bay's call started once the conversation was up ({Time.frameCount - frame} frames).");
                    return;
                }
                if (Time.unscaledTime < until && (app == null || app.isActiveAndEnabled || Time.frameCount <= frame + 2)) return;
                pendingCall = null;
                if (app != null && !app.isActiveAndEnabled)
                {
                    // The player's own doing: the phone closed, another app chosen, or BizMan's unsaved-settings question.
                    Log.Info($"Commissary {siteId}: the bay's call was dropped: Contacts isn't showing (the phone was closed or left for another app).");
                    return;
                }
                Log.Error($"Commissary {siteId}: the bay's call didn't start: the conversation never came up " +
                          $"(app {(app == null ? "missing" : app.isActiveAndEnabled ? "open" : "closed")}, panel field {(ConversationPanelField == null ? "missing" : "found")}).");
                CartSelling.Notify("foodtrucks:notification_commissary_call", ("commissary", NameOf(siteId)));
            }
            catch (Exception ex)
            {
                pendingCall = null;
                Log.Error("Commissary order, starting the call: " + ex);
                CartSelling.Notify("foodtrucks:notification_commissary_call", ("commissary", NameOf(siteId)));
            }
        }

        // A quote placed as Core's orders. No money moves now: each order is paid when it arrives (Deliver).
        internal static IReadOnlyList<CommissaryOrder> PlaceQuote(string siteId, CommissaryQuote quote)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || uninstalled || !FoodTrucksSession.CanWrite) return Array.Empty<CommissaryOrder>();
            var now = HireCrew.Now();
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            var orders = CommissaryOrdering.Place(state.Commissary, rules, siteId, quote, (day, minute), LimitsLifted);
            FoodTrucksSession.MarkChanged();
            foreach (var order in orders)
            {
                var boxes = quote.Lines.FirstOrDefault(l => l.ItemName == order.ItemName)?.Boxes ?? 0;
                Log.Info($"Commissary order {order.Id}: {boxes} boxes, {order.Units} {order.ItemName} at {siteId} for {CartSelling.Money(order.GoodsCents)} " +
                         $"plus {CartSelling.Money(order.FeeCents)} delivery, paid on delivery; due day {order.DeliverDay} at {CityPitches.Clock(rules.DeliveryMinute)}.");
            }
            return orders;
        }

        // A confirmed order that repeats: Core keeps it as a standing order, its first delivery tomorrow.
        internal static CommissaryStandingOrder? PlaceStanding(string siteId, CommissaryQuote quote, CommissaryRepeat repeat, int? firstDay = null)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || uninstalled || !FoodTrucksSession.CanWrite) return null;
            var now = HireCrew.Now();
            var standing = CommissaryStandingOrders.Create(state.Commissary, rules, siteId, quote, repeat, ((int)(now / 1440) + 1, (int)(now % 1440)), LimitsLifted, firstDay);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Commissary standing order {standing.Id} at {siteId}: {repeat}, {string.Join(", ", standing.Lines.Select(l => $"{l.Boxes} {l.ItemName}"))}, " +
                     $"first delivery day {standing.NextDay} at {CityPitches.Clock(rules.DeliveryMinute)}, paid on each delivery at that day's prices.");
            return standing;
        }

        internal static bool CancelStanding(string siteId, long id)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || !CommissaryStandingOrders.Cancel(state.Commissary, id)) return false;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Commissary standing order {id} at {siteId}: cancelled by phone.");
            return true;
        }

        // Why a quote doesn't fit a food's weekly limit, for the phone: "Hot dog: 2 boxes left in the week from Monday
        // (day 8), the order needs 3". Null when it fits.
        internal static string? OverLimit(SaveState state, CommissaryRules rules, string siteId, CommissaryQuote quote, CommissaryRepeat repeat, int? firstDay = null)
        {
            var today = (int)(HireCrew.Now() / 1440) + 1;
            var first = repeat == CommissaryRepeat.Once ? today + 1 : CommissaryStandingOrders.FirstDay(repeat, today, firstDay);
            var check = CommissarySupply.Check(state.Commissary, rules, siteId, quote, repeat, first - 1, LimitsLifted);
            if (check.Fits) return null;
            Log.Info($"Commissary order at {siteId}: {check.ItemName} over its weekly limit ({repeat}): {check.Left} boxes left in the week of day {check.WeekStart}, {check.Needed} needed.");
            return LocalizorManager.GetLocalization("foodtrucks:commissary_over_limit", null)
                .Replace("{item}", LocalizorManager.GetLocalization(check.ItemName, null)).Replace("{left}", check.Left.ToString())
                .Replace("{needed}", check.Needed.ToString()).Replace("{day}", check.WeekStart.ToString());
        }

        // The test bridge's fixture: the Commissary button's Rent for this vehicle at this commissary, without standing in
        // its bay (the commissary is unlocked first, as the first visit does). Null when the vehicle has its space there.
        internal static string? RentForTesting(string vehicleId, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var site = Sites?.Site(siteId);
            if (site == null) return $"no commissary {siteId}";
            if (state == null || Rules == null || uninstalled || !FoodTrucksSession.CanWrite)
                return "the commissary can't rent a space now (data not loaded, read-only or uninstalled)";
            var vehicle = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == vehicleId);
            var kind = FoodTrucksMod.KindOf(vehicle?.vehicleTypeName);
            if (kind == null) return $"no food cart or truck with id {vehicleId}";
            Unlock(state, site, "test bridge fixture");
            Rent(vehicleId, kind, siteId);
            return state.Commissary.Spaces.Any(s => s.VehicleId == vehicleId && s.CommissaryId == siteId) ? null : "the rent didn't happen (see the log)";
        }

        // The test bridge's order: the phone form's confirm without the call. The same Core quote and order code and log
        // lines; nothing is paid now (orders are paid on delivery). `items` are item keys or names with their boxes.
        // Returns the orders placed, or why none were.
        internal static (IReadOnlyList<CommissaryOrder> Orders, string? Refusal) OrderForTesting(string siteId, IEnumerable<(string Item, int Boxes)> items)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var site = Sites?.Site(siteId);
            if (state == null || rules == null || site == null || uninstalled || !FoodTrucksSession.CanWrite)
                return (Array.Empty<CommissaryOrder>(), site == null ? $"no commissary {siteId}" : "the commissary can't take orders now (data not loaded, read-only or uninstalled)");
            if (!CommissaryOrdering.CanOrder(state.Commissary, siteId))
                return (Array.Empty<CommissaryOrder>(), $"no space rented at {siteId} (or it isn't unlocked): rent a space first");
            var catalogue = Catalogue(false).Concat(Catalogue(true)).ToList();
            var requests = new List<CommissaryOrderRequest>();
            foreach (var (item, boxes) in items)
            {
                var line = catalogue.FirstOrDefault(c => c.Item1 == item);
                if (line.Item1 == null)
                    line = catalogue.FirstOrDefault(c => string.Equals(LocalizorManager.GetLocalization(c.Item1, null), item, StringComparison.OrdinalIgnoreCase));
                if (line.Item1 == null) return (Array.Empty<CommissaryOrder>(), $"{item} isn't on the commissary's list");
                requests.Add(new CommissaryOrderRequest(line.Item1, line.Item2, line.Item3, Math.Max(0, boxes), DemandData.Tables?.EquipmentForGameItem(line.Item1) == null));
            }
            var quote = CommissaryOrdering.Quote(rules, requests);
            if (quote.Problem == CommissaryQuoteProblem.Empty) return (Array.Empty<CommissaryOrder>(), "the order has no boxes");
            if (quote.Problem == CommissaryQuoteProblem.TooManyBoxes)
                return (Array.Empty<CommissaryOrder>(), $"more than {CommissaryOrdering.MaxBoxesPerOrder} boxes in one order");
            var over = OverLimit(state, rules, siteId, quote, CommissaryRepeat.Once);
            if (over != null) return (Array.Empty<CommissaryOrder>(), over);
            Log.Info($"Commissary order at {siteId}: read back {quote.Boxes} boxes ({string.Join(", ", quote.Lines.Select(l => $"{l.Boxes} {l.ItemName}"))}), " +
                     $"{CartSelling.Money(quote.GoodsCents)} plus {CartSelling.Money(quote.FeeCents)} delivery (test bridge).");
            var placed = PlaceQuote(siteId, quote);
            return (placed, placed.Count == 0 ? "Core placed no order" : null);
        }

        // ---- The uninstall step ----

        // Delivered stock is paid back at its goods price; open orders are cancelled with nothing to pay back, since they
        // are paid on delivery. Debts the game takes now are paid, the rest dropped, and the contacts go with the mod.
        public static void RemoveForUninstall()
        {
            // Set first: if the refund or a payment below throws, the mod's contacts still lose their protection
            // (ModContacts) and the bookings contact isn't added again in the morning (Catering.OnNewDay).
            uninstalled = true;
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state != null && rules != null && FoodTrucksSession.CanWrite)
            {
                var commissary = state.Commissary;
                var spaces = commissary.Spaces.Count;
                // Rent up to today belongs to the vehicles' time there.
                try { ChargeRent(state, rules); }
                catch (Exception ex) { Log.Error("Uninstall: commissary rent: " + ex); }
                // Stock a logistics plan brought was never paid under the commissary's label (LogisticsCommissary).
                var fromPlans = FoodTrucks.Core.Commissary.CommissaryLogistics.GoodsCents(commissary);
                var refund = Commissaries.Refund(commissary);
                var paidBack = false;
                if (refund.Total > 0)
                {
                    // The game's tax tracking ignores money coming in, so the deduction is taken back by hand below.
                    var info = new TransactionInfo("foodtrucks:transaction_commissary_refund", new Dictionary<string, string>());
                    paidBack = Payments.PayIn(refund.Total, info);
                    if (paidBack)
                    {
                        var reversed = ReverseDeduction(refund.Total - fromPlans);
                        Log.Info($"Uninstall: {CartSelling.Money(reversed)} of this tax year's commissary deduction taken back for the refund.");
                    }
                    else Log.Error($"Uninstall: the commissary refund of {CartSelling.Money(refund.Total)} was not paid.");
                }
                // Rent and fines still owed: paid now if the game takes them, else dropped.
                PayPending(state, rules, showNotification: false);
                if (commissary.Charges.Count > 0 || commissary.Fines.Count > 0)
                    Log.Error($"Uninstall: {commissary.Charges.Count} commissary charges and {commissary.Fines.Count} fines unpaid, dropped.");
                commissary.Charges.Clear();
                commissary.Fines.Clear();
                commissary.Unlocked.Clear();
                FoodTrucksSession.MarkChanged();
                Log.Info($"Uninstall: commissary refund {CartSelling.Money(paidBack ? refund.Total : 0)} for the stock delivered; {refund.CancelledOrders} open orders cancelled, unpaid; " +
                         $"{refund.CancelledStandingOrders} standing orders ended; {spaces} spaces given up.");
                if (paidBack) CartSelling.Notify(refund.CancelledStandingOrders > 0 ? "foodtrucks:notification_uninstall_commissary_standing" : "foodtrucks:notification_uninstall_commissary",
                    ("refund", CartSelling.Money(refund.Total)));
            }
            try { LogisticsCommissary.RemoveFromPlans(); }
            catch (Exception ex) { Log.Error("Uninstall: commissary destinations in logistics plans: " + ex); }
            CartButton.ShowCommissary(false);
            BayMarkers.RemoveAll();
            CommissaryOrderForm.Forget(Sites?.Sites.Select(s => ContactPrefix + s.Id) ?? Enumerable.Empty<string>());
            var removed = SaveGameManager.Current?.Contacts.RemoveAll(c => c != null && c.id != null && c.id.StartsWith(ContactPrefix, StringComparison.Ordinal)) ?? 0;
            if (removed > 0) Log.Info($"Uninstall: {removed} commissary contacts removed.");
        }

        // Takes a refunded amount off this tax year's deductions under the commissary label (TaxHelper keeps them per label
        // and values in currentTaxPeriodDeductibleExpenses; it only ever adds). Never below 0; returns the cents taken.
        // Rent shares the label, so an order paid in an earlier tax year can take this year's rent deduction down.
        internal static long ReverseDeduction(long cents)
        {
            var expenses = SaveGameManager.Current?.currentTaxPeriodDeductibleExpenses;
            if (expenses == null || cents <= 0) return 0;
            var left = cents / 100.0;
            foreach (var expense in expenses.Where(e => e != null && e.key == ExpenseLabel && e.amount > 0f))
            {
                var take = Math.Min(left, expense.amount);
                expense.amount = (float)Math.Max(0.0, expense.amount - take);
                left -= take;
                if (left <= 0) break;
            }
            return (long)Math.Round(cents - Math.Max(0.0, left) * 100.0);
        }

        // ---- Debug ----

        // Where each commissary's bay is from the player and the vehicle they are in, and the nearest one.
        // ---- The test bridge's commissary bay_candidates (docs/test-bridge.md) ----

        internal sealed class BayCandidate
        {
            public double X, Z, Heading, Distance;
            // The walk from the door's NavMesh point (null from a point set in data, or with no walk start).
            public string? Walk;
            public float? WalkMetres, StraightMetres;
            // Whether the straight walk from the door crosses no driving lane (Core BaySide); null with no door.
            public bool? SameSide;
            // The place the automatic search picks from this origin.
            public bool Automatic;
            // Why the automatic search passes this place over ("farther than the automatic pick" when it takes a nearer
            // one first); null for the place it takes (Automatic).
            public string? Rejected;
        }

        internal sealed class BayCandidateOrigin
        {
            public string Kind = "";
            public double X, Z;
            public Vector3? Door;
            public string WalkStart = "";
            // A point set in data: whether it is itself on the door's side (null for an entrance, which is the door).
            public bool? SameSide;
            public int Colliders;
            public bool Truncated;
            public List<BayCandidate> Candidates = new List<BayCandidate>();
        }

        // Legal truck spots (the game's wheel test, as BaySnap's) within radius of each search origin, nearest first, each
        // with its walk from the door, whether it is on the door's side of the street (Core BaySide) and why the automatic
        // search (BaySnap.MaxMetres, the side and walk rules) would pass it over. The origins are a commissary's (its
        // entrances, or the point set in data), or with an address any building's entrances, searched as a commissary's
        // would be: for scouting a new garage. Reads only; nothing is stored. Status is null for an address.
        internal static (string? Status, List<BayCandidateOrigin> Origins) BayCandidatesForTesting(string? siteId, string? address, double radius)
        {
            CommissarySite? site = null;
            List<(double X, double Z)> origins;
            if (siteId != null)
            {
                site = Sites?.Site(siteId) ?? throw new InvalidOperationException($"no commissary {siteId}");
                address = site.Address;
                origins = Origins(site) ?? throw new InvalidOperationException($"{siteId}'s building isn't in the city yet");
            }
            else
            {
                if (address == null) throw new InvalidOperationException("bay_candidates needs a site or an address");
                var entrances = World.EntrancesOf(address) ?? throw new InvalidOperationException($"no building at {address} in this city");
                if (entrances.Length == 0) throw new InvalidOperationException($"the building at {address} has no entrance");
                origins = entrances.Select(d => ((double)d.x, (double)d.z)).ToList();
            }
            var name = siteId ?? address!;
            var measured = site?.Bay != null;
            if (!Legality.ParkingSceneReady()) throw new InvalidOperationException("the city's parking lanes aren't loaded yet; try again in a few seconds");
            var started = Time.realtimeSinceStartup;
            string? status = null;
            if (site != null)
            {
                var resolved = ResolvedBays(site);
                status = resolved == null ? "pending" : resolved.Count == 0 ? "failed" : "resolved";
            }
            var doors = World.EntrancesOf(address!);
            var result = new List<BayCandidateOrigin>();
            foreach (var origin in origins)
            {
                var door = doors == null || doors.Length == 0 ? (Vector3?)null : doors.OrderBy(d => Flat(d, origin)).First();
                var entry = new BayCandidateOrigin { Kind = measured ? "data" : "entrance", X = origin.X, Z = origin.Z, Door = door };
                var centre = new Vector3((float)origin.X, door?.y ?? PlayerHelper.GetPosition().y, (float)origin.Z);
                // Wide enough for the automatic search's 30 m too, so its pick never changes with the listing radius
                // (review round 2, Astra and Sol).
                var probe = new Legality.ParkingProbe(centre, (float)Math.Max(radius, BaySnap.MaxMetres) + 8f);
                entry.Colliders = probe.Count;
                entry.Truncated = probe.Truncated;
                // The side rule's lanes, read for the whole listing radius.
                List<((double X, double Z) A, (double X, double Z) B)>? sideLanes = null;
                var laneless = false;
                if (door != null)
                {
                    var side = SideLanes(door.Value, origin, Math.Max(radius, BaySnap.MaxMetres));
                    sideLanes = side.Lanes ?? throw new InvalidOperationException(side.Why.Length > 0
                        ? "the traffic lanes couldn't be read (" + side.Why + "); try again in a few seconds"
                        : "the traffic system isn't ready to check the door's street; try again in a few seconds");
                    // No lane at all: the side is unknown, as the search treats it (it waits and tries again).
                    if (sideLanes.Count == 0)
                    {
                        laneless = true;
                        sideLanes = null;
                    }
                }
                bool? SameSideOf(double x, double z) =>
                    door == null || sideLanes == null ? (bool?)null : BaySide.SameSide(door.Value.x, door.Value.z, x, z, sideLanes);
                Vector3? walkStart = null;
                var cutOff = false;
                var measuredAcross = false;
                if (measured)
                {
                    entry.SameSide = SameSideOf(origin.X, origin.Z);
                    measuredAcross = entry.SameSide == false;
                    entry.WalkStart = "none (a point set in data is trusted for its walk; its side is checked from the nearest door)";
                }
                else if (door == null) entry.WalkStart = "none (no entrance)";
                else
                {
                    var from = WalkFrom(name, door.Value);
                    switch (from.Kind)
                    {
                        case WalkStart.Door: walkStart = from.Point; entry.WalkStart = "door"; break;
                        case WalkStart.Unknown: entry.WalkStart = "unknown (the door isn't on the NavMesh; walks aren't checked)"; break;
                        case WalkStart.CutOff: walkStart = from.Point; cutOff = true; entry.WalkStart = "cut off: " + from.Why; break;
                        default: throw new InvalidOperationException("the traffic system isn't ready to check the door's street; try again in a few seconds");
                    }
                }
                // The automatic pick is the bay search's own: Nearest within BaySnap.MaxMetres with the same rules (the side
                // from the door; the walk from the door's NavMesh point when it has a short walk to the street, none from a
                // point set in data or a door off the NavMesh), and nothing at all from a door cut off from the street, a
                // point set in data that is itself across the road, or an origin with no door.
                Func<double, double, bool>? reach = null;
                if (!cutOff && sideLanes != null)
                {
                    var doorX = (double)door!.Value.x;
                    var doorZ = (double)door.Value.z;
                    var lanes = sideLanes;
                    var reachFrom = walkStart;
                    reach = (x, z) => BaySide.SameSide(doorX, doorZ, x, z, lanes) && (reachFrom == null || WalkRule.Accepts(Walk(reachFrom.Value, x, z)));
                }
                var automatic = !cutOff && !measuredAcross && door != null && !laneless;
                var listed = BaySnap.CandidatesWithPick(origin.X, origin.Z, probe.Legal, probe.Axes, Legality.TruckFootprint(), radius, reach, automatic,
                    measured ? site!.BayHeading : null);
                var pick = listed.Automatic;
                foreach (var c in listed.Places)
                {
                    var candidate = new BayCandidate { X = c.X, Z = c.Z, Heading = Math.Atan2(c.AlongX, c.AlongZ) * 180 / Math.PI, Distance = c.Distance };
                    candidate.SameSide = SameSideOf(c.X, c.Z);
                    WalkVerdict? verdict = null;
                    if (walkStart != null)
                    {
                        var walk = WalkDetail(walkStart.Value, c.X, c.Z);
                        verdict = walk.Verdict;
                        candidate.Walk = walk.Verdict switch
                        {
                            WalkVerdict.Short => "short", WalkVerdict.TooLong => "too_long", WalkVerdict.NoRoute => "no_route", _ => "unknown"
                        };
                        if (walk.Verdict == WalkVerdict.Short || walk.Verdict == WalkVerdict.TooLong) candidate.WalkMetres = walk.Path;
                        if (walk.Verdict != WalkVerdict.Unknown) candidate.StraightMetres = walk.Straight;
                    }
                    if (ReferenceEquals(c, pick)) candidate.Automatic = true;
                    else if (c.Distance > BaySnap.MaxMetres + 1e-9) candidate.Rejected = $"beyond the automatic search's {BaySnap.MaxMetres:F0} m";
                    else if (door == null) candidate.Rejected = "no entrance to check the street's side from, so the automatic search takes nothing here";
                    else if (laneless) candidate.Rejected = "no driving lanes found round the door, so the street's side can't be checked and the automatic search waits";
                    else if (cutOff) candidate.Rejected = "the door has no short walk to the street, so the automatic search takes nothing from it";
                    else if (measuredAcross) candidate.Rejected = "the point set in data is across the road from the door, so the automatic search takes nothing from it";
                    else if (candidate.SameSide == false) candidate.Rejected = "across the road from the door";
                    else if (verdict == WalkVerdict.TooLong) candidate.Rejected = "too long a walk from the door";
                    else if (verdict == WalkVerdict.NoRoute) candidate.Rejected = "no walking route from the door";
                    else if (pick != null && c.Distance > pick.Distance) candidate.Rejected = "farther than the automatic pick";
                    else candidate.Rejected = "not taken by the automatic search (it passes over cells within 3 m of one it rejected for its side or walk)";
                    entry.Candidates.Add(candidate);
                }
                result.Add(entry);
            }
            Log.Info($"{(siteId != null ? "Commissary " + siteId : "Building " + address)}: bay candidates within {radius:F0} m (test bridge): " +
                     string.Join("; ", result.Select(o => $"from {o.Kind} x {o.X:F1}, z {o.Z:F1}: {o.Candidates.Count} legal, " +
                                                          $"{o.Candidates.Count(c => c.SameSide == true)} on the door's side, " +
                                                          $"{o.Candidates.Count(c => c.Automatic)} the automatic pick")) + $"; {(Time.realtimeSinceStartup - started) * 1000f:F0} ms.");
            return (status, result);
        }

        public static void WhereForTesting()
        {
            var sites = Sites;
            var rules = Rules;
            if (sites == null || rules == null)
            {
                CartSelling.Notify("foodtrucks:notification", ("message", "Commissaries not loaded; see Player.log."));
                return;
            }
            var player = PlayerHelper.GetPosition();
            var cart = VehicleHelper.GetCurrentVehicle();
            var vehicle = cart != null && FoodTrucksMod.KindOf(cart.vehicleTypeName) != null ? PositionOf(cart) : (Vector3?)null;
            var from = vehicle ?? player;
            var nearest = sites.Sites.OrderBy(s => CommissaryTables.Distance(ResolvedPoints(s), from.x, from.z)).First();
            foreach (var site in sites.Sites)
            {
                var bays = ResolvedBays(site);
                Log.Info($"Debug: commissary {site.Id} ({site.Address}) bay {(bays == null ? "not resolved" : bays.Count == 0 ? "none (no legal parking)" : string.Join("; ", bays.Select(b => $"x {b.X:F1}, z {b.Z:F1}")))}, " +
                         $"{CommissaryTables.Distance(ResolvedPoints(site), player.x, player.z):F0} m from the player" +
                         (vehicle != null ? $", {CommissaryTables.Distance(ResolvedPoints(site), vehicle.Value.x, vehicle.Value.z):F0} m from the vehicle" : "") + ".");
            }
            var distance = CommissaryTables.Distance(ResolvedPoints(nearest), from.x, from.z);
            // In the bay is the same test the 04:00 check uses: the vehicle legally parked and its middle within the radius.
            string standing;
            if (ResolvedBays(nearest) == null) standing = "bay not resolved yet";
            else if (cart == null || vehicle == null) standing = distance <= rules.BayRadiusMetres ? "on foot, within the bay's radius" : "outside the bay";
            else
            {
                var inBay = InBay(cart, nearest, out var why);
                standing = inBay ? "in the bay" : distance <= rules.BayRadiusMetres ? "outside the bay: " + why : "outside the bay";
            }
            Log.Info($"Debug: at x {from.x:F1}, z {from.z:F1}; nearest commissary {nearest.Id}, {distance:F0} m (bay radius {rules.BayRadiusMetres} m): {standing}.");
            CartSelling.Notify("foodtrucks:notification", ("message", $"Nearest commissary: {Name(nearest)}, {distance:F0} m ({standing})."));
        }

        // Orders on the way arrive now and are paid, as if it were their 08:00, and a standing order's delivery due by
        // tomorrow comes too (throwaway saves).
        public static void DeliverForTesting()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var now = HireCrew.Now();
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            // Everything is delivered today, never on a day before day 1 (Sol, review round 1: day 1 before 08:00 made day 0).
            var count = 0;
            foreach (var order in state.Commissary.Orders.Where(o => o.DeliverDay > day))
            {
                order.DeliverDay = day;
                count++;
            }
            // A standing order's delivery due today or tomorrow comes now; its schedule goes on from its own day, so no
            // delivery is added and none of its days change (Core BookEarly).
            var early = 0;
            var prices = TodaysPrices();
            foreach (var standing in state.Commissary.StandingOrders.Where(o => o.NextDay >= day && o.NextDay <= day + 1).OrderBy(o => o.Id).ToList())
            {
                var run = CommissaryStandingOrders.BookEarly(state.Commissary, rules, standing, day, minute, prices, LimitsLifted);
                TellRun(state, run);
                early++;
            }
            if (early > 0) FoodTrucksSession.MarkChanged();
            // Delivered (and so removed) in the same step, before any save could see the moved days.
            Deliver(state, rules, day, Math.Max(minute, rules.DeliveryMinute));
            Log.Info($"Debug: {count} commissary orders brought forward and {early} standing deliveries due by tomorrow booked early; all delivered now (paid on delivery).");
            CartSelling.Notify("foodtrucks:notification", ("message", $"Debug: {count} orders and {early} standing deliveries brought forward; see the commissary's texts."));
        }

        // What the 04:00 check would say now, without fining anyone.
        public static void NightForTesting()
        {
            var state = FoodTrucksSession.CurrentState;
            var sites = Sites;
            if (state == null || sites == null || instance == null) return;
            var flags = instance.Flags(state, sites, Fleet(), HireCrew.Now());
            var fined = flags.Count(v => !(v.PlayerInside || v.OnLateStop || v.InTransit || v.AtPlayerProperty || (v.AtOwnCommissaryBay && state.Commissary.Spaces.Any(s => s.VehicleId == v.VehicleId))));
            var message = $"Overnight check now (dry run): {(flags.Count == 0 ? "no food vehicles" : string.Join("; ", flags.Select(Describe)))}; {fined} would be fined.";
            Log.Info("Debug: " + message);
            CartSelling.Notify("foodtrucks:notification", ("message", message));
        }

        private static Commissary? instance;
        private void OnEnable() => instance = this;

        private void OnDisable()
        {
            if (instance == this) instance = null;
        }
    }

    // A commissary's call, all in its conversation: what it holds for you, then "Order stock" opens the order form
    // (CommissaryOrderForm), the commissary reads the order back with its total, and "Confirm order" places it.
    internal sealed class CommissaryDialog : Dialog
    {
        // Whether the call now on was started by the bay's "Order stock" (its StartCall runs this constructor in the same frame).
        private static bool startedFromBay;

        public CommissaryDialog()
        {
            startedFromBay = Commissary.BayCallFrame == Time.frameCount;
            try { DialogController.current.ShowEntry(Start()); }
            catch (Exception ex)
            {
                Log.Error("Commissary call: " + ex);
                try { DialogController.current?.CancelDialog(); }
                catch (Exception inner) { Log.Error("Commissary call, hanging up: " + inner); }
            }
        }

        private static DialogController Current => DialogController.current;

        private static Func<DialogEntry> Guard(Func<DialogEntry?> step) => () =>
        {
            try { return step()!; }
            catch (Exception ex)
            {
                Log.Error("Commissary call: " + ex);
                try { return Closing("foodtrucks:dialog_commissary_unavailable", null); }
                catch (Exception inner)
                {
                    Log.Error("Commissary call, closing: " + inner);
                    return null!;
                }
            }
        };

        // Hanging up, from any step.
        private static void HangUp()
        {
            CommissaryOrderForm.End("hung up");
            var id = Current?.contact?.id;
            var over = PhoneCall.HangUp();
            Log.Info($"Commissary call: {id ?? "?"} hung up ({(startedFromBay ? "started from the bay" : "started with CALL")}); " +
                     $"{(over ? "the call is over, CALL is ready" : "a call is still on")}.");
        }

        private static TextMessage HangUpMessage() => new TextMessage("ba:messagetype_contacts_message_player_cancel_call");

        // The commissary this contact belongs to.
        private static CommissarySite? Site()
        {
            var id = Current?.contact?.id;
            if (id == null || !id.StartsWith(Commissary.ContactPrefix, StringComparison.Ordinal)) return null;
            return DemandData.Commissaries?.Site(id.Substring(Commissary.ContactPrefix.Length));
        }

        // Null when this commissary takes an order now, else the closing line saying why not.
        // forOrders false: only viewing or cancelling standing orders, which needs no space (review round 1, Astra: a
        // standing order outlives the last space, and must stay cancellable).
        private static DialogEntry? Refusal(CommissarySite? site, bool forOrders = true)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || site == null || DemandData.Rules?.Commissary == null || !FoodTrucksSession.CanWrite || Commissary.Uninstalled)
                return Closing("foodtrucks:dialog_commissary_unavailable", null);
            if (!forOrders) return null;
            if (!CommissaryOrdering.CanOrder(state.Commissary, site.Id))
                return Closing("foodtrucks:dialog_commissary_no_space", new { commissary = Commissary.Name(site) });
            if (!CommissaryOrderForm.Ready)
            {
                Log.Error("Commissary call: no order form (see the Commissary orders line at startup).");
                return Closing("foodtrucks:dialog_commissary_unavailable", null);
            }
            return null;
        }

        private static DialogEntry Start()
        {
            var site = Site();
            // Without a space here the call still shows the standing orders, to cancel them; with none, it says why not.
            var refusal = Refusal(site, forOrders: site == null || FoodTrucksSession.CurrentState == null ||
                                               CommissaryStandingOrders.At(FoodTrucksSession.CurrentState.Commissary, site.Id).Count == 0);
            if (refusal != null) return refusal;
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            if (!CommissaryOrdering.CanOrder(commissary, site!.Id))
            {
                var nospace = new { commissary = Commissary.Name(site), standing = CommissaryStandingOrders.At(commissary, site.Id).Count.ToString() };
                Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_start_nospace", ToDictionary(nospace), read: true, isNewInteraction: true));
                return Menu(site.Id, LocalizorManager.Localize("foodtrucks:dialog_commissary_start_nospace", nospace));
            }
            var data = new
            {
                commissary = Commissary.Name(site!),
                spaces = commissary.Spaces.Count(s => s.CommissaryId == site!.Id).ToString(),
                stock = (commissary.Stock.TryGetValue(site!.Id, out var lines) ? lines.Sum(l => (long)l.Units) : 0).ToString(),
                orders = commissary.Orders.Count(o => o.CommissaryId == site.Id).ToString(),
                standing = CommissaryStandingOrders.At(commissary, site.Id).Count.ToString()
            };
            // A new key for the text with standing orders: the phone words old messages again from their key and data.
            Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_start_standing", ToDictionary(data), read: true, isNewInteraction: true));
            return Menu(site.Id, LocalizorManager.Localize("foodtrucks:dialog_commissary_start_standing", data));
        }

        // The call's menu: Order stock (with a space here), and the standing orders when there are any.
        private static DialogEntry Menu(string siteId, Localizor.LanguageChangeEvent.LanguageChangeEventDataHolder message)
        {
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            var standing = CommissaryStandingOrders.At(commissary, siteId).Count;
            if (!CommissaryOrdering.CanOrder(commissary, siteId))
                return new DialogEntry
                {
                    messageData = message,
                    Template = DialogEntry.TemplateType.Text,
                    OnConfirm = Guard(() => Standing(siteId, 0)),
                    ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_standing_list", (object?)null),
                    OnCancel = HangUp,
                    onCancelMessage = HangUpMessage()
                };
            return new DialogEntry
            {
                messageData = message,
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Form(siteId)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_stock", (object?)null),
                OnSecondOption = standing > 0 ? Guard(() => Standing(siteId, 0)) : null!,
                SecondOptionTextOverride = standing > 0 ? "foodtrucks:dialog_commissary_standing_list" : null!,
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // The standing orders, one at a time: what it delivers, how often and when next. Next (or Done on the last, back to
        // the menu), or cancel it.
        private static DialogEntry? Standing(string siteId, int index)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site, forOrders: false);
            if (refusal != null) return refusal;
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            var list = CommissaryStandingOrders.At(commissary, siteId);
            if (list.Count == 0 || index >= list.Count)
                return list.Count == 0 && !CommissaryOrdering.CanOrder(commissary, siteId)
                    ? Closing("foodtrucks:dialog_commissary_standing_none_nospace", new { commissary = Commissary.Name(site!) })
                    : Menu(siteId, LocalizorManager.Localize("foodtrucks:dialog_commissary_standing_done", new { count = list.Count.ToString() }));
            var standing = list[index];
            var rules = DemandData.Rules!.Commissary!;
            var data = new
            {
                index = (index + 1).ToString(),
                count = list.Count.ToString(),
                repeat = Commissary.StandingRepeat(standing),
                items = Commissary.StandingItems(standing),
                day = standing.NextDay.ToString(),
                weekday = CityPitches.Weekday(standing.NextDay),
                time = CityPitches.Clock(rules.DeliveryMinute)
            };
            Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_standing_one", ToDictionary(data), read: true));
            var id = standing.Id;
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_standing_one", data),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Standing(siteId, index + 1)),
                ConfirmTextOverride = LocalizorManager.Localize(index + 1 < list.Count ? "foodtrucks:dialog_commissary_standing_next" : "foodtrucks:dialog_commissary_standing_back", (object?)null),
                OnSecondOption = Guard(() => CancelStanding(siteId, id, index)),
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_standing_cancel",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        private static DialogEntry? CancelStanding(string siteId, long id, int index)
        {
            var standing = CommissaryStandingOrders.At(FoodTrucksSession.CurrentState!.Commissary, siteId).FirstOrDefault(s => s.Id == id);
            if (standing != null && Commissary.CancelStanding(siteId, id))
                Notices.Send(Current.contact, new TextMessage("foodtrucks:message_commissary_standing_cancelled", new Dictionary<string, string>
                {
                    { "repeat", Commissary.StandingRepeat(standing) },
                    { "items", Commissary.StandingItems(standing) }
                }, read: true));
            // The next one moves up into this place.
            return Standing(siteId, index);
        }

        // The game's phone order form, filled with what the commissary sells (CommissaryOrderForm).
        private static DialogEntry? Form(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var lines = Commissary.FormLines(FoodTrucksSession.CurrentState!, siteId);
            if (lines.Count == 0)
            {
                Log.Error($"Commissary order at {siteId}: nothing to sell (no catalogue item is a game item with a price).");
                return Closing("foodtrucks:dialog_commissary_nothing", null);
            }
            CommissaryOrderForm.Begin(Current.contact.id, siteId, lines);
            var weekly = Commissary.LimitsLifted ? "off (the game's wholesale limits are switched off)"
                : string.Join(", ", lines.Where(l => DemandData.Tables?.EquipmentForGameItem(l.Item) == null)
                    .Select(l => $"{l.Item} {CommissarySupply.Left(FoodTrucksSession.CurrentState!.Commissary, DemandData.Rules!.Commissary!, siteId, l.Item, TimeHelper.CurrentDay + 1)}"));
            Log.Info($"Commissary order at {siteId}: the order form lists {lines.Count} items; boxes left this week by food: {weekly}.");
            return new DialogEntry
            {
                headerKey = "foodtrucks:dialog_commissary_order_header",
                Template = DialogEntry.TemplateType.Input,
                InputTemplate = DialogEntry.InputTemplateName.FurnitureDeliverySettings,
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_check", (object?)null),
                OnConfirm = Guard(() => FormSet(siteId)),
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // The form sent: the commissary reads the order back with its total, paid on delivery. Nothing is booked yet.
        private static DialogEntry? FormSet(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            var form = Current.GetInputComponent<UI.Dialog.FurnitureDeliveryContractSettings>();
            var quote = CommissaryOrdering.Quote(rules, CommissaryOrderForm.Requests(form));
            switch (quote.Problem)
            {
                case CommissaryQuoteProblem.Empty:
                    CartSelling.Notify("foodtrucks:notification_commissary_order_empty");
                    return null;
                case CommissaryQuoteProblem.TooManyBoxes:
                    CartSelling.Notify("foodtrucks:notification_commissary_order_too_many", ("max", CommissaryOrdering.MaxBoxesPerOrder.ToString()));
                    return null;
            }
            // A food over its weekly limit for tomorrow's week: refused, the form stays to change it.
            var over = Commissary.OverLimit(FoodTrucksSession.CurrentState!, rules, siteId, quote, CommissaryRepeat.Once);
            if (over != null)
            {
                CartSelling.Notify("foodtrucks:notification", ("message", over));
                return null;
            }
            // The order is read back as it stands now: the form and its item list are done with ("Change" opens a fresh one).
            CommissaryOrderForm.End("read back");
            var items = string.Join("<br>", quote.Lines.Select(LineText));
            Notices.SendFromPlayer(Current.contact, new TextMessage("foodtrucks:message_commissary_order_player", new Dictionary<string, string> { { "items", items } }, read: true));
            var data = new
            {
                items,
                goods = CartSelling.Money(quote.GoodsCents),
                fee = CartSelling.Money(quote.FeeCents),
                total = CartSelling.Money(quote.TotalCents),
                time = CityPitches.Clock(rules.DeliveryMinute)
            };
            Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_quote", ToDictionary(data), read: true));
            Log.Info($"Commissary order at {siteId}: read back {quote.Boxes} boxes ({string.Join(", ", quote.Lines.Select(l => $"{l.Boxes} {l.ItemName}"))}), " +
                     $"{CartSelling.Money(quote.GoodsCents)} plus {CartSelling.Money(quote.FeeCents)} delivery.");
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_quote", data),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => HowOften(siteId, quote)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_confirm", (object?)null),
                OnSecondOption = Guard(() => Form(siteId)),
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_order_change",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // Confirmed: once, or repeating (a standing order, Core CommissaryStandingOrders). Just once places it as before;
        // Repeat asks every day or every week on tomorrow's weekday. `message` replaces the question (an over-limit
        // refusal of a repeat, so Just once stays within reach).
        private static DialogEntry? HowOften(string siteId, CommissaryQuote quote, string? message = null)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            return new DialogEntry
            {
                messageData = message == null
                    ? LocalizorManager.Localize("foodtrucks:dialog_commissary_how_often", (object?)null)
                    : LocalizorManager.Localize("foodtrucks:dialog_commissary_text", new { text = message }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Placed(siteId, quote)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_once", (object?)null),
                OnSecondOption = Guard(() => Repeat(siteId, quote)),
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_repeat",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // The first delivery day is fixed when the question names its weekday, so a call open over midnight books the
        // weekday it named (Core CommissaryStandingOrders.FirstDay).
        private static DialogEntry? Repeat(string siteId, CommissaryQuote quote)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var first = (int)(HireCrew.Now() / 1440) + 2;
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_repeat_which", new { weekday = CityPitches.Weekday(first) }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => PlacedStanding(siteId, quote, CommissaryRepeat.Daily, first)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_every_day", (object?)null),
                OnSecondOption = Guard(() => PlacedStanding(siteId, quote, CommissaryRepeat.Weekly, first)),
                // The game localizes this key with no arguments, so the weekday is in the message above, not on the button.
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_every_week",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // A standing order booked: its food has to fit each food's weekly limit in its first two weeks, else the
        // commissary says why and asks again how often, so Just once (which may still fit) stays within reach.
        private static DialogEntry? PlacedStanding(string siteId, CommissaryQuote quote, CommissaryRepeat repeat, int firstDay)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            var over = Commissary.OverLimit(FoodTrucksSession.CurrentState!, rules, siteId, quote, repeat, firstDay);
            if (over != null)
            {
                Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_text", new Dictionary<string, string> { { "text", over } }, read: true));
                return HowOften(siteId, quote, over);
            }
            var standing = Commissary.PlaceStanding(siteId, quote, repeat, firstDay);
            if (standing == null) return Closing("foodtrucks:dialog_commissary_unavailable", null);
            const string key = "foodtrucks:message_commissary_standing_placed";
            var data = new Dictionary<string, string>
            {
                { "repeat", Commissary.StandingRepeat(standing) },
                { "items", Commissary.StandingItems(standing) },
                { "time", CityPitches.Clock(rules.DeliveryMinute) },
                { "day", standing.NextDay.ToString() }
            };
            Notices.Send(Current.contact, new TextMessage(key, data, read: true));
            CommissaryOrderForm.End("standing order placed");
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, data),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Form(siteId)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_more", (object?)null),
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // "3 boxes of Dumplings (240 items, $408 a box)"; equipment by the piece: "Griddle × 2, $2,000 each".
        private static string LineText(CommissaryQuoteLine line)
        {
            var piece = DemandData.Tables?.EquipmentForGameItem(line.ItemName) != null;
            var key = piece
                ? line.Boxes == 1 ? "foodtrucks:commissary_order_piece_one" : "foodtrucks:commissary_order_piece"
                : line.Boxes == 1 ? "foodtrucks:commissary_order_line_one" : "foodtrucks:commissary_order_line";
            return string.Format(LocalizorManager.GetLocalization(key, null),
                line.Boxes, LocalizorManager.GetLocalization(line.ItemName, null), line.Units, CartSelling.Money(line.BoxPriceCents));
        }

        // Confirmed: Core books the orders, to be paid on delivery; each order's text goes into the conversation.
        private static DialogEntry? Placed(string siteId, CommissaryQuote quote)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            // Checked again at placing: midnight may have passed since the read-back, into a new week (review round 1, Opus).
            // Over the limit, the commissary says why and the order form opens again to change it (review round 2, Opus).
            var over = Commissary.OverLimit(FoodTrucksSession.CurrentState!, rules, siteId, quote, CommissaryRepeat.Once);
            if (over != null)
            {
                Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_text", new Dictionary<string, string> { { "text", over } }, read: true));
                CartSelling.Notify("foodtrucks:notification", ("message", over));
                return Form(siteId);
            }
            var placed = Commissary.PlaceQuote(siteId, quote);
            if (placed.Count == 0) return Closing("foodtrucks:dialog_commissary_unavailable", null);
            var texts = new List<string>();
            foreach (var order in placed)
            {
                const string key = "foodtrucks:message_commissary_ordered";
                var data = new Dictionary<string, string>
                {
                    { "units", order.Units.ToString() },
                    { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                    { "total", CartSelling.Money(order.TotalCents) },
                    { "time", CityPitches.Clock(rules.DeliveryMinute) }
                };
                Notices.Send(Current.contact, new TextMessage(key, data, read: true));
                texts.Add(LocalizorManager.Localize(key, data).ToString());
            }
            CommissaryOrderForm.End("order placed");
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_text", new { text = string.Join("<br>", texts) }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Form(siteId)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_more", (object?)null),
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        private static DialogEntry Closing(string key, object? data)
        {
            CommissaryOrderForm.End("call closing");
            Notices.Send(Current.contact, new TextMessage(key, data == null ? null : ToDictionary(data), read: true, isNewInteraction: true));
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, data),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = Current.FinishDialog
            };
        }

        private static Dictionary<string, string> ToDictionary(object data) =>
            data.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(data)?.ToString() ?? "");
    }
}
