using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class AreaMixTests
{
    [Fact]
    public void LegacyApiCarriesPresencesWithoutNormalizingAndLabelsTheReadout()
    {
        var mix = AreaMix.FromWeights(new Dictionary<string, double> { ["office"] = 0.7, ["nightlife"] = 0.9 });
        Assert.Equal(0.7, mix.PresenceOf("office"));
        Assert.Equal(0.9, mix.PresenceOf("nightlife"));
        Assert.Equal(mix.PresenceOf("office"), mix.ShareOf("office"));
        Assert.Same(mix.Presences, mix.Shares);
        Assert.Contains("nightlife presence", mix.ToString());
        Assert.Contains("office presence", mix.ToString());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PresencesMustBeFiniteAndWithinZeroToOne(double value)
    {
        Assert.Throws<ArgumentException>(() => AreaMix.FromWeights(new Dictionary<string, double> { ["office"] = value }));
    }
}
