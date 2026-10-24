using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Selling
{
    // The first cooler occupies the reserved place. Further coolers are ordinary equipment,
    // retaining the capacity and validity of existing multi-fridge saves.
    public static class EquipmentLayout
    {
        public const string CoolerPlace = "EquipmentCooler";
        public static bool IsCooler(string? id) => id == "cooler";

        // General places first, reserved place last. Optional previous IDs keep existing general
        // displays in place when another piece is removed; reservation always takes precedence.
        public static IReadOnlyList<(int Slot, T Piece)> Place<T>(IEnumerable<T> pieces, int generalSlots,
            int coolerSlots, Func<T, string?> id, IReadOnlyList<string?>? previous = null)
        {
            var remaining = pieces.ToList();
            var result = new List<(int Slot, T Piece)>();
            for (var i = 0; i < coolerSlots; i++)
            {
                var index = remaining.FindIndex(p => IsCooler(id(p)));
                if (index < 0) break;
                result.Add((generalSlots + i, remaining[index]));
                remaining.RemoveAt(index);
            }
            var occupied = new HashSet<int>();
            if (previous != null)
                for (var slot = 0; slot < Math.Min(generalSlots, previous.Count); slot++)
                {
                    if (previous[slot] == null) continue;
                    var index = remaining.FindIndex(p => id(p) == previous[slot]);
                    if (index < 0) continue;
                    result.Add((slot, remaining[index]));
                    occupied.Add(slot);
                    remaining.RemoveAt(index);
                }
            for (var slot = 0; slot < generalSlots && remaining.Count > 0; slot++)
            {
                if (occupied.Contains(slot)) continue;
                result.Add((slot, remaining[0]));
                remaining.RemoveAt(0);
            }
            return result;
        }
    }
}
