using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Economics;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;

namespace FoodTrucks.Core.Tests.Economics;

// One working day for the calibration targets: the vehicle restocks before each stop, runs the
// demand model and the sales simulator minute by minute, and pays for what it sold.
internal sealed class RouteStop
{
    public AreaMix Areas { get; init; } = AreaMix.Single("office");
    public string? Neighbourhood { get; init; }
    public int FromMinute { get; init; }
    public int ToMinute { get; init; }
}

internal sealed class RoutePlan
{
    public string VehicleType { get; init; } = "cart";
    // Units per item at each restock; the total should fit the vehicle's stock limit.
    public IReadOnlyDictionary<string, int> Load { get; init; } = new Dictionary<string, int>();
    public int Servers { get; init; } = 1;
    // Hires paid for every hour from the first stop's opening to the last stop's closing.
    public int Hires { get; init; }
    public DayOfWeek Day { get; init; } = DayOfWeek.Wednesday;
    public int Month { get; init; } = 5;
    public bool Raining { get; init; }
    public double DrivenMetres { get; init; }
    public IReadOnlyList<RouteStop> Stops { get; init; } = Array.Empty<RouteStop>();
    public long Seed { get; init; } = 1;
}

internal sealed class RouteOutcome
{
    public int Sold { get; set; }
    public int Loaded { get; set; }
    // Close to 1 means the stock limit, not the demand, set the day's takings.
    public double SoldShare => Loaded == 0 ? 0 : Sold / (double)Loaded;
    public int GaveUp { get; set; }
    public long RevenueCents { get; set; }
    public long StockCents { get; set; }
    public long WageCents { get; set; }
    public long FuelCents { get; set; }
    public long LicenceCents { get; set; }
    public long ProfitCents => RevenueCents - StockCents - WageCents - FuelCents - LicenceCents;
}

internal static class RouteDay
{
    // Item prices in cents. The hot dog's is the game's (seen in game); the rest are guesses
    // until the game side reads Item.DefaultMarketPrice.
    public static readonly IReadOnlyDictionary<string, long> PriceCents = new Dictionary<string, long>
    {
        ["ba:itemname_hotdog"] = 1165,
        ["ba:itemname_burger"] = 1500,
        ["ba:itemname_frenchfries"] = 600,
        ["ba:itemname_cupofcoffee"] = 450,
        ["ba:itemname_sodacan"] = 350,
    };

    // Wholesale is guessed at 30% of the price, like the reference shop's hot dog.
    public static long WholesaleCents(string item) => (long)Math.Round(PriceCents[item] * 0.3);

    public static RouteOutcome Run(RoutePlan plan, GameVariables? difficulty = null)
    {
        difficulty ??= GameVariables.Default;
        var tables = ShippedData.Tables;
        var economy = new Economy(ShippedData.Rules.Economy);
        var model = new DemandModel(tables);
        var simulator = new SalesSimulator(tables, ShippedData.Rules.Queue);
        var outcome = new RouteOutcome();
        var menu = plan.Load.Keys.ToList();

        foreach (var stop in plan.Stops)
        {
            var stock = plan.Load.ToDictionary(p => p.Key, p => p.Value);
            outcome.Loaded += stock.Values.Sum();
            var state = SalesSimulator.Open(SeededRandom.SeedFor(plan.VehicleType, plan.Seed * 100 + stop.FromMinute));
            for (var minute = stop.FromMinute; minute < stop.ToMinute; minute++)
            {
                var inStock = menu.Where(item => stock[item] > 0).ToList();
                var estimate = model.Estimate(new DemandQuery
                {
                    Areas = stop.Areas,
                    Neighbourhood = stop.Neighbourhood,
                    Menu = inStock,
                    MinuteOfDay = minute % 1440,
                    Day = minute >= 1440 ? (DayOfWeek)(((int)plan.Day + 1) % 7) : plan.Day,
                    Month = plan.Month,
                    Raining = plan.Raining,
                    Promotion = difficulty.BaseCustomerPromotion / GameVariables.Default.BaseCustomerPromotion
                });
                var result = simulator.Advance(state, new SalesInput
                {
                    Menu = menu,
                    Stock = stock,
                    CustomersPerHour = estimate.CustomersPerHour,
                    CustomerShares = estimate.CustomerShares,
                    AddOnsPerCustomer = estimate.AddOnsPerCustomer,
                    Servers = plan.Servers,
                    Raining = plan.Raining
                }, 1);
                foreach (var sale in result.Sales)
                {
                    stock[sale.Item]--;
                    outcome.Sold++;
                    outcome.RevenueCents += PriceCents[sale.Item];
                    outcome.StockCents += (long)Math.Round(Economy.WholesaleCents(WholesaleCents(sale.Item), difficulty));
                }
                outcome.GaveUp += result.Count(CustomerExit.GaveUp) + result.Count(CustomerExit.QueueFull);
                if (result.SoldOut) break;
            }
        }

        if (plan.Stops.Count > 0 && plan.Hires > 0)
        {
            var hours = (plan.Stops.Max(s => s.ToMinute) - plan.Stops.Min(s => s.FromMinute)) / 60.0;
            var shop = economy.Rules.Shop;
            outcome.WageCents = plan.Hires * Economy.WageCents(shop.BaseHourlyWageCents, shop.WorkerSkill, hours, difficulty);
        }
        outcome.FuelCents = economy.FuelCents(plan.DrivenMetres);
        // The licence is the only running fee; vehicles, permits and equipment are bought once.
        outcome.LicenceCents = economy.Price("licence", difficulty) / ShippedData.Rules.Fleet.LicenceDays;
        return outcome;
    }
}
