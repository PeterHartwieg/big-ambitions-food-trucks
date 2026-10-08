using System;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class StreetSalesTests
{
    [Fact]
    public void FirstSaleCreatesVehicleAndUpdatesLedgerAndLifetimeTotals()
    {
        var state = new SaveState();

        Assert.True(StreetSales.CanRecord(state, "cart-1", 5, 125, 3));
        Assert.Empty(state.Vehicles);
        Assert.Empty(state.DailySales);
        StreetSales.Record(state, "cart-1", 5, 125, 3);

        var vehicle = Assert.Single(state.Vehicles);
        Assert.Equal("cart-1", vehicle.VehicleId);
        Assert.Equal(3, vehicle.LifetimeSales);
        Assert.Equal(375, vehicle.LifetimeRevenueCents);
        Assert.Equal(375, SalesLedger.SumCents(state, 5, 5));
        Assert.Equal(0, vehicle.SaleProgress);
    }

    [Fact]
    public void LaterSalesAddToExistingTotalsAndPreserveOtherVehicleState()
    {
        var state = new SaveState();
        var vehicle = StreetSales.GetOrAdd(state, "cart-1");
        vehicle.IsOpen = true;
        vehicle.SaleProgress = 0.5;
        StreetSales.Record(state, "cart-1", 5, 125, 3);
        StreetSales.Record(state, "cart-1", 5, 200, 2);
        StreetSales.Record(state, "cart-1", 6, 100, 1);

        Assert.Same(vehicle, Assert.Single(state.Vehicles));
        Assert.Equal(6, vehicle.LifetimeSales);
        Assert.Equal(875, vehicle.LifetimeRevenueCents);
        Assert.Equal(775, SalesLedger.SumCents(state, 5, 5));
        Assert.Equal(100, SalesLedger.SumCents(state, 6, 6));
        Assert.True(vehicle.IsOpen);
        Assert.Equal(0.5, vehicle.SaleProgress);
    }

    [Fact]
    public void VehicleIdsAreComparedOrdinally()
    {
        var state = new SaveState();
        StreetSales.Record(state, "cart-1", 1, 100, 1);
        StreetSales.Record(state, "Cart-1", 1, 200, 2);

        Assert.Equal(2, state.Vehicles.Count);
        Assert.Equal(1, StreetSales.Find(state, "cart-1")!.LifetimeSales);
        Assert.Equal(2, StreetSales.Find(state, "Cart-1")!.LifetimeSales);
        Assert.Equal(500, SalesLedger.SumCents(state, 1, 1));
    }

    [Fact]
    public void FindDoesNotCreateAndGetOrAddReusesTheEntry()
    {
        var state = new SaveState();

        Assert.Null(StreetSales.Find(null, "cart-1"));
        Assert.Null(StreetSales.Find(state, "cart-1"));
        Assert.Empty(state.Vehicles);
        var vehicle = StreetSales.GetOrAdd(state, "cart-1");

        Assert.Same(vehicle, StreetSales.GetOrAdd(state, "cart-1"));
        Assert.Same(vehicle, StreetSales.Find(state, "cart-1"));
        Assert.Single(state.Vehicles);
    }

    [Fact]
    public void FreeSalesStillIncreaseLifetimeSales()
    {
        var state = new SaveState();

        Assert.True(StreetSales.CanRecord(state, "cart-1", 1, 0, 3));
        StreetSales.Record(state, "cart-1", 1, 0, 3);

        Assert.Equal(3, Assert.Single(state.Vehicles).LifetimeSales);
        Assert.Equal(0, state.Vehicles[0].LifetimeRevenueCents);
        Assert.Empty(state.DailySales);
    }

    [Theory]
    [InlineData(null, 100, 1)]
    [InlineData("", 100, 1)]
    [InlineData("cart-1", -1, 1)]
    [InlineData("cart-1", 100, 0)]
    [InlineData("cart-1", 100, -1)]
    public void InvalidInputIsRejectedWithoutChangingState(string? id, long price, int count)
    {
        var state = new SaveState();

        Assert.False(StreetSales.CanRecord(state, id!, 1, price, count));
        Assert.ThrowsAny<ArgumentException>(() => StreetSales.Record(state, id!, 1, price, count));
        Assert.Empty(state.Vehicles);
        Assert.Empty(state.DailySales);
    }

    [Fact]
    public void NullStateIsRejected()
    {
        Assert.False(StreetSales.CanRecord(null!, "cart-1", 1, 100, 1));
        Assert.Throws<ArgumentNullException>(() => StreetSales.Record(null!, "cart-1", 1, 100, 1));
        Assert.Throws<ArgumentNullException>(() => StreetSales.GetOrAdd(null!, "cart-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetOrAddRejectsEmptyIds(string? id)
    {
        var state = new SaveState();

        Assert.Throws<ArgumentException>(() => StreetSales.GetOrAdd(state, id!));
        Assert.Empty(state.Vehicles);
    }

    [Fact]
    public void PriceTimesCountOverflowIsRejectedBeforeCreatingVehicle()
    {
        var state = new SaveState();

        Assert.False(StreetSales.CanRecord(state, "cart-1", 1, long.MaxValue, 2));
        Assert.Throws<OverflowException>(() => StreetSales.Record(state, "cart-1", 1, long.MaxValue, 2));
        Assert.Empty(state.Vehicles);
        Assert.Empty(state.DailySales);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LedgerOverflowIsRejectedBeforeCreatingVehicle(int day)
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);

        Assert.False(StreetSales.CanRecord(state, "cart-1", day, 1, 1));
        Assert.Throws<OverflowException>(() => StreetSales.Record(state, "cart-1", day, 1, 1));
        Assert.Empty(state.Vehicles);
        Assert.Equal(long.MaxValue, Assert.Single(state.DailySales).RevenueCents);
    }

    [Theory]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, long.MaxValue)]
    public void VehicleTotalsOverflowIsRejectedBeforeChangingLedger(int sales, long revenue)
    {
        var state = new SaveState();
        var vehicle = StreetSales.GetOrAdd(state, "cart-1");
        vehicle.LifetimeSales = sales;
        vehicle.LifetimeRevenueCents = revenue;

        Assert.False(StreetSales.CanRecord(state, "cart-1", 1, 1, 1));
        Assert.Throws<OverflowException>(() => StreetSales.Record(state, "cart-1", 1, 1, 1));
        Assert.Empty(state.DailySales);
        Assert.Equal(sales, vehicle.LifetimeSales);
        Assert.Equal(revenue, vehicle.LifetimeRevenueCents);
    }

    [Fact]
    public void ExactArithmeticLimitsAreAllowed()
    {
        var state = new SaveState();
        var vehicle = StreetSales.GetOrAdd(state, "cart-1");
        vehicle.LifetimeSales = int.MaxValue - 1;
        vehicle.LifetimeRevenueCents = long.MaxValue - 1;
        SalesLedger.Record(state, 1, long.MaxValue - 1);

        Assert.True(StreetSales.CanRecord(state, "cart-1", 1, 1, 1));
        StreetSales.Record(state, "cart-1", 1, 1, 1);

        Assert.Equal(int.MaxValue, vehicle.LifetimeSales);
        Assert.Equal(long.MaxValue, vehicle.LifetimeRevenueCents);
        Assert.Equal(long.MaxValue, SalesLedger.SumCents(state, 1, 1));
    }

    [Theory]
    [InlineData("null vehicles")]
    [InlineData("null vehicle")]
    [InlineData("duplicate vehicle")]
    [InlineData("negative sales")]
    [InlineData("negative revenue")]
    [InlineData("null ledger")]
    [InlineData("null day")]
    [InlineData("negative ledger")]
    public void UnusableStateCannotBeRecorded(string problem)
    {
        var state = new SaveState();
        switch (problem)
        {
            case "null vehicles": state.Vehicles = null!; break;
            case "null vehicle": state.Vehicles.Add(null!); break;
            case "duplicate vehicle":
                state.Vehicles.Add(new VehicleState { VehicleId = "cart-1" });
                state.Vehicles.Add(new VehicleState { VehicleId = "cart-1" });
                break;
            case "negative sales": StreetSales.GetOrAdd(state, "cart-1").LifetimeSales = -1; break;
            case "negative revenue": StreetSales.GetOrAdd(state, "cart-1").LifetimeRevenueCents = -1; break;
            case "null ledger": state.DailySales = null!; break;
            case "null day": state.DailySales.Add(null!); break;
            case "negative ledger": state.DailySales.Add(new DailySales { RevenueCents = -1 }); break;
        }

        Assert.False(StreetSales.CanRecord(state, "cart-1", 1, 100, 1));
        Assert.NotNull(Record.Exception(() => StreetSales.Record(state, "cart-1", 1, 100, 1)));
    }
}
