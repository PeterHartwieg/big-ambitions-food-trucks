using System;
using FoodTrucks.Core.Common;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class GameDaysTests
{
    [Theory]
    [InlineData(1, DayOfWeek.Monday)]
    [InlineData(6, DayOfWeek.Saturday)]
    [InlineData(7, DayOfWeek.Sunday)]
    [InlineData(8, DayOfWeek.Monday)]
    [InlineData(14, DayOfWeek.Sunday)]
    public void DaysFollowTheGameCalendar(int day, DayOfWeek expected)
    {
        Assert.Equal(expected, GameDays.Weekday(day));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void DaysBelowOneAreRejected(int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameDays.Weekday(day));
    }
}
