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
    public void Explicit_y_without_walkable_ground_near_it_is_refused_for_the_player()
    {
        // Live session 5, row 40: y 5.06 over a street at 0.20 put the player on the street.
        var street = new List<(double Y, bool Walkable)> { (0.20, true) };
        var (index, why) = GroundPick.Choose(street, referenceY: 0.1, explicitY: 5.06, walkableOnly: true);
        Assert.Equal(-1, index);
        Assert.Contains("no walkable surface within 1.5 m", why);
        Assert.Equal(-1, GroundPick.Choose(UnderHighway, 0.1, 5.06, walkableOnly: true).Index);
        Assert.Equal(0, GroundPick.Choose(UnderHighway, 0.1, 5.06, walkableOnly: false).Index);
    }

    [Fact]
    public void The_reason_lists_which_surfaces_are_walkable()
    {
        var deck = new List<(double Y, bool Walkable)> { (3.22, true), (0.20, true) };
        var (index, why) = GroundPick.Choose(deck, referenceY: 0.09, explicitY: null);
        Assert.Equal(1, index);
        Assert.Contains("3.22 walkable, 0.20 walkable", why);
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

    [Fact]
    public void Footprints_on_separate_levels_do_not_overlap()
    {
        // Same x and z, but the truck stands on a deck 3 m above the van's level.
        var truck = new FlatBox(0, 0, 0, 1, 1.1, 2.9);
        var van = new FlatBox(0.5, 1.5, 1, 0, 1.0, 2.5);
        Assert.False(Footprint.Overlap(truck, (3.3, 6.0), van, (0.0, 2.2)));
        Assert.True(Footprint.Overlap(truck, (0.3, 3.0), van, (0.0, 2.2)));
        Assert.False(Footprint.Overlap(truck, (0.3, 3.0), new FlatBox(10, 0, 0, 1, 1, 2.5), (0.0, 2.2)));
    }

    [Fact]
    public void A_player_is_refused_when_no_surface_is_walkable()
    {
        // Review round 10: a sole disconnected deck, and every surface failing the walk check.
        var deck = new List<(double Y, bool Walkable)> { (5.06, false) };
        var (index, why) = GroundPick.Choose(deck, referenceY: 0.1, explicitY: null, walkableOnly: true);
        Assert.Equal(-1, index);
        Assert.Contains("no walkable surface of 1: 5.06 not walkable", why);
        var road = new List<(double Y, bool Walkable)> { (5.06, false), (0.05, false) };
        Assert.Equal(-1, GroundPick.Choose(road, 0.1, null, walkableOnly: true).Index);
        // A vehicle keeps the fallback.
        Assert.Equal(0, GroundPick.Choose(deck, 0.1, null, walkableOnly: false).Index);
        Assert.Equal(1, GroundPick.Choose(road, 0.1, null, walkableOnly: false).Index);
    }
}
