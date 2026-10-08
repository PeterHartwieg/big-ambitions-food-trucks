using System;
using System.Collections.Generic;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class ReputationRulesTests
{
    [Fact]
    public void DefaultsMatchTheDesign()
    {
        var health = new HealthRules();
        Assert.Equal(14, health.MemoryDays);
        Assert.Equal(3, health.UncleanedDayPoints);
        Assert.Equal(4, health.StaleStockPoints);
        Assert.Equal(2, health.FirstInspectionWeeks);
        Assert.Equal(7, health.MinimumGapDays);
        Assert.Equal(7, health.DaysPerWeek);
        Assert.Collection(health.Grades,
            a => { Assert.Equal(Grade.A, a.Grade); Assert.Equal(13, a.MaxPoints); Assert.Equal(1, a.Demand); Assert.Equal(12, a.InspectionWeeks); },
            b => { Assert.Equal(Grade.B, b.Grade); Assert.Equal(27, b.MaxPoints); Assert.Equal(0.9, b.Demand); Assert.Equal(6, b.InspectionWeeks); },
            c => { Assert.Equal(Grade.C, c.Grade); Assert.Null(c.MaxPoints); Assert.Equal(0.75, c.Demand); Assert.Equal(4, c.InspectionWeeks); });
        var regulars = new RegularsRules();
        Assert.Equal(0.05, regulars.GainPerVisit);
        Assert.Equal(0.30, regulars.MaxBonus);
        Assert.Equal(0.075, regulars.MissPenalty);
        Assert.Equal(60, regulars.MinOpenMinutes);
        var competition = new CompetitionRules();
        Assert.Equal(80, competition.NearbyMetres);
        Assert.Equal(0.10, competition.FootfallPerNeighbour);
        Assert.Equal(0.30, competition.MaxFootfallBonus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void MemoryDaysMustBeInRange(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthRules(memoryDays: days));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void PointPenaltiesMustBeInRange(int points)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthRules(uncleanedDayPoints: points));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthRules(staleStockPoints: points));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void MinimumGapDaysMustBeInRange(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthRules(minimumGapDays: days));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("366")]
    [InlineData("0.95")]
    public void InvalidMinimumGapNamesItsJsonPath(string value)
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["health"]!["minimumGapDays"] = JToken.Parse(value);
        var error = Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString()));
        Assert.Contains("health.minimumGapDays", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(521)]
    public void InspectionWeeksMustBeInRange(int weeks)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthRules(firstInspectionWeeks: weeks));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradeBand(inspectionWeeks: weeks));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2.01)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void GradeDemandMustBePositiveAndAtMostTwo(double demand)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradeBand(demand: demand));
    }

    [Fact]
    public void GradeBandsMustBeCompleteOrderedAndRising()
    {
        var a = new GradeBand();
        var b = new GradeBand(Grade.B, 27, 0.9, 6);
        var c = new GradeBand(Grade.C, null, 0.75, 4);

        Assert.Throws<ArgumentException>(() => new HealthRules(grades: new[] { a, b }));
        Assert.Throws<ArgumentException>(() => new HealthRules(grades: new[] { b, a, c }));
        Assert.Throws<ArgumentException>(() => new HealthRules(grades: new[] { a, new GradeBand(Grade.B, 13), c }));
        Assert.Throws<ArgumentException>(() => new HealthRules(grades: new[] { new GradeBand(Grade.A, null), b, c }));
        Assert.Throws<ArgumentException>(() => new HealthRules(grades: new[] { a, b, new GradeBand(Grade.C, 30) }));
        Assert.Throws<ArgumentException>(() => new HealthRules(grades: new[] { a, null!, c }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradeBand(Grade.Ungraded));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradeBand(maxPoints: -1));
    }

    [Fact]
    public void RulesCopyTheGradeListSoTheCallerCannotChangeIt()
    {
        var bands = new List<GradeBand>(new HealthRules().Grades);
        var rules = new HealthRules(grades: bands);
        bands.Clear();

        Assert.Equal(3, rules.Grades.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<GradeBand>)rules.Grades).Clear());
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(5.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RegularsFactorsMustBeFiniteAndInRange(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegularsRules(gainPerVisit: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegularsRules(maxBonus: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegularsRules(missPenalty: value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NeighbourRadiusMustBeFiniteAndInRange(double metres)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompetitionRules(nearbyMetres: metres));
    }

    [Fact]
    public void BoundaryValuesAreAllowed()
    {
        var health = new HealthRules(memoryDays: 365, uncleanedDayPoints: 0, staleStockPoints: 100,
            firstInspectionWeeks: 520, minimumGapDays: 365, daysPerWeek: 1);
        var regulars = new RegularsRules(0, 5, 5, 0);
        var competition = new CompetitionRules(10000, 0, 5);

        Assert.Equal(365, health.MemoryDays);
        Assert.Equal(5, regulars.MaxBonus);
        Assert.Equal(10000, competition.NearbyMetres);
        Assert.Equal(2, new GradeBand(demand: 2).Demand);
    }
}
