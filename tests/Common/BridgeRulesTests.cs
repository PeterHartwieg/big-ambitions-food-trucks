using System.Collections.Generic;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Common;

public class BridgeRulesTests
{
    // Live session 4: x -1490, z -1150 has the elevated highway at 5.06 over the street at 0.05.
    private static readonly List<(double Y, bool Walkable)> UnderHighway = new() { (5.06, false), (0.05, true) };

    [Fact]
    public void Teleport_prefers_the_walkable_street_under_an_elevated_road()
    {
        var (index, why) = GroundPick.Choose(UnderHighway, referenceY: 0.1, explicitY: null);
        Assert.Equal(1, index);
        Assert.Contains("walkable", why);
    }

    [Fact]
    public void Teleport_takes_the_walkable_surface_nearest_the_movers_height()
    {
        var stacked = new List<(double Y, bool Walkable)> { (12.0, true), (5.0, false), (0.0, true) };
        Assert.Equal(0, GroundPick.Choose(stacked, referenceY: 11.5, explicitY: null).Index);
        Assert.Equal(2, GroundPick.Choose(stacked, referenceY: 4.9, explicitY: null).Index);
    }

    [Fact]
    public void Teleport_without_walkable_ground_takes_the_nearest_height()
    {
        var road = new List<(double Y, bool Walkable)> { (5.06, false), (0.05, false) };
        var (index, why) = GroundPick.Choose(road, referenceY: 4.0, explicitY: null);
        Assert.Equal(0, index);
        Assert.Contains("none walkable", why);
    }

    [Fact]
    public void Teleport_with_an_explicit_y_takes_the_surface_nearest_it()
    {
        var (index, why) = GroundPick.Choose(UnderHighway, referenceY: 0.1, explicitY: 5);
        Assert.Equal(0, index);
        Assert.Contains("y asked for", why);
    }

    [Fact]
    public void Needs_are_topped_up_below_forty()
    {
        Assert.False(NeedsTopUp.Due(40, 40));
        Assert.True(NeedsTopUp.Due(39.9, 100));
        Assert.True(NeedsTopUp.Due(100, 10));
        Assert.True(NeedsTopUp.Due(-20, 100));
    }

    [Fact]
    public void Shot_keeps_its_distance_on_a_clear_line()
    {
        Assert.Equal(6, ShotClearance.Fit(6, null, 2));
        Assert.Equal(6, ShotClearance.Fit(6, 6.5, 2));
    }

    [Fact]
    public void Shot_pulls_in_before_a_facade()
    {
        // Live session 4, garment-district: a 4 m pavement put the 6 m camera inside the facade.
        Assert.Equal(3.6, ShotClearance.Fit(6, 4.0, 2)!.Value, 6);
    }

    [Fact]
    public void Shot_line_blocked_too_near_tries_another_way()
    {
        Assert.Null(ShotClearance.Fit(6, 2.0, 2));
    }

    [Fact]
    public void Footprints_across_stalls_overlap()
    {
        // Live session 4: a truck along the lane (heading 0) over a van in a perpendicular stall beside it.
        var truck = new FlatBox(0, 0, 0, 1, 1.1, 2.9);
        var van = new FlatBox(0.5, 1.5, 1, 0, 1.0, 2.5);
        Assert.True(Footprint.Overlap(truck, van));
    }

    [Fact]
    public void Footprints_side_by_side_do_not_overlap()
    {
        var truck = new FlatBox(0, 0, 0, 1, 1.1, 2.9);
        var neighbour = new FlatBox(2.5, 0, 0, 1, 1.0, 2.5);
        Assert.False(Footprint.Overlap(truck, neighbour));
        Assert.True(Footprint.Overlap(truck, neighbour, margin: 0.25));
    }

    [Fact]
    public void Footprints_turned_45_degrees_use_their_real_corners()
    {
        // Two 2 x 2 squares 2.3 m apart along x: apart as squares, touching once one is turned 45 degrees.
        var a = new FlatBox(0, 0, 0, 1, 1, 1);
        Assert.False(Footprint.Overlap(a, new FlatBox(2.3, 0, 0, 1, 1, 1)));
        Assert.True(Footprint.Overlap(a, new FlatBox(2.3, 0, 1, 1, 1, 1)));
    }
}
