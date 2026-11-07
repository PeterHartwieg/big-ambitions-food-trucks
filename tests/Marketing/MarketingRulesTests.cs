using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Marketing;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Marketing;

public class MarketingRulesTests
{
    [Fact]
    public void ShippedRulesAndDefaultsMatchTheBrief()
    {
        foreach (var rules in new[] { new MarketingRules(), ShippedData.Rules.Marketing })
        {
            Assert.Equal(new[] { "none", "flyers", "local", "citywide" }, rules.Levels.Select(l => l.Id));
            Assert.Equal(new[] { 1.0, 1.10, 1.20, 1.30 }, rules.Levels.Select(l => l.Footfall));
            Assert.Equal(new long[] { 0, 2000, 4500, 8000 }, rules.Levels.Select(l => l.CostPerDayCents["cart"]));
            Assert.Equal(new long[] { 0, 6000, 14000, 24000 }, rules.Levels.Select(l => l.CostPerDayCents["truck"]));
            Assert.True(rules.Levels[0].IsNone);
            Assert.Same(rules.Levels[2], rules.Level("local"));
            Assert.Null(rules.Level("billboards"));
            Assert.Null(rules.Level(null));
        }
    }

    [Fact]
    public void CustomRulesAreReadExactly()
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["marketing"] = JObject.Parse("{\"levels\":[{\"id\":\"none\",\"footfall\":1,\"costPerDayCents\":{\"van\":0}}," +
                                          "{\"id\":\"radio\",\"footfall\":3,\"costPerDayCents\":{\"van\":9223372036854775807}}]}");
        var rules = RuleTables.Parse(root.ToString()).Marketing;
        Assert.Equal(2, rules.Levels.Count);
        Assert.Equal(3.0, rules.Level("radio")!.Footfall);
        Assert.Equal(long.MaxValue, rules.Level("radio")!.CostPerDayCents["van"]);
    }

    [Theory]
    [InlineData("levels", "[]")]
    [InlineData("levels[0].id", "\"flyers\"")]
    [InlineData("levels[0].footfall", "1.1")]
    [InlineData("levels[0].costPerDayCents.cart", "100")]
    [InlineData("levels[1].id", "\"none\"")]
    [InlineData("levels[2].id", "\"flyers\"")]
    [InlineData("levels[1].id", "\"\"")]
    [InlineData("levels[1].id", "\" \"")]
    [InlineData("levels[1].footfall", "0.9")]
    [InlineData("levels[1].footfall", "3.1")]
    [InlineData("levels[1].footfall", "\"1.15\"")]
    [InlineData("levels[1].costPerDayCents", "{}")]
    [InlineData("levels[1].costPerDayCents.cart", "-1")]
    [InlineData("levels[1].costPerDayCents.cart", "30.5")]
    [InlineData("levels[1].costPerDayCents.cart", "9223372036854775808")]
    [InlineData("levels[1]", "\"flyers\"")]
    public void BadRulesNameTheJsonPath(string path, string value)
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        var token = root.SelectToken("marketing." + path)!;
        var expected = token.Path;
        token.Replace(JToken.Parse(value));
        var error = Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString()));
        Assert.StartsWith("rules.json: ", error.Message);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void UnknownKeysAndMissingBlockFailButUnderscoreNotesAreAllowed()
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["marketing"]!["_note"] = "explanation";
        root["marketing"]!["levels"]![1]!["_note"] = "explanation";
        RuleTables.Parse(root.ToString());
        root["marketing"]!["levels"]![1]!["typo"] = 1;
        Assert.Contains("marketing.levels[1].typo", Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString())).Message);
        root.Remove("marketing");
        Assert.Contains("marketing", Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString())).Message);
    }

    [Fact]
    public void InvalidConstructorInputsAreRejected()
    {
        var costs = new Dictionary<string, long> { ["cart"] = 0 };
        var none = new MarketingLevel("none", 1.0, costs);
        var flyers = new MarketingLevel("flyers", 1.15, new Dictionary<string, long> { ["cart"] = 3000 });
        Assert.Throws<ArgumentException>(() => new MarketingLevel(" ", 1.0, costs));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketingLevel("x", 0.99, costs));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketingLevel("x", 3.01, costs));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketingLevel("x", double.NaN, costs));
        Assert.Throws<ArgumentException>(() => new MarketingLevel("x", 1.0, new Dictionary<string, long>()));
        Assert.Throws<ArgumentException>(() => new MarketingLevel("x", 1.0, new Dictionary<string, long> { ["cart"] = -1 }));
        Assert.Throws<ArgumentException>(() => new MarketingRules(Array.Empty<MarketingLevel>()));
        Assert.Throws<ArgumentException>(() => new MarketingRules(new[] { flyers }));
        Assert.Throws<ArgumentException>(() => new MarketingRules(new[] { new MarketingLevel("none", 1.1, costs) }));
        Assert.Throws<ArgumentException>(() => new MarketingRules(new[] { new MarketingLevel("none", 1.0, new Dictionary<string, long> { ["cart"] = 1 }) }));
        Assert.Throws<ArgumentException>(() => new MarketingRules(new[] { none, flyers, flyers }));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)flyers.CostPerDayCents).Clear());
        costs["cart"] = 5;
        Assert.Equal(0, none.CostPerDayCents["cart"]);
    }
}
