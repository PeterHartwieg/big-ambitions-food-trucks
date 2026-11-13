#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    internal static class BayMarkers
    {
        private const float MakeMetres = 115f;
        private const float MakeMetresDriving = 300f;
        private const float LineMetres = 0.15f;
        private const float Lift = 0.025f;
        private const float TruckLengthMetres = 5.8f;
        private const float MaxWidthMetres = 3.0f;
        private const float LotWidthMetres = 2.5f;
        private const float EdgeInsetMetres = 0.1f;
        // The game's decal road paint: a flat 188 sRGB (T_Line_*, T_Crosswalk_*), the fallback's colour.
        private static readonly Color Paint = new Color(0.74f, 0.74f, 0.72f);
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

            public GameObject? Root { get; }
            public Mesh? Mesh { get; }
            public bool Drawn { get; }
            public Vector3 At { get; }
            public Vector3? Door { get; }

            public void Destroy()
            {
                if (Root != null) Object.Destroy(Root);
                if (Mesh != null) Object.Destroy(Mesh);
            }
        }

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
            public float Offset { get; }
            public float Reach { get; }

            public bool Contains(Vector3 point, float margin)
            {
                var d = point - Foot;
                var a = Vector3.Dot(d, Along);
                var c = Vector3.Dot(d, Across);
                if (Mathf.Abs(c) > Width / 2f + margin) return false;
                return Spans.Any(s => a >= s.From - margin && a <= s.To + margin);
            }
        }

        private static readonly Dictionary<string, Dictionary<int, Marker>> markers = new Dictionary<string, Dictionary<int, Marker>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<int, BayZone?>> zones = new Dictionary<string, Dictionary<int, BayZone?>>(StringComparer.Ordinal);
        private const float RetrySeconds = 10f;
        private static readonly Dictionary<string, float> retryAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static Material? material;
        private static bool failed;
        internal const uint NoDecalsRenderingLayers = 1;
        private static readonly HashSet<string> propsMade = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (int Tries, float At)> propsTries = new Dictionary<string, (int, float)>(StringComparer.Ordinal);
        private const int MaxPropTries = 6;
        private const float PropRetrySeconds = 10f;

        public static void Update(CommissaryTables sites, double radius, Vector3 player, bool inFoodVehicle, bool inside)
        {
            if (failed) return;
            try
            {
                foreach (var site in sites.Sites)
                {
                    var bays = Commissary.ResolvedBays(site);
                    if (bays == null || bays.Count == 0) continue;
                    if (!markers.TryGetValue(site.Id, out var bySite)) markers[site.Id] = bySite = new Dictionary<int, Marker>();
                    var near = bays.Any(b => Flat(player, (b.X, b.Z)) <= (inFoodVehicle ? MakeMetresDriving : MakeMetres));
                    var missing = Enumerable.Range(0, bays.Count).Where(i => !bySite.TryGetValue(i, out var m) || (m.Drawn && m.Root == null)).ToList();
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
                                if (gone.Drawn) ForgetProps(site.Id);
                            }
                            var made = Make(site, i, bays[i], doors, (float)radius, player);
                            if (made != null) bySite[i] = made;
                        }
                    }
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

        public static void RemoveAll()
        {
            foreach (var marker in markers.Values.SelectMany(m => m.Values)) marker.Destroy();
            markers.Clear();
            propsMade.Clear();
            propsTries.Clear();
        }

        public static void Reset()
        {
            RemoveAll();
            zones.Clear();
            retryAt.Clear();
            failed = false;
        }

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
                    var y = doors == null || doors.Length == 0 ? 0f : doors.OrderBy(d => Flat(d, (bays[i].X, bays[i].Z))).First().y;
                    zone = Zone(site, bays[i], radius, y, out var problem);
                    if (zone == null) Log.Error($"Commissary marker {site.Id}: {problem}; no paint and no parked-car keep-out there.");
                    bySite[i] = zone;
                }
                if (zone != null) list.Add(zone);
            }
            return list;
        }

        internal static BayZone? ZoneAt(CommissarySite site, int index, float radius)
        {
            ZonesOf(site, radius);
            return zones.TryGetValue(site.Id, out var bySite) && bySite.TryGetValue(index, out var zone) ? zone : null;
        }

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
            float first = 0f, last = 0f;
            for (var s = 0f; s >= -(float)BaySnap.CentreProbeMetres && Legal(at + across * (s - 0.05f)); s -= 0.05f) first = s - 0.05f;
            for (var s = 0f; s <= (float)BaySnap.CentreProbeMetres && Legal(at + across * (s + 0.05f)); s += 0.05f) last = s + 0.05f;
            var offset = (first + last) / 2f;
            var width = Mathf.Min(last - first - 2f * Legality.WheelBoxHalfWidth, MaxWidthMetres);
            if (site.InLot)
            {
                offset = 0f;
                width = Mathf.Min(LotWidthMetres, 2f * Mathf.Min(-first, last) - 2f * Legality.WheelBoxHalfWidth);
            }
            var foot = at + across * offset;
            var half = width / 2f;
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
            var axis = BayLayout.PaintAxis(across.x, across.z);
            var along = new Vector3((float)axis.X, 0f, (float)axis.Z);
            var mirrored = Vector3.Dot(along, zone.Along) < 0f;
            var spans = zone.Spans.Select(sp => mirrored ? (-sp.To, -sp.From) : (sp.From, sp.To)).OrderBy(sp => sp.Item1).ToList();
            var foot = zone.Foot;
            foot.y = groundY;
            var width = zone.Width;

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
                renderer.renderingLayerMask = NoDecalsRenderingLayers;
                float GroundAt(Vector3 p) => Ground(p, groundY);
                var parent = root.transform;
                void Furnish(string what, Func<string> make)
                {
                    try { furniture.Add(make()); }
                    catch (Exception ex) { furniture.Add($"no {what} ({ex.GetType().Name}: {ex.Message})"); }
                }
                Furnish("lettering", () => BayProps.Lettering(parent, longest, width, GroundAt));
                if (site.InLot)
                {
                    var nose = BaySnap.HeadingAxis(site.BayHeading!.Value);
                    var head = Vector3.Dot(along, new Vector3((float)nose.X, 0f, (float)nose.Z)) >= 0f ? 1 : -1;
                    Furnish("sign", () => BayProps.SignAtHead(parent, site.Id, longest, head, GroundAt));
                }
                else Furnish("sign", () => BayProps.Sign(parent, site.Id, longest, width, GroundAt, groundY));
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
