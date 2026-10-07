using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class SaveStateTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 8)]
    [InlineData(int.MaxValue - 1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void IncrementSaveCountSaturatesAtTheLimit(int initial, int expected)
    {
        var state = new SaveState { SaveCount = initial };

        SaveState.IncrementSaveCount(state);

        Assert.Equal(expected, state.SaveCount);
    }
}
