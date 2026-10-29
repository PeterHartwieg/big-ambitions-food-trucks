using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AgentRigInput
{
    // The loaded Food Trucks test bridge, reached by reflection: this assembly has no compile reference to the mod,
    // so the mod package never depends on it. Every member is checked once at startup; a mismatch disables the service.
    internal static class Bridge
    {
        internal const string TokenKey = "__bridge_lock_token";
        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static Type Commands, BridgeType, Ui, Pump;
        private static Type deferred, bridgeError;
        private static FieldInfo session, current, snapshotSave;
        private static PropertyInfo running;
        private static MethodInfo requireFtCity, uiRun, scene, beforeRun, intercept, track;
        internal static MethodInfo Run, Stop, PumpDisable;
        internal static HashSet<string> SaveLoadOwners;
        internal static int Port;

        // Null when every member is present, else why the service cannot run.
        internal static string Bind()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FoodTrucks");
            if (asm == null) return "the Food Trucks test bridge is not loaded";
            try
            {
                Commands = Need(asm.GetType("FoodTrucks.TestBridgeCommands"), "TestBridgeCommands");
                BridgeType = Need(asm.GetType("FoodTrucks.TestBridge"), "TestBridge");
                Ui = Need(asm.GetType("FoodTrucks.TestBridgeUi"), "TestBridgeUi");
                Pump = Need(asm.GetType("FoodTrucks.TestBridgePump"), "TestBridgePump");
                deferred = Need(asm.GetType("FoodTrucks.Deferred"), "Deferred");
                bridgeError = Need(asm.GetType("FoodTrucks.BridgeError"), "BridgeError");
                Run = Need(Commands.GetMethod("Run", Any, null, new[] { typeof(string), typeof(JObject) }, null), "TestBridgeCommands.Run(string, JObject)");
                Stop = Need(BridgeType.GetMethod("Stop", Any, null, Type.EmptyTypes, null), "TestBridge.Stop()");
                PumpDisable = Need(Pump.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), "TestBridgePump.OnDisable()");
                session = Need(BridgeType.GetField("Session", Any), "TestBridge.Session");
                current = Need(BridgeType.GetField("Current", Any), "TestBridge.Current");
                snapshotSave = Need(current.FieldType.GetField("Save"), "TestBridge.Snapshot.Save");
                running = Need(BridgeType.GetProperty("Running", Any), "TestBridge.Running");
                var port = Need(BridgeType.GetField("Port", Any), "TestBridge.Port");
                Port = Convert.ToInt32(port.IsLiteral ? port.GetRawConstantValue() : port.GetValue(null));
                requireFtCity = Need(Commands.GetMethod("RequireFtCity", Any, null, new[] { typeof(string) }, null), "TestBridgeCommands.RequireFtCity(string)");
                scene = Need(Commands.GetMethod("Scene", Any, null, Type.EmptyTypes, null), "TestBridgeCommands.Scene()");
                uiRun = Need(Ui.GetMethod("Run", Any, null, new[] { typeof(JObject) }, null), "TestBridgeUi.Run(JObject)");
                SaveLoadOwners = Need(Ui.GetField("SaveLoadOwners", Any)?.GetValue(null) as HashSet<string>, "TestBridgeUi.SaveLoadOwners");
                if (deferred.GetConstructor(new[] { typeof(Func<JToken>), typeof(Action) }) == null) return "Deferred(Func<JToken>, Action) changed";
                if (bridgeError.GetConstructor(new[] { typeof(string) }) == null) return "BridgeError(string) changed";
                var gate = BoundControlGate();
                beforeRun = Need(gate.GetMethod("BeforeRun", new[] { typeof(string), typeof(JObject) }), "ControlGate.BeforeRun");
                intercept = Need(gate.GetMethod("Intercept", new[] { typeof(string), typeof(JObject) }), "ControlGate.Intercept");
                track = Need(gate.GetMethod("Track", new[] { typeof(Task<JToken>), typeof(JObject) }), "ControlGate.Track");
            }
            catch (MissingMemberException ex) { return "incompatible bridge: " + ex.Message; }
            return null;
        }

        private static T Need<T>(T value, string what) where T : class =>
            value ?? throw new MissingMemberException(what + " is missing");

        // The lock library copy the bridge itself calls (the mod may carry its own BridgeControl.dll): found from the
        // call in TestBridgeCommands.RefreshOwnership, so ownership checks and reservations share its state.
        private static Type BoundControlGate()
        {
            var refresh = Need(Commands.GetMethod("RefreshOwnership", Any, null, Type.EmptyTypes, null), "TestBridgeCommands.RefreshOwnership()");
            var il = refresh.GetMethodBody()?.GetILAsByteArray() ?? throw new MissingMemberException("RefreshOwnership body");
            foreach (var call in IlScanner.Calls(il))
            {
                MethodBase m;
                try { m = refresh.Module.ResolveMethod(call.Token); }
                catch (Exception) { continue; }
                if (m.Name == "ObserveWork" && m.DeclaringType?.FullName == "BridgeControl.ControlGate") return m.DeclaringType;
            }
            throw new MissingMemberException("the bridge's ControlGate.ObserveWork call");
        }

        internal static bool Running => (bool)running.GetValue(null);
        internal static string Session => (string)session.GetValue(null);
        internal static string Save => current.GetValue(null) is object s ? (string)snapshotSave.GetValue(s) : null;
        internal static string Scene() => (string)Invoke(scene);

        internal static Exception Error(string message) => (Exception)Activator.CreateInstance(bridgeError, message);
        internal static object Deferred(Func<JToken> poll, Action onStop) => Activator.CreateInstance(deferred, poll, onStop);

        internal static void BeforeRun(string cmd, JObject args) => Invoke(beforeRun, cmd, args);
        internal static void RequireFtCity(string what) => Invoke(requireFtCity, what);
        // The existing guarded `ui escape` (FT city and character checks, the game's own Esc handling).
        internal static JToken Escape() => (JToken)Invoke(uiRun, new JObject { ["op"] = "escape" });

        // An extra command reservation held for the whole interaction, through the bridge's public lock API, so lease
        // release is refused while a virtual button is down even after the down command's own reply.
        internal static Action Reserve(string token)
        {
            var args = new JObject { [TokenKey] = token };
            Invoke(intercept, "mouse", args);
            var done = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            Invoke(track, done.Task, args);
            return () => done.TrySetResult(null);
        }

        internal static void AssertOwner(string token) => Invoke(beforeRun, "mouse", new JObject { [TokenKey] = token });

        private static object Invoke(MethodInfo method, params object[] args)
        {
            try { return method.Invoke(null, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
