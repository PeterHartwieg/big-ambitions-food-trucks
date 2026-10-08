using System;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class SalesRateTests
{
    [Theory]
    [InlineData(30, 1.0, 20, 20)]
    [InlineData(10, 1.0, 20, 10)]
    [InlineData(30, 0.5, 20, 15)]
    [InlineData(30, 0, 20, 0)]
    [InlineData(0, 1.0, 20, 0)]
    [InlineData(30, 1.0, 0, 0)]
    [InlineData(double.NaN, 1.0, 20, 0)]
    [InlineData(30, double.PositiveInfinity, 20, 0)]
    public void ServiceCapsDemandAfterPrice(double customers, double conversion, double capacity, double rate)
    {
        Assert.Equal(rate, SalesRate.PerHour(customers, conversion, capacity), 9);
    }

    [Fact]
    public void ADemandRateDrivesTheSameTick()
    {
        var rate = SalesRate.PerHour(30, 0.8, 20);
        var result = SalesTick.Advance(0, 60, rate, 100);

        Assert.Equal(20, result.Sales);
    }

    [Fact]
    public void AnHourAtAnOfficeLunchFromDemandToSales()
    {
        var tables = ShippedData.Tables;
        var menu = Menu.FromEquipment(tables, "cart", new[] { "hotdoggrill", "cooler" }).Menu;
        var estimate = new DemandModel(tables).Estimate(new DemandQuery
        {
            Areas = AreaMix.Single("office"),
            Neighbourhood = "ba:neighborhood_midtown",
            Menu = menu.Items,
            MinuteOfDay = 12 * 60,
            Day = DayOfWeek.Wednesday,
            Month = 4
        });
        var capacity = ServiceRate.CustomersPerHour(1, ServiceRate.OrderMinutes(tables, menu.Items, estimate.CustomerShares));
        var rate = SalesRate.PerHour(estimate.CustomersPerHour, tables.PriceCurve.Conversion(1.0), capacity);

        var progress = 0.0;
        var sales = 0;
        for (var minute = 0; minute < 60; minute++)
        {
            var tick = SalesTick.Advance(progress, 1, rate, 1000 - sales);
            sales += tick.Sales;
            progress = tick.Progress;
        }

        // Midtown office lunch brings more than a one-person cart can serve, so service sets the pace.
        Assert.True(estimate.CustomersPerHour > capacity);
        Assert.Equal(capacity, rate, 9);
        Assert.InRange(sales, (int)Math.Floor(capacity) - 1, (int)Math.Ceiling(capacity));
    }
}
