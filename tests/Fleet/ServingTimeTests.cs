using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class ServingTimeTests
{
    [Theory]
    [InlineData(-1, 5)]
    [InlineData(60, 60)]
    [InlineData(900, 720)]
    public void SliderSelectionHasBoundedWorkingSpells(int value, int selected) => Assert.Equal(selected, ServingTime.Selection(value));

    [Theory]
    [InlineData(1439.75, 200, .25)]
    [InlineData(1440, 200, 1)]
    [InlineData(800.5, .2, .2)]
    public void NativeSkipCannotPassTheNextSalesMinute(double now, double requested, double expected) =>
        Assert.Equal(expected, ServingTime.Advance(now, requested), 8);

    [Fact]
    public void InvalidClockCannotAdvanceTheWorld()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServingTime.Advance(double.NaN, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServingTime.Advance(100, -1));
    }

    [Theory]
    [InlineData(2)] // visible frames catching up
    [InlineData(60)] // fast-forward
    [InlineData(500)] // native work skip
    public void CatchUpKeepsTheSameOrdersStockAndDailyTakingsAsLiveMinutes(int frameMinutes)
    {
        var live = Sell(1, 500);
        Assert.Equal(live, Sell(frameMinutes, 500));
    }

    [Fact]
    public void SellingStopsOnTheSellOutMinuteAndNeverInventsStock()
    {
        var live = Sell(1, 4);
        var skip = Sell(500, 4);
        Assert.Equal(live, skip);
        Assert.Contains("\"stock\":0", skip);
        Assert.Contains("\"sold\":4", skip);
    }

    // CartSelling's absolute SalesClock loop, with real Core orders, cargo draws and midnight pots.
    // Changing the number of minutes between frames cannot change the sequence of one-minute inputs.
    private static string Sell(int frameMinutes, int units)
    {
        var simulator = new SalesSimulator(ShippedData.Tables, ShippedData.Rules.Queue);
        var queue = SalesSimulator.Open(36);
        var state = new SaveState();
        var stock = new Dictionary<string, int> { ["ba:itemname_hotdog"] = units };
        var input = new SalesInput { Menu = stock.Keys.ToArray(), Stock = stock, CustomersPerHour = 100, Servers = 1 };
        var from = SalesClock.Start(1430.7);
        var orders = new List<string>();
        var sold = 0;
        var open = true;
        for (var frame = from + frameMinutes; from < 1610 && open; frame += frameMinutes)
        {
            var until = Math.Min(frame, 1610);
            while (SalesClock.NextStep(from, until) is { } end && open)
            {
                var result = simulator.Advance(queue, input, SalesClock.Elapsed(from, end));
                foreach (var draw in result.Draws) stock[draw.Key] -= draw.Value;
                foreach (var sale in result.Sales)
                {
                    var day = (int)(from / 1440) + 1;
                    CrewTakings.Accrue(state, "cart", day, 1300);
                    StreetSales.Record(state, "cart", day, 1300, 1);
                    orders.Add($"{sale.CustomerId}:{sale.Item}:{sale.Minute}");
                    sold++;
                }
                from = end;
                open = !result.SoldOut;
            }
            // End-of-skip Perform/catch-up at the same absolute minute has nothing left to sell.
            Assert.Null(SalesClock.NextStep(from, Math.Min(until, from)));
        }
        return JsonConvert.SerializeObject(new { stock = stock.Values.Sum(), sold, from, orders, state.CrewTakings, state.DailySales });
    }
}
