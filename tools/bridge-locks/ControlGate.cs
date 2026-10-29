using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BridgeControl
{
    public static class ControlGate
    {
        private const string TokenKey = "__bridge_lock_token", PermitKey = "__bridge_lock_permit";
        private static LeaseStore store;
        private static string configuredInstance;
        [ThreadStatic] private static string lastToken;
        private static string backgroundPermit;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JObject, object> tracked =
            new System.Runtime.CompilerServices.ConditionalWeakTable<JObject, object>();
        public static void ConfigureStore(LeaseStore value) { store = value; lastToken = null; backgroundPermit = null; }
        public static void ConfigureProfile(string profile)
        {
            var product = new DirectoryInfo(profile).Name;
            var instance = product == "com.hovgaardgames.bigambitions.agenttests.mac" ? "agentmac" : product == "com.Hovgaard-Games.Big-Ambitions" ? "normal" : product == "Big Ambitions Agent Tests" ? "agent1" : product == "Big Ambitions" ? "normal" : null;
            if (configuredInstance == instance && store != null) return;
            if (configuredInstance != null) throw new InvalidOperationException("Cannot switch bridge lock instance in a running game");
            ConfigureStore(LeaseStore.ForInstance(instance)); configuredInstance = instance;
        }
        private static LeaseStore Store => store ?? throw new InvalidOperationException("Bridge lock service was not initialized");

        public static bool IsReadOnly(string cmd, JObject args)
        {
            if (new[] { "state", "op", "parks", "places", "map", "day_summary" }.Contains(cmd)) return true;
            var op = args["op"]?.Type == JTokenType.String ? (string)args["op"] : null;
            // The rig virtual mouse's status/trace and UI geometry read only (tools/rig-input).
            if (cmd == "mouse") return op == "status";
            return cmd == "ui" && (op == "list" || op == "tree" || op == "geometry");
        }

        // Only the outer request can supply authorization; reserved fields inside args cannot impersonate it.
        public static JObject PrepareBody(JObject body)
        {
            var args = body["args"] as JObject;
            if (args == null) { args = new JObject(); body["args"] = args; }
            args.Remove(TokenKey); args.Remove(PermitKey);
            var cmd = (string)body["cmd"];
            if (cmd != "lock" && !IsReadOnly(cmd, args) && string.IsNullOrEmpty((string)body["expect_session"]))
                throw new InvalidOperationException("Changing commands require expect_session from /health");
            args[TokenKey] = (string)body["lock_token"];
            return body;
        }

        // Non-null replies bypass the game queue (lock management only). Mutations reserve ownership before queueing.
        public static JObject Intercept(string cmd, JObject args)
        {
            var token = (string)args[TokenKey];
            if (cmd == "lock")
            {
                JObject result;
                switch ((string)args["op"] ?? "status")
                {
                    case "status": result = Store.Status(); break;
                    case "acquire": result = Store.Acquire((string)args["owner"], (string)args["purpose"], token); break;
                    case "release": result = Store.Release(token); break;
                    default: throw new InvalidOperationException("lock op must be status, acquire or release");
                }
                return new JObject { ["ok"] = true, ["result"] = result };
            }
            if (!IsReadOnly(cmd, args)) args[PermitKey] = Store.Begin(token, "command");
            return null;
        }

        public static void BeforeRun(string cmd, JObject args)
        {
            if (!IsReadOnly(cmd, args))
            {
                Store.AssertOwner((string)args[TokenKey]);
                lastToken = (string)args[TokenKey];
            }
        }
        // Called on the game thread before a command completes, then every frame. Acknowledged
        // coroutines (load, drive, run_until, quit) and an unpaused city keep ownership reserved.
        public static void ObserveWork(bool busy)
        {
            if (busy && backgroundPermit == null && lastToken != null)
                backgroundPermit = Store.Begin(lastToken, "background");
            else if (!busy)
            {
                if (backgroundPermit != null) Store.End(backgroundPermit);
                backgroundPermit = null; lastToken = null;
            }
        }
        public static Task<JToken> Track(Task<JToken> task, JObject args)
        {
            var id = (string)args[PermitKey];
            if (id != null)
            {
                // Arguments are already queued on another thread: keep the JObject immutable here.
                tracked.Add(args, new object());
                task.ContinueWith(_ => Store.End(id), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return task;
        }
        public static void EndUnqueued(JObject args)
        {
            var id = (string)args[PermitKey];
            if (id != null && !tracked.TryGetValue(args, out _)) Store.End(id);
        }
        public static JObject Health(JObject health)
        {
            try { health["control_lock"] = Store.Status(); }
            catch (Exception ex) { health["control_lock"] = new JObject { ["error"] = ex.Message }; }
            health["lock_protocol"] = 1;
            health["control_process_pid"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            return health;
        }
    }
}
