using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.LowLevel;

namespace AgentRigInput
{
    internal static class Log
    {
        internal static void Info(string text) => Debug.Log("[AgentRigInput] " + text);
        internal static void Error(string text) => Debug.LogError("[AgentRigInput] " + text);
    }

    internal static class Util
    {
        internal static IEnumerable<Type> Types(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch (Exception) { return Type.EmptyTypes; }
        }
    }

    // Only the isolated agent test player with the Food Trucks test flag: never the normal game, even with the flag.
    internal static class RigGate
    {
        internal const int Agent1 = 18330, AgentMac = 28330;

        internal static string Check(string product, int port)
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-agent-tests")) return "not started with -agent-tests";
            if (!args.Any(a => string.Equals(a, "-foodtrucks-test", StringComparison.OrdinalIgnoreCase))) return "not started with -foodtrucks-test";
            if (port != Agent1 && port != AgentMac) return $"port {port} is not an agent test port";
            if (product == null || !product.StartsWith("Big Ambitions Agent Tests", StringComparison.Ordinal) || Application.productName != product)
                return "not the isolated agent test product";
            var host = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AgentTestRuntime")?.GetType("AgentTests.Host");
            if (!(host?.GetProperty("IsReady", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) is bool ready) || !ready)
                return "the isolated agent test runtime is not ready";
            return null;
        }
    }

    // Two player-loop systems: one at the start of EarlyUpdate (before the Input System's update, EventSystem.Update
    // and coroutines read the frame's input) and one at the start of PreLateUpdate (after Update and the coroutines
    // resumed in it, before LateUpdate and the next frame).
    internal static class FrameDriver
    {
        private struct RigMouseEarly { }
        private struct RigMouseLate { }

        internal static void Install(PlayerLoopSystem.UpdateFunction early, PlayerLoopSystem.UpdateFunction late)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            if (!Insert(ref loop, typeof(UnityEngine.PlayerLoop.EarlyUpdate), new PlayerLoopSystem { type = typeof(RigMouseEarly), updateDelegate = early }) ||
                !Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate), new PlayerLoopSystem { type = typeof(RigMouseLate), updateDelegate = late }))
                throw new InvalidOperationException("EarlyUpdate or PreLateUpdate is missing from the player loop");
            PlayerLoop.SetPlayerLoop(loop);
            Log.Info("Player loop: RigMouseEarly at EarlyUpdate[0], RigMouseLate at PreLateUpdate[0]: Active.");
        }

        internal static void Uninstall()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Remove(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        internal static bool Installed()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            return loop.subSystemList != null && loop.subSystemList.Any(s => s.subSystemList != null && s.subSystemList.Any(x => x.type == typeof(RigMouseEarly)));
        }

        private static bool Insert(ref PlayerLoopSystem loop, Type phase, PlayerLoopSystem system)
        {
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != phase) continue;
                var list = loop.subSystemList[i].subSystemList?.ToList() ?? new List<PlayerLoopSystem>();
                list.Insert(0, system);
                loop.subSystemList[i].subSystemList = list.ToArray();
                return true;
            }
            return false;
        }

        private static bool Remove(ref PlayerLoopSystem loop)
        {
            var changed = false;
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                var subs = loop.subSystemList[i].subSystemList;
                if (subs == null) continue;
                var kept = subs.Where(s => s.type != typeof(RigMouseEarly) && s.type != typeof(RigMouseLate)).ToArray();
                if (kept.Length == subs.Length) continue;
                loop.subSystemList[i].subSystemList = kept;
                changed = true;
            }
            return changed;
        }
    }
}
