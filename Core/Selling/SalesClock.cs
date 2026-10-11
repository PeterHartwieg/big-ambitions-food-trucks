using System;

namespace FoodTrucks.Core.Selling
{
    // A window's sales clock: the absolute game minute it has sold up to. It starts on a whole game minute and moves in
    // whole-minute steps that never cross an hour, so a catch-up to midnight (or any hour) lands on it exactly. A clock
    // started at the live clock's fraction (20:00.5) would stop half a minute short of midnight, keep the closing day open
    // past the game's midnight processing, and book its last sales into that day afterwards (review round 2 of 0.9.10).
    public static class SalesClock
    {
        // Where a clock starts (a window opening, after a skip, after a load): the whole minute it is in.
        public static double Start(double minute)
        {
            if (double.IsNaN(minute) || double.IsInfinity(minute)) throw new ArgumentOutOfRangeException(nameof(minute));
            return Math.Floor(minute);
        }

        // The end of the next step from `from` towards `until`: up to `stepMinutes` whole minutes, never past the next
        // hour; null when less than a whole minute is left.
        public static double? NextStep(double from, double until, double stepMinutes = 1)
        {
            if (until - from < 1) return null;
            var nextHour = (Math.Floor(from / 60) + 1) * 60;
            return Math.Min(until, Math.Max(from + 1, Math.Min(from + stepMinutes, nextHour)));
        }

        // The whole minutes a step from `from` to `to` sells.
        public static int Elapsed(double from, double to) => (int)Math.Floor(to - from);
    }
}
