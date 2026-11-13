using System;

namespace FoodTrucks.Core.Common
{
    public static class GameDays
    {
        public static DayOfWeek Weekday(int day)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            return (DayOfWeek)(day % 7);
        }

        public static int DayOf(double absoluteMinute) => (int)Math.Floor(absoluteMinute / 1440.0) + 1;

        public static int SaleDay(double? saleMinute, int liveDay)
        {
            if (liveDay < 1) throw new ArgumentOutOfRangeException(nameof(liveDay));
            if (saleMinute == null) return liveDay;
            if (double.IsNaN(saleMinute.Value) || saleMinute.Value < 0) throw new ArgumentOutOfRangeException(nameof(saleMinute));
            return DayOf(saleMinute.Value);
        }
    }
}
