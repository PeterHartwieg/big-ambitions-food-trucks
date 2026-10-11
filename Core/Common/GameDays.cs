using System;

namespace FoodTrucks.Core.Common
{
    public static class GameDays
    {
        // TimeHelper starts at Monday [code, unverified in game].
        public static DayOfWeek Weekday(int day)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            return (DayOfWeek)(day % 7);
        }

        // The calendar day an absolute game minute ((day - 1) × 1440 + minute of the day) falls on.
        public static int DayOf(double absoluteMinute) => (int)Math.Floor(absoluteMinute / 1440.0) + 1;

        // The day a sale is booked on: the day of the minute it was simulated at, which lags the live day while sales
        // catch up behind the clock (across midnight, a sleep, or into a new tax period); the live day when the sale
        // has no minute of its own.
        public static int SaleDay(double? saleMinute, int liveDay)
        {
            if (liveDay < 1) throw new ArgumentOutOfRangeException(nameof(liveDay));
            if (saleMinute == null) return liveDay;
            if (double.IsNaN(saleMinute.Value) || saleMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(saleMinute));
            return DayOf(saleMinute.Value);
        }
    }
}
