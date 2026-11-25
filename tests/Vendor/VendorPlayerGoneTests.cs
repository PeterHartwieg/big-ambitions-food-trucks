using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Vendor;

public class VendorPlayerGoneTests
{
    private const double Working = 8;

    [Theory]
    [InlineData("cart")]
    [InlineData("truck")]
    public void AMissingPlayerServesNothingAndIsNeverMeasured(string kind)
    {
        var measured = false;
        Assert.False(PlayerServing.Serves(kind, playerPresent: false, atHatch: true, 0.5, d => { measured = true; return d; }, Working, atCart: true));
        Assert.False(measured);
    }

    [Fact]
    public void APresentPlayerIsMeasuredOnce()
    {
        var measured = 0;
        Assert.True(PlayerServing.Serves("cart", playerPresent: true, atHatch: false, 0.5, d => { measured++; return d; }, Working, atCart: true));
        Assert.Equal(1, measured);
        Assert.False(PlayerServing.Serves("cart", playerPresent: true, atHatch: false, 0.5, _ => null, Working, atCart: true));
    }
}
