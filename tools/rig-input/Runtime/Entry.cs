using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace AgentRigInput
{
    // Called by the agent test runtime (hart-self-driving tools/agent-tests) by reflection, never by a mod.
    // This class touches no Harmony or Unity UI types, so its methods run before those assemblies are resolved.
    public static class Entry
    {
        private static bool resolving;

        // On the main thread, once the bridge pump runs. Returns a one-line status for the runtime's health.
        public static string Start(string product, int port)
        {
            if (!resolving)
            {
                // Harmony and the mod's other dependencies are already loaded by the mod loader; bind to those copies.
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                resolving = true;
            }
            try { return StartService(product, port); }
            catch (Exception ex) { return "failed: " + ex.GetType().Name + ": " + ex.Message; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string StartService(string product, int port) => Service.Start(product, port);

        // Every frame from the bridge pump.
        public static void Tick()
        {
            try { TickService(); }
            catch (Exception) { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TickService() => Service.Instance?.PumpTick();

        // On the bridge's HTTP thread: a snapshot made on the main thread.
        public static JObject Health()
        {
            try { return HealthService(); }
            catch (Exception ex) { return new JObject { ["protocol"] = 1, ["available"] = false, ["status"] = "failed", ["reason"] = ex.Message }; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static JObject HealthService() => Service.HealthSnapshot();

        private static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name;
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
        }
    }
}
