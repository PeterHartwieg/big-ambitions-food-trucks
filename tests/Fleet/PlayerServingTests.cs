using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class PlayerServingTests
{
    private const double Working = 8;

    [Fact]
    public void ATruckIsServedOnlyFromItsHatch()
    {
        Assert.True(PlayerServing.Serves("truck", atHatch: true, 0.5, Working));
        Assert.True(PlayerServing.Serves("truck", atHatch: true, null, Working));
        // Stepped out by the rear door: close by, but not serving.
        Assert.False(PlayerServing.Serves("truck", atHatch: false, 3.5, Working));
        Assert.False(PlayerServing.Serves("truck", atHatch: false, 0, Working));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(8.0, true)]
    [InlineData(8.01, false)]
    [InlineData(30.0, false)]
    public void ACartIsServedByThePlayerStandingBesideIt(double distance, bool serves)
    {
        Assert.Equal(serves, PlayerServing.Serves("cart", atHatch: false, distance, Working));
    }

    [Fact]
    public void ACartWithoutAPositionIsNotServed()
    {
        Assert.False(PlayerServing.Serves("cart", atHatch: false, null, Working));
    }
}
