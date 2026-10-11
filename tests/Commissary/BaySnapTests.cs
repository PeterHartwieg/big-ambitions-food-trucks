using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class BaySnapTests
{
    // Hell's Kitchen as the 0.9.8 log has it: the garage door at x 404.5, z 228.4 across a one-way street, and the
    // only parking lane on the far kerb, 2.4 m wide round x 393.1, from z 200 to z 225.
    private static bool HellsKitchenParking(double x, double z) => x >= 391.9 && x <= 394.3 && z >= 200 && z <= 225;

    private static IEnumerable<(double X, double Z)> LaneAxes(double x, double z) => new[] { (0.0, 1.0), (1.0, 0.0) };

    [Fact]
    public void TheBaySnapsAcrossTheStreetToTheNearestPlaceATruckParksLegally()
    {
        var found = BaySnap.Nearest(404.5, 228.4, HellsKitchenParking, LaneAxes);
        Assert.NotNull(found);
        // On the lane, far enough from its end for the rear wheels: z at most 225 - 1.9.
        Assert.True(HellsKitchenParking(found!.X, found.Z));
        Assert.InRange(found.X, 392.7, 393.5);
        Assert.InRange(found.Z, 222.5, 223.1);
        Assert.Equal(0, found.AlongX, 9);
        Assert.Equal(1, found.AlongZ, 9);
        Assert.True(BaySnap.Fits(found.X, found.Z, (found.AlongX, found.AlongZ), HellsKitchenParking));
    }

    [Fact]
    public void APointAlreadyOnLegalParkingStaysPut()
    {
        var found = BaySnap.Nearest(393.1, 217.6, HellsKitchenParking, LaneAxes);
        Assert.NotNull(found);
        Assert.Equal(393.1, found!.X, 6);
        Assert.Equal(217.6, found.Z, 6);
        Assert.Equal(0, found.Distance, 9);
    }

    [Fact]
    public void AScrapOfParkingTooSmallForATruckIsPassedOver()
    {
        // A 2 m patch right by the door (a disabled bay the probe doesn't count would look the same), and a real lane
        // 12 m off.
        bool Legal(double x, double z) => (x >= 0 && x <= 2 && z >= 0 && z <= 2) || (x >= 11 && x <= 13.4 && z >= -20 && z <= 20);
        var found = BaySnap.Nearest(1, 1, Legal, LaneAxes);
        Assert.NotNull(found);
        Assert.InRange(found!.X, 11.8, 12.6);
    }

    [Fact]
    public void TheTruckLinesUpWithAnAxisTheLaneAllows()
    {
        // A lane running along x: lengthways along z doesn't fit, along x does.
        bool Legal(double x, double z) => z >= -1.2 && z <= 1.2 && x >= -10 && x <= 10;
        var found = BaySnap.Nearest(0, 5, Legal, LaneAxes);
        Assert.NotNull(found);
        Assert.Equal(1, found!.AlongX, 9);
        Assert.Equal(0, found.AlongZ, 9);
        Assert.InRange(found.Z, -0.35, 0.35);
    }

    [Fact]
    public void NoLegalParkingWithinReachFindsNothing()
    {
        Assert.Null(BaySnap.Nearest(0, 0, (x, z) => x > 40, LaneAxes));
    }

    [Fact]
    public void TheGridRunsNearestFirst()
    {
        var grid = BaySnap.Grid(3);
        Assert.Equal((0.0, 0.0), grid[0]);
        var distances = grid.Select(c => c.X * c.X + c.Z * c.Z).ToList();
        Assert.Equal(distances.OrderBy(d => d), distances);
        Assert.All(grid, c => Assert.True(c.X * c.X + c.Z * c.Z <= 9 + 1e-9));
    }
}
