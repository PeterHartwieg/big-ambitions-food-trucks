using System;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    public static class SalesLedger
    {
        public static void Record(SaveState state, int day, long revenueCents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (revenueCents < 0) throw new ArgumentOutOfRangeException(nameof(revenueCents));
            if (revenueCents == 0) return;
            var entry = state.DailySales.FirstOrDefault(d => d.Day == day);
            if (entry == null)
            {
                entry = new DailySales { Day = day };
                state.DailySales.Add(entry);
            }
            entry.RevenueCents = checked(entry.RevenueCents + revenueCents);
        }

        // Inclusive on both ends, like the game's tax period.
        public static long SumCents(SaveState? state, int firstDay, int lastDay)
        {
            if (state == null) return 0;
            long total = 0;
            foreach (var entry in state.DailySales)
            {
                if (entry.Day >= firstDay && entry.Day <= lastDay)
                    total = checked(total + entry.RevenueCents);
            }
            return total;
        }
    }
}
