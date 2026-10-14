using System;
using System.Collections.Generic;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissaryRulesTests
{
    [Fact]
    public void ShippedRulesAndDefaultsMatchTheBrief()
    {
        foreach (var rules in new[] { new CommissaryRules(), ShippedData.Rules.Commissary })
        {
            Assert.Equal(30000, rules.RentPerMonthCents["cart"]);
            Assert.Equal(125000, rules.RentPerMonthCents["truck"]);
            Assert.Equal(986, rules.DailyRentCents("cart"));
            Assert.Equal(4110, rules.DailyRentCents("truck"));
            Assert.Equal(25000, rules.OvernightFineCents);
            Assert.Equal(240, rules.NightCheckMinute);
            Assert.Equal(480, rules.DeliveryMinute);
            Assert.Equal(25000, rules.DeliveryFeeCents);
            Assert.Equal(15, rules.BayRadiusMetres);
            Assert.Equal(10, rules.FoodBoxesPerWeek);
        }
    }

    [Fact]
    public void CustomRulesAreReadExactlyAndCollectionsAreCopied()
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["commissary"] = JObject.Parse("{\"rentPerMonthCents\":{\"van\":9223372036854775807},\"overnightFineCents\":9223372036854775807,\"nightCheckMinute\":200,\"deliveryMinute\":500,\"deliveryFeeCents\":9223372036854775807,\"bayRadiusMetres\":20,\"foodBoxesPerWeek\":{\"default\":3,\"items\":{\"hotdog\":5}}}");
        var rules = RuleTables.Parse(root.ToString()).Commissary;
        Assert.Equal(long.MaxValue, rules.RentPerMonthCents["van"]);
        Assert.Equal(long.MaxValue, rules.OvernightFineCents);
        Assert.Equal(200, rules.NightCheckMinute);
        Assert.Equal(500, rules.DeliveryMinute);
        Assert.Equal(long.MaxValue, rules.DeliveryFeeCents);
        Assert.Equal(20, rules.BayRadiusMetres);
        Assert.Equal(3, rules.FoodBoxesPerWeek);
        Assert.Equal(5, rules.FoodBoxLimit("hotdog"));
        Assert.Equal(3, rules.FoodBoxLimit("coffee"));
        Assert.True(rules.DailyRentCents("van") > 0);
        var rents = new Dictionary<string, long> { ["cart"] = 30000 };
        rules = new CommissaryRules(rents);
        rents.Clear();
        Assert.Single(rules.RentPerMonthCents);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)rules.RentPerMonthCents).Clear());
    }

    [Theory]
    [InlineData("rentPerMonthCents.cart", "-1")]
    [InlineData("rentPerMonthCents.cart", "1.5")]
    [InlineData("rentPerMonthCents.cart", "9223372036854775808")]
    [InlineData("rentPerMonthCents", "{}")]
    [InlineData("overnightFineCents", "-1")]
    [InlineData("nightCheckMinute", "1440")]
    [InlineData("nightCheckMinute", "-1")]
    [InlineData("deliveryMinute", "480.5")]
    [InlineData("deliveryMinute", "null")]
    [InlineData("deliveryFeeCents", "-1")]
    [InlineData("deliveryFeeCents", "250.5")]
    [InlineData("deliveryFeeCents", "null")]
    [InlineData("bayRadiusMetres", "0")]
    [InlineData("bayRadiusMetres", "-1")]
    public void BadRulesNameTheJsonPath(string path, string value)
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root.SelectToken("commissary." + path)!.Replace(JToken.Parse(value));
        var error = Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString()));
        Assert.Contains("commissary." + path, error.Message);
    }

    [Fact]
    public void UnknownKeysAndMissingBlockFailButUnderscoreNotesAreAllowed()
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["commissary"]!["_note"] = "explanation";
        RuleTables.Parse(root.ToString());
        root["commissary"]!["typo"] = 1;
        Assert.Contains("commissary.typo", Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString())).Message);
        root.Remove("commissary");
        Assert.Contains("commissary", Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString())).Message);
    }

    [Fact]
    public void ANegativeDeliveryFeeIsRejectedInDirectConstruction() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(deliveryFeeCents: -1));

    [Fact]
    public void OtherInvalidConstructorInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CommissaryRules(new Dictionary<string, long>()));
        Assert.Throws<ArgumentException>(() => new CommissaryRules(new Dictionary<string, long> { ["cart"] = -1 }));
        Assert.Throws<ArgumentException>(() => new CommissaryRules(new Dictionary<string, long> { [" "] = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(overnightFineCents: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(nightCheckMinute: 1440));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(deliveryMinute: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(bayRadiusMetres: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(bayRadiusMetres: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommissaryRules(bayRadiusMetres: 0));
    }
}
