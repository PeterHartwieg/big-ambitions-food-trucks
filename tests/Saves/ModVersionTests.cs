using System;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class ModVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("99.0.0")]
    [InlineData("0.0.1-spike")]
    [InlineData("1.0.0-alpha.1+build.001")]
    [InlineData("1.0.0-0a+001")]
    [InlineData("999999999999999999999999999999.0.0")]
    public void IsValidAcceptsSemVer(string version)
    {
        Assert.True(ModVersion.IsValid(version));
    }

    [Theory]
    [InlineData("0.0.1-spike", "0.0.1", "0.0.2", "0.1.0", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0")]
    public void VersionsFollowSemVerPrecedence(params string[] versions)
    {
        for (var i = 0; i < versions.Length; i++)
        {
            for (var j = 0; j < versions.Length; j++)
            {
                Assert.Equal(Math.Sign(i.CompareTo(j)), Math.Sign(ModVersion.Compare(versions[i], versions[j])));
                Assert.Equal(i > j, ModVersion.IsNewerThan(versions[i], versions[j]));
            }
        }
    }

    [Theory]
    [InlineData("1.0.0+build", "1.0.0")]
    [InlineData("1.0.0+001", "1.0.0+other.build-2")]
    [InlineData("1.0.0-alpha.1+build", "1.0.0-alpha.1")]
    [InlineData("1.0.0-0a+001", "1.0.0-0a")]
    public void BuildMetadataDoesNotAffectPrecedence(string a, string b)
    {
        Assert.Equal(0, ModVersion.Compare(a, b));
        Assert.Equal(0, ModVersion.Compare(b, a));
        Assert.False(ModVersion.IsNewerThan(a, b));
        Assert.False(ModVersion.IsNewerThan(b, a));
    }

    [Theory]
    [InlineData("1.0.0-9", "1.0.0-10")]
    [InlineData("1.0.0-999", "1.0.0-A")]
    [InlineData("1.0.0-Z", "1.0.0-a")]
    [InlineData("1.0.0-0", "1.0.0-0.0")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.a")]
    [InlineData("1.0.0-alpha+z", "1.0.0-beta+a")]
    [InlineData("1.9.99", "1.10.0")]
    [InlineData("9.99.99", "10.0.0")]
    [InlineData("999999999999999999999999999999.0.0", "1000000000000000000000000000000.0.0")]
    [InlineData("1.999999999999999999999999999999.0", "1.1000000000000000000000000000000.0")]
    [InlineData("1.0.999999999999999999999999999999", "1.0.1000000000000000000000000000000")]
    [InlineData("1.0.0-999999999999999999999999999999", "1.0.0-1000000000000000000000000000000")]
    public void IdentifiersUseNumericAndOrdinalOrdering(string lower, string higher)
    {
        Assert.True(ModVersion.Compare(lower, higher) < 0);
        Assert.True(ModVersion.Compare(higher, lower) > 0);
        Assert.True(ModVersion.IsNewerThan(higher, lower));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("99.0")]
    [InlineData("1.0.0.0")]
    [InlineData("v1.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.01.0")]
    [InlineData("1.0.01")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-alpha.01")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0-alpha_beta")]
    [InlineData("1.0.0+build..1")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+build+other")]
    [InlineData("1.0.0-é")]
    [InlineData("1.0.0-١")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0\n")]
    public void InvalidVersionsAreEqualAndLowerThanEveryValidVersion(string? invalid)
    {
        Assert.False(ModVersion.IsValid(invalid));
        Assert.Equal(0, ModVersion.Compare(invalid, null));
        Assert.Equal(0, ModVersion.Compare(null, invalid));
        Assert.Equal(0, ModVersion.Compare(invalid, ""));
        Assert.Equal(0, ModVersion.Compare("garbage", invalid));
        Assert.True(ModVersion.Compare(invalid, "0.0.0-0") < 0);
        Assert.True(ModVersion.Compare("0.0.0-0", invalid) > 0);
        Assert.False(ModVersion.IsNewerThan(invalid, "0.0.0-0"));
    }
}
