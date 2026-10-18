using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Soak;

internal sealed partial class SoakWorld
{
    internal void CheckMidnightLagRegression()
    {
        // A real service completion on each side of the payout attempt, both sold
        // before midnight on day 28 (a tax-period end), with the second simulated late.
        var v = state.Vehicles[0];
        v.Schedule = new HireSchedule { Stops = new()
        {
            new() { Day = System.DayOfWeek.Sunday, SpotKey = "night", OpenAt = 1380, CloseAt = 1560 }
        } };
        FoodTrucks.Core.Tests.Fleet.WeekTestData.AllDay(v);
        now = HireSchedules.Absolute(28, 1424);
        Open(v, 0, now);
        void ReadyCustomer(long id)
        {
            v.Service!.NextCustomerId = id + 1;
            v.Service.Window.Add(new ServiceCustomer { Id = id, Item = "ba:itemname_hotdog",
                Category = "fastfood", Patience = 20, MinutesLeft = 0.5,
                ArrivedAtMinute = v.Service.Minute });
        }
        ReadyCustomer(1);
        RunVehicle(v, now + 1);
        v = state.Vehicles[0];
        Require(takingsOracle[(v.VehicleId, 28)].Sales > 0, "regression needs a taking before midnight");
        ReadyCustomer(v.Service!.NextCustomerId);
        var before = takingsOracle[(v.VehicleId, 28)].Sales;
        DeferLastMinutes(v.VehicleId, HireSchedules.Absolute(28, 1425), HireSchedules.Absolute(29, 0));
        now = HireSchedules.Absolute(29, 0);
        PayTakings(false);
        Require(heldAtMidnight == 1 && book.All(e => e.Kind != "sales payout"), "first midnight paid incomplete sales");
        SaveLoad(); // The incomplete taking and live service survive loading before catch-up.
        CatchUpDeferred();
        Require(lateCrewSales > 0 && takingsOracle[(v.VehicleId, 28)].Sales > before,
            "regression did not actually sell the last hour after midnight");
        CheckLedger(28);
        Require(SalesLedger.SumCents(state, 29, 56) == 0, "late sale leaked into the next tax period");
        now = HireSchedules.Absolute(30, 0);
        PayTakings(false);
        PayTakings(false); // Retry must not split or repeat the day's payment.
        var payout = book.Single(e => e.Kind == "sales payout");
        Require(payout.Day == 28 && payout.Cents == takingsOracle[(v.VehicleId, 28)].Cents &&
            delayedPayouts == 1 && state.CrewTakings.Count == 0, "held day was not paid once in full on its own day");
    }
}

public class MidnightLagTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public void Seed0LastHourWaitsAcrossMidnightThenPaysOnceAfterCatchUp() =>
        new SoakWorld(0).CheckMidnightLagRegression();
}
