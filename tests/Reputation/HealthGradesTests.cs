using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Reputation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class HealthGradesTests
{
    private static readonly HealthRules rules = new();
    private static readonly HealthGrades health = new(rules);

    [Theory]
    [InlineData(0, Grade.A)]
    [InlineData(13, Grade.A)]
    [InlineData(14, Grade.B)]
    [InlineData(27, Grade.B)]
    [InlineData(28, Grade.C)]
    public void ThresholdsGiveTheGrade(int points, Grade expected)
    {
        Assert.Equal(expected, health.GradeFor(points));
    }

    [Fact]
    public void ANewVehicleHasGradePendingAndNeutralDemand()
    {
        var state = HealthGrades.NewVehicle(rules, 1, 42);
        Assert.Equal(Grade.Ungraded, state.Grade);
        Assert.Null(state.LastInspectionDay);
        Assert.Equal(1, state.FirstSeenDay);
        Assert.Equal(42, state.RandomState);
        Assert.Equal(1, health.DemandFactor(state));
    }

    [Fact]
    public void DailyCleaningStaysAOverEightWeeks()
    {
        var state = HealthGrades.NewVehicle(rules, 1, 42);
        var inspections = 0;
        for (var day = 1; day <= 56; day++)
        {
            var result = health.OnOpen(state, day);
            if (result.HasValue)
            {
                inspections++;
                Assert.Equal(Grade.A, result.Value.Grade);
                Assert.Equal(0, result.Value.Points);
            }
            health.RecordDay(state, day, cleaned: true, soldStaleStock: false);
            Assert.Equal(0, health.Points(state));
        }
        Assert.True(inspections > 0);
        Assert.Equal(Grade.A, state.Grade);
    }

    [Fact]
    public void InspectionSeesEarlierOpenDaysAndCleaningDoesNotChangeWindowGrade()
    {
        var custom = new HealthRules(firstInspectionWeeks: 1, daysPerWeek: 1, minimumGapDays: 7);
        var grades = new HealthGrades(custom);
        var state = HealthGrades.NewVehicle(custom, 1, 42);
        for (var day = 1; day <= 7; day++) grades.RecordDay(state, day, false, true);
        Assert.Equal(49, grades.OnOpen(state, 8)!.Value.Points);
        Assert.Equal(Grade.C, state.Grade);
        for (var day = 8; day <= 21; day++) grades.RecordDay(state, day, true, false);
        Assert.Equal(0, grades.Points(state));
        Assert.Equal(Grade.C, state.Grade);
        Assert.Equal(0.75, grades.DemandFactor(state));
        Assert.Equal(Grade.A, grades.OnOpen(state, 22)!.Value.Grade);
    }

    [Fact]
    public void CleaningEveryOtherOpenDayGivesB()
    {
        var state = HealthGrades.NewVehicle(rules, 1, 42);
        for (var day = 1; day <= 14; day++) health.RecordDay(state, day, day % 2 == 0, false);
        Assert.Equal(21, health.Points(state));
        Assert.Equal(Grade.B, health.GradeFor(health.Points(state)));
    }

    [Theory]
    [InlineData(0, Grade.A, 12)]
    [InlineData(14, Grade.B, 6)]
    [InlineData(28, Grade.C, 4)]
    public void MeanInspectionGapMatchesEachGrade(int points, Grade grade, int weeks)
    {
        long totalGap = 0;
        for (var seed = 0; seed < 2000; seed++)
        {
            var state = HealthGrades.NewVehicle(rules, 1, seed);
            state.Grade = grade;
            state.LastInspectionDay = 1;
            state.Marks.Add(new HealthMark { Day = 1, Points = points });
            var day = 2;
            while (!health.OnOpen(state, day).HasValue) day++;
            totalGap += day - 1;
            Assert.Equal(grade, state.Grade);
        }
        Assert.InRange(totalGap / 2000.0, weeks * 7 * 0.8, weeks * 7 * 1.2);
    }

    [Theory]
    [InlineData(Grade.Ungraded)]
    [InlineData(Grade.A)]
    [InlineData(Grade.B)]
    [InlineData(Grade.C)]
    public void NoInspectionInsideGapAndSomeSeedInspectsAtItsBoundary(Grade grade)
    {
        var found = false;
        for (var seed = 0; seed < 1000; seed++)
        {
            var state = HealthGrades.NewVehicle(rules, 10, seed);
            state.Grade = grade;
            if (grade != Grade.Ungraded) state.LastInspectionDay = 10;
            for (var day = 10; day < 17; day++) Assert.Null(health.OnOpen(state, day));
            Assert.Equal(seed, state.RandomState);
            if (health.OnOpen(state, 17).HasValue) found = true;
        }
        Assert.True(found);
    }

    [Fact]
    public void ZeroGapAndShortIntervalInspectImmediatelyButNotTwice()
    {
        var custom = new HealthRules(firstInspectionWeeks: 1, daysPerWeek: 1, minimumGapDays: 0);
        var grades = new HealthGrades(custom);
        var state = HealthGrades.NewVehicle(custom, 1, 42);
        Assert.NotNull(grades.OnOpen(state, 1));
        var random = state.RandomState;
        Assert.Null(grades.OnOpen(state, 1));
        Assert.Equal(random, state.RandomState);
    }

    [Fact]
    public void TheSameSeedAndSaveGiveTheSameSchedule()
    {
        var first = HealthGrades.NewVehicle(rules, 1, 42);
        var second = HealthGrades.NewVehicle(rules, 1, 42);
        for (var day = 1; day < 1000; day++)
        {
            if (day % 5 == 0) continue;
            Assert.Equal(health.OnOpen(first, day), health.OnOpen(second, day));
            second = JsonConvert.DeserializeObject<HealthState>(JsonConvert.SerializeObject(second))!;
            Assert.Equal(first.RandomState, second.RandomState);
        }
    }

    [Fact]
    public void OnlyOldestOpenMarksDropOutAndRepeatedDaysReplacePoints()
    {
        var custom = new HealthGrades(new HealthRules(memoryDays: 2));
        var state = HealthGrades.NewVehicle(rules, 1, 42);
        custom.RecordDay(state, 1, false, true);
        custom.RecordDay(state, 15, false, false);
        Assert.Equal(10, custom.Points(state));
        custom.RecordDay(state, 15, true, true);
        Assert.Equal(11, custom.Points(state));
        custom.RecordDay(state, 30, true, false);
        Assert.Equal(new[] { 15, 30 }, state.Marks.Select(m => m.Day));
        Assert.Equal(4, custom.Points(state));
        custom.RecordDay(state, 1, false, true);
        Assert.Equal(new[] { 15, 30 }, state.Marks.Select(m => m.Day));
    }

    [Fact]
    public void ClosingForTwoWeeksDoesNotWashAwayFortyPoints()
    {
        var state = HealthGrades.NewVehicle(rules, 1, 42);
        state.Marks.Add(new HealthMark { Day = 1, Points = 40 });
        for (var seed = 0; seed < 1000; seed++)
        {
            state.RandomState = seed;
            state.LastDrawDay = null;
            var result = health.OnOpen(state, 16);
            if (!result.HasValue) continue;
            Assert.Equal(40, result.Value.Points);
            Assert.Equal(Grade.C, result.Value.Grade);
            Assert.Equal(40, health.Points(state));
            return;
        }
        Assert.Fail("No seed inspected on reopening.");
    }

    [Fact]
    public void OpeningTwiceInADayDrawsOnce()
    {
        var state = HealthGrades.NewVehicle(rules, 1, 42);
        health.OnOpen(state, 20);
        var after = state.RandomState;

        Assert.Null(health.OnOpen(state, 20));
        Assert.Equal(after, state.RandomState);
    }

    [Fact]
    public void APartTimeVehicleIsInspectedAsOftenInCalendarDays()
    {
        // Open two days a week (Mondays and Thursdays); an A should still average about 12 weeks.
        var gaps = new List<int>();
        for (var seed = 1; seed <= 300; seed++)
        {
            var state = HealthGrades.NewVehicle(rules, 1, seed);
            state.Grade = Grade.A;
            state.LastInspectionDay = 1;
            for (var day = 2; day < 2000; day++)
            {
                if (day % 7 != 1 && day % 7 != 4) continue;
                if (health.OnOpen(state, day).HasValue)
                {
                    gaps.Add(day - 1);
                    break;
                }
            }
        }

        Assert.InRange(gaps.Average(), 84 * 0.8, 84 * 1.2);
    }

    [Fact]
    public void PointsSumKeptMarksAndClampOverflow()
    {
        var state = new HealthState();
        health.RecordDay(state, 5, false, false);
        health.RecordDay(state, 2, true, true);
        Assert.Equal(7, health.Points(state));
        state.Marks[0].Points = int.MaxValue;
        Assert.Equal(int.MaxValue, health.Points(state));
    }

    [Theory]
    [InlineData("nullMarks")]
    [InlineData("nullMark")]
    [InlineData("duplicateDay")]
    [InlineData("negativePoints")]
    [InlineData("unknownGrade")]
    public void BadHealthStateFailsValidation(string problem)
    {
        var state = new HealthState();
        switch (problem)
        {
            case "nullMarks": state.Marks = null!; break;
            case "nullMark": state.Marks.Add(null!); break;
            case "duplicateDay": state.Marks.AddRange(new[] { new HealthMark { Day = 1 }, new HealthMark { Day = 1 } }); break;
            case "negativePoints": state.Marks.Add(new HealthMark { Points = -1 }); break;
            case "unknownGrade": state.Grade = (Grade)99; break;
        }

        Assert.Throws<JsonSerializationException>(() => health.Points(state));
    }

    [Fact]
    public void HealthStateKeepsEnumNamesAndUnknownFields()
    {
        var state = JsonConvert.DeserializeObject<HealthState>("{\"Grade\":\"B\",\"future\":7,\"Marks\":[{\"Day\":1,\"Points\":3,\"note\":true}]}")!;
        var root = JObject.Parse(JsonConvert.SerializeObject(state));

        Assert.Equal("B", root["Grade"]!.Value<string>());
        Assert.Equal(7, root["future"]!.Value<int>());
        Assert.True(root["Marks"]![0]!["note"]!.Value<bool>());
    }
}
