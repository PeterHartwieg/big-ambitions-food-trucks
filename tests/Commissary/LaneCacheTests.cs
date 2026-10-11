using System;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class LaneCacheTests
{
    private sealed class Lane
    {
        public bool Alive = true;
        public bool Active = true;
    }

    [Fact]
    public void NoLanesAtFirstThenLanesAppear()
    {
        var cache = new LaneCache<Lane>();
        var found = Array.Empty<Lane>();
        bool Ready(double now) => cache.Ready(now, () => found, l => l.Alive, l => l.Active);
        Assert.False(Ready(0));
        found = new[] { new Lane() };
        // Not looked up again before the interval...
        Assert.False(Ready(1));
        Assert.Equal(1, cache.Lookups);
        // ...then found.
        Assert.True(Ready(LaneCache<Lane>.RefreshSeconds));
        Assert.Equal(2, cache.Lookups);
    }

    [Fact]
    public void InactiveLanesAreLookedUpAgainUntilOneIsActive()
    {
        var cache = new LaneCache<Lane>();
        var lane = new Lane { Active = false };
        var found = new[] { lane };
        Assert.False(cache.Ready(0, () => found, l => l.Alive, l => l.Active));
        lane.Active = true;
        Assert.True(cache.Ready(0.5, () => found, l => l.Alive, l => l.Active));
        Assert.True(cache.Ready(100, () => found, l => l.Alive, l => l.Active));
        Assert.Equal(1, cache.Lookups);
    }

    [Fact]
    public void ALaneGoneIsFoundAgainAtOnce()
    {
        var cache = new LaneCache<Lane>();
        var first = new Lane();
        var found = new[] { first };
        Assert.True(cache.Ready(0, () => found, l => l.Alive, l => l.Active));
        first.Alive = false;
        found = new[] { new Lane() };
        Assert.True(cache.Ready(0.1, () => found, l => l.Alive, l => l.Active));
        Assert.Equal(2, cache.Lookups);
    }
}
