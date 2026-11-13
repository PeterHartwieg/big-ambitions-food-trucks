#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UI.Load;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    internal static class TestBridge
    {
        internal const string Flag = "-foodtrucks-test";
        internal const int Port = 8330;
        internal const int BridgeVersion = 1;

        private static TestBridgeHttp? http;
        private static TestBridgePump? pump;

        internal static bool Running => http != null;

        internal static volatile string Session = "";

        internal sealed class Snapshot
        {
            public string Scene = "menu";
            public string? Save;
            public int? Day;
            public string? Time;
            public bool? Paused;
            public bool? TimeSkip;
            public int Frame;
            public string? LoadName;
            public string? LoadStatus;
        }

        internal static volatile Snapshot Current = new Snapshot();

        internal static int MainThread = -1;

        internal static void Start()
        {
            if (!Environment.GetCommandLineArgs().Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Info($"Test bridge: off (the game was started without {Flag}).");
                return;
            }
            if (http != null) return;
            try
            {
                MainThread = Thread.CurrentThread.ManagedThreadId;
                BridgeControl.ControlGate.ConfigureProfile(Application.persistentDataPath);
                Session = Guid.NewGuid().ToString("N").Substring(0, 8);
                TestBridgePump.Admit();
                TestBridgeEvents.Hook();
                pump = TestBridgePump.Install();
                var server = new TestBridgeHttp(Port, TestBridgePump.Generation);
                server.Start();
                http = server;
                Patches.ApplyTestBridge();
                Application.runInBackground = true;
                Log.Info($"Test bridge: listening on http://127.0.0.1:{Port}/ (session {Session}).");
            }
            catch (Exception ex)
            {
                Log.Error($"Test bridge: could not listen on 127.0.0.1:{Port} ({ex.GetType().Name}: {ex.Message}); is another game or program using the port? The bridge is off.");
                Stop();
            }
        }

        internal const string StoppedText = "the bridge stopped";

        internal static void Stop()
        {
            CrewDriveProbe.StopAll("Test bridge stopped");
            var wasRunning = http != null;
            TestBridgePump.FailAll(StoppedText);
            try { http?.Stop(); }
            catch (Exception ex) { Log.Error("Test bridge, stopping: " + ex.Message); }
            http = null;
            try { TestBridgeCommands.RestoreAllInterpolation(); }
            catch (Exception ex) { Log.Error("Test bridge, restoring interpolation: " + ex.Message); }
            if (pump != null) Object.Destroy(pump.gameObject);
            pump = null;
            try { TestBridgeCommands.Reset(); }
            catch (Exception ex) { Log.Error("Test bridge, clearing: " + ex.Message); }
            lock (opsGate)
            {
                ops.Clear();
                opOrder.Clear();
            }
            Current = new Snapshot();
            lastMinutes = null;
            jump = null;
            TestBridgeEvents.Unhook();
            TestBridgeEvents.Clear();
            if (wasRunning) Log.Info("Test bridge: stopped.");
        }

        private sealed class Op
        {
            public long Id;
            public string Cmd = "";
            public string Status = "pending";
            public JToken? Result;
            public string? Error;
        }

        private const int OpsKept = 500;
        private static readonly object opsGate = new object();
        private static readonly Dictionary<long, Op> ops = new Dictionary<long, Op>();
        private static readonly Queue<long> opOrder = new Queue<long>();
        private static long nextOp = 1;

        internal static JObject Call(string cmd, JObject args, double timeoutSeconds, int generation)
        {
            try
            {
                if (cmd == "op") return OpStatus(args);
                if (TestBridgePump.Stopped) return Fail(StoppedText);
                var intercepted = BridgeControl.ControlGate.Intercept(cmd, args);
                if (intercepted != null) return intercepted;
                Op op;
                lock (opsGate)
                {
                    op = new Op { Id = nextOp++, Cmd = cmd };
                    ops[op.Id] = op;
                }
                var task = BridgeControl.ControlGate.Track(TestBridgePump.Run(() => TestBridgeCommands.Run(cmd, args), generation), args);
                task.ContinueWith(t => Finish(op, t));
                bool finished;
                try { finished = task.Wait(TimeSpan.FromSeconds(timeoutSeconds)); }
                catch (AggregateException) { finished = true; }
                if (!finished)
                    return new JObject
                    {
                        ["ok"] = false, ["op"] = op.Id,
                        ["error"] = $"timed out on the main thread; the command goes on: ask {{\"cmd\": \"op\", \"args\": {{\"id\": {op.Id}}}}} for its outcome",
                    };
                Finish(op, task);
                lock (opsGate)
                {
                    return op.Status == "done"
                        ? new JObject { ["ok"] = true, ["op"] = op.Id, ["result"] = op.Result?.DeepClone() }
                        : new JObject { ["ok"] = false, ["op"] = op.Id, ["error"] = op.Error };
                }
            } finally { BridgeControl.ControlGate.EndUnqueued(args); }
        }

        private static void Finish(Op op, Task<JToken?> task)
        {
            lock (opsGate)
            {
                if (op.Status != "pending" || !task.IsCompleted) return;
                if (task.IsFaulted)
                {
                    var inner = task.Exception?.InnerException ?? (Exception?)task.Exception;
                    op.Status = "failed";
                    op.Error = inner is BridgeError ? inner.Message : $"{inner?.GetType().Name}: {inner?.Message}";
                }
                else if (task.IsCanceled)
                {
                    op.Status = "failed";
                    op.Error = "cancelled";
                }
                else
                {
                    op.Status = "done";
                    op.Result = task.Result;
                }
                if (ops.TryGetValue(op.Id, out var retained) && ReferenceEquals(retained, op))
                {
                    opOrder.Enqueue(op.Id);
                    while (opOrder.Count > OpsKept) ops.Remove(opOrder.Dequeue());
                }
            }
        }

        private static JObject OpStatus(JObject args)
        {
            var id = TestBridgeCommands.Num(args, "id");
            if (id == null) return Fail("op needs \"id\": the number a /call reply gave as \"op\".");
            lock (opsGate)
            {
                if (!ops.TryGetValue((long)id.Value, out var op))
                    return Fail($"No operation {id} (the bridge keeps the last {OpsKept}).");
                var result = new JObject { ["id"] = op.Id, ["cmd"] = op.Cmd, ["session"] = Session, ["status"] = op.Status };
                if (op.Status == "done") result["result"] = op.Result?.DeepClone();
                if (op.Status == "failed") result["error"] = op.Error;
                return new JObject { ["ok"] = true, ["result"] = result };
            }
        }

        internal static JObject Fail(string error) => new JObject { ["ok"] = false, ["error"] = error };

        internal static void Refresh()
        {
            var game = SaveGameManager.Current;
            var scene = TestBridgeCommands.Scene();
            var snapshot = new Snapshot
            {
                Scene = scene,
                Frame = UnityEngine.Time.frameCount,
                Save = game?.SaveGameName,
                LoadName = TestBridgeCommands.LoadTarget,
                LoadStatus = TestBridgeCommands.LoadStatus,
            };
            if (game != null && scene == "city")
            {
                snapshot.Day = game.Day;
                snapshot.Time = $"{game.Hour:D2}:{(int)game.Minute:D2}";
                snapshot.Paused = TestBridgeCommands.Paused();
                snapshot.TimeSkip = TestBridgeCommands.TimeSkipRunning();
            }
            Current = snapshot;
            WatchClock(game, scene);
        }

        internal static (int? Day, string? Time) ClockNow()
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThread)
            {
                try
                {
                    var game = SaveGameManager.Current;
                    if (game != null && TestBridgeCommands.Scene() == "city") return (game.Day, $"{game.Hour:D2}:{(int)game.Minute:D2}");
                }
                catch (Exception) { }
            }
            var s = Current;
            return (s.Day, s.Time);
        }

        private const double JumpMinutes = 30;
        private static double? lastMinutes;
        private static (double From, double To, string Why, float Settled)? jump;

        private static void WatchClock(GameInstance? game, string scene)
        {
            if (game == null || scene != "city")
            {
                lastMinutes = null;
                FlushJump(force: true);
                return;
            }
            var now = game.Day * 1440.0 + game.Hour * 60.0 + game.Minute;
            var real = UnityEngine.Time.realtimeSinceStartup;
            if (lastMinutes != null && (now - lastMinutes.Value > JumpMinutes || now < lastMinutes.Value - 0.01))
            {
                var why = TestBridgeCommands.TimeSkipRunning() == true ? "a time skip (sleep or a paid activity)"
                    : Helpers.EnergyHelper.goingToHospital ? "the player collapsed (hospital)"
                    : "something set the time (the game, a load or another mod)";
                jump = jump == null ? (lastMinutes.Value, now, why, real + 1f) : (jump.Value.From, now, jump.Value.Why, real + 1f);
            }
            else if (jump != null) jump = (jump.Value.From, now, jump.Value.Why, jump.Value.Settled);
            lastMinutes = now;
            FlushJump(force: false);
        }

        private static void FlushJump(bool force)
        {
            if (jump == null || (!force && UnityEngine.Time.realtimeSinceStartup < jump.Value.Settled)) return;
            var (from, to, why, _) = jump.Value;
            jump = null;
            Log.Info($"Test bridge: the clock jumped from {Stamp(from)} to {Stamp(to)} in one go: {why}.");
        }

        private static string Stamp(double minutes)
        {
            var day = (int)Math.Floor(minutes / 1440.0);
            var rest = minutes - day * 1440.0;
            return $"day {day} {(int)(rest / 60):D2}:{(int)(rest % 60):D2}";
        }

        internal static JObject HealthJson()
        {
            var s = Current;
            return BridgeControl.ControlGate.Health(new JObject
            {
                ["ok"] = true,
                ["mod_version"] = FoodTrucksMod.Version,
                ["bridge_version"] = BridgeVersion,
                ["scene"] = s.Scene,
                ["save"] = s.Save,
                ["day"] = s.Day,
                ["time"] = s.Time,
                ["paused"] = s.Paused,
                ["time_skip"] = s.TimeSkip,
                ["frame"] = s.Frame,
                ["load"] = s.LoadName == null ? null : new JObject { ["name"] = s.LoadName, ["status"] = s.LoadStatus },
            });
        }
    }

    internal sealed class BridgeError : Exception
    {
        public BridgeError(string message) : base(message) { }
    }

    internal sealed class Deferred
    {
        public Deferred(Func<JToken?> poll, Action? onStop = null)
        {
            Poll = poll;
            OnStop = onStop;
        }

        public Func<JToken?> Poll { get; }
        public Action? OnStop { get; }
    }

    internal sealed class TestBridgePump : MonoBehaviour
    {
        private static readonly ConcurrentQueue<(Func<object?> Work, TaskCompletionSource<JToken?> Done, int Generation)> queue =
            new ConcurrentQueue<(Func<object?>, TaskCompletionSource<JToken?>, int)>();
        private static readonly object admission = new object();
        private static int generation;
        internal static volatile bool Stopped;

        internal static int Generation => Volatile.Read(ref generation);

        internal static void Admit()
        {
            lock (admission) Stopped = false;
        }
        internal static TestBridgePump? Instance { get; private set; }

        internal static TestBridgePump Install()
        {
            var go = new GameObject("FoodTrucks.TestBridge");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            return go.AddComponent<TestBridgePump>();
        }

        private static readonly List<(Deferred Work, TaskCompletionSource<JToken?> Done)> pending = new List<(Deferred, TaskCompletionSource<JToken?>)>();

        internal static Task<JToken?> Run(Func<object?> work, int admitted)
        {
            var done = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (admission)
            {
                if (Stopped || admitted != generation) done.SetException(new BridgeError(TestBridge.StoppedText));
                else queue.Enqueue((work, done, generation));
            }
            return done.Task;
        }

        private static void RunQueued(Func<object?> work, TaskCompletionSource<JToken?> done)
        {
            try
            {
                var result = work();
                TestBridgeCommands.RefreshOwnership();
                if (result is Deferred later) pending.Add((later, done));
                else done.SetResult(result as JToken);
            }
            catch (Exception ex) { TestBridgeCommands.RefreshOwnership(); done.SetException(ex); }
        }

        internal static void FailAll(string why)
        {
            lock (admission)
            {
                Stopped = true;
                generation++;
                while (queue.TryDequeue(out var item)) item.Done.TrySetException(new BridgeError(why));
            }
            foreach (var (work, done) in pending)
            {
                try { work.OnStop?.Invoke(); }
                catch (Exception ex) { Log.Error("Test bridge, releasing a pending call: " + ex.Message); }
                done.TrySetException(new BridgeError(why));
            }
            pending.Clear();
        }

        private static void PollPending()
        {
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var (work, done) = pending[i];
                try
                {
                    var result = work.Poll();
                    if (result == null) continue;
                    pending.RemoveAt(i);
                    done.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    pending.RemoveAt(i);
                    try { work.OnStop?.Invoke(); }
                    catch (Exception cleanup) { Log.Error("Test bridge, releasing a failed call: " + cleanup.Message); }
                    done.TrySetException(ex);
                }
            }
        }

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            try { TestBridge.Refresh(); }
            catch (Exception ex) { Once("refresh", ex); }
            try { TestBridgeCommands.EveryFrame(); }
            catch (Exception ex) { Once("every frame", ex); }
            PollPending();
            var n = 0;
            while (n++ < 50 && queue.TryDequeue(out var item))
            {
                if (item.Generation != Volatile.Read(ref generation)) item.Done.TrySetException(new BridgeError(TestBridge.StoppedText));
                else RunQueued(item.Work, item.Done);
            }
        }

        private static readonly HashSet<string> failed = new HashSet<string>();

        private static void Once(string what, Exception ex)
        {
            if (failed.Add(what)) Log.Error($"Test bridge, {what}: {ex}");
        }
    }
}
