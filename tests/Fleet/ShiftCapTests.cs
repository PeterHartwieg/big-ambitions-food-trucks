using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class ShiftCapTests
{
    private static readonly FleetRules shipped = ShippedData.Rules.Fleet;

    private static CrewShift S(string who, DayOfWeek day, int start, int end, string row = "serving1") =>
        new() { EmployeeId = who, Day = day, Start = start, End = end, Station = row };

    private static List<CrewShift> L(params CrewShift[] shifts) => shifts.ToList();

    [Fact]
    public void ShippedCapIsTwelveHours()
    {
        Assert.Equal(12, shipped.MaxShiftHours);
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(), L(S("e1", DayOfWeek.Monday, 0, 12))));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 13) },
            WeekPlan.OverMaxHours(shipped, L(S("e1", DayOfWeek.Monday, 0, 12)), L(S("e1", DayOfWeek.Monday, 0, 13))));
    }

    [Theory]
    [InlineData(8, 8, false)]
    [InlineData(8, 9, true)]
    [InlineData(12, 12, false)]
    [InlineData(12, 13, true)]
    [InlineData(24, 24, false)]
    public void CapComesFromTheRules(int cap, int hours, bool over)
    {
        var rules = new FleetRules(maxShiftHours: cap);
        var result = WeekPlan.OverMaxHours(rules, L(), L(S("e1", DayOfWeek.Tuesday, 0, hours)));
        Assert.Equal(over, result.Count == 1);
    }

    [Theory]
    [InlineData(0, 13)]
    [InlineData(0, 24)]
    [InlineData(6, 19)]
    public void OneShiftOverTheCap(int start, int end)
    {
        var result = WeekPlan.OverMaxHours(shipped, L(S("e1", DayOfWeek.Monday, 8, 16)), L(S("e1", DayOfWeek.Monday, start, end)));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, end - start) }, result);
    }

    [Fact]
    public void SeveralShiftsAddUpAcrossRows()
    {
        var accepted = L(S("e1", DayOfWeek.Monday, 0, 8));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 14) },
            WeekPlan.OverMaxHours(shipped, accepted, L(S("e1", DayOfWeek.Monday, 0, 8), S("e1", DayOfWeek.Monday, 10, 16))));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 14) },
            WeekPlan.OverMaxHours(shipped, accepted, L(S("e1", DayOfWeek.Monday, 0, 8), S("e1", DayOfWeek.Monday, 10, 16, "serving2"))));
        Assert.Single(WeekPlan.OverMaxHours(shipped, L(),
            L(S("e1", DayOfWeek.Friday, 0, 5), S("e1", DayOfWeek.Friday, 6, 11), S("e1", DayOfWeek.Friday, 12, 15))));
    }

    [Fact]
    public void ExactlyAtTheCapIsAllowed()
    {
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(S("e1", DayOfWeek.Monday, 0, 6)),
            L(S("e1", DayOfWeek.Monday, 0, 6), S("e1", DayOfWeek.Monday, 8, 14))));
        Assert.Empty(WeekPlan.OverMaxHours(new FleetRules(maxShiftHours: 8), L(),
            L(S("e1", DayOfWeek.Monday, 0, 4), S("e1", DayOfWeek.Monday, 10, 14, "serving2"))));
    }

    [Fact]
    public void TheCapIsPerCrewMemberNotPerVehicle()
    {
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(),
            L(S("e1", DayOfWeek.Monday, 0, 12), S("e2", DayOfWeek.Monday, 0, 12, "serving2"))));
    }

    [Fact]
    public void MidnightSplitCountsOnEachWeekday()
    {
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(), L(S("e1", DayOfWeek.Monday, 18, 24), S("e1", DayOfWeek.Tuesday, 0, 6))));
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(), L(S("e1", DayOfWeek.Sunday, 12, 24), S("e1", DayOfWeek.Monday, 0, 12))));
        var night = L(S("e1", DayOfWeek.Monday, 18, 24), S("e1", DayOfWeek.Tuesday, 0, 6));
        Assert.Equal(new[] { ("e1", DayOfWeek.Tuesday, 13) },
            WeekPlan.OverMaxHours(shipped, night, night.Append(S("e1", DayOfWeek.Tuesday, 8, 15))));
        var wrap = L(S("e1", DayOfWeek.Sunday, 14, 24), S("e1", DayOfWeek.Monday, 0, 4));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 13) },
            WeekPlan.OverMaxHours(shipped, wrap, wrap.Append(S("e1", DayOfWeek.Monday, 6, 15))));
        Assert.Empty(WeekPlan.OverMaxHours(shipped, wrap, wrap.Append(S("e1", DayOfWeek.Monday, 6, 14))));
    }
}
