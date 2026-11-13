using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Pitches
{
    public static class Holds
    {
        public static string? Claim(List<PlaceHold> holds, string vehicleId, long minute, int capacity)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            if (Holding(holds, vehicleId)) return null;
            var blocker = Blocker(holds, vehicleId, minute, capacity);
            if (blocker != null) return blocker;
            holds.Add(new PlaceHold { VehicleId = vehicleId, From = minute });
            return null;
        }

        public static string? Blocker(IReadOnlyList<PlaceHold> holds, string vehicleId, long minute, int capacity)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            if (Holding(holds, vehicleId)) return null;
            if (capacity <= 0) return holds.FirstOrDefault(h => h.VehicleId != vehicleId)?.VehicleId ?? string.Empty;
            var others = holds.Where(h => h.VehicleId != vehicleId && (h.Until == null || h.Until > Math.Max(h.From, minute))).ToList();
            var changes = others.Select(h => (At: Math.Max(h.From, minute), Step: 1, Hold: h))
                .Concat(others.Where(h => h.Until != null).Select(h => (At: h.Until!.Value, Step: -1, Hold: h)))
                .OrderBy(c => c.At).ThenBy(c => c.Step);
            var held = new List<PlaceHold>();
            foreach (var change in changes)
            {
                if (change.Step < 0) held.Remove(change.Hold);
                else held.Add(change.Hold);
                if (held.Count >= capacity) return held[0].VehicleId;
            }
            return null;
        }

        public static int Release(List<PlaceHold> holds, string vehicleId, long minute)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            var released = 0;
            foreach (var hold in holds.Where(h => h.VehicleId == vehicleId && h.Until == null))
            {
                hold.Until = Math.Max(hold.From, minute);
                released++;
            }
            return released;
        }

        public static bool Holding(IReadOnlyList<PlaceHold> holds, string vehicleId) =>
            holds.Any(h => h.VehicleId == vehicleId && h.Until == null);

        public static bool HeldBy(IReadOnlyList<PlaceHold> holds, string vehicleId, long minute) =>
            holds.Any(h => h.VehicleId == vehicleId && HeldAt(h, minute));

        public static int Prune(List<PlaceHold> holds, long before)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            return holds.RemoveAll(h => h.Until != null && h.Until <= before);
        }

        private static bool HeldAt(PlaceHold hold, long minute) => hold.From <= minute && (hold.Until == null || hold.Until > minute);
    }
}
