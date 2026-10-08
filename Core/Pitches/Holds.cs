using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Pitches
{
    // Who held a shared place (a premium spot, an event's booked places) from when until when, in absolute game
    // minutes. Vehicles catch up one after another after a time skip, so a claim is checked against what the
    // others held at that minute, not against who is open now: a vehicle that closed at 12:00 still holds the place
    // for a claim at 11:30.
    public static class Holds
    {
        // The vehicle takes a place at that minute, unless `capacity` others hold one then. Null when it holds one
        // (already or now); otherwise a vehicle holding a place then.
        public static string? Claim(List<PlaceHold> holds, string vehicleId, long minute, int capacity)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            if (Holding(holds, vehicleId)) return null;
            var blocker = Blocker(holds, vehicleId, minute, capacity);
            if (blocker != null) return blocker;
            holds.Add(new PlaceHold { VehicleId = vehicleId, From = minute });
            return null;
        }

        // Who would keep the vehicle from a place at that minute; null when one is free (or it holds one).
        public static string? Blocker(IReadOnlyList<PlaceHold> holds, string vehicleId, long minute, int capacity)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            if (Holding(holds, vehicleId)) return null;
            var others = holds.Where(h => h.VehicleId != vehicleId && HeldAt(h, minute)).ToList();
            if (others.Count < capacity) return null;
            return others.Count == 0 ? string.Empty : others[0].VehicleId;
        }

        // The vehicle gives its place back at that minute.
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

        // Drops holds that ended at or before that minute.
        public static int Prune(List<PlaceHold> holds, long before)
        {
            if (holds == null) throw new ArgumentNullException(nameof(holds));
            return holds.RemoveAll(h => h.Until != null && h.Until <= before);
        }

        private static bool HeldAt(PlaceHold hold, long minute) => hold.From <= minute && (hold.Until == null || hold.Until > minute);
    }
}
