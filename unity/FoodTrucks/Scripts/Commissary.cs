#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Dialogs;
using Entities;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UI;
using UI.InteriorDesigner;
using UI.ItemPanel;
using UI.Notification;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

namespace FoodTrucks
{
    internal sealed partial class Commissary : MonoBehaviour
    {
        public const string ContactPrefix = "foodtrucks-commissary-";
        private const string ContactDescription = "foodtrucks:commissary_description";
        private const CallDialogType DialogType = (CallDialogType)9103;
        private const string ExpenseLabel = "foodtrucks:transaction_commissary_label";
        private const float VisitExtraMetres = 15f;
        private const float CheckSeconds = 0.5f;

        private static bool attached;
        private static bool uninstalled;
        internal static bool Uninstalled => uninstalled;
        private long lastMinute = -1;
        private float nextCheck;
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
            LogGameDeliveryFees(Rules!);
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

        private static bool deliveryFeesLogged;

        private static void LogGameDeliveryFees(CommissaryRules rules)
        {
            if (deliveryFeesLogged) return;
            deliveryFeesLogged = true;
            try
            {
                var fees = Resources.FindObjectsOfTypeAll<global::Buildings.WholesaleStoreSettings>()
                    .Where(w => w != null).Select(w => $"{w.name} {CartSelling.Money((long)Math.Round(w.deliveryFee * 100))}").Distinct().ToList();
                Log.Info($"Commissary delivery fee: {CartSelling.Money(rules.DeliveryFeeCents)} per delivery; the game's wholesale delivery fees: " +
                         (fees.Count == 0 ? "none loaded" : string.Join(", ", fees)) + "; furniture deliveries $250.");
            }
            catch (Exception ex) { Log.Error("Commissary, reading the game's delivery fees: " + ex.Message); }
        }

        public static void Detach() => attached = false;

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

        internal static List<(double X, double Z)>? Origins(CommissarySite site)
        {
            if (site.Bay != null) return new List<(double X, double Z)> { site.Bay.Value };
            return World.EntrancesOf(site.Address)?.Select(d => ((double)d.x, (double)d.z)).ToList();
        }

        internal static IReadOnlyList<CommissaryBayPoint>? ResolvedBays(CommissarySite site)
        {
            var state = FoodTrucksSession.CurrentState;
            var origins = Origins(site);
            if (state == null || origins == null || origins.Count == 0) return null;
            return CommissaryBays.Current(state.Commissary, site.Id, origins)?.Points;
        }

        private static IEnumerable<(double X, double Z)>? ResolvedPoints(CommissarySite site) => ResolvedBays(site)?.Select(p => (p.X, p.Z));

        private const double SameBayMetres = 3;
        private const float ProbeMetres = (float)BaySnap.MaxMetres + 8f;
        private const float ResolveRetrySeconds = 5f;
        private static readonly Dictionary<string, float> resolveAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly HashSet<string> notReadyLogged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> errorLogged = new HashSet<string>(StringComparer.Ordinal);

        private static bool Once(HashSet<string> logged, string siteId, string reason) => logged.Add(siteId + "|" + reason);

        private static readonly HashSet<string> searchedThisLoad = new HashSet<string>(StringComparer.Ordinal);

        private static bool SearchDue(CommissarySite site)
        {
            var state = FoodTrucksSession.CurrentState;
            var origins = Origins(site);
            if (state == null || origins == null || origins.Count == 0) return false;
            return CommissaryBays.SearchDue(CommissaryBays.Current(state.Commissary, site.Id, origins), searchedThisLoad.Contains(site.Id));
        }

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
                var tooLong = 0;
                var noRoute = 0;
                if (site.Bay == null)
                {
                    var from = WalkFrom(site.Id, door.Value);
                    if (from.Kind == WalkStart.NotReady)
                    {
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
            BayParking.SweepNewBay(site, Rules!.BayRadiusMetres);
            var ms = (Time.realtimeSinceStartup - start) * 1000f;
            foreach (var (bay, from, _) in found)
                Log.Info($"Commissary {site.Id}: bay on legal parking at x {bay.X:F1}, z {bay.Z:F1}, {bay.Distance:F1} m from x {from.X:F1}, z {from.Z:F1} " +
                         $"({(site.Bay != null ? "set in data" : "entrance")}), {(site.InLot ? "nose-in in a lot, facing" : "the lane along")} {Math.Atan2(bay.AlongX, bay.AlongZ) * 180 / Math.PI:F0} degrees; " +
                         $"resolved {why} in {ms:F0} ms and kept in the save.");
            if (found.Count == 0)
                Log.Error($"Commissary {site.Id}: no bay this load (searched {why} in {ms:F0} ms): {string.Join("; ", tried)}. " +
                          "No vehicle is ever in its bay, no paint is drawn and its rented vehicles aren't fined at 04:00. " +
                          "Set a measured bay point in data/commissaries.json (the test bridge's commissary bay_candidates lists legal truck spots up to 80 m out).");
            return true;
        }

        private const float SideSlackMetres = 40f;

        private static (List<((double X, double Z) A, (double X, double Z) B)>? Lanes, string Why) SideLanes(Vector3 door, (double X, double Z) origin, double reach)
        {
            if (!GleyTrafficSystem.TrafficManager.HasInstance || !GleyTrafficSystem.TrafficManager.IsInitialized) return (null, "");
            var lanes = TrafficLanes.Near(door, Flat(door, origin) + (float)reach + SideSlackMetres);
            if (lanes.Unavailable) return (null, lanes.Describe());
            return (lanes.FlatSegments(), lanes.Describe());
        }

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
            Door,
            Unknown,
            CutOff,
            NotReady,
        }

        private static (WalkStart Kind, Vector3 Point, string Why) WalkFrom(string siteId, Vector3 door)
        {
            try
            {
                const int areas = UnityEngine.AI.NavMesh.AllAreas;
                if (!UnityEngine.AI.NavMesh.SamplePosition(door, out var doorHit, 3f, areas)) return (WalkStart.Unknown, door, "");
                if (!GleyTrafficSystem.TrafficManager.HasInstance || !GleyTrafficSystem.TrafficManager.IsInitialized) return (WalkStart.NotReady, door, "");
                var lanes = TrafficLanes.Near(door, 40f);
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

        private static WalkVerdict Walk(Vector3 from, double x, double z) => WalkDetail(from, x, z).Verdict;

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

        internal static CommissarySite? SiteNear(Vector3 position, float extraMetres)
        {
            var sites = Sites;
            var rules = Rules;
            if (sites == null || rules == null) return null;
            return sites.At(ResolvedPoints, position.x, position.z, rules.BayRadiusMetres + extraMetres);
        }

        internal static CommissarySite? SiteOf(VehicleInstance vehicle) => Sites?.Sites.FirstOrDefault(s => InBay(vehicle, s, out _));

        private static CommissarySite? NearestSite(VehicleInstance vehicle, double metres)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            var position = controller != null ? controller.transform.position : (Vector3)vehicle.position;
            CommissarySite? nearest = null;
            var best = metres;
            foreach (var site in Sites?.Sites ?? Enumerable.Empty<CommissarySite>())
                foreach (var point in ResolvedBays(site) ?? Array.Empty<CommissaryBayPoint>())
                {
                    var d = Math.Sqrt((point.X - position.x) * (point.X - position.x) + (point.Z - position.z) * (point.Z - position.z));
                    if (d > best) continue;
                    best = d;
                    nearest = site;
                }
            return nearest;
        }

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

        private static bool NoBay(CommissarySite site) => ResolvedBays(site) is { Count: 0 };

        private static Vector3 PositionOf(VehicleInstance vehicle)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            return controller != null ? controller.transform.position : (Vector3)vehicle.position;
        }

        internal static bool InBay(VehicleInstance vehicle, CommissarySite site, out string why) => InBayAnswer(vehicle, site, out why) == true;

        internal static bool? InBayAnswer(VehicleInstance vehicle, CommissarySite site, out string why)
        {
            var rules = Rules;
            var points = ResolvedBays(site);
            if (rules == null || points == null) { why = "bay not resolved"; return false; }
            if (!string.IsNullOrEmpty(vehicle.streetName)) { why = "inside a building"; return false; }
            var position = PositionOf(vehicle);
            if (!CommissaryBays.WithinRadius(points, position.x, position.z, rules.BayRadiusMetres)) { why = "outside the radius"; return false; }
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

        internal static Contact ContactFor(string siteId)
        {
            var contact = Contact.EnsurePermanentContact(ContactPrefix + siteId, ContactCategoryName.General, ContactDescription);
            contact.callDialogTypeOverride = DialogType;
            return contact;
        }

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
            var address = CommissaryLocation.Address(site);
            var welcome = new Dictionary<string, string>
            {
                { "commissary", Name(site) },
                { "cart", CartSelling.Money(rules.RentPerMonthCents.TryGetValue("cart", out var cart) ? cart : 0) },
                { "truck", CartSelling.Money(rules.RentPerMonthCents.TryGetValue("truck", out var truck) ? truck : 0) }
            };
            if (address != null) welcome["address"] = address;
            Notices.SendText(contact, CommissaryLocation.MessageKey("foodtrucks:message_commissary_welcome_phone", address), welcome);
            Log.Info($"Commissary {site.Id}: unlocked on day {SaveGameManager.Current.Day} ({how}); its contact added.");
            return true;
        }

        private static bool NoPlayer => InstanceBehavior<GameManager>.Instance?.playerController == null;

        private void LateUpdate()
        {
            CommissaryOrderForm.Watch();
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

        private void Step(SaveState state, CommissaryRules rules, CommissaryTables sites, long now)
        {
            CartSale.Retry(state);
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            PayPending(state, rules, showNotification: false);
            PruneSpaces(state, rules);
            ChargeRent(state, rules);
            BookStanding(state, rules, day, minute);
            Deliver(state, rules, day, minute);
            NightCheck(state, rules, sites, now, day, minute);
            ResolvePendingCleaning(state, sites);
        }

        internal static bool LimitsLifted => SaveGameManager.Current?.gameVariables?.disableWholesaleAndImportLimits == true;

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

        internal static void BookStanding(SaveState state, CommissaryRules rules, int day, int minute)
        {
            if (state.Commissary.StandingOrders.Count == 0) return;
            var runs = CommissaryStandingOrders.Book(state.Commissary, rules, day, minute, TodaysPrices(), LimitsLifted);
            if (runs.Count == 0) return;
            FoodTrucksSession.MarkChanged();
            foreach (var run in runs) TellRun(state, run);
        }

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

        internal static string StandingItems(CommissaryStandingOrder standing) =>
            string.Join(", ", standing.Lines.Select(l => string.Format(LocalizorManager.GetLocalization(
                l.Capped ? (l.Boxes == 1 ? "foodtrucks:commissary_standing_line_one" : "foodtrucks:commissary_standing_line") : "foodtrucks:commissary_standing_piece", null),
                l.Boxes, LocalizorManager.GetLocalization(l.ItemName, null))));

        internal static string StandingRepeat(CommissaryStandingOrder standing) =>
            standing.Repeat == CommissaryRepeat.Weekly
                ? LocalizorManager.GetLocalization("foodtrucks:commissary_repeat_weekly", null).Replace("{weekday}", CityPitches.Weekday(standing.NextDay))
                : LocalizorManager.GetLocalization("foodtrucks:commissary_repeat_daily", null);

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

        private static Dictionary<string, VehicleInstance> Fleet()
        {
            var game = SaveGameManager.Current;
            var fleet = new Dictionary<string, VehicleInstance>(StringComparer.Ordinal);
            foreach (var vehicle in game.VehicleInstances.Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>()))
                if (vehicle != null && !string.IsNullOrEmpty(vehicle.id) && FoodTrucksMod.KindOf(vehicle.vehicleTypeName) != null && !fleet.ContainsKey(vehicle.id))
                    fleet[vehicle.id] = vehicle;
            return fleet;
        }

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

        internal static void SettleRentForSale()
        {
            if (FoodTrucksSession.CurrentState == null || Rules == null || !FoodTrucksSession.CanWrite)
                throw new InvalidOperationException("Commissary rent is unavailable.");
            ChargeRent(FoodTrucksSession.CurrentState, Rules);
        }

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

        internal static void Deliver(SaveState state, CommissaryRules rules, int day, int minute)
        {
            if (state.Commissary.Orders.Count == 0) return;
            var today = SaveGameManager.Current.Day;
            var run = Commissaries.Deliver(state.Commissary, rules, day, minute, order =>
            {
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
                         $"paid {CartSelling.Money(order.TotalCents)} on delivery (goods {CartSelling.Money(order.GoodsCents)}, fee {CartSelling.Money(order.FeeCents)}" +
                         (order.FeeCents > 0 ? ", this morning's delivery fee here)." : ", the delivery's fee paid with another order)."));
                if (state.Commissary.Unlocked.Contains(order.CommissaryId))
                    Notices.SendText(ContactFor(order.CommissaryId), order.FeeCents > 0 ? "foodtrucks:message_commissary_delivered_fee" : "foodtrucks:message_commissary_delivered", new Dictionary<string, string>
                    {
                        { "units", order.Units.ToString() },
                        { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                        { "commissary", NameOf(order.CommissaryId) },
                        { "total", CartSelling.Money(order.TotalCents) },
                        { "fee", CartSelling.Money(order.FeeCents) }
                    });
            }
            foreach (var order in run.Refused)
            {
                var standing = order.StandingOrderId != null;
                Log.Info($"Commissary delivery: order {order.Id}, {order.Units} {order.ItemName} at {order.CommissaryId} not delivered: " +
                         $"the payment of {CartSelling.Money(order.TotalCents)} was refused (not enough money); the order is cancelled" +
                         (standing ? $", standing order {order.StandingOrderId} stays." : "."));
                if (state.Commissary.Unlocked.Contains(order.CommissaryId))
                    Notices.SendText(ContactFor(order.CommissaryId), standing ? "foodtrucks:message_commissary_standing_unpaid" : "foodtrucks:message_commissary_delivery_unpaid", new Dictionary<string, string>
                    {
                        { "units", order.Units.ToString() },
                        { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                        { "total", CartSelling.Money(order.TotalCents) }
                    });
            }
        }

        private void NightCheck(SaveState state, CommissaryRules rules, CommissaryTables sites, long now, int day, int minute)
        {
            var commissary = state.Commissary;
            var fleet = Fleet();
            if (commissary.NightCheckedThroughDay == null)
            {
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
            var latest = minute >= rules.NightCheckMinute ? day : day - 1;
            if (latest <= commissary.NightCheckedThroughDay.Value) return;
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
            var pendingBefore = commissary.PendingCleaning.Keys.ToList();
            CleanOvernight(state, Commissaries.CleanedOvernight(commissary, rules, day, minute, flags));
            foreach (var held in commissary.PendingCleaning.Keys.Except(pendingBefore).ToList())
                Log.Info($"Overnight cleaning: {World.Short(held)} is by its commissary's bay, but the street's side can't be read yet; " +
                         $"day {string.Join(", ", commissary.PendingCleaning[held])} waits for it.");
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
                var space = commissary.Spaces.FirstOrDefault(sp => sp.VehicleId == fine.VehicleId);
                var key = space == null ? "foodtrucks:message_city_overnight_fine_nospace" : "foodtrucks:message_city_overnight_fine_ownbay";
                Notices.SendText(CityLicensing.City(), paid ? key : key + "_unpaid", new Dictionary<string, string>
                {
                    { "commissary", space == null ? "" : NameOf(space.CommissaryId) },
                    { "vehicle", VehicleName(state, fine.VehicleId) },
                    { "day", fine.Day.ToString() },
                    { "fine", CartSelling.Money(fine.Cents) },
                    { "time", CityPitches.Clock(rules.NightCheckMinute) }
                });
                Notices.Show(NotificationType.Warning, "foodtrucks:notification_overnight_fine", new Dictionary<string, string>
                {
                    { "vehicle", VehicleName(state, fine.VehicleId) },
                    { "fine", CartSelling.Money(fine.Cents) }
                }, 8f);
            }
        }

        private int nightWaitLogged = -1;
        private int nightSideWaitLogged = -1;

        private static void CleanOvernight(SaveState state, IReadOnlyList<(string VehicleId, int ServiceDay)> cleaned)
        {
            var closedThrough = ReputationBook.ClosedThrough(state);
            foreach (var (vehicleId, serviceDay) in cleaned)
            {
                var vehicle = StreetSales.GetOrAdd(state, vehicleId);
                if (string.IsNullOrEmpty(vehicle.VehicleType))
                    vehicle.VehicleType = state.Commissary.Spaces.FirstOrDefault(sp => sp.VehicleId == vehicleId)?.Kind ?? vehicle.VehicleType;
                var marked = ReputationBook.CleanedOn(vehicle, serviceDay, closedThrough);
                Log.Info(marked
                    ? $"Overnight cleaning: {World.Short(vehicleId)} stood in its commissary's bay at the check; cleaned for day {serviceDay}."
                    : $"Overnight cleaning: {World.Short(vehicleId)} stood in its commissary's bay at the check, but day {serviceDay} is already counted (through day {closedThrough}); nothing marked.");
            }
            if (cleaned.Count > 0) FoodTrucksSession.MarkChanged();
        }

        private static void ResolvePendingCleaning(SaveState state, CommissaryTables sites)
        {
            var pending = state.Commissary.PendingCleaning;
            if (pending.Count == 0) return;
            var fleet = Fleet();
            foreach (var vehicleId in pending.Keys.ToList())
            {
                var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicleId);
                var site = space == null ? null : sites.Site(space.CommissaryId);
                var why = "it is gone or has no space there any more";
                bool? inBay = fleet.TryGetValue(vehicleId, out var vehicle) && site != null ? InBayAnswer(vehicle, site, out why) : false;
                if (inBay == null) continue;
                var days = Commissaries.ResolvePendingCleaning(state.Commissary, vehicleId, inBay.Value);
                FoodTrucksSession.MarkChanged();
                if (inBay.Value) CleanOvernight(state, days.Select(d => (vehicleId, d)).ToList());
                else Log.Info($"Overnight cleaning: {World.Short(vehicleId)} wasn't in its commissary's bay once its side could be read ({why}); nothing cleaned.");
            }
        }

        private static string Describe(CommissaryVehicle v) =>
            $"{World.Short(v.VehicleId)} {(v.AtOwnCommissaryBay ? "in its bay" : "out")}{(v.PlayerInside ? ", player inside" : "")}{(v.OnLateStop ? ", late stop" : "")}{(v.InTransit ? ", in transit" : "")}{(v.AtPlayerProperty ? ", at your own building" : "")}";

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

        private static bool AtProperty(VehicleInstance vehicle, HashSet<string> property)
        {
            if (property.Count == 0) return false;
            if (!string.IsNullOrEmpty(vehicle.streetName)) return property.Contains(World.AddressKey(vehicle.streetName, vehicle.streetNumber));
            var rules = Rules;
            if (rules == null) return false;
            var position = PositionOf(vehicle);
            return property.Any(address => World.DistanceTo(address, position.x, position.z) <= rules.BayRadiusMetres);
        }

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
                    BayGrace = site != null && !withDriver.Contains(vehicle.id) && NoBay(site),
                    PlayerInside = game.ActiveVehicleId == vehicle.id || TruckWindow.PlayerAt(vehicle.id),
                    OnLateStop = saved != null && saved.IsOpen && saved.HireStop != null && HireCrew.OnShift(saved, HireCrew.Now()).Count > 0,
                    InTransit = withDriver.Contains(vehicle.id) || saved?.Transit != null,
                    AtPlayerProperty = !withDriver.Contains(vehicle.id) && AtProperty(vehicle, property)
                });
            }
            return list;
        }

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

        private static TransactionInfo FineInfo() => new TransactionInfo("foodtrucks:transaction_overnight_fine", new Dictionary<string, string>());

        internal sealed class SpaceChoice
        {
            public SpaceChoice(string vehicleId, string kind, string op, string label)
            {
                VehicleId = vehicleId;
                Kind = kind;
                Op = op;
                Label = label;
            }

            public string VehicleId { get; }
            public string Kind { get; }
            public string Op { get; }
            public string Label { get; }
        }

        internal static List<SpaceChoice> SpaceChoices(string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var list = new List<SpaceChoice>();
            if (state == null || rules == null || Sites?.Site(siteId) == null) return list;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var fleet = Fleet().Values.Select(v => (Vehicle: v, Name: VehicleName(state, v.id)))
                .OrderBy(v => v.Name, StringComparer.CurrentCulture).ThenBy(v => v.Vehicle.id, StringComparer.Ordinal);
            foreach (var (vehicle, baseName) in fleet)
            {
                var kind = FoodTrucksMod.KindOf(vehicle.vehicleTypeName);
                if (kind == null) continue;
                seen[baseName] = (seen.TryGetValue(baseName, out var n) ? n : 0) + 1;
                var name = seen[baseName] > 1 ? $"{baseName} ({seen[baseName]})" : baseName;
                var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
                var rent = CartSelling.Money(rules.RentPerMonthCents.TryGetValue(kind, out var monthly) ? monthly : 0);
                if (space == null)
                    list.Add(new SpaceChoice(vehicle.id, kind, "rent", Format("foodtrucks:commissary_spaces_rent", name, rent)));
                else if (space.CommissaryId != siteId)
                    list.Add(new SpaceChoice(vehicle.id, kind, "move", Format("foodtrucks:commissary_spaces_move", name, NameOf(space.CommissaryId), rent)));
                else
                    list.Add(new SpaceChoice(vehicle.id, kind, "release", Format("foodtrucks:commissary_spaces_release", name)));
            }
            return list;
        }

        internal static string? ApplySpace(SpaceChoice choice, string siteId, bool inCall)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var who = $"{choice.Kind} {World.Short(choice.VehicleId)}";
            if (state == null || rules == null || uninstalled || !FoodTrucksSession.CanWrite)
            {
                Log.Info($"Commissary {siteId}: Spaces {choice.Op} for {who} refused: the mod's data isn't writable now.");
                return null;
            }
            if (!Fleet().ContainsKey(choice.VehicleId))
            {
                Log.Info($"Commissary {siteId}: Spaces {choice.Op} for {who} refused: the vehicle is gone.");
                return null;
            }
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == choice.VehicleId);
            var fits = space == null ? "rent" : space.CommissaryId != siteId ? "move" : "release";
            if (fits != choice.Op)
            {
                Log.Info($"Commissary {siteId}: Spaces {choice.Op} for {who} refused: its space changed since the list opened (now {fits}).");
                return null;
            }
            var from = space?.CommissaryId;
            var done = choice.Op == "release" ? Release(choice.VehicleId, choice.Kind, siteId) : Rent(choice.VehicleId, choice.Kind, siteId);
            if (!done) return null;
            try { VehicleTodos.ReconcileNow(); }
            catch (Exception ex) { Log.Error("To-dos after a space change: " + ex.Message); }
            var name = VehicleName(state, choice.VehicleId);
            var rent = CartSelling.Money(rules.RentPerMonthCents.TryGetValue(choice.Kind, out var monthly) ? monthly : 0);
            var (key, data) = choice.Op switch
            {
                "rent" => ("foodtrucks:message_commissary_space_rented", new Dictionary<string, string> { { "vehicle", name }, { "rent", rent }, { "time", CityPitches.Clock(rules.NightCheckMinute) } }),
                "move" => ("foodtrucks:message_commissary_space_moved", new Dictionary<string, string> { { "vehicle", name }, { "from", NameOf(from) }, { "rent", rent } }),
                _ => ("foodtrucks:message_commissary_space_released", new Dictionary<string, string> { { "vehicle", name } })
            };
            var site = Sites?.Site(siteId);
            var address = choice.Op == "release" || site == null ? null : CommissaryLocation.Address(site);
            if (address != null)
            {
                key = CommissaryLocation.MessageKey(key, address);
                data["address"] = address;
            }
            var contact = ContactFor(siteId);
            if (inCall) Notices.Send(contact, new TextMessage(key, data, read: true));
            else Notices.SendText(contact, key, data);
            return Notices.Text(key, data);
        }

        private static void SayNotInBay(VehicleInstance vehicle, string kind)
        {
            var near = Sites?.Sites.FirstOrDefault(s => !InBay(vehicle, s, out var whyNot) && whyNot.StartsWith("not legally parked", StringComparison.Ordinal));
            CartSelling.Say(near != null ? "foodtrucks:notification_commissary_park_legally" : "foodtrucks:notification_commissary_not_here", kind);
        }

        private const double RentNearMetres = 40.0;
        internal static string? RentBayForTesting(VehicleInstance vehicle)
        {
            const string refused = "the commissary's Spaces refused (see the notice)";
            var state = FoodTrucksSession.CurrentState;
            var kind = FoodTrucksMod.KindOf(vehicle.vehicleTypeName);
            if (state == null || Rules == null || kind == null || uninstalled) return "the commissary can't rent a space now (data not loaded or uninstalled)";
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return refused;
            }
            var site = SiteOf(vehicle) ?? NearestSite(vehicle, RentNearMetres);
            if (site == null)
            {
                SayNotInBay(vehicle, kind);
                return refused;
            }
            Unlock(state, site, "vehicle at the bay");
            var choice = SpaceChoices(site.Id).FirstOrDefault(c => c.VehicleId == vehicle.id);
            if (choice == null) return $"the commissary's Spaces don't list {vehicle.id}";
            if (choice.Op == "release") return "the commissary's Spaces offer release for this vehicle here, not rent or move";
            return ApplySpace(choice, site.Id, inCall: false) == null ? "the Spaces choice changed nothing (see the log)" : null;
        }

        private static bool Rent(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return false;
            var day = SaveGameManager.Current.Day;
            var before = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicleId)?.CommissaryId;
            if (!rules.RentPerMonthCents.TryGetValue(kind, out var monthly))
            {
                Log.Error($"Commissary: no rent for {kind} in rules.json.");
                return false;
            }
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
            return true;
        }

        private static bool Release(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return false;
            ChargeRent(state, rules);
            if (!Commissaries.Release(state.Commissary, vehicleId))
            {
                Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} has no space to give up.");
                return false;
            }
            FoodTrucksSession.MarkChanged();
            Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} gave up its space on day {SaveGameManager.Current.Day}.");
            CartSelling.Say("foodtrucks:notification_commissary_released", kind, ("commissary", NameOf(siteId)));
            return true;
        }

        private sealed class Loaded
        {
            public Dictionary<string, int> Items { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
            public IReadOnlyList<string> NotHeld { get; set; } = Array.Empty<string>();
            public long Back { get; set; }
            public long Left { get; set; }
        }

        private static Loaded LoadFromStock(SaveState state, FoodTrucks.Core.Demand.DemandTables tables, VehicleInstance vehicle, string kind, string siteId)
        {
            var result = new Loaded();
            var plan = StockTransfer.FillUp(tables, kind, CargoLimit.Lines(vehicle), InStock(state, siteId));
            if (plan.Load.Count > 0) result.Back += Load(state, vehicle, siteId, plan.Load, result.Items);
            result.NotHeld = plan.NotHeld;
            FoodTrucksSession.MarkChanged();
            if (result.Items.Count > 0 || result.Back > 0)
            {
                try { GameEvent.Invoke("ba:gameevent_itemcargochanged"); }
                catch (Exception ex) { Log.Error("Commissary fill up, cargo event: " + ex); }
            }
            result.Left = state.Commissary.Stock.TryGetValue(siteId, out var rest) ? rest.Sum(x => (long)x.Units) : 0;
            return result;
        }

        private static void CargoChanged(VehicleInstance vehicle)
        {
            try
            {
                vehicle.OnItemsInCargoUpdated()?.Invoke();
                GameEvent.Invoke("ba:gameevent_itemcargochanged");
            }
            catch (Exception ex) { Log.Error("Commissary stock, cargo event: " + ex); }
        }

        internal static string? CrewRestockSite(SaveState state, VehicleInstance vehicle)
        {
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
            var site = space == null ? null : Sites?.Site(space.CommissaryId);
            if (!attached || uninstalled || DemandData.Tables == null || site == null) return null;
            return InBay(vehicle, site, out _) ? site.Id : null;
        }

        internal static CommissarySite? OwnBay(SaveState state, VehicleInstance vehicle, out string why)
        {
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
            var site = space == null ? null : Sites?.Site(space.CommissaryId);
            if (!attached || uninstalled) why = "the commissaries aren't running";
            else if (space == null) why = "no commissary space";
            else if (site == null) why = $"its commissary {space.CommissaryId} isn't known";
            else if (InBay(vehicle, site, out var bay)) { why = "in its bay at " + site.Id; return site; }
            else why = $"not in its bay at {site.Id} ({bay})";
            return null;
        }

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

        internal static string? CrewStockKey(SaveState state, VehicleInstance vehicle, string kind, string siteId)
        {
            var tables = DemandData.Tables;
            return tables == null ? null : FoodTrucks.Core.Fleet.CrewRestock.StockKey(tables, kind, CargoLimit.Lines(vehicle), InStock(state, siteId));
        }

        internal static void CrewRestock(SaveState state, VehicleState saved, VehicleInstance vehicle, string kind, string siteId, long now)
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var result = LoadFromStock(state, tables, vehicle, kind, siteId);
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

        private static string PieceFull(VehicleInstance vehicle, StoragePool pool, List<CommissaryStockLine>? stock)
        {
            var foods = pool.Piece.Products.Where(p => stock != null && stock.Any(l => l.ItemName == p && l.Units > 0))
                .Select(p => LocalizorManager.GetLocalization(p, null));
            return LocalizorManager.GetLocalization("foodtrucks:commissary_restock_piece_full", null)
                .Replace("{foods}", string.Join(", ", foods)).Replace("{piece}", StoragePanel.PieceName(vehicle, pool.Piece))
                .Replace("{load}", pool.LoadUnits.ToString()).Replace("{capacity}", pool.Capacity.ToString());
        }

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

        private static string CapNote(VehicleInstance vehicle, StoragePool pool) =>
            LocalizorManager.GetLocalization(pool.Pieces > 1 ? "foodtrucks:commissary_restock_capped_many" : "foodtrucks:commissary_restock_capped", null)
                .Replace("{piece}", StoragePanel.PieceName(vehicle, pool.Piece)).Replace("{count}", pool.Pieces.ToString())
                .Replace("{capacity}", pool.Capacity.ToString());

        private static bool Capped(FoodTrucks.Core.Demand.DemandTables tables, StoragePool pool, List<CommissaryStockLine>? stock)
        {
            if (stock == null) return false;
            var waiting = pool.Piece.Products.Where(p => stock.Any(l => l.ItemName == p && l.Units > 0)).ToList();
            if (waiting.Count == 0) return false;
            var lightest = waiting.Min(p => tables.Product(p)?.StockWeight ?? 1.0);
            return pool.Room + 1e-9 < lightest;
        }

        private static Dictionary<string, int> InStock(SaveState state, string siteId)
        {
            var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!state.Commissary.Stock.TryGetValue(siteId, out var stock)) return wanted;
            foreach (var line in stock) wanted[line.ItemName] = (int)Math.Min(int.MaxValue, (wanted.TryGetValue(line.ItemName, out var w) ? w : 0) + (long)line.Units);
            return wanted;
        }

        private static void Followed(SaveState state, VehicleInstance vehicle, CommissaryStockLine line, int moved)
        {
            try
            {
                Commissaries.TookOnVehicle(state.Commissary, line, moved);
                if (Commissaries.LoadedDay(line) is { } day && DemandData.Tables?.Product(line.ItemName) != null)
                {
                    var saved = StreetSales.GetOrAdd(state, vehicle.id);
                    if (string.IsNullOrEmpty(saved.VehicleType)) saved.VehicleType = FoodTrucksMod.KindOf(vehicle.vehicleTypeName) ?? saved.VehicleType;
                    ReputationBook.Restored(saved, line.ItemName, moved, day, UnitsOnBoard(vehicle, line.ItemName), SaveGameManager.Current.Day);
                    Log.Info($"Commissary stock: {moved} {line.ItemName} loaded into {World.Short(vehicle.id)} keep their age (on board since day {day}).");
                }
            }
            catch (Exception ex) { Log.Error($"Commissary stock, following {line.ItemName} onto {vehicle.id}: {ex}"); }
        }

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
                if (moved > 0)
                {
                    loaded[line.ItemName] = (loaded.TryGetValue(line.ItemName, out var l) ? l : 0) + moved;
                    Followed(state, vehicle, line, moved);
                }
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
                if (price <= 0f) price = item.DefaultMarketPrice * (SaveGameManager.Current?.gameVariables.marketPriceMultiplier ?? 1f);
                var cents = (long)Math.Round(price * 100.0);
                if (cents <= 0) continue;
                list.Add((name, machines ? 1 : Math.Max(1, item.boxSize), cents));
            }
            return list.OrderBy(i => LocalizorManager.GetLocalization(i.Item1, null), StringComparer.CurrentCulture).ToList();
        }

        internal static List<CommissaryOrderForm.Line> FormLines(SaveState state, string siteId)
        {
            var stock = state.Commissary.Stock.TryGetValue(siteId, out var lines) ? lines : new List<CommissaryStockLine>();
            return Catalogue(false).Concat(Catalogue(true))
                .Select(c => new CommissaryOrderForm.Line(c.Item, c.Box, c.UnitCents, stock.Where(l => l.ItemName == c.Item).Sum(l => (long)l.Units)))
                .ToList();
        }

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
                Log.Info($"Commissary order {order.Id}: {boxes} boxes, {order.Units} {order.ItemName} at {siteId} for {CartSelling.Money(order.GoodsCents)}, paid on delivery; " +
                         $"due day {order.DeliverDay} at {CityPitches.Clock(rules.DeliveryMinute)}.");
            }
            if (orders.Count > 0)
                Log.Info($"Commissary delivery fee at {siteId} for day {orders[0].DeliverDay}: {CartSelling.Money(rules.DeliveryFeeCents)} once for that morning's delivery" +
                         (quote.SharedDelivery ? ", shared with an order already arriving then." : "."));
            return orders;
        }

        internal static CommissaryQuote? QuoteFor(string siteId, IEnumerable<CommissaryOrderRequest> requests)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null) return null;
            var tomorrow = (int)(HireCrew.Now() / 1440) + 2;
            return CommissaryOrdering.Quote(rules, requests, CommissaryOrdering.SharesDelivery(state.Commissary, siteId, tomorrow));
        }

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

        internal static string? OverLimit(SaveState state, CommissaryRules rules, string siteId, CommissaryQuote quote, CommissaryRepeat repeat, int? firstDay = null)
        {
            var today = (int)(HireCrew.Now() / 1440) + 1;
            var first = repeat == CommissaryRepeat.Once ? today + 1 : CommissaryStandingOrders.FirstDay(repeat, today, firstDay);
            var check = CommissarySupply.Check(state.Commissary, rules, quote, repeat, first - 1, LimitsLifted);
            if (check.Fits) return null;
            Log.Info($"Commissary order at {siteId}: {check.ItemName} over its weekly limit for the whole fleet ({repeat}): {check.Left} boxes left in the week of day {check.WeekStart}, {check.Needed} needed.");
            return LocalizorManager.GetLocalization("foodtrucks:commissary_over_limit", null)
                .Replace("{item}", LocalizorManager.GetLocalization(check.ItemName, null)).Replace("{left}", check.Left.ToString())
                .Replace("{needed}", check.Needed.ToString()).Replace("{day}", check.WeekStart.ToString())
                .Replace("{limit}", rules.FoodBoxLimit(check.ItemName!).ToString());
        }

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

        internal static (IReadOnlyList<CommissaryOrder> Orders, string? Refusal, long FeeCents) OrderForTesting(string siteId, IEnumerable<(string Item, int Boxes)> items)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var site = Sites?.Site(siteId);
            if (state == null || rules == null || site == null || uninstalled || !FoodTrucksSession.CanWrite)
                return (Array.Empty<CommissaryOrder>(), site == null ? $"no commissary {siteId}" : "the commissary can't take orders now (data not loaded, read-only or uninstalled)", 0);
            if (!CommissaryOrdering.CanOrder(state.Commissary, siteId))
                return (Array.Empty<CommissaryOrder>(), $"no space rented at {siteId} (or it isn't unlocked): rent a space first", 0);
            var catalogue = Catalogue(false).Concat(Catalogue(true)).ToList();
            var requests = new List<CommissaryOrderRequest>();
            foreach (var (item, boxes) in items)
            {
                var line = catalogue.FirstOrDefault(c => c.Item1 == item);
                if (line.Item1 == null)
                    line = catalogue.FirstOrDefault(c => string.Equals(LocalizorManager.GetLocalization(c.Item1, null), item, StringComparison.OrdinalIgnoreCase));
                if (line.Item1 == null) return (Array.Empty<CommissaryOrder>(), $"{item} isn't on the commissary's list", 0);
                requests.Add(new CommissaryOrderRequest(line.Item1, line.Item2, line.Item3, Math.Max(0, boxes), DemandData.Tables?.EquipmentForGameItem(line.Item1) == null));
            }
            var quote = QuoteFor(siteId, requests)!;
            if (quote.Problem == CommissaryQuoteProblem.Empty) return (Array.Empty<CommissaryOrder>(), "the order has no boxes", 0);
            if (quote.Problem == CommissaryQuoteProblem.TooManyBoxes)
                return (Array.Empty<CommissaryOrder>(), $"more than {CommissaryOrdering.MaxBoxesPerOrder} boxes in one order", 0);
            var over = OverLimit(state, rules, siteId, quote, CommissaryRepeat.Once);
            if (over != null) return (Array.Empty<CommissaryOrder>(), over, 0);
            Log.Info($"Commissary order at {siteId}: read back {quote.Boxes} boxes ({string.Join(", ", quote.Lines.Select(l => $"{l.Boxes} {l.ItemName}"))}), " +
                     $"{CartSelling.Money(quote.GoodsCents)} plus {CartSelling.Money(quote.FeeCents)} delivery{(quote.SharedDelivery ? " (shared)" : "")} (test bridge).");
            var placed = PlaceQuote(siteId, quote);
            return (placed, placed.Count == 0 ? "Core placed no order" : null, quote.FeeCents);
        }

        public static void RemoveForUninstall()
        {
            uninstalled = true;
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state != null && rules != null && FoodTrucksSession.CanWrite)
            {
                var commissary = state.Commissary;
                var spaces = commissary.Spaces.Count;
                try { ChargeRent(state, rules); }
                catch (Exception ex) { Log.Error("Uninstall: commissary rent: " + ex); }
                var fromPlans = FoodTrucks.Core.Commissary.CommissaryLogistics.GoodsCents(commissary);
                var fromVehicles = Commissaries.ReturnedGoodsCents(commissary);
                var refund = Commissaries.Refund(commissary);
                var paidBack = false;
                if (refund.Total > 0)
                {
                    var info = new TransactionInfo("foodtrucks:transaction_commissary_refund", new Dictionary<string, string>());
                    paidBack = Payments.PayIn(refund.Total, info);
                    if (paidBack)
                    {
                        var reversed = ReverseDeduction(refund.Total - fromPlans - fromVehicles);
                        Log.Info($"Uninstall: {CartSelling.Money(reversed)} of this tax year's commissary deduction taken back for the refund.");
                    }
                    else Log.Error($"Uninstall: the commissary refund of {CartSelling.Money(refund.Total)} was not paid.");
                }
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
            CommissaryDialog.EndSpaces("uninstall step");
            var removed = SaveGameManager.Current?.Contacts.RemoveAll(c => c != null && c.id != null && c.id.StartsWith(ContactPrefix, StringComparison.Ordinal)) ?? 0;
            if (removed > 0) Log.Info($"Uninstall: {removed} commissary contacts removed.");
        }

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

        internal sealed class BayCandidate
        {
            public double X, Z, Heading, Distance;
            public string? Walk;
            public float? WalkMetres, StraightMetres;
            public bool? SameSide;
            public bool Automatic;
            public string? Rejected;
        }

        internal sealed class BayCandidateOrigin
        {
            public string Kind = "";
            public double X, Z;
            public Vector3? Door;
            public string WalkStart = "";
            public bool? SameSide;
            public int Colliders;
            public bool Truncated;
            public List<BayCandidate> Candidates = new List<BayCandidate>();
        }

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
                var probe = new Legality.ParkingProbe(centre, (float)Math.Max(radius, BaySnap.MaxMetres) + 8f);
                entry.Colliders = probe.Count;
                entry.Truncated = probe.Truncated;
                List<((double X, double Z) A, (double X, double Z) B)>? sideLanes = null;
                var laneless = false;
                if (door != null)
                {
                    var side = SideLanes(door.Value, origin, Math.Max(radius, BaySnap.MaxMetres));
                    sideLanes = side.Lanes ?? throw new InvalidOperationException(side.Why.Length > 0
                        ? "the traffic lanes couldn't be read (" + side.Why + "); try again in a few seconds"
                        : "the traffic system isn't ready to check the door's street; try again in a few seconds");
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

        public static void DeliverForTesting()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var now = HireCrew.Now();
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            var count = 0;
            foreach (var order in state.Commissary.Orders.Where(o => o.DeliverDay > day))
            {
                order.DeliverDay = day;
                count++;
            }
            var early = 0;
            var prices = TodaysPrices();
            foreach (var standing in state.Commissary.StandingOrders.Where(o => o.NextDay >= day && o.NextDay <= day + 1).OrderBy(o => o.Id).ToList())
            {
                var run = CommissaryStandingOrders.BookEarly(state.Commissary, rules, standing, day, minute, prices, LimitsLifted);
                TellRun(state, run);
                early++;
            }
            if (early > 0) FoodTrucksSession.MarkChanged();
            Deliver(state, rules, day, Math.Max(minute, rules.DeliveryMinute));
            Log.Info($"Debug: {count} commissary orders brought forward and {early} standing deliveries due by tomorrow booked early; all delivered now (paid on delivery).");
            CartSelling.Notify("foodtrucks:notification", ("message", $"Debug: {count} orders and {early} standing deliveries brought forward; see the commissary's texts."));
        }

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

    internal sealed class CommissaryDialog : Dialog
    {
        public CommissaryDialog()
        {
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

        private static void HangUp()
        {
            CommissaryOrderForm.End("hung up");
            EndSpaces("hung up");
            var id = Current?.contact?.id;
            var over = PhoneCall.HangUp();
            Log.Info($"Commissary call: {id ?? "?"} hung up; {(over ? "the call is over, CALL is ready" : "a call is still on")}.");
        }

        private static TextMessage HangUpMessage() => new TextMessage("ba:messagetype_contacts_message_player_cancel_call");

        private static CommissarySite? Site()
        {
            var id = Current?.contact?.id;
            if (id == null || !id.StartsWith(Commissary.ContactPrefix, StringComparison.Ordinal)) return null;
            return DemandData.Commissaries?.Site(id.Substring(Commissary.ContactPrefix.Length));
        }

        private static DialogEntry? Refusal(CommissarySite? site, bool forOrders = true)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || site == null || DemandData.Rules?.Commissary == null || !FoodTrucksSession.CanWrite || Commissary.Uninstalled)
                return Closing("foodtrucks:dialog_commissary_unavailable", null);
            if (!forOrders) return null;
            if (!CommissaryOrdering.CanOrder(state.Commissary, site.Id))
                return Closing("foodtrucks:dialog_commissary_no_space_phone", new { commissary = Commissary.Name(site) });
            if (!CommissaryOrderForm.Ready)
            {
                Log.Error("Commissary call: no order form (see the Commissary orders line at startup).");
                return Closing("foodtrucks:dialog_commissary_unavailable", null);
            }
            return null;
        }

        private static DialogEntry Start()
        {
            EndSpaces("a new call");
            var site = Site();
            var refusal = Refusal(site, forOrders: false);
            if (refusal != null) return refusal;
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            var address = CommissaryLocation.Address(site!);
            if (!CommissaryOrdering.CanOrder(commissary, site!.Id))
            {
                var rules = DemandData.Rules!.Commissary!;
                var nospace = new
                {
                    commissary = Commissary.Name(site),
                    address = address ?? "",
                    standing = CommissaryStandingOrders.At(commissary, site.Id).Count.ToString(),
                    cart = CartSelling.Money(rules.RentPerMonthCents.TryGetValue("cart", out var cart) ? cart : 0),
                    truck = CartSelling.Money(rules.RentPerMonthCents.TryGetValue("truck", out var truck) ? truck : 0)
                };
                var spacesKey = CommissaryLocation.MessageKey("foodtrucks:dialog_commissary_start_spaces", address);
                Notices.Send(Current.contact, new TextMessage(spacesKey, ToDictionary(nospace), read: true, isNewInteraction: true));
                return Menu(site.Id, LocalizorManager.Localize(spacesKey, nospace));
            }
            var data = new
            {
                commissary = Commissary.Name(site!),
                address = address ?? "",
                spaces = Notices.Counted(commissary.Spaces.Count(s => s.CommissaryId == site!.Id), "foodtrucks:count_spaces"),
                stock = Notices.Counted(commissary.Stock.TryGetValue(site!.Id, out var lines) ? lines.Sum(l => (long)l.Units) : 0, "foodtrucks:count_items"),
                orders = Notices.Counted(commissary.Orders.Count(o => o.CommissaryId == site.Id), "foodtrucks:count_orders"),
                standing = Notices.Counted(CommissaryStandingOrders.At(commissary, site.Id).Count, "foodtrucks:count_standing")
            };
            var key = CommissaryLocation.MessageKey("foodtrucks:dialog_commissary_start_standing", address);
            Notices.Send(Current.contact, new TextMessage(key, ToDictionary(data), read: true, isNewInteraction: true));
            return Menu(site.Id, LocalizorManager.Localize(key, data));
        }

        private static DialogEntry Menu(string siteId, Localizor.LanguageChangeEvent.LanguageChangeEventDataHolder message)
        {
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            if (!CommissaryOrdering.CanOrder(commissary, siteId)) return SpacesMenu(siteId, message);
            return new DialogEntry
            {
                messageData = message,
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Form(siteId)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_stock", (object?)null),
                OnSecondOption = Guard(() => SpacesMenu(siteId, LocalizorManager.Localize("foodtrucks:dialog_commissary_spaces_menu", (object?)null))),
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_spaces_and_standing",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        private static DialogEntry SpacesMenu(string siteId, Localizor.LanguageChangeEvent.LanguageChangeEventDataHolder message)
        {
            var refusal = Refusal(DemandData.Commissaries?.Site(siteId), forOrders: false);
            if (refusal != null) return refusal;
            var standing = CommissaryStandingOrders.At(FoodTrucksSession.CurrentState!.Commissary, siteId).Count;
            return new DialogEntry
            {
                messageData = message,
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Spaces(siteId)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_spaces", (object?)null),
                OnSecondOption = standing > 0 ? Guard(() => Standing(siteId, 0)) : null!,
                SecondOptionTextOverride = standing > 0 ? "foodtrucks:dialog_commissary_standing_list" : null!,
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        private static (int Id, string SiteId)? spacesOpen;
        private static int spacesCount;

        private static DialogEntry? Spaces(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site, forOrders: false);
            if (refusal != null) return refusal;
            var choices = Commissary.SpaceChoices(siteId);
            if (choices.Count == 0)
            {
                Log.Info($"Commissary call {siteId}: Spaces lists nothing: the player has no food cart or truck.");
                return SpacesMenu(siteId, LocalizorManager.Localize("foodtrucks:dialog_commissary_spaces_none", (object?)null));
            }
            if (DropdownSelector.onShow == null)
            {
                Log.Error($"Commissary call {siteId}: Spaces can't open: the game's list picker isn't available in this scene.");
                return SpacesMenu(siteId, LocalizorManager.Localize("foodtrucks:dialog_commissary_spaces_unavailable", (object?)null));
            }
            EndSpaces("a new Spaces list");
            var id = ++spacesCount;
            var contact = Current.contact;
            spacesOpen = (id, siteId);
            var labels = choices.Select(c => c.Label).ToList();
            var byLabel = choices.ToDictionary(c => c.Label, c => c, StringComparer.Ordinal);
            Log.Info($"Commissary call {siteId}: Spaces lists {choices.Count} vehicles ({string.Join(", ", choices.Select(c => $"{World.Short(c.VehicleId)} {c.Op}"))}); " +
                     "the call ends and the phone closes for the list.");
            Notices.Send(contact, new TextMessage("foodtrucks:dialog_commissary_spaces_pick", null, read: true));
            CommissaryOrderForm.End("Spaces list");
            PhoneCall.HangUp();
            InstanceBehavior<UIs>.Instance?.fullMenu?.Toggle(show: false);
            HireRoutes.Later(() =>
            {
                try
                {
                    if (spacesOpen?.Id != id) return;
                    DropdownSelector.Show(labels, chosen =>
                    {
                        try { Chosen(id, siteId, contact, byLabel.TryGetValue(chosen ?? "", out var choice) ? choice : null); }
                        catch (Exception ex) { Log.Error("Commissary call, Spaces: " + ex); }
                    }, "foodtrucks:commissary_spaces_header", "foodtrucks:commissary_spaces_body");
                }
                catch (Exception ex)
                {
                    spacesOpen = null;
                    Log.Error("Commissary call, opening the Spaces list: " + ex);
                }
            });
            return null;
        }

        private static void Chosen(int id, string siteId, Contact contact, Commissary.SpaceChoice? choice)
        {
            if (spacesOpen?.Id != id)
            {
                Log.Info($"Commissary call {siteId}: a Spaces choice came after its list had ended; nothing done.");
                return;
            }
            spacesOpen = null;
            if (choice == null)
            {
                Log.Info($"Commissary call {siteId}: the Spaces list closed with nothing chosen.");
                return;
            }
            if (Commissary.ApplySpace(choice, siteId, inCall: false) == null)
                CartSelling.Notify("foodtrucks:notification_commissary_spaces_unchanged");
            HireRoutes.Later(() =>
            {
                try
                {
                    if (Commissary.Uninstalled) return;
                    SaveGameManager.Current.PlayerDefaults.contactsLastName = contact.id;
                    SaveGameManager.Current.PlayerDefaults.contactsLastCategoryName = contact.category;
                    InstanceBehavior<UIs>.Instance.fullMenu.ShowApp(AppName.Contacts);
                }
                catch (Exception ex) { Log.Error($"Commissary call {siteId}: opening the conversation again after Spaces: {ex}"); }
            });
        }

        internal static void EndSpaces(string why)
        {
            var current = spacesOpen;
            spacesOpen = null;
            if (current == null) return;
            try
            {
                if (!DropdownSelector.isOpen)
                {
                    Log.Info($"Commissary call {current.Value.SiteId}: Spaces list forgotten ({why}); the picker was already closed.");
                    return;
                }
                var picker = UnityEngine.Object.FindObjectOfType<DropdownSelectorUI>();
                picker?.OnCancel();
                Log.Info($"Commissary call {current.Value.SiteId}: Spaces list closed ({why}){(picker == null ? "; no picker found to close, its choice does nothing" : "")}.");
            }
            catch (Exception ex) { Log.Error("Commissary call, closing the Spaces list: " + ex); }
        }

        private static DialogEntry? Standing(string siteId, int index)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site, forOrders: false);
            if (refusal != null) return refusal;
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            var list = CommissaryStandingOrders.At(commissary, siteId);
            if (list.Count == 0 || index >= list.Count)
                return Menu(siteId, LocalizorManager.Localize("foodtrucks:dialog_commissary_standing_done", new { count = list.Count.ToString() }));
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
            return Standing(siteId, index);
        }

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
                    .Select(l => $"{l.Item} {CommissarySupply.Left(FoodTrucksSession.CurrentState!.Commissary, DemandData.Rules!.Commissary!, l.Item, TimeHelper.CurrentDay + 1)}"));
            Log.Info($"Commissary order at {siteId}: the order form lists {lines.Count} items; boxes left this week by food, whole fleet: {weekly}.");
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

        private static DialogEntry? FormSet(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            var form = Current.GetInputComponent<UI.Dialog.FurnitureDeliveryContractSettings>();
            var quote = Commissary.QuoteFor(siteId, CommissaryOrderForm.Requests(form))!;
            switch (quote.Problem)
            {
                case CommissaryQuoteProblem.Empty:
                    CartSelling.Notify("foodtrucks:notification_commissary_order_empty");
                    return null;
                case CommissaryQuoteProblem.TooManyBoxes:
                    CartSelling.Notify("foodtrucks:notification_commissary_order_too_many", ("max", CommissaryOrdering.MaxBoxesPerOrder.ToString()));
                    return null;
            }
            var over = Commissary.OverLimit(FoodTrucksSession.CurrentState!, rules, siteId, quote, CommissaryRepeat.Once);
            if (over != null)
            {
                CartSelling.Notify("foodtrucks:notification", ("message", over));
                return null;
            }
            CommissaryOrderForm.End("read back");
            var items = string.Join("<br>", quote.Lines.Select(LineText));
            Notices.SendFromPlayer(Current.contact, new TextMessage("foodtrucks:message_commissary_order_player", new Dictionary<string, string> { { "items", items } }, read: true));
            var data = new
            {
                items,
                goods = CartSelling.Money(quote.GoodsCents),
                fee = CartSelling.Money(quote.FeeCents),
                perDelivery = CartSelling.Money(quote.FeePerDeliveryCents),
                total = CartSelling.Money(quote.TotalCents),
                time = CityPitches.Clock(rules.DeliveryMinute)
            };
            var quoteKey = quote.SharedDelivery ? "foodtrucks:dialog_commissary_quote_shared" : "foodtrucks:dialog_commissary_quote";
            Notices.Send(Current.contact, new TextMessage(quoteKey, ToDictionary(data), read: true));
            Log.Info($"Commissary order at {siteId}: read back {quote.Boxes} boxes ({string.Join(", ", quote.Lines.Select(l => $"{l.Boxes} {l.ItemName}"))}), " +
                     $"{CartSelling.Money(quote.GoodsCents)} plus {CartSelling.Money(quote.FeeCents)} delivery{(quote.SharedDelivery ? " (shared with an order already arriving then)" : "")}.");
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(quoteKey, data),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => HowOften(siteId, quote)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_confirm", (object?)null),
                OnSecondOption = Guard(() => Form(siteId)),
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_order_change",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

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
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_every_week",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

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
                { "day", standing.NextDay.ToString() },
                { "fee", CartSelling.Money(rules.DeliveryFeeCents) }
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

        private static string LineText(CommissaryQuoteLine line)
        {
            var piece = DemandData.Tables?.EquipmentForGameItem(line.ItemName) != null;
            var key = piece
                ? line.Boxes == 1 ? "foodtrucks:commissary_order_piece_one" : "foodtrucks:commissary_order_piece"
                : line.Boxes == 1 ? "foodtrucks:commissary_order_line_one" : "foodtrucks:commissary_order_line";
            return string.Format(LocalizorManager.GetLocalization(key, null),
                line.Boxes, LocalizorManager.GetLocalization(line.ItemName, null), line.Units, CartSelling.Money(line.BoxPriceCents));
        }

        private static DialogEntry? Placed(string siteId, CommissaryQuote quote)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            var over = Commissary.OverLimit(FoodTrucksSession.CurrentState!, rules, siteId, quote, CommissaryRepeat.Once);
            if (over != null)
            {
                Notices.Send(Current.contact, new TextMessage("foodtrucks:dialog_commissary_text", new Dictionary<string, string> { { "text", over } }, read: true));
                CartSelling.Notify("foodtrucks:notification", ("message", over));
                return Form(siteId);
            }
            var tomorrow = (int)(HireCrew.Now() / 1440) + 2;
            var shared = CommissaryOrdering.SharesDelivery(FoodTrucksSession.CurrentState!.Commissary, siteId, tomorrow);
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
                    { "total", CartSelling.Money(order.GoodsCents) },
                    { "time", CityPitches.Clock(rules.DeliveryMinute) }
                };
                Notices.Send(Current.contact, new TextMessage(key, data, read: true));
                texts.Add(LocalizorManager.Localize(key, data).ToString());
            }
            {
                var key = shared ? "foodtrucks:message_commissary_ordered_fee_shared" : "foodtrucks:message_commissary_ordered_fee";
                var data = new Dictionary<string, string> { { "fee", CartSelling.Money(rules.DeliveryFeeCents) }, { "time", CityPitches.Clock(rules.DeliveryMinute) } };
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
            EndSpaces("call closing");
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
