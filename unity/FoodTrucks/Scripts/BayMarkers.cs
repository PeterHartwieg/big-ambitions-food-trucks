#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // A painted loading bay on the road at each commissary (Peter, 10 October 2026: "nothing shows where the bay is"). A vehicle
    // is in the bay when the game calls it legally parked and its middle is within the bay radius (rules.json) of a
    // resolved bay point (Commissary.InBay; review round 4). The bay's zone (BayZone) is the stretches of the bay point's
    // parking lane that are legal parking across the paint's whole width, every half metre, and lie wholly within the
    // radius; the paint outlines that zone, so a vehicle standing wholly inside the paint is legally parked and in the
    // bay. A bay with no legal parking gets no zone and no paint (0.9.8 drew an assumed lane on a no-parking kerb). The
    // game's parking lanes are invisible colliders and its road paint is baked into the road textures or drawn as decal
    // quads, none of them a loading zone, so the bay is drawn as the game's road paint looks (0.9.10, Peter: "the purple
    // box breaks immersion"): a white outline in the game's paint colour, worn, and LOADING ZONE in the longest stretch,
    // with a loading zone sign at the kerb and props by the door (BayProps). The paint is one flat mesh a few
    // centimetres above the road with no collider, so it blocks no vehicle and changes no NavMesh. Once made it shows
    // like any street paint, wherever the player is outside; it is made when first needed (within MakeMetres on foot,
    // MakeMetresDriving in a food vehicle). BayParking keeps the zone free of parked cars.
    internal static class BayMarkers
    {
        private const float MakeMetres = 115f;
        private const float MakeMetresDriving = 300f;
        private const float LineMetres = 0.15f;
        private const float Lift = 0.025f;
        private const float TruckLengthMetres = 5.8f;
        // The paint's width: the lane's, at most this.
        private const float MaxWidthMetres = 3.0f;
        // The edge probes stand this far inside the paint's edges.
        private const float EdgeInsetMetres = 0.1f;
        // The game's decal road paint: a flat 188 sRGB (T_Line_*, T_Crosswalk_*), the fallback's colour.
        private static readonly Color Paint = new Color(0.74f, 0.74f, 0.72f);
        // The paint texture repeats every this many metres.
        private const float PaintTileMetres = 2f;

        private sealed class Marker
        {
            public Marker(GameObject? root, Mesh? mesh, Vector3 at, Vector3? door = null)
            {
                Root = root;
                Mesh = mesh;
                Drawn = root != null;
                At = at;
                Door = door;
            }

            // Null when the bay has no usable zone: nothing is drawn, and it isn't tried again this session.
            public GameObject? Root { get; }
            // The marker's own mesh, destroyed with it (a Mesh made at run time isn't freed with its GameObject).
            public Mesh? Mesh { get; }
            // Drawn once; a drawn marker whose object is gone (the scene reloaded under it) is drawn again.
            public bool Drawn { get; }
            public Vector3 At { get; }
            // The commissary's entrance nearest this bay, where its props go.
            public Vector3? Door { get; }

            public void Destroy()
            {
                if (Root != null) Object.Destroy(Root);
                if (Mesh != null) Object.Destroy(Mesh);
            }
        }

        // One bay's zone in the lane's own frame: Foot on the lane's middle line, Along the lane (the bay point's
        // direction), the paint's Width, and the stretches as metres along from Foot.
        internal sealed class BayZone
        {
            public BayZone(Vector3 foot, Vector3 along, float width, List<(float From, float To)> spans, float offset, float reach)
            {
                Foot = foot;
                Along = along;
                Width = width;
                Spans = spans;
                Offset = offset;
                Reach = reach;
            }

            public Vector3 Foot { get; }
            public Vector3 Along { get; }
            public Vector3 Across => new Vector3(Along.z, 0f, -Along.x);
            public float Width { get; }
            public List<(float From, float To)> Spans { get; }
            // The lane's middle across from the bay point, and how far the zone may reach either way along it.
            public float Offset { get; }
            public float Reach { get; }

            // Whether a point lies in one of the stretches (flat), with `margin` metres added all round.
            public bool Contains(Vector3 point, float margin)
            {
                var d = point - Foot;
                var a = Vector3.Dot(d, Along);
                var c = Vector3.Dot(d, Across);
                if (Mathf.Abs(c) > Width / 2f + margin) return false;
                return Spans.Any(s => a >= s.From - margin && a <= s.To + margin);
            }
        }

        // Each site's markers by bay (its index among the site's resolved bay points).
        private static readonly Dictionary<string, Dictionary<int, Marker>> markers = new Dictionary<string, Dictionary<int, Marker>>(StringComparer.Ordinal);
        // Each site's zones by bay, worked out once per load (null: none usable). BayParking reads them too.
        private static readonly Dictionary<string, Dictionary<int, BayZone?>> zones = new Dictionary<string, Dictionary<int, BayZone?>>(StringComparer.Ordinal);
        // A bay whose traffic lanes aren't there yet (the traffic system still starting) is tried again after a while:
        // reading the lanes scans every waypoint in the city.
        private const float RetrySeconds = 10f;
        private static readonly Dictionary<string, float> retryAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static Material? material;
        private static bool failed;
        // HDRP rendering layer 0 only, as the game's vehicles have: street dirt decals (decal layer 0, bit 8) skip the bay's
        // paint and everything made for it at run time (the build sets the bundle's bay pieces the same way, SurfaceLook.NoStreetDecals).
        internal const uint NoDecalsRenderingLayers = 1;
        // Commissaries whose props by the door are decided: placed, or refused for good, or out of tries.
        private static readonly HashSet<string> propsMade = new HashSet<string>(StringComparer.Ordinal);
        // Tries so far for commissaries whose props are not decided yet, and when the next may run. A try that found the
        // street not ready, or a vehicle or the player in a prop's place, is undone and run again while the player is near.
        private static readonly Dictionary<string, (int Tries, float At)> propsTries = new Dictionary<string, (int, float)>(StringComparer.Ordinal);
        private const int MaxPropTries = 6;
        private const float PropRetrySeconds = 10f;

        // Twice a second from Commissary.Look: markers made when first needed, then shown or hidden.
        public static void Update(CommissaryTables sites, double radius, Vector3 player, bool inFoodVehicle, bool inside)
        {
            if (failed) return;
            try
            {
                foreach (var site in sites.Sites)
                {
                    // Only resolved bays on legal parking are painted; Commissary logs a commissary that has none.
                    var bays = Commissary.ResolvedBays(site);
                    if (bays == null || bays.Count == 0) continue;
                    if (!markers.TryGetValue(site.Id, out var bySite)) markers[site.Id] = bySite = new Dictionary<int, Marker>();
                    // Drawn from the parking colliders and the road around it, so only with the player within a few
                    // hundred metres, where the street is loaded; farther than that the paint is too small to see anyway.
                    var near = bays.Any(b => Flat(player, (b.X, b.Z)) <= (inFoodVehicle ? MakeMetresDriving : MakeMetres));
                    var missing = Enumerable.Range(0, bays.Count).Where(i => !bySite.TryGetValue(i, out var m) || (m.Drawn && m.Root == null)).ToList();
                    // A bay point with nothing drawn yet is checked for parked cars right before its markers: none is
                    // made while a lane car stands in its zone (BayParking.ClearBeforePaint; a car in view keeps it
                    // waiting until it is out of view and swept), so no car stands in fresh paint.
                    // Per bay point: a point without markers is checked even when another point is drawn; drawn markers
                    // are left alone.
                    var ready = missing.Count > 0 && near && !(retryAt.TryGetValue(site.Id, out var at) && Time.unscaledTime < at)
                        ? missing.Where(i => BayParking.ClearBeforePaint(site, ZoneAt(site, i, (float)radius))).ToList()
                        : new List<int>();
                    if (ready.Count > 0)
                    {
                        retryAt[site.Id] = Time.unscaledTime + RetrySeconds;
                        var doors = World.EntrancesOf(site.Address);
                        foreach (var i in ready)
                        {
                            if (bySite.TryGetValue(i, out var gone))
                            {
                                gone.Destroy();
                                bySite.Remove(i);
                                // Its props may have gone with it: they are made again.
                                if (gone.Drawn) ForgetProps(site.Id);
                            }
                            var made = Make(site, i, bays[i], doors, (float)radius, player);
                            if (made != null) bySite[i] = made;
                        }
                    }
                    // Street paint and furniture: shown wherever the player is outside, hidden indoors.
                    foreach (var marker in bySite.Values)
                    {
                        if (marker.Root == null) continue;
                        var show = !inside;
                        if (marker.Root.activeSelf != show) marker.Root.SetActive(show);
                    }
                    if (near && !inside) FurnishProps(site, bySite);
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Log.Error("Commissary markers: " + ex + " No bay markers until the next load.");
                RemoveAll();
            }
        }

        // After the uninstall step and on unload.
        public static void RemoveAll()
        {
            foreach (var marker in markers.Values.SelectMany(m => m.Values)) marker.Destroy();
            markers.Clear();
            propsMade.Clear();
            propsTries.Clear();
        }

        // Called on each load: a new city gets new markers.
        public static void Reset()
        {
            RemoveAll();
            zones.Clear();
            retryAt.Clear();
            failed = false;
        }

        // A commissary's bay was resolved again: its markers and zones are made afresh.
        public static void Forget(string siteId)
        {
            if (markers.TryGetValue(siteId, out var bySite))
            {
                foreach (var marker in bySite.Values) marker.Destroy();
                markers.Remove(siteId);
                ForgetProps(siteId);
            }
            zones.Remove(siteId);
            retryAt.Remove(siteId);
        }

        // The site's zones (worked out now if not yet; null entries have none). Empty while the bay isn't resolved.
        internal static List<BayZone> ZonesOf(CommissarySite site, float radius)
        {
            var bays = Commissary.ResolvedBays(site);
            var list = new List<BayZone>();
            if (bays == null) return list;
            var doors = World.EntrancesOf(site.Address);
            if (!zones.TryGetValue(site.Id, out var bySite)) zones[site.Id] = bySite = new Dictionary<int, BayZone?>();
            for (var i = 0; i < bays.Count; i++)
            {
                if (!bySite.TryGetValue(i, out var zone))
                {
                    // Probed round the nearest entrance's height (the probe reads the lane's own height from its collider).
                    var y = doors == null || doors.Length == 0 ? 0f : doors.OrderBy(d => Flat(d, (bays[i].X, bays[i].Z))).First().y;
                    zone = Zone(site, bays[i], radius, y, out var problem);
                    if (zone == null) Log.Error($"Commissary marker {site.Id}: {problem}; no paint and no parked-car keep-out there.");
                    bySite[i] = zone;
                }
                if (zone != null) list.Add(zone);
            }
            return list;
        }

        // One bay point's zone (worked out now if not yet), or null when it has none.
        internal static BayZone? ZoneAt(CommissarySite site, int index, float radius)
        {
            ZonesOf(site, radius);
            return zones.TryGetValue(site.Id, out var bySite) && bySite.TryGetValue(index, out var zone) ? zone : null;
        }

        // The bay's zone: the lane's middle and width across the bay point, then every half metre along the lane the
        // middle and both edges of the paint (EdgeInsetMetres inside) on legal parking, with the truck lined up with the
        // lane (the game's probe box turned the same way). A lane that bends or narrows ends the stretch there.
        private static BayZone? Zone(CommissarySite site, CommissaryBayPoint bay, float radius, float y, out string problem)
        {
            var along = new Vector3((float)bay.AlongX, 0f, (float)bay.AlongZ).normalized;
            var across = new Vector3(along.z, 0f, -along.x);
            var at = new Vector3((float)bay.X, y, (float)bay.Z);
            var probe = new Legality.ParkingProbe(at, radius + 5f);
            bool Legal(Vector3 p) => probe.Legal(p.x, p.z, along.x, along.z);
            var centred = BaySnap.Centred(at.x, at.z, (along.x, along.z), (x, z, ax, az) => probe.Legal(x, z, ax, az));
            if (centred == null)
            {
                problem = $"no legal parking under the bay at x {at.x:F1}, z {at.z:F1}";
                return null;
            }
            // The legal run across, edge to edge (the probe box finds the lane 0.1 m beyond each edge).
            float first = 0f, last = 0f;
            for (var s = 0f; s >= -(float)BaySnap.CentreProbeMetres && Legal(at + across * (s - 0.05f)); s -= 0.05f) first = s - 0.05f;
            for (var s = 0f; s <= (float)BaySnap.CentreProbeMetres && Legal(at + across * (s + 0.05f)); s += 0.05f) last = s + 0.05f;
            var offset = (first + last) / 2f;
            var width = Mathf.Min(last - first - 2f * Legality.WheelBoxHalfWidth, MaxWidthMetres);
            var foot = at + across * offset;
            var half = width / 2f;
            // The whole paint lies within the radius of the bay point, so a vehicle wholly inside it is within it.
            var reach = Mathf.Sqrt(Mathf.Max(0f, radius * radius - (Mathf.Abs(offset) + half) * (Mathf.Abs(offset) + half)));
            var edge = half - EdgeInsetMetres;
            var stretches = BayLayout.Stretches(reach, x =>
            {
                var p = foot + along * (float)x;
                return Legal(p) && Legal(p + across * edge) && Legal(p - across * edge);
            });
            if (stretches.Source == BayStretchSource.NoneUsable)
            {
                problem = $"the parking lane at x {foot.x:F1}, z {foot.z:F1} has no stretch of {BayLayout.MinStretchMetres:F0} m or more that is legal parking across " +
                          $"{width:F1} m within the bay ({reach:F1} m either way)";
                return null;
            }
            problem = "";
            return new BayZone(foot, along, width, stretches.Spans.Select(sp => ((float)sp.From, (float)sp.To)).ToList(), offset, reach);
        }

        // One bay's marker, or null while its traffic lanes aren't known (tried again later). Nothing is created
        // before every check has passed.
        private static Marker? Make(CommissarySite site, int index, CommissaryBayPoint bay, Vector3[]? doors, float radius, Vector3 player)
        {
            // The bay's height: its nearest entrance's, else the player's (the zone's probes use the lane's own height).
            var door = doors == null || doors.Length == 0 ? (Vector3?)null : doors.OrderBy(d => Flat(d, (bay.X, bay.Z))).First();
            var y = door?.y ?? player.y;
            var at = new Vector3((float)bay.X, y, (float)bay.Z);
            var lanes = TrafficLanes.Near(at, 40f);
            if (!lanes.Known) return null;
            ZonesOf(site, radius);
            var zone = zones.TryGetValue(site.Id, out var bySite) && bySite.TryGetValue(index, out var z) ? z : null;
            if (zone == null) return new Marker(null, null, at);
            return Draw(site, index, zone, at, door, radius, lanes);
        }

        private static Marker Draw(CommissarySite site, int index, BayZone zone, Vector3 bay, Vector3? door, float radius, TrafficLanes lanes)
        {
            // The pavement side: away from the nearest driving lane; with none near, towards the garage's door (review
            // round 4), so COMMISSARY reads from the pavement either way.
            var across = zone.Across;
            var nearest = lanes.Nearest(zone.Foot);
            var groundY = bay.y;
            if (nearest != null)
            {
                var fromLane = zone.Foot - nearest.Value.Point;
                fromLane.y = 0f;
                if (Vector3.Dot(fromLane, across) < 0f) across = -across;
                groundY = nearest.Value.Point.y;
            }
            else if (door != null)
            {
                var toDoor = door.Value - zone.Foot;
                toDoor.y = 0f;
                if (Vector3.Dot(toDoor, across) < 0f) across = -across;
            }
            // Along the street, as the paint's local +x (the root faces -across). The zone's stretches are measured along
            // its own direction; when the paint's axis runs the other way they are mirrored, so each is painted where it was found.
            var axis = BayLayout.PaintAxis(across.x, across.z);
            var along = new Vector3((float)axis.X, 0f, (float)axis.Z);
            var mirrored = Vector3.Dot(along, zone.Along) < 0f;
            var spans = zone.Spans.Select(sp => mirrored ? (-sp.To, -sp.From) : (sp.From, sp.To)).OrderBy(sp => sp.Item1).ToList();
            var foot = zone.Foot;
            foot.y = groundY;
            var width = zone.Width;

            // Local +z points to the road and +x along it, so the word reads from the pavement.
            var rotation = Quaternion.LookRotation(-across, Vector3.up);
            var longest = spans.OrderByDescending(sp => sp.Item2 - sp.Item1).First();
            var mesh = BuildMesh(Matrix4x4.TRS(foot, rotation, Vector3.one), spans, width, groundY);
            GameObject? root = null;
            var furniture = new List<string>();
            try
            {
                root = new GameObject("FoodTrucks.CommissaryBay." + site.Id);
                root.SetActive(false);
                root.transform.SetPositionAndRotation(foot, rotation);
                var filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Material();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                // Made at run time, so not covered by the build's setting: no street dirt on the paint either.
                renderer.renderingLayerMask = NoDecalsRenderingLayers;
                float GroundAt(Vector3 p) => Ground(p, groundY);
                var parent = root.transform;
                // The furniture is looks only: one that fails is logged and the paint stays.
                void Furnish(string what, Func<string> make)
                {
                    try { furniture.Add(make()); }
                    catch (Exception ex) { furniture.Add($"no {what} ({ex.GetType().Name}: {ex.Message})"); }
                }
                Furnish("lettering", () => BayProps.Lettering(parent, longest, width, GroundAt));
                Furnish("sign", () => BayProps.Sign(parent, site.Id, longest, width, GroundAt, groundY));
            }
            catch
            {
                if (root != null) Object.Destroy(root);
                Object.Destroy(mesh);
                throw;
            }

            var length = spans.Sum(sp => sp.Item2 - sp.Item1);
            var fits = spans.Any(sp => sp.Item2 - sp.Item1 >= TruckLengthMetres);
            Log.Info($"Commissary marker {site.Id}: bay paint at x {foot.x:F1}, z {foot.z:F1}, along {Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg:F0} degrees, " +
                     $"{spans.Count} {(spans.Count == 1 ? "stretch" : "stretches")} of {length:F1} m ({string.Join(", ", spans.Select(sp => $"{sp.Item1:+0.0;-0.0}…{sp.Item2:+0.0;-0.0} m"))}), " +
                     $"{width:F2} m wide, legal parking at its middle and both edges every half metre; the lane's middle {zone.Offset:+0.00;-0.00} m across from the bay point" +
                     $"{(nearest != null ? $", {Mathf.Abs(Vector3.Dot(foot - nearest.Value.Point, across)):F1} m from the driving lane" : ", no driving lane near (faces the door)")}; " +
                     $"the paint lies within the {radius:F0} m bay ({zone.Reach:F1} m either way along); " +
                     $"a {TruckLengthMetres:F1} m truck {(fits ? "fits wholly inside" : "can't stand wholly inside")}; " +
                     string.Join("; ", furniture) + ".");
            return new Marker(root, mesh, foot, door);
        }

        // One set of props per commissary, by the door nearest its first drawn bay, under that bay's marker. Decided once it
        // is placed or refused for good; a try the street wasn't ready for, or that found a vehicle or the player in a
        // prop's place, is undone and run again PropRetrySeconds later while the player is near, MaxPropTries in all.
        private static void FurnishProps(CommissarySite site, Dictionary<int, Marker> bySite)
        {
            if (propsMade.Contains(site.Id)) return;
            var host = bySite.OrderBy(p => p.Key).Select(p => p.Value).FirstOrDefault(m => m.Root != null && m.Door != null);
            if (host == null) return;
            propsTries.TryGetValue(site.Id, out var tries);
            if (tries.Tries > 0 && Time.unscaledTime < tries.At) return;
            var door = host.Door!.Value;
            var old = host.Root!.transform.Find(PropsName);
            if (old != null) Object.Destroy(old.gameObject);
            var holder = new GameObject(PropsName);
            holder.transform.SetParent(host.Root.transform, false);
            BayProps.Outcome outcome;
            try
            {
                var doorLanes = TrafficLanes.Near(door, 40f);
                outcome = doorLanes.Known
                    ? BayProps.ByTheDoor(holder.transform, door, doorLanes, World.BuildingAt(site.Address))
                    : new BayProps.Outcome("no props yet (no traffic lanes near the door)", true);
            }
            catch (Exception ex) { outcome = new BayProps.Outcome($"no props ({ex.GetType().Name}: {ex.Message})", true); }
            var attempt = tries.Tries + 1;
            if (outcome.TryAgain && attempt < MaxPropTries)
            {
                // Undone in full, so a later try never stacks a second set on the first.
                Object.Destroy(holder);
                propsTries[site.Id] = (attempt, Time.unscaledTime + PropRetrySeconds);
                Log.Info($"Commissary props {site.Id}, try {attempt}: {outcome.Line}; trying again in {PropRetrySeconds:F0} s.");
                return;
            }
            propsMade.Add(site.Id);
            propsTries.Remove(site.Id);
            Log.Info($"Commissary props {site.Id}{(attempt > 1 ? $", try {attempt}" : "")}: {outcome.Line}.");
        }

        private const string PropsName = "Props";

        private static void ForgetProps(string siteId)
        {
            propsMade.Remove(siteId);
            propsTries.Remove(siteId);
        }

        // The outline of each stretch, as flat quads following the road's height: long edges are cut at every metre, where
        // the road is sampled. UVs in metres over PaintTileMetres, so the worn paint texture repeats along the lines.
        private static Mesh BuildMesh(Matrix4x4 local, List<(float From, float To)> spans, float width, float groundY)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var half = width / 2f;
            var footY = local.MultiplyPoint3x4(Vector3.zero).y;
            var heights = new Dictionary<(int, int), float>();
            float Sample(int key, float z)
            {
                // The road's height at a whole metre along the bay, at this vertex's own distance across it: a kerb or
                // camber under one edge lifts only that edge's vertices (review round 2; a first version took the
                // highest of three samples for the whole width, so a kerb under one edge lifted the other too).
                var cell = (key, Mathf.RoundToInt(z * 100f));
                if (heights.TryGetValue(cell, out var h)) return h;
                h = Ground(local.MultiplyPoint3x4(new Vector3(key, 0f, z)), groundY) - footY + Lift;
                heights[cell] = h;
                return h;
            }
            float Height(float x, float z)
            {
                var a = Mathf.FloorToInt(x);
                var t = x - a;
                return t < 0.001f ? Sample(a, z) : Mathf.Lerp(Sample(a, z), Sample(a + 1, z), t);
            }
            void Quad(float x0, float z0, float x1, float z1)
            {
                var i = vertices.Count;
                vertices.Add(new Vector3(x0, Height(x0, z0), z0));
                vertices.Add(new Vector3(x0, Height(x0, z1), z1));
                vertices.Add(new Vector3(x1, Height(x1, z1), z1));
                vertices.Add(new Vector3(x1, Height(x1, z0), z0));
                uvs.Add(new Vector2(x0, z0) / PaintTileMetres);
                uvs.Add(new Vector2(x0, z1) / PaintTileMetres);
                uvs.Add(new Vector2(x1, z1) / PaintTileMetres);
                uvs.Add(new Vector2(x1, z0) / PaintTileMetres);
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Edge(float from, float to, float z0, float z1)
            {
                var cuts = BayLayout.Cuts(from, to);
                for (var k = 0; k + 1 < cuts.Count; k++) Quad((float)cuts[k], z0, (float)cuts[k + 1], z1);
            }
            foreach (var (from, to) in spans)
            {
                Edge(from, to, -half, -half + LineMetres);
                Edge(from, to, half - LineMetres, half);
                Quad(from, -half + LineMetres, from + LineMetres, half - LineMetres);
                Quad(to - LineMetres, -half + LineMetres, to, half - LineMetres);
            }
            var mesh = new Mesh { name = "FoodTrucks.CommissaryBay" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, vertices.Count).ToList());
            mesh.RecalculateBounds();
            return mesh;
        }

        // The road's height at a point: the highest surface a little above the lane's height that isn't a vehicle or a
        // person, so a parked car or a pedestrian isn't painted on.
        private static float Ground(Vector3 point, float laneY)
        {
            var hits = Physics.RaycastAll(new Vector3(point.x, laneY + 2f, point.z), Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var best = float.MinValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.point.y > laneY + 0.5f) continue;
                if (hit.collider.GetComponentInParent<VehicleController>() != null || hit.collider.GetComponentInParent<ThirdPersonCharacter>() != null) continue;
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            return best > float.MinValue ? best : laneY;
        }

        // Road paint: the mod's worn white (M_BayPaint, art/commissary), else the cart's HDRP/Lit material copied in the
        // game's paint colour with no texture.
        private static Material Material()
        {
            if (material != null) return material;
            var made = BayProps.Material("M_BayPaint");
            if (made != null) return material = made;
            Log.Error("Commissary markers: M_BayPaint missing from the mod's bundle; plain white paint instead.");
            var source = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(FoodTrucksMod.CartPrefabPath)?.GetComponentsInChildren<Renderer>(true)
                .Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            material = source != null ? new Material(source) : new Material(Shader.Find("HDRP/Lit"));
            material.name = "FoodTrucks.CommissaryBayPaint";
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Paint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Paint);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            return material;
        }

        private static float Flat(Vector3 a, (double X, double Z) b) => new Vector2(a.x - (float)b.X, a.z - (float)b.Z).magnitude;
        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    }
}
