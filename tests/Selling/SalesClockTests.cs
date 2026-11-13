using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class SalesClockTests
{
    [Fact]
    public void AWindowOpenedOnAFractionalMinuteSellsExactlyUpToMidnightAndTheDayIsPaidThen()
    {
        var state = new SaveState();
        var midnight = HireSchedules.Absolute(29, 0);
        var clock = SalesClock.Start(HireSchedules.Absolute(28, 20 * 60) + 0.5);
        var steps = 0;
        while (SalesClock.NextStep(clock, midnight) is { } end)
        {
            var elapsed = SalesClock.Elapsed(clock, end);
            Assert.True(elapsed >= 1);
            var soldAt = clock + elapsed - 1 + 0.5;
            var day = GameDays.SaleDay(soldAt, 29);
            StreetSales.Record(state, "truck", day, 100, 1);
            CrewTakings.Accrue(state, "truck", day, 100);
            clock += elapsed;
            steps++;
        }
        Assert.Equal(midnight, clock);
        Assert.Equal(4 * 60, steps);
        var due = CrewTakings.Due(state, 29, _ => clock);
        Assert.Equal((28, 4 * 60 * 100L), (due.Single().Day, due.Single().Cents));
        Assert.Equal(4 * 60 * 100L, SalesLedger.SumCents(state, 1, 28));
        Assert.Equal(0, SalesLedger.SumCents(state, 29, 56));
        Assert.Null(SalesClock.NextStep(clock, midnight));
        Assert.Equal(29, GameDays.SaleDay(SalesClock.NextStep(clock, midnight + 1)!.Value - 0.5, 29));
    }

    [Fact]
    public void AClockLeftOnTheFractionWouldStopShortOfMidnight()
    {
        var midnight = HireSchedules.Absolute(29, 0);
        var clock = HireSchedules.Absolute(28, 20 * 60) + 0.5;
        while (SalesClock.NextStep(clock, midnight) is { } end) clock += SalesClock.Elapsed(clock, end);
        Assert.Equal(midnight - 0.5, clock);
        var state = new SaveState();
        CrewTakings.Accrue(state, "truck", 28, 100);
        Assert.Empty(CrewTakings.Due(state, 29, _ => clock));
    }

    [Fact]
    public void StepsNeverCrossAnHour()
    {
        Assert.Equal(60, SalesClock.NextStep(59, 200, 5));
        Assert.Equal(66, SalesClock.NextStep(61, 200, 5));
        Assert.Null(SalesClock.NextStep(10, 10.9));
        Assert.Equal(10, SalesClock.Start(10.99));
    }
}
