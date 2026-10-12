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
    // The test bridge (docs/test-bridge.md): HTTP on 127.0.0.1:8322 inside the game, so an agent on the Mac can drive and
    // read the game through an ssh tunnel. Only when the game was started with -foodtrucks-test; without the flag nothing
    // listens and the game behaves as before. It starts at the mod's initialisation load, so it answers at the main menu,
    // and stops when the mod unloads.
    //
    // Threads: the HTTP thread (TestBridgeHttp) never touches the game. /health and /events read values the main thread
    // writes (Snapshot, TestBridgeEvents); /call queues its command for the main thread (TestBridgePump) and waits.
    internal static class TestBridge
    {
        internal const string Flag = "-foodtrucks-test";
        internal const int Port = 8322;
        internal const int BridgeVersion = 1;

        private static TestBridgeHttp? http;
        private static TestBridgePump? pump;

        internal static bool Running => http != null;

        // A new random id at each start, in every reply: a client sees from a change that the game (or the mod) restarted.
        internal static volatile string Session = "";

        // The values /health answers with, refreshed by the main thread every frame. Replaced whole, never changed.
        internal sealed class Snapshot
        {
            public string Scene = "menu";
            public string? Save;
            public int? Day;
            public string? Time;
            public bool? Paused;
            public int Frame;
            public string? LoadName;
            public string? LoadStatus;
        }

        internal static volatile Snapshot Current = new Snapshot();

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
                Session = Guid.NewGuid().ToString("N").Substring(0, 8);
                TestBridgePump.Admit();
                TestBridgeEvents.Hook();
                pump = TestBridgePump.Install();
                var server = new TestBridgeHttp(Port);
                server.Start();
                http = server;
                Patches.ApplyTestBridge();
                // An agent drives the game while its window may not have the focus.
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

        // Stops taking calls, fails every queued and pending one, releases what they hold and clears the bridge's state,
        // so a start after a mod re-enable begins clean.
        internal static void Stop()
        {
            var wasRunning = http != null;
            TestBridgePump.FailAll(StoppedText);
            try { http?.Stop(); }
            catch (Exception ex) { Log.Error("Test bridge, stopping: " + ex.Message); }
            http = null;
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
            TestBridgeEvents.Unhook();
            TestBridgeEvents.Clear();
            if (wasRunning) Log.Info("Test bridge: stopped.");
        }

        // Every /call is an operation with a number, kept with its outcome (the last OpsKept), so a client whose call
        // timed out can ask what became of it (cmd "op") instead of running it again.
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

        // Runs a /call on the main thread and waits up to timeoutSeconds for its reply. Runs on an HTTP thread.
        internal static JObject Call(string cmd, JObject args, double timeoutSeconds)
        {
            if (cmd == "op") return OpStatus(args);
            if (TestBridgePump.Stopped) return Fail(StoppedText);
            Op op;
            lock (opsGate)
            {
                op = new Op { Id = nextOp++, Cmd = cmd };
                ops[op.Id] = op;
                opOrder.Enqueue(op.Id);
                while (opOrder.Count > OpsKept) ops.Remove(opOrder.Dequeue());
            }
            var task = TestBridgePump.Run(() => TestBridgeCommands.Run(cmd, args));
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
            }
        }

        // { "cmd": "op", "args": { "id": N } }: answered on the HTTP thread, whatever the main thread is doing.
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

        // Called every frame on the main thread.
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
            }
            Current = snapshot;
        }

        internal static JObject HealthJson()
        {
            var s = Current;
            return new JObject
            {
                ["ok"] = true,
                ["mod_version"] = FoodTrucksMod.Version,
                ["bridge_version"] = BridgeVersion,
                ["scene"] = s.Scene,
                ["save"] = s.Save,
                ["day"] = s.Day,
                ["time"] = s.Time,
                ["paused"] = s.Paused,
                ["frame"] = s.Frame,
                ["load"] = s.LoadName == null ? null : new JObject { ["name"] = s.LoadName, ["status"] = s.LoadStatus },
            };
        }
    }

    // A command's refusal: the reply is ok: false with this message.
    internal sealed class BridgeError : Exception
    {
        public BridgeError(string message) : base(message) { }
    }

    // A command whose result comes in a later frame (a save finishing, a teleport out of a building): the pump asks
    // Poll every frame until it returns the result or throws.
    internal sealed class Deferred
    {
        public Deferred(Func<JToken?> poll, Action? onStop = null)
        {
            Poll = poll;
            OnStop = onStop;
        }

        public Func<JToken?> Poll { get; }
        // Run when the bridge stops before the result came (the shot's camera and texture go).
        public Action? OnStop { get; }
    }

    // A hidden object that lives across scenes and runs the bridge's work on the main thread each frame: queued calls,
    // the /health snapshot, run_until and the bridge's own coroutines (load, quit).
    internal sealed class TestBridgePump : MonoBehaviour
    {
        // Each queued call carries the generation it was admitted under; a stop moves to the next one, and the pump fails
        // anything from an earlier generation. Admission and the stop share one lock, so nothing slips in between.
        private static readonly ConcurrentQueue<(Func<object?> Work, TaskCompletionSource<JToken?> Done, int Generation)> queue =
            new ConcurrentQueue<(Func<object?>, TaskCompletionSource<JToken?>, int)>();
        private static readonly object admission = new object();
        private static int generation;
        // Set when the bridge stops: no new work is taken.
        internal static volatile bool Stopped;

        // The bridge starts (again): calls are taken under the current generation.
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

        // Calls in progress whose result comes later (Deferred), with their waiters.
        private static readonly List<(Deferred Work, TaskCompletionSource<JToken?> Done)> pending = new List<(Deferred, TaskCompletionSource<JToken?>)>();

        internal static Task<JToken?> Run(Func<object?> work)
        {
            var done = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (admission)
            {
                if (Stopped) done.SetException(new BridgeError(TestBridge.StoppedText));
                else queue.Enqueue((work, done, generation));
            }
            return done.Task;
        }

        private static void RunQueued(Func<object?> work, TaskCompletionSource<JToken?> done)
        {
            try
            {
                var result = work();
                if (result is Deferred later) pending.Add((later, done));
                else done.SetResult(result as JToken);
            }
            catch (Exception ex) { done.SetException(ex); }
        }

        // The bridge stops: every queued and pending call fails, and what pending ones hold is released.
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
            // Calls queued by the HTTP thread; each one answers its own waiter, errors included.
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
