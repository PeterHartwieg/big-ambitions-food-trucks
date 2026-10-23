using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Pitches;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class CartSalesTests
{
    [Theory]
    [InlineData(0.75, 82500)]
    [InlineData(0.5, 55000)]
    [InlineData(1, 110000)]
    public void UsesGameResaleSettingAndIncludesInventory(double multiplier, long expected)
    {
        Assert.Equal(expected, CartSales.Quote(1000, multiplier, 0, new[] { 100 * multiplier }));
        Assert.Equal(7500, CartSales.Quote(1000, .75, 900, new[] { 75.0 }));
        Assert.Equal(7500, CartSales.Quote(1000, .75, 0, new[] { 75.0 }, true));
    }

    [Fact]
    public void ClosureRetainsAccountsAndSharedCommissaryButEndsOnlyThisBusiness()
    {
        var sold = new VehicleState { VehicleId = "sold", HasPermit = true, IsOpen = true,
            Crew = new List<string> { "worker" }, Schedule = new HireSchedule(), ReturnPending = true,
            CrewWindowLost = true, LifetimeRevenueCents = 900 };
        var other = new VehicleState { VehicleId = "other", HasPermit = true, IsOpen = true };
        var state = new SaveState { Vehicles = new List<VehicleState> { sold, other } };
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "sold", CommissaryId = "site", Kind = "cart" });
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "other", CommissaryId = "site", Kind = "cart" });
        state.Commissary.Stock["site"] = new List<CommissaryStockLine> { new CommissaryStockLine { ItemName = "food", Units = 10 } };
        state.Partnerships.Add(new PartnershipState { VehicleId = "sold", Venue = "venue" });
        state.Partnerships.Add(new PartnershipState { VehicleId = "other", Venue = "other-venue" });
        state.PremiumHolds["pitch"] = new List<PlaceHold> { new PlaceHold { VehicleId = "sold", From = 5 } };
        state.Events.Events.Add(new CityEvent { Booked = 1, Holds = new List<PlaceHold> { new PlaceHold { VehicleId = "sold", From = 5 } } });
        CartSales.Close(state, "sold", 100);
        CartSales.Close(state, "sold", 200);
        Assert.False(sold.HasPermit);
        Assert.False(sold.IsOpen);
        Assert.Empty(sold.Crew);
        Assert.Null(sold.Schedule);
        Assert.False(sold.ReturnPending);
        Assert.False(sold.CrewWindowLost);
        Assert.Equal(900, sold.LifetimeRevenueCents);
        Assert.Equal(2, state.Vehicles.Count);
        Assert.True(other.IsOpen);
        Assert.True(other.HasPermit);
        Assert.Equal("other", Assert.Single(state.Commissary.Spaces).VehicleId);
        Assert.Equal(10, Assert.Single(state.Commissary.Stock["site"]).Units);
        Assert.Equal("other", Assert.Single(state.Partnerships).VehicleId);
        Assert.Equal(100, state.PremiumHolds["pitch"][0].Until);
        Assert.Equal(1, state.Events.Events[0].Booked);
        Assert.Equal(100, state.Events.Events[0].Holds[0].Until);
    }

    [Fact]
    public void PendingSaleSurvivesReloadAndOldSavesStartEmpty()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        state.CartSales.Add(new CartSale { VehicleId = "cart", Name = "Food cart 1", Day = 1, Cents = 12345, Minute = 50 });
        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(12345, Assert.Single(loaded.State!.CartSales).Cents);
        Assert.Empty(codec.Load("{\"schemaVersion\":2}").State!.CartSales);
        state.CartSales[0].Cents = -1;
        Assert.Equal(LoadStatus.Corrupt, codec.Load(codec.Serialize(state)).Status);
    }

    [Fact]
    public void SettlingOneCartLeavesOtherCrewHoursPending()
    {
        var state = new SaveState();
        CrewPay.Served(state, "employee1", "sold", 10);
        CrewPay.Served(state, "employee2", "other", 10);
        var paid = CrewPay.CloseHours(state, long.MaxValue, (_, _, _) => true, "sold");
        Assert.Single(paid);
        Assert.Equal("employee1", paid[0].EmployeeId);
        Assert.True(state.CrewServedHour.ContainsKey("employee2"));
        Assert.Empty(CrewPay.CloseHours(state, long.MaxValue, (_, _, _) => true, "sold"));
    }
}
