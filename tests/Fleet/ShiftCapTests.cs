using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// A crew member's hours on one weekday, all their shifts together, stay within FleetRules.MaxShiftHours when the
// Schedule tab changes them (issue #14); groups the edit leaves alone aren't judged.
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
        // 8 + 6 on one row, and on two rows.
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 14) },
            WeekPlan.OverMaxHours(shipped, accepted, L(S("e1", DayOfWeek.Monday, 0, 8), S("e1", DayOfWeek.Monday, 10, 16))));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 14) },
            WeekPlan.OverMaxHours(shipped, accepted, L(S("e1", DayOfWeek.Monday, 0, 8), S("e1", DayOfWeek.Monday, 10, 16, "serving2"))));
        // Three short shifts.
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
        // Two crew members, 12 hours each on their own rows: 24 vehicle hours, both within the cap.
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(),
            L(S("e1", DayOfWeek.Monday, 0, 12), S("e2", DayOfWeek.Monday, 0, 12, "serving2"))));
    }

    [Fact]
    public void MidnightSplitCountsOnEachWeekday()
    {
        // Monday 18-24 and Tuesday 0-6: twelve hours across midnight, six on each day. No rolling-24-hour rule.
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(), L(S("e1", DayOfWeek.Monday, 18, 24), S("e1", DayOfWeek.Tuesday, 0, 6))));
        // A full Sunday 12-24 then Monday 0-12: each day exactly at the cap.
        Assert.Empty(WeekPlan.OverMaxHours(shipped, L(), L(S("e1", DayOfWeek.Sunday, 12, 24), S("e1", DayOfWeek.Monday, 0, 12))));
        // Tuesday's after-midnight part counts with Tuesday's other shift: 6 + 7.
        var night = L(S("e1", DayOfWeek.Monday, 18, 24), S("e1", DayOfWeek.Tuesday, 0, 6));
        Assert.Equal(new[] { ("e1", DayOfWeek.Tuesday, 13) },
            WeekPlan.OverMaxHours(shipped, night, night.Append(S("e1", DayOfWeek.Tuesday, 8, 15))));
        // The week's wrap: Sunday 14-24 into Monday 0-4, then Monday 6-15 is 13 hours on Monday; Sunday stays at 10.
        var wrap = L(S("e1", DayOfWeek.Sunday, 14, 24), S("e1", DayOfWeek.Monday, 0, 4));
        Assert.Equal(new[] { ("e1", DayOfWeek.Monday, 13) },
            WeekPlan.OverMaxHours(shipped, wrap, wrap.Append(S("e1", DayOfWeek.Monday, 6, 15))));
        Assert.Empty(WeekPlan.OverMaxHours(shipped, wrap, wrap.Append(S("e1", DayOfWeek.Monday, 6, 14))));
    }

    [Fact]
    public void AnUntouchedLegacyGroupIsNotJudged()
    {
        // A save's Tuesday of 14 hours (two shifts) from before the cap was checked, and a valid Monday.
        var legacy = L(S("e1", DayOfWeek.Monday, 0, 6), S("e1", DayOfWeek.Tuesday, 0, 8), S("e1", DayOfWeek.Tuesday, 10, 16));
        // Monday's edit is judged on Monday only.
        Assert.Empty(WeekPlan.OverMaxHours(shipped, legacy,
            L(S("e1", DayOfWeek.Monday, 0, 8), S("e1", DayOfWeek.Tuesday, 0, 8), S("e1", DayOfWeek.Tuesday, 10, 16))));
        // Another crew member's edit, the same Tuesday.
        Assert.Empty(WeekPlan.OverMaxHours(shipped, legacy, legacy.Append(S("e2", DayOfWeek.Tuesday, 0, 4, "serving2"))));
        // Moving the legacy shifts to another row changes no hours.
        Assert.Empty(WeekPlan.OverMaxHours(shipped, legacy,
            L(S("e1", DayOfWeek.Monday, 0, 6), S("e1", DayOfWeek.Tuesday, 0, 8, "serving2"), S("e1", DayOfWeek.Tuesday, 10, 16))));
        // Changing the legacy day's hours makes it an edit: still over is refused, within the cap is fine, removing is fine.
        Assert.Equal(new[] { ("e1", DayOfWeek.Tuesday, 13) }, WeekPlan.OverMaxHours(shipped, legacy,
            L(S("e1", DayOfWeek.Monday, 0, 6), S("e1", DayOfWeek.Tuesday, 0, 8), S("e1", DayOfWeek.Tuesday, 11, 16))));
        Assert.Empty(WeekPlan.OverMaxHours(shipped, legacy,
            L(S("e1", DayOfWeek.Monday, 0, 6), S("e1", DayOfWeek.Tuesday, 0, 8), S("e1", DayOfWeek.Tuesday, 12, 16))));
        Assert.Empty(WeekPlan.OverMaxHours(shipped, legacy, L(S("e1", DayOfWeek.Monday, 0, 6))));
    }

    [Fact]
    public void LegacyOverLimitSaveLoadsUnchanged()
    {
        // Characterisation: loading leaves an over-limit day as saved (legacy saves are out of scope for issue #14).
        var vehicle = new VehicleState
        {
            VehicleId = "t1", VehicleType = "truck", Crew = new List<string> { "e1" },
            Schedule = new HireSchedule { CrewShifts = L(S("e1", DayOfWeek.Tuesday, 0, 24)) }
        };
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(new SaveState { Vehicles = new List<VehicleState> { vehicle } }));
        var shift = Assert.Single(loaded.State!.Vehicles.Single().Schedule!.CrewShifts);
        Assert.Equal((0, 24), (shift.Start, shift.End));
    }
}
