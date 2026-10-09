using System;
using System.Linq;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class WindowPlacesTests
{
    [Fact]
    public void WithoutThePlayerTheCrewSpreadAboutTheMiddle()
    {
        var (player, crew) = WindowPlaces.Offsets(3, player: false, spacing: 0.6);
        Assert.Null(player);
        Assert.Equal(new[] { -0.6, 0.0, 0.6 }, crew.Select(c => Math.Round(c, 6)));
        Assert.Equal(new[] { -0.3, 0.3 }, WindowPlaces.Offsets(2, false, 0.6).Crew.Select(c => Math.Round(c, 6)));
    }

    [Fact]
    public void ThePlayerKeepsTheMiddleAndTheCrewStandEitherSide()
    {
        var (player, crew) = WindowPlaces.Offsets(3, player: true, spacing: 0.6);
        Assert.Equal(0.0, player);
        Assert.Equal(new[] { 0.6, -0.6, 1.2 }, crew.Select(c => Math.Round(c, 6)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void NobodySharesAPlace(int count)
    {
        var (player, crew) = WindowPlaces.Offsets(count, player: true, spacing: 0.6);
        var all = crew.Append(player!.Value).Select(c => Math.Round(c, 6)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void BadInputIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPlaces.Offsets(-1, true, 0.6));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPlaces.Offsets(1, true, 0));
    }
}
