using System;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class SalesLedgerTests
{
    [Fact]
    public void CanRecordRejectsNegativeRevenueWithoutChangingState()
    {
        var state = new SaveState();

        Assert.False(SalesLedger.CanRecord(state, 1, -1));
        Assert.Empty(state.DailySales);
    }

    [Fact]
    public void CanRecordRejectsPerDayOverflowWithoutChangingState()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);

        Assert.False(SalesLedger.CanRecord(state, 1, 1));
        Assert.Single(state.DailySales);
        Assert.Equal(long.MaxValue, state.DailySales[0].RevenueCents);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void CanRecordRejectsTotalOverflowEvenWhenTheDayHasRoom(int day)
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue - 1);
        SalesLedger.Record(state, 2, 1);

        Assert.False(SalesLedger.CanRecord(state, day, 1));
        Assert.Equal(2, state.DailySales.Count);
        Assert.Equal(long.MaxValue, SalesLedger.SumCents(state, 1, 3));
    }

    [Fact]
    public void CanRecordRejectsAnAlreadyOverflowingTotal()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);
        SalesLedger.Record(state, 2, 1);

        Assert.False(SalesLedger.CanRecord(state, 3, 0));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    public void CanRecordAllowsRevenueUpToTheTotalLimitAndRecordSucceeds(int day, long revenueCents)
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue - 3);
        SalesLedger.Record(state, 2, 1);

        Assert.True(SalesLedger.CanRecord(state, day, revenueCents));
        Assert.Equal(long.MaxValue - 2, SalesLedger.SumCents(state, 1, 3));
        SalesLedger.Record(state, day, revenueCents);

        Assert.Equal(long.MaxValue - 2 + revenueCents, SalesLedger.SumCents(state, 1, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(long.MaxValue)]
    public void CanRecordAllowsRevenueOnAnEmptyLedger(long revenueCents)
    {
        var state = new SaveState();

        Assert.True(SalesLedger.CanRecord(state, 1, revenueCents));
        Assert.Empty(state.DailySales);
        SalesLedger.Record(state, 1, revenueCents);

        Assert.Equal(revenueCents, SalesLedger.SumCents(state, 1, 1));
    }

    [Fact]
    public void CanRecordRejectsNullStateWithoutThrowing()
    {
        Assert.False(SalesLedger.CanRecord(null!, 1, 1));
    }

    [Fact]
    public void RecordAddsToTheSameDay()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 5, 1000);
        SalesLedger.Record(state, 5, 250);
        SalesLedger.Record(state, 6, 99);

        Assert.Equal(2, state.DailySales.Count);
        Assert.Equal(1250, SalesLedger.SumCents(state, 5, 5));
    }

    [Fact]
    public void SumIsInclusiveAndIgnoresOtherDays()
    {
        var state = new SaveState();
        for (var day = 1; day <= 10; day++) SalesLedger.Record(state, day, 100);

        Assert.Equal(300, SalesLedger.SumCents(state, 3, 5));
        Assert.Equal(0, SalesLedger.SumCents(state, 11, 20));
        Assert.Equal(0, SalesLedger.SumCents(null, 1, 10));
    }

    [Fact]
    public void NegativeRevenueIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesLedger.Record(new SaveState(), 1, -1));
    }

    [Fact]
    public void ZeroRevenueCreatesNoEntry()
    {
        var state = new SaveState();

        SalesLedger.Record(state, 1, 0);

        Assert.Empty(state.DailySales);
        SalesLedger.Record(state, 2, long.MaxValue);
        SalesLedger.Record(state, 2, 0);
        SalesLedger.Record(state, 3, 0);
        Assert.Single(state.DailySales);
        Assert.Equal(long.MaxValue, state.DailySales[0].RevenueCents);
    }

    [Fact]
    public void RecordOverflowThrowsWithoutChangingRevenue()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);

        Assert.Throws<OverflowException>(() => SalesLedger.Record(state, 1, 1));

        Assert.Single(state.DailySales);
        Assert.Equal(long.MaxValue, state.DailySales[0].RevenueCents);
    }

    [Fact]
    public void SumOverflowThrows()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);
        SalesLedger.Record(state, 2, 1);

        Assert.Equal(long.MaxValue, SalesLedger.SumCents(state, 1, 1));
        Assert.Throws<OverflowException>(() => SalesLedger.SumCents(state, 1, 2));
    }

    [Fact]
    public void LedgerSurvivesARoundTrip()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        SalesLedger.Record(state, 42, 123456);

        var loaded = codec.Load(codec.Serialize(state)).State!;

        Assert.Equal(123456, SalesLedger.SumCents(loaded, 42, 42));
    }
}
