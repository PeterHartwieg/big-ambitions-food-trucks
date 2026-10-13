#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Helpers;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The bridge's `shoot`: one picture from a camera of the bridge's own, written as a PNG under
    // persistentDataPath/FoodTrucksTest/shots/. The game's camera and UI are left alone: the bridge's camera copies the
    // game camera's settings (and HDRP's per-camera data), renders into a texture of its own for a few frames so HDRP's
    // exposure and history settle, and is destroyed after the read-back. Screen-space UI isn't drawn by a camera that
    // renders into a texture, so the picture has none.
    //
    // What the game changes on buildings for its own camera is undone round each frame of the bridge's camera and put
    // back before the game's camera renders (TestBridgeUncover: the view-blocking fade and the hover outline). That fade
    // was the ghost facade of live sessions 3 to 6. The shader globals of the game's BuildingHider (GlobalPlayerPos,
    // _PO_Height, _PO_Height_Offset) are also moved out of reach for the frame and put back, as since 407e9a4: they
    // weren't the ghost's cause, but nothing shows they never cut anything, and the override is undone in the same frame.
    internal static class TestBridgeShoot
    {
        private const int Frames = 8;
        // The as-is frames of `compare`, rendered after the uncovered ones.
        private const int CompareFrames = 4;
        private static readonly int PlayerPosition = Shader.PropertyToID("GlobalPlayerPos");
        private static readonly int CutHeight = Shader.PropertyToID("_PO_Height");
        private static readonly int CutHeightOffset = Shader.PropertyToID("_PO_Height_Offset");

        // One frame of the bridge's camera: with `uncover`, without the game's fades and outlines for its own camera.
        // The scope's lists say what was (or, as-is, would have been) undone.
        private static TestBridgeUncover RenderFrame(Camera camera, bool uncover)
        {
            var savedPlayer = Shader.GetGlobalVector(PlayerPosition);
            var savedHeight = Shader.GetGlobalFloat(CutHeight);
            var savedOffset = Shader.GetGlobalFloat(CutHeightOffset);
            var t = camera.transform;
            TestBridgeUncover? scope = null;
            try
            {
                Shader.SetGlobalVector(PlayerPosition, t.position + Vector3.up * 1000f);
                Shader.SetGlobalFloat(CutHeight, 10000f);
                Shader.SetGlobalFloat(CutHeightOffset, 0f);
                scope = TestBridgeUncover.Begin(t.position, uncover);
                camera.Render();
                return scope;
            }
            finally
            {
                scope?.Dispose();
                Shader.SetGlobalVector(PlayerPosition, savedPlayer);
                Shader.SetGlobalFloat(CutHeight, savedHeight);
                Shader.SetGlobalFloat(CutHeightOffset, savedOffset);
            }
        }
        private const float EyeHeight = 1.65f;
        // HDRP isn't among the mod's compile references; its per-camera data is added and copied by reflection.
        private static readonly Type? HdCameraData =
            Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, Unity.RenderPipelines.HighDefinition.Runtime");
        private static readonly MethodInfo? CopyTo = HdCameraData?.GetMethod("CopyTo", new[] { HdCameraData });

        internal static object Shoot(JObject args)
        {
            var name = TestBridgeCommands.Need(args, "name", "the picture's file name, without .png");
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9._-]{1,80}$")) throw new BridgeError("name may use letters, digits, '.', '_' and '-' only.");
            var width = (int)(TestBridgeCommands.Num(args, "width") ?? 1600);
            var height = (int)(TestBridgeCommands.Num(args, "height") ?? 900);
            if (width < 16 || height < 16 || width > 3840 || height > 2160) throw new BridgeError("width and height must be between 16 and 3840 by 2160.");
            var (position, rotation, fov, note) = Pose(args);
            foreach (var flag in new[] { "compare", "game_view" })
                if (args[flag] != null && TestBridgeCommands.Bool(args, flag) == null) throw new BridgeError($"{flag} must be true or false.");
            // compare: also the as-is picture (<name>-asis.png), rendered after the uncovered one with nothing undone.
            // game_view: also the game's own frame (<name>-game.png), as the player sees it.
            var compare = TestBridgeCommands.Bool(args, "compare") == true;
            var gameView = TestBridgeCommands.Bool(args, "game_view") == true;

            var source = Camera.main;
            if (source == null) throw new BridgeError("The game has no main camera now.");
            var holder = new GameObject("FoodTrucks.TestBridge.Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = holder.AddComponent<Camera>();
            camera.CopyFrom(source);
            if (HdCameraData != null)
            {
                var ours = holder.AddComponent(HdCameraData);
                var theirs = source.GetComponent(HdCameraData);
                if (theirs != null && CopyTo != null) CopyTo.Invoke(theirs, new object[] { ours });
            }
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.1f;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            texture.Create();
            camera.targetTexture = texture;
            // Rendered by hand, once a frame (RenderFrame), never by itself.
            camera.enabled = false;

            var folder = Path.Combine(Application.persistentDataPath, "FoodTrucksTest", "shots");
            var path = Path.Combine(folder, name + ".png");
            var asIsPath = Path.Combine(folder, name + "-asis.png");
            var gamePath = Path.Combine(folder, name + "-game.png");
            var frames = 0;
            List<string> uncovered = new List<string>(), outlined = new List<string>();
            List<string> asIsUncovered = new List<string>(), asIsOutlined = new List<string>();
            string? gameError = null;
            var gameDone = !gameView;
            if (gameView)
            {
                var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
                pump.StartCoroutine(CaptureGameView(gamePath, error => { gameError = error; gameDone = true; }));
            }
            var released = false;
            void Release()
            {
                if (released) return;
                released = true;
                if (camera != null) camera.targetTexture = null;
                if (holder != null) Object.Destroy(holder);
                texture.Release();
                Object.Destroy(texture);
            }
            void Write(string to)
            {
                var previous = RenderTexture.active;
                RenderTexture.active = texture;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(to, image.EncodeToPNG());
                Object.Destroy(image);
            }
            var total = Frames + (compare ? CompareFrames : 0);
            return new Deferred(() =>
            {
                if (frames < total)
                {
                    // The camera renders once a frame; the picture is read after a few, so HDRP's exposure and history
                    // settle. With compare, the as-is frames follow the uncovered ones.
                    var uncover = frames < Frames;
                    var scope = RenderFrame(camera, uncover);
                    frames++;
                    if (uncover)
                    {
                        uncovered = scope.Uncovered;
                        outlined = scope.Outlined;
                        if (frames == Frames)
                        {
                            try { Write(path); }
                            catch { Release(); throw; }
                        }
                    }
                    else
                    {
                        asIsUncovered = scope.Uncovered;
                        asIsOutlined = scope.Outlined;
                        if (frames == total)
                        {
                            try { Write(asIsPath); }
                            catch { Release(); throw; }
                        }
                    }
                    if (frames < total) return null;
                    Release();
                }
                if (!gameDone) return null;
                var why = note == null ? "" : " (" + note + ")";
                var lists = (uncovered.Count == 0 ? "" : $"; uncovered {string.Join(", ", uncovered)}") +
                            (outlined.Count == 0 ? "" : $"; outline lifted from {string.Join(", ", outlined)}");
                Log.Info($"Test bridge: shot {name} ({width}x{height}) from x {position.x:F1}, y {position.y:F1}, z {position.z:F1}{why}{lists}: {path}.");
                var angles = rotation.eulerAngles;
                var reply = new JObject
                {
                    ["path"] = path,
                    ["camera"] = new JObject
                    {
                        ["x"] = TestBridgeState.Round(position.x), ["y"] = TestBridgeState.Round(position.y), ["z"] = TestBridgeState.Round(position.z),
                        ["yaw"] = TestBridgeState.Round(angles.y), ["pitch"] = TestBridgeState.Round(angles.x > 180f ? angles.x - 360f : angles.x), ["fov"] = fov,
                    },
                    ["pose_note"] = note,
                    ["uncovered"] = new JArray(uncovered),
                    ["outlined"] = new JArray(outlined),
                    ["asis_path"] = compare ? asIsPath : null,
                    ["game_path"] = gameView && gameError == null ? gamePath : null,
                    ["game_error"] = gameError,
                };
                if (compare)
                {
                    reply["asis_uncovered"] = new JArray(asIsUncovered);
                    reply["asis_outlined"] = new JArray(asIsOutlined);
                }
                return reply;
            }, Release);
        }

        // The game's own frame, as the player sees it (its camera, its UI), captured at the end of this frame.
        private static System.Collections.IEnumerator CaptureGameView(string to, Action<string?> done)
        {
            yield return new WaitForEndOfFrame();
            Texture2D? image = null;
            try
            {
                image = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.WriteAllBytes(to, image.EncodeToPNG());
                done(null);
            }
            catch (Exception ex) { done($"{ex.GetType().Name}: {ex.Message}"); }
            finally { if (image != null) Object.Destroy(image); }
        }

        // The camera's place: an explicit pose (x, y, z with yaw and pitch, or look_at) or a named pose round a target,
        // with a note when the named pose had to move to stand clear (null otherwise).
        private static (Vector3 Position, Quaternion Rotation, float Fov, string? Note) Pose(JObject args)
        {
            var fov = (float)(TestBridgeCommands.Num(args, "fov") ?? 60);
            if (fov < 5 || fov > 120) throw new BridgeError("fov must be 5 to 120 degrees.");
            var pose = TestBridgeCommands.Str(args, "pose");
            if (pose == null)
            {
                var x = TestBridgeCommands.Num(args, "x");
                var y = TestBridgeCommands.Num(args, "y");
                var z = TestBridgeCommands.Num(args, "z");
                if (x == null || y == null || z == null) throw new BridgeError("shoot needs \"pose\" and \"target\", or \"x\", \"y\", \"z\" (with \"yaw\" and \"pitch\", or \"look_at\", and \"fov\").");
                var position = new Vector3((float)x, (float)y, (float)z);
                if (args["look_at"] is JObject at)
                {
                    if (args["yaw"] != null || args["pitch"] != null) throw new BridgeError("shoot takes \"look_at\" or \"yaw\" and \"pitch\", not both.");
                    var ax = TestBridgeCommands.Num(at, "x");
                    var ay = TestBridgeCommands.Num(at, "y");
                    var az = TestBridgeCommands.Num(at, "z");
                    if (ax == null || ay == null || az == null) throw new BridgeError("look_at needs \"x\", \"y\" and \"z\".");
                    var look = new Vector3((float)ax, (float)ay, (float)az) - position;
                    if (look.sqrMagnitude < 1e-4f) throw new BridgeError("look_at is where the camera stands; give a point away from it.");
                    return (position, Quaternion.LookRotation(look), fov, InsideNote(position));
                }
                if (args["look_at"] != null) throw new BridgeError("look_at must be an object { \"x\", \"y\", \"z\" }.");
                var yaw = (float)(TestBridgeCommands.Num(args, "yaw") ?? 0);
                var pitch = (float)(TestBridgeCommands.Num(args, "pitch") ?? 0);
                return (position, Quaternion.Euler(pitch, yaw, 0f), fov, InsideNote(position));
            }
            var target = TestBridgeCommands.Need(args, "target", "a vehicle id, bay:<site> or commissary:<site>");
            var (ways, subject, weigh) = Ways(pose, target);
            var (from, lookAt, note) = Clear(ways, subject, weigh);
            var direction = lookAt - from;
            return (from, direction.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(direction) : Quaternion.identity, fov, note);
        }

        // One way to take a named pose: where the camera stands, the point it looks at, the nearest it may come to that
        // point along the line between them and still show the subject, and a few words for the note.
        private readonly struct Way
        {
            public Way(Vector3 from, Vector3 at, float minimum, string label)
            {
                From = from;
                At = at;
                Minimum = minimum;
                Label = label;
            }

            public Vector3 From { get; }
            public Vector3 At { get; }
            public float Minimum { get; }
            public string Label { get; }
        }

        // The ways to take a named pose, the pose's own first, and the subject's transform (its colliders never block).
        private static (List<Way> Ways, Transform? Subject, bool Weigh) Ways(string pose, string target)
        {
            var ways = new List<Way>();
            if (target.StartsWith("bay:", StringComparison.Ordinal))
            {
                var (point, along, side) = TestBridgeCommands.Bay(target.Substring(4));
                var pavement = Quaternion.Euler(0f, 90f * side, 0f) * along;
                switch (pose)
                {
                    case "bay-pavement":
                        ways.Add(new Way(Up(point + pavement * 6f, EyeHeight), Up(point, 1f), 2.5f, "from the pavement"));
                        ways.Add(new Way(Up(point + pavement * 4f, 5f), Up(point, 0.5f), 2.5f, "from above the pavement"));
                        ways.Add(new Way(Up(point - pavement * 6f, EyeHeight), Up(point, 1f), 2.5f, "from the road side"));
                        break;
                    case "bay-road":
                    {
                        // From the driving lane next to the bay, 12 m back along it, looking at the bay's middle.
                        var lane = TrafficLanes.Near(point, 25f).Nearest(point);
                        var from = lane != null ? lane.Value.Point - lane.Value.Direction * 12f : point + along * 12f - pavement * 5f;
                        var other = lane != null ? lane.Value.Point + lane.Value.Direction * 12f : point - along * 12f - pavement * 5f;
                        ways.Add(new Way(Up(from, 1.5f), Up(point, 0.5f), 4f, "from the lane behind the bay"));
                        ways.Add(new Way(Up(other, 1.5f), Up(point, 0.5f), 4f, "from the lane ahead of the bay"));
                        ways.Add(new Way(Up(from, 6f), Up(point, 0.5f), 4f, "from above the lane"));
                        break;
                    }
                    default:
                        throw new BridgeError($"A bay takes the poses bay-pavement and bay-road, not {pose}.");
                }
                return (ways, null, false);
            }
            if (target.StartsWith("commissary:", StringComparison.Ordinal))
            {
                if (pose != "door") throw new BridgeError($"A commissary takes the pose door, not {pose}.");
                // From the street side: 6 m along the walk from the door to the street, looking back at the entrance; then
                // other ways out of the building (towards the lane, straight out, turned up to 60 degrees either way), at
                // eye height and higher, for a door whose walk is cut off (live session 4, garment-district).
                var site = target.Substring(11);
                var (door, outward, view) = TestBridgeCommands.DoorView(site, 6f);
                var at = Up(door, 1.4f);
                ways.Add(new Way(Up(view, EyeHeight), at, 2f, "along the walk to the street"));
                var outs = new List<(Vector3 Dir, string Label)> { (outward, "straight out") };
                var lane = TrafficLanes.Near(door, 40f).Nearest(door);
                if (lane != null)
                {
                    var toLane = lane.Value.Point - door;
                    toLane.y = 0f;
                    if (toLane.sqrMagnitude > 1e-4f && Vector3.Dot(toLane.normalized, outward) > 0f) outs.Insert(0, (toLane.normalized, "towards the lane"));
                }
                foreach (var (dir, label) in outs.ToList())
                    foreach (var turn in new[] { 30f, -30f, 60f, -60f })
                        outs.Add((Quaternion.Euler(0f, turn, 0f) * dir, $"{label}, turned {turn:+0;-0} degrees"));
                foreach (var height in new[] { EyeHeight, 3.5f })
                    foreach (var (dir, label) in outs)
                        ways.Add(new Way(Up(door + dir * 6f, height), at, 2f, height > EyeHeight ? label + ", 3.5 m up" : label));
                return (ways, null, true);
            }
            var vehicle = TestBridgeCommands.FoodVehicle(target);
            var transform = TestBridgeCommands.Spawned(vehicle).transform;
            var (hatch, outward2, forward) = TestBridgeCommands.HatchOf(vehicle, transform);
            var middle = transform.position;
            var hatchLevel = new Vector3(hatch.x, middle.y, hatch.z);
            switch (pose)
            {
                case "hatch-pavement":
                    ways.Add(new Way(Up(hatchLevel + outward2 * 3f, EyeHeight), Up(hatchLevel, 1.9f), 1.2f, "from the pavement"));
                    break;
                case "hatch-close":
                    ways.Add(new Way(Up(hatchLevel + outward2 * 1.5f, EyeHeight), Up(hatchLevel, 1.9f), 0.8f, "from the pavement"));
                    break;
                case "vehicle-side":
                    // Live session 4: on pavements narrower than 6 m the camera stood inside the facade.
                    ways.Add(new Way(Up(middle + outward2 * 6f, 1.8f), Up(middle, 1.2f), 3f, "from the hatch side"));
                    ways.Add(new Way(Up(middle + outward2 * 4f, 4.5f), Up(middle, 1.2f), 3f, "from above the hatch side"));
                    ways.Add(new Way(Up(middle - outward2 * 6f, 1.8f), Up(middle, 1.2f), 3f, "from the other side"));
                    ways.Add(new Way(Up(middle + outward2 * 2f, 9f), Up(middle, 1f), 4f, "from high above the hatch side"));
                    break;
                case "vehicle-overhead":
                    ways.Add(new Way(Up(middle - forward * 8f, 12f), Up(middle, 0f), 5f, "from above behind"));
                    ways.Add(new Way(Up(middle - forward * 1f, 14f), Up(middle, 0f), 5f, "from straight above"));
                    break;
                case "queue":
                    // From 8 m along the pavement ahead of the hatch's front end, looking back along the line.
                    ways.Add(new Way(Up(hatchLevel + outward2 * 2.5f + forward * 9f, 1.8f), Up(hatchLevel + outward2 * 2.5f - forward * 3f, 1f), 4f, "along the pavement"));
                    ways.Add(new Way(Up(hatchLevel + outward2 * 2.5f + forward * 9f, 4.5f), Up(hatchLevel + outward2 * 2.5f - forward * 3f, 1f), 4f, "along the pavement, from above"));
                    break;
                case "cart-counter":
                    ways.Add(new Way(Up(middle + outward2 * 1.5f, 1.9f), Up(middle, 1f), 0.8f, "from the queue side"));
                    break;
                default:
                    throw new BridgeError($"A vehicle takes the poses hatch-pavement, hatch-close, vehicle-side, vehicle-overhead, queue and cart-counter, not {pose}.");
            }
            return (ways, transform, false);
        }

        // The first way whose line from the subject out to the camera is clear far enough (Core ShotClearance): the
        // camera at the pose's distance, or pulled in to stand in front of the first solid thing on that line, and never
        // inside anything. With `weigh` (the door), the way with the most room. The note says what moved; null when the
        // pose's own first way stood clear at its full distance.
        private static (Vector3 From, Vector3 At, string? Note) Clear(List<Way> ways, Transform? subject, bool weigh)
        {
            var fits = new List<(int Index, Vector3 From, double Distance, string? Blocker)>();
            for (var i = 0; i < ways.Count; i++)
            {
                var way = ways[i];
                var line = way.From - way.At;
                var wanted = line.magnitude;
                if (wanted < 1e-3f) continue;
                var direction = line / wanted;
                var (hit, blocker) = FirstSolid(way.At, direction, wanted + (float)FoodTrucks.Core.Common.ShotClearance.Margin + 0.5f, subject);
                var fit = FoodTrucks.Core.Common.ShotClearance.Fit(wanted, hit, way.Minimum);
                if (fit == null) continue;
                var from = way.At + direction * (float)fit.Value;
                if (Inside(from, subject)) continue;
                fits.Add((i, from, fit.Value, fit.Value < wanted - 1e-3 ? blocker : null));
                if (!weigh || (i == 0 && fit.Value >= wanted - 1e-3)) break;
            }
            if (fits.Count == 0)
                return (ways[0].From, ways[0].At, $"no way round stood clear (every line from the subject meets something nearer than the pose allows); taken {ways[0].Label} as asked");
            var best = weigh ? fits.OrderByDescending(f => Math.Round(f.Distance, 1)).ThenBy(f => f.Index).First() : fits[0];
            if (best.Index == 0 && best.Blocker == null) return (best.From, ways[0].At, null);
            var pulled = best.Blocker != null ? $", pulled in to {best.Distance:F1} m in front of {best.Blocker}" : "";
            var first = best.Index == 0 ? "" : $"{ways[0].Label} was blocked; ";
            return (best.From, ways[best.Index].At, $"{first}taken {ways[best.Index].Label}{pulled}");
        }

        private const float SweepRadius = 0.25f;

        // Whether a collider counts as something a camera can't stand in or see through for a pose: buildings, walls,
        // roofs, large props. Not the subject, people, vehicles (they move, and a car in front isn't a wall), triggers,
        // or thin things (poles, signs) a line passes beside.
        private static bool Solid(Collider c, Transform? subject, bool thinCounts, bool vehiclesCount = false)
        {
            if (c == null || c.isTrigger) return false;
            if (subject != null && c.transform.IsChildOf(subject)) return false;
            var layer = c.gameObject.layer;
            if (((1 << layer) & LayerHelper.humanLayerMask.value) != 0) return false;
            if (c.GetComponentInParent<PlayerController>() != null || c.GetComponentInParent<ThirdPersonCharacter>() != null) return false;
            var vehicle = layer == LayerHelper.VehiclesLayerIndex || layer == LayerHelper.AiVehiclesLayerIndex || layer == LayerHelper.ParkedVehiclesLayerIndex ||
                          c.GetComponentInParent<VehicleController>() != null;
            if (vehicle) return vehiclesCount;
            if (!thinCounts)
            {
                var size = c.bounds.size;
                if (Mathf.Max(size.x, size.z) < 0.8f) return false;
            }
            return true;
        }

        // The distance to the first solid thing along a line from the subject's point, and its name; null when clear. A
        // collider the sweep starts in (distance 0: the subject's own building at its door) doesn't count.
        private static (double? Distance, string? What) FirstSolid(Vector3 from, Vector3 direction, float length, Transform? subject)
        {
            double? nearest = null;
            string? what = null;
            foreach (var hit in Physics.SphereCastAll(from, SweepRadius, direction, length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance <= 0f || !Solid(hit.collider, subject, thinCounts: false)) continue;
                if (nearest != null && hit.distance >= nearest.Value) continue;
                nearest = hit.distance;
                what = $"{hit.collider.transform.root.name}/{hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)})";
            }
            return (nearest, what);
        }

        // A free camera is placed where it is asked to be; the reply says when that is inside something.
        private static string? InsideNote(Vector3 point) =>
            Inside(point, null) ? "the camera stands inside something solid (a building, a prop or a vehicle); the picture may be dark" : null;

        // Whether the camera's place is inside something solid, thin things and vehicles included.
        private static bool Inside(Vector3 point, Transform? subject) =>
            Physics.OverlapSphere(point, 0.15f, ~0, QueryTriggerInteraction.Ignore).Any(c => Solid(c, subject, thinCounts: true, vehiclesCount: true));

        // A point `metres` above the ground under it (the vehicle's own height where there is no ground below).
        private static Vector3 Up(Vector3 point, float metres)
        {
            var ground = point.y;
            if (Physics.Raycast(new Vector3(point.x, point.y + 30f, point.z), Vector3.down, out var hit, 60f, Helpers.LayerHelper.groundLayerMask, QueryTriggerInteraction.Ignore))
                ground = hit.point.y;
            return new Vector3(point.x, ground + metres, point.z);
        }
    }
}
