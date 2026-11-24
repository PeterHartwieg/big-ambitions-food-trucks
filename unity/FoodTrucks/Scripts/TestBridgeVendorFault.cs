#nullable enable
using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace FoodTrucks
{
    internal static class TestBridgeVendorFault
    {
        internal const string HookType = "FoodTrucks.VendorFaultHook";
        internal const string Unavailable = "vendor_fault hook unavailable: this FoodTrucks.Core.dll was built without FOODTRUCKS_TEST_FAULTS (tools/build-mod.sh --test-faults).";

        internal static JToken Run(string cmd, JObject args)
        {
            var bridge = typeof(FoodTrucks.Core.Vendor.VendorService).Assembly.GetType(HookType)
                ?.GetMethod("Bridge", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(JObject) }, null);
            if (bridge == null) throw new BridgeError(Unavailable);
            if ((string?)args["action"] != "status") TestBridgeCommands.RequireFtCity(cmd);
            try
            {
                var reply = (JObject)bridge.Invoke(null, new object[] { args });
                Log.Info($"Test bridge: vendor_fault {(string?)args["action"]}: {reply.ToString(Newtonsoft.Json.Formatting.None)}");
                return reply;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException || ex.InnerException is InvalidOperationException)
            {
                throw new BridgeError(ex.InnerException.Message);
            }
        }
    }
}
