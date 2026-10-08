using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class ShippedDataTests
{
    [Fact]
    public void ShippedFilesLoad()
    {
        var tables = ShippedData.Tables;

        Assert.Equal(6, tables.AreaNames.Count);
        Assert.Equal(6, tables.CategoryNames.Count);
        Assert.Equal(0.3, tables.MarketFloor);
        Assert.Empty(tables.Zones);
    }
}
