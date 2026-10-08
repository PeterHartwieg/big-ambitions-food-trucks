using System.Collections.Generic;
using FoodTrucks.Core.Selling;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class CartMenuTests
{
    [Theory]
    [InlineData("ba:itemname_hotdog", true)]
    [InlineData("ba:itemname_sodacan", true)]
    [InlineData("ba:itemname_cheapflower", false)]
    [InlineData("BA:ITEMNAME_HOTDOG", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOnMenu(string? item, bool expected)
    {
        Assert.Equal(expected, CartMenu.IsOnMenu(item));
    }

    [Fact]
    public void PicksTheItemWithMostUnits()
    {
        var stock = new Dictionary<string, int> { ["ba:itemname_hotdog"] = 3, ["ba:itemname_sodacan"] = 7 };
        Assert.Equal("ba:itemname_sodacan", CartMenu.PickNext(stock));
    }

    [Fact]
    public void TiesGoToMenuOrder()
    {
        var stock = new Dictionary<string, int> { ["ba:itemname_sodacan"] = 4, ["ba:itemname_hotdog"] = 4 };
        Assert.Equal("ba:itemname_hotdog", CartMenu.PickNext(stock));
    }

    [Fact]
    public void IgnoresItemsOffTheMenuAndEmptyStock()
    {
        var stock = new Dictionary<string, int> { ["ba:itemname_cheapflower"] = 9, ["ba:itemname_burger"] = 0 };
        Assert.Null(CartMenu.PickNext(stock));
    }
}
