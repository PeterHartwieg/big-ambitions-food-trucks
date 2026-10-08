using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class ServiceRateTests
{
    private static DemandTables Tables => ShippedData.Tables;

    [Fact]
    public void HotDogCartServesTwentyAnHour()
    {
        var minutes = ServiceRate.OrderMinutes(Tables, new[] { "ba:itemname_hotdog" }, null);

        Assert.Equal(3, minutes);
        Assert.Equal(20, ServiceRate.CustomersPerHour(1, minutes), 9);
    }

    [Fact]
    public void TheSlowestItemSetsTheCategorysPace()
    {
        var minutes = ServiceRate.OrderMinutes(Tables, new[] { "ba:itemname_frenchfries", "ba:itemname_burger" }, null);

        Assert.Equal(5, minutes);
        Assert.Equal(12, ServiceRate.CustomersPerHour(1, minutes), 9);
        Assert.Equal(36, ServiceRate.CustomersPerHour(3, minutes), 9);
    }

    [Fact]
    public void CategoriesWeighByTheirCustomers()
    {
        var shares = new Dictionary<string, double> { ["pizza"] = 0.75, ["bakery"] = 0.25 };

        var minutes = ServiceRate.OrderMinutes(Tables, new[] { "ba:itemname_pizza", "ba:itemname_donut" }, shares);

        Assert.Equal(0.75 * 6 + 0.25 * 3, minutes, 9);
    }

    [Fact]
    public void NothingToServeServesNobody()
    {
        Assert.Equal(0, ServiceRate.OrderMinutes(Tables, new[] { "ba:itemname_cheapflower" }, null));
        Assert.Equal(0, ServiceRate.CustomersPerHour(1, 0));
        Assert.Equal(0, ServiceRate.CustomersPerHour(0, 3));
    }
}
