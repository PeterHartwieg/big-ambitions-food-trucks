using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AgentRigInput
{
    // One virtual mouse per rig process. Installed by the agent test runtime once the bridge pump runs; refuses to
    // install anywhere else. Bridge commands `mouse` and `ui geometry` are answered here, in the bridge's own command
    // dispatch, with its ownership and FT-save checks.
    internal sealed class Service : IMouseHost
    {
        internal const string HarmonyId = "agent-tests.rig-input";
        internal static Service Instance;
        private static volatile JObject health = new JObject { ["protocol"] = MouseArgs.Protocol, ["available"] = false, ["status"] = "off" };

        private readonly Harmony harmony = new Harmony(HarmonyId);
        private readonly MouseEngine engine;
        private readonly LegacyInput legacy = new LegacyInput();
        private readonly RigPointer pointer = new RigPointer();
        private readonly InputSystemMouse inputSystem = new InputSystemMouse();
        private readonly UiGeometry geometry = new UiGeometry();
        private string status = "starting", reason;
        private bool scheduleBound, stopped;
        private long earlyFrame = -1;
        private int lateTicks;
        private double nextOwnerCheck;
        private string token, save;
        private readonly HashSet<string> failures = new HashSet<string>();

        private Service() { engine = new MouseEngine(this); }

        public long Frame => Time.frameCount;
        public double Now => Time.realtimeSinceStartupAsDouble;
        public int ScreenWidth => Screen.width;
        public int ScreenHeight => Screen.height;

        internal static string Start(string product, int port)
        {
            if (Instance != null) return Instance.status;
            var refused = RigGate.Check(product, port) ?? Bridge.Bind();
            if (refused == null && Bridge.Port != port) refused = $"the bridge listens on {Bridge.Port}, not the agent port {port}";
            if (refused != null)
            {
                health = new JObject { ["protocol"] = MouseArgs.Protocol, ["available"] = false, ["status"] = "unavailable", ["reason"] = refused };
                Log.Info("Rig virtual mouse: off (" + refused + ").");
                return "unavailable: " + refused;
            }
            var s = new Service();
            Instance = s;
            try { s.Install(); }
            catch (Exception ex)
            {
                s.Shutdown();
                s.status = "unavailable";
                s.reason = "startup failed: " + ex.GetType().Name + ": " + ex.Message;
                s.RefreshHealth();
                Log.Error("Rig virtual mouse: " + s.reason);
                return "unavailable: " + s.reason;
            }
            return s.status;
        }

        private void Install()
        {
            LegacyInputShim.State = engine.Timeline.State;
            Patch(Bridge.Run, nameof(RunPrefix), null, "TestBridgeCommands.Run (mouse and ui geometry)");
            Patch(Bridge.Stop, nameof(StopPrefix), null, "TestBridge.Stop (virtual mouse cleanup)");
            Patch(Bridge.PumpDisable, null, nameof(PumpDisabled), "TestBridgePump.OnDisable (virtual mouse cleanup)");
            legacy.StartScan();
            inputSystem.Probe(harmony);
            FrameDriver.Install(Early, Late);
            Application.quitting += Shutdown;
            SceneManager.activeSceneChanged += (from, to) => engine.Cancel("the scene changed", "mouse_cancelled", true);
            status = "ready";
            RefreshHealth();
            Log.Info($"Rig virtual mouse: ready on port {Bridge.Port}; legacy {legacy.Backend}, UI {RigPointer.Backend}, Input System {inputSystem.Backend}.");
        }

        private void Patch(MethodInfo target, string prefix, string postfix, string what)
        {
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Service).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Service).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)));
                Log.Info($"Patch {what}: Active.");
            }
            catch (Exception ex)
            {
                Log.Info($"Patch {what}: Not active ({ex.Message}).");
                throw;
            }
        }

        private static bool RunPrefix(string cmd, JObject args, ref object __result)
        {
            var s = Instance;
            if (s == null || s.stopped) return true;
            if (cmd == "mouse" || (cmd == "ui" && args?["op"]?.Type == JTokenType.String && (string)args["op"] == "geometry"))
            {
                __result = s.Run(cmd, args ?? new JObject());
                return false;
            }
            return true;
        }

        // Before the bridge fails its pending calls: no virtual button survives a stop or a session change.
        private static void StopPrefix()
        {
            var s = Instance;
            if (s == null) return;
            s.engine.ResetSession("the bridge stopped");
            s.geometry.Forget();
        }

        private static void PumpDisabled() => Instance?.engine.Cancel("the bridge pump stopped", "mouse_cancelled", false);

        private object Run(string cmd, JObject args)
        {
            try
            {
                if (cmd == "ui")
                {
                    Bridge.BeforeRun(cmd, args);
                    return geometry.Query(args, Bridge.Session);
                }
                var op = MouseArgs.Op(args);
                Bridge.BeforeRun(cmd, args);
                if (op == "status") return Status(args);
                if (status != "ready") throw new MouseError("rig_mouse_unavailable", reason ?? status);
                var credential = args[Bridge.TokenKey]?.Type == JTokenType.String ? (string)args[Bridge.TokenKey] : null;
                var owner = Fingerprint(credential);
                var session = Bridge.Session;
                if (op == "release")
                {
                    var released = engine.Release(owner, session);
                    if (released == null) return engine.ReleasedNothing();
                    return released.Done ? engine.Poll(released) : Defer(released);
                }
                Bridge.RequireFtCity("mouse " + op);
                return Defer(Begin(op, args, credential, owner, session));
            }
            catch (MouseError e) { throw Bridge.Error(e.Message); }
        }

        private object Defer(MouseCommand c) => Bridge.Deferred(() =>
        {
            try { return engine.Poll(c); }
            catch (MouseError e) { throw Bridge.Error(e.Message); }
        }, () => { if (!c.Done) engine.Cancel("the bridge stopped", "mouse_cancelled", false); });

        private MouseCommand Begin(string op, JObject args, string credential, string owner, string session)
        {
            var w = Screen.width; var h = Screen.height;
            var st = engine.Timeline.State;
            Func<Action> reserve = () =>
            {
                var end = Bridge.Reserve(credential);
                token = credential;
                save = Bridge.Save;
                nextOwnerCheck = Now + 1;
                return end;
            };
            switch (op)
            {
                case "move":
                {
                    var to = Resolve(MouseArgs.Point(args, "to", true, w, h), session, false, false);
                    var frames = MouseArgs.Int(args, "frames", 1, 1, MouseArgs.MaxFrames);
                    return engine.Begin("move", "hover", session, owner,
                        (fresh, at) => fresh ? Gestures.Prepare(to.Point, 0, true) : Gestures.Move(at, to.Point, st.Held, frames),
                        false, false, null, reserve, 5 + frames / 10.0);
                }
                case "down":
                {
                    var b = MouseArgs.Button(args);
                    if (engine.Active && st.IsHeld(b)) throw new MouseError("mouse_invalid", $"button {b} is already down");
                    var arg = MouseArgs.Point(args, "at", false, w, h);
                    if (arg == null && !engine.Active) throw new MouseError("mouse_invalid", "down needs \"at\" when no interaction is active");
                    var at = Resolve(arg ?? Here(), session, true, true);
                    return engine.Begin("down", "press", session, owner, (fresh, here) =>
                    {
                        var held = fresh ? 0 : st.Held;
                        var list = fresh || arg != null ? Gestures.Prepare(at.Point, held, fresh) : new List<MouseSample>();
                        list.AddRange(Gestures.Down(at.Point, held, b));
                        return list;
                    }, false, false, null, reserve, 10);
                }
                case "up":
                {
                    var b = MouseArgs.Button(args);
                    var settle = MouseArgs.Int(args, "settle_frames", 2, 0, MouseArgs.MaxSettle);
                    if (!engine.Active || !st.IsHeld(b)) throw new MouseError("mouse_invalid", $"button {b} is not down");
                    var observed = b == 0 && Schedule.Observes(pointer.TrackedPress) ? true : (bool?)null;
                    return engine.Begin("up", "press", session, owner, (fresh, at) => Gestures.Up(at, st.Held, b, settle),
                        false, false, observed, reserve, 10 + settle / 10.0);
                }
                case "scroll":
                {
                    var x = MouseArgs.Number(args, "x", 0, -MouseArgs.MaxScroll, MouseArgs.MaxScroll);
                    var y = MouseArgs.Number(args, "y", 0, -MouseArgs.MaxScroll, MouseArgs.MaxScroll);
                    if (x == 0 && y == 0) throw new MouseError("mouse_invalid", "scroll needs a nonzero x or y");
                    var arg = MouseArgs.Point(args, "at", false, w, h);
                    if (arg == null && !engine.Active) throw new MouseError("mouse_invalid", "scroll needs \"at\" when no interaction is active");
                    var at = Resolve(arg ?? Here(), session, false, true);
                    return engine.Begin("scroll", "hover", session, owner, (fresh, here) =>
                    {
                        var held = fresh ? 0 : st.Held;
                        var list = fresh ? Gestures.Prepare(at.Point, 0, true) : arg != null ? Gestures.Move(here, at.Point, held, 1) : new List<MouseSample>();
                        list.AddRange(Gestures.Scroll(at.Point, held, x, y));
                        return list;
                    }, !engine.Active, false, null, reserve, 10);
                }
                case "drag":
                {
                    var b = MouseArgs.Button(args);
                    var steps = MouseArgs.Int(args, "steps", 12, 1, MouseArgs.MaxSteps);
                    var duration = MouseArgs.Number(args, "duration_s", 0.4, 0, MouseArgs.MaxDuration);
                    var settle = MouseArgs.Int(args, "settle_frames", 2, 0, MouseArgs.MaxSettle);
                    var after = MouseArgs.AfterReleaseEscape(args);
                    var from = Resolve(MouseArgs.Point(args, "from", true, w, h), session, true, true);
                    var to = Resolve(MouseArgs.Point(args, "to", true, w, h), session, false, true);
                    var observed = b == 0 && Schedule.Observes(from.Hit) ? true : (bool?)null;
                    if (after && observed != true)
                        throw new MouseError("mouse_invalid", "after_release needs a left-button drag that starts on a Schedule resize handle (the native release it waits for)");
                    var c = engine.Begin("drag", "drag", session, owner, (fresh, at) => Gestures.Drag(from.Point, to.Point, b, steps, duration, settle, fresh),
                        true, after, observed, reserve, duration + (steps + settle + 8) / 10.0 + 15);
                    c.Start = from.Point; c.End = to.Point;
                    c.Extra = new JObject { ["press_target"] = from.Hit == null ? null : UiGeometry.IndexedPath(from.Hit.transform) };
                    return c;
                }
                default: throw new MouseError("mouse_invalid", "unknown mouse op " + op);
            }
        }

        private PointArg Here() => new PointArg { Screen = new ScreenPoint(engine.Timeline.State.X, engine.Timeline.State.Y) };

        private (ScreenPoint Point, GameObject Hit, Component Target) Resolve(PointArg arg, string session, bool press, bool protect)
        {
            var r = geometry.Resolve(arg, session, press);
            if (protect) Protect(r.Hit);
            return r;
        }

        private static Type miniMenu;
        // The same native controls `ui click` refuses: save, load, delete and character screens, and the pause menu.
        private static void Protect(GameObject hit)
        {
            if (hit == null) return;
            miniMenu = miniMenu ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(Util.Types).FirstOrDefault(t => t.FullName == "UI.MiniMenu.MiniMenu");
            var menuOpen = miniMenu?.GetProperty("IsOpen", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as bool?
                           ?? miniMenu?.GetField("IsOpen", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as bool? ?? false;
            for (var t = hit.transform; t != null; t = t.parent)
                foreach (var c in t.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var name = c.GetType().Name;
                    if (Bridge.SaveLoadOwners.Contains(name) || name == "MiniMenu" || (name == "HudConfirmUi" && menuOpen))
                        throw new MouseError("mouse_protected", $"the pointer would press the game's own {name} control; use the save and load commands");
                }
        }

        private static string Fingerprint(string credential)
        {
            if (string.IsNullOrEmpty(credential)) return "";
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(credential)).Take(8).Select(x => x.ToString("x2")));
        }

        // ---- Frames ----

        private void Early()
        {
            try
            {
                earlyFrame = Time.frameCount;
                if (!legacy.Applied)
                {
                    legacy.ApplyIfReady(harmony);
                    if (legacy.Applied && !scheduleBound) { scheduleBound = true; Schedule.Bind(harmony, legacy.ResizeCoroutine); }
                }
                var current = engine.Current;
                if (current != null)
                {
                    if (!Bridge.Running || Bridge.Session != current.Session) engine.Cancel("the bridge session changed", "mouse_cancelled", false);
                    else if (Bridge.Save != save) engine.Cancel("the loaded save changed", "mouse_cancelled", true);
                    else if (pointer.PressTargetLost(engine.Timeline.State.Held != 0)) engine.Cancel("the pressed UI object was destroyed", "mouse_cancelled", true);
                    else if (Now >= nextOwnerCheck)
                    {
                        nextOwnerCheck = Now + 1;
                        try { Bridge.AssertOwner(token); }
                        catch (Exception ex) { engine.Cancel("the lease owner changed: " + ex.Message, "mouse_cancelled", true); }
                    }
                }
                engine.EarlyFrame();
                LegacyInputShim.Active = engine.Active;
                if (engine.Active) inputSystem.Feed(engine.Timeline.State);
            }
            catch (Exception ex) { Fail("early frame", ex); }
        }

        private void Late()
        {
            try
            {
                engine.LateFrame();
                geometry.Observe(SceneManager.GetActiveScene().name);
                if (++lateTicks % 15 == 0 || engine.Active) RefreshHealth();
            }
            catch (Exception ex) { Fail("late frame", ex); }
        }

        // From the bridge pump: the player-loop hooks must keep running while an interaction is active.
        internal void PumpTick()
        {
            if (stopped) return;
            if (engine.Active && earlyFrame < Time.frameCount - 2)
            {
                engine.Cancel("the frame driver stopped running", "mouse_cancelled", false);
                LegacyInputShim.Active = false;
            }
            if (Time.frameCount % 120 == 0 && !FrameDriver.Installed())
            {
                Log.Error("Player loop hooks were removed by another system; reinstalling.");
                FrameDriver.Install(Early, Late);
            }
        }

        private void Fail(string where, Exception ex)
        {
            if (failures.Add(where)) Log.Error($"{where}: {ex}");
            try { engine.Cancel("internal error in the " + where + ": " + ex.Message, "mouse_cancelled", false); }
            catch (Exception) { }
            LegacyInputShim.Active = engine.Active;
        }

        private void Shutdown()
        {
            if (stopped) return;
            stopped = true;
            try { engine.Cancel("the game is quitting", "mouse_cancelled", false); }
            catch (Exception ex) { Log.Error("quit cleanup: " + ex.Message); }
            LegacyInputShim.Active = false;
            try { FrameDriver.Uninstall(); } catch (Exception) { }
            try { harmony.UnpatchAll(HarmonyId); } catch (Exception) { }
            status = "stopped";
            RefreshHealth();
        }

        // ---- IMouseHost ----

        public void Activate()
        {
            pointer.Activate(engine.Timeline.State);
            LegacyInputShim.TakeReads();
            Schedule.TakeFrame();
            // Legacy reads turn virtual with the first sample, at the next EarlyUpdate.
            inputSystem.Activate();
        }

        public void Restore()
        {
            LegacyInputShim.Active = false;
            try { pointer.Deactivate(); }
            finally
            {
                inputSystem.Deactivate();
                token = null;
            }
        }

        public void ForceRelease() => pointer.ForceRelease();

        public JObject ObserveFrame() => new JObject
        {
            ["pointer"] = pointer.Observe(),
            ["reads"] = LegacyInputShim.TakeReads(),
            ["schedule"] = Schedule.TakeFrame(),
            ["input_system_events"] = inputSystem.Events,
        };

        public long NativeReleaseFrame => Schedule.LastEndFrame;
        public JToken RunAfterRelease() => Bridge.Escape();

        // ---- Status and health ----

        private JObject Status(JObject args)
        {
            var result = StatusJson();
            var id = args["trace_id"];
            if (id != null)
            {
                if (id.Type != JTokenType.String) throw new MouseError("mouse_invalid", "trace_id must be a string");
                foreach (var p in engine.Traces.Find((string)id, Bridge.Session).Json().Properties()) result[p.Name] = p.Value;
            }
            return result;
        }

        private JObject StatusJson()
        {
            var st = engine.Timeline.State;
            var current = engine.Current;
            return new JObject
            {
                ["protocol"] = MouseArgs.Protocol,
                ["available"] = status == "ready",
                ["status"] = status,
                ["reason"] = reason,
                ["rig_only"] = true,
                ["active"] = current != null,
                ["interaction"] = current == null ? null : new JObject
                {
                    ["id"] = current.Id, ["kind"] = current.Kind, ["releasing"] = current.Releasing,
                    ["running"] = current.Pending?.Op, ["started_frame"] = current.StartFrame,
                },
                ["position"] = MouseEngine.Point(new ScreenPoint(st.X, st.Y)),
                ["buttons"] = new JArray(st.Buttons()),
                ["frame"] = Time.frameCount,
                ["screen"] = engine.Screen(),
                ["backends"] = Backends(),
                ["input_module"] = RigPointer.CurrentModule(),
                ["replaced_modules"] = current == null ? null : pointer.OriginalModules,
                ["legacy_coverage"] = legacy.Coverage(),
                ["input_system"] = inputSystem.Notes(),
                ["schedule_observer"] = new JObject
                {
                    ["available"] = Schedule.Handle != null && Schedule.Problem == null, ["problem"] = Schedule.Problem,
                    ["last_slide_end_frame"] = Schedule.LastEndFrame, ["resize_steps"] = Schedule.CoroutineSteps,
                },
                ["last_error"] = engine.LastError,
            };
        }

        private JObject Backends() => new JObject
        {
            ["legacy"] = legacy.Backend, ["event_system"] = RigPointer.Backend, ["input_system"] = inputSystem.Backend,
        };

        private void RefreshHealth()
        {
            health = new JObject
            {
                ["protocol"] = MouseArgs.Protocol, ["available"] = status == "ready", ["status"] = status, ["reason"] = reason,
                ["active"] = engine.Active, ["backends"] = Backends(),
            };
        }

        // Read on the bridge's HTTP thread: only the snapshot the main thread made.
        internal static JObject HealthSnapshot() => (JObject)health.DeepClone();
    }
}
