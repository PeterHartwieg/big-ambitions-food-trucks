using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentRigInput
{
    // Identical-signature replacements for Unity's legacy mouse reads. Unity's own methods are untouched (the button
    // reads are InternalCall and cannot carry a prefix); patched game call sites call these instead, and these call
    // Unity whenever no virtual interaction is active.
    public static class LegacyInputShim
    {
        internal static volatile bool Active;
        internal static VirtualMouseState State;
        internal static int ButtonReads, PositionReads, ScrollReads;

        public static bool GetMouseButton(int button)
        {
            if (!Active) return Input.GetMouseButton(button);
            ButtonReads++;
            return State.IsHeld(button);
        }

        public static bool GetMouseButtonDown(int button)
        {
            if (!Active) return Input.GetMouseButtonDown(button);
            ButtonReads++;
            return State.IsDown(button);
        }

        public static bool GetMouseButtonUp(int button)
        {
            if (!Active) return Input.GetMouseButtonUp(button);
            ButtonReads++;
            return State.IsUp(button);
        }

        public static Vector3 get_mousePosition()
        {
            if (!Active) return Input.mousePosition;
            PositionReads++;
            return new Vector3((float)State.X, (float)State.Y, 0f);
        }

        public static Vector2 get_mouseScrollDelta()
        {
            if (!Active) return Input.mouseScrollDelta;
            ScrollReads++;
            return new Vector2((float)State.ScrollX, (float)State.ScrollY);
        }

        public static bool get_mousePresent() => Active || Input.mousePresent;

        internal static JObject TakeReads()
        {
            var reads = new JObject { ["button"] = ButtonReads, ["position"] = PositionReads, ["scroll"] = ScrollReads };
            ButtonReads = PositionReads = ScrollReads = 0;
            return reads;
        }
    }

    // Finds the legacy mouse call sites of a fixed list of game assemblies (iterator and other nested types included)
    // and reroutes them to the shim. Other assemblies, other mods included, are reported as not covered.
    internal sealed class LegacyInput
    {
        internal static readonly string[] Policy = { "BigAmbitions", "BigAmbitions.InteriorDesigner", "BigAmbitions.PlacementSystem", "BigAmbitions.InputSystem" };
        private const string Schedule = "UI.Smartphone.Apps.BizMan.Schedule.";
        private Task<List<(MethodBase Method, int Calls)>> scan;
        private readonly List<string> problems = new List<string>();
        private readonly List<string> scanned = new List<string>();
        private int patched, sites, skipped;
        internal bool Applied { get; private set; }
        internal string Backend { get; private set; } = "scanning";
        internal bool ScheduleResize { get; private set; }
        internal bool ScheduleEdgeScroll { get; private set; }
        internal MethodBase ResizeCoroutine { get; private set; }

        internal static IEnumerable<KeyValuePair<MethodBase, MethodInfo>> Map()
        {
            var input = typeof(Input);
            var shim = typeof(LegacyInputShim);
            MethodInfo M(Type t, string name, params Type[] p) => t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, p, null)
                                                                  ?? throw new MissingMethodException(t.Name, name);
            foreach (var name in new[] { "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp" })
                yield return new KeyValuePair<MethodBase, MethodInfo>(M(input, name, typeof(int)), M(shim, name, typeof(int)));
            foreach (var name in new[] { "mousePosition", "mouseScrollDelta", "mousePresent" })
                yield return new KeyValuePair<MethodBase, MethodInfo>(input.GetProperty(name).GetGetMethod(), M(shim, "get_" + name));
        }

        internal void StartScan()
        {
            CallSiteTranspiler.SetMap(Map());
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => Policy.Contains(a.GetName().Name)).ToList();
            scanned.AddRange(assemblies.Select(a => a.GetName().Name));
            var own = typeof(LegacyInputShim).Assembly;
            // Reading IL and metadata does not touch Unity, so the scan stays off the main thread.
            scan = Task.Run(() => CallSiteTranspiler.Scan(assemblies, own, problems));
        }

        // On the main thread once the scan is done.
        internal void ApplyIfReady(Harmony harmony)
        {
            if (Applied || scan == null || !scan.IsCompleted) return;
            Applied = true;
            if (scan.IsFaulted) { Backend = "unavailable: scan failed: " + scan.Exception?.InnerException?.Message; Log.Info(Backend); return; }
            var transpiler = new HarmonyMethod(typeof(CallSiteTranspiler).GetMethod(nameof(CallSiteTranspiler.Transpiler)));
            foreach (var (method, calls) in scan.Result)
            {
                var name = $"{method.DeclaringType?.FullName}.{method.Name}";
                if (method.DeclaringType == null || method.DeclaringType.ContainsGenericParameters || method.ContainsGenericParameters)
                {
                    skipped++;
                    Log.Info($"Patch {name} (legacy mouse call sites): Not active (generic method).");
                    continue;
                }
                try
                {
                    harmony.Patch(method, transpiler: transpiler);
                    CallSiteTranspiler.Replaced.TryGetValue(method, out var replaced);
                    if (replaced != calls) problems.Add($"{name}: {replaced} of {calls} call sites rewritten");
                    patched++; sites += replaced;
                    Log.Info($"Patch {name} (legacy mouse call sites): Active, {replaced} of {calls} rerouted.");
                    var type = method.DeclaringType.FullName ?? "";
                    if (method.Name == "MoveNext" && type.StartsWith(Schedule + "WorkShiftSliderHandle+") && type.Contains("DraggingCoroutine") && replaced >= 2)
                    {
                        ScheduleResize = true;
                        ResizeCoroutine = method;
                    }
                    if (method.Name == "MoveNext" && type.StartsWith(Schedule + "WorkShiftHelper+") && type.Contains("HandleAutoScrollOnEdges") && replaced >= 1)
                        ScheduleEdgeScroll = true;
                }
                catch (Exception ex)
                {
                    problems.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                    Log.Info($"Patch {name} (legacy mouse call sites): Not active ({ex.Message}).");
                }
            }
            if (patched == 0) Backend = "unavailable: no legacy mouse call sites were rerouted";
            else if (!ScheduleResize) Backend = "partial: the Schedule resize handle coroutine was not found or not rerouted";
            else Backend = "ready";
            Log.Info($"Legacy mouse input: {Backend}; {patched} methods, {sites} call sites, {skipped} skipped, {problems.Count} problems.");
        }

        internal JObject Coverage() => new JObject
        {
            ["scanned_assemblies"] = new JArray(scanned),
            ["policy"] = new JArray(Policy),
            ["methods_patched"] = patched,
            ["call_sites"] = sites,
            ["skipped"] = skipped,
            ["schedule_resize"] = ScheduleResize,
            ["schedule_edge_scroll"] = ScheduleEdgeScroll,
            ["problems"] = new JArray(problems.Take(20)),
            ["not_covered"] = "assemblies outside the policy, other mods included",
        };
    }
}
