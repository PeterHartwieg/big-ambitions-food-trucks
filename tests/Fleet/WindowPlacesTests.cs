using System;
using System.Linq;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class WindowPlacesTests
{
    // The truck's hatch: opening z -2.00 to -0.20 (art/README.md), less a quarter metre for a body at each end.
    private const double HalfWidth = 0.65;

    private static double?[] Rounded(double?[] places) => places.Select(p => p.HasValue ? Math.Round(p.Value, 6) : (double?)null).ToArray();

    [Fact]
    public void WithoutThePlayerTheCrewSpreadAboutTheMiddle()
    {
        var (player, crew) = WindowPlaces.Offsets(3, player: false, spacing: 0.6, HalfWidth);
        Assert.Null(player);
        Assert.Equal(new double?[] { -0.6, 0.0, 0.6 }, Rounded(crew));
        Assert.Equal(new double?[] { -0.3, 0.3 }, Rounded(WindowPlaces.Offsets(2, false, 0.6, HalfWidth).Crew));
    }

    [Fact]
    public void ThePlayerKeepsTheMiddleAndTheCrewStandEitherSide()
    {
        var (player, crew) = WindowPlaces.Offsets(2, player: true, spacing: 0.45, HalfWidth);
        Assert.Equal(0.0, player);
        Assert.Equal(new double?[] { 0.45, -0.45 }, Rounded(crew));
    }

    [Fact]
    public void CrewWhoDontFitTheOpeningWaitInside()
    {
        // With the player in the middle, a third crew member would stand at +0.90, past the opening.
        Assert.Equal(new double?[] { 0.45, -0.45, null }, Rounded(WindowPlaces.Offsets(3, true, 0.45, HalfWidth).Crew));
        Assert.Equal(new double?[] { -0.6, 0.0, 0.6, null }, Rounded(WindowPlaces.Offsets(4, false, 0.6, HalfWidth).Crew));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(6, false)]
    public void EveryPlaceIsInsideTheOpeningAndNobodySharesOne(int count, bool withPlayer)
    {
        var (player, crew) = WindowPlaces.Offsets(count, withPlayer, spacing: withPlayer ? 0.45 : 0.6, HalfWidth);
        var all = crew.Where(c => c.HasValue).Select(c => c!.Value).Concat(player.HasValue ? new[] { player.Value } : Array.Empty<double>())
            .Select(c => Math.Round(c, 6)).ToList();
        Assert.All(all, p => Assert.InRange(Math.Abs(p), 0, HalfWidth));
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void BadInputIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPlaces.Offsets(-1, true, 0.6, HalfWidth));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPlaces.Offsets(1, true, 0, HalfWidth));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPlaces.Offsets(1, true, 0.6, -1));
    }
}
