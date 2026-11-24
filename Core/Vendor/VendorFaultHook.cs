#if FOODTRUCKS_TEST_FAULTS
using System;
using Newtonsoft.Json.Linq;

namespace FoodTrucks
{
    public sealed class VendorFaultException : Exception
    {
        public VendorFaultException(string point, string vehicleId, string item)
            : base($"FoodTrucks test fault {point}: VendorFaultHook threw after the ledger record of {item} at {vehicleId}")
        {
            Point = point;
        }

        public string Point { get; }
    }

    public static class VendorFaultHook
    {
        public const string SellAfterLedger = "sell_after_ledger";

        private static readonly object gate = new object();
        private static string? point;
        private static int shots;
        private static bool fired;

        public static bool Armed { get { lock (gate) return shots > 0; } }
        public static string? Point { get { lock (gate) return point; } }
        public static int ShotsRemaining { get { lock (gate) return shots; } }
        public static bool Fired { get { lock (gate) return fired; } }

        public static void Arm(string? at)
        {
            lock (gate)
            {
                if (at != SellAfterLedger) throw new ArgumentException($"Unknown fault point '{at}': {SellAfterLedger}.");
                if (shots > 0) throw new InvalidOperationException($"The fault hook is already armed at {point}: disarm it or let the shot fire first.");
                point = at;
                shots = 1;
                fired = false;
            }
        }

        public static void Disarm()
        {
            lock (gate)
            {
                point = null;
                shots = 0;
            }
        }

        public static void Reset()
        {
            lock (gate)
            {
                point = null;
                shots = 0;
                fired = false;
            }
        }

        public static void AfterLedger(string vehicleId, string item)
        {
            lock (gate)
            {
                if (shots <= 0 || point != SellAfterLedger) return;
                shots = 0;
                fired = true;
            }
            throw new VendorFaultException(SellAfterLedger, vehicleId, item);
        }

        public static JObject Bridge(JObject args)
        {
            var action = (string?)args["action"];
            switch (action)
            {
                case "arm":
                    lock (gate)
                    {
                        Arm((string?)args["point"]);
                        return new JObject { ["armed"] = true, ["point"] = point, ["shots_remaining"] = shots };
                    }
                case "status":
                    lock (gate)
                        return new JObject { ["armed"] = shots > 0, ["point"] = point, ["shots_remaining"] = shots, ["fired"] = fired };
                case "disarm":
                    Disarm();
                    return new JObject { ["armed"] = false, ["fired"] = Fired };
                default:
                    throw new ArgumentException($"Unknown vendor_fault action '{action}': arm, status or disarm.");
            }
        }
    }
}
#endif
