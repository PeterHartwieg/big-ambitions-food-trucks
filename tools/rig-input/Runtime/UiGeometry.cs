using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace AgentRigInput
{
    // `ui geometry`: where UI elements are in Unity screen pixels (bottom-left origin), with opaque targets the mouse
    // commands accept. A target is valid for this bridge session and layout generation only, and only while the
    // element stays where it was measured.
    internal sealed class UiGeometry
    {
        private sealed class Record
        {
            public Component Component;
            public string Session;
            public int Generation, Width, Height;
            public Vector2 Center;
        }

        private const int Kept = 512;
        private const float MovedPixels = 1f;
        private readonly Dictionary<string, Record> targets = new Dictionary<string, Record>();
        private readonly Queue<string> order = new Queue<string>();
        private readonly Dictionary<string, Type[]> types = new Dictionary<string, Type[]>();
        private int serial, width, height;
        private string scene;
        internal int Generation { get; private set; } = 1;

        // Every frame: a new screen size or scene starts a new layout generation, and old targets go.
        internal void Observe(string currentScene)
        {
            if (Screen.width == width && Screen.height == height && currentScene == scene) return;
            width = Screen.width; height = Screen.height; scene = currentScene;
            Generation++;
            Forget();
        }

        internal void Forget()
        {
            targets.Clear();
            order.Clear();
        }

        internal JObject Query(JObject args, string session)
        {
            MouseArgs.Allow(args, MouseArgs.GeometryFields);
            var limit = MouseArgs.Int(args, "max", 20, 1, 50);
            var found = new List<Component>();
            var target = args["target"];
            if (target != null)
            {
                if (target.Type != JTokenType.String) throw new MouseError("mouse_invalid", "target must be a string from ui geometry");
                found.Add(Live((string)target, session).Component);
            }
            else
            {
                var name = args["component"];
                if (name == null || name.Type != JTokenType.String || ((string)name).Length == 0)
                    throw new MouseError("mouse_invalid", "ui geometry needs \"component\" (a component type name, e.g. WorkShiftSliderHandle) or \"target\"");
                var suffix = args["path_suffix"];
                if (suffix != null && suffix.Type != JTokenType.String) throw new MouseError("mouse_invalid", "path_suffix must be a string");
                foreach (var type in TypesNamed((string)name))
                    foreach (var o in Object.FindObjectsOfType(type, false))
                        if (o is Component c && c.transform is RectTransform && (suffix == null || Matches(c.transform, (string)suffix))) found.Add(c);
                found = found.Distinct().OrderBy(c => IndexedPath(c.transform), StringComparer.Ordinal).ToList();
            }
            var elements = new JArray();
            foreach (var c in found.Take(limit)) elements.Add(Element(c, session));
            return new JObject
            {
                ["screen"] = new JObject { ["width"] = Screen.width, ["height"] = Screen.height, ["origin"] = "bottom-left" },
                ["frame"] = Time.frameCount,
                ["layout_generation"] = Generation,
                ["count"] = found.Count,
                ["truncated"] = found.Count > limit,
                ["elements"] = elements,
            };
        }

        private IEnumerable<Type> TypesNamed(string name)
        {
            if (!types.TryGetValue(name, out var list))
            {
                list = AppDomain.CurrentDomain.GetAssemblies().SelectMany(Util.Types)
                    .Where(t => (t.Name == name || t.FullName == name) && typeof(Component).IsAssignableFrom(t)).ToArray();
                types[name] = list;
            }
            if (list.Length == 0) throw new MouseError("mouse_invalid", $"no loaded component type named '{name}'");
            return list;
        }

        private static bool Matches(Transform t, string suffix) =>
            Path(t).EndsWith(suffix, StringComparison.Ordinal) || IndexedPath(t).EndsWith(suffix, StringComparison.Ordinal);

        internal static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        internal static string IndexedPath(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent) parts.Add($"{p.name}[{p.GetSiblingIndex()}]");
            parts.Reverse();
            return string.Join("/", parts);
        }

        private JObject Element(Component c, string session)
        {
            var rt = (RectTransform)c.transform;
            var projected = Project(rt);
            var center = Point(rt, rt.rect.center);
            var id = $"ui{Generation}.{++serial}";
            targets[id] = new Record { Component = c, Session = session, Generation = Generation, Width = Screen.width, Height = Screen.height, Center = center };
            order.Enqueue(id);
            while (order.Count > Kept) targets.Remove(order.Dequeue());
            var hit = Raycast(center);
            var canvas = c.GetComponentInParent<Canvas>();
            var root = canvas == null ? null : canvas.rootCanvas;
            var parent = rt.parent;
            var element = new JObject
            {
                ["target"] = id,
                ["instance_id"] = c.GetInstanceID(),
                ["path"] = IndexedPath(rt),
                ["name_path"] = Path(rt),
                ["components"] = new JArray(c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name)),
                ["active"] = c.gameObject.activeInHierarchy,
                ["visible"] = Visible(c, center),
                ["corners"] = projected["corners"],
                ["center"] = projected["center"],
                ["size"] = projected["size"],
                ["uv_origin"] = "bottom-left of the element's rect",
                ["canvas"] = root == null ? null : new JObject
                {
                    ["name"] = root.name, ["render_mode"] = root.renderMode.ToString(), ["scale_factor"] = root.scaleFactor, ["sorting_order"] = root.sortingOrder,
                },
                ["raycast_top"] = hit == null ? null : IndexedPath(hit.transform),
                ["raycast_matches"] = hit != null && (hit == c.gameObject || hit.transform.IsChildOf(rt)),
                ["parent"] = parent == null ? null : new JObject
                {
                    ["instance_id"] = parent.gameObject.GetInstanceID(),
                    ["path"] = IndexedPath(parent),
                    ["components"] = new JArray(parent.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name)),
                },
            };
            if (Schedule.Handle != null && Schedule.Handle.IsInstanceOfType(c))
                element["schedule"] = Schedule.Describe(c, Project, Point);
            return element;
        }

        // All four corners through the element's own transform and its root canvas camera, so scalers, nested scales
        // and rotations are included once. Order: bottom-left, top-left, top-right, bottom-right.
        internal JObject Project(RectTransform rt)
        {
            var world = new Vector3[4];
            rt.GetWorldCorners(world);
            var cam = Camera(rt);
            var corners = new JArray(world.Select(w => Json(RectTransformUtility.WorldToScreenPoint(cam, w))));
            var center = Point(rt, rt.rect.center);
            return new JObject
            {
                ["corners"] = corners,
                ["center"] = Json(center),
                ["size"] = new JObject { ["width"] = Math.Round(rt.rect.width, 2), ["height"] = Math.Round(rt.rect.height, 2), ["units"] = "local" },
            };
        }

        internal Vector2 Point(RectTransform rt, Vector2 local) =>
            RectTransformUtility.WorldToScreenPoint(Camera(rt), rt.TransformPoint(new Vector3(local.x, local.y, 0)));

        private static UnityEngine.Camera Camera(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();
            var root = canvas == null ? null : canvas.rootCanvas;
            if (root == null || root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return root.worldCamera;
        }

        private static JObject Json(Vector2 v) => new JObject { ["x"] = Math.Round(v.x, 2), ["y"] = Math.Round(v.y, 2) };

        private static bool Visible(Component c, Vector2 center)
        {
            if (!c.gameObject.activeInHierarchy) return false;
            var canvas = c.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled) return false;
            foreach (var group in c.GetComponentsInParent<CanvasGroup>())
            {
                if (group.alpha < 0.05f) return false;
                if (group.ignoreParentGroups) break;
            }
            return center.x >= 0 && center.y >= 0 && center.x < Screen.width && center.y < Screen.height;
        }

        internal static GameObject Raycast(Vector2 at)
        {
            var es = EventSystem.current;
            if (es == null) return null;
            var results = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = at }, results);
            return results.Count == 0 ? null : results[0].gameObject;
        }

        private Record Live(string id, string session)
        {
            if (!targets.TryGetValue(id, out var r)) throw new MouseError("stale_ui_target", $"unknown target '{id}': query ui geometry again");
            if (r.Session != session || r.Generation != Generation || r.Width != Screen.width || r.Height != Screen.height)
                throw new MouseError("stale_ui_target", $"target '{id}' is from an earlier session, layout or screen size: query ui geometry again");
            if (r.Component == null || !r.Component.gameObject.activeInHierarchy)
                throw new MouseError("stale_ui_target", $"target '{id}' is gone or inactive: query ui geometry again");
            var now = Point((RectTransform)r.Component.transform, ((RectTransform)r.Component.transform).rect.center);
            if ((now - r.Center).magnitude > MovedPixels)
                throw new MouseError("stale_ui_target", $"target '{id}' moved since it was measured: query ui geometry again");
            return r;
        }

        // A screen point for a mouse argument, the topmost UI object there, and the target component if one was named.
        // `press`: the point starts a press, so a target must be the topmost object there (or contain it).
        internal (ScreenPoint Point, GameObject Hit, Component Target) Resolve(PointArg arg, string session, bool press)
        {
            if (arg.Target == null)
            {
                var raw = arg.Screen.Value;
                return (raw, Raycast(new Vector2((float)raw.X, (float)raw.Y)), null);
            }
            var r = Live(arg.Target, session);
            var rt = (RectTransform)r.Component.transform;
            var rect = rt.rect;
            var p = Point(rt, new Vector2(rect.xMin + rect.width * (float)arg.U, rect.yMin + rect.height * (float)arg.V));
            if (p.x < 0 || p.y < 0 || p.x >= Screen.width || p.y >= Screen.height)
                throw new MouseError("stale_ui_target", $"target '{arg.Target}' at {p.x:F1},{p.y:F1} is off the screen");
            var hit = Raycast(p);
            if (press && (hit == null || (hit != r.Component.gameObject && !hit.transform.IsChildOf(rt))))
                throw new MouseError("stale_ui_target", $"target '{arg.Target}' is covered at {p.x:F1},{p.y:F1} by {(hit == null ? "nothing that takes the pointer" : IndexedPath(hit.transform))}");
            return (new ScreenPoint(p.x, p.y), hit, r.Component);
        }
    }
}
