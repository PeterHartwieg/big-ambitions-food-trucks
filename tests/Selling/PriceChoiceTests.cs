using FoodTrucks.Core.Selling;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class PriceChoiceTests
{
    [Theory]
    [InlineData(500L, 400L, 300L, 500L)]
    [InlineData(null, 400L, 300L, 400L)]
    [InlineData(null, null, 300L, 300L)]
    [InlineData(0L, 400L, 300L, 400L)]
    [InlineData(-1L, 400L, 300L, 400L)]
    [InlineData(null, 0L, 300L, 300L)]
    [InlineData(0L, -1L, 300L, 300L)]
    [InlineData(500L, 0L, -1L, 500L)]
    [InlineData(null, null, 0L, 0L)]
    [InlineData(-1L, -1L, -1L, 0L)]
    [InlineData(long.MaxValue, 400L, 300L, long.MaxValue)]
    public void ChoosesTheFirstPositivePrice(long? custom, long? manager, long fallback, long expected)
    {
        Assert.Equal(expected, PriceChoice.Choose(custom, manager, fallback));
    }
}

public class PriceParseTests
{
    [Theory]
    [InlineData("12.50", 1250L)]
    [InlineData("12,50", 1250L)]
    [InlineData("$12", 1200L)]
    [InlineData(" 0.01 ", 1L)]
    [InlineData("0.005", 1L)]
    [InlineData("10000", 1_000_000L)]
    [InlineData("10000.004", 1_000_000L)]
    public void ParsesPrices(string text, long expected)
    {
        Assert.True(PriceChoice.TryParseCents(text, out var cents));
        Assert.Equal(expected, cents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("0.004")]
    [InlineData("-3")]
    [InlineData("10000.01")]
    [InlineData("10000.005")]
    [InlineData("1e3")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    public void RejectsWhatTheSaveWouldRefuse(string? text)
    {
        Assert.False(PriceChoice.TryParseCents(text, out _));
    }

    [Fact]
    public void EveryAcceptedPriceLoadsBack()
    {
        var codec = new FoodTrucks.Core.Saves.SaveCodec();
        var state = new FoodTrucks.Core.Saves.SaveState();
        var vehicle = FoodTrucks.Core.Saves.StreetSales.GetOrAdd(state, "cart");
        foreach (var text in new[] { "0.005", "0.01", "9999.995", "10000" })
        {
            Assert.True(PriceChoice.TryParseCents(text, out var cents));
            vehicle.CustomPriceCents["ba:itemname_hotdog"] = cents;
            Assert.Equal(FoodTrucks.Core.Saves.LoadStatus.Loaded, codec.Load(codec.Serialize(state)).Status);
        }
    }
}
