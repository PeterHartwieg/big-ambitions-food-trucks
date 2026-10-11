#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // A painted bay on the road at each commissary (Peter, 10 October 2026: "nothing shows where the bay is"). A vehicle
    // is in the bay when its middle is within the bay radius (rules.json) of the bay point, which Commissary has snapped
    // to the nearest legal parking (BaySnap). The paint covers the stretches of legal parking along that lane whose
    // middle is within the radius, so a vehicle standing wholly inside the paint is in the bay and legally parked. A
    // bay with no legal parking found gets no paint (0.9.8 drew an assumed lane on a no-parking kerb). The game draws no parking or loading zones of its own in the street (its
    // parking lanes are invisible colliders and its delivery spots are inside buildings), so the bay is drawn like road
    // paint: an outline and the word COMMISSARY in the commissary colour of the map filter, lit like the road. It is one
    // flat mesh a few centimetres above the road with no collider, so it blocks no vehicle and changes no NavMesh.
    // It shows while the player is within ShowMetres of it, or anywhere while driving or pushing a food vehicle.
    internal static class BayMarkers
    {
        private const float ShowMetres = 100f;
        private const float MakeMetresDriving = 300f;
        private const float LineMetres = 0.15f;
        private const float Lift = 0.025f;
        // How far either way across the lane the paint looks for the lane's edges.
        private const float AcrossProbeMetres = 2.0f;
        private const float TruckLengthMetres = 5.8f;
        private static readonly Color Paint = MapFilters.CommissaryColour;

        private sealed class Marker
        {
            public Marker(GameObject? root, Mesh? mesh, Vector3 at)
            {
                Root = root;
                Mesh = mesh;
                Drawn = root != null;
                At = at;
            }

            // Null when the bay doesn't reach the kerb or has no usable parking: nothing is drawn, and it isn't tried
            // again this session.
            public GameObject? Root { get; }
            // The marker's own mesh, destroyed with it (a Mesh made at run time isn't freed with its GameObject).
            public Mesh? Mesh { get; }
            // Drawn once; a drawn marker whose object is gone (the scene reloaded under it) is drawn again.
            public bool Drawn { get; }
            public Vector3 At { get; }

            public void Destroy()
            {
                if (Root != null) Object.Destroy(Root);
                if (Mesh != null) Object.Destroy(Mesh);
            }
        }

        // Each site's markers by bay (its index among the site's distinct bay points): a bay drawn stays drawn while
        // another of the same site waits for its traffic lanes.
        private static readonly Dictionary<string, Dictionary<int, Marker>> markers = new Dictionary<string, Dictionary<int, Marker>>(StringComparer.Ordinal);
        // A bay whose traffic lanes aren't there yet (the traffic system still starting) is tried again after a while:
        // reading the lanes scans every waypoint in the city.
        private const float RetrySeconds = 10f;
        private static readonly Dictionary<string, float> retryAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static Material? material;
        private static bool failed;

        // Twice a second from Commissary.Look: markers made when first needed, then shown or hidden.
        public static void Update(CommissaryTables sites, double radius, Vector3 player, bool inFoodVehicle, bool inside)
        {
            if (failed) return;
            try
            {
                foreach (var site in sites.Sites)
                {
                    // Only bays on legal parking are painted; Commissary logs a bay that has none.
                    var bays = Commissary.SnappedBays(site);
                    if (bays == null || bays.Count == 0) continue;
                    if (!markers.TryGetValue(site.Id, out var bySite)) markers[site.Id] = bySite = new Dictionary<int, Marker>();
                    // Drawn from the parking colliders and the road around it, so only with the player within a few
                    // hundred metres, where the street is loaded; farther than that the paint is too small to see anyway.
                    var near = bays.Any(b => Flat(player, (b.X, b.Z)) <= (inFoodVehicle ? MakeMetresDriving : ShowMetres + (float)radius));
                    // Not made yet (or the scene was reloaded under them): made once the player is near or driving,
                    // when the traffic lanes they are drawn from are there.
                    var missing = Enumerable.Range(0, bays.Count).Where(i => !bySite.TryGetValue(i, out var m) || (m.Drawn && m.Root == null)).ToList();
                    if (missing.Count > 0 && near && !(retryAt.TryGetValue(site.Id, out var at) && Time.unscaledTime < at))
                    {
                        retryAt[site.Id] = Time.unscaledTime + RetrySeconds;
                        var doors = World.EntrancesOf(site.Address);
                        foreach (var i in missing)
                        {
                            if (bySite.TryGetValue(i, out var gone))
                            {
                                gone.Destroy();
                                bySite.Remove(i);
                            }
                            var made = Make(site, bays[i], doors, radius, player);
                            if (made != null) bySite[i] = made;
                        }
                    }
                    foreach (var marker in bySite.Values)
                    {
                        if (marker.Root == null) continue;
                        var show = !inside && (inFoodVehicle || Flat(player, marker.At) <= ShowMetres);
                        if (marker.Root.activeSelf != show) marker.Root.SetActive(show);
                    }
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
        }

        // Called on each load: a new city gets new markers.
        public static void Reset()
        {
            RemoveAll();
            retryAt.Clear();
            failed = false;
        }

        // One bay's marker, or null while its traffic lanes aren't known (tried again later). Nothing is created
        // before every check has passed.
        private static Marker? Make(CommissarySite site, BaySnapResult bay, Vector3[]? doors, double radius, Vector3 player)
        {
            // The bay's height: its nearest entrance's (the bay is on the street by it), else the player's.
            var door = doors == null || doors.Length == 0 ? (Vector3?)null : doors.OrderBy(d => Flat(d, (bay.X, bay.Z))).First();
            var y = door?.y ?? player.y;
            var at = new Vector3((float)bay.X, y, (float)bay.Z);
            var lanes = TrafficLanes.Near(at, 40f);
            if (!lanes.Known) return null;
            return Draw(site, at, new Vector3((float)bay.AlongX, 0f, (float)bay.AlongZ).normalized, (float)radius, lanes);
        }

        private static Marker Draw(CommissarySite site, Vector3 bay, Vector3 laneAlong, float radius, TrafficLanes lanes)
        {
            // Across the parking lane, towards the pavement: away from the nearest driving lane.
            var across = new Vector3(laneAlong.z, 0f, -laneAlong.x);
            var nearest = lanes.Nearest(bay);
            var groundY = bay.y;
            if (nearest != null)
            {
                var fromLane = bay - nearest.Value.Point;
                fromLane.y = 0f;
                if (Vector3.Dot(fromLane, across) < 0f) across = -across;
                groundY = nearest.Value.Point.y;
            }
            // Along the street, as the paint's local +x (the root faces -across): the lane is probed along the same signed
            // axis as the paint is drawn, so a stretch found on one side of the bay point is painted on that side.
            var axis = BayLayout.PaintAxis(across.x, across.z);
            var along = new Vector3((float)axis.X, 0f, (float)axis.Z);

            // The parking lane's edges across the bay point, by the same legality test the bay was found with.
            float first = float.NaN, last = float.NaN;
            for (var s = -AcrossProbeMetres; s <= AcrossProbeMetres + 0.001f; s += 0.1f)
            {
                if (!Legality.LegalParkingAt(bay + across * s)) continue;
                if (float.IsNaN(first)) first = s;
                last = s;
            }
            if (float.IsNaN(first))
            {
                Log.Error($"Commissary marker {site.Id}: no legal parking under the bay at x {bay.x:F1}, z {bay.z:F1}; nothing drawn.");
                return new Marker(null, null, bay);
            }
            var offset = (first + last) / 2f;
            // The probe box (0.1 m either side) finds the lane a little beyond its edges.
            var width = Mathf.Clamp(last - first - 0.2f, 2.0f, 3.0f);
            var foot = bay + across * offset;
            foot.y = groundY;
            var reach = Mathf.Sqrt(Mathf.Max(0f, radius * radius - offset * offset));

            // The stretches of legal parking within reach (every half metre along the lane's middle).
            var stretches = BayLayout.Stretches(reach, x => Legality.LegalParkingAt(foot + along * (float)x + Vector3.up * (bay.y - groundY)));
            if (stretches.Source == BayStretchSource.NoneUsable)
            {
                Log.Error($"Commissary marker {site.Id}: the parking lane at x {foot.x:F1}, z {foot.z:F1} has no stretch of legal parking of {BayLayout.MinStretchMetres:F0} m or more " +
                          $"within the bay ({reach:F1} m either way); nothing drawn.");
                return new Marker(null, null, bay);
            }
            var spans = stretches.Spans.Select(s => ((float)s.From, (float)s.To)).ToList();

            // Local +z points to the road and +x along it, so the word reads from the pavement.
            var rotation = Quaternion.LookRotation(-across, Vector3.up);
            var longest = spans.OrderByDescending(s => s.Item2 - s.Item1).First();
            var mesh = BuildMesh(Matrix4x4.TRS(foot, rotation, Vector3.one), spans, width, longest, groundY);
            GameObject? root = null;
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
            }
            catch
            {
                if (root != null) Object.Destroy(root);
                Object.Destroy(mesh);
                throw;
            }

            var length = spans.Sum(s => s.Item2 - s.Item1);
            var fits = spans.Any(s => s.Item2 - s.Item1 >= TruckLengthMetres);
            Log.Info($"Commissary marker {site.Id}: bay paint at x {foot.x:F1}, z {foot.z:F1}, along {Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg:F0} degrees, " +
                     $"{spans.Count} {(spans.Count == 1 ? "stretch" : "stretches")} of {length:F1} m ({string.Join(", ", spans.Select(s => $"{s.Item1:+0.0;-0.0}…{s.Item2:+0.0;-0.0} m"))}) " +
                     $"on legal parking {width:F1} m wide, its middle {offset:+0.0;-0.0} m across from the bay point" +
                     $"{(nearest != null ? $" ({Vector3.Dot(foot - nearest.Value.Point, across):F1} m from the driving lane)" : " (no driving lane near)")}; " +
                     $"the {radius:F0} m bay reaches {reach:F1} m either way along the lane; " +
                     $"a {TruckLengthMetres:F1} m truck {(fits ? "fits wholly inside" : "can't stand wholly inside: park its middle on the paint")}.");
            return new Marker(root, mesh, foot);
        }

        // The outline of each stretch and the word on the longest, as flat quads following the road's height: long edges
        // are cut at every metre, where the road is sampled.
        private static Mesh BuildMesh(Matrix4x4 local, List<(float From, float To)> spans, float width, (float From, float To) longest, float groundY)
        {
            var vertices = new List<Vector3>();
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
            // COMMISSARY in a 5 x 7 pixel font, each row's runs of pixels one quad.
            const string word = "COMMISSARY";
            var room = longest.To - longest.From - 1.0f;
            var pixel = Mathf.Min(0.16f, room / (word.Length * 6 - 1), (width - 0.8f) / 7f);
            if (pixel >= 0.05f)
            {
                var left = (longest.From + longest.To) / 2f - pixel * (word.Length * 6 - 1) / 2f;
                var bottom = -pixel * 7f / 2f;
                for (var c = 0; c < word.Length; c++)
                {
                    var rows = Glyphs[word[c]];
                    for (var r = 0; r < 7; r++)
                    {
                        var row = rows[r];
                        // Row 0 is the top of the letter: furthest towards the road (+z).
                        var z0 = bottom + (6 - r) * pixel;
                        for (var k = 0; k < 5;)
                        {
                            if (row[k] != '#') { k++; continue; }
                            var start = k;
                            while (k < 5 && row[k] == '#') k++;
                            Quad(left + (c * 6 + start) * pixel, z0, left + (c * 6 + k) * pixel, z0 + pixel);
                        }
                    }
                }
            }
            var mesh = new Mesh { name = "FoodTrucks.CommissaryBay" };
            mesh.SetVertices(vertices);
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

        // Road paint: the cart's own HDRP/Lit material, copied, with no texture and the commissary colour.
        private static Material Material()
        {
            if (material != null) return material;
            var source = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(FoodTrucksMod.CartPrefabPath)?.GetComponentsInChildren<Renderer>(true)
                .Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            material = source != null ? new Material(source) : new Material(Shader.Find("HDRP/Lit"));
            material.name = "FoodTrucks.CommissaryBayPaint";
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", null);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Paint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Paint);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            return material;
        }

        private static float Flat(Vector3 a, (double X, double Z) b) => new Vector2(a.x - (float)b.X, a.z - (float)b.Z).magnitude;
        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        };
    }
}
