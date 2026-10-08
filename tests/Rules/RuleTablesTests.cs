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
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("queue.patienceMinutes", "0")]
    [InlineData("queue.patienceSpread", "1")]
    [InlineData("queue.rainPatience", "1.5")]
    [InlineData("queue.maxQueue", "2.5")]
    [InlineData("queue.maxQueue", null)]
    [InlineData("queue", "rename:queu")]
    [InlineData("economy.referenceShop.dailyRentCents", "-1")]
    [InlineData("economy.referenceShop.hoursOpen", "25")]
    [InlineData("economy.referenceShop.itemPriceCents", "11.65")]
    [InlineData("economy.referenceShop.workers", null)]
    [InlineData("economy.prices.multiples.cart", "-0.1")]
    [InlineData("economy.prices.roundToCents", "0")]
    [InlineData("economy.fuel.litresPerKm", "11")]
    [InlineData("economy.midGameBusinessShops", "0.5")]
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
    [InlineData("fleet.permitUnlocks[1].days", "10")]
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
