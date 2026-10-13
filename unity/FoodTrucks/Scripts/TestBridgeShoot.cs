#nullable enable
using System;
using System.IO;
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
    // The game cuts away the parts of buildings between a camera and the player (its building shader reads the player's
    // place from the shader global GlobalPlayerPos, which ThirdPersonCharacter sets every frame), so a building in front
    // of the bridge's camera with the player behind it came out as a pale ghost. The bridge's camera therefore renders
    // by hand (Camera.Render, which renders and submits at once) with GlobalPlayerPos moved behind it, and puts the
    // player's place back straight after, before the game's own camera renders the frame.
    internal static class TestBridgeShoot
    {
        private const int Frames = 8;
        private static readonly int PlayerPosition = Shader.PropertyToID("GlobalPlayerPos");

        // One frame of the bridge's camera, with nothing cut away for the player.
        private static void RenderUncut(Camera camera)
        {
            var saved = Shader.GetGlobalVector(PlayerPosition);
            var t = camera.transform;
            Shader.SetGlobalVector(PlayerPosition, t.position - t.forward * 500f);
            try { camera.Render(); }
            finally { Shader.SetGlobalVector(PlayerPosition, saved); }
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
            var (position, rotation, fov) = Pose(args);

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
            // Rendered by hand, once a frame (RenderUncut), never by itself.
            camera.enabled = false;

            var folder = Path.Combine(Application.persistentDataPath, "FoodTrucksTest", "shots");
            var path = Path.Combine(folder, name + ".png");
            var frames = 0;
            void Release()
            {
                if (camera != null) camera.targetTexture = null;
                if (holder != null) Object.Destroy(holder);
                texture.Release();
                Object.Destroy(texture);
            }
            return new Deferred(() =>
            {
                // The camera renders once a frame while enabled; the picture is read after a few.
                RenderUncut(camera);
                if (++frames < Frames) return null;
                try
                {
                    var previous = RenderTexture.active;
                    RenderTexture.active = texture;
                    var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    RenderTexture.active = previous;
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Object.Destroy(image);
                }
                finally { Release(); }
                Log.Info($"Test bridge: shot {name} ({width}x{height}) from x {position.x:F1}, y {position.y:F1}, z {position.z:F1}: {path}.");
                return new JObject { ["path"] = path };
            }, Release);
        }

        // The camera's place: an explicit pose (x, y, z, yaw, pitch, fov) or a named pose round a target.
        private static (Vector3 Position, Quaternion Rotation, float Fov) Pose(JObject args)
        {
            var fov = (float)(TestBridgeCommands.Num(args, "fov") ?? 60);
            var pose = TestBridgeCommands.Str(args, "pose");
            if (pose == null)
            {
                var x = TestBridgeCommands.Num(args, "x");
                var y = TestBridgeCommands.Num(args, "y");
                var z = TestBridgeCommands.Num(args, "z");
                if (x == null || y == null || z == null) throw new BridgeError("shoot needs \"pose\" and \"target\", or \"x\", \"y\", \"z\" (with \"yaw\", \"pitch\", \"fov\").");
                var yaw = (float)(TestBridgeCommands.Num(args, "yaw") ?? 0);
                var pitch = (float)(TestBridgeCommands.Num(args, "pitch") ?? 0);
                return (new Vector3((float)x, (float)y, (float)z), Quaternion.Euler(pitch, yaw, 0f), fov);
            }
            var target = TestBridgeCommands.Need(args, "target", "a vehicle id, bay:<site> or commissary:<site>");
            var (from, at) = Place(pose, target);
            var look = at - from;
            return (from, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look) : Quaternion.identity, fov);
        }

        // Where the camera stands and the point it looks at, for a named pose.
        private static (Vector3 From, Vector3 At) Place(string pose, string target)
        {
            if (target.StartsWith("bay:", StringComparison.Ordinal))
            {
                var (point, along, side) = TestBridgeCommands.Bay(target.Substring(4));
                var pavement = Quaternion.Euler(0f, 90f * side, 0f) * along;
                switch (pose)
                {
                    case "bay-pavement":
                        return (Up(point + pavement * 6f, EyeHeight), Up(point, 1f));
                    case "bay-road":
                    {
                        // From the driving lane next to the bay, 12 m back along it, looking at the bay's middle.
                        var lane = TrafficLanes.Near(point, 25f).Nearest(point);
                        var from = lane != null ? lane.Value.Point - lane.Value.Direction * 12f : point + along * 12f - pavement * 5f;
                        return (Up(from, 1.5f), Up(point, 0.5f));
                    }
                    default:
                        throw new BridgeError($"A bay takes the poses bay-pavement and bay-road, not {pose}.");
                }
            }
            if (target.StartsWith("commissary:", StringComparison.Ordinal))
            {
                if (pose != "door") throw new BridgeError($"A commissary takes the pose door, not {pose}.");
                // From the street side: 6 m along the walk from the door to the street, looking back at the entrance.
                var (door, _, view) = TestBridgeCommands.DoorView(target.Substring(11), 6f);
                return (Up(view, EyeHeight), Up(door, 1.4f));
            }
            var vehicle = TestBridgeCommands.FoodVehicle(target);
            var transform = TestBridgeCommands.Spawned(vehicle).transform;
            var (hatch, outward, forward) = TestBridgeCommands.HatchOf(vehicle, transform);
            var middle = transform.position;
            var hatchLevel = new Vector3(hatch.x, middle.y, hatch.z);
            switch (pose)
            {
                case "hatch-pavement":
                    return (Up(hatchLevel + outward * 3f, EyeHeight), Up(hatchLevel, 1.9f));
                case "hatch-close":
                    return (Up(hatchLevel + outward * 1.5f, EyeHeight), Up(hatchLevel, 1.9f));
                case "vehicle-side":
                    return (Up(middle + outward * 6f, 1.8f), Up(middle, 1.2f));
                case "vehicle-overhead":
                    return (Up(middle - forward * 8f, 12f), Up(middle, 0f));
                case "queue":
                    // From 8 m along the pavement ahead of the hatch's front end, looking back along the line.
                    return (Up(hatchLevel + outward * 2.5f + forward * 9f, 1.8f), Up(hatchLevel + outward * 2.5f - forward * 3f, 1f));
                case "cart-counter":
                    return (Up(middle + outward * 1.5f, 1.9f), Up(middle, 1f));
                default:
                    throw new BridgeError($"A vehicle takes the poses hatch-pavement, hatch-close, vehicle-side, vehicle-overhead, queue and cart-counter, not {pose}.");
            }
        }

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
