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
    public void EveryVehicleCanHaveAPermit()
    {
        var state = Licensed(vehicles: 6);

        for (var i = 1; i <= 6; i++) Assert.Equal(PermitProblem.None, Licensing.BuyPermit(state, $"v{i}", 1));
        Assert.Equal(PermitProblem.AlreadyHasPermit, Licensing.BuyPermit(state, "v1", 1));
    }

    [Fact]
    public void TheCityChargesTheFeesInTheRules()
    {
        Assert.Equal(250_00, ShippedData.Rules.Fleet.LicenceFeeCents);
        Assert.Equal(500_00, ShippedData.Rules.Fleet.PermitFeeCents);
    }

    [Fact]
    public void TheFoodSafetyCourseTakesSixHoursAtTwentyFiveDollars()
    {
        Assert.Equal(360, ShippedData.Rules.Fleet.CourseMinutes);
        Assert.Equal(25, ShippedData.Rules.Fleet.CoursePricePerHour);
    }

    [Fact]
    public void TheCityWritesWhenRenewalOpensAndWhenTheLicenceRunsOut()
    {
        var licence = new LicenceState();
        licensing.Buy(licence, 1, passedFoodSafety: true);

        Assert.Equal(LicenceReminder.None, licensing.ReminderFor(licence, 50));
        Assert.Equal(LicenceReminder.RenewalOpen, licensing.ReminderFor(licence, 51));
        Assert.Equal(LicenceReminder.None, licensing.ReminderFor(licence, 60));
        Assert.Equal(LicenceReminder.Expired, licensing.ReminderFor(licence, 61));
        Assert.Equal(LicenceReminder.None, licensing.ReminderFor(licence, 62));
        Assert.Equal(LicenceReminder.None, licensing.ReminderFor(new LicenceState(), 61));
    }

    [Fact]
    public void PermitsNeedALicence()
    {
        var state = new SaveState();
        state.Vehicles.Add(new VehicleState { VehicleId = "v1" });

        Assert.Equal(PermitProblem.NoLicence, Licensing.CanBuyPermit(state, "v1", 1));
        Assert.Equal(PermitProblem.UnknownVehicle, Licensing.CanBuyPermit(state, "nope", 1));
    }

    [Fact]
    public void OpeningNeedsLicencePermitAndCrew()
    {
        var state = Licensed(vehicles: 1);

        Assert.Equal(OpenProblem.NoPermit, Licensing.CanOpen(state, "v1", 1, playerAtWindow: true, hires: 0));
        Licensing.BuyPermit(state, "v1", 1);
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
        Licensing.BuyPermit(state, "v1", 1);
        state.Vehicles[0].Transit = new TransitState { ToSpotKey = "a", DepartsAt = 0, ArrivesAt = 10 };

        Assert.Equal(OpenProblem.InTransit, Licensing.CanOpen(state, "v1", 1, playerAtWindow: false, hires: 1));
    }

    [Theory]
    [InlineData("cart", true, 3, 1)]
    [InlineData("truck", true, 1, 2)]
    [InlineData("truck", true, 5, 2)]
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
        Licensing.BuyPermit(state, "v1", 3);

        var loaded = codec.Load(codec.Serialize(state)).State!;

        Assert.Equal(3, loaded.Licence.FirstLicensedDay);
        Assert.True(loaded.Vehicles[0].HasPermit);
        Assert.Equal("cart", loaded.Vehicles[0].VehicleType);
    }
}
