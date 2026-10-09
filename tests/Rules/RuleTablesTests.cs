using System;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Rules;

public class RuleTablesTests
{
    [Fact]
    public void TheShippedFileLoads()
    {
        var queue = ShippedData.Rules.Queue;

        Assert.Equal(20, queue.PatienceMinutes);
        Assert.True(queue.RainPatience < 1);
        Assert.Equal(7, queue.WalkInMinMinutes);
        Assert.Equal(14, queue.WalkInMaxMinutes);
        var service = ShippedData.Rules.Service;
        Assert.Equal(50, service.BaseSkill);
        Assert.Equal(1.5, service.SlowestTimeFactor);
        Assert.Equal(1.3, service.FastestSpeed);
        Assert.Equal(25, service.ComplainBelowSkill);
        Assert.Equal(75, service.PleasedFromSkill);
        Assert.Equal(6, service.QueueLimit("cart", queue.MaxQueue));
        Assert.Equal(10, service.QueueLimit("truck", queue.MaxQueue));
    }

    [Fact]
    public void SkillScalesSpeedAroundTheBase()
    {
        var service = ShippedData.Rules.Service;

        Assert.Equal(1.0, service.Speed(50));
        Assert.Equal(1.0 / 1.5, service.Speed(0));
        Assert.Equal(1.3, service.Speed(100));
        Assert.Equal((1.0 / 1.5 + 1.0) / 2, service.Speed(25), 12);
        Assert.Equal(1.15, service.Speed(75), 12);
        Assert.Equal(service.Speed(0), service.Speed(-10));
        Assert.Equal(service.Speed(100), service.Speed(150));
        Assert.Equal(service.Speed(0), service.Speed(double.NegativeInfinity));
        Assert.Equal(service.Speed(100), service.Speed(double.PositiveInfinity));
        Assert.Equal(1.0, service.Speed(double.NaN));
    }

    [Theory]
    [InlineData(80, 50, 40)]
    [InlineData(50, 100, 50)]
    [InlineData(150, 50, 50)]
    [InlineData(50, 150, 50)]
    [InlineData(-10, 100, 0)]
    [InlineData(100, -10, 0)]
    [InlineData(double.NaN, 100, 0)]
    [InlineData(100, double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 100, 100)]
    [InlineData(100, double.PositiveInfinity, 100)]
    [InlineData(double.NegativeInfinity, 100, 0)]
    [InlineData(100, double.NegativeInfinity, 0)]
    public void EffectiveSkillCombinesClampedSkillAndSatisfaction(double skill, double satisfaction, double expected)
    {
        Assert.Equal(expected, ServiceRules.EffectiveSkill(skill, satisfaction));
    }

    [Theory]
    [InlineData(0, ServiceMood.Complaining)]
    [InlineData(24.9, ServiceMood.Complaining)]
    [InlineData(25, ServiceMood.Neutral)]
    [InlineData(50, ServiceMood.Neutral)]
    [InlineData(74.9, ServiceMood.Neutral)]
    [InlineData(75, ServiceMood.Pleased)]
    [InlineData(100, ServiceMood.Pleased)]
    public void MoodUsesTheSkillThresholds(double skill, ServiceMood expected)
    {
        Assert.Equal(expected, ShippedData.Rules.Service.Mood(skill));
    }

    [Fact]
    public void VehicleLimitsAreCappedAndUnknownKindsUseTheGlobalLimit()
    {
        var service = ShippedData.Rules.Service;

        Assert.Equal(4, service.QueueLimit("cart", 4));
        Assert.Equal(8, service.QueueLimit("truck", 8));
        Assert.Equal(12, service.QueueLimit("unknown", 12));
    }

    [Fact]
    public void ServiceTuningComesFromTheFile()
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["service"] = JObject.Parse("{\"baseSkill\":40,\"slowestTimeFactor\":2,\"fastestSpeed\":2,\"complainBelowSkill\":10,\"pleasedFromSkill\":90,\"queueLimits\":{\"van\":8}}");

        var service = RuleTables.Parse(root.ToString()).Service;

        Assert.Equal(0.5, service.Speed(0));
        Assert.Equal(1.0, service.Speed(40));
        Assert.Equal(1.5, service.Speed(70));
        Assert.Equal(2.0, service.Speed(100));
        Assert.Equal(ServiceMood.Neutral, service.Mood(25));
        Assert.Equal(ServiceMood.Neutral, service.Mood(75));
        Assert.Equal(ServiceMood.Pleased, service.Mood(90));
        Assert.Equal(8, service.QueueLimit("van", 12));
        Assert.Equal(12, service.QueueLimit("cart", 12));
    }

    [Fact]
    public void EmptyQueueLimitsUseTheGlobalLimit()
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["service"]!["queueLimits"] = new JObject();

        Assert.Equal(12, RuleTables.Parse(root.ToString()).Service.QueueLimit("cart", 12));
    }

    [Fact]
    public void ServiceDefaultsMatchTheShippedRules()
    {
        var service = new ServiceRules();
        var shipped = ShippedData.Rules.Service;

        Assert.Equal(shipped.BaseSkill, service.BaseSkill);
        Assert.Equal(shipped.SlowestTimeFactor, service.SlowestTimeFactor);
        Assert.Equal(shipped.FastestSpeed, service.FastestSpeed);
        Assert.Equal(shipped.ComplainBelowSkill, service.ComplainBelowSkill);
        Assert.Equal(shipped.PleasedFromSkill, service.PleasedFromSkill);
        Assert.Equal(shipped.QueueLimits, service.QueueLimits);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("queue.patienceMinutes", "0")]
    [InlineData("queue.patienceSpread", "1")]
    [InlineData("queue.rainPatience", "1.5")]
    [InlineData("queue.maxQueue", "2.5")]
    [InlineData("queue.maxQueue", null)]
    [InlineData("queue.walkInMinutes", null)]
    [InlineData("queue.walkInMinutes", "[14, 7]")]
    [InlineData("queue.walkInMinutes", "[7]")]
    [InlineData("queue.walkInMinutes", "[-1, 7]")]
    [InlineData("queue.walkInMinutes", "[7, 61]")]
    [InlineData("queue.walkInMinutes", "[7, 13.5]")]
    [InlineData("queue", "rename:queu")]
    [InlineData("service", null)]
    [InlineData("service", "rename:servce")]
    [InlineData("service.baseSkill", "0")]
    [InlineData("service.baseSkill", "100")]
    [InlineData("service.baseSkill", "\"50\"")]
    [InlineData("service.baseSkill", "rename:baseSkil")]
    [InlineData("service.slowestTimeFactor", "0.9")]
    [InlineData("service.slowestTimeFactor", "5.1")]
    [InlineData("service.fastestSpeed", "0.9")]
    [InlineData("service.fastestSpeed", "3.1")]
    [InlineData("service.complainBelowSkill", "-1")]
    [InlineData("service.complainBelowSkill", "101")]
    [InlineData("service.pleasedFromSkill", "-1")]
    [InlineData("service.pleasedFromSkill", "101")]
    [InlineData("service.pleasedFromSkill", "25")]
    [InlineData("service.pleasedFromSkill", "24")]
    [InlineData("service.queueLimits", "[]")]
    [InlineData("service.queueLimits", null)]
    [InlineData("service.queueLimits.cart", "0")]
    [InlineData("service.queueLimits.cart", "101")]
    [InlineData("service.queueLimits.cart", "6.5")]
    [InlineData("service.queueLimits.cart", "\"6\"")]
    [InlineData("health.memoryDays", "0")]
    [InlineData("health.grades[0].grade", "\"B\"")]
    [InlineData("health.grades[1].maxPoints", "10")]
    [InlineData("health.grades[2]", "{\"grade\":\"C\",\"maxPoints\":40,\"demand\":0.75,\"inspectionWeeks\":4}")]
    [InlineData("health.minimumGapDays", "366")]
    [InlineData("regulars.maxBonus", "6")]
    [InlineData("regulars.minOpenMinutes", "-1")]
    [InlineData("competition.nearbyMetres", "0")]
    [InlineData("fleet.licenceDays", "0")]
    [InlineData("fleet.renewWindowDays", "61")]
    [InlineData("fleet.licenceFee", "-1")]
    [InlineData("fleet.travel.pushMetresPerSecond", "0")]
    [InlineData("fleet.schedules.maxStops", "0")]
    [InlineData("catering.leadDays", "[5, 2]")]
    [InlineData("catering.reputation.min", "1.5")]
    [InlineData("catering.kinds[0].start[0]", "\"25:00\"")]
    [InlineData("catering.kinds[1].guests", "[0, 10]")]
    [InlineData("catering.kinds[2].days[0]", "\"fry\"")]
    [InlineData("catering.kinds[0].categories", "{}")]
    [InlineData("catering.kinds", "[]")]
    public void BadFieldsNameTheirPath(string path, string? change)
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        var token = root.SelectToken(path)!;
        var expectedPath = token.Path;
        if (change == null) ((JProperty)token.Parent!).Remove();
        else if (change.StartsWith("rename:", StringComparison.Ordinal))
        {
            var property = (JProperty)token.Parent!;
            var newName = change.Substring("rename:".Length);
            property.Replace(new JProperty(newName, token.DeepClone()));
            expectedPath = newName;
        }
        else token.Replace(JToken.Parse(change));

        var error = Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString()));

        Assert.StartsWith("rules.json: ", error.Message);
        Assert.Contains(expectedPath, error.Message);
    }
}
