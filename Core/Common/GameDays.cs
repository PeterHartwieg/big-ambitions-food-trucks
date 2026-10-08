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
    }
}
