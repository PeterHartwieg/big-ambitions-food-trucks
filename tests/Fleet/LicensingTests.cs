using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class LicensingTests
{
    private static readonly Licensing licensing = new(ShippedData.Rules.Fleet);

    private static SaveState Licensed(int day = 1, int vehicles = 0)
    {
        var state = new SaveState();
        licensing.Buy(state.Licence, day, passedFoodSafety: true);
        for (var i = 1; i <= vehicles; i++) state.Vehicles.Add(new VehicleState { VehicleId = $"v{i}", VehicleType = "cart" });
        return state;
    }

    [Fact]
    public void TheLicenceNeedsTheFoodSafetyCourse()
    {
        var licence = new LicenceState();

        Assert.Equal(LicenceProblem.NoCourse, licensing.Buy(licence, 5, passedFoodSafety: false));
        Assert.False(licence.HasVendorLicence);
    }

    [Fact]
    public void ALicenceLastsSixtyDays()
    {
        var licence = new LicenceState();
        licensing.Buy(licence, 5, passedFoodSafety: true);

        Assert.Equal(64, licence.LicenceExpiresOnDay);
        Assert.True(Licensing.IsValid(licence, 64));
        Assert.False(Licensing.IsValid(licence, 65));
        Assert.Equal(60, Licensing.DaysLeft(licence, 5));
        Assert.Equal(5, licence.FirstLicensedDay);
    }

    [Fact]
    public void RenewingEarlyIsRefusedUntilTheLastTenDays()
    {
        var licence = new LicenceState();
        licensing.Buy(licence, 1, passedFoodSafety: true);

        Assert.Equal(LicenceProblem.TooEarlyToRenew, licensing.Buy(licence, 50, passedFoodSafety: true));
        Assert.Equal(LicenceProblem.None, licensing.Buy(licence, 51, passedFoodSafety: true));
        // Renewing before expiry adds a full period to the old expiry.
        Assert.Equal(120, licence.LicenceExpiresOnDay);
        Assert.Equal(1, licence.FirstLicensedDay);
    }

    [Fact]
    public void RenewingAfterExpiryStartsFromToday()
    {
        var licence = new LicenceState();
        licensing.Buy(licence, 1, passedFoodSafety: true);

        licensing.Buy(licence, 100, passedFoodSafety: true);

        Assert.Equal(159, licence.LicenceExpiresOnDay);
    }

    [Fact]
    public void TheFleetStartsWithOnePermit()
    {
        var state = Licensed(vehicles: 2);

        Assert.Equal(1, licensing.PermitCap(state, 1));
        Assert.Equal(PermitProblem.None, licensing.BuyPermit(state, "v1", 1));
        Assert.Equal(PermitProblem.CapReached, licensing.BuyPermit(state, "v2", 1));
    }

    [Fact]
    public void DaysInBusinessUnlockPermits()
    {
        var state = Licensed(day: 1);

        Assert.Equal(1, licensing.PermitCap(state, 14));
        Assert.Equal(2, licensing.PermitCap(state, 15));
        Assert.Equal(3, licensing.PermitCap(state, 36));
        Assert.Equal(4, licensing.PermitCap(state, 71));
    }

    [Fact]
    public void SalesUnlockPermitsSooner()
    {
        var state = Licensed(vehicles: 2);
        state.Vehicles[0].LifetimeSales = 1000;
        state.Vehicles[1].LifetimeSales = 500;

        Assert.Equal(3, licensing.PermitCap(state, 2));
    }

    [Fact]
    public void TheCapBlocksAFifthVehicle()
    {
        // Licensed on day 1, renewed on day 150 when the fourth permit has unlocked.
        var state = Licensed(vehicles: 5);
        licensing.Buy(state.Licence, 150, passedFoodSafety: true);
        for (var i = 1; i <= 4; i++) Assert.Equal(PermitProblem.None, licensing.BuyPermit(state, $"v{i}", 200));

        Assert.Equal(PermitProblem.CapReached, licensing.BuyPermit(state, "v5", 200));
    }

    [Fact]
    public void TheModSettingMovesTheCeiling()
    {
        var state = Licensed(vehicles: 6);

        Assert.Equal(2, licensing.PermitCap(state, 200, maxPermits: 2));
        Assert.Equal(4, licensing.PermitCap(state, 200, maxPermits: 10));
    }

    [Fact]
    public void TheCapNeverDropsWhenAVehicleLeaves()
    {
        var state = Licensed(vehicles: 2);
        state.Vehicles[0].LifetimeSales = 2000;
        Assert.Equal(3, licensing.PermitCap(state, 2));

        state.Vehicles.RemoveAt(0);

        Assert.Equal(3, licensing.PermitCap(state, 2));
        Assert.Equal(2, licensing.PermitCap(state, 2, maxPermits: 2));
    }

    [Fact]
    public void MilestoneOneCartsKeepWorkingWithAPermit()
    {
        var json = "{\"schemaVersion\":1,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"cart-1\",\"lifetimeSales\":15}],\"dailySales\":[]}";
        var state = new SaveCodec().Load(json).State!;

        Assert.Equal(1, Licensing.GrandfatherOldVehicles(state, "0.3.0"));
        Assert.Equal(0, Licensing.GrandfatherOldVehicles(state, "0.3.0"));

        Assert.Equal("cart", state.Vehicles[0].VehicleType);
        Assert.True(state.Vehicles[0].HasPermit);
        // The licence is still the player's to get.
        Assert.Equal(OpenProblem.NoLicence, Licensing.CanOpen(state, "cart-1", 1, playerAtWindow: true, hires: 0));
        licensing.Buy(state.Licence, 1, passedFoodSafety: true);
        Assert.Equal(OpenProblem.None, Licensing.CanOpen(state, "cart-1", 1, playerAtWindow: true, hires: 0));
    }

    [Fact]
    public void ANewerSaveGetsNoFreePermits()
    {
        var state = new SaveState { WrittenByModVersion = "0.3.0" };
        state.Vehicles.Add(new VehicleState { VehicleId = "v1" });

        Assert.Equal(0, Licensing.GrandfatherOldVehicles(state, "0.3.0"));
        Assert.False(state.Vehicles[0].HasPermit);
    }

    [Fact]
    public void ReleasingAVehicleKeepsTheUnlockItsSalesEarned()
    {
        var state = Licensed(vehicles: 2);
        state.Vehicles[0].LifetimeSales = 500;
        licensing.BuyPermit(state, "v1", 2);

        licensing.ReleasePermit(state, "v1", 2);
        state.Vehicles.RemoveAt(0);

        Assert.Equal(2, licensing.PermitCap(state, 2));
    }

    [Fact]
    public void ReleasingAPermitFreesAPlace()
    {
        var state = Licensed(vehicles: 2);
        licensing.BuyPermit(state, "v1", 1);

        licensing.ReleasePermit(state, "v1", 1);

        Assert.Equal(PermitProblem.None, licensing.BuyPermit(state, "v2", 1));
    }

    [Fact]
    public void PermitsNeedALicence()
    {
        var state = new SaveState();
        state.Vehicles.Add(new VehicleState { VehicleId = "v1" });

        Assert.Equal(PermitProblem.NoLicence, licensing.CanBuyPermit(state, "v1", 1));
        Assert.Equal(PermitProblem.UnknownVehicle, licensing.CanBuyPermit(state, "nope", 1));
    }

    [Fact]
    public void OpeningNeedsLicencePermitAndCrew()
    {
        var state = Licensed(vehicles: 1);

        Assert.Equal(OpenProblem.NoPermit, Licensing.CanOpen(state, "v1", 1, playerAtWindow: true, hires: 0));
        licensing.BuyPermit(state, "v1", 1);
        Assert.Equal(OpenProblem.None, Licensing.CanOpen(state, "v1", 1, playerAtWindow: true, hires: 0));
        Assert.Equal(OpenProblem.NoCrew, Licensing.CanOpen(state, "v1", 1, playerAtWindow: false, hires: 0));
        // A hire works under the vehicle's permit and the player's licence.
        Assert.Equal(OpenProblem.None, Licensing.CanOpen(state, "v1", 1, playerAtWindow: false, hires: 1));
        Assert.Equal(OpenProblem.LicenceExpired, Licensing.CanOpen(state, "v1", 61, playerAtWindow: true, hires: 0));
        Assert.Equal(OpenProblem.NoLicence, Licensing.CanOpen(new SaveState { Vehicles = { new VehicleState { VehicleId = "v1" } } }, "v1", 1, true, 0));
    }

    [Fact]
    public void AVehicleInTransitCantOpen()
    {
        var state = Licensed(vehicles: 1);
        licensing.BuyPermit(state, "v1", 1);
        state.Vehicles[0].Transit = new TransitState { ToSpotKey = "a", DepartsAt = 0, ArrivesAt = 10 };

        Assert.Equal(OpenProblem.InTransit, Licensing.CanOpen(state, "v1", 1, playerAtWindow: false, hires: 1));
    }

    [Theory]
    [InlineData("cart", true, 3, 1)]
    [InlineData("truck", true, 1, 2)]
    [InlineData("truck", true, 5, 3)]
    [InlineData("truck", false, 0, 0)]
    public void CrewIsCappedByTheVehicle(string type, bool player, int hires, int crew)
    {
        Assert.Equal(crew, Licensing.Crew(ShippedData.Tables.Vehicle(type)!, player, hires));
    }

    [Fact]
    public void LicenceAndPermitsSurviveASave()
    {
        var codec = new SaveCodec();
        var state = Licensed(day: 3, vehicles: 1);
        licensing.BuyPermit(state, "v1", 3);

        var loaded = codec.Load(codec.Serialize(state)).State!;

        Assert.Equal(3, loaded.Licence.FirstLicensedDay);
        Assert.True(loaded.Vehicles[0].HasPermit);
        Assert.Equal("cart", loaded.Vehicles[0].VehicleType);
    }
}
